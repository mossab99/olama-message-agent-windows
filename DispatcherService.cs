using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OlamaMessageAgent
{
    // ── Dispatcher state machine ─────────────────────────────────────────────
    public enum DispatcherState
    {
        Disabled,   // Operator has not enabled the dispatcher
        Idle,       // Enabled; waiting for next poll window
        Polling,    // Currently executing a reserve request
        Reserved,   // At least one job was reserved this cycle (dry-run: logged only)
        DryRun,     // Dry-run cycle completed; no SMS was sent
        Error       // Last poll ended in an unrecoverable error
    }

    /// <summary>Sequential SMS worker with durable attempt outcomes and acknowledgement recovery.</summary>
    public class DispatcherService
    {
        // ── Dependencies (injected at construction) ──────────────────────────
        private readonly AgentSettings _settings;

        // ── HTTP client (shared with WordPressAgentClient pattern) ───────────
        private static readonly HttpClient HttpClient;
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        static DispatcherService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        // ── Public state ─────────────────────────────────────────────────────

        /// <summary>Whether the operator has enabled the dispatcher.</summary>
        public bool IsEnabled { get; private set; } = false;

        /// <summary>Current state for tray menu display.</summary>
        public DispatcherState CurrentState { get; private set; } = DispatcherState.Disabled;

        public int SuggestedIdleDelayMilliseconds { get; private set; } = 10000;

        public DateTime? LastPollAt { get; private set; } = null;
        public string LastResultMessage { get; private set; } = null;
        public string LastErrorMessage { get; private set; } = null;

        /// <summary>
        /// When true and a job is reserved, the dispatcher posts a controlled
        /// 'failed/DRY_RUN_ONLY' result back to WordPress instead of leaving the job
        /// in 'reserved' state indefinitely.
        /// Default: false — reserve and log only.
        /// </summary>
        public bool ReportDryRunFailures { get; set; } = false;

        // ── Loaded config (null until first successful LoadConfigAsync call) ──
        private DispatcherConfig _config = null;
        private PendingResult _pendingResult = null;
        private bool _pendingResultPersistenceError = false;

        public sealed class PendingResult
        {
            public int QueueId { get; set; }
            public string AttemptId { get; set; }
            public string Status { get; set; }
            public int? KdeExitCode { get; set; }
            public string Stdout { get; set; }
            public string Stderr { get; set; }
        }

        /// <summary>Loaded poll interval from server configuration. Defaults to 10s.</summary>
        public int PollIntervalSeconds
        {
            get { return _config != null ? _config.PollIntervalSeconds : 10; }
        }

        // ── Constructor ──────────────────────────────────────────────────────
        public DispatcherService(AgentSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException("settings");
            this.ReportDryRunFailures = _settings.ReportDryRunFailures;
            _pendingResult = LoadPendingResult();
            if (_pendingResult != null)
            {
                Logger.Warn($"Recovered an unacknowledged result for queue_id={_pendingResult.QueueId} from disk.");
            }
        }

        public int NextTickDelayMilliseconds(long cycleMilliseconds, DispatcherResult result)
        {
            if (result == null || !result.Success) return Math.Max(10000, PollIntervalSeconds * 1000);
            if (result.JobsReserved.Count == 0) return Math.Max(250, SuggestedIdleDelayMilliseconds);
            return (int)Math.Max(250L, PollIntervalSeconds * 1000L - cycleMilliseconds);
        }

        // ── Operator controls ────────────────────────────────────────────────

        /// <summary>
        /// Operator enables the dispatcher. Validates prerequisites before activating.
        /// Returns a human-readable string explaining why it was rejected, or null on success.
        /// </summary>
        public string Enable()
        {
            string reason = _pendingResult != null ? null : CheckPrerequisites();
            if (reason != null)
            {
                Logger.Warn($"Dispatcher enable rejected: {reason}");
                return reason;
            }

            IsEnabled = true;
            CurrentState = DispatcherState.Idle;
            Logger.Info("Dispatcher enabled by operator.");
            return null;
        }

        /// <summary>Operator disables the dispatcher. Safe to call at any time.</summary>
        public void Disable()
        {
            IsEnabled = false;
            CurrentState = DispatcherState.Disabled;
            Logger.Info("Dispatcher disabled by operator.");
        }

        // ── Main poll method ─────────────────────────────────────────────────

        /// <summary>
        /// Execute one full poll cycle:
        ///   1. Re-validate prerequisites.
        ///   2. Load/refresh config if needed.
        ///   3. Call reserve endpoint.
        ///   4. Log reserved jobs (dry-run: optionally post failure result).
        /// This must be called only while IsEnabled == true.
        /// </summary>
        public async Task<DispatcherResult> PollOnceAsync()
        {
            if (!IsEnabled)
            {
                return DispatcherResult.Fail(0, "Dispatcher is disabled.");
            }

            if (_pendingResult == null && _pendingResultPersistenceError)
            {
                CurrentState = DispatcherState.Error;
                return DispatcherResult.Fail(0, "Pending result recovery failed. Inspect or remove pending-dispatch-result.json before sending more SMS messages.");
            }

            // Result acknowledgement only needs WordPress connectivity. Retry it
            // before KDE prerequisite checks so a disconnected phone cannot strand
            // the outcome of a send that already happened.
            if (_pendingResult != null)
            {
                Logger.Warn($"Retrying unacknowledged result for queue_id={_pendingResult.QueueId} before reserving another job.");
                bool acknowledged = await PostResultAsync(
                    _pendingResult.QueueId,
                    _pendingResult.Status,
                    _pendingResult.KdeExitCode,
                    _pendingResult.Stdout,
                    _pendingResult.Stderr, _pendingResult.AttemptId);
                if (!acknowledged)
                {
                    CurrentState = DispatcherState.Error;
                    return DispatcherResult.Fail(0, $"Result for job {_pendingResult.QueueId} is still awaiting acknowledgement.");
                }
                ClearPendingResult();
            }

            SuggestedIdleDelayMilliseconds = PollIntervalSeconds * 1000;
            LastPollAt = DateTime.UtcNow;
            int waitSeconds = SmsRateLimiter.WaitSeconds(_settings.KdeDeviceId);
            if (waitSeconds > 10)
            {
                SuggestedIdleDelayMilliseconds = waitSeconds * 1000;
                CurrentState = DispatcherState.Idle;
                LastErrorMessage = null;
                LastResultMessage = $"Rate limit: next SMS in approximately {waitSeconds} seconds.";
                return DispatcherResult.Ok(LastResultMessage);
            }

            // Re-validate prerequisites on every poll tick
            string prereqFail = await Task.Run(() => CheckPrerequisites());
            if (prereqFail != null)
            {
                CurrentState = DispatcherState.Error;
                LastErrorMessage = "Prerequisite failed: " + prereqFail;
                LastResultMessage = null;
                Logger.Warn($"Dispatcher poll skipped — prerequisite check failed: {prereqFail}");
                return DispatcherResult.Fail(0, prereqFail);
            }

            // Refresh config if not yet loaded or older than 10 minutes
            if (_config == null || (DateTime.UtcNow - _config.LoadedAt).TotalMinutes > 10)
            {
                var cfgResult = await LoadConfigAsync();
                if (!cfgResult.Success)
                {
                    CurrentState = DispatcherState.Error;
                    LastErrorMessage = "Config load failed: " + cfgResult.Message;
                    LastResultMessage = null;
                    return cfgResult;
                }
            }

            // Execute the reserve poll
            Logger.Info("Dispatcher poll started.");
            CurrentState = DispatcherState.Polling;
            LastPollAt = DateTime.UtcNow;

            var result = await ReserveJobsAsync();

            if (!result.Success)
            {
                CurrentState = DispatcherState.Error;
                LastErrorMessage = "Reserve poll failed: " + result.Message;
                LastResultMessage = null;
                Logger.Warn($"Dispatcher poll failed: {result.Message}");
                return result;
            }

            if (result.JobsReserved.Count == 0)
            {
                CurrentState = DispatcherState.Idle;
                LastResultMessage = "Poll succeeded: No jobs in queue.";
                LastErrorMessage = null;
                Logger.Info("Dispatcher poll: no jobs returned.");
                return result;
            }

            // Log safe metadata only; message bodies remain in memory.
            CurrentState = DispatcherState.Reserved;
            LastResultMessage = $"Reserved {result.JobsReserved.Count} job(s).";
            LastErrorMessage = null;

            foreach (var job in result.JobsReserved)
            {
                Logger.Info(
                    $"Reserved queue_id={job.QueueId} campaign_id={job.CampaignId} " +
                    $"phone={job.MaskedPhone} msg_length={job.MessageLength} " +
                    $"expires_at={job.ExpiresAt:yyyy-MM-dd HH:mm:ss}Z"
                );
                // DO NOT log job.MessageBody — it may contain the payment URL
                // DO NOT log job.PhoneE164 — use job.MaskedPhone only
            }

            if (ReportDryRunFailures)
            {
                // Controlled dry-run path: post a failure result so WordPress reclaims
                // the reservation cleanly, rather than waiting for TTL expiry.
                foreach (var job in result.JobsReserved)
                {
                    Logger.Info($"Dry-run mode: reporting controlled failure for queue_id={job.QueueId}.");
                    await PostDryRunResultAsync(job);
                }
                CurrentState = DispatcherState.DryRun;
                LastResultMessage = "Dry-run cycle completed. No SMS sent.";
                LastErrorMessage = null;
                Logger.Info("Dry-run mode: SMS not sent. Controlled failure result posted.");
            }
            else
            {
                // Real sending path: one durable attempt per reservation.
                CurrentState = DispatcherState.Reserved;
                var job = result.JobsReserved[0]; // Process exactly 1 job per cycle for safety

                try
                {
                    if (string.IsNullOrWhiteSpace(job.AttemptId) || job.ExpiresAt <= DateTime.UtcNow)
                        throw new InvalidOperationException("SMS reservation is missing an attempt ID or has expired.");
                    // Persist intent BEFORE invoking an external sender. After a crash,
                    // recover this as unknown and report it; never invoke KDE again.
                    if (!SetPendingResult(new PendingResult {
                        QueueId = job.QueueId, AttemptId = job.AttemptId, Status = "unknown",
                        KdeExitCode = -3, Stdout = "", Stderr = "Agent stopped before recording the SMS outcome. Check phone history."
                    }))
                        return DispatcherResult.Fail(0, "Cannot persist SMS intent. No SMS was sent.");
                    Logger.Info($"Invoking KDE Connect CLI to send SMS for queue_id={job.QueueId}...");
                    var sendResult = await Task.Run(() => KdeCliHelper.SendSms(_settings.KdeCliPath, _settings.KdeDeviceId, job.PhoneE164, job.MessageBody));

                    string status = sendResult.Success ? "sent_by_kde" : (sendResult.OutcomeUnknown ? "unknown" : "failed");
                    Logger.Info($"KDE Send completed. Success={sendResult.Success} ExitCode={sendResult.ExitCode} Status={status}");

                    SetPendingResult(new PendingResult
                    {
                        QueueId = job.QueueId,
                        AttemptId = job.AttemptId,
                        Status = status,
                        KdeExitCode = sendResult.ExitCode,
                        Stdout = sendResult.Stdout,
                        Stderr = sendResult.Stderr
                    });
                    bool acknowledged = await PostResultAsync(job.QueueId, status, sendResult.ExitCode, sendResult.Stdout, sendResult.Stderr, job.AttemptId);
                    if (acknowledged)
                    {
                        ClearPendingResult();
                        CurrentState = sendResult.OutcomeUnknown ? DispatcherState.Error : DispatcherState.Idle;
                    }
                    else
                    {
                        CurrentState = DispatcherState.Error;
                        return DispatcherResult.Fail(0, $"SMS execution completed, but WordPress did not acknowledge job {job.QueueId}.");
                    }

                    if (LastResultMessage == null && LastErrorMessage == null)
                    {
                        if (sendResult.Success)
                        {
                            LastResultMessage = $"Job {job.QueueId} submitted via KDE Connect.";
                        }
                        else
                        {
                            LastErrorMessage = $"Job {job.QueueId} failed. Exit code: {sendResult.ExitCode}.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unhandled exception during SMS dispatch for queue_id={job.QueueId}.", ex);
                    try
                    {
                        SetPendingResult(new PendingResult
                        {
                            QueueId = job.QueueId,
                            AttemptId = job.AttemptId,
                            Status = "unknown",
                            KdeExitCode = -1,
                            Stdout = "",
                            Stderr = "Exception: " + ex.Message
                        });
                        if (await PostResultAsync(job.QueueId, "unknown", -3, "", "Exception: " + ex.Message, job.AttemptId))
                        {
                            ClearPendingResult();
                        }
                    }
                    catch
                    {
                        // Ignore secondary errors
                    }
                    LastErrorMessage = $"Job {job.QueueId} exception: {ex.Message}";
                    LastResultMessage = null;
                }
            }

            return result;
        }

        // ── Config loader ────────────────────────────────────────────────────

        /// <summary>
        /// Fetch dispatcher configuration from WordPress.
        /// GET /wp-json/olama-messages/v1/agent/jobs/config
        /// </summary>
        public async Task<DispatcherResult> LoadConfigAsync()
        {
            string baseUrl = WordPressAgentClient.NormalizeBaseUrl(_settings.SiteUrl);
            string endpoint = baseUrl + "/wp-json/olama-messages/v1/agent/jobs/config";

            Logger.Info($"Loading dispatcher config from WordPress...");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
                {
                    AddAuthHeaders(request);

                    using (var response = await HttpClient.SendAsync(request))
                    {
                        int code = (int)response.StatusCode;
                        string body = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.Warn($"Dispatcher config load rejected: HTTP {code}. Body: {body}");
                            LastErrorMessage = $"Config load HTTP error: {code}";
                            LastResultMessage = null;
                            return DispatcherResult.Fail(code, $"Config endpoint returned HTTP {code}.");
                        }

                        // Require the durable dispatch protocol before reserving a job.
                        _config = ParseConfig(body);
                        LastResultMessage = "Config loaded successfully.";
                        LastErrorMessage = null;
                        Logger.Info(
                            $"Dispatcher config loaded. poll_interval={_config.PollIntervalSeconds}s " +
                            $"max_jobs={_config.MaxJobsPerPoll} " +
                            $"min_between_sms={_config.MinSecondsBetweenSms}s " +
                            $"max_per_hour={_config.MaxSmsPerHour} " +
                            $"reservation_ttl={_config.ReservationTtlSeconds}s"
                        );
                        return DispatcherResult.Ok("Config loaded.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Dispatcher config load failed.", ex);
                // Fail closed: configuration and protocol must be verified before sending.
                _config = null;
                LastErrorMessage = $"Config load exception: {ex.Message}";
                LastResultMessage = null;
                return DispatcherResult.Fail(0, $"Config load failed: {ex.Message}. Sending is stopped until configuration is verified.");
            }
        }

        // ── Reserve poller ───────────────────────────────────────────────────

        /// <summary>
        /// POST /wp-json/olama-messages/v1/agent/jobs/reserve
        /// Returns a DispatcherResult with reserved jobs (may be empty).
        /// </summary>
        public async Task<DispatcherResult> ReserveJobsAsync()
        {
            string baseUrl = WordPressAgentClient.NormalizeBaseUrl(_settings.SiteUrl);
            string endpoint = baseUrl + "/wp-json/olama-messages/v1/agent/jobs/reserve";

            // Strictly force maxJobs = 1 for Run 4D single-SMS safety boundary
            int maxJobs = 1;

            var payload = new Dictionary<string, object>
            {
                { "max_jobs", maxJobs },
                { "dispatch_protocol_version", 2 },
                {
                    "capabilities", new Dictionary<string, object>
                    {
                        { "sms", true },
                        { "kde_cli_found", KdeCliHelper.DetectCli(_settings.KdeCliPath) },
                        // We report device reachability from the last known cached state
                        // (same value used in heartbeat — no new discovery call here)
                        { "kde_device_reachable", IsKdeDeviceReachableFromSettings() }
                    }
                }
            };

            string jsonPayload = Serializer.Serialize(payload);

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    AddAuthHeaders(request);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    using (var response = await HttpClient.SendAsync(request))
                    {
                        int code = (int)response.StatusCode;
                        string body = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.Warn($"Reserve endpoint rejected: HTTP {code}.");
                            return DispatcherResult.Fail(code, $"Reserve endpoint returned HTTP {code}.");
                        }

                        var envelope = Serializer.Deserialize<Dictionary<string, object>>(body);
                        if (envelope != null && envelope.TryGetValue("poll_after_seconds", out object retryAfter))
                            SuggestedIdleDelayMilliseconds = Math.Max(250, Convert.ToInt32(retryAfter) * 1000);
                        var jobs = ParseReservedJobs(body);
                        return DispatcherResult.Ok($"Poll succeeded. {jobs.Count} job(s) reserved.", jobs);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Reserve endpoint call failed.", ex);
                return DispatcherResult.Fail(0, $"Reserve call failed: {ex.Message}");
            }
        }

        // ── Dry-run result reporter ──────────────────────────────────────────

        /// <summary>
        /// Post a controlled failure result to WordPress so it can reclaim the
        /// reservation and put it back in the retry queue, rather than waiting
        /// for TTL expiry.
        /// Only called when ReportDryRunFailures == true.
        /// </summary>
        private async Task PostDryRunResultAsync(DispatchJob job)
        {
            string baseUrl = WordPressAgentClient.NormalizeBaseUrl(_settings.SiteUrl);
            string endpoint = baseUrl + "/wp-json/olama-messages/v1/agent/jobs/result";

            var payload = new Dictionary<string, object>
            {
                { "queue_id",      job.QueueId },
                { "attempt_id",    job.AttemptId },
                { "status",        "failed" },
                { "kde_exit_code", -1 },
                { "stdout",        "DRY_RUN_ONLY_NO_SMS_SENT" },
                { "stderr",        string.Empty },
                { "sent_at",       null }
            };

            string jsonPayload = Serializer.Serialize(payload);

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    AddAuthHeaders(request);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    using (var response = await HttpClient.SendAsync(request))
                    {
                        int code = (int)response.StatusCode;
                        if (response.IsSuccessStatusCode)
                        {
                            Logger.Info($"Dry-run result for queue_id={job.QueueId} accepted by WordPress (HTTP {code}).");
                            LastResultMessage = $"Dry-run result for job {job.QueueId} posted successfully.";
                            LastErrorMessage = null;
                        }
                        else
                        {
                            Logger.Warn($"Dry-run result for queue_id={job.QueueId} rejected: HTTP {code}.");
                            LastErrorMessage = $"Dry-run result post rejected: HTTP {code}";
                            LastResultMessage = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to post dry-run result for queue_id={job.QueueId}.", ex);
                LastErrorMessage = $"Dry-run result post exception: {ex.Message}";
                LastResultMessage = null;
            }
        }

        /// <summary>
        /// Post the real execution result of an SMS send attempt to WordPress.
        /// </summary>
        private async Task<bool> PostResultAsync(int queueId, string status, int? kdeExitCode, string stdout, string stderr, string attemptId = null)
        {
            string baseUrl = WordPressAgentClient.NormalizeBaseUrl(_settings.SiteUrl);
            string endpoint = baseUrl + "/wp-json/olama-messages/v1/agent/jobs/result";

            var payload = new Dictionary<string, object>
            {
                { "queue_id",      queueId },
                { "attempt_id",    attemptId },
                { "status",        status }, // "sent_by_kde" or "failed"
                { "kde_exit_code", kdeExitCode },
                { "stdout",        stdout },
                { "stderr",        stderr },
                { "sent_at",       status == "sent_by_kde" ? DateTime.UtcNow.ToString("o") : null }
            };

            string jsonPayload = Serializer.Serialize(payload);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        AddAuthHeaders(request);
                        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        using (var response = await HttpClient.SendAsync(request))
                        {
                            int code = (int)response.StatusCode;
                            if (response.IsSuccessStatusCode)
                            {
                                Logger.Info($"Sent result for queue_id={queueId} accepted by WordPress (HTTP {code}).");
                                if (status == "sent_by_kde")
                                {
                                    LastResultMessage = $"Job {queueId} submitted via KDE and result recorded.";
                                    LastErrorMessage = null;
                                }
                                else
                                {
                                    LastErrorMessage = status == "unknown" ? $"Job {queueId}: outcome unknown; review the phone history in WordPress before retrying." : $"Job {queueId} was not submitted; result recorded.";
                                    LastResultMessage = null;
                                }
                                return true;
                            }
                            else
                            {
                                Logger.Warn($"Sent result for queue_id={queueId} rejected by WordPress: HTTP {code} (attempt {attempt}/3).");
                                LastErrorMessage = $"Result post rejected by WordPress: HTTP {code}";
                                LastResultMessage = null;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to post sent result for queue_id={queueId} (attempt {attempt}/3).", ex);
                    LastErrorMessage = $"Result post exception: {GetExceptionSummary(ex)}";
                    LastResultMessage = null;
                }

                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                }
            }

            return false;
        }

        // ── Prerequisite validator ───────────────────────────────────────────

        /// <summary>
        /// Check all pre-conditions required before the dispatcher may poll.
        /// Returns null if all checks pass, or a human-readable reason string if not.
        /// </summary>
        private string CheckPrerequisites()
        {
            if (string.IsNullOrWhiteSpace(_settings.SiteUrl))
                return "WordPress site URL is not configured.";

            if (string.IsNullOrWhiteSpace(_settings.AgentUuid))
                return "Agent UUID is not configured.";

            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
                return "Agent API key is not configured or could not be decrypted.";

            if (!KdeCliHelper.DetectCli(_settings.KdeCliPath))
                return $"KDE Connect CLI not found at: {_settings.KdeCliPath}";

            if (string.IsNullOrWhiteSpace(_settings.KdeDeviceId))
                return "No KDE target device ID configured. Open Settings and select a device.";

            if (!KdeCliHelper.IsDeviceReachable(_settings.KdeCliPath, _settings.KdeDeviceId))
                return "Target KDE Connect device is disconnected or unreachable.";

            return null; // All checks passed
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void AddAuthHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("X-Olama-Agent-UUID", _settings.AgentUuid);
            request.Headers.Add("X-Olama-Agent-Key",  _settings.ApiKey);
        }

        private static string GetExceptionSummary(Exception ex)
        {
            if (ex == null) return "Unknown error";
            Exception deepest = ex;
            while (deepest.InnerException != null) deepest = deepest.InnerException;
            return deepest == ex ? ex.Message : ex.Message + " | " + deepest.GetType().Name + ": " + deepest.Message;
        }

        private bool SetPendingResult(PendingResult pendingResult)
        {
            _pendingResult = pendingResult;
            try
            {
                string path = SettingsManager.GetDataFilePath("pending-dispatch-result.json");
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, Serializer.Serialize(pendingResult), Encoding.UTF8);
                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
                _pendingResultPersistenceError = false;
                return true;
            }
            catch (Exception ex)
            {
                _pendingResultPersistenceError = true;
                Logger.Error("Failed to persist the pending dispatcher result.", ex);
                return false;
            }
        }

        private PendingResult LoadPendingResult()
        {
            try
            {
                string path = SettingsManager.GetDataFilePath("pending-dispatch-result.json");
                if (!File.Exists(path)) return null;
                var pending = Serializer.Deserialize<PendingResult>(File.ReadAllText(path, Encoding.UTF8));
                if (pending == null || pending.QueueId <= 0 ||
                    (pending.Status != "sent_by_kde" && pending.Status != "failed" && pending.Status != "unknown"))
                    throw new InvalidDataException("Invalid pending SMS result.");
                return pending;
            }
            catch (Exception ex)
            {
                _pendingResultPersistenceError = true;
                Logger.Error("Failed to load the pending dispatcher result.", ex);
                return null;
            }
        }

        private void ClearPendingResult()
        {
            try
            {
                string path = SettingsManager.GetDataFilePath("pending-dispatch-result.json");
                if (File.Exists(path)) File.Delete(path);
                _pendingResult = null;
                _pendingResultPersistenceError = false;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to remove the acknowledged dispatcher result file.", ex);
                throw;
            }
        }

        /// <summary>
        /// The dispatcher reports KDE reachability from the last cached device
            /// state stored by the heartbeat discovery.
        /// </summary>
        private bool IsKdeDeviceReachableFromSettings()
        {
            // Access the shared tray context to read cached diagnostics state
            // ApplicationContextInstance.Current is set at startup in TrayApplicationContext.
            var ctx = ApplicationContextInstance.Current;
            if (ctx == null)
            {
                return KdeCliHelper.IsDeviceReachable(_settings.KdeCliPath, _settings.KdeDeviceId);
            }
            return ctx.LastKnownDeviceReachable;
        }

        private DispatcherConfig ParseConfig(string json)
        {
            try
            {
                var dict = Serializer.Deserialize<Dictionary<string, object>>(json);
                var cfg = new DispatcherConfig();
                if (dict == null) throw new InvalidDataException("Missing dispatcher configuration.");

                // The WordPress response wraps data inside a "data" key
                Dictionary<string, object> data = dict;
                if (dict.ContainsKey("data") && dict["data"] is Dictionary<string, object> inner)
                    data = inner;

                if (!data.TryGetValue("dispatch_protocol_version", out object version) || Convert.ToInt32(version) != 2)
                    throw new InvalidDataException("Update the WordPress plugin to dispatch protocol 2 before sending.");

                if (data.TryGetValue("poll_interval_seconds", out object pis))
                    cfg.PollIntervalSeconds = Math.Max(10, Convert.ToInt32(pis));

                if (data.TryGetValue("max_jobs_per_poll", out object mjp))
                    cfg.MaxJobsPerPoll = Math.Max(1, Math.Min(3, Convert.ToInt32(mjp)));

                if (data.TryGetValue("min_seconds_between_sms", out object mbs))
                    cfg.MinSecondsBetweenSms = Math.Max(5, Convert.ToInt32(mbs));

                if (data.TryGetValue("max_sms_per_hour", out object mph))
                    cfg.MaxSmsPerHour = Math.Max(1, Convert.ToInt32(mph));

                if (data.TryGetValue("reservation_ttl_seconds", out object rtt))
                    cfg.ReservationTtlSeconds = Math.Max(30, Convert.ToInt32(rtt));

                cfg.LoadedAt = DateTime.UtcNow;
                return cfg;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to verify dispatcher configuration. Sending is stopped.", ex);
                throw;
            }
        }

        private List<DispatchJob> ParseReservedJobs(string json)
        {
            var jobs = new List<DispatchJob>();
            try
            {
                var dict = Serializer.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return jobs;

                // Expected shape: { "status": "ok", "jobs": [ { ... } ] }
                if (!dict.ContainsKey("jobs")) return jobs;

                var jobsArray = dict["jobs"] as System.Collections.ArrayList;
                if (jobsArray == null) return jobs;

                foreach (object item in jobsArray)
                {
                    if (!(item is Dictionary<string, object> j)) continue;

                    var job = new DispatchJob();
                    if (j.TryGetValue("attempt_id", out object aid)) job.AttemptId = aid?.ToString();

                    if (j.TryGetValue("queue_id",     out object qid)) job.QueueId    = Convert.ToInt32(qid);
                    if (j.TryGetValue("campaign_id",  out object cid)) job.CampaignId = Convert.ToInt32(cid);
                    if (j.TryGetValue("phone_e164",   out object ph))  job.PhoneE164  = ph?.ToString() ?? string.Empty;
                    if (j.TryGetValue("message_body", out object mb))  job.MessageBody = mb?.ToString() ?? string.Empty;
                    if (j.TryGetValue("message_length", out object ml)) job.MessageLength = Convert.ToInt32(ml);

                    // expires_at arrives as ISO 8601 UTC string
                    if (j.TryGetValue("expires_at", out object ea) && ea != null)
                    {
                        if (DateTime.TryParse(ea.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime expiry))
                            job.ExpiresAt = expiry;
                    }

                    // Derive MessageLength from body if server did not send it
                    if (job.MessageLength == 0 && !string.IsNullOrEmpty(job.MessageBody))
                        job.MessageLength = job.MessageBody.Length;

                    jobs.Add(job);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to parse reserved jobs response.", ex);
            }
            return jobs;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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

    /// <summary>
    /// Dispatcher skeleton for Phase 4 Run 4C.
    ///
    /// Responsibilities in Run 4C:
    ///   • Load server-side dispatcher config from WordPress.
    ///   • Poll the reserve endpoint on a configurable interval.
    ///   • Log reserved jobs with masked phone and safe metadata.
    ///   • In dry-run mode, optionally report a controlled failure result.
    ///
    /// FORBIDDEN in Run 4C:
    ///   • Calling KdeCliHelper.SendSms()
    ///   • Sending real SMS messages
    ///   • Caching jobs across restarts
    ///   • Enabling dispatcher automatically on startup
    /// </summary>
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

        /// <summary>Loaded poll interval from server configuration. Defaults to 30s.</summary>
        public int PollIntervalSeconds
        {
            get { return _config != null ? _config.PollIntervalSeconds : 30; }
        }

        // ── Constructor ──────────────────────────────────────────────────────
        public DispatcherService(AgentSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException("settings");
            this.ReportDryRunFailures = _settings.ReportDryRunFailures;
        }

        // ── Operator controls ────────────────────────────────────────────────

        /// <summary>
        /// Operator enables the dispatcher. Validates prerequisites before activating.
        /// Returns a human-readable string explaining why it was rejected, or null on success.
        /// </summary>
        public string Enable()
        {
            string reason = CheckPrerequisites();
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

            // Re-validate prerequisites on every poll tick
            string prereqFail = CheckPrerequisites();
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

            // Jobs were reserved — log them (dry-run: never send)
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
                // Real sending path (Run 4D - Single SMS only with Circuit Breaker)
                CurrentState = DispatcherState.Reserved;
                var job = result.JobsReserved[0]; // Process exactly 1 job per cycle for safety

                try
                {
                    Logger.Info($"Invoking KDE Connect CLI to send SMS for queue_id={job.QueueId}...");
                    var sendResult = await Task.Run(() => KdeCliHelper.SendSms(_settings.KdeCliPath, _settings.KdeDeviceId, job.PhoneE164, job.MessageBody));

                    string status = sendResult.Success ? "sent_by_kde" : "failed";
                    Logger.Info($"KDE Send completed. Success={sendResult.Success} ExitCode={sendResult.ExitCode} Status={status}");

                    await PostResultAsync(job.QueueId, status, sendResult.ExitCode, sendResult.Stdout, sendResult.Stderr);

                    if (LastResultMessage == null && LastErrorMessage == null)
                    {
                        if (sendResult.Success)
                        {
                            LastResultMessage = $"Job {job.QueueId} sent successfully via KDE Connect.";
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
                        await PostResultAsync(job.QueueId, "failed", -1, "", "Exception: " + ex.Message);
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

                        // Parse config fields — use defaults for any missing fields
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
                // Fall back to safe defaults so we can still poll
                _config = new DispatcherConfig();
                LastErrorMessage = $"Config load exception: {ex.Message}";
                LastResultMessage = null;
                return DispatcherResult.Fail(0, $"Config load failed: {ex.Message}. Using defaults.");
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
                { "status",        "failed" },
                { "kde_exit_code", null },
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
        private async Task PostResultAsync(int queueId, string status, int? kdeExitCode, string stdout, string stderr)
        {
            string baseUrl = WordPressAgentClient.NormalizeBaseUrl(_settings.SiteUrl);
            string endpoint = baseUrl + "/wp-json/olama-messages/v1/agent/jobs/result";

            var payload = new Dictionary<string, object>
            {
                { "queue_id",      queueId },
                { "status",        status }, // "sent_by_kde" or "failed"
                { "kde_exit_code", kdeExitCode },
                { "stdout",        stdout },
                { "stderr",        stderr },
                { "sent_at",       status == "sent_by_kde" ? DateTime.UtcNow.ToString("o") : null }
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
                            Logger.Info($"Sent result for queue_id={queueId} accepted by WordPress (HTTP {code}).");
                            if (status == "sent_by_kde")
                            {
                                LastResultMessage = $"Job {queueId} sent and result posted successfully.";
                                LastErrorMessage = null;
                            }
                            else
                            {
                                LastErrorMessage = $"Job {queueId} failed and result posted successfully.";
                                LastResultMessage = null;
                            }
                        }
                        else
                        {
                            Logger.Warn($"Sent result for queue_id={queueId} rejected by WordPress: HTTP {code}.");
                            LastErrorMessage = $"Result post rejected by WordPress: HTTP {code}";
                            LastResultMessage = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to post sent result for queue_id={queueId}.", ex);
                LastErrorMessage = $"Result post exception: {ex.Message}";
                LastResultMessage = null;
            }
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
                if (dict == null) return cfg;

                // The WordPress response wraps data inside a "data" key
                Dictionary<string, object> data = dict;
                if (dict.ContainsKey("data") && dict["data"] is Dictionary<string, object> inner)
                    data = inner;

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
                Logger.Error("Failed to parse dispatcher config JSON. Using safe defaults.", ex);
                return new DispatcherConfig();
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

                    if (j.TryGetValue("queue_id",     out object qid)) job.QueueId    = Convert.ToInt32(qid);
                    if (j.TryGetValue("campaign_id",  out object cid)) job.CampaignId = Convert.ToInt32(cid);
                    if (j.TryGetValue("phone_e164",   out object ph))  job.PhoneE164  = ph?.ToString() ?? string.Empty;
                    if (j.TryGetValue("message_body", out object mb))  job.MessageBody = mb?.ToString() ?? string.Empty;
                    if (j.TryGetValue("message_length", out object ml)) job.MessageLength = Convert.ToInt32(ml);

                    // expires_at arrives as ISO 8601 UTC string
                    if (j.TryGetValue("expires_at", out object ea) && ea != null)
                    {
                        if (DateTime.TryParse(ea.ToString(), out DateTime expiry))
                            job.ExpiresAt = expiry.ToUniversalTime();
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

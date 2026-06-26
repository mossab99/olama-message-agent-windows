using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OlamaMessageAgent
{
    public class HeartbeatResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
    }

    public static class WordPressAgentClient
    {
        private static readonly HttpClient HttpClient;
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        static WordPressAgentClient()
        {
            // Ensure TLS 1.2 is active for the process
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Reuse single HttpClient instance for socket optimization
            HttpClient = new HttpClient();
            HttpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public static string NormalizeBaseUrl(string inputUrl)
        {
            if (string.IsNullOrWhiteSpace(inputUrl)) return string.Empty;

            string url = inputUrl.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            try
            {
                var uri = new Uri(url);
                string rootUrl = uri.GetLeftPart(UriPartial.Authority);
                
                // Preserve potential subdirectories (e.g., http://localhost/wordpress/)
                string originalPath = uri.AbsolutePath;
                int wpJsonIndex = originalPath.IndexOf("/wp-json", StringComparison.OrdinalIgnoreCase);
                if (wpJsonIndex > 0)
                {
                    string subDir = originalPath.Substring(0, wpJsonIndex);
                    return rootUrl + subDir.TrimEnd('/');
                }

                // If path has no /wp-json, just use authority + clean path
                return (rootUrl + originalPath).TrimEnd('/');
            }
            catch
            {
                return url.TrimEnd('/');
            }
        }

        public static async Task<HeartbeatResult> SendHeartbeatAsync(AgentSettings settings)
        {
            string baseUrl = NormalizeBaseUrl(settings.SiteUrl);
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(settings.AgentUuid) || string.IsNullOrEmpty(settings.ApiKey))
            {
                return new HeartbeatResult
                {
                    Success = false,
                    Message = "Agent settings are incomplete.",
                    StatusCode = 0
                };
            }

            string endpoint = baseUrl + "/wp-json/olama-messages/v1/agent/heartbeat";

            // Prepare diagnostics payload representing agent state
            var payload = new Dictionary<string, object>
            {
                { "platform", "Windows" },
                { "app_version", "1.0.0" },
                { "machine_name", Environment.MachineName },
                { "windows_user", Environment.UserName },
                { "kde_cli_path", settings.KdeCliPath ?? string.Empty },
                { "kde_cli_found", false },
                { "kde_device_id", settings.KdeDeviceId ?? string.Empty },
                { "kde_device_name", "" },
                { "kde_device_reachable", false }
            };

            string jsonPayload = Serializer.Serialize(payload);

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.Add("X-Olama-Agent-UUID", settings.AgentUuid);
                    request.Headers.Add("X-Olama-Agent-Key", settings.ApiKey);
                    
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    Logger.Info($"Sending heartbeat. Endpoint: {baseUrl}/wp-json/olama-messages/v1/agent/heartbeat (Agent UUID Prefix: {GetUuidPrefix(settings.AgentUuid)})");

                    using (var response = await HttpClient.SendAsync(request))
                    {
                        int code = (int)response.StatusCode;
                        string content = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            Logger.Info($"Heartbeat succeeded. Response code: {code}.");
                            return new HeartbeatResult
                            {
                                Success = true,
                                StatusCode = code,
                                Message = "Heartbeat succeeded."
                            };
                        }
                        else
                        {
                            Logger.Warn($"Heartbeat rejected by server. Response code: {code}. Details: {content}");
                            return new HeartbeatResult
                            {
                                Success = false,
                                StatusCode = code,
                                Message = $"HTTP {(int)response.StatusCode} - {response.ReasonPhrase}",
                                ErrorDetails = content
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Heartbeat connection failed.", ex);
                return new HeartbeatResult
                {
                    Success = false,
                    StatusCode = 0,
                    Message = "Connection failed",
                    ErrorDetails = ex.Message
                };
            }
        }

        private static string GetUuidPrefix(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return string.Empty;
            return uuid.Length > 8 ? uuid.Substring(0, 8) : uuid;
        }
    }
}

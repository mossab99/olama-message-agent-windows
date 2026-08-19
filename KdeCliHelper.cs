using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OlamaMessageAgent
{
    public class KdeDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Reachable { get; set; }
    }

    public class SmsSendResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
    }

    public static class KdeCliHelper
    {
        private const int DiscoveryTimeoutMilliseconds = 10000;
        private const int SendTimeoutMilliseconds = 30000;

        private sealed class CliExecutionResult
        {
            public bool Started { get; set; }
            public bool TimedOut { get; set; }
            public int ExitCode { get; set; } = -1;
            public string Stdout { get; set; } = string.Empty;
            public string Stderr { get; set; } = string.Empty;
        }

        public static bool DetectCli(string cliPath)
        {
            if (string.IsNullOrWhiteSpace(cliPath)) return false;
            try
            {
                return File.Exists(cliPath);
            }
            catch
            {
                return false;
            }
        }

        public static List<KdeDevice> DiscoverDevices(string cliPath)
        {
            var devices = new List<KdeDevice>();

            if (!DetectCli(cliPath))
            {
                Logger.Warn($"KDE Connect CLI not found at path: {cliPath}. Skipping device discovery.");
                return devices;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = "--list-devices",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                var execution = ExecuteProcess(psi, DiscoveryTimeoutMilliseconds, "device discovery");
                if (execution.Started && !execution.TimedOut && execution.ExitCode == 0)
                {
                    string[] lines = execution.Stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string trimmed = line.Trim();
                            
                            // Expected output line format:
                            // "- Galaxy S21: 1234567890 (paired and reachable)"
                            // "- Nexus 5: abcdef (paired)"
                            if (!trimmed.StartsWith("- ")) continue;
                            string content = trimmed.Substring(2);

                            int colonIdx = content.IndexOf(':');
                            if (colonIdx == -1) continue;

                            string name = content.Substring(0, colonIdx).Trim();
                            string remaining = content.Substring(colonIdx + 1).Trim();

                            int spaceIdx = remaining.IndexOf(' ');
                            string id;
                            bool reachable = false;

                            if (spaceIdx == -1)
                            {
                                id = remaining;
                            }
                            else
                            {
                                id = remaining.Substring(0, spaceIdx).Trim();
                                string status = remaining.Substring(spaceIdx + 1).Trim();
                                // Never use a substring match here: "unreachable" contains
                                // "reachable" and would otherwise produce a dangerous false positive.
                                reachable = status.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) == -1 &&
                                            status.IndexOf("paired and reachable", StringComparison.OrdinalIgnoreCase) != -1;
                            }

                        devices.Add(new KdeDevice
                        {
                            Id = id,
                            Name = name,
                            Reachable = reachable
                        });
                    }
                }
                else if (execution.TimedOut)
                {
                    Logger.Warn("KDE device discovery timed out; treating the device as unreachable.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception occurred during KDE device discovery.", ex);
            }

            return devices;
        }

        public static bool IsDeviceReachable(string cliPath, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            foreach (var device in DiscoverDevices(cliPath))
            {
                if (string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                    return device.Reachable;
            }

            return false;
        }

        public static SmsSendResult SendSms(string cliPath, string deviceId, string phone, string message)
        {
            if (!DetectCli(cliPath))
            {
                return new SmsSendResult
                {
                    Success = false,
                    ExitCode = -1,
                    Stdout = string.Empty,
                    Stderr = "KDE Connect CLI executable not found."
                };
            }

            // kdeconnect-cli may exit with code 0 even when the remembered device is
            // currently disconnected. Refuse to invoke --send-sms unless a fresh
            // discovery confirms that this exact target is reachable.
            if (!IsDeviceReachable(cliPath, deviceId))
            {
                return new SmsSendResult
                {
                    Success = false,
                    ExitCode = -2,
                    Stdout = string.Empty,
                    Stderr = "Target KDE Connect device is disconnected or unreachable. SMS was not sent."
                };
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    // Safe command escaping avoiding shell injection
                    Arguments = "--device " + QuoteArg(deviceId) + " " +
                                "--send-sms " + QuoteArg(message) + " " +
                                "--destination " + QuoteArg(phone)
                };

                var execution = ExecuteProcess(psi, SendTimeoutMilliseconds, "SMS send");
                if (!execution.Started)
                {
                    return new SmsSendResult
                    {
                        Success = false,
                        ExitCode = -1,
                        Stdout = string.Empty,
                        Stderr = "Failed to launch KDE CLI process."
                    };
                }

                if (execution.TimedOut)
                {
                    return new SmsSendResult
                    {
                        Success = false,
                        ExitCode = -3,
                        Stdout = Truncate(execution.Stdout),
                        Stderr = "KDE Connect CLI timed out after 30 seconds and was terminated."
                    };
                }

                return new SmsSendResult
                {
                    Success = execution.ExitCode == 0,
                    ExitCode = execution.ExitCode,
                    Stdout = Truncate(execution.Stdout),
                    Stderr = Truncate(execution.Stderr)
                };
            }
            catch (Exception ex)
            {
                Logger.Error("Exception occurred while sending SMS via KDE Connect.", ex);
                return new SmsSendResult
                {
                    Success = false,
                    ExitCode = -1,
                    Stdout = string.Empty,
                    Stderr = ex.Message.Length > 4000 ? ex.Message.Substring(0, 4000) : ex.Message
                };
            }
        }

        private static CliExecutionResult ExecuteProcess(ProcessStartInfo startInfo, int timeoutMilliseconds, string operationName)
        {
            var result = new CliExecutionResult();
            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    return result;
                }

                result.Started = true;
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    result.TimedOut = true;
                    Logger.Error($"KDE CLI {operationName} exceeded {timeoutMilliseconds / 1000} seconds; terminating process.");
                    try
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to terminate timed-out KDE CLI {operationName} process.", ex);
                    }
                }

                // Both streams are drained concurrently to prevent stdout/stderr pipe deadlocks.
                try
                {
                    Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 2000);
                }
                catch (AggregateException ex)
                {
                    Logger.Error($"Failed while reading KDE CLI {operationName} output.", ex.Flatten());
                }

                if (stdoutTask.Status == TaskStatus.RanToCompletion)
                    result.Stdout = stdoutTask.Result ?? string.Empty;
                if (stderrTask.Status == TaskStatus.RanToCompletion)
                    result.Stderr = stderrTask.Result ?? string.Empty;
                if (!result.TimedOut && process.HasExited)
                    result.ExitCode = process.ExitCode;

                return result;
            }
        }

        private static string Truncate(string value)
        {
            value = value ?? string.Empty;
            return value.Length > 4000 ? value.Substring(0, 4000) : value;
        }

        private static string QuoteArg(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            // Double backslashes and escape double quotes
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}

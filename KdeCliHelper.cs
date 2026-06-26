using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string stdout = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        string[] lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
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
                                reachable = status.IndexOf("reachable", StringComparison.OrdinalIgnoreCase) != -1;
                            }

                            devices.Add(new KdeDevice
                            {
                                Id = id,
                                Name = name,
                                Reachable = reachable
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception occurred during KDE device discovery.", ex);
            }

            return devices;
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

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        return new SmsSendResult
                        {
                            Success = false,
                            ExitCode = -1,
                            Stdout = string.Empty,
                            Stderr = "Failed to launch KDE CLI process."
                        };
                    }

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    // Truncate logs to 4000 characters to prevent DB overflow
                    string truncatedStdout = stdout.Length > 4000 ? stdout.Substring(0, 4000) : stdout;
                    string truncatedStderr = stderr.Length > 4000 ? stderr.Substring(0, 4000) : stderr;

                    return new SmsSendResult
                    {
                        Success = (process.ExitCode == 0),
                        ExitCode = process.ExitCode,
                        Stdout = truncatedStdout,
                        Stderr = truncatedStderr
                    };
                }
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

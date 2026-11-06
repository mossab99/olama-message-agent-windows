using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OlamaMessageAgent
{
    // Shared by campaign and manual sends. Records submission intent before KDE
    // and survives restarts; reservations denied here never invoke the sender.
    public static class SmsRateLimiter
    {
        private static readonly object Gate = new object();
        public static int WaitSeconds(string deviceId)
        {
            lock (Gate) { return Remaining(Load(), deviceId); }
        }
        public static int RecordIntent(string deviceId)
        {
            lock (Gate)
            {
                var history = Load();
                int wait = Remaining(history, deviceId);
                if (wait > 0) return wait;
                var now = DateTime.UtcNow;
                List<DateTime> times;
                if (!history.TryGetValue(deviceId, out times)) times = new List<DateTime>();
                history[deviceId] = times.Where(t => t > now.AddHours(-1)).ToList();
                history[deviceId].Add(now);
                string path = SettingsManager.GetDataFilePath("sms-rate-history.json");
                string temp = path + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(history), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                return 0;
            }
        }
        private static Dictionary<string, List<DateTime>> Load()
        {
            string path = SettingsManager.GetDataFilePath("sms-rate-history.json");
            if (!File.Exists(path)) return new Dictionary<string, List<DateTime>>();
            var history = new JavaScriptSerializer().Deserialize<Dictionary<string, List<DateTime>>>(File.ReadAllText(path, Encoding.UTF8));
            if (history == null) throw new InvalidDataException("Invalid SMS rate history. Sending stopped.");
            return history;
        }
        private static int Remaining(Dictionary<string, List<DateTime>> history, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return 0;
            List<DateTime> times;
            if (!history.TryGetValue(deviceId, out times) || times == null) return 0;
            var now = DateTime.UtcNow;
            var active = times.Where(t => t > now.AddHours(-1)).OrderBy(t => t).ToList();
            if (active.Count == 0) return 0;
            // Ten-second spacing and at most 360 intents per rolling hour.
            double seconds = (active.Last().AddSeconds(10) - now).TotalSeconds;
            if (active.Count >= 360) seconds = Math.Max(seconds, (active[active.Count - 360].AddHours(1) - now).TotalSeconds);
            return Math.Max(0, (int)Math.Ceiling(seconds));
        }
    }
}

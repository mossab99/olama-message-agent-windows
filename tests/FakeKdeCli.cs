using System;
using System.IO;
using System.Threading;
class FakeKdeCli {
    static int Main(string[] args) {
        string mode = Environment.GetEnvironmentVariable("OLAMA_FAKE_MODE") ?? "success";
        if (Array.IndexOf(args, "--list-devices") >= 0) {
            if (mode != "offline") Console.WriteLine("- Test Phone: synthetic-phone (paired and reachable)");
            return 0;
        }
        string dir = Environment.GetEnvironmentVariable("OLAMA_AGENT_SETTINGS_DIR");
        if (!File.Exists(Path.Combine(dir, "pending-dispatch-result.json"))) return 7;
        File.AppendAllText(Path.Combine(dir, "fake-sends.txt"), "intent-present\n");
        if (mode == "hang") Thread.Sleep(120000);
        return mode == "fail" ? 8 : 0;
    }
}

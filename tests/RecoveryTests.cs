using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using OlamaMessageAgent;
class RecoveryTests {
    static int reserves, results; static bool rejectResults; static string lastBody; static int protocol = 2;
    static string dir; static HttpListener listener;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    static void CleanRate() { string path = Path.Combine(dir, "sms-rate-history.json"); if (File.Exists(path)) File.Delete(path); }
    static int Sends() { string path=Path.Combine(dir,"fake-sends.txt"); return File.Exists(path) ? File.ReadAllLines(path).Length : 0; }
    static async Task Serve() {
        while (listener.IsListening) {
            var context = await listener.GetContextAsync();
            string body;
            if (context.Request.Url.AbsolutePath.EndsWith("/config")) body="{\"dispatch_protocol_version\":"+protocol+",\"poll_interval_seconds\":10}";
            else if (context.Request.Url.AbsolutePath.EndsWith("/reserve")) {
                reserves++;
                string incoming = new StreamReader(context.Request.InputStream).ReadToEnd();
                Check(incoming.Contains("dispatch_protocol_version"), "reservation declares protocol");
                body="{\"jobs\":[{\"queue_id\":1,\"campaign_id\":1,\"attempt_id\":\""+Guid.NewGuid()+"\",\"phone_e164\":\"+000000000\",\"message_body\":\"Synthetic only\",\"expires_at\":\""+DateTime.UtcNow.AddMinutes(3).ToString("o")+"\"}]}";
            } else {
                results++; lastBody = new StreamReader(context.Request.InputStream).ReadToEnd();
                context.Response.StatusCode = rejectResults ? 503 : 200;
                body="{\"status\":\"ok\"}";
            }
            byte[] data = Encoding.UTF8.GetBytes(body); context.Response.ContentType="application/json";
            context.Response.ContentLength64=data.Length; await context.Response.OutputStream.WriteAsync(data,0,data.Length); context.Response.Close();
        }
    }
    static int Main(string[] args) { try { Run(args).GetAwaiter().GetResult(); return 0; } catch(Exception error) { Console.Error.WriteLine(error); return 1; } finally { if(listener!=null) listener.Close(); } }
    static async Task Run(string[] args) {
        dir = Path.Combine(Path.GetTempPath(),"olama-agent-recovery-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("OLAMA_AGENT_SETTINGS_DIR",dir);
        var portFinder = new TcpListener(IPAddress.Loopback,0); portFinder.Start(); int port=((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        listener=new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:"+port+"/"); listener.Start(); var server=Serve();
        var settings=new AgentSettings { SiteUrl="http://127.0.0.1:"+port, AgentUuid="synthetic", ApiKey="synthetic", KdeCliPath=Path.GetFullPath(args[0]), KdeDeviceId="synthetic-phone" };
        int oldResults=results, oldReserves=reserves;
        var connectionTest=await WordPressAgentClient.TestConnectionAsync(settings);
        Check(connectionTest.Success && results==oldResults && reserves==oldReserves,"settings connection test uses read-only config and never publishes heartbeat or reserves work");
        var worker=new DispatcherService(settings); Check(worker.Enable()==null,"fixture worker enabled");
        var response=await worker.PollOnceAsync();
        Check(response.Success && Sends()==1,"one synthetic command executes with intent already on disk");
        Check(lastBody.Contains("attempt_id") && lastBody.Contains("sent_by_kde"),"exact attempt result reported");
        Check(!File.Exists(Path.Combine(dir,"pending-dispatch-result.json")),"acknowledged result cleared");
        int before=reserves; await worker.PollOnceAsync(); Check(reserves==before && Sends()==1,"local rate gate prevents extra reservation");
        Check(SmsRateLimiter.WaitSeconds("synthetic-phone")>0,"rate history survives worker recreation");
        var agedHistory = new Dictionary<string,List<DateTime>> { { "synthetic-phone", new List<DateTime> { DateTime.UtcNow.AddSeconds(-11) } } };
        File.WriteAllText(Path.Combine(dir,"sms-rate-history.json"),new JavaScriptSerializer().Serialize(agedHistory));
        Check(SmsRateLimiter.WaitSeconds("synthetic-phone")==0,"eleven-second-old intent permits next send without one-minute wait");
        CleanRate(); Environment.SetEnvironmentVariable("OLAMA_FAKE_MODE","fail"); await worker.PollOnceAsync();
        Check(lastBody.Contains("unknown"),"nonzero KDE result is uncertain, not automatically retryable");
        CleanRate(); Environment.SetEnvironmentVariable("OLAMA_FAKE_MODE","hang"); var watch=System.Diagnostics.Stopwatch.StartNew(); await worker.PollOnceAsync();
        Check(watch.Elapsed.TotalSeconds<60 && lastBody.Contains("unknown"),"hung command times out and records unknown");
        CleanRate(); Environment.SetEnvironmentVariable("OLAMA_FAKE_MODE","success"); rejectResults=true; await worker.PollOnceAsync();
        Check(File.Exists(Path.Combine(dir,"pending-dispatch-result.json")),"unacknowledged result retained durably");
        int sent=Sends(); rejectResults=false; Environment.SetEnvironmentVariable("OLAMA_FAKE_MODE","offline");
        var recovered=new DispatcherService(settings); Check(recovered.Enable()==null,"pending outcome can recover with disconnected phone"); await recovered.PollOnceAsync();
        Check(Sends()==sent && !File.Exists(Path.Combine(dir,"pending-dispatch-result.json")),"restart replays result without resending SMS");
        CleanRate(); Environment.SetEnvironmentVariable("OLAMA_FAKE_MODE","success"); protocol=1;
        var incompatible=new DispatcherService(settings); incompatible.Enable(); before=reserves; await incompatible.PollOnceAsync();
        Check(reserves==before,"old server protocol fails closed"); protocol=2;
        File.WriteAllText(Path.Combine(dir,"pending-dispatch-result.json"),"null");
        var corrupt=new DispatcherService(settings); corrupt.Enable(); before=reserves; await corrupt.PollOnceAsync(); Check(reserves==before,"corrupt pending file fails closed");
        File.Delete(Path.Combine(dir,"pending-dispatch-result.json")); Directory.CreateDirectory(Path.Combine(dir,"pending-dispatch-result.json"));
        var blocked=new DispatcherService(settings); blocked.Enable(); sent=Sends(); await blocked.PollOnceAsync();
        Check(Sends()==sent,"intent persistence failure prevents invoking sender");
        Console.WriteLine("Recovery checks complete; all commands targeted a fake CLI and local HTTP fixture. Artifacts: "+dir);
    }
}

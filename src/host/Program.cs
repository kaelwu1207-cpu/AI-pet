namespace AIpet.Host;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        Native.EnableDpi();
        try {
            if(args.Length==4 && args[0]=="--resident") {
                if(new System.IO.FileInfo(args[1]).Length>65536)throw new ArgumentException("Resident configuration exceeds budget");
                var config=System.Text.Json.JsonSerializer.Deserialize<ResidentPlan>(System.IO.File.ReadAllText(args[1]))??throw new ArgumentException("Missing resident configuration");
                return HostRun.RunDispatcher(()=>ResidentTray.RunAsync(config,args[2],args[3]));
            }
            if(args.Length==1 && args[0] is "--overlay" or "--overlay-test-hang") return BlackoutWorker.Run(args[0]=="--overlay-test-hang");
            if(args.Length==1 && args[0]=="--describe-display") {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new DisplayScope("session-primary",1,new(0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1))))); return 0;
            }
            if(args.Length==2 && args[0]=="--describe-topology") {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(NativeTopology.ReadAsync(args[1]).GetAwaiter().GetResult()));return 0;
            }
            if(args.Length==3 && args[0]=="--run-plan") {
                if(new System.IO.FileInfo(args[1]).Length>65536) throw new ArgumentException("Plan exceeds 64 KiB input budget");
                var plan=System.Text.Json.JsonSerializer.Deserialize<HostPlan>(System.IO.File.ReadAllText(args[1])) ?? throw new ArgumentException("Missing plan");
                return HostRun.RunDispatcher(()=>HostRun.ExecuteAsync(plan,args[2]));
            }
            if(args.Length==4 && args[0]=="--closed-loop") {
                if(new System.IO.FileInfo(args[1]).Length>65536) throw new ArgumentException("Loop plan exceeds 64 KiB");
                var plan=System.Text.Json.JsonSerializer.Deserialize<LoopPlan>(System.IO.File.ReadAllText(args[1])) ?? throw new ArgumentException("Missing loop plan");
                return HostRun.RunDispatcher(async ()=> {await RecoveringLoop.RunAsync(plan,args[2],args[3]);});
            }
            if(args.Length==1 && args[0] is "--owner-test" or "--owner-test-throw") {
                var request=System.Text.Json.JsonSerializer.Deserialize<EffectRequest>(Console.ReadLine() ?? "") ?? throw new ArgumentException("Missing fixture request");
                return HostRun.RunDispatcher(async ()=> {
                    await using var effect=await EffectSession.StartAsync(request);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { effect.ProcessId,Hwnd=effect.Hwnd.ToInt64() }));
                    Console.Out.Flush();
                    if(args[0]=="--owner-test-throw") throw new InvalidOperationException("Injected host exception");
                    await Task.Delay(TimeSpan.FromSeconds(10));
                });
            }
            Console.Error.WriteLine("Usage: AIpet.Host --run-plan <resolved-plan.json> <fresh-data-output-directory>"); return 2;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

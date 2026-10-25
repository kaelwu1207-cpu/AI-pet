using AIpet.Host;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Text.Json;

static class HostIntegration
{
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS "+name); }
    [STAThread] static int Main(string[] args)
    {
        Native.EnableDpi();
        var topology = new DisplayScope("test-display", 1, new PixelRect(0, 0, 1920, 1080));
        var target = new MotionRequest("plan-1", "test-display", 1, 500, 300, 400, 2000);
        target.Validate(topology, 120, 120);
        bool rejected = false;
        try { (target with { TopologyEpoch = 0 }).Validate(topology, 120, 120); }
        catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("stale topology must reject before movement");
        rejected = false;
        try { (target with { X = -5 }).Validate(topology, 120, 120); }
        catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("target outside display must reject");
        Console.WriteLine("PASS motion boundary contract");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Exception? failure=null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => {
            try { await TestActor(args[0]); await TestEffects(args[0]); } catch(Exception error) { failure=error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        if(failure != null) { Console.Error.WriteLine(failure); return 1; }
        return 0;
    }
    static async Task TestActor(string output)
    {
        Directory.CreateDirectory(output);
        foreach(var item in new[]{("red",Colors.Red),("blue",Colors.Blue)}) {
            var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new[]{item.Item2.B,item.Item2.G,item.Item2.R,item.Item2.A},4);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,item.Item1+".png")); encoder.Save(stream);
        }
        var foreground=Native.GetForegroundWindow();
        var scope=new DisplayScope("session-primary",1,new(0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1)));
        using var actor=new VPetActor(scope,new(300,150,120,120),Path.Combine(output,"cache"));
        actor.SetSkin(new("skin-red",new Dictionary<string,string>{{"idle",Path.Combine(output,"red.png")},{"stride",Path.Combine(output,"blue.png")}}));
        actor.Play(new("walk-clip",new[]{new AnimationStep("idle",100),new AnimationStep("stride",100)}));
        bool transientIdle=false;
        actor.Pet.GraphDisplayHandler+=info=>{if(actor.Motion.IsMoving && info.Type!=VPet_Simulator.Core.GraphInfo.GraphType.Move)transientIdle=true;};
        var result=await actor.Motion.MoveToAsync(new("move-1",scope.DisplayId,1,450,200,500,2500));
        Check(result.Status=="completed" && result.ActualBounds.X==450 && result.ActualBounds.Y==200,"external target reached in physical pixels");
        Check(!transientIdle,"animation transitions retain native Move type without a transient idle state");
        var replacement=new Dictionary<string,string>{{"idle",Path.Combine(output,"blue.png")},{"stride",Path.Combine(output,"red.png")}};
        actor.SetSkin(new("skin-blue",replacement));
        var visible=actor.Pet.PetGrid.Visibility==Visibility.Visible?actor.Pet.PetGrid:actor.Pet.PetGrid2;
        Check(actor.AnimationId=="walk-clip" && actor.SkinId=="skin-blue"
            && ((VPet_Simulator.Core.Picture)visible.Tag).Path==Path.GetFullPath(replacement[actor.CurrentPose!]),"skin replacement preserves animation and rebinds the actual VPet frame");
        using var cancel=new CancellationTokenSource(60);
        var cancelled=await actor.Motion.MoveToAsync(new("move-cancel",scope.DisplayId,1,650,200,100,2500),cancel.Token);
        var settled=cancelled.ActualBounds; await Task.Delay(120);
        Check(cancelled.Status=="cancelled" && Native.Bounds(Native.Handle(actor.Window))==settled,"cancel stops native motion without late movement");
        var timeout=await actor.Motion.MoveToAsync(new("move-timeout",scope.DisplayId,1,900,200,1,100));
        Check(timeout.Status=="deadline","motion deadline settles");
        var slowOrigin=Native.Bounds(Native.Handle(actor.Window));
        var slow=await actor.Motion.MoveToAsync(new("move-slow",scope.DisplayId,1,slowOrigin.X+1,slowOrigin.Y,1,2000));
        Check(slow.Status=="completed" && slow.ActualBounds.X==slowOrigin.X+1,"subpixel accumulation supports slow directed movement");
        Check(Native.GetForegroundWindow()==foreground,"pet show, movement and skin swap preserve foreground");
    }
    static async Task TestEffects(string output)
    {
        var scope=new DisplayScope("session-primary",1,new(0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1)));
        var foreground=Native.GetForegroundWindow();
        var fixture=new Window { Width=300,Height=210,WindowStyle=WindowStyle.None,ShowActivated=false,
            ShowInTaskbar=false,Background=Brushes.White,ResizeMode=ResizeMode.NoResize };
        Native.MakePassive(fixture,false); fixture.Show(); Native.Place(Native.Handle(fixture),new(50,50,300,210));
        try {
            var request=new EffectRequest("black-normal",scope,new(90,90,120,80),600);
            await using(var effect=await EffectSession.StartAsync(request)) {
                Check(Native.IsWindowVisible(effect.Hwnd) && Native.Bounds(effect.Hwnd)==request.Bounds,"independent black window uses actual physical target bounds");
                long styles=Native.GetWindowLongPtr(effect.Hwnd,-20).ToInt64();
                Check((styles & (Native.NoActivate|Native.Transparent|Native.Layered))==(Native.NoActivate|Native.Transparent|Native.Layered),"overlay enables no-activate and layered click-through styles");
                Check(Native.SendMessage(effect.Hwnd,0x84,IntPtr.Zero,new IntPtr((110<<16)|110))==new IntPtr(-1),"native nonclient hit test is transparent");
                Check(Native.WindowFromPoint(new(110,110))==Native.Handle(fixture),"OS hit testing reaches underlying fixture across processes");
                var receipt=await effect.Completion;
                Check(receipt.RemovalConfirmed && receipt.EndReason=="duration" && !Native.IsWindow(effect.Hwnd),"duration removes overlay window");
                Check(receipt.ObservedMs>0 && receipt.WindowPixelMs==request.Bounds.Area*receipt.ObservedMs && receipt.LastObservedTicks>receipt.StartTicks,"coverage uses observed geometry and monotonic duration");
                Console.WriteLine("COVERAGE "+JsonSerializer.Serialize(receipt));
            }
            await using(var effect=await EffectSession.StartAsync(request with { PlanId="black-cancel",DurationMs=3000 })) {
                await Task.Delay(100); var receipt=await effect.CancelAsync();
                Check(receipt.EndReason=="cancelled" && receipt.RemovalConfirmed && receipt.ObservedMs<1000,"cancel retracts early and does not bill requested duration");
                Console.WriteLine("COVERAGE "+JsonSerializer.Serialize(receipt));
            }
            await using(var effect=await EffectSession.StartAsync(request with { PlanId="black-composited",DurationMs=4000 })) {
                string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.."));
                var info=new ProcessStartInfo(Path.Combine(root,"data/envs/capture/Scripts/python.exe")) { UseShellExecute=false,
                    RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
                info.ArgumentList.Add("-B"); info.ArgumentList.Add(Path.Combine(root,"tests/pet-hosts/probe_blackout.py"));
                foreach(var number in new[]{90,90,120,80}) info.ArgumentList.Add(number.ToString());
                using var sample=Process.Start(info)!;
                var stdout=sample.StandardOutput.ReadToEndAsync(); var stderr=sample.StandardError.ReadToEndAsync();
                try { await sample.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { if(!sample.HasExited) sample.Kill(); throw; }
                Console.WriteLine("COMPOSITOR "+await stdout); Console.Error.Write(await stderr);
                Check(sample.ExitCode==0,"upstream WGC observes black inside mask and untouched white outside");
                await effect.CancelAsync();
            }
            await using(var effect=await EffectSession.StartAsync(request with { PlanId="black-heartbeat",DurationMs=4000 })) {
                effect.StopHeartbeatForTest(); var receipt=await effect.Completion;
                Check(receipt.EndReason=="heartbeat-expired" && receipt.RemovalConfirmed && effect.ExitCode==3,"lost host heartbeat removes overlay independently");
                Console.WriteLine("COVERAGE "+JsonSerializer.Serialize(receipt));
            }
            await using(var effect=await EffectSession.StartAsync(request with { PlanId="black-worker-exit",DurationMs=4000 })) {
                using var worker=Process.GetProcessById(effect.ProcessId); worker.Kill();
                var receipt=await effect.Completion;
                Check(receipt.EndReason=="worker-exited-without-receipt" && receipt.RemovalConfirmed && receipt.WindowPixelMs==0,
                    "worker crash yields explicit missing evidence with zero coverage credit");
                Console.WriteLine("COVERAGE "+JsonSerializer.Serialize(receipt));
            }
            await using(var effect=await EffectSession.StartHungForTest(request with { PlanId="black-ui-hang",DurationMs=300 })) {
                var receipt=await effect.Completion;
                Check(receipt.EndReason=="hard-deadline" && receipt.RemovalConfirmed && receipt.ObservedMs==0,"blocked overlay dispatcher is killed and unobserved time gets zero credit");
                Console.WriteLine("COVERAGE "+JsonSerializer.Serialize(receipt));
            }
            var ownerInfo=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,RedirectStandardInput=true,
                RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
            ownerInfo.ArgumentList.Add(typeof(AIpet.Host.Program).Assembly.Location); ownerInfo.ArgumentList.Add("--owner-test");
            using(var owner=Process.Start(ownerInfo)!) {
                owner.StandardInput.WriteLine(JsonSerializer.Serialize(request with { PlanId="black-parent-death",DurationMs=5000 }));
                var line=await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
                using var ready=JsonDocument.Parse(line!);
                int childId=ready.RootElement.GetProperty("ProcessId").GetInt32();
                var hwnd=new IntPtr(ready.RootElement.GetProperty("Hwnd").GetInt64());
                using var child=Process.GetProcessById(childId); _=child.SafeHandle;
                try {
                    owner.Kill(); await owner.WaitForExitAsync();
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    Check(!Native.IsWindow(hwnd),"hard-killed owner removes overlay even when its stdout pipe breaks");
                    Console.WriteLine(JsonSerializer.Serialize(new {scenario="parent-killed",ownerExitCode=owner.ExitCode,workerExitCode=child.ExitCode,removed=true}));
                } finally {
                    if(!owner.HasExited) owner.Kill();
                    if(!child.HasExited) child.Kill();
                }
            }
            ownerInfo.ArgumentList.Clear(); ownerInfo.ArgumentList.Add(typeof(AIpet.Host.Program).Assembly.Location); ownerInfo.ArgumentList.Add("--owner-test-throw");
            using(var owner=Process.Start(ownerInfo)!) {
                owner.StandardInput.WriteLine(JsonSerializer.Serialize(request with { PlanId="black-owner-exception",DurationMs=5000 }));
                var errorRead=owner.StandardError.ReadToEndAsync();
                var line=await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
                using var ready=JsonDocument.Parse(line!);
                var hwnd=new IntPtr(ready.RootElement.GetProperty("Hwnd").GetInt64());
                await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Check(owner.ExitCode==1 && (await errorRead).Contains("Injected host exception") && !Native.IsWindow(hwnd),"handled host exception disposes the independent overlay");
                Console.WriteLine(JsonSerializer.Serialize(new {scenario="host-exception",ownerExitCode=owner.ExitCode,removed=true}));
            }
            Check(Native.GetForegroundWindow()==foreground,"overlay show, cancel and watchdog failures preserve foreground");
        } finally { fixture.Close(); }
    }
}

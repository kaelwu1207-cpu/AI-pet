using AIpet.Host;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
            try { await TestActor(args[0]); } catch(Exception error) { failure=error; }
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
        actor.SetSkin(new("skin-red",new Dictionary<string,string>{{"idle",Path.Combine(output,"red.png")}}));
        actor.Play(new("idle-clip",new[]{new AnimationStep("idle",100)}));
        var result=await actor.Motion.MoveToAsync(new("move-1",scope.DisplayId,1,450,200,500,2500));
        Check(result.Status=="completed" && result.ActualBounds.X==450 && result.ActualBounds.Y==200,"external target reached in physical pixels");
        actor.SetSkin(new("skin-blue",new Dictionary<string,string>{{"idle",Path.Combine(output,"blue.png")}}));
        Check(actor.AnimationId=="idle-clip" && actor.SkinId=="skin-blue","skin replacement preserves animation definition");
        using var cancel=new CancellationTokenSource(60);
        var cancelled=await actor.Motion.MoveToAsync(new("move-cancel",scope.DisplayId,1,650,200,100,2500),cancel.Token);
        var settled=cancelled.ActualBounds; await Task.Delay(120);
        Check(cancelled.Status=="cancelled" && Native.Bounds(Native.Handle(actor.Window))==settled,"cancel stops native motion without late movement");
        var timeout=await actor.Motion.MoveToAsync(new("move-timeout",scope.DisplayId,1,900,200,1,100));
        Check(timeout.Status=="deadline","motion deadline settles");
        Check(Native.GetForegroundWindow()==foreground,"pet show, movement and skin swap preserve foreground");
    }
}

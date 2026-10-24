using AIpet.Host;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path=System.IO.Path;
using System.Windows.Media.Imaging;

static class LoopGpu
{
    static void Check(bool value,string name) {if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    [STAThread] static int Main(string[] args) {Native.EnableDpi();return HostRun.RunDispatcher(()=>Run(args[0]));}
    static async Task Run(string output)
    {
        Directory.CreateDirectory(output);
        int x=Native.GetSystemMetrics(0)-800,y=Native.GetSystemMetrics(1)-400;
        var scope=new DisplayScope("session-primary",1,new(0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1)));
        var roi=new PixelRect(x,y,320,128);
        var canvas=new Canvas {Width=320,Height=128,Background=Brushes.Black,SnapsToDevicePixels=true};
        RenderOptions.SetEdgeMode(canvas,EdgeMode.Aliased);
        var definition=ObservationBaseline.Fixture;
        foreach(var line in definition.GetProperty("LineXs").EnumerateArray()) {
            var edge=new Rectangle {Width=1,Height=definition.GetProperty("LineHeight").GetInt32(),Fill=Brushes.White};Canvas.SetLeft(edge,line.GetInt32());Canvas.SetTop(edge,definition.GetProperty("LineY").GetInt32());canvas.Children.Add(edge);
        }
        var cursor=new Rectangle {Width=definition.GetProperty("CursorWidth").GetInt32(),Height=definition.GetProperty("CursorHeight").GetInt32(),Fill=Brushes.White};Canvas.SetLeft(cursor,definition.GetProperty("CursorX").GetInt32());Canvas.SetTop(cursor,definition.GetProperty("CursorY").GetInt32());canvas.Children.Add(cursor);
        var video=new Rectangle {Width=definition.GetProperty("DynamicWidth").GetInt32(),Height=128,Fill=Brushes.Red};Canvas.SetLeft(video,definition.GetProperty("DynamicX").GetInt32());canvas.Children.Add(video);
        var blink=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(definition.GetProperty("BlinkMs").GetInt32())};blink.Tick+=(_,_)=>cursor.Visibility=cursor.Visibility==Visibility.Visible?Visibility.Hidden:Visibility.Visible;
        int color=0;var dynamicTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(definition.GetProperty("DynamicMs").GetInt32())};
        var colors=new[]{Brushes.Red,Brushes.Blue,Brushes.Green,Brushes.Yellow};dynamicTimer.Tick+=(_,_)=>video.Fill=colors[++color%colors.Length];
        var fixture=new Window {Width=320,Height=128,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,
            Content=new Viewbox {Stretch=Stretch.Fill,Child=canvas}};
        Native.MakePassive(fixture,false);fixture.Show();Native.Place(Native.Handle(fixture),roi);blink.Start();dynamicTimer.Start();
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.."));
        var source=new GpuObserver(Path.Combine(root,"data/build/capture-rust/debug/observe.exe"),roi);
        var model=new StaticMap();long sequence=0;
        async Task Sample(int milliseconds) {
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<milliseconds) {
                if(source.Latest is { } frame && frame.Sequence>sequence) {model.Accept(frame);sequence=frame.Sequence;}
                if(source.Failure!=null)throw new InvalidOperationException(source.Failure);
                await Task.Delay(20);
            }
        }
        try {
            await source.WaitMasksAsync(source.SetMasks());await Sample(1600);
            Check(model.Regions.First(r=>r.Id==0).StableEdges>0 && model.Regions.First(r=>r.Id==0).StableMs>=700,"GPU preserves fixed one-pixel lines and their stable time");
            Check(model.Regions.First(r=>r.Id==1).StableMs>=700,"blinking cursor does not reset neighboring fixed-line age");
            Check(model.Regions.First(r=>r.Id==3).StableMs<500,"dynamic region does not acquire static age from adjacent lines");
            var mask=new PixelRect(x,y,64,64);
            await source.WaitMasksAsync(source.SetMasks(mask));await Sample(150);
            double frozen=model.Regions.First(r=>r.Id==0).StableMs;
            await using(var effect=await EffectSession.StartAsync(new("own-mask",scope,mask,500))) {
                await Sample(350);
                var region=model.Regions.First(r=>r.Id==0);
                Check(!region.Observable && region.StableMs==frozen,"actual own black overlay freezes prior static facts as unobservable");
                await effect.Completion;
            }
            await source.WaitMasksAsync(source.SetMasks());
            model.Accept(source.Latest!);sequence=source.Latest!.Sequence;
            Check(model.Regions.First(r=>r.Id==0).StableMs<500,"revealed pixels reacquire confidence instead of assuming hidden continuity");
            await Sample(700);
            Check(model.Regions.First(r=>r.Id==0).StableMs>=500,"revealed fixed lines become observable and stable again");
            Console.WriteLine("GPU_SUMMARY "+System.Text.Json.JsonSerializer.Serialize(new {sequence,regions=model.Regions.Count,
                statisticBytesPerFrame=source.Latest!.Tiles.Length*32,pixelReadback=false,pixelsPersisted=false}));
        } finally {
            await source.DisposeAsync();blink.Stop();dynamicTimer.Stop();fixture.Close();
            Console.WriteLine("GPU_EXIT "+source.ExitCode);if(source.Failure!=null)Console.Error.WriteLine(source.Failure);
        }
        // Reopen the same owned fixture for a full GPU -> scheduler -> pet/effect -> receipt run.
        fixture=new Window {Width=320,Height=128,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            ShowActivated=false,ShowInTaskbar=false,Content=new Viewbox {Stretch=Stretch.Fill}};
        // Previous Window owns its Viewbox; detach the Canvas before reusing it.
        var parent=(Viewbox)canvas.Parent;parent.Child=null;((Viewbox)fixture.Content).Child=canvas;
        Native.MakePassive(fixture,false);fixture.Show();Native.Place(Native.Handle(fixture),roi);blink.Start();dynamicTimer.Start();
        string skinPath=Path.Combine(output,"loop-skin.png");
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{200,140,20,255},4);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(skinPath))png.Save(file);
        bool inject=false,used=false;PixelRect? active=null;long effectStarted=0;double? fullAvoidanceMs=null;int nativeRecentSamples=0;
        var timer=Stopwatch.StartNew();
        OperationContext ReadInput() {
            var native=Native.ReadOperationContext();
            if(native.RecentInput)nativeRecentSamples++;
            // Own-fixture replay controls the global activity bit; native cursor/caret exclusions
            // remain active. Production/default CLI uses the complete native input policy.
            if(inject && active!=null && Stopwatch.GetElapsedTime(effectStarted).TotalMilliseconds>=80)
                return new(native.Exclusions.Append(active).ToArray(),false);
            return new(native.Exclusions,false);
        }
        void Trace(LoopEvent e) {
            if(e.Phase=="effect-active" && !used) {used=true;inject=true;active=e.Bounds;effectStarted=Stopwatch.GetTimestamp();}
            if(e.Phase=="finished" && inject) {fullAvoidanceMs=Stopwatch.GetElapsedTime(effectStarted).TotalMilliseconds-80;inject=false;}
        }
        try {
            var plan=new LoopPlan(scope,roi,new(x+360,y+150,120,120),new("loop-fixture",new Dictionary<string,string>{{"idle",skinPath}}),
                new("loop-idle",new[]{new AnimationStep("idle",150)}));
            var result=await ClosedLoop.RunAsync(plan,Path.Combine(root,"data/build/capture-rust/debug/observe.exe"),Path.Combine(output,"closed-loop"),ReadInput,Trace);
            Check(result.Actions.Length==4 && result.Actions.All(a=>a.Motion.Status=="completed"),"real GPU observations schedule four VPet directed actions");
            Check(result.Actions.Any(a=>a.Coverage?.EndReason=="cancelled") && fullAvoidanceMs is >=0 and <=150,"injected operation exclusion retracts active black mask within 150ms");
            Check(result.Actions.Where(a=>a.Coverage?.EndReason=="duration").Select(a=>a.RegionId).Distinct().Count()>=2,"real receipt feedback distributes service across distinct static regions");
            Check(result.SelfMaskedFrames>0 && result.ObservedWindowServiceMs.Values.Sum()>0,"self masks and measured receipts feed the running loop");
            Check(result.GpuExitCode==0 && result.CaptureFailure==null,"GPU statistics worker stops cleanly after closed loop");
            Console.WriteLine("LOOP_SUMMARY "+System.Text.Json.JsonSerializer.Serialize(new {frames=result.Frames,actions=result.Actions.Length,
                avoidanceMs=fullAvoidanceMs,elapsedMs=timer.ElapsedMilliseconds,maskedFrames=result.SelfMaskedFrames,inputSamples=result.InputSamples,nativeRecentSamples}));
            string cliPlan=Path.Combine(output,"cli-plan.json"),cliOutput=Path.Combine(output,"cli-loop");
            await File.WriteAllTextAsync(cliPlan,System.Text.Json.JsonSerializer.Serialize(plan with {MaximumActions=2,RunMs=6000}));
            var info=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{typeof(AIpet.Host.Program).Assembly.Location,"--closed-loop",cliPlan,Path.Combine(root,"data/build/capture-rust/debug/observe.exe"),cliOutput})info.ArgumentList.Add(arg);
            using var cli=Process.Start(info)!;var cliErrors=cli.StandardError.ReadToEndAsync();var cliText=cli.StandardOutput.ReadToEndAsync();
            try {await cli.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}catch{if(!cli.HasExited)cli.Kill();throw;}
            if(cli.ExitCode!=0)throw new InvalidOperationException(await cliErrors);
            var cliResult=System.Text.Json.JsonSerializer.Deserialize<LoopResult>(await File.ReadAllTextAsync(Path.Combine(cliOutput,"loop-result.json")))!;
            Check(cliResult.GpuExitCode==0 && cliResult.Frames>0 && cliResult.CaptureFailure==null
                && (cliResult.Actions.Length>0 || cliResult.InputSamples>0 && cliResult.AvoidedSelections>0)
                && cliResult.Actions.All(a=>a.Coverage==null||a.Coverage.RemovalConfirmed),"external JSON entry returns native-policy execution, pause or cancellation with clean removal");
            inject=false;used=true;
            var fullPlan=plan with {Roi=scope.Bounds,TargetBounds=new(x+32,y+32,256,64),MaximumActions=3};
            var full=await ClosedLoop.RunAsync(fullPlan,Path.Combine(root,"data/build/capture-rust/debug/observe.exe"),Path.Combine(output,"full-loop"),ReadInput);
            Check(full.Actions.Length==3 && full.Actions.All(a=>a.Coverage?.RemovalConfirmed==true),"full-screen GPU grid drives three measured local effects inside the owned fixture");
            await RecoveryTests.Run(plan,Path.Combine(root,"data/build/capture-rust/debug/observe.exe"),output,ReadInput);
        } finally {blink.Stop();dynamicTimer.Stop();fixture.Close();}
    }
}

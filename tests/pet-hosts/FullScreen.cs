using AIpet.Host;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

static class FullScreen
{
    static double P95(IEnumerable<double> input) {var values=input.Order().ToArray();return values.Length==0?0:values[(int)Math.Ceiling(values.Length*0.95)-1];}
    [STAThread] static int Main(string[] args) {Native.EnableDpi();return HostRun.RunDispatcher(()=>Run(args));}
    static async Task Run(string[] args) {
        string output=HostRun.ResolveOutput(args[0],"performance.json");Directory.CreateDirectory(output);
        var roi=new PixelRect(0,0,Native.GetSystemMetrics(0),Native.GetSystemMetrics(1));
        var patch=new Border {Background=Brushes.DarkBlue};
        var fixture=new Window {WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=160,Height=64,ShowActivated=false,ShowInTaskbar=false,Content=patch};
        Native.MakePassive(fixture,false);fixture.Show();Native.Place(Native.Handle(fixture),new(roi.Width-400,roi.Height-200,160,64));
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(120)};bool change=false;
        timer.Tick+=(_,_)=>patch.Background=(change=!change)?Brushes.DarkBlue:Brushes.DarkGreen;timer.Start();
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.."));
        var source=new GpuObserver(Path.Combine(root,"data/build/capture-rust/debug/observe.exe"),roi);
        var map=new StaticMap();var schedule=new CoverageScheduler(42);
        var wall=new List<double>();var gpu=new List<double>();var maps=new List<double>();var selections=new List<double>();
        var gaps=new List<double>();long sequence=0,lastStamp=0,privatePeak=0,workingPeak=0;ulong gpuPeak=0,owned=0;int bytes=0,stale=0;
        double cpuMs=0,hostCpuMs=0,seconds=0;int handles=0;
        using var host=Process.GetCurrentProcess();
        try {
            await source.WaitMasksAsync(source.SetMasks());await Task.Delay(1000);
            var initial=source.Counters();double initialHost=host.TotalProcessorTime.TotalMilliseconds;
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<6000) {
                if(source.Latest is {} frame && frame.Sequence>sequence) {
                    sequence=frame.Sequence;wall.Add(frame.AnalysisWallMs);
                    if(frame.Performance?.GpuMs is {} value)gpu.Add(value);
                    gpuPeak=Math.Max(gpuPeak,frame.Performance?.GpuLocalMemoryBytes??0);owned=frame.Performance?.OwnedGpuBytes??0;bytes=frame.StatisticReadbackBytes;
                    if(lastStamp!=0)gaps.Add((frame.Timestamp100ns-lastStamp)/10000.0);lastStamp=frame.Timestamp100ns;
                    var step=Stopwatch.StartNew();map.Accept(frame);maps.Add(step.Elapsed.TotalMilliseconds);
                    step.Restart();schedule.Select(map.Regions,new(Array.Empty<PixelRect>(),false),watch.Elapsed.TotalMilliseconds);selections.Add(step.Elapsed.TotalMilliseconds);
                }
                if(!source.Fresh)stale++;
                var counters=source.Counters();privatePeak=Math.Max(privatePeak,counters.PrivateBytes);workingPeak=Math.Max(workingPeak,counters.WorkingSet);handles=Math.Max(handles,counters.Handles);
                if(source.Failure!=null)throw new InvalidOperationException(source.Failure);
                await Task.Delay(20);
            }
            seconds=watch.Elapsed.TotalSeconds;cpuMs=source.Counters().CpuMs-initial.CpuMs;hostCpuMs=host.TotalProcessorTime.TotalMilliseconds-initialHost;
        } finally {await source.DisposeAsync();timer.Stop();fixture.Close();}
        if(source.ExitCode!=0 || source.Failure!=null || wall.Count<10 || gpu.Count<10)throw new Exception($"Insufficient healthy full-screen samples: frames={wall.Count}, gpu={gpu.Count}, exit={source.ExitCode}, last={JsonSerializer.Serialize(source.Latest?.Performance)}, error={source.Failure}");
        // Equal measured-duration receipts isolate scheduling fairness at the full display grid size.
        var fairRegions=map.Regions.Select(r=>r with{Observable=true,StableMs=600,StableEdges=1,Brightness=0.5}).ToArray();
        var fair=new CoverageScheduler(42,"full-grid",1);var visits=fairRegions.ToDictionary(r=>r.Id,_=>0);
        int iterations=Math.Min(4096,fairRegions.Length+100);var fairTimes=new List<double>();
        for(int i=0;i<iterations;i++) {
            var watch=Stopwatch.StartNew();var target=fair.Select(fairRegions,new(Array.Empty<PixelRect>(),false),i*100.0)!;fairTimes.Add(watch.Elapsed.TotalMilliseconds);
            visits[target.Id]++;
            fair.Apply("full-"+i,target,new("aipet.coverage/0.1","full-"+i,"full-grid",1,target.Bounds,0,100,1000,100,target.Bounds.Area*100,"opaque-black",1,"duration",true,true,"compositor-submitted-window-observed","unknown",0));
        }
        if(visits.Values.Max()-visits.Values.Min()>1)throw new Exception("Full-grid service fairness failed");
        var result=new {Schema="aipet.full-screen-performance/1",Profile=ObservationBaseline.Schema,Roi=roi,Frames=wall.Count,Seconds=seconds,
            AnalysisP95Ms=P95(wall),GpuP95Ms=P95(gpu),MapP95Ms=P95(maps),SelectionP95Ms=P95(selections),FrameGapP95Ms=P95(gaps),
            SourceCpuOneCorePercent=cpuMs/(seconds*10),HostCpuOneCorePercent=hostCpuMs/(seconds*10),LogicalProcessors=Environment.ProcessorCount,
            SourcePrivatePeakBytes=privatePeak,SourceWorkingPeakBytes=workingPeak,SourceHandlesPeak=handles,GpuLocalPeakBytes=gpuPeak,OwnedGpuBytes=owned,
            StatisticReadbackBytesPerFrame=bytes,StaleSamples=stale,FullGridRegions=fairRegions.Length,FairnessIterations=iterations,FairSelectionP95Ms=P95(fairTimes),VisitSpread=visits.Values.Max()-visits.Values.Min(),
            GpuExitCode=source.ExitCode,PixelReadback=false,PixelsPersisted=false};
        await File.WriteAllTextAsync(Path.Combine(output,"performance.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}

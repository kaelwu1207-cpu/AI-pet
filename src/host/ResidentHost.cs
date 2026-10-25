using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace AIpet.Host;
public enum ResidentState { Paused,Starting,Running,Pausing,WaitingDisplay,Faulted,Stopping,Stopped }
public sealed record ResidentSnapshot(ResidentState State,long Epoch,long Frames,long Actions,int Generations,string? Error);
public sealed class ResidentHost : IAsyncDisposable
{
    readonly LoopPlan template;readonly string gpu,output;readonly int interval;
    readonly SemaphoreSlim transition=new(1,1);readonly TopologyTracker tracker=new();
    readonly Func<OperationContext>? input;readonly Action<LoopEvent>? trace;
    CancellationTokenSource? cancellation;Task? running;
    long frames,actions;int generations;string? selectedId,error;long sampled;
    double captureCpuMs,effectCpuMs;long effectPrivatePeak;int effectHandlesPeak;
    public ResidentState State {get;private set;}=ResidentState.Paused;
    public ResidentSnapshot Snapshot=>new(State,tracker.Epoch,frames,actions,generations,error);
    public IntPtr ActivePetWindow {get;private set;}
    public LoopAction? LastAction {get;private set;}
    GpuObserver? currentSource;
    public void RestartCapture(){if(currentSource==null || currentSource.HasExited)throw new InvalidOperationException("No active capture");currentSource.RequestRestart();}
    public event Action<ResidentSnapshot>? Changed;
    public ResidentHost(LoopPlan plan,string gpuExecutable,string directory,int minimumActionIntervalMs=5000,
        Func<OperationContext>? inputSource=null,Action<LoopEvent>? traceSource=null)
    {
        if(minimumActionIntervalMs is <500 or >60000)throw new ArgumentException("Action interval budget 500..60000 ms");
        template=plan;gpu=gpuExecutable;output=HostRun.ResolveOutput(directory,"resident-result.json");interval=minimumActionIntervalMs;
        input=inputSource;trace=traceSource;Directory.CreateDirectory(output);
    }
    void Set(ResidentState state) {State=state;Changed?.Invoke(Snapshot);WriteStatus();}
    void WriteStatus()=>File.WriteAllText(Path.Combine(output,"status.json"),JsonSerializer.Serialize(Snapshot));
    public async Task ResumeAsync()
    {
        await transition.WaitAsync();
        try {
            if(State is ResidentState.Stopped or ResidentState.Stopping)throw new InvalidOperationException("Host has exited");
            if(running is {IsCompleted:false})return;
            cancellation?.Dispose();cancellation=new();error=null;Set(ResidentState.Starting);
            running=RunAsync(cancellation.Token);
            while(State==ResidentState.Starting)await Task.Delay(20);
            if(State==ResidentState.Faulted)throw new InvalidOperationException(error);
        } finally {transition.Release();}
    }
    public async Task PauseAsync()
    {
        await transition.WaitAsync();
        try {if(State==ResidentState.Stopped)return;Set(ResidentState.Pausing);cancellation?.Cancel();if(running!=null)await running;Set(ResidentState.Paused);}
        finally {transition.Release();}
    }
    public async Task ExitAsync()
    {
        await transition.WaitAsync();
        try {
            if(State==ResidentState.Stopped)return;Set(ResidentState.Stopping);cancellation?.Cancel();if(running!=null)await running;
            Set(ResidentState.Stopped);using var process=Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(output,"resident-result.json"),JsonSerializer.Serialize(new{Snapshot,StoppedUtc=DateTime.UtcNow,ScreenPixelsPersisted=false,
                CaptureCpuMs=captureCpuMs,EffectCpuMs=effectCpuMs,HostCpuMs=process.TotalProcessorTime.TotalMilliseconds,EffectPrivatePeakBytes=effectPrivatePeak,EffectHandlesPeak=effectHandlesPeak}));
        } finally {transition.Release();}
    }
    async Task RunAsync(CancellationToken stop)
    {
        int failures=0;
        try {
            while(!stop.IsCancellationRequested) {
                var topology=await NativeTopology.ReadAsync(gpu);tracker.Update(topology);tracker.Invalidate();
                selectedId??=template.Display.DisplayId=="session-primary"?topology.Displays.Single(d=>d.Primary).DisplayId:template.Display.DisplayId;
                var display=tracker.Find(selectedId);
                if(display==null){Set(ResidentState.WaitingDisplay);await Task.Delay(1000,stop);continue;}
                if(++generations>128)throw new InvalidOperationException("Session generation budget reached; restart the host");
                frames=0;
                var plan=template with{Display=display.Scope(tracker.Epoch),
                    Roi=RecoveringLoop.Rebase(template.Roi,template.Display.Bounds,display.Bounds),
                    PetBounds=RecoveringLoop.Rebase(template.PetBounds,template.Display.Bounds,display.Bounds),
                    TargetBounds=template.TargetBounds==null?null:RecoveringLoop.Rebase(template.TargetBounds,template.Display.Bounds,display.Bounds)};
                string phase=Path.Combine(output,"sessions",generations.ToString("D4"));
                long began=Stopwatch.GetTimestamp();
                var result=await ClosedLoop.RunAsync(plan,gpu,phase,input,trace,
                    (scope,roi,size)=>new GpuObserver(gpu,roi,size,scope,topology.Signature,true),true,
                    new LoopSession(stop,interval,(actor,source,count,_)=>Sample(actor,source,count),action=>{
                        actions++;LastAction=action;
                        if(action.Coverage?.WorkerMetrics is {} m){effectCpuMs+=m.CpuMs;effectPrivatePeak=Math.Max(effectPrivatePeak,m.PrivatePeakBytes);effectHandlesPeak=Math.Max(effectHandlesPeak,m.HandlesPeak);}
                        BoundedAppend("actions.jsonl",JsonSerializer.Serialize(action),4_194_304);
                    }));
                captureCpuMs+=result.CaptureCpuMs;
                currentSource=null;ActivePetWindow=IntPtr.Zero;
                if(stop.IsCancellationRequested)break;
                if(result.Frames>0 && Stopwatch.GetElapsedTime(began).TotalSeconds>=60)failures=0;
                if(result.GpuExitCode==0 && result.CaptureFailure==null)failures=0;
                else if(++failures>=3)throw new GpuUnavailableException(result.CaptureFailure??"Repeated capture interruption");
                Set(ResidentState.WaitingDisplay);await Task.Delay(1000,stop);
            }
        } catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception e) {error=e.Message;Set(ResidentState.Faulted);}
        finally {currentSource=null;ActivePetWindow=IntPtr.Zero;}
    }
    void Sample(VPetActor actor,GpuObserver source,long count)
    {
        currentSource=source;ActivePetWindow=Native.Handle(actor.Window);frames=count;
        if(count>0 && State is ResidentState.Starting or ResidentState.WaitingDisplay)Set(ResidentState.Running);
        if(Stopwatch.GetElapsedTime(sampled).TotalMilliseconds<1000 || source.HasExited)return;sampled=Stopwatch.GetTimestamp();
        using var p=Process.GetCurrentProcess();p.Refresh();var c=source.Counters();var f=source.Latest;
        BoundedAppend("metrics.jsonl",JsonSerializer.Serialize(new{Schema="aipet.resident-metrics/1",Utc=DateTime.UtcNow,Snapshot,SourcePid=source.ProcessId,HostCpuMs=p.TotalProcessorTime.TotalMilliseconds,
            HostPrivateBytes=p.PrivateMemorySize64,HostWorkingSet=p.WorkingSet64,HostHandles=p.HandleCount,
            SourceCpuMs=c.CpuMs,SourcePrivateBytes=c.PrivateBytes,SourceWorkingSet=c.WorkingSet,SourceHandles=c.Handles,
            AnalysisMs=f?.AnalysisWallMs,Gpu=f?.Performance,Fresh=source.Fresh,SourceTotalCpuMs=captureCpuMs+c.CpuMs,
            EffectTotalCpuMs=effectCpuMs,EffectPrivatePeakBytes=effectPrivatePeak,EffectHandlesPeak=effectHandlesPeak}),8_388_608);WriteStatus();
    }
    void BoundedAppend(string name,string text,long limit)
    {
        string path=Path.Combine(output,name);
        if(File.Exists(path)&&new FileInfo(path).Length>limit)File.Move(path,path+".previous",true);
        File.AppendAllText(path,text+Environment.NewLine);
    }
    public async ValueTask DisposeAsync(){await ExitAsync();cancellation?.Dispose();}
}

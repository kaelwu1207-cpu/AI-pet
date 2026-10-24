using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace AIpet.Host;
public sealed record RecoveryPhase(long Epoch,string Status,LoopResult Result);
public sealed record RecoveryRun(string DisplayId,string State,int WaitingSamples,RecoveryPhase[] Phases);
public static class RecoveringLoop
{
    public static PixelRect Rebase(PixelRect rectangle,PixelRect oldBounds,PixelRect newBounds) {
        if(rectangle==oldBounds)return newBounds;
        int width=Math.Min(rectangle.Width,newBounds.Width),height=Math.Min(rectangle.Height,newBounds.Height);
        return new(Math.Clamp(newBounds.X+(rectangle.X-oldBounds.X),newBounds.X,newBounds.X+newBounds.Width-width),
            Math.Clamp(newBounds.Y+(rectangle.Y-oldBounds.Y),newBounds.Y,newBounds.Y+newBounds.Height-height),width,height);
    }
    public static async Task<RecoveryRun> RunAsync(LoopPlan template,string gpuExecutable,string output,
        Func<OperationContext>? inputSource=null,Action<LoopEvent>? trace=null,
        Func<Task<NativeTopology>>? topologySource=null,Action<GpuObserver>? sourceOpened=null)
    {
        if(template.RunMs is <1000 or >30000 || template.MaximumActions is <1 or >8)throw new ArgumentException("Recovery run budget exceeded");
        output=HostRun.ResolveOutput(output,"recovery-result.json");
        HostRun.ResolveOutput(output,"loop-result.json");Directory.CreateDirectory(output);
        var clock=Stopwatch.StartNew();var tracker=new TopologyTracker();var phases=new List<RecoveryPhase>();
        string selected=template.Display.DisplayId,state="waiting-display-timeout";int waits=0,attempts=0,actions=0;
        while(clock.ElapsedMilliseconds<template.RunMs && actions<template.MaximumActions && attempts<3) {
            NativeTopology snapshot;
            try {snapshot=await (topologySource?.Invoke()??NativeTopology.ReadAsync(gpuExecutable));tracker.Update(snapshot);}
            catch(InvalidOperationException) {waits++;await Task.Delay(200);continue;}
            catch(TimeoutException) {waits++;await Task.Delay(200);continue;}
            if(selected=="session-primary")selected=snapshot.Displays.SingleOrDefault(d=>d.Primary)?.DisplayId??selected;
            var display=tracker.Find(selected);
            if(display==null) {waits++;await Task.Delay(200);continue;}
            int remaining=(int)(template.RunMs-clock.ElapsedMilliseconds);if(remaining<1000)break;
            var plan=template with {Display=display.Scope(tracker.Epoch),Roi=Rebase(template.Roi,template.Display.Bounds,display.Bounds),
                PetBounds=Rebase(template.PetBounds,template.Display.Bounds,display.Bounds),
                TargetBounds=template.TargetBounds==null?null:Rebase(template.TargetBounds,template.Display.Bounds,display.Bounds),
                RunMs=Math.Min(12000,remaining),MaximumActions=template.MaximumActions-actions};
            GpuObserver Create(DisplayScope scope,PixelRect roi,int tileSize) {
                var source=new GpuObserver(gpuExecutable,roi,tileSize,scope,snapshot.Signature);sourceOpened?.Invoke(source);return source;
            }
            LoopResult result;
            try {result=await ClosedLoop.RunAsync(plan,gpuExecutable,Path.Combine(output,"epochs",tracker.Epoch.ToString()),inputSource,trace,Create,true);}
            catch(DisplayUnavailableException error) {result=new(0,0,0,0,Array.Empty<LoopAction>(),new(),-998,error.Message);}
            bool healthy=result.GpuExitCode==0 && result.CaptureFailure==null;
            phases.Add(new(tracker.Epoch,healthy?"completed":"capture-interrupted",result));actions+=result.Actions.Length;
            if(healthy){state="completed";break;}
            state="capture-interrupted";attempts++;tracker.Invalidate();await Task.Delay(150);
        }
        var recovery=new RecoveryRun(selected,state,waits,phases.ToArray());
        await File.WriteAllTextAsync(Path.Combine(output,"recovery-result.json"),JsonSerializer.Serialize(recovery,new JsonSerializerOptions{WriteIndented=true}));
        var last=phases.LastOrDefault()?.Result;
        var combined=new LoopResult(phases.Sum(p=>p.Result.Frames),phases.Sum(p=>p.Result.SelfMaskedFrames),phases.Sum(p=>p.Result.InputSamples),
            phases.Sum(p=>p.Result.AvoidedSelections),phases.SelectMany(p=>p.Result.Actions).ToArray(),last?.ObservedWindowServiceMs??new(),
            last?.GpuExitCode??-999,state=="completed"?null:last?.CaptureFailure??state);
        await File.WriteAllTextAsync(Path.Combine(output,"loop-result.json"),JsonSerializer.Serialize(combined,new JsonSerializerOptions{WriteIndented=true}));
        if(state!="completed")throw new InvalidOperationException("Bounded recovery ended: "+state);
        return recovery;
    }
}

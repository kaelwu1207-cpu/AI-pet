using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AIpet.Host;
public sealed record LoopPlan(DisplayScope Display,PixelRect Roi,PixelRect PetBounds,SkinDefinition Skin,AnimationClip Animation,
    int RunMs=10000,int MaximumActions=4,int EffectMs=240,int Seed=42,int OutputTileSize=64,PixelRect? TargetBounds=null);
public sealed record LoopAction(int RegionId,MotionReceipt Motion,CoverageReceipt? Coverage,double? AvoidanceLatencyMs,string? InterruptReason);
public sealed record LoopEvent(string Phase,int RegionId,PixelRect Bounds);
public sealed record LoopResult(long Frames,int SelfMaskedFrames,int InputSamples,int AvoidedSelections,
    LoopAction[] Actions,Dictionary<int,double> ObservedWindowServiceMs,int? GpuExitCode,string? CaptureFailure);
public static class ClosedLoop
{
    static PixelRect Expanded(PixelRect r)=>new(r.X-48,r.Y-48,r.Width+96,r.Height+96);
    public static async Task<LoopResult> RunAsync(LoopPlan plan,string gpuExecutable,string output,
        Func<OperationContext>? inputSource=null,Action<LoopEvent>? trace=null,
        Func<DisplayScope,PixelRect,int,GpuObserver>? sourceFactory=null,bool allowCaptureFailure=false)
    {
        if(plan.RunMs is < 1000 or > 12000 || plan.MaximumActions is < 1 or > 8 || plan.EffectMs is < 100 or > 1000
            || !plan.Display.Bounds.Contains(plan.Roi) || plan.Roi.Area>ObservationBaseline.MaximumPixels || !plan.Display.Bounds.Contains(plan.PetBounds)
            || plan.OutputTileSize is not (16 or 64) || (plan.TargetBounds!=null && !plan.Roi.Contains(plan.TargetBounds)))
            throw new ArgumentException("Invalid bounded loop plan");
        Native.ValidateDisplay(plan.Display);
        if(plan.Display.DisplayId=="session-primary") {
            var topology=await NativeTopology.ReadAsync(gpuExecutable);
            var display=topology.Displays.Single(d=>d.Primary);
            if(display.Bounds!=plan.Display.Bounds)throw new ArgumentException("Legacy primary bounds changed");
            plan=plan with{Display=display.Scope(Stopwatch.GetTimestamp())};
        }
        output=HostRun.ResolveOutput(output,"loop-result.json");
        Directory.CreateDirectory(output);
        using var actor=new VPetActor(plan.Display,plan.PetBounds,Path.Combine(output,"cache"));
        actor.SetSkin(plan.Skin);actor.Play(plan.Animation);
        var source=sourceFactory?.Invoke(plan.Display,plan.Roi,plan.OutputTileSize)??new GpuObserver(gpuExecutable,plan.Roi,plan.OutputTileSize,plan.Display);
        var map=new StaticMap();var scheduler=new CoverageScheduler(plan.Seed,plan.Display.DisplayId,plan.Display.TopologyEpoch);
        var actions=new List<LoopAction>();long sequence=0;int maskedFrames=0,inputSamples=0,avoided=0;
        var elapsed=Stopwatch.StartNew();
        string? unavailable=null;
        OperationContext Input() {inputSamples++;return (inputSource??Native.ReadOperationContext)();}
        void Observe() {
            if(source.Latest is { } frame && frame.Sequence>sequence) {
                map.Accept(frame);sequence=frame.Sequence;if(frame.Tiles.Any(t=>t.Masked>0))maskedFrames++;
            }
        }
        try {
            await source.WaitMasksAsync(source.SetMasks(Expanded(Native.Bounds(Native.Handle(actor.Window)))));
            while(elapsed.ElapsedMilliseconds<plan.RunMs && actions.Count<plan.MaximumActions) {
                Observe();
                if(!source.Fresh) {
                    if(source.HasExited)break;
                    if(elapsed.ElapsedMilliseconds>1000)throw new GpuUnavailableException("GPU observations stalled");
                    await Task.Delay(30);continue;
                }
                var context=Input();
                var region=scheduler.Select(map.Regions.Where(r=>plan.TargetBounds==null || plan.TargetBounds.Contains(r.Bounds)),context,elapsed.Elapsed.TotalMilliseconds);
                if(region==null) {avoided++;await Task.Delay(30);continue;}
                // Cast from beside the target, so the pet body does not replace the observed target.
                var body=Native.Bounds(Native.Handle(actor.Window));
                var destination=body with {X=Math.Clamp(plan.Roi.X+plan.Roi.Width+32,plan.Display.Bounds.X,plan.Display.Bounds.X+plan.Display.Bounds.Width-body.Width),
                    Y=Math.Clamp(region.Bounds.Y,plan.Display.Bounds.Y,plan.Display.Bounds.Y+plan.Display.Bounds.Height-body.Height)};
                if(context.Blocks(Geometry.Swept(body,destination))) {scheduler.FailedAttempt(region.Id,elapsed.Elapsed.TotalMilliseconds);avoided++;await Task.Delay(30);continue;}
                string planId=Guid.NewGuid().ToString("N");
                // Reserve the whole swept route before moving: capture can lag window placement.
                // Keep that conservative mask until a post-motion frame acknowledges the body.
                var routeMask=Expanded(Geometry.Swept(body,destination));
                await source.WaitMasksAsync(source.SetMasks(routeMask,region.Bounds));
                trace?.Invoke(new("moving",region.Id,region.Bounds));
                using var cancel=new CancellationTokenSource();
                var moving=actor.Motion.MoveToAsync(new(planId,plan.Display.DisplayId,plan.Display.TopologyEpoch,destination.X,destination.Y,1200,1800),cancel.Token);
                double? blockedAt=null;
                string? interruptReason=null;
                while(!moving.IsCompleted) {
                    Observe();var now=Input();
                    if(!source.Fresh || now.Blocks(region.Bounds) || now.Blocks(Geometry.Swept(Native.Bounds(Native.Handle(actor.Window)),destination))) {
                        blockedAt ??= elapsed.Elapsed.TotalMilliseconds;cancel.Cancel();
                        interruptReason ??= !source.Fresh?"capture-stale":now.Blocks(region.Bounds)?"operation-target":"operation-route";
                    }
                    await Task.Delay(20);
                }
                var motion=await moving;
                await source.WaitMasksAsync(source.SetMasks(Expanded(Native.Bounds(Native.Handle(actor.Window))),region.Bounds));
                CoverageReceipt? receipt=null;
                var effectInput=Input();
                if(motion.Status=="completed" && source.Fresh && !effectInput.Blocks(region.Bounds)) {
                    Native.ValidateDisplay(plan.Display);
                    await using var effect=await EffectSession.StartAsync(new(planId,plan.Display,region.Bounds,plan.EffectMs));
                    trace?.Invoke(new("effect-active",region.Id,region.Bounds));
                    while(!effect.Completion.IsCompleted) {
                        Observe();
                        var activeInput=Input();
                        if(!source.Fresh || activeInput.Blocks(region.Bounds)) {
                            blockedAt ??= elapsed.Elapsed.TotalMilliseconds;
                            interruptReason ??= !source.Fresh?"capture-stale":activeInput.RecentInput?"recent-input":"operation-effect";
                            receipt=await effect.CancelAsync();break;
                        }
                        await Task.Delay(20);
                    }
                    receipt ??= await effect.Completion;
                    if(receipt.DisplayId!=plan.Display.DisplayId || receipt.TopologyEpoch!=plan.Display.TopologyEpoch) throw new InvalidOperationException("Receipt scope mismatch");
                    scheduler.Apply(planId,region,receipt);
                } else {scheduler.FailedAttempt(region.Id,elapsed.Elapsed.TotalMilliseconds);interruptReason ??= !source.Fresh?"capture-stale":"operation-before-effect";}
                double? latency=blockedAt.HasValue?elapsed.Elapsed.TotalMilliseconds-blockedAt:null;
                actions.Add(new(region.Id,motion,receipt,latency,interruptReason));
                trace?.Invoke(new("finished",region.Id,region.Bounds));
                source.SetMasks(Expanded(Native.Bounds(Native.Handle(actor.Window))));
                await Task.Delay(30);
            }
        } catch(Exception error) when(allowCaptureFailure && error is GpuUnavailableException or DisplayUnavailableException) {unavailable=error.Message;}
        finally {await source.DisposeAsync();}
        var result=new LoopResult(sequence,maskedFrames,inputSamples,avoided,actions.ToArray(),
            map.Regions.ToDictionary(r=>r.Id,r=>scheduler.WindowServiceMs(r.Id)),source.ExitCode,source.Failure??unavailable);
        await File.WriteAllTextAsync(Path.Combine(output,"loop-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));
        if((result.GpuExitCode!=0 || result.CaptureFailure!=null) && !allowCaptureFailure) throw new InvalidOperationException("GPU observer failed: "+result.CaptureFailure);
        return result;
    }
}

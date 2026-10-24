using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace AIpet.Host;
public sealed record HostPlan(DisplayScope Display,PixelRect PetBounds,SkinDefinition Skin,AnimationClip Animation,
    MotionRequest Motion,EffectRequest Effect);
public static class HostRun
{
    public static async Task ExecuteAsync(HostPlan plan,string output)
    {
        // The development build lives in data/build/<host>; all mutable runtime files stay in data.
        string dataRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."));
        output=Path.GetFullPath(output);
        if(!output.StartsWith(dataRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Run output must be inside the declared project data root");
        if(File.Exists(Path.Combine(output,"run-result.json"))) throw new ArgumentException("Use a fresh run output directory");
        Native.ValidateDisplay(plan.Display);
        if(plan.Effect.Display!=plan.Display || !plan.Display.Bounds.Contains(plan.PetBounds)) throw new ArgumentException("Plan scope mismatch");
        plan.Motion.Validate(plan.Display,plan.PetBounds.Width,plan.PetBounds.Height); plan.Effect.Validate();
        Directory.CreateDirectory(output);
        using var actor=new VPetActor(plan.Display,plan.PetBounds,Path.Combine(output,"cache"));
        actor.SetSkin(plan.Skin); actor.Play(plan.Animation);
        await using var effect=await EffectSession.StartAsync(plan.Effect);
        MotionReceipt? motion=null; CoverageReceipt? coverage=null; string? failure=null;
        try {
            motion=await actor.Motion.MoveToAsync(plan.Motion);
            if(motion.Status!="completed") { failure="Motion ended: "+motion.Status; coverage=await effect.CancelAsync(); }
            else coverage=await effect.Completion;
            if(coverage.EndReason!="duration" && failure==null) failure="Effect ended: "+coverage.EndReason;
        }
        catch(Exception error) { failure=error.Message; coverage=await effect.CancelAsync(); }
        finally {
            coverage ??= await effect.CancelAsync();
            await File.WriteAllTextAsync(Path.Combine(output,"run-result.json"),JsonSerializer.Serialize(new {
                schema="aipet.host-run/0.1",runId=Guid.NewGuid().ToString("N"),motion,coverage,error=failure,
                completedUtc=DateTime.UtcNow,screenPixelsPersisted=false
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        if(failure!=null) throw new InvalidOperationException(failure);
    }
    public static int RunDispatcher(Func<Task> action)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Exception? failure=null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async ()=> {
            try { await action(); } catch(Exception error) { failure=error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        if(failure==null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }
}

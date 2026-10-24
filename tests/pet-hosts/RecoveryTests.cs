using AIpet.Host;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
static class RecoveryTests
{
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    public static async Task Run(LoopPlan plan,string executable,string output,Func<OperationContext> input) {
        var topology=await NativeTopology.ReadAsync(executable);var repeated=await NativeTopology.ReadAsync(executable);
        Check(topology.Signature==repeated.Signature && topology.Displays.Select(d=>d.DisplayId).SequenceEqual(repeated.Displays.Select(d=>d.DisplayId)),"native display identity and topology survive repeated enumeration");
        var primary=topology.Displays.Single(d=>d.Primary);
        var stale=new GpuObserver(executable,plan.Roi,64,primary.Scope(10),"stale-signature");bool rejected=false;
        try{await stale.WaitMasksAsync(stale.SetMasks());}catch(GpuUnavailableException){rejected=true;}finally{await stale.DisposeAsync();}
        Check(rejected && stale.ExitCode!=0 && stale.FirstFrame==null,"stale topology startup is rejected before any frame or effect");
        var sources=new List<GpuObserver>();GpuObserver? active=null;bool requested=false;int reads=0;
        var restart=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(80)};
        restart.Tick+=(_,_)=>{restart.Stop();active!.RequestRestart();};
        void Trace(LoopEvent e) {if(e.Phase=="effect-active" && !requested){requested=true;restart.Start();}}
        Task<NativeTopology> Snapshot() {
            reads++;
            // Controlled removal/reconnect, never changes the owner's actual display configuration.
            return Task.FromResult(reads is 2 or 3?topology with {Displays=Array.Empty<NativeDisplay>(),Signature="fixture-disconnected"}:topology);
        }
        RecoveryRun result;
        try {
            result=await RecoveringLoop.RunAsync(plan with{RunMs=12000,MaximumActions=3},executable,Path.Combine(output,"recovery"),input,Trace,Snapshot,
                source=>{active=source;sources.Add(source);});
        } finally{restart.Stop();}
        Check(result.State=="completed" && result.Phases.Length==2 && result.Phases[0].Result.GpuExitCode==4 && result.Phases[1].Result.GpuExitCode==0,
            "real WGC statistics worker restarts and resumes after active-effect interruption");
        Check(result.WaitingSamples>=2 && result.Phases[1].Epoch>result.Phases[0].Epoch,"controlled disconnect waits for the same device and creates a fresh epoch");
        Check(sources.Count==2 && sources.All(s=>s.FirstFrame?.GapReset==true) && sources.Select(s=>s.FirstFrame!.TopologyEpoch).Distinct().Count()==2,
            "each restarted GPU stream rebuilds pixel history and carries its new epoch");
        Check(result.Phases.SelectMany(p=>p.Result.Actions).All(a=>a.Coverage==null || a.Coverage.RemovalConfirmed)
            && result.Phases[0].Result.Actions.Any(a=>a.Coverage?.EndReason=="cancelled"),"capture interruption retracts black masks before resuming");
        var old=result.Phases[0].Result.Actions.First(a=>a.Coverage!=null).Coverage!;
        var ledger=new CoverageScheduler(42,primary.DisplayId,result.Phases[1].Epoch);rejected=false;
        try{ledger.Apply(old.PlanId,new(0,old.ActualBounds,600,true,1,0.5),old);}catch(ArgumentException){rejected=true;}
        Check(rejected,"recovered scheduler rejects the prior epoch's measured receipt");
        int raceReads=0;
        Task<NativeTopology> StartupRace() {
            raceReads++;
            return Task.FromResult(raceReads==1?topology with {Signature="fixture-startup-race",Displays=topology.Displays.Select(d=>d.Primary?d with{Bounds=d.Bounds with{Width=d.Bounds.Width-1}}:d).ToArray()}:topology);
        }
        var race=await RecoveringLoop.RunAsync(plan with{RunMs=8000,MaximumActions=1},executable,Path.Combine(output,"startup-race"),input,null,StartupRace);
        Check(race.State=="completed" && race.Phases.Length==2 && race.Phases[0].Result.Frames==0 && race.Phases[0].Result.Actions.Length==0
            && race.Phases[0].Result.GpuExitCode==null && race.Phases[0].Status=="startup-topology-changed"
            && race.Phases[1].Result.Actions.Any(a=>a.Coverage?.RemovalConfirmed==true),"startup geometry race reacquires topology before producing coverage");
        Console.WriteLine("RECOVERY_SUMMARY "+JsonSerializer.Serialize(new{phases=result.Phases.Length,result.WaitingSamples,epochs=result.Phases.Select(p=>p.Epoch),sourceExits=result.Phases.Select(p=>p.Result.GpuExitCode),physicalDisplays=topology.Displays.Length,controlledTopology=true}));
    }
}

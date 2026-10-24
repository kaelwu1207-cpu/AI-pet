using AIpet.Host;
static class TopologyTests
{
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    public static void Run() {
        var a=new NativeDisplay("monitor-a","DISPLAY1",new(0,0,1920,1080),1,60,1,100,true,"device-interface-path",0);
        var b=new NativeDisplay("monitor-b","DISPLAY2",new(-1280,0,1280,1024),1,60,1,150,false,"device-interface-path",0);
        var tracker=new TopologyTracker();tracker.Update(new("aipet.topology/1","first",new[]{a,b}));long first=tracker.Epoch;
        tracker.Update(new("aipet.topology/1","reordered",new[]{b,a}));
        Check(tracker.Epoch==first && tracker.Find("monitor-b")!.Bounds.X==-1280,"enumeration order preserves display identity and negative coordinates");
        tracker.Update(new("aipet.topology/1","scaled",new[]{a,b with{ScalePercent=200}}));
        Check(tracker.Epoch>first,"scale changes invalidate the capture epoch");
        long scaled=tracker.Epoch;tracker.Update(new("aipet.topology/1","removed",new[]{a}));
        Check(tracker.Find("monitor-b")==null && tracker.Epoch>scaled,"removed display becomes unavailable instead of retargeting another monitor");
        tracker.Update(new("aipet.topology/1","returned",new[]{a,b with{DeviceName="DISPLAY9"}}));
        Check(tracker.Find("monitor-b")!.DisplayId==b.DisplayId,"reconnected device path survives changed enumeration name");
        long before=tracker.Epoch;tracker.Invalidate();Check(tracker.Epoch>before,"same-topology source restart receives a fresh epoch");
        bool rejected=false;try{tracker.Update(new("aipet.topology/1","duplicate",new[]{a,a}));}catch(ArgumentException){rejected=true;}
        Check(rejected,"ambiguous display identities are rejected");
        Check(RecoveringLoop.Rebase(new(100,100,320,128),a.Bounds,new(-1280,-100,1280,1024))==new PixelRect(-1180,0,320,128),"bounded ROI rebases into moved negative-coordinate displays");
        Check(RecoveringLoop.Rebase(a.Bounds,a.Bounds,b.Bounds)==b.Bounds,"full-screen observation follows the new display bounds");
    }
}

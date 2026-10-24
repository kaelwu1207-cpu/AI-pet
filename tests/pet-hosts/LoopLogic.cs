using AIpet.Host;
static class LoopLogic
{
    static void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); }
    static int Main()
    {
        try { Run();return 0; }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Run()
    {
        var model=new StaticMap();
        var tile=new GpuTile(0,0,16,16,256,255,16,900,900,1,65000,0);
        model.Accept(new(1,1000000,1,new(0,0,16,16),new[]{tile},true,false));
        Check(model.Regions[0].StableMs==900,"fixed edges retain their observed age despite one changed cursor pixel");
        model.Accept(new(2,2000000,2,new(0,0,16,16),new[]{tile with { Visible=0,Masked=256,StaticPixels=0,StableEdges=0,Changed=0,LumaSum=0,EdgeAgeMs=0 }},true,false));
        Check(!model.Regions[0].Observable && model.Regions[0].StableMs==900,"own overlay freezes prior observations without inventing hidden stability");
        var scheduler=new CoverageScheduler(42);
        var regions=Enumerable.Range(0,4).Select(i=>new StaticRegion(i,new(i*64,0,64,64),900,true,16,0.5)).ToArray();
        var counts=new int[4];
        for(int i=0;i<40;i++) {
            var selected=scheduler.Select(regions,new(Array.Empty<PixelRect>(),false),i*1000)!;
            counts[selected.Id]++;
            scheduler.Apply("plan-"+i,selected,new("aipet.coverage/0.1","plan-"+i,"test",1,selected.Bounds,1,101,1000,100,selected.Bounds.Area*100,"opaque-black",1,"duration",true,true,"compositor-submitted-window-observed","unknown",0));
        }
        Check(counts.Max()-counts.Min()<=1,"actual service time balances all eligible regions");
        Check(scheduler.Select(regions,new(new[]{new PixelRect(0,0,256,64)},false),50000)==null,"operation exclusion rejects all overlapping targets");
        Check(scheduler.Select(regions,new(Array.Empty<PixelRect>(),true),50000)==null,"recent input pauses scheduling");
        var bounded=new CoverageScheduler(1,"display",2);var region=regions[0];
        var receipt=new CoverageReceipt("aipet.coverage/0.1","dedup","display",2,region.Bounds,1,101,1000,100,region.Bounds.Area*100,"opaque-black",1,"duration",true,true,"compositor-submitted-window-observed","unknown",0);
        bounded.Apply("dedup",region,receipt);bounded.Apply("dedup",region,receipt);
        Check(bounded.WindowServiceMs(region.Id)==100,"duplicate actual receipt cannot double-credit coverage");
        bounded.Apply("missing",region,receipt with {PlanId="missing",EvidenceLevel="missing",ObservedMs=0,WindowPixelMs=0});
        Check(bounded.WindowServiceMs(region.Id)==100,"missing evidence gets zero fairness credit");
        bool invalid=false;try{bounded.Apply("bad",region,receipt with{PlanId="bad",TopologyEpoch=1});}catch(ArgumentException){invalid=true;}
        Check(invalid,"old display epoch cannot update the ledger");
        invalid=false;try{bounded.Apply("bad-time",region,receipt with{PlanId="bad-time",ObservedMs=500});}catch(ArgumentException){invalid=true;}
        Check(invalid,"planned or inconsistent time cannot replace measured duration");
        Check(Geometry.Intersects(Geometry.Swept(new(0,0,20,20),new(100,100,20,20)),new(50,50,10,10)),"operation avoidance checks swept path as well as destination");
        invalid=false;try{new StaticMap().Accept(new(1,1000,0,new(0,0,32,16),new[]{tile,tile},true,false));}catch(ArgumentException){invalid=true;}
        Check(invalid,"duplicate GPU tiles cannot inflate full-grid counts");
        Console.WriteLine("RESULT loop logic passed");
    }
}

namespace AIpet.Host;
public sealed record GpuTile(int X,int Y,int Width,int Height,int Visible,int StaticPixels,int StableEdges,
    int MaxAgeMs,int EdgeAgeMs,int Changed,int LumaSum,int Masked);
public sealed record ObservationFrame(long Sequence,long Timestamp100ns,int MaskRevision,PixelRect Roi,
    GpuTile[] Tiles,bool Healthy,bool GapReset,double AnalysisWallMs=0,GpuPerformance? Performance=null,int StatisticReadbackBytes=0,int TileSize=16);
public sealed record GpuPerformance(double? GpuMs,ulong? GpuLocalMemoryBytes,ulong OwnedGpuBytes,ulong? GpuNonLocalMemoryBytes=null,string? Adapter=null);
public sealed record StaticRegion(int Id,PixelRect Bounds,double StableMs,bool Observable,int StableEdges,double Brightness);
public sealed record OperationContext(PixelRect[] Exclusions,bool RecentInput)
{
    public bool Blocks(PixelRect rectangle) => RecentInput || Exclusions.Any(r=>Geometry.Intersects(r,rectangle));
}
public static class Geometry
{
    public static bool Intersects(PixelRect a,PixelRect b) => a.X<(long)b.X+b.Width && b.X<(long)a.X+a.Width && a.Y<(long)b.Y+b.Height && b.Y<(long)a.Y+a.Height;
    public static long IntersectionArea(PixelRect a,PixelRect b) => Math.Max(0,Math.Min((long)a.X+a.Width,(long)b.X+b.Width)-Math.Max(a.X,b.X))
        *Math.Max(0,Math.Min((long)a.Y+a.Height,(long)b.Y+b.Height)-Math.Max(a.Y,b.Y));
    public static PixelRect Swept(PixelRect from,PixelRect to) => new(Math.Min(from.X,to.X),Math.Min(from.Y,to.Y),
        Math.Max(from.X+from.Width,to.X+to.Width)-Math.Min(from.X,to.X),Math.Max(from.Y+from.Height,to.Y+to.Height)-Math.Min(from.Y,to.Y));
}
public sealed class StaticMap
{
    long sequence=-1,timestamp=-1;
    PixelRect? roi;
    readonly Dictionary<int,StaticRegion> history=new();
    public IReadOnlyList<StaticRegion> Regions {get;private set;}=Array.Empty<StaticRegion>();
    public void Accept(ObservationFrame frame)
    {
        if(frame.Sequence<=sequence || frame.Timestamp100ns<=timestamp || frame.Tiles.Length>65536 || frame.TileSize is not (16 or 64) || (roi!=null && roi!=frame.Roi)
            || frame.Tiles.Length!=((frame.Roi.Width+frame.TileSize-1)/frame.TileSize)*((frame.Roi.Height+frame.TileSize-1)/frame.TileSize)
            || frame.Tiles.Select(t=>(t.X,t.Y)).Distinct().Count()!=frame.Tiles.Length
            || frame.Tiles.Any(t=>(t.X-frame.Roi.X)%frame.TileSize!=0 || (t.Y-frame.Roi.Y)%frame.TileSize!=0
                || t.Width!=Math.Min(frame.TileSize,frame.Roi.X+frame.Roi.Width-t.X) || t.Height!=Math.Min(frame.TileSize,frame.Roi.Y+frame.Roi.Height-t.Y))
            || frame.Tiles.Any(t=>!frame.Roi.Contains(new(t.X,t.Y,t.Width,t.Height)) || t.Width<1 || t.Width>frame.TileSize || t.Height<1 || t.Height>frame.TileSize
                || t.Visible<0 || t.Masked<0 || t.Visible+t.Masked!=t.Width*t.Height || t.StaticPixels<0 || t.StaticPixels>t.Visible
                || t.StableEdges<0 || t.StableEdges>t.Visible || t.Changed<0 || t.Changed>t.Visible || t.MaxAgeMs<0 || t.EdgeAgeMs<0
                || t.LumaSum<0 || t.LumaSum>t.Visible*255))
            throw new ArgumentException("Invalid or nonmonotonic observation");
        sequence=frame.Sequence; timestamp=frame.Timestamp100ns;roi=frame.Roi;
        int size=ObservationBaseline.RegionSize;
        int columns=(frame.Roi.Width+size-1)/size;
        var regions=new List<StaticRegion>();
        foreach(var group in frame.Tiles.GroupBy(t=>((t.Y-frame.Roi.Y)/size)*columns+(t.X-frame.Roi.X)/size)) {
            int gx=group.Key%columns,gy=group.Key/columns;
            var bounds=new PixelRect(frame.Roi.X+gx*size,frame.Roi.Y+gy*size,Math.Min(size,frame.Roi.Width-gx*size),Math.Min(size,frame.Roi.Height-gy*size));
            int visible=group.Sum(t=>t.Visible),edges=group.Sum(t=>t.StableEdges);
            bool observable=frame.Healthy && group.Sum(t=>t.Masked)==0 && visible>0;
            double age=edges>0?group.Max(t=>t.EdgeAgeMs):group.Sum(t=>t.StaticPixels)>=visible*ObservationBaseline.MinimumStableFraction?group.Max(t=>t.MaxAgeMs):0;
            if(!observable) age=history.GetValueOrDefault(group.Key)?.StableMs ?? 0;
            var region=new StaticRegion(group.Key,bounds,age,observable,edges,visible>0?group.Sum(t=>t.LumaSum)/(visible*255.0):0);
            history[group.Key]=region; regions.Add(region);
        }
        Regions=regions;
    }
}
public sealed class CoverageScheduler(int seed,string? displayId=null,long? topologyEpoch=null)
{
    readonly Random random=new(seed);
    readonly Dictionary<int,double> serviceMs=new();
    readonly HashSet<string> receipts=new();
    readonly Dictionary<int,double> retryAfter=new();
    public double WindowServiceMs(int regionId)=>serviceMs.GetValueOrDefault(regionId);
    public StaticRegion? Select(IEnumerable<StaticRegion> regions,OperationContext input,double nowMs)
    {
        var candidates=regions.Where(r=>r.Observable && r.StableMs>=ObservationBaseline.StableMs && (r.StableEdges>0 || r.Brightness>=ObservationBaseline.MinimumBrightness)
            && !input.Blocks(r.Bounds) && retryAfter.GetValueOrDefault(r.Id)<=nowMs).ToArray();
        if(candidates.Length==0) return null;
        double least=candidates.Min(r=>WindowServiceMs(r.Id));
        var fair=candidates.Where(r=>WindowServiceMs(r.Id)<=least+0.001).ToArray();
        return fair[random.Next(fair.Length)];
    }
    public void FailedAttempt(int regionId,double nowMs)=>retryAfter[regionId]=nowMs+250;
    public void Apply(string planId,StaticRegion region,CoverageReceipt receipt)
    {
        if(receipt.PlanId!=planId || receipt.SchemaVersion!="aipet.coverage/0.1"
            || (displayId!=null && receipt.DisplayId!=displayId) || (topologyEpoch!=null && receipt.TopologyEpoch!=topologyEpoch))
            throw new ArgumentException("Receipt identity or version mismatch");
        if(receipts.Contains(planId)) return;
        if(receipts.Count>=4096) throw new InvalidOperationException("Receipt budget exhausted");
        if(receipt.EvidenceLevel=="compositor-submitted-window-observed" && (region.Bounds.Area<=0 || receipt.ClockFrequency<=0
            || receipt.StartTicks<0 || receipt.LastObservedTicks<receipt.StartTicks || receipt.ObservedMs is <0 or >10000
            || !double.IsFinite(receipt.ObservedMs) || !double.IsFinite(receipt.WindowPixelMs)
            || Math.Abs(receipt.ObservedMs-(receipt.LastObservedTicks-receipt.StartTicks)*1000.0/receipt.ClockFrequency)>0.01
            || Math.Abs(receipt.WindowPixelMs-receipt.ActualBounds.Area*receipt.ObservedMs)>0.1))
            throw new ArgumentException("Receipt measurements are inconsistent");
        receipts.Add(planId);
        if(receipt.EvidenceLevel!="compositor-submitted-window-observed" || !receipt.RemovalConfirmed
            || receipt.ObservedMs<0 || !double.IsFinite(receipt.ObservedMs) || receipt.BlendMode!="opaque-black" || receipt.Alpha!=1) return;
        double fraction=Geometry.IntersectionArea(region.Bounds,receipt.ActualBounds)/(double)region.Bounds.Area;
        serviceMs[region.Id]=WindowServiceMs(region.Id)+receipt.ObservedMs*fraction;
        // This is observed window service for fairness, not confirmed OLED dimming dose.
    }
}

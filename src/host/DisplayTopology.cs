using System.Diagnostics;
using System.Text.Json;
namespace AIpet.Host;
public sealed record NativeDisplay(string DisplayId,string DeviceName,PixelRect Bounds,int Rotation,int RefreshNumerator,
    int RefreshDenominator,int ScalePercent,bool Primary,string IdentityQuality,int? AdvancedColorState,int Dpi=96,string? AdapterId=null,uint TargetId=0)
{
    public DisplayScope Scope(long epoch)=>new(DisplayId,epoch,Bounds);
}
public sealed record NativeTopology(string Schema,string Signature,NativeDisplay[] Displays)
{
    public static async Task<NativeTopology> ReadAsync(string gpuExecutable) {
        var info=new ProcessStartInfo(gpuExecutable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add("--topology");
        using var process=Process.Start(info)??throw new InvalidOperationException("Topology adapter did not start");
        var text=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
        catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}throw;}
        string json=await text;
        if(process.ExitCode!=0 || json.Length>262144)throw new InvalidOperationException("Topology read failed: "+await error);
        return JsonSerializer.Deserialize<NativeTopology>(json)??throw new InvalidOperationException("Missing topology");
    }
}
public sealed class TopologyTracker
{
    string? signature;
    NativeDisplay[] displays=Array.Empty<NativeDisplay>();
    public long Epoch {get;private set;}=Stopwatch.GetTimestamp();
    public bool Update(NativeTopology topology) {
        if(topology.Schema!="aipet.topology/1" || topology.Displays.Length>16 || topology.Displays.Any(d=>string.IsNullOrWhiteSpace(d.DisplayId)
            || d.DisplayId.Length>256 || d.Bounds.Width<=0 || d.Bounds.Height<=0) || topology.Displays.Select(d=>d.DisplayId).Distinct().Count()!=topology.Displays.Length)
            throw new ArgumentException("Invalid topology identity or geometry");
        var sorted=topology.Displays.OrderBy(d=>d.DisplayId,StringComparer.Ordinal).ToArray();
        string next=JsonSerializer.Serialize(sorted);if(next==signature)return false;
        signature=next;displays=sorted;Invalidate();return true;
    }
    public void Invalidate()=>Epoch=checked(Epoch+1);
    public NativeDisplay? Find(string id)=>displays.FirstOrDefault(d=>d.DisplayId==id);
}

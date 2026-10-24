using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AIpet.Host;
public sealed class GpuObserver : IAsyncDisposable
{
    readonly Process process;
    readonly object gate=new();
    readonly Task reader;
    readonly Task<string> errors;
    ObservationFrame? latest;
    long arrived;
    int revision;
    public string? Failure {get;private set;}
    public int ExitCode {get;private set;}=-999;
    public ObservationFrame? Latest {get {lock(gate)return latest;}}
    public (double CpuMs,long PrivateBytes,long WorkingSet,int Handles) Counters() {
        process.Refresh();return(process.TotalProcessorTime.TotalMilliseconds,process.PrivateMemorySize64,process.WorkingSet64,process.HandleCount);
    }
    public bool Fresh => Latest!=null && Stopwatch.GetElapsedTime(Interlocked.Read(ref arrived)).TotalMilliseconds<ObservationBaseline.FreshMs && Failure==null && !process.HasExited;
    public GpuObserver(string executable,PixelRect roi,int tileSize=16)
    {
        var info=new ProcessStartInfo(executable) {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        process=Process.Start(info) ?? throw new InvalidOperationException("GPU observer failed to start");
        process.StandardInput.WriteLine(JsonSerializer.Serialize(new {Roi=roi,TileSize=tileSize}));
        errors=process.StandardError.ReadToEndAsync(); reader=ReadAsync();
    }
    async Task ReadAsync()
    {
        try {
            while(await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line) {
                if(line.Length>16_000_000) throw new InvalidOperationException("Observation budget exceeded");
                using var doc=JsonDocument.Parse(line);
                if(doc.RootElement.TryGetProperty("Kind",out _)) continue;
                var frame=JsonSerializer.Deserialize<ObservationFrame>(line) ?? throw new InvalidOperationException("Missing observation");
                lock(gate) latest=frame;
                Interlocked.Exchange(ref arrived,Stopwatch.GetTimestamp());
            }
        } catch(Exception error) {Failure=error.Message;}
    }
    public int SetMasks(params PixelRect[] masks)
    {
        if(masks.Length>16) throw new ArgumentException("Self mask budget exceeded");
        process.StandardInput.WriteLine(JsonSerializer.Serialize(new {Revision=++revision,Masks=masks})); return revision;
    }
    public async Task WaitMasksAsync(int required)
    {
        var start=Stopwatch.GetTimestamp();
        while(!Fresh || Latest!.MaskRevision<required) {
            if(Failure!=null || process.HasExited || Stopwatch.GetElapsedTime(start).TotalMilliseconds>2500)
                throw new InvalidOperationException("Self-mask acknowledgment unavailable: "+Failure);
            await Task.Delay(20);
        }
    }
    public async ValueTask DisposeAsync()
    {
        try {if(!process.HasExited) process.StandardInput.WriteLine("stop"); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
        catch {if(!process.HasExited) {process.Kill();await process.WaitForExitAsync();}}
        await reader; string error=await errors; ExitCode=process.ExitCode;
        if(ExitCode!=0) Failure=error; process.Dispose();
    }
}

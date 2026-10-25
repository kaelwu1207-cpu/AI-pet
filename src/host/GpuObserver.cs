using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace AIpet.Host;
public sealed class GpuUnavailableException(string message):Exception(message);
public sealed class GpuObserver : IAsyncDisposable
{
    readonly Process process;
    readonly object gate=new();
    readonly Task reader;
    readonly Task<string> errors;
    readonly DispatcherTimer? lease;
    ObservationFrame? latest;
    ObservationFrame? first;
    long arrived;
    int revision;
    public string? Failure {get;private set;}
    public int ExitCode {get;private set;}=-999;
    public double TotalCpuMs {get;private set;}
    public bool HasExited=>process.HasExited;
    public int ProcessId=>process.Id;
    public ObservationFrame? Latest {get {lock(gate)return latest;}}
    public ObservationFrame? FirstFrame {get {lock(gate)return first;}}
    public (double CpuMs,long PrivateBytes,long WorkingSet,int Handles) Counters() {
        process.Refresh();return(process.TotalProcessorTime.TotalMilliseconds,process.PrivateMemorySize64,process.WorkingSet64,process.HandleCount);
    }
    public bool Fresh => Latest!=null && Stopwatch.GetElapsedTime(Interlocked.Read(ref arrived)).TotalMilliseconds<ObservationBaseline.FreshMs && Failure==null && !process.HasExited;
    readonly PixelRect roi;
    readonly DisplayScope? scope;
    public GpuObserver(string executable,PixelRect roi,int tileSize=16,DisplayScope? scope=null,string? expectedSignature=null,bool resident=false)
    {
        this.roi=roi;this.scope=scope;
        var info=new ProcessStartInfo(executable) {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        process=Process.Start(info) ?? throw new InvalidOperationException("GPU observer failed to start");
        process.StandardInput.WriteLine(JsonSerializer.Serialize(new {Roi=roi,TileSize=tileSize,DisplayId=scope?.DisplayId,TopologyEpoch=scope?.TopologyEpoch??0,ExpectedSignature=expectedSignature,Resident=resident}));
        errors=process.StandardError.ReadToEndAsync(); reader=ReadAsync();
        if(resident) {lease=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};lease.Tick+=(_,_)=>{try{Write("heartbeat");}catch(GpuUnavailableException){lease.Stop();}};lease.Start();}
    }
    async Task ReadAsync()
    {
        try {
            while(await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line) {
                if(line.Length>16_000_000) throw new InvalidOperationException("Observation budget exceeded");
                using var doc=JsonDocument.Parse(line);
                if(doc.RootElement.TryGetProperty("Kind",out _)) {
                    if(doc.RootElement.TryGetProperty("ExitCode",out var code) && code.GetInt32()!=0)
                        Failure=doc.RootElement.GetProperty("Reason").GetString()??"Capture ended";
                    continue;
                }
                var frame=JsonSerializer.Deserialize<ObservationFrame>(line) ?? throw new InvalidOperationException("Missing observation");
                if(frame.Roi!=roi || (scope!=null && (frame.DisplayId!=scope.DisplayId || frame.TopologyEpoch!=scope.TopologyEpoch)))
                    throw new InvalidOperationException("GPU observation scope mismatch");
                lock(gate) {first??=frame;latest=frame;}
                Interlocked.Exchange(ref arrived,Stopwatch.GetTimestamp());
            }
        } catch(Exception error) {Failure=error.Message;}
    }
    public int SetMasks(params PixelRect[] masks)
    {
        if(masks.Length>16) throw new ArgumentException("Self mask budget exceeded");
        Write(JsonSerializer.Serialize(new {Revision=++revision,Masks=masks})); return revision;
    }
    void Write(string command) {try{process.StandardInput.WriteLine(command);}catch(Exception e) when(e is IOException or InvalidOperationException){throw new GpuUnavailableException(e.Message);}}
    public void RequestRestart()=>Write("restart");
    public async Task WaitMasksAsync(int required,CancellationToken cancellation=default)
    {
        var start=Stopwatch.GetTimestamp();
        while(!Fresh || Latest!.MaskRevision<required) {
            cancellation.ThrowIfCancellationRequested();
            if(Failure!=null || process.HasExited || Stopwatch.GetElapsedTime(start).TotalMilliseconds>2500)
                throw new GpuUnavailableException("Self-mask acknowledgment unavailable: "+Failure);
            await Task.Delay(20);
        }
    }
    public async ValueTask DisposeAsync()
    {
        lease?.Stop();
        try {if(!process.HasExited) process.StandardInput.WriteLine("stop"); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
        catch {if(!process.HasExited) {process.Kill();await process.WaitForExitAsync();}}
        await reader; string error=await errors; ExitCode=process.ExitCode;
        TotalCpuMs=process.TotalProcessorTime.TotalMilliseconds;
        if(ExitCode!=0) Failure=string.IsNullOrWhiteSpace(error)?Failure??$"Capture exit {ExitCode}":error; process.Dispose();
    }
}

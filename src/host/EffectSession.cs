using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;

namespace AIpet.Host;
public sealed class EffectSession : IAsyncDisposable
{
    readonly Process process;
    readonly EffectRequest request;
    readonly DispatcherTimer heartbeat=new() { Interval=TimeSpan.FromMilliseconds(100) };
    readonly Task<CoverageReceipt> completion;
    EffectProcessMetrics metrics=new(0,0,0,0);
    void Sample() {if(process.HasExited)return;process.Refresh();metrics=new(Math.Max(metrics.CpuMs,process.TotalProcessorTime.TotalMilliseconds),
        Math.Max(metrics.PrivatePeakBytes,process.PrivateMemorySize64),Math.Max(metrics.WorkingSetPeakBytes,process.WorkingSet64),Math.Max(metrics.HandlesPeak,process.HandleCount));}
    public IntPtr Hwnd { get; }
    public int ProcessId => process.Id;
    public int ExitCode => process.ExitCode;
    EffectSession(Process child,IntPtr hwnd,EffectRequest effectRequest) {
        process=child; Hwnd=hwnd; request=effectRequest;
        Sample();heartbeat.Tick+=(_,_)=> { try { if(!process.HasExited){Sample();process.StandardInput.WriteLine("beat");} } catch { heartbeat.Stop(); } };
        heartbeat.Start(); completion=CollectAsync();
    }
    public static Task<EffectSession> StartAsync(EffectRequest request) => LaunchAsync(request,false);
    internal static Task<EffectSession> StartHungForTest(EffectRequest request) => LaunchAsync(request,true);
    static async Task<EffectSession> LaunchAsync(EffectRequest request,bool blockDispatcherForTest)
    {
        request.Validate();
        var info=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,RedirectStandardInput=true,
            RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        // Apphost and `dotnet host.dll` are both supported without depending on global PATH.
        if(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        else info.FileName=System.IO.Path.ChangeExtension(typeof(Program).Assembly.Location,".exe");
        info.ArgumentList.Add(blockDispatcherForTest?"--overlay-test-hang":"--overlay");
        var process=Process.Start(info) ?? throw new InvalidOperationException("Overlay process did not start");
        try {
            string nonce=Guid.NewGuid().ToString("N");
            process.StandardInput.WriteLine(JsonSerializer.Serialize(new OverlayLaunch(request,Environment.ProcessId,nonce)));
            var line=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var ready=JsonSerializer.Deserialize<OverlayReady>(line ?? "") ?? throw new InvalidOperationException("No effect readiness");
            if(ready.Nonce!=nonce || ready.Hwnd==0) throw new InvalidOperationException("Effect readiness identity mismatch");
            return new EffectSession(process,new IntPtr(ready.Hwnd),request);
        } catch { if(!process.HasExited) process.Kill(); process.WaitForExit(); process.Dispose(); throw; }
    }
    async Task<CoverageReceipt> CollectAsync()
    {
        var outputTask=process.StandardOutput.ReadToEndAsync();
        var errorTask=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        string output=await outputTask;
        string errors=await errorTask;
        heartbeat.Stop();
        metrics=metrics with{CpuMs=process.TotalProcessorTime.TotalMilliseconds};
        CoverageReceipt? receipt=null;
        try { receipt=JsonSerializer.Deserialize<CoverageReceipt>(output.Trim()); } catch(JsonException) { }
        if(receipt==null || receipt.PlanId!=request.PlanId || receipt.DisplayId!=request.Display.DisplayId
            || receipt.TopologyEpoch!=request.Display.TopologyEpoch) {
            return new("aipet.coverage/0.1",request.PlanId,request.Display.DisplayId,request.Display.TopologyEpoch,
                new(0,0,0,0),0,0,Stopwatch.Frequency,0,0,"unknown",0,"worker-exited-without-receipt",!Native.IsWindow(Hwnd),
                false,"missing","unknown",process.ExitCode,metrics);
        }
        return receipt with { RemovalConfirmed=!Native.IsWindow(Hwnd),WorkerExitCode=process.ExitCode,WorkerMetrics=metrics };
    }
    public Task<CoverageReceipt> Completion => completion;
    public async Task<CoverageReceipt> CancelAsync() {
        if(!process.HasExited) { try { process.StandardInput.WriteLine("cancel"); } catch(System.IO.IOException) { } }
        return await completion;
    }
    internal void StopHeartbeatForTest() => heartbeat.Stop();
    public async ValueTask DisposeAsync() {
        try { await CancelAsync(); }
        finally { heartbeat.Stop(); if(!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); }
    }
}

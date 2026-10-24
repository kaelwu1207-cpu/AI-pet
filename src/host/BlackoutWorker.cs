using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIpet.Host;

// Dedicated process: a blocked pet dispatcher cannot hold a black window indefinitely.
public static class BlackoutWorker
{
    public static int Run(bool blockDispatcherForTest=false)
    {
        var launch=JsonSerializer.Deserialize<OverlayLaunch>(Console.ReadLine() ?? "") ?? throw new ArgumentException("Missing launch");
        launch.Request.Validate();
        Native.ValidateDisplay(launch.Request.Display);
        using var parent=Process.GetProcessById(launch.ParentPid);
        _=parent.SafeHandle; // Hold the original process identity, including after PID reuse.
        var request=launch.Request;
        var foreground=Native.GetForegroundWindow();
        var dispatcher=Dispatcher.CurrentDispatcher;
        var window=new Window { WindowStyle=WindowStyle.None, AllowsTransparency=true, Background=Brushes.Black,
            ShowInTaskbar=false, ShowActivated=false, ResizeMode=ResizeMode.NoResize,
            Width=request.Bounds.Width,Height=request.Bounds.Height };
        IntPtr hwnd=IntPtr.Zero;
        long started=0,lastObserved=0,heartbeat=Stopwatch.GetTimestamp(),boot=heartbeat;
        PixelRect actual=request.Bounds;
        int finished=0,focusChanged=0,exitCode=0;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(40) };
        CoverageReceipt Receipt(string reason,bool removed) {
            long end=Interlocked.Read(ref lastObserved),begin=Interlocked.Read(ref started);
            double ms=begin>0?Math.Max(0,(end-begin)*1000.0/Stopwatch.Frequency):0;
            return new("aipet.coverage/0.1",request.PlanId,request.Display.DisplayId,request.Display.TopologyEpoch,
                actual,begin,end,Stopwatch.Frequency,ms,actual.Area*ms,"opaque-black",1,reason,removed,
                Volatile.Read(ref focusChanged)==0,"compositor-submitted-window-observed","unknown-external-occlusion");
        }
        void Abort(string reason) {
            if(Interlocked.Exchange(ref finished,1)!=0) return;
            // Pixel-time stops at the last healthy UI observation, never at the requested deadline.
            try { Console.WriteLine(JsonSerializer.Serialize(Receipt(reason,false))); Console.Out.Flush(); }
            finally { Environment.Exit(3); } // Broken parent stdout must never prevent OS window cleanup.
        }
        void Finish(string reason) {
            if(Interlocked.Exchange(ref finished,1)!=0) return;
            timer.Stop(); window.Close(); Native.Flush();
            Console.WriteLine(JsonSerializer.Serialize(Receipt(reason,!Native.IsWindow(hwnd))));
            Console.Out.Flush(); dispatcher.InvokeShutdown();
        }
        using var watchdog=new System.Threading.Timer(_ => {
            try {
                if(parent.HasExited) Abort("parent-exited");
                else if(Interlocked.Read(ref started)>0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref heartbeat)).TotalMilliseconds>900) Abort("heartbeat-expired");
                else if(Interlocked.Read(ref started)>0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds>request.DurationMs+1000) Abort("hard-deadline");
                else if(Interlocked.Read(ref started)==0 && Stopwatch.GetElapsedTime(boot).TotalMilliseconds>3000) Abort("startup-deadline");
            } catch { Abort("watchdog-error"); }
        },null,100,50);
        _=Task.Run(async () => {
            while(true) {
                var line=await Console.In.ReadLineAsync();
                if(line=="beat") Interlocked.Exchange(ref heartbeat,Stopwatch.GetTimestamp());
                else { _=dispatcher.BeginInvoke(new Action(()=>Finish(line=="cancel"?"cancelled":"parent-eof"))); return; }
            }
        });
        timer.Tick+=(_,_)=> {
            try {
                Native.ValidateDisplay(request.Display);
                if(Native.GetForegroundWindow()!=foreground) Interlocked.Exchange(ref focusChanged,1);
                if(!Native.IsWindowVisible(hwnd) || Native.Bounds(hwnd)!=actual) { Finish("window-state-changed"); return; }
                Native.Flush(); Interlocked.Exchange(ref lastObserved,Stopwatch.GetTimestamp());
                if(Stopwatch.GetElapsedTime(started).TotalMilliseconds>=request.DurationMs) Finish("duration");
            } catch { Abort("render-error"); }
        };
        dispatcher.BeginInvoke(new Action(async () => {
            try {
                Native.MakePassive(window,true); hwnd=Native.Handle(window);
                window.Show(); Native.Place(hwnd,request.Bounds);
                await Task.Delay(60); Native.Flush(); actual=Native.Bounds(hwnd);
                if(actual!=request.Bounds || !Native.IsWindowVisible(hwnd)) throw new InvalidOperationException("Effect geometry mismatch");
                if(Native.GetForegroundWindow()!=foreground) Interlocked.Exchange(ref focusChanged,1);
                Interlocked.Exchange(ref started,Stopwatch.GetTimestamp()); Interlocked.Exchange(ref lastObserved,started);
                Console.WriteLine(JsonSerializer.Serialize(new OverlayReady(launch.Nonce,hwnd.ToInt64()))); Console.Out.Flush();
                timer.Start();
                if(blockDispatcherForTest) Thread.Sleep(Timeout.Infinite);
            } catch { Abort("initialization-error"); }
        }));
        Dispatcher.Run();
        return exitCode;
    }
}

using AIpet.Host;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;
public static class ResidentTests
{
    public static async Task Run(string root,string gpu,string output)
    {
        var assets=AssetPacks.Load(root,"green","wander");
        var topology=await NativeTopology.ReadAsync(gpu);var display=topology.Displays.Single(d=>d.Primary);
        using(var preview=new VPetActor(display.Scope(1),new(display.Bounds.X+200,display.Bounds.Y+200,128,128),System.IO.Path.Combine(output,"matrix-cache"))) {
            foreach(var skinId in new[]{"green","pink"})foreach(var motionId in new[]{"wander","rest"}) {
                var combo=AssetPacks.Load(root,skinId,motionId);preview.SetSkin(combo.Skin);preview.Play(combo.Animation);await Task.Delay(220);
                var visible=preview.Pet.PetGrid.Visibility==Visibility.Visible?preview.Pet.PetGrid:preview.Pet.PetGrid2;
                Check(((VPet_Simulator.Core.Picture)visible.Tag).Path==combo.Skin.Poses[preview.CurrentPose!],"formal VPet rendering "+skinId+" / "+motionId);
            }
        }
        var roi=new PixelRect(display.Bounds.X+500,display.Bounds.Y+500,320,128);
        var fixture=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.White};
        Native.MakePassive(fixture,false);fixture.Show();Native.Place(Native.Handle(fixture),roi);
        var plan=new LoopPlan(display.Scope(1),display.Bounds,new(display.Bounds.X+200,display.Bounds.Y+200,128,128),assets.Skin,assets.Animation,EffectMs:1000,TargetBounds:roi);
        var active=new TaskCompletionSource<long>();
        await using var host=new ResidentHost(plan,gpu,output,minimumActionIntervalMs:600,
            inputSource:()=>Native.ReadOperationContext() with{RecentInput=false},traceSource:e=>{if(e.Phase=="effect-active")active.TrySetResult(e.WindowHandle);});
        var before=Native.GetForegroundWindow();
        await host.ResumeAsync();bool launchPreserved=Native.GetForegroundWindow()==before;await Wait(()=>host.Snapshot.Frames>12);
        long epoch=host.Snapshot.Epoch;long black=await active.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var pauseForeground=Native.GetForegroundWindow();var latency=Stopwatch.StartNew();await host.PauseAsync();
        bool pausePreserved=Native.GetForegroundWindow()==pauseForeground;
        Check(!Native.IsWindow(new IntPtr(black)) && host.LastAction?.Coverage?.RemovalConfirmed==true && latency.ElapsedMilliseconds<1500,"pause during real blackout confirms removal within 1500 ms");
        Check(host.State==ResidentState.Paused && host.ActivePetWindow==IntPtr.Zero,"pause removes pet, capture and effect before acknowledgment");
        var resumeForeground=Native.GetForegroundWindow();await host.ResumeAsync();bool resumePreserved=Native.GetForegroundWindow()==resumeForeground;
        await Wait(()=>host.Snapshot.Frames>12);
        Check(host.Snapshot.Epoch>epoch,"resume refreshes capture history and epoch");
        var hwnd=host.ActivePetWindow;
        Check((Native.GetWindowLongPtr(hwnd,-20).ToInt64()&Native.Transparent)!=0,"resident pet is click-through");
        Check(launchPreserved && pausePreserved && resumePreserved,"each resident lifecycle transition preserves foreground at its acknowledgment");
        int generation=host.Snapshot.Generations;host.RestartCapture();
        await Wait(()=>host.Snapshot.Generations>generation && host.Snapshot.Frames>12);
        Check(host.State==ResidentState.Running,"resident recovers an actual capture process restart");
        await host.ExitAsync();
        Check(host.State==ResidentState.Stopped && !Native.IsWindow(hwnd),"exit settles owned resources");
        fixture.Close();
    }
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static async Task Wait(Func<bool> ready){var sw=Stopwatch.StartNew();while(!ready()){if(sw.ElapsedMilliseconds>8000)throw new Exception("resident timeout");await Task.Delay(40);}}
    static async Task Lease(string gpu) {
        var topology=await NativeTopology.ReadAsync(gpu);var display=topology.Displays.Single(d=>d.Primary);
        var start=new ProcessStartInfo(gpu){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using var child=Process.Start(start)!;var text=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
        child.StandardInput.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Roi=display.Bounds,DisplayId=display.DisplayId,TileSize=64,Resident=true}));
        try {await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6));}
        finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
        Check(child.ExitCode==3 && (await text).Contains("host-lease-expired") && string.IsNullOrEmpty(await errors),"resident capture expires when host stops renewing lease");
    }
    [STAThread]public static int Main(string[] args){Native.EnableDpi();return HostRun.RunDispatcher(()=>args[0]=="--lease"?Lease(args[1]):Run(args[0],args[1],args[2]));}
}

using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace AIpet.Host;
public sealed record ResidentPlan(string AssetsRoot,string Skin="green",string Motion="wander",string? DisplayId=null,int ActionIntervalMs=5000);
public static class ResidentTray
{
    public static async Task RunAsync(ResidentPlan config,string gpu,string output)
    {
        var assets=AssetPacks.Load(config.AssetsRoot,config.Skin,config.Motion);
        var topology=await NativeTopology.ReadAsync(gpu);
        var display=config.DisplayId==null?topology.Displays.Single(d=>d.Primary):topology.Displays.Single(d=>d.DisplayId==config.DisplayId);
        var b=display.Bounds;
        var plan=new LoopPlan(display.Scope(1),b,new(b.X+Math.Min(200,b.Width-128),b.Y+Math.Min(200,b.Height-128),128,128),assets.Skin,assets.Animation);
        await using var host=new ResidentHost(plan,gpu,output,config.ActionIntervalMs);
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var menu=new Forms.ContextMenuStrip();
        using var icon=new Forms.NotifyIcon{Icon=SystemIcons.Application,Text="AI-pet · 正在启动",ContextMenuStrip=menu,Visible=true};
        var pause=menu.Items.Add("暂停");var resume=menu.Items.Add("继续");menu.Items.Add(new Forms.ToolStripSeparator());var exit=menu.Items.Add("退出");
        async Task Command(string command) {
            try {
                switch(command) {
                    case "pause":await host.PauseAsync();break;
                    case "resume":await host.ResumeAsync();break;
                    case "exit":await host.ExitAsync();done.TrySetResult();break;
                    case "status":Console.WriteLine(JsonSerializer.Serialize(host.Snapshot));break;
                }
            }catch(Exception e){Console.Error.WriteLine(e.Message);}
        }
        pause.Click+=async(_,_)=>await Command("pause");resume.Click+=async(_,_)=>await Command("resume");exit.Click+=async(_,_)=>await Command("exit");
        host.Changed+=s=>{icon.Text="AI-pet · "+(s.State switch{ResidentState.Running=>"运行中",ResidentState.Paused=>"已暂停",ResidentState.Faulted=>"需要恢复",ResidentState.WaitingDisplay=>"等待显示器",_=>"正在处理"});
            pause.Enabled=s.State is ResidentState.Running or ResidentState.WaitingDisplay or ResidentState.Starting;
            resume.Enabled=s.State is ResidentState.Paused or ResidentState.Faulted;};
        var dispatcher=Dispatcher.CurrentDispatcher;
        // Read only redirected development control. Closing its owner channel exits the child host.
        if(Console.IsInputRedirected)_=Task.Run(async()=>{while(await Console.In.ReadLineAsync() is {} line){if(line.Length>32)break;
            await dispatcher.InvokeAsync(()=>Command(line)).Task.Unwrap();if(line=="exit")return;}
            await dispatcher.InvokeAsync(()=>Command("exit")).Task.Unwrap();});
        await host.ResumeAsync();await done.Task;icon.Visible=false;
    }
}

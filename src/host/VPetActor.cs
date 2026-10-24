using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LinePutScript;
using VPet_Simulator.Core;

namespace AIpet.Host;
public sealed record SkinDefinition(string Id, IReadOnlyDictionary<string, string> Poses);
public sealed record AnimationStep(string Pose, int DurationMs);
public sealed record AnimationClip(string Id, IReadOnlyList<AnimationStep> Steps);

// Binding and sequencing only: images and display transitions remain in VPet Core.
public sealed class VPetActor : IDisposable
{
    readonly GraphCore graph;
    readonly DispatcherTimer animationTimer = new();
    readonly Dictionary<string, Picture> pictures = new();
    SkinDefinition? skin;
    AnimationClip? clip;
    int frame;
    public Window Window { get; }
    public VPet_Simulator.Core.Main Pet { get; }
    public TargetController Motion { get; }
    public string? SkinId => skin?.Id;
    public string? AnimationId => clip?.Id;
    public VPetActor(DisplayScope scope, PixelRect bounds, string cacheRoot)
    {
        GraphCore.CachePath = cacheRoot;
        graph = new GraphCore(500, Dispatcher.CurrentDispatcher, new GraphCore.Config(new LpsDocument()));
        Window = new Window { Width=bounds.Width, Height=bounds.Height, WindowStyle=WindowStyle.None,
            AllowsTransparency=true, Background=Brushes.Transparent, ShowInTaskbar=false,
            ShowActivated=false, ResizeMode=ResizeMode.NoResize };
        Motion = new TargetController(Window, scope);
        Pet = new VPet_Simulator.Core.Main(new GameCore { Controller=Motion, Graph=graph, Save=new GameSave() });
        Motion.Attach(Pet); Window.Content=Pet;
        Native.MakePassive(Window, false); Window.Show(); Native.Place(Native.Handle(Window), bounds);
        animationTimer.Tick += (_, _) => { frame=(frame+1)%clip!.Steps.Count; ShowFrame(); };
    }
    public void SetSkin(SkinDefinition next)
    {
        if (string.IsNullOrWhiteSpace(next.Id) || next.Poses.Count is < 1 or > 64 || next.Poses.Values.Any(p => !File.Exists(p)))
            throw new ArgumentException("Resolved skin requires existing image paths");
        if (clip != null && clip.Steps.Any(s => !next.Poses.ContainsKey(s.Pose))) throw new ArgumentException("Skin lacks clip poses");
        // IDs identify immutable resolved resources for this actor lifetime.
        if (next.Poses.Any(p => pictures.TryGetValue(next.Id+"/"+p.Key,out var cached) && cached.Path != Path.GetFullPath(p.Value)))
            throw new ArgumentException("Changed assets require a new generation ID");
        skin=new(next.Id,new Dictionary<string,string>(next.Poses)); if (clip != null) ShowFrame();
    }
    public void Play(AnimationClip next)
    {
        if (next.Steps.Count is < 1 or > 128 || skin == null || next.Steps.Any(s => s.DurationMs is < 50 or > 5000 || !skin.Poses.ContainsKey(s.Pose)))
            throw new ArgumentException("Clip requires supported poses and bounded frame intervals");
        clip=next; frame=0; ShowFrame(); animationTimer.Start();
    }
    void ShowFrame()
    {
        var step=clip!.Steps[frame]; string key=skin!.Id+"/"+step.Pose;
        if (!pictures.TryGetValue(key,out var picture)) {
            if (pictures.Count >= 256) throw new InvalidOperationException("Actor image budget exceeded");
            picture=new Picture(graph,Path.GetFullPath(skin.Poses[step.Pose]),new GraphInfo(key),10000);
            pictures.Add(key,picture); graph.AddGraph(picture);
        }
        Pet.Display(picture);
        if (Motion.IsMoving) Pet.DisplayType=new GraphInfo("external-target",GraphInfo.GraphType.Move);
        animationTimer.Interval=TimeSpan.FromMilliseconds(step.DurationMs);
    }
    public void Dispose() { animationTimer.Stop(); Motion.Dispose(); Pet.Dispose(); Window.Close(); graph.Dispose(); }
}

public sealed class TargetController(Window window, DisplayScope scope) : IController, IDisposable
{
    VPet_Simulator.Core.Main pet = null!;
    readonly DispatcherTimer deadlineTimer = new() { Interval=TimeSpan.FromMilliseconds(20) };
    MotionRequest? request;
    TaskCompletionSource<MotionReceipt>? completion;
    CancellationToken cancellation;
    long started, previous;
    double positionX, positionY;
    int pending;
    public bool IsMoving => request != null;
    public void Attach(VPet_Simulator.Core.Main value) { pet=value; pet.Load_3_BindingTimer(); deadlineTimer.Tick+=(_,_)=>CheckDeadline(); }
    public Task<MotionReceipt> MoveToAsync(MotionRequest next, CancellationToken token=default)
    {
        window.Dispatcher.VerifyAccess();
        var bounds=Native.Bounds(Native.Handle(window)); next.Validate(scope,bounds.Width,bounds.Height);
        if (IsMoving) throw new InvalidOperationException("Cancel or finish the active plan before replacing it");
        request=next; cancellation=token; completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        started=previous=Stopwatch.GetTimestamp();
        positionX=bounds.X; positionY=bounds.Y;
        pet.CleanState(); pet.DisplayType=new GraphInfo("external-target",GraphInfo.GraphType.Move);
        pet.MoveTimerSmartMove=true; pet.MoveTimerPoint=new Point(0,0); pet.MoveTimer.Interval=16;
        deadlineTimer.Start(); pet.MoveTimer.Start(); return completion.Task;
    }
    public void MoveWindows(double x,double y)
    {
        if (!IsMoving) { pet.MoveTimer.Stop(); return; }
        if (Interlocked.Exchange(ref pending,1)!=0) return;
        window.Dispatcher.BeginInvoke(new Action(() => {
            try { Advance(); } catch { Finish("failed"); } finally { Volatile.Write(ref pending,0); }
        }));
    }
    void Advance()
    {
        if (request == null) return;
        if (CheckDeadline()) return;
        var actual=Native.Bounds(Native.Handle(window)); long now=Stopwatch.GetTimestamp();
        double dt=Math.Clamp((now-previous)/(double)Stopwatch.Frequency,0,0.064); previous=now;
        double dx=request.X-positionX, dy=request.Y-positionY, distance=Math.Sqrt(dx*dx+dy*dy);
        double step=request.PixelsPerSecond*dt;
        positionX=distance<=step?request.X:positionX+dx/distance*step;
        positionY=distance<=step?request.Y:positionY+dy/distance*step;
        Native.Place(Native.Handle(window),actual with { X=(int)Math.Round(positionX),Y=(int)Math.Round(positionY) });
        if (distance<=step) Finish("completed");
    }
    bool CheckDeadline()
    {
        if (request == null) return true;
        if (cancellation.IsCancellationRequested) { Finish("cancelled"); return true; }
        if (Stopwatch.GetElapsedTime(started).TotalMilliseconds>=request.DeadlineMs) { Finish("deadline"); return true; }
        return false;
    }
    void Finish(string status)
    {
        if (request == null) return;
        var receipt=new MotionReceipt(request.PlanId,status,Native.Bounds(Native.Handle(window)),Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        request=null; deadlineTimer.Stop(); pet.MoveTimer.Stop(); pet.MoveTimerSmartMove=false;
        completion!.TrySetResult(receipt);
    }
    public void Dispose() { Finish("disposed"); deadlineTimer.Stop(); }
    public double GetWindowsDistanceLeft()=>Native.Bounds(Native.Handle(window)).X-scope.Bounds.X;
    public double GetWindowsDistanceRight()=>scope.Bounds.X+scope.Bounds.Width-Native.Bounds(Native.Handle(window)).X;
    public double GetWindowsDistanceUp()=>Native.Bounds(Native.Handle(window)).Y-scope.Bounds.Y;
    public double GetWindowsDistanceDown()=>scope.Bounds.Y+scope.Bounds.Height-Native.Bounds(Native.Handle(window)).Y;
    public double ZoomRatio=>1; public int PressLength=>500; public bool EnableFunction=>false;
    public int InteractionCycle=>1000; public bool RePositionActive {get;set;}
    public void ShowPanel(){} public void ResetPosition(){} public bool CheckPosition()=>false;
}

namespace AIpet.Host;

public sealed record PixelRect(int X, int Y, int Width, int Height)
{
    public bool Contains(PixelRect other) => Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
        && other.X >= X && other.Y >= Y && (long)other.X + other.Width <= (long)X + Width
        && (long)other.Y + other.Height <= (long)Y + Height;
    public long Area => (long)Width * Height;
}
public sealed record DisplayScope(string DisplayId, long TopologyEpoch, PixelRect Bounds);
public sealed record MotionRequest(string PlanId, string DisplayId, long TopologyEpoch,
    int X, int Y, double PixelsPerSecond, int DeadlineMs)
{
    public void Validate(DisplayScope scope, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(PlanId) || PlanId.Length>128 || DisplayId != scope.DisplayId || TopologyEpoch != scope.TopologyEpoch
            || !double.IsFinite(PixelsPerSecond) || PixelsPerSecond is < 1 or > 2000 || DeadlineMs is < 50 or > 15000
            || !scope.Bounds.Contains(new(X, Y, width, height))) throw new ArgumentException("Invalid motion target, budget or topology");
    }
}
public sealed record MotionReceipt(string PlanId, string Status, PixelRect ActualBounds, double ElapsedMs);
public sealed record EffectRequest(string PlanId, DisplayScope Display, PixelRect Bounds, int DurationMs, string EffectId = "blackout")
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PlanId) || PlanId.Length>128 || string.IsNullOrWhiteSpace(Display.DisplayId) || Display.DisplayId.Length>256 || Display.TopologyEpoch < 0
            || !Display.Bounds.Contains(Bounds) || Bounds.Area > 640 * 640 || DurationMs is < 100 or > 5000 || EffectId != "blackout")
            throw new ArgumentException("Invalid local effect bounds, budget or capability");
    }
}
public sealed record OverlayLaunch(EffectRequest Request, int ParentPid, string Nonce);
public sealed record OverlayReady(string Nonce, long Hwnd);
public sealed record CoverageReceipt(string SchemaVersion, string PlanId, string DisplayId, long TopologyEpoch,
    PixelRect ActualBounds, long StartTicks, long LastObservedTicks, long ClockFrequency,
    double ObservedMs, double WindowPixelMs, string BlendMode, double Alpha, string EndReason,
    bool RemovalConfirmed, bool FocusPreserved, string EvidenceLevel, string OcclusionStatus, int? WorkerExitCode=null);

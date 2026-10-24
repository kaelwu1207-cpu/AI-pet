using System.Text.Json;
namespace AIpet.Host;
public static class ObservationBaseline
{
    static readonly JsonDocument Document=JsonDocument.Parse(typeof(ObservationBaseline).Assembly.GetManifestResourceStream("AIpet.StaticBaseline")!);
    static JsonElement Root=>Document.RootElement;
    public static string Schema=>Root.GetProperty("Schema").GetString()!;
    public static int StableMs=>Root.GetProperty("StableMs").GetInt32();
    public static int FreshMs=>Root.GetProperty("FreshMs").GetInt32();
    public static int RegionSize=>Root.GetProperty("RegionSize").GetInt32();
    public static int MaximumPixels=>Root.GetProperty("MaximumPixels").GetInt32();
    public static double MinimumBrightness=>Root.GetProperty("MinimumBrightness").GetDouble();
    public static double MinimumStableFraction=>Root.GetProperty("MinimumStableFraction").GetDouble();
    public static JsonElement Fixture=>Root.GetProperty("Fixture");
}

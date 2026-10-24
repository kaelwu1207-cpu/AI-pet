namespace AIpet.Host;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        Native.EnableDpi();
        Console.Error.WriteLine("Host integration entry is being initialized");
        return 1;
    }
}

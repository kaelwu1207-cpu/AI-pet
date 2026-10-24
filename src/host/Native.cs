using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIpet.Host;
public static class Native
{
    public const int Transparent = 0x20, NoActivate = 0x08000000, ToolWindow = 0x80, Layered = 0x80000;
    public static void EnableDpi() => SetProcessDpiAwarenessContext(new IntPtr(-4));
    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();
    public static void MakePassive(Window window, bool clickThrough)
    {
        var hwnd = Handle(window);
        long flags = GetWindowLongPtr(hwnd, -20).ToInt64() | NoActivate | ToolWindow;
        if (clickThrough) flags |= Transparent | Layered;
        SetWindowLongPtr(hwnd, -20, new IntPtr(flags));
        HwndSource.FromHwnd(hwnd)!.AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) => {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (clickThrough && message == 0x84) { handled = true; return new IntPtr(-1); }
            return IntPtr.Zero;
        });
    }
    public static PixelRect Bounds(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) throw new System.ComponentModel.Win32Exception();
        return new(r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top);
    }
    public static void Place(IntPtr hwnd, PixelRect rect, bool topmost = true)
    {
        if (!SetWindowPos(hwnd, topmost ? new IntPtr(-1) : IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, 0x10 | 0x40))
            throw new System.ComponentModel.Win32Exception();
    }
    public static void Flush() { int result = DwmFlush(); if (result < 0) Marshal.ThrowExceptionForHR(result); }
    public static void ValidateDisplay(DisplayScope scope)
    {
        var bounds=scope.Bounds;
        var rect=new Rect { Left=bounds.X,Top=bounds.Y,Right=checked(bounds.X+bounds.Width),Bottom=checked(bounds.Y+bounds.Height) };
        var monitor=MonitorFromRect(ref rect,0);
        var info=new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>() };
        if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info)
            || bounds != new PixelRect(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top))
            throw new ArgumentException("Display bounds no longer match an active physical monitor");
    }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; public Point(int x,int y) { X=x; Y=y; } }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Rect rect,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll",SetLastError=true)] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
}

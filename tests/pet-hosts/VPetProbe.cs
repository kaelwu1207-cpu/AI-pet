using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LinePutScript;
using VPet_Simulator.Core;

// Real upstream core + synthetic owned PNG resources; no stock character redistribution.
class VPetProbe
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Pump(int ms) {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Fixture(string path, Color color) {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { color.B, color.G, color.R, color.A }, 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    static byte[] Pixel(Visual visual) {
        var bitmap = new RenderTargetBitmap(500, 500, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(100,100,1,1), pixel, 4, 0); return pixel;
    }
    [STAThread] static int Main(string[] args) {
        try {
            Directory.CreateDirectory(args[0]);
            GraphCore.CachePath = System.IO.Path.Combine(args[0], "cache");
            string red = System.IO.Path.Combine(args[0], "skin-red.png"), blue = System.IO.Path.Combine(args[0], "skin-blue.png");
            Fixture(red, Colors.Red); Fixture(blue, Colors.Blue);
            using var graph = new GraphCore(500, Dispatcher.CurrentDispatcher, new GraphCore.Config(new LpsDocument()));
            var host = new Window { Width=200, Height=200, Left=120, Top=120, WindowStyle=WindowStyle.None,
                AllowsTransparency=true, Background=Brushes.Transparent, ShowInTaskbar=false, ShowActivated=false };
            var controller = new Controller(host);
            using var pet = new VPet_Simulator.Core.Main(new GameCore { Controller=controller, Graph=graph, Save=new GameSave() });
            host.Content=pet; host.Show(); Pump(100);
            using var first = new Picture(graph, red, new GraphInfo("skin-red"), 10000);
            using var second = new Picture(graph, blue, new GraphInfo("skin-blue"), 10000);
            graph.AddGraph(first); graph.AddGraph(second);
            pet.Display(first); Pump(100);
            var a = Pixel(pet.PetGrid.Visibility == Visibility.Visible ? pet.PetGrid : pet.PetGrid2);
            Check(a[2] > 240 && a[0] < 10, "upstream Picture renders first resource");
            pet.Display(second); Pump(100);
            var b = Pixel(pet.PetGrid.Visibility == Visibility.Visible ? pet.PetGrid : pet.PetGrid2);
            Check(b[0] > 240 && b[2] < 10, "Main.Display replaces resource without rebuilding host");
            pet.Load_3_BindingTimer(); pet.DisplayType = new GraphInfo("probe-move", GraphInfo.GraphType.Move);
            pet.MoveTimerSmartMove=true; pet.MoveTimerPoint=new Point(4, 0); pet.MoveTimer.Interval=20;
            double before=host.Left; pet.MoveTimer.Start(); Pump(240); pet.MoveTimer.Stop();
            int calls=controller.Calls; Check(calls >= 3 && host.Left > before, "upstream MoveTimer drives IController and native window");
            pet.MoveTimerSmartMove=false; Pump(80);
            Check(controller.Calls == calls, "external movement pause settles");
            var effect = new Rectangle { Width=180, Height=180, Fill=Brushes.Black, IsHitTestVisible=false,
                HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top };
            pet.UIGrid.Children.Add(effect); Pump(50);
            var black=Pixel(pet.UIGrid);
            Check(black[0]==0 && black[1]==0 && black[2]==0 && black[3]==255, "independent UIGrid effect renders opaque black");
            pet.UIGrid.Children.Remove(effect); Pump(30);
            Check(pet.UIGrid.Children.Count==0 && second.Control != null, "effect removed independently of skin");
            host.Close();
            Console.WriteLine("RESULT passed; single-screen WPF core integration; no mouse or multi-screen acceptance");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    sealed class Controller(Window window) : IController {
        public int Calls;
        public void MoveWindows(double x,double y) => window.Dispatcher.Invoke(() => { window.Left+=x; window.Top+=y; Calls++; });
        public double GetWindowsDistanceLeft()=>1000; public double GetWindowsDistanceRight()=>1000;
        public double GetWindowsDistanceUp()=>1000; public double GetWindowsDistanceDown()=>1000;
        public double ZoomRatio=>1; public int PressLength=>500; public bool EnableFunction=>false;
        public int InteractionCycle=>1000; public bool RePositionActive {get;set;}
        public void ShowPanel(){} public void ResetPosition(){} public bool CheckPosition()=>false;
    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using System.Xml;
using DesktopPet;

// Load the real upstream XML parser with two synthetic sprite sheets.
class ESheepResourceProbe
{
    static BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Field(object o,string n) { return o.GetType().GetField(n,F).GetValue(o); }
    static object Call(object o,string n,params object[] a) { return o.GetType().GetMethod(n,F).Invoke(o,a); }
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name); Console.WriteLine("PASS "+name); }
    static string Resource(string template,Color color) {
        var doc=new XmlDocument(); doc.LoadXml(template);
        using(var bitmap=new Bitmap(640,440)) using(var g=Graphics.FromImage(bitmap)) using(var stream=new MemoryStream()) {
            g.Clear(color); bitmap.Save(stream,ImageFormat.Png);
            doc.GetElementsByTagName("png",doc.DocumentElement.NamespaceURI)[0].InnerText=Convert.ToBase64String(stream.ToArray());
        }
        return doc.OuterXml;
    }
    [STAThread] static int Main(string[] args) {
        try {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            var assembly=typeof(FormPet).Assembly;
            var program=assembly.GetType("DesktopPet.Program");
            program.GetField("Mainthread",F).SetValue(null,FormatterServices.GetUninitializedObject(assembly.GetType("DesktopPet.StartUp")));
            var data=program.GetField("MyData",F).GetValue(null);
            Call(data,"SetScale",1); Call(data,"SetVolume",0.0); Call(data,"SetWindowForeground",false);
            string template=File.ReadAllText(args[0]);
            foreach(var color in new[]{Color.Red,Color.Blue}) {
                Call(data,"SetXml",Resource(template,color),"aipet-synthetic");
                using(var xml=new DesktopPet.Xml(1)) {
                    Check(xml.ReadXML(),"upstream parser loads replacement XML sprite sheet "+color.Name);
                    Check(xml.sprites[0].GetPixel(10,10).ToArgb()==color.ToArgb(),"parsed sprite color "+color.Name);
                    var animations=new Animations(xml);
                    var animation=new TAnimation("probe",900);
                    animation.Start.Interval.Value=animation.End.Interval.Value=200;
                    animation.Start.Opacity=animation.End.Opacity=1;
                    animation.Sequence.Frames.Add(0); animation.Sequence.TotalSteps=1;
                    animation.EndAnimation.Add(new TNextAnimation(900,1,TNextAnimation.TOnly.NONE));
                    animations.SaveAnimation(animation,900);
                    using(var pet=new FormPet(animations,xml)) {
                        foreach(var sprite in xml.sprites) pet.AddImage(sprite);
                        pet.Show(xml.spriteWidth,xml.spriteHeight); pet.Location=new Point(150,150); pet.Opacity=1;
                        ((Timer)Field(pet,"timer1")).Stop();
                        pet.GetType().GetField("CurrentAnimation",F).SetValue(pet,animation);
                        Call(pet,"SetNewAnimation",900);
                        pet.GetType().GetField("AnimationStep",F).SetValue(pet,0);
                        Call(pet,"NextStep"); Application.DoEvents();
                        var image=(Bitmap)((PictureBox)Field(pet,"pictureBox1")).Image;
                        Check(image.GetPixel(10,10).ToArgb()==color.ToArgb(),"real pet publishes replacement sprite "+color.Name);
                    }
                }
            }
            Console.WriteLine("RESULT passed; XML reload via new pet instance; private animation entry requires adapter"); return 0;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

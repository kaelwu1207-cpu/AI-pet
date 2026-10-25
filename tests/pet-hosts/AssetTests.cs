using AIpet.Host;
using System.IO;
using System.Text.Json;
public static class AssetTests
{
    public static void Run(string root)
    {
        var green=AssetPacks.Load(root,"green","wander");
        var pink=AssetPacks.Load(root,"pink","wander");
        var blink=AssetPacks.Load(root,"green","rest");
        if(green.Animation!=pink.Animation && JsonSerializer.Serialize(green.Animation)!=JsonSerializer.Serialize(pink.Animation))throw new Exception("Skin changed animation");
        if(JsonSerializer.Serialize(green.Skin)!=JsonSerializer.Serialize(blink.Skin))throw new Exception("Animation changed skin");
        foreach(var skin in new[]{green.Skin,pink.Skin})foreach(var clip in new[]{green.Animation,blink.Animation})
            if(clip.Steps.Any(s=>!skin.Poses.ContainsKey(s.Pose)))throw new Exception("Binding mismatch");
        bool rejected=false;try{AssetPacks.Load(root,"../green","rest");}catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("Traversal accepted");
        Console.WriteLine("PASS licensed two skins by two animations, independent contracts and traversal rejection");
    }
}


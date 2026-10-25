using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
namespace AIpet.Host;
public sealed record AssetFrame(string Path,string Sha256,long Bytes);
public sealed record SkinManifest(string Schema,string Id,string Version,string Rig,string Author,string Source,string License,
    Dictionary<string,AssetFrame> Poses);
public sealed record MotionManifest(string Schema,string Id,string Version,string Rig,AnimationStep[] Steps);
public sealed record ResolvedAssets(SkinDefinition Skin,AnimationClip Animation);
// Trusted engine renders data-only PNG resources through VPet Picture; packages contain no executable code.
public static class AssetPacks
{
    static readonly JsonSerializerOptions Json=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    static void Id(string value) {if(!Regex.IsMatch(value,"^[a-z][a-z0-9-]{0,47}$"))throw new ArgumentException("Invalid package identity");}
    static string Inside(string root,string relative)
    {
        if(Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/','\\').Any(s=>s is ".." or "." or ""))throw new ArgumentException("Invalid package path");
        root=Path.GetFullPath(root);string file=Path.GetFullPath(Path.Combine(root,relative));
        if(!file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Package path escaped");
        for(string? p=file;p!=null;p=Path.GetDirectoryName(p)) {
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Linked package paths are unsupported");
            if(p==root)break;
        }
        return file;
    }
    static T Read<T>(string file) {if(new FileInfo(file).Length>65536)throw new ArgumentException("Manifest exceeds budget");return JsonSerializer.Deserialize<T>(File.ReadAllText(file),Json)??throw new ArgumentException("Empty package");}
    public static ResolvedAssets Load(string root,string skinId,string motionId)
    {
        Id(skinId);Id(motionId);
        string skinRoot=Inside(root,"skins/"+skinId);string skinFile=Inside(skinRoot,"manifest.json");
        var skin=Read<SkinManifest>(skinFile);var motion=Read<MotionManifest>(Inside(root,"motions/"+motionId+".json"));
        if(skin.Schema!="aipet.skin/1" || motion.Schema!="aipet.motion/1" || skin.Id!=skinId || motion.Id!=motionId
            || skin.Version!="1.0.0" || motion.Version!="1.0.0" || skin.Rig!="kenney-platformer/1" || motion.Rig!=skin.Rig
            || string.IsNullOrWhiteSpace(skin.Author) || !Uri.TryCreate(skin.Source,UriKind.Absolute,out var uri) || uri.Scheme!="https"
            || skin.Poses.Count is <1 or >64 || motion.Steps.Length is <1 or >128)throw new ArgumentException("Unsupported package contract");
        Inside(skinRoot,skin.License);
        var poses=new Dictionary<string,string>();long bytes=0;
        foreach(var item in skin.Poses) {
            Id(item.Key);var f=item.Value;string path=Inside(skinRoot,f.Path);var info=new FileInfo(path);bytes+=info.Length;
            if(Path.GetExtension(path)!=".png" || info.Length!=f.Bytes || info.Length>4_194_304 || bytes>16_777_216
                || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Asset fingerprint or budget mismatch");
            using var stream=File.OpenRead(path);var image=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            if(image.Frames.Count!=1 || image.Frames[0].PixelWidth is <1 or >512 || image.Frames[0].PixelHeight is <1 or >512)throw new ArgumentException("Texture budget exceeded");
            poses.Add(item.Key,path);
        }
        if(motion.Steps.Any(s=>s.DurationMs is <50 or >5000 || !poses.ContainsKey(s.Pose)))throw new ArgumentException("Incompatible pose binding");
        string generation=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(skinFile)))[..16];
        return new(new(skin.Id+"@"+generation,poses),new(motion.Id,motion.Steps));
    }
}

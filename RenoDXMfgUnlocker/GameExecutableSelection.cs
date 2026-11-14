using System.IO;
using System.Text.Json;

namespace RenoDXMfgUnlocker;

/// <summary>Selection evidence only; never a runtime compatibility or installation grant.</summary>
internal static class GameExecutableSelection
{
    internal static string? Automatic(string root,string appId,IReadOnlyList<string> candidates,string? confirmed)
    {
        bool Safe(string path) {
            try {
                var full=Path.GetFullPath(path);var relative=Path.GetRelativePath(Path.GetFullPath(root),full);
                if(Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar)||!File.Exists(full)||!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return false;
                StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(full)!,full);return true;
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){return false;}
        }
        if(confirmed is not null&&Safe(confirmed))return Path.GetFullPath(confirmed);
        var eligible=candidates.Where(Safe).Where(p=>!ExecutableCandidatePolicy.IsAuxiliary(Path.GetFileName(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Reviewed Witcher diagnostic profile is DirectX 12. This path selects that EXE,
        // not the different bin/x64 (DX11) build, and grants no file operations.
        if(appId=="292030"&&new BuiltInGameCompatibilityProfileCatalog().FindByGameId(appId)?.GraphicsApi=="DirectX 12") {
            var known=Path.Combine(root,"bin","x64_dx12","witcher3.exe");
            if(eligible.Any(p=>Path.GetFullPath(p).Equals(Path.GetFullPath(known),StringComparison.OrdinalIgnoreCase)))return Path.GetFullPath(known);
        }
        return eligible.Length==1?eligible[0]:null;
    }
    // Valve's official store identifies AppID 730 as Counter-Strike 2.
    // https://store.steampowered.com/app/730/CounterStrike_2/
    internal static string DisplayName(string appId,string? name)=>string.IsNullOrWhiteSpace(name)||name.Equals("appid_"+appId,StringComparison.OrdinalIgnoreCase)
        ?appId=="730"?"Counter-Strike 2":"appid_"+appId:name;
}

internal sealed class ApplicationPreferences
{
    private sealed record Data(bool CheckUpdatesAtLaunch,Dictionary<string,string> Executables);
    private readonly string path;
    private Data data=new(false,new(StringComparer.OrdinalIgnoreCase));
    internal ApplicationPreferences(string? path=null) {
        this.path=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","preferences.json");
        try {
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(this.path)!,this.path);
            if(new FileInfo(this.path).Length>262144)return;
            var saved=JsonSerializer.Deserialize<Data>(File.ReadAllText(this.path));
            if(saved?.Executables is not null)data=new(saved.CheckUpdatesAtLaunch,new(saved.Executables,StringComparer.OrdinalIgnoreCase));
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){}
    }
    internal bool CheckUpdatesAtLaunch=>data.CheckUpdatesAtLaunch;
    internal string? ConfirmedExe(string root)=>data.Executables.GetValueOrDefault(Path.GetFullPath(root));
    internal void SetUpdateCheck(bool enabled)=>Save(data with {CheckUpdatesAtLaunch=enabled});
    internal void ConfirmExe(string root,string exe) {
        var entries=new Dictionary<string,string>(data.Executables,StringComparer.OrdinalIgnoreCase){[Path.GetFullPath(root)]=Path.GetFullPath(exe)};
        Save(data with {Executables=entries});
    }
    private void Save(Data next) {
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(path)!,path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,path,true);data=next;}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

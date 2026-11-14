using System.IO;
using System.Text.Json;

namespace RenoDXMfgUnlocker;

/// <summary>Opt-in release metadata only. Never retrieves source, approves metadata or writes game configuration.</summary>
internal sealed class MfgUpstreamReleaseNotice
{
    internal sealed record State(bool Enabled=false,DateTimeOffset? AttemptUtc=null,string? Latest=null);
    private readonly string root;
    private readonly Func<CancellationToken,Task<string>> latest;
    private readonly Func<DateTimeOffset> clock;
    internal static string UserRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","UpstreamReleaseNotice");
    internal MfgUpstreamReleaseNotice(string? root=null,Func<CancellationToken,Task<string>>? latest=null,Func<DateTimeOffset>? clock=null)
    {
        this.root=Path.GetFullPath(root??UserRoot);this.latest=latest??Fetch;this.clock=clock??(()=>DateTimeOffset.UtcNow);
    }
    private static async Task<string> Fetch(CancellationToken token){using var client=new MfgUpstreamSettingsClient();return await client.LatestStableAsync(token).ConfigureAwait(false);}
    private string PathFor(string name)=>Path.Combine(root,name);
    private void Safe(string path)=>StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(root)!,path);
    private FileStream Lease()
    {
        Safe(root);Directory.CreateDirectory(root);var path=PathFor("metadata.lock");Safe(path);
        return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    internal State Read()
    {
        var path=PathFor("metadata.json");
        try{
            Safe(path);if(!File.Exists(path))return new();
            var s=JsonSerializer.Deserialize<State>(MfgSettingsCatalogStore.ReadBounded(path,4096));
            if(s is null||s.Latest is not null&&!MfgSettingsCapabilityCatalog.TryStableVersion(s.Latest,out _)
                ||s.AttemptUtc>clock().AddMinutes(5))return new();
            return s;
        }catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException){return new();}
    }
    private void Save(State state)
    {
        var path=PathFor("metadata.json");Safe(path);var temp=PathFor(".save-"+Guid.NewGuid().ToString("N"));Safe(temp);
        try{
            using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(JsonSerializer.SerializeToUtf8Bytes(state));f.Flush(true);}
            Safe(path);Safe(temp);File.Move(temp,path,true);
        }finally{if(File.Exists(temp)){Safe(temp);File.Delete(temp);}}
    }
    internal void SetEnabled(bool enabled)
    {
        using var lease=Lease();var state=Read();
        // A corrupt cache disables polling. Re-enabling it never causes an immediate retry storm.
        var path=PathFor("metadata.json");
        if(state.AttemptUtc is null&&File.Exists(path))state=state with{AttemptUtc=new DateTimeOffset(File.GetLastWriteTimeUtc(path))};
        Save(state with{Enabled=enabled});
    }
    internal async Task<string?> CheckIfDueAsync(CancellationToken token=default)
    {
        DateTimeOffset attempt;
        try{
            using var lease=Lease();var state=Read();var now=clock();
            if(!state.Enabled)return null;
            if(state.AttemptUtc is {} previous&&now-previous<TimeSpan.FromHours(24))return state.Latest;
            token.ThrowIfCancellationRequested();attempt=now;Save(state with{AttemptUtc=attempt});
        }catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException){return null;}
        // Claim is durably saved before HTTP, including failed/rate-limited attempts and concurrent instances.
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try{
            var tag=await latest(timeout.Token).ConfigureAwait(false);
            if(!MfgSettingsCapabilityCatalog.TryStableVersion(tag,out var version))return null;
            using var lease=Lease();var state=Read();
            if(!state.Enabled||state.AttemptUtc!=attempt)return null;
            if(state.Latest is {} old&&MfgSettingsCapabilityCatalog.TryStableVersion(old,out var known)&&version<known)return old;
            Save(state with{Latest=tag});return tag;
        }catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException){return null;}
    }
    internal static bool HasUnreviewedRelease(string? latest,string? reviewed)=>MfgSettingsCapabilityCatalog.TryStableVersion(latest,out var available)
        &&(!MfgSettingsCapabilityCatalog.TryStableVersion(reviewed,out var known)||available>known);
}

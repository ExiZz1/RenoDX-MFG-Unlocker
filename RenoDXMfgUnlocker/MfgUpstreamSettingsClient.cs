using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

/// <summary>Official repository only. No code execution/extraction. Commit-tree blob hashes bind bounded source text to a release tag.</summary>
internal sealed class MfgUpstreamSettingsClient : IDisposable
{
    internal const string Repository="mavismmg/MFGAdaUnlock-RenoDx";
    private const string Api="https://api.github.com/repos/"+Repository+"/";
    private readonly HttpClient http;
    private readonly bool owns;
    internal MfgUpstreamSettingsClient(HttpClient? client=null){owns=client is null;http=client??new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false}){Timeout=TimeSpan.FromSeconds(45)};}
    internal async Task<string> LatestStableAsync(CancellationToken token=default)
    {
        using var doc=JsonDocument.Parse(await ReadAsync(Api+"releases/latest",512*1024,token));var r=doc.RootElement;var tag=r.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"\A[0-9]+\.[0-9]+\.[0-9]+\z")||!MfgSettingsCapabilityCatalog.TryStableVersion(tag,out _)
            ||r.GetProperty("draft").GetBoolean()||r.GetProperty("prerelease").GetBoolean()||r.GetProperty("html_url").GetString()!="https://github.com/"+Repository+"/releases/tag/"+tag)throw Invalid();
        return tag;
    }
    internal async Task<UpstreamSourceSnapshot> FetchAsync(string installedVersion,CancellationToken cancellation=default)
    {
        if(!MfgSettingsCapabilityCatalog.TryStableVersion(installedVersion,out var version)||version.Revision>0)throw Invalid();
        var tag=$"{version.Major}.{version.Minor}.{version.Build}";
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation);linked.CancelAfter(TimeSpan.FromSeconds(60));var token=linked.Token;
        using var release=JsonDocument.Parse(await ReadAsync(Api+"releases/tags/"+tag,512*1024,token));var r=release.RootElement;
        if(r.GetProperty("tag_name").GetString()!=tag||r.GetProperty("draft").GetBoolean()||r.GetProperty("prerelease").GetBoolean()
            ||r.GetProperty("html_url").GetString()!="https://github.com/"+Repository+"/releases/tag/"+tag)throw Invalid();
        using var commitDoc=JsonDocument.Parse(await ReadAsync(Api+"commits/"+tag,1024*1024,token));var c=commitDoc.RootElement;
        var commit=c.GetProperty("sha").GetString()??"";var tree=c.GetProperty("commit").GetProperty("tree").GetProperty("sha").GetString()??"";
        if(!Sha(commit)||!Sha(tree))throw Invalid();
        using var treeDoc=JsonDocument.Parse(await ReadAsync(Api+"git/trees/"+tree+"?recursive=1",2*1024*1024,token));
        if(treeDoc.RootElement.GetProperty("truncated").GetBoolean())throw Invalid();
        var expected=new Dictionary<string,string>(StringComparer.Ordinal);var all=treeDoc.RootElement.GetProperty("tree");
        if(all.GetArrayLength()>10000)throw Invalid();
        foreach(var item in all.EnumerateArray()){
            var path=item.GetProperty("path").GetString()??"";if(!MfgUpstreamSettingsAnalyzer.SafeSourcePath(path))continue;
            if(item.GetProperty("type").GetString()!="blob"||item.GetProperty("mode").GetString()!="100644"||item.GetProperty("size").GetInt64()>512*1024)throw Invalid();
            var hash=item.GetProperty("sha").GetString()??"";if(!Sha(hash)||!expected.TryAdd(path,hash)||expected.Count>256)throw Invalid();
        }
        var zip=await ReadAsync("https://codeload.github.com/"+Repository+"/zip/"+commit,16*1024*1024,token);
        var result=ReadArchive(zip,commit,expected);
        // A moving tag is not silently accepted between metadata and download.
        using var recheck=JsonDocument.Parse(await ReadAsync(Api+"commits/"+tag,1024*1024,token));
        if(recheck.RootElement.GetProperty("sha").GetString()!=commit)throw Invalid();
        var snapshot=new UpstreamSourceSnapshot(tag,commit,result);MfgUpstreamSettingsAnalyzer.ValidateSnapshot(snapshot);return snapshot;
    }
    internal static Dictionary<string,string> ReadArchive(byte[] bytes,string commit,IReadOnlyDictionary<string,string> expected)
    {
        if(bytes.Length>16*1024*1024||!Sha(commit)||expected.Count is <1 or >256)throw Invalid();
        using var memory=new MemoryStream(bytes,false);using var archive=new ZipArchive(memory,ZipArchiveMode.Read);
        if(archive.Entries.Count>10000)throw Invalid();
        var prefix="MFGAdaUnlock-RenoDx-"+commit+"/";var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new Dictionary<string,string>(StringComparer.Ordinal);long total=0;
        foreach(var entry in archive.Entries){
            var name=entry.FullName;
            if(!name.StartsWith(prefix,StringComparison.Ordinal)||name.Length>512||name.Contains('\\')||name.Split('/').Any(p=>p is "." or "..")||!names.Add(name)
                ||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw Invalid();
            var path=name[prefix.Length..];if(!MfgUpstreamSettingsAnalyzer.SafeSourcePath(path))continue;
            if(!expected.TryGetValue(path,out var hash)||entry.Length>512*1024||(total+=entry.Length)>6*1024*1024)throw Invalid();
            using var input=entry.Open();using var output=new MemoryStream();var buffer=new byte[16384];int read;
            while((read=input.Read(buffer))>0){if(output.Length+read>512*1024)throw Invalid();output.Write(buffer,0,read);}
            var data=output.ToArray();if(data.Length!=entry.Length)throw Invalid();
            var header=Encoding.ASCII.GetBytes("blob "+data.Length+"\0");
            var actual=Convert.ToHexString(SHA1.HashData([..header,..data])).ToLowerInvariant(); // Git object identity, not a signature/security claim.
            if(actual!=hash)throw Invalid();result.Add(path,new UTF8Encoding(false,true).GetString(data));
        }
        if(result.Count!=expected.Count)throw Invalid();return result;
    }
    private async Task<byte[]> ReadAsync(string url,int maximum,CancellationToken token)
    {
        var uri=new Uri(url);if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0
            ||!(url.StartsWith(Api,StringComparison.Ordinal)||Regex.IsMatch(url,@"\Ahttps://codeload\.github\.com/mavismmg/MFGAdaUnlock-RenoDx/zip/[a-f0-9]{40}\z")))throw Invalid();
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("RenoDX-MFG-Unlocker/1.3.0");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
        if((int)response.StatusCode is >=300 and <400)throw Invalid();response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>maximum)throw Invalid();
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(output.Length+read>maximum)throw Invalid();output.Write(buffer,0,read);}return output.ToArray();
    }
    private static bool Sha(string s)=>Regex.IsMatch(s,@"\A[a-f0-9]{40}\z",RegexOptions.CultureInvariant);
    private static InvalidDataException Invalid()=>new("Invalid upstream source snapshot.");
    public void Dispose(){if(owns)http.Dispose();}
}

/// <summary>Locally approved source data, DPAPI-authenticated for this Windows user. Never an owner-signed catalog.</summary>
internal sealed class MfgUpstreamSettingsStore(string root)
{
    internal static string UserRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","UpstreamSettings");
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("RenoDX upstream settings approval v1");
    private void Safe(string p)=>StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(root))!,p);
    internal UpstreamSourceSnapshot? Load()
    {
        var path=Path.Combine(root,"approved.dat");Safe(path);if(!File.Exists(path))return null;
        var bytes=MfgSettingsCatalogStore.ReadBounded(path,16*1024*1024);var clear=ProtectedData.Unprotect(bytes,Entropy,DataProtectionScope.CurrentUser);
        var snapshot=JsonSerializer.Deserialize<UpstreamSourceSnapshot>(clear)??throw new InvalidDataException("Invalid upstream source snapshot.");MfgUpstreamSettingsAnalyzer.ValidateSnapshot(snapshot);return snapshot;
    }
    internal void Save(UpstreamSourceSnapshot snapshot)
    {
        MfgUpstreamSettingsAnalyzer.ValidateSnapshot(snapshot);Safe(root);Directory.CreateDirectory(root);
        var lockPath=Path.Combine(root,"approval.lock");Safe(lockPath);
        using var lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var previous=Load();
        if(previous is not null){
            var next=Version.Parse(snapshot.Tag);var old=Version.Parse(previous.Tag);
            if(next<old)throw new InvalidDataException("Upstream approval downgrade is not allowed.");
            if(next==old&&snapshot.Commit!=previous.Commit)throw new InvalidDataException("Approved upstream release points to a different commit. Manual review is required.");
        }
        var path=Path.Combine(root,"approved.dat");Safe(path);var temporary=Path.Combine(root,".save-"+Guid.NewGuid().ToString("N"));Safe(temporary);
        try{
            var data=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(snapshot),Entropy,DataProtectionScope.CurrentUser);
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(data);file.Flush(true);}
            Safe(path);Safe(temporary);File.Move(temporary,path,true);
        }finally{if(File.Exists(temporary)){Safe(temporary);File.Delete(temporary);}}
    }
}

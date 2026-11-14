using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

internal sealed record ApplicationVersion(BigInteger Major,BigInteger Minor,BigInteger Patch,string[] Prerelease):IComparable<ApplicationVersion>
{
    internal static ApplicationVersion? Parse(string? text) {
        if(text is null||text.Length>256)return null;
        var m=Regex.Match(text,@"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",RegexOptions.CultureInvariant);
        if(!m.Success)return null;
        var pre=m.Groups[4].Success?m.Groups[4].Value.Split('.'):[];
        if(pre.Any(x=>x.All(char.IsAsciiDigit)&&x.Length>1&&x[0]=='0'))return null;
        return new(BigInteger.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),BigInteger.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture),BigInteger.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture),pre);
    }
    public int CompareTo(ApplicationVersion? other) {
        if(other is null)return 1;
        foreach(var pair in new[]{(Major,other.Major),(Minor,other.Minor),(Patch,other.Patch)}){var c=pair.Item1.CompareTo(pair.Item2);if(c!=0)return c;}
        if(Prerelease.Length==0||other.Prerelease.Length==0)return (Prerelease.Length==0?1:0).CompareTo(other.Prerelease.Length==0?1:0);
        for(var i=0;i<Math.Min(Prerelease.Length,other.Prerelease.Length);i++) {
            var a=Prerelease[i];var b=other.Prerelease[i];var an=BigInteger.TryParse(a,NumberStyles.None,CultureInfo.InvariantCulture,out var av);var bn=BigInteger.TryParse(b,NumberStyles.None,CultureInfo.InvariantCulture,out var bv);
            var c=an&&bn?av.CompareTo(bv):an!=bn?(an?-1:1):string.CompareOrdinal(a,b);if(c!=0)return c;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }
}

internal sealed record ApplicationRelease(string Tag,string Title,string Notes,DateTimeOffset Published,string ReleaseUrl,string? AssetName,string? DownloadUrl,long Size,string? Sha256,ApplicationMetadataAsset? Manifest=null,ApplicationMetadataAsset? Signature=null);
internal sealed record ApplicationMetadataAsset(string Name,string Url,long Size);
internal sealed record ApplicationUpdateCheck(ApplicationRelease? Latest,bool UpdateAvailable);

/// <summary>Download-only. No executable launch, extraction, installation or GitHub credentials.</summary>
internal sealed class ApplicationUpdateService:IDisposable
{
    internal const string Repository="ExiZz1/RenoDX-MFG-Unlocker";
    internal const string ApiRoot="https://api.github.com/repos/"+Repository;
    internal const string WebRoot="https://github.com/"+Repository;
    internal const string IntegrityNotice="GitHub SHA-256 verifies downloaded bytes against release metadata. It is not independent proof of publisher authenticity. No files are installed or executed.";
    private readonly HttpClient http;
    private readonly bool ownsClient;
    private readonly SemaphoreSlim checks=new(1,1);
    private readonly SemaphoreSlim downloads=new(1,1);
    private ApplicationRelease? cached;
    private DateTimeOffset checkedAt;
    internal ApplicationUpdateService(HttpClient? client=null) {
        ownsClient=client is null;
        http=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=Timeout.InfiniteTimeSpan};
    }
    internal static bool SafeUri(Uri uri)=>uri.Scheme==Uri.UriSchemeHttps&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&uri.OriginalString.IndexOf('\\')<0;
    private static Uri Exact(string value,string expected) {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||!SafeUri(uri)||uri.AbsoluteUri!=expected)throw new InvalidDataException("Invalid official release URL.");return uri;
    }
    internal static bool AllowedRedirect(Uri uri)=>SafeUri(uri)&&uri.Host=="release-assets.githubusercontent.com"&&uri.AbsolutePath.StartsWith("/github-production-release-asset/",StringComparison.Ordinal);
    internal static ApplicationUpdateCheck Evaluate(string installed,ApplicationRelease? release) {
        var current=ApplicationVersion.Parse(installed)??throw new InvalidDataException("Invalid application version.");
        return new(release,release is not null&&ApplicationVersion.Parse(release.Tag)!.CompareTo(current)>0);
    }
    internal static IReadOnlyList<ApplicationRelease> Parse(string json) {
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Invalid GitHub release metadata.");
        var found=new List<ApplicationRelease>();
        foreach(var item in doc.RootElement.EnumerateArray()) {
            if(item.GetProperty("draft").GetBoolean()||item.GetProperty("prerelease").GetBoolean())continue;
            var tag=item.GetProperty("tag_name").GetString()!;var version=ApplicationVersion.Parse(tag);
            if(version is null||version.Prerelease.Length!=0)continue;
            var url=item.GetProperty("html_url").GetString()!;Exact(url,WebRoot+"/releases/tag/"+tag);
            Exact(item.GetProperty("url").GetString()!,ApiRoot+"/releases/"+item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture));
            var expected="RenoDX_MFG_Unlocker_v"+tag.TrimStart('v')+"_Windows_x64.zip";
            string? name=null,download=null,digest=null;long size=0;ApplicationMetadataAsset? manifest=null,signature=null;
            foreach(var asset in item.GetProperty("assets").EnumerateArray()) {
                var metadataName=asset.GetProperty("name").GetString();
                if(metadataName==expected+".update.json"||metadataName==expected+".sig") {
                    if(asset.GetProperty("state").GetString()!="uploaded")continue;
                    var metadataUrl=asset.GetProperty("browser_download_url").GetString()!;
                    Exact(metadataUrl,WebRoot+"/releases/download/"+tag+"/"+metadataName);
                    Exact(asset.GetProperty("url").GetString()!,ApiRoot+"/releases/assets/"+asset.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture));
                    var metadataSize=asset.GetProperty("size").GetInt64();if(metadataSize<=0||metadataSize>(metadataName.EndsWith(".sig",StringComparison.Ordinal)?512:65536))throw new InvalidDataException("Invalid signed metadata asset size.");
                    var entry=new ApplicationMetadataAsset(metadataName,metadataUrl,metadataSize);
                    if(metadataName.EndsWith(".sig",StringComparison.Ordinal)){if(signature is not null)throw new InvalidDataException("Duplicate update signature.");signature=entry;}
                    else {if(manifest is not null)throw new InvalidDataException("Duplicate update manifest.");manifest=entry;}continue;
                }
                if(asset.GetProperty("name").GetString()!=expected||asset.GetProperty("state").GetString()!="uploaded")continue;
                if(name is not null)throw new InvalidDataException("Ambiguous application release assets.");
                var candidate=asset.GetProperty("browser_download_url").GetString()!;Exact(candidate,WebRoot+"/releases/download/"+tag+"/"+expected);
                Exact(asset.GetProperty("url").GetString()!,ApiRoot+"/releases/assets/"+asset.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture));
                name=expected;download=candidate;size=asset.GetProperty("size").GetInt64();
                if(size<=0||size>1024L*1024*1024)throw new InvalidDataException("Invalid update size.");
                if(asset.TryGetProperty("digest",out var d)&&d.ValueKind==JsonValueKind.String&&Regex.IsMatch(d.GetString()??"",@"^sha256:[0-9a-fA-F]{64}$",RegexOptions.CultureInvariant))digest=d.GetString()![7..].ToLowerInvariant();
            }
            found.Add(new(tag,item.GetProperty("name").GetString()??tag,item.TryGetProperty("body",out var body)?body.GetString()??"":"",item.GetProperty("published_at").GetDateTimeOffset(),url,name,download,size,digest,manifest,signature));
        }
        return found;
    }
    private static HttpRequestMessage Request(Uri uri) {
        var request=new HttpRequestMessage(HttpMethod.Get,uri);
        request.Headers.UserAgent.ParseAdd("RenoDX-MFG-Unlocker/1.2.1");
        if(uri.Host=="api.github.com"){request.Headers.Accept.ParseAdd("application/vnd.github+json");request.Headers.Add("X-GitHub-Api-Version","2022-11-28");}
        return request;
    }
    internal async Task<ApplicationUpdateCheck> CheckAsync(string installed,bool force=false,CancellationToken token=default) {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(45));token=deadline.Token;
        await checks.WaitAsync(token).ConfigureAwait(false);
        try {
            if(!force&&DateTimeOffset.UtcNow-checkedAt<TimeSpan.FromHours(1))return Evaluate(installed,cached);
            var releases=new List<ApplicationRelease>();
            for(var page=1;page<=5;page++) {
                using var request=Request(new Uri(ApiRoot+"/releases?per_page=100&page="+page));
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var stream=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var buffer=new MemoryStream();var bytes=new byte[32768];int read;
                while((read=await stream.ReadAsync(bytes,token).ConfigureAwait(false))>0){if(buffer.Length+read>4*1024*1024)throw new InvalidDataException("Release metadata exceeds limit.");buffer.Write(bytes,0,read);}
                var json=Encoding.UTF8.GetString(buffer.ToArray());releases.AddRange(Parse(json));
                using var count=JsonDocument.Parse(json);if(count.RootElement.GetArrayLength()<100)break;
                if(page==5)throw new InvalidDataException("Release listing exceeds review limit.");
            }
            cached=releases.OrderByDescending(r=>ApplicationVersion.Parse(r.Tag)).FirstOrDefault();checkedAt=DateTimeOffset.UtcNow;
            return Evaluate(installed,cached);
        }finally{checks.Release();}
    }
    internal async Task<string> DownloadAsync(ApplicationRelease release,string directory,IProgress<StreamlineDownloadProgress>? progress=null,CancellationToken token=default) {
        // A fresh check must still select precisely this metadata. Callers cannot supply
        // a stale UI row or substitute a different ZIP/digest through a direct call.
        var fresh=await CheckAsync("0.0.0",true,token).ConfigureAwait(false);
        if(fresh.Latest!=release)throw new InvalidDataException("Release metadata changed. Check for updates again.");
        if(release.AssetName is null||release.DownloadUrl is null||release.Sha256 is null)throw new InvalidDataException("An official SHA-256 digest and Windows x64 ZIP are required.");
        var initial=Exact(release.DownloadUrl,WebRoot+"/releases/download/"+release.Tag+"/"+release.AssetName);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(15));token=deadline.Token;
        await downloads.WaitAsync(token).ConfigureAwait(false);
        string? temporary=null;
        try {
            directory=Path.GetFullPath(directory);var destination=Path.Combine(directory,release.AssetName);
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(directory)!,destination);
            Directory.CreateDirectory(directory);
            if(File.Exists(destination)){await VerifyAsync(destination,release,token).ConfigureAwait(false);return destination;}
            temporary=Path.Combine(directory,"."+Guid.NewGuid().ToString("N")+".partial");
            var uri=initial;
            for(var hops=0;hops<=3;hops++) {
                using var request=Request(uri);
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                if(response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther) {
                    if(hops==3||response.Headers.Location is not {} location)throw new InvalidDataException("Invalid release asset redirect.");
                    var next=location.IsAbsoluteUri?location:new Uri(uri,location);
                    if(!AllowedRedirect(next))throw new InvalidDataException("Untrusted release asset redirect.");uri=next;continue;
                }
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is {} length&&length!=release.Size)throw new InvalidDataException("Update size does not match release metadata.");
                await using(var source=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using(var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)) {
                    var bytes=new byte[65536];long total=0;int read;
                    while((read=await source.ReadAsync(bytes,token).ConfigureAwait(false))>0){total+=read;if(total>release.Size)throw new InvalidDataException("Update exceeds expected size.");await target.WriteAsync(bytes.AsMemory(0,read),token).ConfigureAwait(false);progress?.Report(new(total,release.Size));}
                }
                await VerifyAsync(temporary,release,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
                StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(directory)!,destination);
                File.Move(temporary,destination,false);temporary=null;return destination;
            }
            throw new InvalidDataException("Release asset redirect limit exceeded.");
        }finally{if(temporary is not null&&File.Exists(temporary))File.Delete(temporary);downloads.Release();}
    }
    internal static async Task VerifyAsync(string path,ApplicationRelease release,CancellationToken token) {
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
        if(stream.Length!=release.Size||release.Sha256 is null)throw new InvalidDataException("Update size or digest metadata is invalid.");
        if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update SHA-256 mismatch.");
        stream.Position=0;using var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
        if(!zip.Entries.Any(e=>e.FullName.Equals("RenoDX MFG Unlocker.exe",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith("/RenoDX MFG Unlocker.exe",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("The release ZIP has no application EXE.");
        // Contents are never extracted or executed here, even after a valid digest.
    }
    internal async Task<SignedUpdateMetadata> GetSignedMetadataAsync(ApplicationRelease release,CancellationToken token=default) {
        var fresh=await CheckAsync("0.0.0",true,token).ConfigureAwait(false);
        if(fresh.Latest!=release||release.Manifest is null||release.Signature is null)throw new InvalidDataException("Signed release metadata is unavailable or changed.");
        return new(await ReadMetadataAssetAsync(release,release.Manifest,token).ConfigureAwait(false),await ReadMetadataAssetAsync(release,release.Signature,token).ConfigureAwait(false));
    }
    private async Task<byte[]> ReadMetadataAssetAsync(ApplicationRelease release,ApplicationMetadataAsset asset,CancellationToken token) {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(45));token=deadline.Token;
        var uri=Exact(asset.Url,WebRoot+"/releases/download/"+release.Tag+"/"+asset.Name);
        for(var hop=0;hop<4;hop++) {
            using var request=Request(uri);using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther) {
                if(response.Headers.Location is not {} location)throw new InvalidDataException("Invalid signed metadata redirect.");var next=location.IsAbsoluteUri?location:new Uri(uri,location);if(!AllowedRedirect(next))throw new InvalidDataException("Untrusted signed metadata redirect.");uri=next;continue;
            }
            response.EnsureSuccessStatusCode();using var source=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new MemoryStream();var bytes=new byte[4096];int read;
            while((read=await source.ReadAsync(bytes,token).ConfigureAwait(false))>0){if(output.Length+read>asset.Size||output.Length+read>65536)throw new InvalidDataException("Signed metadata exceeds expected size.");output.Write(bytes,0,read);}
            if(output.Length!=asset.Size)throw new InvalidDataException("Signed metadata size mismatch.");return output.ToArray();
        }throw new InvalidDataException("Signed metadata redirect limit exceeded.");
    }
    public void Dispose(){if(ownsClient)http.Dispose();checks.Dispose();downloads.Dispose();}
}

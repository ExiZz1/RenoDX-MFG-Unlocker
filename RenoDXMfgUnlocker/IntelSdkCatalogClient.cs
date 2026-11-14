using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public sealed record IntelSdkAsset(string Name,string DownloadUrl,long Size,string? Sha256,string Tag,string Title,DateTimeOffset? PublishedAt,string ReleaseUrl,string Notes,bool OfficialLatest,bool Prerelease);
public sealed record IntelSdkCatalog(IReadOnlyList<IntelSdkAsset> Assets,string? LatestTag);

public sealed class IntelSdkCatalogClient
{
    internal const string Api="https://api.github.com/repos/intel/xess/releases";
    private readonly HttpClient http;
    public IntelSdkCatalogClient(HttpClient? http=null){this.http=http??new HttpClient {Timeout=TimeSpan.FromSeconds(45)};if(!this.http.DefaultRequestHeaders.UserAgent.Any())this.http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RenoDX-MFG-Unlocker","1.1"));}
    public async Task<IntelSdkCatalog> GetAsync(CancellationToken token=default)
    {
        string? latest=null;
        using(var response=await http.GetAsync(Api+"/latest",HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)){
            ValidateApiResponse(response,"/repos/intel/xess/releases/latest");
            if(response.StatusCode!=System.Net.HttpStatusCode.NotFound){response.EnsureSuccessStatusCode();using var doc=await ReadJson(response,token).ConfigureAwait(false);latest=Text(doc.RootElement,"tag_name");}
        }
        var assets=new List<IntelSdkAsset>();
        for(var page=1;page<=10;page++){
            using var response=await http.GetAsync(Api+$"?per_page=100&page={page}",HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);ValidateApiResponse(response,"/repos/intel/xess/releases");response.EnsureSuccessStatusCode();
            using var doc=await ReadJson(response,token).ConfigureAwait(false);if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Invalid official Intel release metadata.");
            assets.AddRange(Parse(doc.RootElement,latest));if(doc.RootElement.GetArrayLength()<100)break;if(page==10)throw new InvalidDataException("Too many Intel release pages.");
        }
        if(assets.Select(a=>a.Tag+"/"+a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=assets.Count)throw new InvalidDataException("Duplicate Intel release assets.");
        return new(assets,latest);
    }
    internal static IReadOnlyList<IntelSdkAsset> Parse(JsonElement root,string? latest)
    {
        var result=new List<IntelSdkAsset>();
        foreach(var release in root.EnumerateArray()){
            if(release.TryGetProperty("draft",out var draft)&&draft.GetBoolean())continue;
            var tag=Text(release,"tag_name")??"";if(!Regex.IsMatch(tag,@"^v?\d+(?:\.\d+){1,3}(?:[-+][A-Za-z0-9.-]+)?$",RegexOptions.CultureInvariant))continue;
            var releaseUrl=Text(release,"html_url")??"";if(releaseUrl!="https://github.com/intel/xess/releases/tag/"+Uri.EscapeDataString(tag))continue;
            if(!release.TryGetProperty("assets",out var files)||files.ValueKind!=JsonValueKind.Array)continue;
            foreach(var file in files.EnumerateArray()){
                var name=Text(file,"name")??"";var url=Text(file,"browser_download_url")??"";
                var digest=Text(file,"digest");var sha=digest is not null&&Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$")?digest[7..].ToLowerInvariant():null;
                DateTimeOffset? date=DateTimeOffset.TryParse(Text(release,"published_at"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var parsed)?parsed:null;
                if(!file.TryGetProperty("size",out var size)||!size.TryGetInt64(out var bytes))continue;
                var asset=new IntelSdkAsset(name,url,bytes,sha,tag,Text(release,"name")??tag,date,releaseUrl,Text(release,"body")??"",tag==latest,release.TryGetProperty("prerelease",out var pre)&&pre.GetBoolean());
                try{Validate(asset);result.Add(asset);}catch(InvalidDataException){/* Do not offer unrelated archives or untrusted download URLs. */}
            }
        }return result;
    }
    internal static void Validate(IntelSdkAsset asset)
    {
        if(!Regex.IsMatch(asset.Tag,@"^v?\d+(?:\.\d+){1,3}(?:[-+][A-Za-z0-9.-]+)?$")||!Regex.IsMatch(asset.Name,@"^XeSS[_.]SDK[_.-][0-9][A-Za-z0-9._+-]*\.zip$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)||asset.Name.Contains("..")||asset.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Invalid Intel SDK archive name or tag.");
        if(asset.Size<=0||asset.Size>1024L*1024*1024)throw new InvalidDataException("Invalid Intel SDK archive size.");
        if(asset.Sha256 is not null&&!Regex.IsMatch(asset.Sha256,@"^[0-9a-fA-F]{64}$"))throw new InvalidDataException("Invalid Intel SDK digest.");
        if(!Uri.TryCreate(asset.DownloadUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||uri.Port!=443||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0||Uri.UnescapeDataString(uri.AbsolutePath)!=$"/intel/xess/releases/download/{asset.Tag}/{asset.Name}")throw new InvalidDataException("Not an official Intel XeSS release asset URL.");
        if(asset.ReleaseUrl!="https://github.com/intel/xess/releases/tag/"+Uri.EscapeDataString(asset.Tag))throw new InvalidDataException("Not an official Intel release page.");
    }
    private static void ValidateApiResponse(HttpResponseMessage response,string path){var uri=response.RequestMessage?.RequestUri;if(uri is null||uri.Scheme!="https"||uri.Host!="api.github.com"||uri.AbsolutePath!=path)throw new InvalidDataException("Intel metadata redirected away from the official repository.");}
    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response,CancellationToken token){await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[16384];int count;while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){if(output.Length+count>4*1024*1024)throw new InvalidDataException("Intel release metadata exceeds the size limit.");output.Write(buffer,0,count);}return JsonDocument.Parse(output.ToArray());}
    private static string? Text(JsonElement item,string key)=>item.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
}

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public sealed record IntelSdkDownload(string Path,string Sha256,bool OfficialDigestVerified);
public sealed record IntelSdkFile(string RelativePath,string Path,string Version,string Sha256,long Size,string Architecture);
public sealed record IntelSdkStage(IntelSdkAsset Asset,string Directory,IReadOnlyList<IntelSdkFile> Files,int Skipped,bool CatalogCompatible);

/// <summary>Official SDK acquisition and inspection only. No game paths or installation authority.</summary>
public sealed class IntelSdkPackageService
{
    private readonly HttpClient http;
    private readonly Func<string,string?> readVersion;
    public string Store {get;}
    public string Downloads=>Path.Combine(Store,"SDK","Downloads");
    public string Staging=>Path.Combine(Store,"SDK","Staging");
    public IntelRuntimeService Runtime {get;}
    public IntelSdkPackageService(string? store=null,HttpClient? http=null):this(store,http,ReadFileVersion){}
    private static string? ReadFileVersion(string path){var full=Path.GetFullPath(path);if(OperatingSystem.IsWindows()&&!full.StartsWith(@"\\?\",StringComparison.Ordinal))full=full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;return FileVersionInfo.GetVersionInfo(full).FileVersion;}
    internal IntelSdkPackageService(string? store,HttpClient? http,Func<string,string?> readVersion){Store=Path.GetFullPath(store??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","Intel"));this.http=http??new HttpClient {Timeout=TimeSpan.FromMinutes(10)};this.readVersion=readVersion;Runtime=new IntelRuntimeService(Store,http);}
    private static void Safe(string path)=>StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(path))!,path);
    private string ArchivePath(IntelSdkAsset asset)=>Path.Combine(Downloads,asset.Tag,asset.Name);
    private string StagePath(IntelSdkAsset asset)=>Path.Combine(Staging,asset.Tag+"-"+asset.Sha256);
    public async Task<IntelSdkDownload> DownloadAsync(IntelSdkAsset asset,IProgress<StreamlineDownloadProgress>? progress=null,CancellationToken token=default)
    {
        IntelSdkCatalogClient.Validate(asset);var target=ArchivePath(asset);Safe(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if(File.Exists(target)){
            if(asset.Sha256 is null)throw new IOException("Existing Intel archive has no official digest; it was kept unchanged.");
            return await VerifyArchive(target,asset,token).ConfigureAwait(false);
        }
        var temporary=target+"."+Guid.NewGuid().ToString("N")+".partial";
        try{
            using var response=await http.GetAsync(asset.DownloadUrl,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);response.EnsureSuccessStatusCode();
            var final=response.RequestMessage?.RequestUri;
            if(final is null||final.Scheme!="https"||!(final.AbsoluteUri==asset.DownloadUrl||(final.Host=="release-assets.githubusercontent.com"&&final.AbsolutePath.StartsWith("/github-production-release-asset/",StringComparison.Ordinal))||(final.Host=="objects.githubusercontent.com"&&final.AbsolutePath.StartsWith("/github-production-release-asset/",StringComparison.Ordinal))))throw new InvalidDataException("Intel download redirected outside official GitHub asset delivery.");
            await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)){
                var buffer=new byte[131072];long total=0;int count;while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){total+=count;if(total>asset.Size)throw new InvalidDataException("Intel download exceeds advertised size.");await output.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);progress?.Report(new(total,asset.Size));}if(total!=asset.Size)throw new EndOfStreamException("Incomplete Intel SDK download.");
            }
            var result=await VerifyArchive(temporary,asset,token).ConfigureAwait(false);Safe(target);File.Move(temporary,target,false);return result with {Path=target};
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static async Task<IntelSdkDownload> VerifyArchive(string path,IntelSdkAsset asset,CancellationToken token)
    {
        Safe(path);await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,true);if(stream.Length!=asset.Size)throw new InvalidDataException("Intel archive size mismatch.");var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false)).ToLowerInvariant();if(asset.Sha256 is not null&&!hash.Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Intel archive SHA-256 mismatch.");return new(path,hash,asset.Sha256 is not null);
    }
    public Task<IntelSdkStage?> InspectPreparedAsync(IntelSdkAsset asset,CancellationToken token=default)
    {
        // A previous download may outlive staging (or the previous app session).
        // Recover from that exact asset's archive, never from the independent pinned cache.
        IntelSdkCatalogClient.Validate(asset);return asset.Sha256 is null||(!File.Exists(ArchivePath(asset))&&!Directory.Exists(StagePath(asset)))?Task.FromResult<IntelSdkStage?>(null):InspectExisting();
        async Task<IntelSdkStage?> InspectExisting()=>await StageAsync(asset,token).ConfigureAwait(false);
    }
    public async Task<IntelSdkStage> ImportArchiveAsync(string source,IntelSdkAsset asset,CancellationToken token=default)
    {
        IntelSdkCatalogClient.Validate(asset);
        if(asset.Sha256 is null)throw new InvalidDataException("An official SHA-256 digest is required for Intel staging. The archive remains Unverified.");
        source=Path.GetFullPath(source);var target=ArchivePath(asset);Safe(source);Safe(target);
        if(File.Exists(target)&&!source.Equals(target,StringComparison.OrdinalIgnoreCase))await VerifyArchive(source,asset,token).ConfigureAwait(false);
        if(!source.Equals(target,StringComparison.OrdinalIgnoreCase)&&!File.Exists(target)){
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);var temporary=target+"."+Guid.NewGuid().ToString("N")+".partial";
            try{
                await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,131072,true)){
                    if(input.Length!=asset.Size)throw new InvalidDataException("Intel archive size mismatch.");
                    await using var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true);
                    var buffer=new byte[131072];long total=0;int count;
                    while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){total+=count;if(total>asset.Size)throw new InvalidDataException("Intel download exceeds advertised size.");await output.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);}
                }
                await VerifyArchive(temporary,asset,token).ConfigureAwait(false);Safe(target);File.Move(temporary,target,false);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        // Reuses the same official digest, archive and staging checks as Download / Prepare.
        return await StageAsync(asset,token).ConfigureAwait(false);
    }
    public async Task<IntelSdkStage> StageAsync(IntelSdkAsset asset,CancellationToken token=default)
    {
        IntelSdkCatalogClient.Validate(asset);if(asset.Sha256 is null)throw new InvalidDataException("An official SHA-256 digest is required for Intel staging. The archive remains Unverified.");
        await VerifyArchive(ArchivePath(asset),asset,token).ConfigureAwait(false);
        return await Task.Run(()=>Stage(asset,token),token).ConfigureAwait(false);
    }
    private IntelSdkStage Stage(IntelSdkAsset asset,CancellationToken token)
    {
        var final=StagePath(asset);Safe(final);Directory.CreateDirectory(Staging);var existing=Directory.Exists(final);var output=existing?final:final+"."+Guid.NewGuid().ToString("N")+".partial";if(!existing)Directory.CreateDirectory(output);
        try{
            Safe(ArchivePath(asset));using var archiveInput=new FileStream(ArchivePath(asset),FileMode.Open,FileAccess.Read,FileShare.Read);
            if(archiveInput.Length!=asset.Size||!Convert.ToHexString(SHA256.HashData(archiveInput)).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Intel archive changed before staging.");archiveInput.Position=0;
            using var archive=new ZipArchive(archiveInput,ZipArchiveMode.Read);if(archive.Entries.Count>10000)throw new InvalidDataException("Too many Intel SDK entries.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var files=new List<IntelSdkFile>();long expanded=0,staged=0;var skipped=0;
            foreach(var entry in archive.Entries){
                token.ThrowIfCancellationRequested();var relative=StreamlineArchiveStager.ValidateEntryName(entry.FullName);
                if(relative.Length>1024||relative.Split('/').Any(s=>Regex.IsMatch(s.Split('.')[0],@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase)))throw new InvalidDataException("Unsafe Intel archive path.");
                if(!seen.Add(relative))throw new InvalidDataException("Duplicate Intel archive paths.");
                if(((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked entries are not allowed in Intel archives.");
                if(entry.Length<0||entry.Length>512L*1024*1024||(expanded=checked(expanded+entry.Length))>2L*1024*1024*1024)throw new InvalidDataException("Intel archive expansion exceeds safety limits.");
                if(relative.EndsWith('/'))continue;
                var runtime=relative.Equals("bin/libxess_fg.dll",StringComparison.OrdinalIgnoreCase)||relative.Equals("bin/libxell.dll",StringComparison.OrdinalIgnoreCase);
                var leaf=Path.GetFileName(relative);var notice=(leaf.Contains("license",StringComparison.OrdinalIgnoreCase)||leaf.Contains("notice",StringComparison.OrdinalIgnoreCase)||leaf.Contains("third-party",StringComparison.OrdinalIgnoreCase))&&(leaf.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)||leaf.EndsWith(".md",StringComparison.OrdinalIgnoreCase)||leaf.Equals("LICENSE",StringComparison.OrdinalIgnoreCase));
                if(!runtime&&!notice){skipped++;continue;}
                if((staged=checked(staged+entry.Length))>128L*1024*1024)throw new InvalidDataException("Intel staging exceeds the allowed size.");
                var path=Path.GetFullPath(Path.Combine(output,relative.Replace('/',Path.DirectorySeparatorChar)));if(!StreamlineStagingEvidenceService.IsStrictChild(output,path))throw new InvalidDataException("Intel staging path escapes cache.");Safe(path);
                using var source=entry.Open();string hash;
                if(existing){hash=Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();if(!File.Exists(path)||new FileInfo(path).Length!=entry.Length||Hash(path)!=hash)throw new InvalidDataException("Prepared Intel files changed. Existing cache was preserved.");}
                else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);using(var destination=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var buffer=new byte[131072];long copied=0;int count;while((count=source.Read(buffer))>0){token.ThrowIfCancellationRequested();copied+=count;if(copied>entry.Length)throw new InvalidDataException("Intel ZIP member exceeds its declared size.");destination.Write(buffer,0,count);}if(copied!=entry.Length)throw new InvalidDataException("Truncated Intel ZIP member.");}hash=Hash(path);}
                string version="Not applicable",architecture="Not applicable";
                if(runtime){version="Unknown version";architecture="Unknown";try{using var pe=new PEReader(File.OpenRead(path));if(pe.PEHeaders.CoffHeader.Machine==Machine.Amd64&&pe.PEHeaders.PEHeader?.Magic==PEMagic.PE32Plus&&pe.PEHeaders.CorHeader is null&&(pe.PEHeaders.CoffHeader.Characteristics&Characteristics.Dll)!=0)architecture="Native x64 DLL";}catch(BadImageFormatException){}try{var value=readVersion(path);if(!string.IsNullOrWhiteSpace(value))version=value;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception){} }
                files.Add(new(relative,path,version,hash,entry.Length,architecture));
            }
            if(files.Count==0)throw new InvalidDataException("No allowed Intel runtime or license files in archive.");
            if(existing&&CountSafeFiles(final)!=files.Count)throw new InvalidDataException("Unexpected files in Intel staging; cache was preserved.");
            if(!existing){Safe(final);Directory.Move(output,final);files=files.Select(f=>f with {Path=Path.Combine(final,f.RelativePath.Replace('/',Path.DirectorySeparatorChar))}).ToList();}
            return new(asset,final,files,skipped,MatchesCatalog(files));
        }catch{if(!existing&&Directory.Exists(output)&&StreamlineStagingEvidenceService.IsStrictChild(Staging,output)){Safe(output);Directory.Delete(output,true);}throw;}
    }
    internal static bool MatchesCatalog(IReadOnlyList<IntelSdkFile> files)=>IntelRuntimeService.Assets.All(a=>files.Any(f=>f.RelativePath.Equals(a.Source,StringComparison.OrdinalIgnoreCase)&&f.Size==a.Size&&f.Sha256.Equals(a.Sha256,StringComparison.OrdinalIgnoreCase)&&(a.Version.Length==0||(BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(f.Version)==a.Version&&f.Architecture=="Native x64 DLL"))));
    public async Task<VerifiedIntelRuntime> PrepareCompatibleAsync(IntelSdkStage stage,CancellationToken token=default)
    {
        stage=await InspectPreparedAsync(stage.Asset,token).ConfigureAwait(false)??throw new InvalidDataException("Prepare the official Intel SDK first.");
        // Unknown SDKs never become installer packages. Only the existing immutable four-file catalog can cross this boundary.
        if(!MatchesCatalog(stage.Files)||!StreamlineStagingEvidenceService.IsStrictChild(Staging,stage.Directory))throw new InvalidDataException("This Intel SDK is not the confirmed MFG Unlock runtime catalog.");
        foreach(var asset in IntelRuntimeService.Assets){var file=stage.Files.Single(f=>f.RelativePath.Equals(asset.Source,StringComparison.OrdinalIgnoreCase));if(!StreamlineStagingEvidenceService.IsStrictChild(stage.Directory,file.Path))throw new InvalidDataException("Intel source escapes staging.");await Runtime.VerifyFileAsync(file.Path,asset,token).ConfigureAwait(false);}
        Safe(Runtime.PackageDirectory);Directory.CreateDirectory(Runtime.PackageDirectory);
        foreach(var asset in IntelRuntimeService.Assets){
            token.ThrowIfCancellationRequested();var target=Path.Combine(Runtime.PackageDirectory,asset.Name);Safe(target);if(File.Exists(target)){await Runtime.VerifyFileAsync(target,asset,token).ConfigureAwait(false);continue;}
            var source=stage.Files.Single(f=>f.RelativePath.Equals(asset.Source,StringComparison.OrdinalIgnoreCase)).Path;var temporary=target+"."+Guid.NewGuid().ToString("N")+".partial";
            try{File.Copy(source,temporary,false);await Runtime.VerifyFileAsync(temporary,asset,token).ConfigureAwait(false);Safe(target);File.Move(temporary,target,false);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        return await Runtime.PrepareAsync(token:token).ConfigureAwait(false);
    }
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
    private static int CountSafeFiles(string root){var pending=new Stack<string>();pending.Push(root);var files=0;while(pending.Count>0)foreach(var path in Directory.EnumerateFileSystemEntries(pending.Pop())){var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked paths are not allowed in Intel staging.");if((attributes&FileAttributes.Directory)!=0)pending.Push(path);else files++;if(files+pending.Count>10000)throw new InvalidDataException("Too many local Intel staging entries.");}return files;}
}

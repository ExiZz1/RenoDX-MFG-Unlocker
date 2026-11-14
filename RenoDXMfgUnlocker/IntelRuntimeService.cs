using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace RenoDXMfgUnlocker;

public sealed record IntelRuntimeAsset(string Name, string Source, string Version, long Size, string Sha256);
public sealed record VerifiedIntelRuntime(string DirectoryPath);
public enum IntelIntegration { D3D12, VulkanExperimental }
public sealed record IntelInstallPlan(string Root, string Executable, string Target, string ContextHash,
    IReadOnlyList<StreamlineComponentPlanEntry> Entries, IntelIntegration Integration=IntelIntegration.D3D12);

/// <summary>Pinned to the pair reviewed by the upstream addon, not the newest arbitrary Intel release.</summary>
public sealed class IntelRuntimeService
{
    public const string Commit = "de0fb9c1c510661c571164e1418ceca8101dab69";
    public const string UpstreamCommit = "4ae69f13835c2a747bb2e09713523d213cd362da";
    // 1.5.8 source bfa2af50ebe5e895011baecdac48c6b77787ea1d has the identical pinned catalog.
    // 1.7.0 (7fa2e69fc3fec7e8c90249c55e247de9dc4a0b5e) keeps this exact dependency catalog.
    public static bool SupportsAddon(string? value) => MfgSettingsCapabilityCatalog.Find(value) is {IntelD3D12:true};
    public static bool SupportsIntegration(string? value,IntelIntegration integration)=>Enum.IsDefined(integration) && SupportsAddon(value) && (integration==IntelIntegration.D3D12 || MfgSettingsCapabilityCatalog.Find(value) is {Vulkan:true});
    public const string BaseUrl = "https://raw.githubusercontent.com/intel/xess/" + Commit + "/";
    public static IReadOnlyList<IntelRuntimeAsset> Assets { get; } = Array.AsReadOnly(new[] {
        new IntelRuntimeAsset("libxess_fg.dll", "bin/libxess_fg.dll", "1.3.1.78", 22957432, "ec5e0c65e075570c6ede72618bb666d0be0c2e10b2ea9762c0fe8cb8e375ab27"),
        new IntelRuntimeAsset("libxell.dll", "bin/libxell.dll", "1.3.2.10", 415368, "d2030dcd694fda8f2ec7e044b13e6db8f0b56d4ba9113a5efad334e3f3ded8c7"),
        new IntelRuntimeAsset("Intel-XeSS-LICENSE.txt", "LICENSE.txt", "", 4194, "784be42c39a4a4d03cf85d88e15e59d75828fca773ce45b8dbfffbd7eaf208df"),
        new IntelRuntimeAsset("Intel-XeSS-third-party-programs.txt", "third-party-programs.txt", "", 35788, "e8d8899dbfc4de0e8ca25560be9358e03c1405d7c956d20a1838c3c709b9b51c") });
    private readonly HttpClient http;
    private readonly string store;
    private readonly Func<string,string> version;
    private readonly Func<string,bool> running;
    public IntelRuntimeService(string store, HttpClient? http = null) : this(store, http,
        p => FileVersionInfo.GetVersionInfo(p).FileVersion ?? "", p => {
            try { var processes=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p));
                var found=processes.Length>0; foreach(var process in processes) process.Dispose(); return found; }
            catch { return true; }
        }) { }
    internal IntelRuntimeService(string store, HttpClient? http, Func<string,string> version, Func<string,bool> running)
    { this.store=Path.GetFullPath(store); this.http=http ?? new HttpClient {Timeout=TimeSpan.FromMinutes(5)}; this.version=version; this.running=running; }
    public string PackageDirectory => Path.Combine(store, Commit);

    public async Task<VerifiedIntelRuntime> PrepareAsync(IProgress<string>? progress=null, CancellationToken token=default)
    {
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(store)!, store);
        Directory.CreateDirectory(PackageDirectory);
        foreach(var asset in Assets)
        {
            token.ThrowIfCancellationRequested();
            var target=Path.Combine(PackageDirectory, asset.Name);
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(store)!,target);
            if(File.Exists(target)) {
                await VerifyFileAsync(target,asset,token).ConfigureAwait(false);
                progress?.Report("Verified cached " + asset.Name); continue;
            }
            var partial=target+"."+Guid.NewGuid().ToString("N")+".partial";
            try {
                using var response=await http.GetAsync(BaseUrl+asset.Source,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var finalUri=response.RequestMessage?.RequestUri;
                if(finalUri is null || finalUri.Scheme!="https" || !finalUri.Host.Equals("raw.githubusercontent.com",StringComparison.OrdinalIgnoreCase)
                    || finalUri.AbsolutePath!=$"/intel/xess/{Commit}/{asset.Source}") throw new InvalidDataException("Intel source redirected away from its pinned official path.");
                await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)) {
                    var buffer=new byte[131072]; long count=0; int read;
                    while((read=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0) {
                        count+=read; if(count>asset.Size) throw new InvalidDataException("Intel download exceeds the pinned size.");
                        await output.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);
                        progress?.Report($"Downloading {asset.Name} · {count:N0} / {asset.Size:N0} bytes");
                    }
                    if(count!=asset.Size) throw new InvalidDataException("Incomplete Intel download.");
                }
                await VerifyFileAsync(partial,asset,token).ConfigureAwait(false);
                StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(store)!,target);
                File.Move(partial,target,false);
            } finally { if(File.Exists(partial)) File.Delete(partial); }
        }
        return new(PackageDirectory);
    }

    public async Task VerifyFileAsync(string path,IntelRuntimeAsset asset,CancellationToken token=default)
    {
        if(!Assets.Contains(asset)) throw new InvalidDataException("Untrusted Intel catalog entry.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(path))!,path);
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,true);
        if(input.Length!=asset.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input,token).ConfigureAwait(false)).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Intel runtime size / SHA-256 mismatch: "+asset.Name);
        if(asset.Version.Length>0) {
            input.Position=0;
            using var pe=new PEReader(input,PEStreamOptions.LeaveOpen);
            if(pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64 || pe.PEHeaders.PEHeader?.Magic!=PEMagic.PE32Plus
                || pe.PEHeaders.CorHeader is not null || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll)==0
                || BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(ReadVersion(path))!=asset.Version)
                throw new InvalidDataException("Intel runtime is not the pinned native x64 DLL version: "+asset.Name);
        }
    }
    private static string? ReadVersion(string path)
    {
        var full=Path.GetFullPath(path);
        if(OperatingSystem.IsWindows() && !full.StartsWith(@"\\?\",StringComparison.Ordinal))
            full=full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;
        return FileVersionInfo.GetVersionInfo(full).FileVersion;
    }
    private async Task VerifyPackageAsync(VerifiedIntelRuntime package,CancellationToken token)
    {
        if(!StreamlineStagingEvidenceService.Canonical(package.DirectoryPath).Equals(StreamlineStagingEvidenceService.Canonical(PackageDirectory),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Intel package must come from the verified application cache.");
        foreach(var asset in Assets) await VerifyFileAsync(Path.Combine(package.DirectoryPath,asset.Name),asset,token).ConfigureAwait(false);
    }

    public async Task<IntelInstallPlan> PreviewAsync(VerifiedIntelRuntime package,string root,string exe,string target,bool confirmedD3d12Inputs,CancellationToken token=default)
    {
        return await PreviewAsync(package,root,exe,target,confirmedD3d12Inputs,IntelIntegration.D3D12,token).ConfigureAwait(false);
    }
    public async Task<IntelInstallPlan> PreviewAsync(VerifiedIntelRuntime package,string root,string exe,string target,bool confirmedInputs,IntelIntegration integration,CancellationToken token=default)
    {
        await VerifyPackageAsync(package,token).ConfigureAwait(false);
        return await Task.Run(()=>BuildPlan(package,root,exe,target,confirmedInputs,integration,token),token).ConfigureAwait(false);
    }
    private IntelInstallPlan BuildPlan(VerifiedIntelRuntime package,string root,string exe,string target,bool confirmed,IntelIntegration integration,CancellationToken token)
    {
        root=Path.GetFullPath(root); exe=Path.GetFullPath(exe); target=Path.GetFullPath(target);
        RuntimeTargetPolicy.Validate(root,exe,target,true);
        if(!confirmed) throw new InvalidDataException("Confirm the selected graphics API and supported input requirements. Intel files alone cannot add game integration.");
        if(!File.Exists(exe) || !exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || !StreamlineStagingEvidenceService.IsStrictChild(root,exe)
            || !StreamlineStagingEvidenceService.IsSameOrChild(root,target) || !Directory.Exists(target)) throw new InvalidDataException("EXE/target must exist inside the selected game root.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(root)!,exe);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,target);
        var exeDirectory=Path.GetDirectoryName(exe)!;
        if(!target.Equals(exeDirectory,StringComparison.OrdinalIgnoreCase)
            && !(target.Equals(Path.Combine(exeDirectory,"_storage_"),StringComparison.OrdinalIgnoreCase)
                && Assets.Take(2).All(a=>File.Exists(Path.Combine(target,a.Name)))))
            throw new InvalidDataException("Intel target must be the running EXE's folder, or its existing _storage_ runtime pair. No guessed root placement is allowed.");
        if(running(exe)) throw new IOException("Close the selected game before preparing or applying Intel runtime changes.");
        var addon=Path.Combine(exeDirectory,"renodx-mfgunlock.addon64");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,addon);
        if(!File.Exists(addon) || !SupportsIntegration(version(addon),integration))
            throw new InvalidDataException("Intel runtime installation requires addon 1.5.8.0 or newer and a supported integration. Experimental Vulkan requires addon 1.7.0 or newer and explicit ReShade Vulkan / DLSS input confirmation.");
        // The explicit destination is the complete write scope. Scanned duplicates are diagnostic only.
        var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase){target};
        var rows=new List<StreamlineComponentPlanEntry>();
        foreach(var folder in folders.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)) foreach(var asset in Assets) {
            token.ThrowIfCancellationRequested();
            var destination=Path.Combine(folder,asset.Name);
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,destination);
            var exists=File.Exists(destination);
            if(Directory.Exists(destination)) throw new IOException("A directory occupies an Intel target filename.");
            var hash=exists?Hash(destination):null;
            var existingVersion=exists?SafeVersion(destination):"Missing";
            if(exists&&hash!=asset.Sha256&&asset.Version.Length>0&&!Version.TryParse(BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(existingVersion),out _))
                throw new InvalidDataException($"Existing Intel DLL version is unknown: {asset.Name}. Review is required; no files will be changed.");
            var action=hash==asset.Sha256?"No change":exists?"Replace existing":"New file";
            rows.Add(new(asset.Name,Path.Combine(package.DirectoryPath,asset.Name),destination,asset.Version.Length==0?"License / notice":asset.Version,asset.Sha256,
                existingVersion,hash,action,action!="No change",false,action=="No change"?"Identical SHA-256; no write or backup is needed.":"Pinned Intel catalog; only the explicitly confirmed destination is changed. License and notice files accompany the DLLs. Runtime activity and game compatibility remain unverified."));
        }
        return new(root,exe,target,Hash(exe)+":"+Hash(addon),rows,integration);
    }
    private string SafeVersion(string path) { try { return version(path); } catch { return "Unknown version"; } }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public async Task<BackupTransactionResult?> InstallAsync(VerifiedIntelRuntime package,IntelInstallPlan displayed,bool confirmedD3d12Inputs,BackupRestoreService backup,IProgress<int>? progress=null,CancellationToken token=default)
    {
        var fresh=await PreviewAsync(package,displayed.Root,displayed.Executable,displayed.Target,confirmedD3d12Inputs,displayed.Integration,token).ConfigureAwait(false);
        if(fresh.ContextHash!=displayed.ContextHash || !fresh.Entries.SequenceEqual(displayed.Entries)) throw new InvalidDataException("Intel source, inventory or target changed after Preview. Refresh and confirm again.");
        var files=new List<BackupTransactionFile>();
        foreach(var row in fresh.Entries.Where(e=>e.IsSelected)) {
            var bytes=await File.ReadAllBytesAsync(row.SourcePath,token).ConfigureAwait(false);
            if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=row.Sha256) throw new InvalidDataException("Intel source changed before write.");
            files.Add(new(Path.GetRelativePath(fresh.Root,row.TargetPath),bytes,row.ExistingSha256 is not null,row.ExistingSha256));
        }
        token.ThrowIfCancellationRequested();
        if(running(fresh.Executable) || Hash(fresh.Executable)+":"+Hash(Path.Combine(Path.GetDirectoryName(fresh.Executable)!,"renodx-mfgunlock.addon64"))!=fresh.ContextHash)
            throw new IOException("Game process, EXE or addon changed before write.");
        // Re-evaluate the complete inventory immediately before handing bytes to the existing transaction authority.
        var final=await PreviewAsync(package,fresh.Root,fresh.Executable,fresh.Target,confirmedD3d12Inputs,displayed.Integration,token).ConfigureAwait(false);
        if(!final.Entries.SequenceEqual(fresh.Entries)) throw new IOException("Intel target inventory changed before write.");
        if(files.Count==0) return null;
        return await backup.ExecuteWithProgressAsync(fresh.Root,files,progress,token).ConfigureAwait(false);
    }
}

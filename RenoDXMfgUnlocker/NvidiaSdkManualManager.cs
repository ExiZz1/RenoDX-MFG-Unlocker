using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace RenoDXMfgUnlocker;

public sealed record NvidiaSdkComponent(string FileName,string SourcePath,string Version,long Size,string Sha256,string Architecture,string Signature,string Category,bool Common,bool Eligible,string Reason,string Product,string Description);
public sealed record NvidiaManualPackage(StreamlineSdkAsset Asset,string ArchivePath,string Directory,IReadOnlyList<NvidiaSdkComponent> Components,int Notices);
public sealed record NvidiaManualPlan(string Root,string Executable,string Destination,string ContextFingerprint,IReadOnlyList<string> Selection,IReadOnlyList<StreamlineComponentPlanEntry> Entries,IReadOnlyList<string> Warnings,bool Acknowledged,bool CanApply);

/// <summary>Official SDK inventory and user-directed file plans. All writes use BackupRestoreService.</summary>
public sealed class NvidiaSdkManualManager
{
    internal const string Disclaimer="Manual DLL management changes files only. It does not add game integration or verify DLSS, Frame Generation or MFG activity. Incomplete or mixed runtimes can cause startup failures.";
    internal const string Consent="I understand that file installation does not establish runtime compatibility and I reviewed the selected changes and game restrictions.";
    private readonly string downloads,staging;
    private readonly BackupRestoreService transactions;
    private readonly Func<string,bool> running;
    public NvidiaSdkManualManager(string downloads,string staging,BackupRestoreService transactions):this(downloads,staging,transactions,IsRunning){}
    internal NvidiaSdkManualManager(string downloads,string staging,BackupRestoreService transactions,Func<string,bool> running)
    {this.downloads=Path.GetFullPath(downloads);this.staging=Path.GetFullPath(staging);this.transactions=transactions;this.running=running;}

    private sealed record Role(string Category,bool Common,bool Deployable,string? ProductToken,string? DescriptionToken,string[] Dependencies);
    private static Role Classify(string name)=>name.ToLowerInvariant() switch {
        "sl.interposer.dll"=>new("Streamline loader",true,true,"NVIDIA STREAMLINE","SL.INTERPOSER",["sl.common.dll"]),
        "sl.common.dll"=>new("Streamline common runtime",true,true,"NVIDIA STREAMLINE","SL.COMMON",["sl.interposer.dll"]),
        "sl.dlss.dll"=>new("DLSS Super Resolution plugin",true,true,"NVIDIA STREAMLINE","SL.DLSS PLUGIN",["sl.common.dll","sl.interposer.dll","nvngx_dlss.dll"]),
        "sl.dlss_d.dll"=>new("DLSS Ray Reconstruction plugin",true,true,"NVIDIA STREAMLINE","SL.DLSS_RR",["sl.common.dll","sl.interposer.dll","nvngx_dlssd.dll"]),
        "sl.dlss_g.dll"=>new("DLSS Frame Generation plugin",true,true,"NVIDIA STREAMLINE","SL.DLSS_G",["sl.common.dll","sl.interposer.dll","sl.reflex.dll","sl.pcl.dll","nvngx_dlssg.dll"]),
        "sl.reflex.dll"=>new("Reflex latency plugin",true,true,"NVIDIA STREAMLINE","SL.REFLEX",["sl.common.dll","sl.interposer.dll","sl.pcl.dll"]),
        "sl.pcl.dll"=>new("PC latency statistics plugin",true,true,"NVIDIA STREAMLINE","SL.PCL",["sl.common.dll","sl.interposer.dll"]),
        "sl.nis.dll"=>new("NVIDIA Image Scaling plugin",true,true,"NVIDIA STREAMLINE","SL.NIS",["sl.common.dll","sl.interposer.dll"]),
        "sl.deepdvc.dll"=>new("DeepDVC vibrance plugin",false,true,"NVIDIA STREAMLINE","SL.DEEPDVC",["sl.common.dll","sl.interposer.dll","nvngx_deepdvc.dll"]),
        "sl.directsr.dll"=>new("DirectSR plugin",false,true,"NVIDIA STREAMLINE","SL.DIRECTSR",["sl.common.dll","sl.interposer.dll"]),
        "sl.nvperf.dll"=>new("Profiling / tools plugin",false,true,"NVIDIA STREAMLINE","SL.NVPERF",[]),
        "nvngx_dlss.dll"=>new("DLSS Super Resolution provider",true,true,"NVIDIA Deep Learning SuperSampling",null,[]),
        "nvngx_dlssd.dll"=>new("DLSS Ray Reconstruction provider",true,true,"NVIDIA DLSS Ray Reconstruction",null,[]),
        "nvngx_dlssg.dll"=>new("DLSS Frame Generation provider",true,true,"NVIDIA DLSS-G",null,[]),
        "nvngx_deepdvc.dll"=>new("DeepDVC provider",false,true,"NVIDIA DEEPDVC",null,[]),
        "nvlowlatencyvk.dll"=>new("Vulkan low-latency support library",false,true,null,null,[]),
        _=>new("Unreviewed SDK binary",false,false,null,null,[])
    };
    private static bool ProductionDll(string path)=>path.StartsWith("bin/x64/",StringComparison.OrdinalIgnoreCase)&&path.Split('/').Length==3&&path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase);
    private static bool Notice(string path){var leaf=Path.GetFileName(path);return (leaf.Contains("license",StringComparison.OrdinalIgnoreCase)||leaf.Contains("notice",StringComparison.OrdinalIgnoreCase)||leaf.Contains("third-party",StringComparison.OrdinalIgnoreCase))&&Path.GetExtension(leaf).ToLowerInvariant() is "" or ".txt" or ".md" or ".mit" or ".pdf";}
    private static async Task ValidateMembersAsync(string archive,IReadOnlyList<StagedStreamlineFile> files,CancellationToken token)
    {
        await StreamlineStagingEvidenceService.ValidateArchiveMembersAsync(archive,files,token).ConfigureAwait(false);
        using var zip=ZipFile.OpenRead(archive);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)if(!paths.Add(StreamlineArchiveStager.ValidateEntryName(entry.FullName).TrimEnd('/')))throw new InvalidDataException("The archive contains duplicate file paths.");
    }

    private async Task VerifyArchiveAsync(StreamlineSdkAsset asset,string archive,CancellationToken token)
    {
        if(!StreamlineReleaseFilter.IsSdkArchiveName(asset.Name)||asset.Sha256 is not {Length:64} hash||!hash.All(Uri.IsHexDigit)
            ||asset.Size<=0||asset.Size>1024L*1024*1024||!Uri.TryCreate(asset.DownloadUrl,UriKind.Absolute,out var url)
            ||url.Scheme!="https"||!url.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)
            ||!Uri.UnescapeDataString(url.AbsolutePath).Equals("/NVIDIA-RTX/Streamline/releases/download/"+asset.ReleaseTag+"/"+asset.Name,StringComparison.Ordinal))
            throw new InvalidDataException("Official SDK metadata and SHA-256 are required for manual DLL management.");
        if(!StreamlineStagingEvidenceService.IsStrictChild(downloads,archive)||!File.Exists(archive))throw new InvalidDataException("The selected SDK archive is missing from the download store.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(archive))!,archive);
        if(new FileInfo(archive).Length!=asset.Size||!await StreamlineDigestVerifier.MatchesAsync(archive,hash,token).ConfigureAwait(false))throw new InvalidDataException("The SDK archive size or SHA-256 changed. No game files were written.");
    }
    public async Task<NvidiaManualPackage> PrepareAsync(StreamlineSdkAsset asset,string archive,CancellationToken token=default)
    {
        await VerifyArchiveAsync(asset,archive,token).ConfigureAwait(false);
        await ValidateMembersAsync(archive,[],token).ConfigureAwait(false);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(staging)!,staging);Directory.CreateDirectory(staging);
        var directory=Path.Combine(staging,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var notices=0;
        try {
            using(var zip=ZipFile.OpenRead(archive))foreach(var entry in zip.Entries){
                token.ThrowIfCancellationRequested();var relative=StreamlineArchiveStager.ValidateEntryName(entry.FullName);
                if(relative.EndsWith('/')||(!ProductionDll(relative)&&!Notice(relative)))continue;
                if(Notice(relative)&&entry.Length>8*1024*1024)throw new InvalidDataException("SDK notice exceeds the safe text-file limit.");
                var target=Path.GetFullPath(Path.Combine(directory,relative.Replace('/',Path.DirectorySeparatorChar)));
                if(!StreamlineStagingEvidenceService.IsStrictChild(directory,target))throw new InvalidDataException("Unsafe SDK staging path.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);StreamlineStagingEvidenceService.EnsureNoReparsePoints(directory,target);
                using var input=entry.Open();using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                var buffer=new byte[81920];long count=0;int n;while((n=await input.ReadAsync(buffer,token).ConfigureAwait(false))!=0){count+=n;if(count>entry.Length)throw new InvalidDataException("SDK entry exceeds its declared length.");await output.WriteAsync(buffer.AsMemory(0,n),token).ConfigureAwait(false);}
                if(count!=entry.Length)throw new InvalidDataException("Truncated SDK entry.");if(Notice(relative))notices++;
            }
            var runtime=Path.Combine(directory,"bin","x64");if(!Directory.Exists(runtime))throw new InvalidDataException("No production bin/x64 DLLs were found in the SDK.");
            var components=Directory.EnumerateFiles(runtime,"*.dll").Select(p=>Inspect(p,true)).OrderBy(c=>c.FileName,StringComparer.OrdinalIgnoreCase).ToArray();
            if(components.Length==0||components.Length>256)throw new InvalidDataException("The SDK runtime inventory is empty or exceeds the safety limit.");
            return new(asset,Path.GetFullPath(archive),directory,Array.AsReadOnly(components),notices);
        }catch{try{if(Directory.Exists(directory))Directory.Delete(directory,true);}catch{}throw;}
    }

    internal static NvidiaSdkComponent Inspect(string path,bool officialSource)
    {
        var name=Path.GetFileName(path);var role=Classify(name);var version="Unknown";var product="";var description="";var architecture="Unsupported architecture";var signature="Not checked";
        string hash="";long size=0;bool native=false,authentic=false,identity=false,production=true;
        try {
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(path))!,path);
            using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);size=input.Length;if(size<1024||size>512L*1024*1024)throw new InvalidDataException("Unsupported runtime binary size.");
            hash=Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();input.Position=0;
            using(var pe=new PEReader(input,PEStreamOptions.LeaveOpen))native=pe.PEHeaders.CoffHeader.Machine==Machine.Amd64&&pe.PEHeaders.PEHeader?.Magic==PEMagic.PE32Plus&&pe.PEHeaders.CorHeader is null&&(pe.PEHeaders.CoffHeader.Characteristics&Characteristics.Dll)!=0;
            if(!native)throw new InvalidDataException("Unsupported runtime architecture or file type.");architecture="x64";
            var metadata=FileVersionInfo.GetVersionInfo(path);version=string.IsNullOrWhiteSpace(metadata.FileVersion)?"Unknown":TechnicalVersionDisplay.Format(metadata.FileVersion);product=metadata.ProductName??"";description=metadata.FileDescription??"";
            DlssGProviderVerifier.VerifyAuthenticode(path);
            using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            authentic=certificate.GetNameInfo(X509NameType.SimpleName,false)=="NVIDIA Corporation";
            signature=authentic?"NVIDIA signature verified":"Publisher not verified";
            identity=role.ProductToken is not null&&product.Contains(role.ProductToken,StringComparison.OrdinalIgnoreCase)&&(role.DescriptionToken is null||description.Contains(role.DescriptionToken,StringComparison.OrdinalIgnoreCase));
            if(officialSource&&(name.StartsWith("sl.",StringComparison.OrdinalIgnoreCase)||name.StartsWith("nvngx_",StringComparison.OrdinalIgnoreCase))){production=description.Contains("PRODUCTION",StringComparison.OrdinalIgnoreCase);identity&=production;}
            // This signed metadata-free Vulkan support file is identifiable only by official archive membership.
            if(officialSource&&name.Equals("NvLowLatencyVk.dll",StringComparison.OrdinalIgnoreCase))identity=true;
        }catch(Exception e)when(e is IOException or InvalidDataException or BadImageFormatException or UnauthorizedAccessException or CryptographicException){signature=native?"Signature not verified":"Not checked";}
        var eligible=native&&authentic&&identity&&role.Deployable;
        var reason=!role.Deployable?"This SDK binary has no reviewed deployable component identity.":!native?"Only native x64 runtime DLLs can be selected.":!authentic?"A valid NVIDIA publisher signature is required.":!production?"Development/debug SDK binaries cannot be installed.":!identity?"The component identity could not be established.":"Official production SDK member; file-operation eligibility is not runtime compatibility.";
        return new(name,path,version,size,hash,architecture,signature,role.Category,role.Common,eligible,reason,product,description);
    }
    private async Task ValidatePackageAsync(NvidiaManualPackage package,CancellationToken token)
    {
        await VerifyArchiveAsync(package.Asset,package.ArchivePath,token).ConfigureAwait(false);
        if(!StreamlineStagingEvidenceService.IsStrictChild(staging,package.Directory)||!Directory.Exists(package.Directory)||package.Components.Count==0||package.Components.Count>256||package.Components.Select(c=>c.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=package.Components.Count)
            throw new InvalidDataException("Manual SDK inventory is missing or ambiguous.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(staging)!,package.Directory);
        foreach(var component in package.Components){
            var expected=Path.Combine(package.Directory,"bin","x64",component.FileName);
            if(component.FileName!=Path.GetFileName(component.FileName)||!Path.GetFullPath(expected).Equals(Path.GetFullPath(component.SourcePath),StringComparison.OrdinalIgnoreCase)||!File.Exists(expected))throw new InvalidDataException("A manual SDK source is missing or outside the verified staging directory.");
            var actual=Inspect(expected,true);if(actual!=component)throw new InvalidDataException("A prepared SDK component changed. Prepare the SDK again.");
        }
        await ValidateMembersAsync(package.ArchivePath,package.Components.Select(c=>new StagedStreamlineFile(c.FileName,"bin/x64/"+c.FileName,c.Sha256,c.Version)).ToArray(),token).ConfigureAwait(false);
    }

    internal void ValidateDestination(string root,string exe,string target)
    {
        RuntimeTargetPolicy.Validate(root,exe,target,false);
        var full=Path.GetFullPath(exe);var parts=full.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries);
        if(ExecutableCandidatePolicy.IsAuxiliary(Path.GetFileNameWithoutExtension(exe))||NvidiaComponentScanner.IsBackupPath(full)||ExecutableCandidatePolicy.IsCopyDirectory(Path.GetDirectoryName(full)!)||parts.Any(p=>p.Equals("copied",StringComparison.OrdinalIgnoreCase)||p.Equals("helper",StringComparison.OrdinalIgnoreCase)||p.Equals("helpers",StringComparison.OrdinalIgnoreCase)||p.Equals("development",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Backup, copied and helper directories are not installation targets.");
        if(Path.GetFullPath(root).TrimEnd('\\','/').Equals(Path.GetPathRoot(Path.GetFullPath(root))?.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)
            ||IsProtectedPath(target)||StreamlineStagingEvidenceService.IsSameOrChild(AppContext.BaseDirectory,target)
            ||StreamlineStagingEvidenceService.IsSameOrChild(downloads,target)||StreamlineStagingEvidenceService.IsSameOrChild(staging,target))throw new InvalidDataException("Protected application, system and cache directories are not installation targets.");
        using var input=File.OpenRead(exe);using var pe=new PEReader(input);
        if(pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64||pe.PEHeaders.PEHeader?.Magic!=PEMagic.PE32Plus||pe.PEHeaders.CorHeader is not null||(pe.PEHeaders.CoffHeader.Characteristics&Characteristics.Dll)!=0)throw new InvalidDataException("Select a native Windows x64 game executable.");
        if(running(exe))throw new IOException("Close the selected game before runtime maintenance.");
    }
    internal static bool IsProtectedPath(string path)
    {
        var roots=new List<string>{Environment.GetFolderPath(Environment.SpecialFolder.Windows)};
        foreach(var parent in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}.Where(p=>p.Length>0))
            foreach(var vendor in new[]{"NVIDIA","NVIDIA Corporation","Intel","AMD"})roots.Add(Path.Combine(parent,vendor));
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);if(Path.GetPathRoot(windows) is {} drive)roots.Add(Path.Combine(drive,"NVIDIA"));
        return roots.Where(p=>p.Length>0).Any(root=>StreamlineStagingEvidenceService.IsSameOrChild(root,path));
    }
    private static bool IsRunning(string exe){try{return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Any(p=>{p.Dispose();return true;});}catch{return true;}}
    private static string Hash(string path){using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
    public async Task<NvidiaManualPlan> PreviewAsync(NvidiaManualPackage package,string root,string exe,string target,IReadOnlySet<string> selection,bool acknowledged,CancellationToken token=default)
    {
        ValidateDestination(root,exe,target);await ValidatePackageAsync(package,token).ConfigureAwait(false);
        if(selection.Any(n=>!package.Components.Any(c=>c.FileName.Equals(n,StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("The selection contains an unknown SDK component.");
        var rows=new List<StreamlineComponentPlanEntry>();var warnings=new HashSet<string>(StringComparer.Ordinal){Disclaimer};
        foreach(var source in package.Components.Where(c=>selection.Contains(c.FileName))){
            token.ThrowIfCancellationRequested();var path=Path.Combine(target,source.FileName);StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,path);
            string? hash=null;var oldVersion="Not found";var action="New file";var reason="Explicitly selected SDK file; target absent. Authenticated transaction ownership will be recorded.";
            if(!source.Eligible){action="Blocked";reason=source.Reason;}
            else if(Directory.Exists(path)){action="Blocked";reason="A directory occupies the target file path.";}
            else if(File.Exists(path)){
                hash=Hash(path);var rawVersion=FileVersionInfo.GetVersionInfo(path).FileVersion;oldVersion=string.IsNullOrWhiteSpace(rawVersion)?"Unknown":TechnicalVersionDisplay.Format(rawVersion);
                if(hash.Equals(source.Sha256,StringComparison.OrdinalIgnoreCase)){action="No change";reason="Identical SHA-256; no write or backup is needed.";}
                else {var existing=Inspect(path,false);action=existing.Eligible&&existing.Category==source.Category?"Replace existing":"Blocked";reason=action=="Blocked"?"Existing component identity or NVIDIA publisher is unverified; replacement is blocked.":"Explicit replacement of the same authenticated NVIDIA component; original bytes will be backed up.";}
            }
            rows.Add(new(source.FileName,source.SourcePath,path,source.Version,source.Sha256,oldVersion,hash,action,action!="No change",false,reason));
            foreach(var dependency in Classify(source.FileName).Dependencies)if(!selection.Contains(dependency)&&!File.Exists(Path.Combine(target,dependency)))warnings.Add("Incomplete dependency selection: "+dependency);
            if(source.FileName.Equals("sl.directsr.dll",StringComparison.OrdinalIgnoreCase))warnings.Add("DirectSR requires game/API integration and system dependencies; DLL copying does not provide them.");
            if(source.FileName.Equals("NvLowLatencyVk.dll",StringComparison.OrdinalIgnoreCase))warnings.Add("Vulkan support DLL only; no system layer is installed or enabled.");
            if(source.FileName.Equals("sl.nvperf.dll",StringComparison.OrdinalIgnoreCase))warnings.Add("Profiling plugin only; engine/tool integration and dependencies are still required. It does not provide Frame Generation.");
        }
        var selectedNames=selection.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        var fingerprint=Hash(exe)+"|"+StreamlineStagingEvidenceService.Canonical(root)+"|"+Path.GetFullPath(target)+"|"+package.Asset.Sha256+"|"+string.Join("|",selectedNames);
        if(rows.Count>0)warnings.Add("Dependency filenames are presence observations. Existing versions and actual runtime compatibility are not verified.");
        return Acknowledge(new(Path.GetFullPath(root),Path.GetFullPath(exe),Path.GetFullPath(target),fingerprint,selectedNames,rows,warnings.ToArray(),false,false),acknowledged);
    }
    public static NvidiaManualPlan Acknowledge(NvidiaManualPlan plan,bool acknowledged)=>plan with {Acknowledged=acknowledged,CanApply=acknowledged&&plan.Entries.Any(r=>r.IsSelected)&&plan.Entries.All(r=>r.Action!="Blocked")};
    public async Task<StreamlineComponentInstallResult> ApplyAsync(NvidiaManualPackage package,NvidiaManualPlan displayed,IProgress<int>? progress=null,CancellationToken token=default)
    {
        if(!displayed.Acknowledged||!displayed.CanApply)throw new InvalidOperationException("Review and acknowledge a valid manual DLL Preview before Apply.");
        var fresh=await PreviewAsync(package,displayed.Root,displayed.Executable,displayed.Destination,displayed.Selection.ToHashSet(StringComparer.OrdinalIgnoreCase),true,token).ConfigureAwait(false);
        if(!fresh.CanApply||fresh.ContextFingerprint!=displayed.ContextFingerprint||!fresh.Entries.SequenceEqual(displayed.Entries))throw new InvalidOperationException("The manual DLL Preview changed. Refresh and confirm it again.");
        var selected=fresh.Entries.Where(r=>r.IsSelected).ToArray();var files=new List<BackupTransactionFile>();
        foreach(var row in selected){var bytes=await File.ReadAllBytesAsync(row.SourcePath,token).ConfigureAwait(false);if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(row.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The selected source SHA-256 changed before Apply.");files.Add(new(Path.GetRelativePath(fresh.Root,row.TargetPath).Replace('\\','/'),bytes,row.ExistingSha256 is not null,row.ExistingSha256));}
        var transaction=await transactions.ExecuteGuardedAsync(fresh.Root,files,i=>{
            ValidateDestination(fresh.Root,fresh.Executable,fresh.Destination);
            if(!fresh.ContextFingerprint.StartsWith(Hash(fresh.Executable)+"|",StringComparison.Ordinal)||!Hash(selected[i].SourcePath).Equals(selected[i].Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Source or game EXE changed during the file operation.");
        },progress,token).ConfigureAwait(false);
        foreach(var row in selected)if(!Hash(row.TargetPath).Equals(row.Sha256,StringComparison.OrdinalIgnoreCase)){
            try{await transactions.RestoreAsync(fresh.Root,transaction.TransactionId,CancellationToken.None).ConfigureAwait(false);}catch(Exception e){throw new BackupTransactionException("Post-write verification failed; Restore needs review.",transaction.TransactionId,e);}
            throw new BackupTransactionException("Post-write verification failed; recorded changes were restored.",transaction.TransactionId,new IOException("Hash mismatch"));
        }
        return new(transaction,selected.Select(r=>r.TargetPath).ToArray());
    }
}

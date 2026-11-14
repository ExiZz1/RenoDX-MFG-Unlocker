using System.Diagnostics;
using System.IO;
namespace RenoDXMfgUnlocker;

/// <summary>User-directed file maintenance, not a game compatibility grant.</summary>
public sealed class AdvancedRuntimeInstaller
{
    private readonly string downloads, staging;
    private readonly BackupRestoreService backup;
    private readonly Func<string,string> version;
    private readonly Func<string,bool> running;
    public AdvancedRuntimeInstaller(string downloads, string staging, BackupRestoreService backup)
        : this(downloads, staging, backup, p => FileVersionInfo.GetVersionInfo(p).FileVersion ?? "",
            p => { try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p)).Any(x => { x.Dispose(); return true; }); } catch { return true; } }) { }
    internal AdvancedRuntimeInstaller(string downloads, string staging, BackupRestoreService backup, Func<string,string> version, Func<string,bool> running)
    { this.downloads=downloads; this.staging=staging; this.backup=backup; this.version=version; this.running=running; }

    private static readonly string[] Core = ["sl.interposer.dll","sl.common.dll","sl.dlss_g.dll","sl.reflex.dll","sl.pcl.dll"];
    private static readonly Dictionary<string,string?> Plugins = new(StringComparer.OrdinalIgnoreCase)
    { ["sl.dlss.dll"]="nvngx_dlss.dll", ["sl.dlss_d.dll"]="nvngx_dlssd.dll", ["sl.nis.dll"]=null };

    private (StreamlineComponentInstaller Installer, HashSet<string> Required, string Fingerprint) Prepare(VerifiedStreamlineStage stage, string root, string exe, string target, bool d3d12, VerifiedDlssGProvider? imported = null)
    {
        if (!d3d12) throw new InvalidDataException("Manual Dynamic runtime maintenance requires explicit D3D12 confirmation. Vulkan and unknown APIs are blocked.");
        RuntimeTargetPolicy.Validate(root,exe,target,false);
        if (!File.Exists(exe) || !exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)
            || !StreamlineStagingEvidenceService.IsStrictChild(root,exe)
            || !StreamlineStagingEvidenceService.IsSameOrChild(root,target) || !Directory.Exists(target))
            throw new InvalidDataException("Select an existing EXE and confirm a target directory inside its game root.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(root))!,exe);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,target);
        if (running(exe)) throw new IOException("Close the selected game before runtime maintenance.");
        var required = new HashSet<string>(Core,StringComparer.OrdinalIgnoreCase);
        foreach(var path in Directory.EnumerateFiles(target,"*.dll"))
        {
            var name=Path.GetFileName(path);
            if(!name.StartsWith("sl.",StringComparison.OrdinalIgnoreCase)) continue;
            if(!required.Contains(name) && !Plugins.ContainsKey(name))
                throw new InvalidDataException($"Unknown Streamline dependency {name}. No files will be changed.");
            required.Add(name);
        }
        // The runtime updater maintains an existing integration. It cannot create one.
        foreach(var name in Core)
            if(!File.Exists(Path.Combine(target,name))) throw new InvalidDataException($"Missing existing integration component {name}; adding a new integration is not supported.");
        var sourceVersion = stage.Asset.ReleaseTag.TrimStart('v','V') switch
        { "2.14.16" => "2.14.16.0", "2.14.1" => "2.14.1.0", _ => throw new InvalidDataException("Manual mode currently supports official SDK 2.14.1 and 2.14.16 only.") };
        var provider = Path.Combine(target,"nvngx_dlssg.dll");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,provider);
        var expectedProvider=sourceVersion=="2.14.16.0"?"310.9.16.0":"310.9.1.0";
        if(imported is null && (!File.Exists(provider) || Normalize(version(provider))!=expectedProvider))
            throw new InvalidDataException($"DLSS-G must already be {expectedProvider} in the confirmed target. Choose Use SDK DLSS-G or Import DLSS-G DLL to select a verified replacement, or use Setup Wizard. The selected provider must pass NVIDIA signature, x64, version and hash checks.");
        if(imported is not null) {
            new DlssGProviderVerifier().Revalidate(imported);
            var expected=sourceVersion=="2.14.16.0" ? "310.9.16.0" : "310.9.1.0";
            if(imported.Version!=expected) throw new InvalidDataException($"Selected SDK requires the documented provider pair {expected}; selected {imported.Version}.");
        }
        foreach(var name in required)
        {
            var source=stage.Files.SingleOrDefault(x=>x.FileName.Equals(name,StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"SDK stage lacks required plugin {name}. Stage the complete official package again.");
            var sourcePath=Path.Combine(stage.StagingDirectory,source.RelativePath.Replace('/',Path.DirectorySeparatorChar));
            if(!File.Exists(sourcePath) || Normalize(version(sourcePath)) != sourceVersion)
                throw new InvalidDataException($"Missing, unknown or mixed production source version for {name}.");
            var existing=Path.Combine(target,name);
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,existing);
            var oldVersion=Normalize(version(existing));
            if(oldVersion.Length==0 || !oldVersion.StartsWith("2.",StringComparison.Ordinal))
                throw new InvalidDataException($"Cannot identify existing Streamline 2.x component {name}.");
            if(Plugins.TryGetValue(name,out var dependency) && dependency is not null)
            {
                var path=Path.Combine(target,dependency);
                StreamlineStagingEvidenceService.EnsureNoReparsePoints(root,path);
                if(!File.Exists(path) || Normalize(version(path)).Length==0)
                    throw new InvalidDataException($"Required provider {dependency} is missing or unreadable. It will not be copied from Streamline.");
            }
        }
        var dependencies=required.Where(Plugins.ContainsKey).Select(n=>Plugins[n]).Where(n=>n is not null).Select(n=>Path.Combine(target,n!)).Append(provider).Append(exe).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
        string Fingerprint() => string.Join("|",dependencies.Select(p=>p.ToUpperInvariant()+":"+(File.Exists(p)?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))):"missing")));
        var fingerprint=Fingerprint()+(imported is null ? "" : "|selected:"+imported.Sha256);
        var relative=Path.GetRelativePath(root,target).Replace(Path.DirectorySeparatorChar,'/');
        return (new StreamlineComponentInstaller(downloads,staging,backup,new ManualCatalog(required,relative), () => { if(running(exe) || Fingerprint()+(imported is null ? "" : "|selected:"+new DlssGProviderVerifier().Revalidate(imported).Sha256)!=fingerprint) throw new IOException("Game process or unchanged provider/EXE dependency changed before write."); }),required,fingerprint);
    }
    private static string Normalize(string value) => BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(value);
    public async Task<StreamlineComponentPlan> PreviewAsync(VerifiedStreamlineStage stage,string root,string exe,string target,bool d3d12,CancellationToken token=default, VerifiedDlssGProvider? imported=null)
    {
        var prepared=await Task.Run(()=>Prepare(stage,root,exe,target,d3d12,imported),token).ConfigureAwait(false);
        var plan=await prepared.Installer.CreatePlanAsync(stage,"manual-user-directed",root,target,prepared.Required,token);
        if(imported is not null) {
            var row=ProviderRow(imported,target);
            plan=plan with { Entries=plan.Entries.Append(row).ToArray(), SelectedNewFiles=plan.SelectedNewFiles+(row.Action=="New file"?1:0), SelectedReplacements=plan.SelectedReplacements+(row.Action=="Replace existing"?1:0) };
        }
        return plan with { Entries = plan.Entries.Select(x => x.IsSelected ? x with { Message = "Manual replacement with backup. Game compatibility has not been certified." } : x with {Message=x.Action=="No change"?x.Message:"Not required by the existing integration; not selected for installation."}).ToArray(), CanInstall=plan.SelectedNeedsReview==0&&plan.SelectedBlocked==0&&(plan.SelectedNewFiles+plan.SelectedReplacements)>0, ContextFingerprint = prepared.Fingerprint, Message = plan.Entries.All(x=>!x.IsSelected)?"No changes needed. Identical files are not rewritten; runtime activity remains unverified.":$"Manual / Advanced: {plan.SelectedNewFiles} new / {plan.SelectedReplacements} replacement file(s). Compatibility is unverified. Review all files and confirm before writing." };
    }
    public async Task<StreamlineComponentInstallResult> InstallAsync(VerifiedStreamlineStage stage,string root,string exe,string target,bool d3d12,StreamlineComponentPlan displayed,IProgress<int>? progress=null,CancellationToken token=default, VerifiedDlssGProvider? imported=null)
    {
        var prepared=await Task.Run(()=>Prepare(stage,root,exe,target,d3d12,imported),token).ConfigureAwait(false);
        if (displayed.ContextFingerprint != prepared.Fingerprint) throw new InvalidDataException("EXE or provider dependencies changed after manual preview. Refresh first.");
        var core=displayed with { Entries=displayed.Entries.Where(x=>x.FileName!="nvngx_dlssg.dll").ToArray() };
        if(imported is null) {
            if(displayed.Entries.Any(x=>x.FileName=="nvngx_dlssg.dll")) throw new InvalidDataException("Provider selection changed.");
            return await prepared.Installer.InstallAsync(stage,"manual-user-directed",root,target,prepared.Required,core,progress,token);
        }
        var fresh=ProviderRow(imported,target);
        var old=displayed.Entries.SingleOrDefault(x=>x.FileName=="nvngx_dlssg.dll");
        if(old is null || old with { Message = fresh.Message } != fresh) throw new InvalidDataException("DLSS-G source or target changed after preview.");
        return await prepared.Installer.InstallWithProviderAsync(stage,"manual-user-directed",root,target,prepared.Required,core,fresh,progress,token);
    }
    private StreamlineComponentPlanEntry ProviderRow(VerifiedDlssGProvider imported,string target)
    {
        new DlssGProviderVerifier().Revalidate(imported);
        var path=Path.Combine(target,"nvngx_dlssg.dll");
        var exists=File.Exists(path);
        var hash=exists?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant():null;
        var identical=string.Equals(hash,imported.Sha256,StringComparison.OrdinalIgnoreCase);
        return new("nvngx_dlssg.dll",imported.Path,path,imported.Version,imported.Sha256,exists?version(path):"Missing",hash,
            identical?"No change":exists?"Replace existing":"New file",!identical,false,identical?"Identical SHA-256; no write or backup is needed.":"NVIDIA Authenticode verified; manual file operation, runtime compatibility unconfirmed.");
    }    private sealed class ManualCatalog(HashSet<string> files,string target):IStreamlineCompatibilityCatalog
    {
        public StreamlineCompatibilityGrant? FindGrant(string id,string file) => id=="manual-user-directed" && files.Contains(file)
            ? new(id,file,target,false,true,"Explicit manual file maintenance; no compatibility certification") : null;
    }
}

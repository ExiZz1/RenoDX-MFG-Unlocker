using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public sealed record RuntimeLogInput(string Path, string Text, DateTimeOffset LastWriteUtc);
public sealed record RuntimeProcessSnapshot(int? Id, DateTimeOffset? StartedUtc, string Description);
public enum RuntimeDiskLocation { Unknown, BesideExecutable, GameSubfolder, ConfirmedBackup }
public sealed record RuntimeDiskComponent(string Name, string Version, RuntimeDiskLocation Location, bool BackupFolderMarker = false);
public sealed record RuntimeDiagnosticReport(IReadOnlyList<string> Statuses, IReadOnlyList<string> Evidence, IReadOnlyList<string> Warnings,IReadOnlyList<RuntimeLogInput>? Logs=null,
    IReadOnlyList<RuntimeDiskComponent>? DiskComponents=null);

/// <summary>Read-only, bounded log evidence. No writes, hooks, driver edits or inferred compatibility grants.</summary>
public sealed class RuntimeDiagnosticsService
{
    internal const string DuplicateAddonWarning = "Multiple MFG Unlock addon files are present beside the selected EXE.";
    internal const string FactoryHookWarning = "The recorded session reported a XeMFG DXGI factory hook failure.";
    internal const string EarlyLoadWarning = "The recorded session reported an early addon loading issue or a pending restart.";
    internal const string ProviderGateWarning = "The recorded session reported that the DLSS-G architecture gate was refused.";

    internal static IReadOnlyList<string> FindAddonCopies(string root, string executable)
    {
        var folder = Path.GetDirectoryName(executable)!;
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(root, folder);
        return Directory.EnumerateFiles(folder, "*mfgunlock*.addon64", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static IReadOnlyList<RuntimeDiskComponent> BuildDiskInventory(IEnumerable<NvidiaComponentEntry> entries, string executable, string gameRoot)
    {
        var exeFolder = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        return entries.Where(e => e.Status == "Found").Select(entry =>
        {
            var location = RuntimeDiskLocation.Unknown;
            try
            {
                var fullPath = Path.GetFullPath(entry.Path);
                // A conventional backup folder name does not authenticate the file's provenance.
                if (!entry.IsBackup && StreamlineStagingEvidenceService.IsStrictChild(gameRoot, fullPath))
                    location = string.Equals(Path.GetDirectoryName(fullPath), exeFolder, StringComparison.OrdinalIgnoreCase)
                        ? RuntimeDiskLocation.BesideExecutable : RuntimeDiskLocation.GameSubfolder;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
            return new RuntimeDiskComponent(entry.Name, entry.Version, location, entry.IsBackup);
        }).ToArray();
    }

    private const long MaxLogBytes=8*1024*1024;
    internal static RuntimeDiskComponent[] BackupInventory(IEnumerable<BackupTransactionInfo> transactions) =>
        transactions.Where(t => t.ManifestVerified).SelectMany(t => t.Files)
            .Where(f => f.OriginalBackupVerified)
            .Select(f => Path.GetFileName(f.RelativeTargetPath))
            .Where(n => NvidiaComponentScanner.KnownComponentNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Select(n => new RuntimeDiskComponent(n, "", RuntimeDiskLocation.ConfirmedBackup)).ToArray();

    public async Task<RuntimeDiagnosticReport> InspectAsync(string executable,string gameRoot,DateTimeOffset? installedUtc=null,bool installationHashesVerified=false,CancellationToken token=default,bool configurationHashesVerified=false)
    {
        executable=Path.GetFullPath(executable);
        if(!File.Exists(executable) || !StreamlineStagingEvidenceService.IsStrictChild(gameRoot,executable)) throw new InvalidDataException("Select the current game's existing EXE.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(executable)!,executable);
        var logs=new List<RuntimeLogInput>();var warnings=new List<string>();
        foreach(var folder in new[]{Path.GetDirectoryName(executable)!,Path.Combine(Path.GetDirectoryName(executable)!,"_storage_")})
        foreach(var name in new[]{"ReShade.log","MFGAdaUnlock-XeMFG.log"}) {
            token.ThrowIfCancellationRequested();var path=Path.Combine(folder,name);
            try {
                StreamlineStagingEvidenceService.EnsureNoReparsePoints(gameRoot,path);
                if(!File.Exists(path))continue;
                var before=new FileInfo(path);var stamp=before.LastWriteTimeUtc;var length=before.Length;
                if(length>MaxLogBytes){warnings.Add(name+": exceeds the 8 MiB limit; no truncated session is interpreted.");continue;}
                using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,true);
                using var reader=new StreamReader(file,new UTF8Encoding(false,true),true);
                var builder=new StringBuilder();var buffer=new char[8192];int read;
                while((read=await reader.ReadAsync(buffer,token).ConfigureAwait(false))>0) {
                    if(builder.Length+read>MaxLogBytes)throw new InvalidDataException("Log grew beyond the read limit; no partial session interpreted.");
                    builder.Append(buffer,0,read);
                }
                var text=builder.ToString();
                before.Refresh();
                if(before.LastWriteTimeUtc!=stamp || before.Length!=length){warnings.Add(name+": changed while reading; refresh for a consistent snapshot.");continue;}
                if(text.Length>MaxLogBytes)throw new InvalidDataException("Log expanded beyond the read limit.");
                logs.Add(new(path,text,new DateTimeOffset(stamp,TimeSpan.Zero)));
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException) {warnings.Add(name+": "+ex.Message);}
        }
        var scan=await Task.Run(()=>new NvidiaComponentScanner().Scan(executable,gameRoot,token),token).ConfigureAwait(false);
        var version=FileVersionInfo.GetVersionInfo(executable).FileVersion??"";
        var parsed=Analyze(executable,version,FindProcess(executable),logs,DateTimeOffset.UtcNow,installedUtc,installationHashesVerified,configurationHashesVerified);
        var found=scan.Entries.Where(e=>e.Status=="Found" && !e.IsBackup).ToArray();
        var dllCount=found.Count(e=>e.Name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase));
        var configEvidence=new List<string>();
        try {
            if (FindAddonCopies(gameRoot, executable).Count > 1) warnings.Add(DuplicateAddonWarning);
            var addon=Path.Combine(Path.GetDirectoryName(executable)!,"renodx-mfgunlock.addon64");
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(gameRoot,addon);
            var addonVersion=File.Exists(addon)?FileVersionInfo.GetVersionInfo(addon).FileVersion??"":"";
            if(MfgUnlockSettingsService.HasGenerationMethods(addonVersion)){
                var config=new MfgUnlockSettingsService(true).Load(Path.Combine(Path.GetDirectoryName(executable)!,"MFGAdaUnlock.ini"),gameRoot,addonVersion);
                configEvidence.Add(ConfigurationEvidence(config));
                var verifier=new IntelRuntimeService(Path.GetDirectoryName(executable)!);
                foreach(var asset in IntelRuntimeService.Assets.Where(a=>a.Version.Length>0)){
                    var path=Path.Combine(Path.GetDirectoryName(executable)!,asset.Name);
                    try{await verifier.VerifyFileAsync(path,asset,token).ConfigureAwait(false);configEvidence.Add("Pinned Intel file SHA-256 verified (not loaded proof): "+asset.Name+" · "+asset.Version);}
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException){warnings.Add(asset.Name+": "+ex.Message);}
                }
            }
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){warnings.Add("Configuration not verified: "+ex.Message);}
        return parsed with {Statuses=new[]{"Runtime detected on disk: "+(dllCount==0?"no known DLLs found (not proof of a broken game).":$"{dllCount} non-backup DLLs; loaded copies unknown.")}.Concat(parsed.Statuses).Concat(configEvidence.Take(1)).ToArray(),
            Evidence=found.Select(e=>$"Disk · {e.Name} · {e.Version}\n{e.Path}\n{e.IntegritySummary}").Concat(parsed.Evidence).Concat(configEvidence).ToArray(),Warnings=warnings.Concat(scan.Warnings).Concat(parsed.Warnings).ToArray(),Logs=logs.AsReadOnly(),
            DiskComponents=BuildDiskInventory(scan.Entries, executable, gameRoot)};
    }
    internal static string ConfigurationEvidence(MfgUnlockSettingsSnapshot snapshot)
    {
        if(!MfgUnlockSettingsService.HasGenerationMethods(snapshot.AddonVersion)||!snapshot.IsSupportedAddonVersion||snapshot.Settings.Any(s=>!s.IsValid))return "Generation configuration: Not verified";
        var selected=snapshot.Settings.Single(s=>s.Definition.Key=="Selected").EffectiveValue;
        return "Generation configuration: "+(selected=="XeMFG"?"Intel XeMFG (CUDA)":MfgUnlockSettingsService.XmxSelected(snapshot.Settings)?"XMX (experimental)":"NVIDIA DLSS-G")+" · saved preference only; loaded runtime and generation activity are not verified.";
    }
    private static RuntimeProcessSnapshot FindProcess(string exe)
    {
        var matches=new List<(int Id,DateTimeOffset Started)>();bool unknown=false;
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))) using(process) {
            try {if(string.Equals(Path.GetFullPath(process.MainModule!.FileName),exe,StringComparison.OrdinalIgnoreCase))matches.Add((process.Id,process.StartTime.ToUniversalTime()));}
            catch {unknown=true;}
        }
        return matches.Count==1 && !unknown?new(matches[0].Id,matches[0].Started,"Current process path/start verified"):
            new(null,null,unknown||matches.Count>1?"Process identity unavailable or ambiguous; current activity cannot be verified":"No matching game process; logs describe previous launches");
    }
    internal static RuntimeDiagnosticReport Analyze(string exe,string exeVersion,RuntimeProcessSnapshot process,IReadOnlyList<RuntimeLogInput> logs,DateTimeOffset now,DateTimeOffset? installedUtc=null,bool installationHashesVerified=false,bool configurationHashesVerified=false)
    {
        var evidence=new List<string>();var warnings=new List<string>();
        var verifiedChange=installationHashesVerified||configurationHashesVerified;
        string loaded="Not confirmed for the current session",dynamic="Not verified",xemfg="Not verified",restart=installedUtc is not null && verifiedChange?"Verified file change; a later matching game session has not been confirmed":"Not confirmed (no verified pending file change)";
        var jsonSessions=new List<(RuntimeLogInput Log,JsonElement Header,JsonElement[] Events)>();
        foreach(var log in logs) {
            if(log.Text.Length>MaxLogBytes){warnings.Add("Log exceeds parse limit: "+log.Path);continue;}
            if(Path.GetFileName(log.Path).Equals("ReShade.log",StringComparison.OrdinalIgnoreCase)) { ParseReShade(log,exe,evidence,warnings,ref dynamic);continue; }
            if(!Path.GetFileName(log.Path).Equals("MFGAdaUnlock-XeMFG.log",StringComparison.OrdinalIgnoreCase))continue;
            var events=new List<JsonElement>();int malformed=0;
            foreach(var line in log.Text.Split('\n')) {
                if(string.IsNullOrWhiteSpace(line))continue;
                if(line.Length>131072){malformed++;continue;}
                try {using var json=JsonDocument.Parse(line,new(){MaxDepth=16});
                    if(json.RootElement.ValueKind!=JsonValueKind.Object || json.RootElement.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=json.RootElement.EnumerateObject().Count()){malformed++;continue;}
                    events.Add(json.RootElement.Clone());
                } catch(JsonException){malformed++;}
            }
            if(malformed>0)warnings.Add($"{log.Path}: {malformed} malformed/oversized record(s); current activity is not verified from an incomplete log.");
            var last=events.FindLastIndex(e=>Str(e,"event")=="native_session");
            if(last<0){warnings.Add(log.Path+": no native_session header; events cannot be attributed to a launch.");continue;}
            var header=events[last];
            if(!Str(header,"executable").Equals(Path.GetFileName(exe),StringComparison.OrdinalIgnoreCase)) {warnings.Add(log.Path+": last session belongs to a different EXE; earlier sessions are not substituted.");continue;}
            var slice=events.Skip(last+1).Where(e=>Number(e,"pid")==Number(header,"pid")).ToArray();
            // A malformed log remains useful for historical evidence, never current-active authorization.
            if(malformed>0)header=JsonSerializer.SerializeToElement(new{ @event="native_session",executable=Str(header,"executable"),executable_version=Str(header,"executable_version"),utc=Str(header,"utc"),pid=0,tick=Number(header,"tick")});
            jsonSessions.Add((log,header,slice));
        }
        if(jsonSessions.Count>1)warnings.Add("Multiple addon log locations found; current-active classification is withheld.");
        foreach(var session in jsonSessions) {
            var h=session.Header;var pid=Number(h,"pid");var tick=Number(h,"tick");
            if(!DateTimeOffset.TryParseExact(Str(h,"utc"),"yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var started)
                || started>now.AddSeconds(5) || session.Log.LastWriteUtc<started.AddSeconds(-5) || pid<=0 || tick<0) {warnings.Add(session.Log.Path+": inconsistent/missing session identity or UTC; Not verified.");continue;}
            var versionMatch=BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(Str(h,"executable_version"))==BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(exeVersion) && !string.IsNullOrWhiteSpace(exeVersion);
            // 1.7.0 native_session/native_modules fields reviewed at upstream 7fa2e69.
            // This only recognizes the log schema; Vulkan/Reflex capability is not inferred.
            var knownSchema=MfgSettingsCapabilityCatalog.Find(Str(h,"addon")) is {NativeLog:true};
            var current=jsonSessions.Count==1 && knownSchema && process.Id==pid && process.StartedUtc is {} launch && started>=launch.AddSeconds(-2) && started<=launch.AddMinutes(5) && versionMatch;
            evidence.Add($"{(current?"Current process session matched":"Previous / unverified session")} · {started:O} · PID {pid}\n{session.Log.Path}\nEXE version {Str(h,"executable_version")} · addon {Str(h,"addon")}\n{process.Description}");
            if(installedUtc is {} newer && started<=newer)current=false; // old session cannot validate newly installed bytes
            bool mapped=false;JsonElement? output=null;bool failure=false;var handles=new HashSet<long>();
            foreach(var item in session.Events) {
                var itemTick=Number(item,"tick");
                if(itemTick<tick || (double)itemTick-tick>(now-started).TotalMilliseconds+5000) {warnings.Add("Inconsistent tick ignored; current activity withheld.");current=false;continue;}
                var name=Str(item,"event");
                if(name=="native_modules" && item.TryGetProperty("modules",out var modules) && modules.ValueKind==JsonValueKind.Array) {
                    foreach(var module in modules.EnumerateArray()) if(Str(module,"name") is var moduleName && NvidiaComponentScanner.KnownComponentNames.Contains(moduleName,StringComparer.OrdinalIgnoreCase)) {
                        evidence.Add("Mapped module reported · "+moduleName+" · "+Str(module,"version"));
                        if(moduleName.Equals("libxess_fg.dll",StringComparison.OrdinalIgnoreCase)||moduleName.Equals("sl.interposer.dll",StringComparison.OrdinalIgnoreCase))mapped=true;
                    }
                }
                if(name=="native_streamline_version") {
                    foreach(var field in item.EnumerateObject().Where(p=>p.Name=="interposer" || p.Name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))) {
                        if(field.Value.ValueKind!=JsonValueKind.String)continue;var v=field.Value.GetString()!;
                        evidence.Add("Mapped version reported · "+field.Name+" · "+v);
                        if(Regex.IsMatch(v,@"^\d+\.\d+\.\d+"))mapped=true;
                    }
                }
                if(name=="output_status") {output=item;handles.Add(Number(item,"handle"));}
                if(name is "startup_rejected" or "port_rejected" or "native_swapchain_init_failed" or "native_factory_hook_failure" || name=="runtime_call" && Bool(item,"failed")) {
                    failure=true;evidence.Add("Error reported · "+item.GetRawText());
                    if(name=="native_factory_hook_failure" && !warnings.Contains(FactoryHookWarning)) warnings.Add(FactoryHookWarning);
                }
                if(name=="native_frame_admission" && !string.IsNullOrWhiteSpace(Str(item,"blocker")) && Str(item,"blocker")!="ready")evidence.Add("Input / display blocker · "+Str(item,"blocker"));
                if(name=="runtime_selection")evidence.Add("Runtime selected (not loaded proof) · "+item.GetRawText());
            }
            if(installedUtc is {} install && verifiedChange && current && started>install)restart="Restart observed: matching process session began after installation";
            if(mapped)loaded=current?"Module mapping reported by a matching current addon session (snapshot, not live instrumentation)":"Not verified for current launch; mapped versions reported in previous session";
            if(output is {} status) {
                var sample=started.AddMilliseconds(Number(status,"tick")-tick);
                var enabled=Number(status,"enabled")==1;var frames=Number(status,"frames_presented");
                evidence.Add($"XeMFG SDK output at {sample:O} · enabled={Number(status,"enabled")} · frames_presented={frames} · interpolation_result={Number(status,"interpolation_result")} · tensor_launches={Number(status,"tensor_launches")}");
                xemfg=enabled?"Enabled reported in recorded session; active Not verified":"Disabled reported in recorded session; active Not verified";
                if(current && mapped && handles.Count==1 && Number(status,"handle")>0 && enabled && frames>1 && Number(status,"interpolation_result")==0 && Number(status,"tensor_launches")>0
                    && now-sample<=TimeSpan.FromSeconds(15) && sample<=now.AddSeconds(2) && !failure)
                    xemfg="Active reported by a fresh matching XeMFG SDK output sample (not a monitor/FPS measurement)";
                else if(failure)xemfg="Error reported; active Not verified";
            } else if(failure)xemfg="Error / unsupported initialization reported; active Not verified";
        }
        if(logs.Count==0)warnings.Add("No supported logs found beside the EXE or in _storage_. Start the game yourself only when ready, then refresh; this tool never launches it.");
        warnings.Add("Logs are mutable diagnostic evidence. Old logs, capability support, accepted options, copied DLLs and driver overrides do not prove current MFG activity. NVIDIA App / NVPI profiles are not read or changed.");
        return new(new[]{"DLL installed: "+(installationHashesVerified?"verified against an authenticated current installation transaction":"Not verified by an installation transaction; use the disk inventory"),
            "Loaded modules: "+loaded,"NVIDIA Dynamic MFG: "+dynamic+"; active Not verified (no unambiguous mode-specific presentation evidence)","Intel XeMFG: "+xemfg,"XMX: Not verified; configuration, hook and execute records do not prove current activity.","Restart: "+restart},evidence,warnings);
    }
    private static void ParseReShade(RuntimeLogInput log,string exe,List<string> evidence,List<string> warnings,ref string dynamic)
    {
        var lines=log.Text.Split('\n');var header=Array.FindLastIndex(lines,l=>l.Contains("Initializing crosire's ReShade version '",StringComparison.Ordinal));
        if(header<0){warnings.Add(log.Path+": no ReShade initialization header; no session inferred.");return;}
        var path=Regex.Match(lines[header],@" into '([^']+)' \(0x[0-9A-Fa-f]+\)");
        if(!path.Success || !path.Groups[1].Value.Equals(exe,StringComparison.OrdinalIgnoreCase)){warnings.Add(log.Path+": ReShade session EXE path does not match.");return;}
        evidence.Add("ReShade recorded session (clock-only timestamps; current-process identity not proven)\n"+log.Path+"\n"+lines[header].Trim());
        foreach(var line in lines.Skip(header+1)) {
            if(!line.Contains("mfgunlock:",StringComparison.OrdinalIgnoreCase))continue;
            if (line.Contains("ADDON.LoadFromDllMain", StringComparison.OrdinalIgnoreCase))
            {
                if (!warnings.Contains(EarlyLoadWarning)) warnings.Add(EarlyLoadWarning);
                evidence.Add(line.Trim());
            }
            if (line.Contains("mfgunlock: architecture gate refused", StringComparison.Ordinal))
            {
                if (!warnings.Contains(ProviderGateWarning)) warnings.Add(ProviderGateWarning);
                evidence.Add(line.Trim());
            }
            if(line.Contains("mfgunlock: xmx ",StringComparison.Ordinal))evidence.Add("Recorded XMX evidence (not current activity proof) · "+line.Trim());
            if(line.Contains("verified mapped DLSS-G provider candidate version",StringComparison.Ordinal)||line.Contains("observed Streamline DLSS-G wrapper version",StringComparison.Ordinal))evidence.Add(line.Trim());
            if(line.Contains("mfgunlock: NVIDIA Dynamic MFG accepted (target ",StringComparison.Ordinal)||line.Contains("mfgunlock: Dynamic MFG accepted on the bounded retry",StringComparison.Ordinal)
                ||line.Contains("mfgunlock: Dynamic MFG accepted after UI recomposition was removed",StringComparison.Ordinal)||line.Contains("mfgunlock: Dynamic MFG with UI Composition was accepted",StringComparison.Ordinal)) {dynamic="Enabled / accepted in recorded ReShade session";evidence.Add(line.Trim());}
            if(line.Contains("Dynamic MFG was rejected",StringComparison.Ordinal)||line.Contains("Dynamic MFG unsupported",StringComparison.Ordinal)||line.Contains("Dynamic MFG is blocked",StringComparison.Ordinal)) {dynamic="Error / unsupported reported in recorded ReShade session";evidence.Add(line.Trim());}
            if(line.Contains("| ERROR",StringComparison.Ordinal)||line.Contains("mfgunlock: Dynamic MFG was not submitted",StringComparison.Ordinal))evidence.Add(line.Trim());
        }
    }
    private static string Str(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    private static long Number(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.Number && v.TryGetInt64(out var n)?n:long.MinValue;
    private static bool Bool(JsonElement e,string key)=>e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.True;
}

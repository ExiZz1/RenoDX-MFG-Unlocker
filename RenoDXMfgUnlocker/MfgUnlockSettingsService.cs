using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public sealed record MfgUnlockSettingDefinition(
    string Key,
    string Label,
    string Description,
    string DefaultValue,
    bool RequiresRestart,
    IReadOnlyList<MfgUnlockSettingChoice> Choices,
    string? Section = null,
    MfgCatalogEntry? CatalogEntry = null)
{
    internal bool IsUpstreamSource {get;init;}
    internal string MetadataTrust {get;init;}="Built-in reviewed metadata";
}

public sealed record MfgUnlockSettingChoice(string Value, string Label);

public sealed record MfgUnlockSettingState(
    MfgUnlockSettingDefinition Definition,
    string? RawValue,
    string EffectiveValue,
    string? ProposedValue,
    bool IsExplicitlySet,
    bool IsValid);

public sealed record MfgUnlockSettingsSnapshot(
    string ConfigPath,
    string AllowedRoot,
    string AddonVersion,
    bool IsSupportedAddonVersion,
    bool ConfigExists,
    string ContentSha256,
    byte[] OriginalBytes,
    IReadOnlyList<MfgUnlockSettingState> Settings,
    string Newline,
    bool HasUtf8Bom,
    string CatalogIdentity = "legacy")
{
    public IReadOnlyList<MfgUnknownSetting> UnknownSettings { get; init; } = [];
}

public sealed record MfgUnknownSetting(string Section, string Key, string RawValue);

/// <summary>
/// Reads the documented restart-required settings from the MFG Unlock section of a per-game
/// ReShade.ini. It never writes files itself; callers stage changes through BackupRestoreService.
/// </summary>
public sealed class MfgUnlockSettingsService
{
    private readonly bool xeFg;
    public MfgDynamicSettingsCatalog Catalog { get; private set; }
    public MfgUnlockSettingsService(bool xeFg=false, MfgDynamicSettingsCatalog? catalog=null){this.xeFg=xeFg;Catalog=catalog??MfgDynamicSettingsCatalog.Empty;}
    public void UseCatalog(MfgDynamicSettingsCatalog catalog)=>Catalog=catalog??throw new ArgumentNullException(nameof(catalog));
    public string ConfigFileName=>xeFg?"MFGAdaUnlock.ini":"ReShade.ini";
    public string ConfigSection=>xeFg?"XeFG":SectionName;
    public bool SupportsVersion(string? version)=>MfgSettingsCapabilityCatalog.CanWrite(version,xeFg);
    public const string SectionName = "RenoDX.MFGUnlock";
    public const string SupportedAddonVersion = ">=1.6.0 (feature-dependent)";
    public static bool SupportsAddon(string? version)=>MfgSettingsCapabilityCatalog.CanWrite(version,false);

    private static readonly IReadOnlyList<MfgUnlockSettingDefinition> definitions =
    [
        Bool("Enabled", "Addon enabled", "Enables MFG Unlock on the next game launch.", "1"),
        Bool("ForceFlipMeteringOff", "Legacy software pacing", "Normally leave off. Use only for a documented higher-multiplier freeze workaround.", "0"),
        Bool("TemporalFix", "Temporal interpolation fix", "Controls the addon temporal correction path.", "1"),
        Bool("BlackwellFrameworkKernels", "Blackwell framework kernels", "Uses exact-fingerprint replacement kernels when the installed provider matches; otherwise the addon falls back.", "1"),
        Bool("ExperimentalAdaptiveQuality", "Experimental adaptive quality", "Enables the coordinated experimental quality path.", "1"),
        new("RuntimeSelectionMode", "Streamline runtime selection", "0 preserves the game's policy, 1 prefers the complete local runtime, 2 forces NVIDIA OTA flags.", "0", true,
            [new("0", "Game default"), new("1", "Prefer local runtime"), new("2", "Force NVIDIA OTA")])
    ];

    public IReadOnlyList<MfgUnlockSettingDefinition> Definitions => xeFg?DefinitionsFor("1.7.0.0"):definitions;
    public IReadOnlyList<MfgUnlockSettingDefinition> DefinitionsFor(string version)=>[..LegacyDefinitionsFor(version),..Catalog.Definitions(ConfigFileName,version)];
    private IReadOnlyList<MfgUnlockSettingDefinition> LegacyDefinitionsFor(string version)=>xeFg?[
        Bool("Vulkan","Vulkan XeMFG (experimental)","Experimental Vulkan needs ReShade Vulkan addon support and game DLSS SR/RR inputs. Set 0 if this path fails; restart required.","1"),
        Bool("VulkanPresentMetering","Vulkan present metering","Uses NVIDIA present metering only where the driver offers it and VSync is off. Read at Vulkan initialization.","1"),
        Bool("XeFGFlipQueue","XeMFG flip queue (experimental)","Optional queue experiment; upstream reports a latency cost. Keep off unless comparing pacing.","0"),
        Bool("UIStabilization","HUD stabilization (experimental)","Experimental static HUD stabilization. Moving or transparent HUD elements still interpolate. The original addon panel controls this live.","0"),
        ..(HasGenerationMethods(version)?GenerationDefinitions:[])
    ]:MfgSettingsCapabilityCatalog.Displays17Keys(version) ? [..definitions,Bool("Rtx50QualityFixes","RTX 50 quality fixes (experimental)","RTX 50 only: experimental validated warp-blend fix. Off by default; field testing is still required.","0")] : definitions;

    private string? CanonicalValue(MfgUnlockSettingDefinition definition,string raw)
    {
        if(definition.CatalogEntry is {} entry)return MfgDynamicSettingsCatalog.Canonical(entry,entry.Type is "Integer" or "Float"?Regex.Replace(raw,@"\s*[;#].*$",""):raw);
        if(definition.Section=="Backend")return definition.Choices.FirstOrDefault(c=>c.Value.Equals(raw.Trim(),StringComparison.OrdinalIgnoreCase))?.Value;
        if(!xeFg)return definition.Choices.FirstOrDefault(c=>c.Value.Equals(raw.Trim(),StringComparison.OrdinalIgnoreCase))?.Value;
        var value=raw.Trim();
        if(definition.Key=="UIStabilization")return value.ToLowerInvariant() switch {"" or "0" or "false" or "off"=>"0","1" or "true" or "on"=>"1",_=>null};
        // These three upstream keys use GetPrivateProfileIntW(... ) != 0, not ReadBool.
        var integer=Regex.Match(value,@"^([+-]?\d+)\s*(?:[;#].*)?$");
        return integer.Success&&int.TryParse(integer.Groups[1].Value,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var number)?definition.Key is "XmxGenerator" or "XeFGStock2xModel"?(number==1?"1":"0"):number==0?"0":"1":null;
    }

    internal static bool HasGenerationMethods(string version)=>MfgSettingsCapabilityCatalog.SupportsGenerationMethods(version);
    public string SectionFor(MfgUnlockSettingDefinition definition)=>definition.Section??ConfigSection;
    private static readonly MfgUnlockSettingDefinition[] GenerationDefinitions=[
        new("Selected","Generation backend","DLSSG uses NVIDIA presentation; XeMFG uses Intel XeMFG (CUDA). XMX additionally requires XmxGenerator=1 with DLSSG. Restart the game.","DLSSG",true,[new("DLSSG","NVIDIA DLSS-G"),new("XeMFG","Intel XeMFG (CUDA)")],"Backend"),
        new("XmxGenerator","XeMFG via DLSS-G (XMX, experimental)","Only with Selected=DLSSG. Requires D3D12, native DLSS Frame Generation and the pinned Intel pair beside the EXE. Configuration does not prove active XMX. Restart required.","0",true,[new("0","Off"),new("1","On")],"DLSSG"),
        new("ForceBorderless","DLSS-G borderless window","Serves the DLSS-G window as borderless. Read at startup; does not create a Frame Generation integration.","0",true,[new("0","Off"),new("1","On")],"DLSSG"),
        Bool("XeFGStock2xModel","Intel stock 2x model","Experimental: Intel's stock model supports only 2x. Above 2x, generation does not start. Restart required.","0")
    ];
    internal static bool XmxSelected(IEnumerable<MfgUnlockSettingState> settings)=>settings.Any(s=>s.Definition.Key=="Selected"&&s.IsValid&&s.EffectiveValue=="DLSSG")&&settings.Any(s=>s.Definition.Key=="XmxGenerator"&&s.IsValid&&s.EffectiveValue=="1");
    internal static bool NvidiaGeneratorOnly(string key)=>key is "TemporalFix" or "BlackwellFrameworkKernels" or "ExperimentalAdaptiveQuality" or "Rtx50QualityFixes";

    public MfgUnlockSettingsSnapshot Load(string configPath, string allowedRoot, string addonVersion)
    {
        var root = Path.GetFullPath(allowedRoot);
        var config = ValidateConfigPath(configPath, root);
        EnsureNoReparsePoints(root, config);
        var supported = SupportsVersion(addonVersion);
        var exists = File.Exists(config);
        if(exists&&new FileInfo(config).Length>2*1024*1024)throw new InvalidDataException("Configuration is too large for safe settings editing.");
        var bytes = exists ? File.ReadAllBytes(config) : [];
        var bom = HasBom(bytes);
        var text = Decode(bytes, bom);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var sections=DefinitionsFor(addonVersion).Select(SectionFor).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(s=>s,s=>ParseSection(text,s),StringComparer.OrdinalIgnoreCase);
        var states = DefinitionsFor(addonVersion).Select(definition =>
        {
            sections[SectionFor(definition)].TryGetValue(definition.Key, out var raw);
            var canonical=raw is null?definition.DefaultValue:CanonicalValue(definition,raw);
            var isValid = canonical is not null;
            var effective = canonical??raw!.Trim();
            var proposed = isValid ? effective : null;
            return new MfgUnlockSettingState(definition, raw, effective, proposed, raw is not null, isValid);
        }).ToArray();

        return new(config, root, addonVersion ?? string.Empty, supported, exists,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes, states, newline, bom,Catalog.Identity) {UnknownSettings=DiscoverUnknown(text,states)};
    }

    private IReadOnlyList<MfgUnknownSetting> DiscoverUnknown(string text,IReadOnlyList<MfgUnlockSettingState> known)
    {
        var result=new List<MfgUnknownSetting>();var section="";
        foreach(var line in SplitLines(text)){
            var header=Regex.Match(line,@"^\s*\[([^\]]+)\]\s*$");
            if(header.Success){section=header.Groups[1].Value;continue;}
            if(!xeFg&&!section.Equals(ConfigSection,StringComparison.OrdinalIgnoreCase))continue;
            var pair=Regex.Match(line,@"^\s*([^;#=\s]+)\s*=\s*(.*?)\s*$");
            if(!pair.Success||section.Length==0||known.Any(k=>SectionFor(k.Definition).Equals(section,StringComparison.OrdinalIgnoreCase)&&k.Definition.Key.Equals(pair.Groups[1].Value,StringComparison.OrdinalIgnoreCase)))continue;
            if(result.Count==512)throw new InvalidDataException("Too many unknown settings for safe inspection.");
            result.Add(new(section,pair.Groups[1].Value,pair.Groups[2].Value));
        }
        return result;
    }

    public bool CanEdit(MfgUnlockSettingDefinition definition,IEnumerable<MfgUnlockSettingState> states,IReadOnlyDictionary<string,string>? changes=null)
    {
        if(definition.CatalogEntry is not {} entry)return true;
        if(entry.Type=="ReadOnly")return false;
        return (entry.Requires??[]).All(d=>states.Any(s=>s.Definition.Key.Equals(d.Key,StringComparison.OrdinalIgnoreCase)&&s.IsValid&&(changes is not null&&changes.TryGetValue(s.Definition.Key,out var value)?value:s.EffectiveValue)==d.Value));
    }

    public byte[] CreateUpdatedBytes(MfgUnlockSettingsSnapshot snapshot, IReadOnlyDictionary<string, string> changes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changes);
        if(snapshot.CatalogIdentity!=Catalog.Identity)throw new InvalidDataException("Settings catalog changed. Reload before saving.");
        if (!snapshot.IsSupportedAddonVersion || !SupportsVersion(snapshot.AddonVersion))
            throw new InvalidOperationException(xeFg?"XeMFG advanced editing requires addon 1.7.0 or newer with XeFG configuration support.":$"Settings editing is supported only for MFG Unlock {SupportedAddonVersion}; detected {snapshot.AddonVersion}.");
        if (snapshot.Settings.Any(setting => !setting.IsValid))
            throw new InvalidDataException(ConfigFileName+" contains an invalid MFG Unlock setting. Reload and correct the configuration before saving; no values were changed.");

        var config = ValidateConfigPath(snapshot.ConfigPath, snapshot.AllowedRoot);
        EnsureNoReparsePoints(snapshot.AllowedRoot, config);
        var current = File.Exists(config) ? File.ReadAllBytes(config) : [];
        var currentHash = Convert.ToHexString(SHA256.HashData(current)).ToLowerInvariant();
        if (!currentHash.Equals(snapshot.ContentSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException(ConfigFileName+" changed after the settings preview. Reload it and review the new values before saving.");

        foreach (var (key, value) in changes)
        {
            var definition = DefinitionsFor(snapshot.AddonVersion).FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (definition is null || !CanEdit(definition,snapshot.Settings,changes) || (definition.CatalogEntry is not null?CanonicalValue(definition,value)!=value:!definition.Choices.Any(choice => choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException($"Unsupported MFG Unlock setting or value: {key}={value}.");
        }

        if(!xeFg&&HasGenerationMethods(snapshot.AddonVersion)&&changes.Keys.Any(NvidiaGeneratorOnly)){
            var backend=new MfgUnlockSettingsService(true).Load(Path.Combine(Path.GetDirectoryName(config)!,"MFGAdaUnlock.ini"),snapshot.AllowedRoot,snapshot.AddonVersion);
            if(backend.Settings.Any(s=>!s.IsValid)||XmxSelected(backend.Settings))throw new InvalidDataException("NVIDIA generator settings are inapplicable with XMX. Preserve saved values; switch back to DLSS-G and reload before editing them.");
        }
        if (changes.Count == 0) return current;
        var text = Decode(current, snapshot.HasUtf8Bom);
        var lines = SplitLines(text);
        foreach(var group in changes.GroupBy(c=>SectionFor(DefinitionsFor(snapshot.AddonVersion).Single(d=>d.Key.Equals(c.Key,StringComparison.OrdinalIgnoreCase))))){
        var sectionName=group.Key;
        var section = FindSection(lines,sectionName);
        if (section.Count > 1) throw new InvalidDataException($"{ConfigFileName} contains multiple [{ConfigSection}] sections; no change was made.");
        var edits = group.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        if (section.Count == 1)
        {
            var start = section[0];
            var end = FindSectionEnd(lines, start + 1);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = start + 1; i < end; i++)
            {
                var match = Regex.Match(lines[i], @"^\s*([^;#=\s]+)\s*=");
                if (!match.Success || !edits.ContainsKey(match.Groups[1].Value)) continue;
                var key = match.Groups[1].Value;
                if (!seen.Add(key)) throw new InvalidDataException($"{ConfigFileName} contains duplicate {key} keys; no change was made.");
                var valueMatch = Regex.Match(lines[i], @"^(\s*[^=]+?=\s*)([^;#]*?)(\s*(?:[;#].*)?)$");
                if (!valueMatch.Success) throw new InvalidDataException($"Could not safely parse the {key} setting; no change was made.");
                lines[i] = valueMatch.Groups[1].Value + edits[key] + valueMatch.Groups[3].Value;
                edits.Remove(key);
            }
            foreach (var (key, value) in edits)
                lines.Insert(end++, key + "=" + value);
        }
        else
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
            lines.Add("[" + sectionName + "]");
            foreach (var (key, value) in edits) lines.Add(key + "=" + value);
        }

        }
        var output = string.Join(snapshot.Newline, lines);
        if ((text.EndsWith("\n", StringComparison.Ordinal) || text.Length == 0) && !output.EndsWith(snapshot.Newline, StringComparison.Ordinal)) output += snapshot.Newline;
        var encoded = new UTF8Encoding(false, true).GetBytes(output);
        if (!snapshot.HasUtf8Bom) return encoded;
        return [0xEF, 0xBB, 0xBF, .. encoded];
    }

    public static string GetSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public BackupTransactionFile PrepareTransaction(MfgUnlockSettingsSnapshot snapshot,IReadOnlyDictionary<string,string> changes,Func<bool> canWrite)
    {
        if(!canWrite())throw new IOException("Close the game and reload the confirmed addon before saving.");
        var bytes=CreateUpdatedBytes(snapshot,changes);
        if(!canWrite())throw new IOException("Close the game and reload the confirmed addon before saving.");
        return new(Path.GetRelativePath(snapshot.AllowedRoot,snapshot.ConfigPath),bytes,snapshot.ConfigExists,snapshot.ConfigExists?snapshot.ContentSha256:null);
    }

    private static MfgUnlockSettingDefinition Bool(string key, string label, string description, string defaultValue) =>
        new(key, label, description, defaultValue, true,
            [new("0", "Off"), new("1", "On")]);

    private static string NormalizeVersion(string? value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+(?:[.,]\d+)+", RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Replace(',', '.') : string.Empty;
    }

    private string ValidateConfigPath(string path, string root)
    {
        var config = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!config.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(config).Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only "+ConfigFileName+" files inside the selected game's folder can be edited.");
        return config;
    }

    private void EnsureNoReparsePoints(string root, string target)
    {
        var fullRoot = Path.GetFullPath(root);
        var directory = Path.GetDirectoryName(Path.GetFullPath(target))!;
        var current = directory;
        while (true)
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A junction, symlink, or other reparse point exists in the selected game's settings path.");
            if (current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) break;
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || !current.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The settings path is not inside the selected game's root.");
            current = parent;
        }
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(ConfigFileName+" is a reparse point; no change was made.");
    }

    private Dictionary<string, string> ParseSection(string text,string sectionName)
    {
        var lines = Regex.Split(text, "\r\n|\n|\r");
        if(xeFg&&lines.Any(line=>Regex.IsMatch(line,@"^\s*\[\s*"+Regex.Escape(sectionName)+@"\s*\]",RegexOptions.IgnoreCase)&&!line.Trim().Equals("["+sectionName+"]",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Ambiguous settings section header; no change was made.");
        var sections = FindSection(lines,sectionName);
        if (sections.Count > 1) throw new InvalidDataException($"{ConfigFileName} contains multiple [{ConfigSection}] sections.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sections.Count == 0) return values;
        var end = FindSectionEnd(lines, sections[0] + 1);
        for (var i = sections[0] + 1; i < end; i++)
        {
            var match = Regex.Match(lines[i], xeFg?@"^\s*([^;#=\s]+)\s*=\s*(.*?)\s*$":@"^\s*([^;#=\s]+)\s*=\s*(.*?)\s*(?:[;#].*)?$");
            if (!match.Success) continue;
            var key = match.Groups[1].Value;
            if (!values.TryAdd(key, match.Groups[2].Value))
                throw new InvalidDataException($"{ConfigFileName} contains duplicate {key} keys in [{ConfigSection}].");
        }
        return values;
    }

    private List<int> FindSection(IReadOnlyList<string> lines,string sectionName) => lines
        .Select((line, index) => (line, index))
        .Where(item => item.line.Trim().Equals("[" + sectionName + "]", StringComparison.OrdinalIgnoreCase))
        .Select(item => item.index).ToList();

    private static int FindSectionEnd(IReadOnlyList<string> lines, int start)
    {
        for (var i = start; i < lines.Count; i++)
            if (Regex.IsMatch(lines[i].Trim(), @"^\[[^\]]+\]$")) return i;
        return lines.Count;
    }

    private static List<string> SplitLines(string text) => Regex.Split(text, "\r\n|\n|\r").ToList();
    private static bool HasBom(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    private static string Decode(byte[] bytes, bool bom)
    {
        ReadOnlySpan<byte> data = bom ? bytes.AsSpan(3) : bytes.AsSpan();
        return new UTF8Encoding(false, true).GetString(data);
    }
}

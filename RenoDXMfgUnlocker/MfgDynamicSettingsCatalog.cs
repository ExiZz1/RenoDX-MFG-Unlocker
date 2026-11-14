using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public sealed record MfgCatalogDependency(string Key, string Value);
public sealed record MfgCatalogExclusion(string From, string? Until);
public sealed record MfgCatalogText(string Label, string Description);
public sealed record MfgCatalogEntry(
    string File, string Section, string Key, string Type, string Default,
    string MinimumAddon, string? MaximumAddonExclusive, string Category,
    string Description, string Evidence, bool RequiresRestart,
    string[]? Values = null, decimal? Minimum = null, decimal? Maximum = null,
    MfgCatalogDependency[]? Requires = null, MfgCatalogExclusion[]? Exclusions = null,
    Dictionary<string, MfgCatalogText>? Translations = null);
internal sealed record MfgCatalogDocument(int Schema, string Purpose, string KeyId, long Revision, MfgCatalogEntry[] Entries);

/// <summary>Declarative metadata only: no expressions, commands, URLs to execute or arbitrary destination paths.</summary>
public sealed class MfgDynamicSettingsCatalog
{
    internal const string Purpose = "renodx-mfg-settings-catalog";
    internal static readonly byte[] SignatureDomain = Encoding.UTF8.GetBytes("RenoDX MFG Settings Catalog v1\n");
    internal const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    public static MfgDynamicSettingsCatalog Empty { get; } = new([], 0, "legacy");
    public static MfgDynamicSettingsCatalog BuiltIn { get; } = LoadBuiltIn();
    private readonly MfgCatalogEntry[] entries;
    private readonly HashSet<string> upstreamKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool ownerAuthenticated;
    public long Revision { get; }
    public string Identity { get; }
    private MfgDynamicSettingsCatalog(MfgCatalogEntry[] entries, long revision, string identity)
    { this.entries = entries; Revision = revision; Identity = identity; }

    // A separately authenticated fallback; do not rewrite builtin-v1 or the owner's original Revision 2 files.
    internal static MfgDynamicSettingsCatalog ReviewedFallback()
    {
        byte[] Read(string name){using var s=typeof(MfgDynamicSettingsCatalog).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.SettingsCatalog."+name)??throw Invalid();using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
        return Verify(Read("reviewed-r2.json"),Read("reviewed-r2.sig"));
    }
    internal MfgDynamicSettingsCatalog WithUpstream(UpstreamSettingsAnalysis analysis)
    {
        var extra=new List<MfgCatalogEntry>();
        var reserved=entries.Select(e=>e.File+"|"+e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var file in new[]{"ReShade.ini","MFGAdaUnlock.ini"})foreach(var d in new MfgUnlockSettingsService(file=="MFGAdaUnlock.ini").DefinitionsFor("999.0.0"))reserved.Add(file+"|"+d.Key);
        foreach(var f in analysis.Editable){
            var e=f.Definition!;
            if(reserved.Contains(e.File+"|"+e.Key))continue; // Neither owner metadata nor any protected key can be overridden.
            var translated=e with {Translations=new(){{"en-US",new(f.Label,f.Description)}}};
            // Reuse exactly the signed-catalog type/path/value/dependency validator without pretending this is signed.
            try{Create(new(1,Purpose,"upstream-source-not-owner-signed",1,[translated]),"preflight");}
            catch(InvalidDataException){continue;}
            reserved.Add(e.File+"|"+e.Key);extra.Add(translated);
        }
        if(extra.Count==0)return this;
        var sourceHash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(analysis.Snapshot)));
        var merged=Create(new(1,Purpose,"upstream-source-not-owner-signed",Revision,[..entries,..extra]),Identity+"+upstream:"+sourceHash);
        merged.ownerAuthenticated=ownerAuthenticated;
        foreach(var e in extra)merged.upstreamKeys.Add(e.File+"|"+e.Key);
        return merged;
    }
    internal bool IsUpstream(MfgUnlockSettingDefinition d)=>d.CatalogEntry is {} e&&upstreamKeys.Contains(e.File+"|"+e.Key);

    internal static MfgDynamicSettingsCatalog Verify(byte[] json, byte[] signature, IReadOnlyDictionary<string, string>? trustedKeys = null)
    {
        if (json.Length is 0 or > MaximumBytes || signature.Length is < 256 or > 1024) throw Invalid();
        var document = Parse(json);
        if (string.IsNullOrEmpty(document.KeyId) || document.KeyId.Length > 64) throw Invalid();
        if (!(trustedKeys ?? SelfUpdateTrust.Keys).TryGetValue(document.KeyId, out var key)) throw Invalid();
        using var rsa = RSA.Create();
        var spki = Convert.FromBase64String(key);
        rsa.ImportSubjectPublicKeyInfo(spki, out var used);
        byte[] signedData = [..SignatureDomain, ..json];
        if (used != spki.Length || rsa.KeySize < 2048 || !rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw Invalid();
        var result=Create(document, Convert.ToHexString(SHA256.HashData(json)));
        result.ownerAuthenticated=trustedKeys is null;
        return result;
    }
    private static MfgCatalogDocument Parse(byte[] json)
    {
        if (json.Length is 0 or > MaximumBytes) throw Invalid();
        using var parsed = JsonDocument.Parse(json, new() { MaxDepth = 16 });
        // System.Text.Json normally accepts duplicate property names. Never accept ambiguous signed metadata.
        void Unique(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object) {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in node.EnumerateObject()) { if (!seen.Add(p.Name)) throw Invalid(); Unique(p.Value); }
            } else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Unique(item);
        }
        Unique(parsed.RootElement);
        return JsonSerializer.Deserialize<MfgCatalogDocument>(json, JsonOptions) ?? throw Invalid();
    }
    private static MfgDynamicSettingsCatalog LoadBuiltIn()
    {
        using var stream = typeof(MfgDynamicSettingsCatalog).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.SettingsCatalog.builtin.json") ?? throw Invalid();
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        return Create(Parse(memory.ToArray()), "builtin-v1");
    }
    private static MfgDynamicSettingsCatalog Create(MfgCatalogDocument document, string identity)
    {
        if (document.Schema != 1 || document.Purpose != Purpose || document.Revision is < 1 or > 1000000000 || document.Entries is null || document.Entries.Length > 128) throw Invalid();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in document.Entries) {
            if (e is null || e.File is not ("ReShade.ini" or "MFGAdaUnlock.ini") || !Identifier(e.Section) || !Identifier(e.Key)
                || e.File == "ReShade.ini" && e.Section != MfgUnlockSettingsService.SectionName || e.Section.StartsWith("XeFG.Learned", StringComparison.OrdinalIgnoreCase)
                || !keys.Add(e.File + "|" + e.Key) || e.Category is not ("General" or "Advanced" or "Other") || !e.RequiresRestart
                || !MfgSettingsCapabilityCatalog.TryStableVersion(e.MinimumAddon, out var minimum)
                || minimum < new Version(e.File == "ReShade.ini" ? "1.6.0.0" : "1.7.0.0")
                || e.MaximumAddonExclusive is not null && (!MfgSettingsCapabilityCatalog.TryStableVersion(e.MaximumAddonExclusive, out var maximum) || maximum <= minimum)
                || !Text(e.Description, 2000) || !Evidence(e.Evidence) || e.Type is not ("Boolean" or "Integer" or "Float" or "Enum" or "ReadOnly")
                || e.Requires?.Length > 16 || e.Exclusions?.Length > 16 || e.Translations?.Count > 6) throw Invalid();
            // Protected builtin definitions cannot be replaced, moved or have their constraints weakened by data.
            var builtin = new MfgUnlockSettingsService(e.File == "MFGAdaUnlock.ini").DefinitionsFor("999.0.0");
            if (builtin.Any(d => d.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase)) || new[]{"Selected","XmxGenerator"}.Contains(e.Key,StringComparer.OrdinalIgnoreCase)) throw Invalid();
            if (e.Type is "Integer" or "Float") {
                if (e.Minimum is null || e.Maximum is null || e.Minimum > e.Maximum || e.Minimum < -1000000000 || e.Maximum > 1000000000
                    || e.Type == "Integer" && (e.Minimum != decimal.Truncate(e.Minimum.Value) || e.Maximum != decimal.Truncate(e.Maximum.Value))) throw Invalid();
            }
            if (e.Type == "Enum" && (e.Values is null || e.Values.Length is < 1 or > 32 || e.Values.Any(v => !Token(v)) || e.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != e.Values.Length)) throw Invalid();
            if (e.Type != "Enum" && e.Values is { Length: > 0 } || Canonical(e, e.Default) is null) throw Invalid();
            foreach (var x in e.Exclusions ?? []) if (x is null || !MfgSettingsCapabilityCatalog.TryStableVersion(x.From, out var from) || x.Until is not null && (!MfgSettingsCapabilityCatalog.TryStableVersion(x.Until, out var until) || until <= from)) throw Invalid();
            foreach (var d in e.Requires ?? []) if (d is null || !Identifier(d.Key) || d.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase) || !Token(d.Value)) throw Invalid();
            foreach (var t in e.Translations ?? []) if (!UiLocalization.Cultures.Contains(t.Key) || t.Value is null || !Text(t.Value.Label, 120) || !Text(t.Value.Description, 2000)) throw Invalid();
        }
        // No missing/cyclic dependencies. Dependencies are same-file, exact-value conditions, never executable expressions.
        foreach (var e in document.Entries) {
            var builtin = new MfgUnlockSettingsService(e.File == "MFGAdaUnlock.ini").DefinitionsFor(e.MinimumAddon);
            foreach (var d in e.Requires ?? []) {
                var target = document.Entries.FirstOrDefault(x => x.File == e.File && x.Key.Equals(d.Key, StringComparison.OrdinalIgnoreCase));
                if (target is null ? !builtin.Any(b => b.Key.Equals(d.Key, StringComparison.OrdinalIgnoreCase) && b.Choices.Any(c => c.Value == d.Value)) : Canonical(target, d.Value) != d.Value) throw Invalid();
            }
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(MfgCatalogEntry entry) {
                if(visited.Contains(entry.Key))return;
                if (!visiting.Add(entry.Key)) throw Invalid();
                foreach (var d in entry.Requires ?? []) { var t = document.Entries.FirstOrDefault(x => x.File == entry.File && x.Key.Equals(d.Key, StringComparison.OrdinalIgnoreCase)); if (t is not null) Visit(t); }
                visiting.Remove(entry.Key);
                visited.Add(entry.Key);
            }
            Visit(e);
        }
        return new(document.Entries, document.Revision, identity);
    }
    internal IReadOnlyList<MfgUnlockSettingDefinition> Definitions(string file, string version) => entries.Where(e => e.File == file && Supports(e, version)).Select(e => new MfgUnlockSettingDefinition(
        e.Key, upstreamKeys.Contains(e.File+"|"+e.Key)?e.Translations?.GetValueOrDefault("en-US")?.Label??e.Key:e.Key, "Catalog-defined setting. See Details for the upstream description, constraints and evidence.", e.Default, true,
        e.Type == "Boolean" ? [new("0", "Off"), new("1", "On")] : e.Type == "Enum" ? e.Values!.Select(v => new MfgUnlockSettingChoice(v, v)).ToArray() : [], e.Section, e){IsUpstreamSource=upstreamKeys.Contains(e.File+"|"+e.Key),MetadataTrust=upstreamKeys.Contains(e.File+"|"+e.Key)?"Upstream source — locally approved, not owner-signed":ownerAuthenticated?"Owner-signed catalog metadata":"Built-in reviewed metadata"}).ToArray();
    internal static bool Supports(MfgCatalogEntry e, string version) => MfgSettingsCapabilityCatalog.TryStableVersion(version, out var v)
        && MfgSettingsCapabilityCatalog.TryStableVersion(e.MinimumAddon, out var minimum) && v >= minimum
        && (e.MaximumAddonExclusive is null || MfgSettingsCapabilityCatalog.TryStableVersion(e.MaximumAddonExclusive, out var maximum) && v < maximum)
        && !(e.Exclusions ?? []).Any(x => MfgSettingsCapabilityCatalog.TryStableVersion(x.From, out var f) && v >= f && (x.Until is null || MfgSettingsCapabilityCatalog.TryStableVersion(x.Until, out var u) && v < u));
    internal static string? Canonical(MfgCatalogEntry e, string raw)
    {
        if (raw is null || raw.Length > 256 || raw.Any(char.IsControl)) return null;
        var v = raw.Trim();
        return e.Type switch {
            "Boolean" => v is "0" or "1" ? v : null,
            "Enum" => e.Values?.FirstOrDefault(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)),
            "Integer" => Regex.IsMatch(v, @"\A[+-]?[0-9]+\z", RegexOptions.CultureInvariant) && decimal.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n >= e.Minimum && n <= e.Maximum ? n.ToString("0", CultureInfo.InvariantCulture) : null,
            "Float" => Regex.IsMatch(v, @"\A[+-]?[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant) && decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var f) && f >= e.Minimum && f <= e.Maximum ? f.ToString("0.############################", CultureInfo.InvariantCulture) : null,
            "ReadOnly" => Token(v) ? v : null,
            _ => null
        };
    }
    private static bool Identifier(string? s) => s is { Length: > 0 and <= 80 } && Regex.IsMatch(s, @"\A[A-Za-z][A-Za-z0-9_.]*\z", RegexOptions.CultureInvariant);
    private static bool Token(string? s) => s is { Length: > 0 and <= 128 } && !s.Any(c => char.IsControl(c) || c is ';' or '#' or '=' or '[' or ']' or '\\' or '/');
    private static bool Text(string? s, int max) => s is { Length: > 0 } && s.Length <= max && !s.Any(char.IsControl);
    private static bool Evidence(string? s) => s is not null && Regex.IsMatch(s, @"\Ahttps://github\.com/mavismmg/MFGAdaUnlock-RenoDx/blob/[a-f0-9]{40}/[A-Za-z0-9_./-]+(?:#L[0-9]+(?:-L[0-9]+)?)?\z", RegexOptions.CultureInvariant) && !s.Contains("..");
    private static InvalidDataException Invalid() => new("Invalid or untrusted settings catalog.");
}

/// <summary>Immutable, signed revisions in application storage. Never downloads, installs or evaluates code.</summary>
internal sealed class MfgSettingsCatalogStore(string root, IReadOnlyDictionary<string,string>? verificationKeys = null)
{
    internal static string UserRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "SettingsCatalogs");
    private void Safe(string path) => StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(root))!, path);
    internal MfgDynamicSettingsCatalog Load()
    {
        Safe(root);
        var current = MfgDynamicSettingsCatalog.BuiltIn;
        if (!Directory.Exists(root)) return current;
        var directories = Directory.GetDirectories(root);
        if (directories.Length > 100) throw new InvalidDataException("Too many settings catalogs.");
        foreach (var directory in directories.Order(StringComparer.Ordinal)) {
            if (!Regex.IsMatch(Path.GetFileName(directory), @"\A[A-F0-9]{64}\z")) continue;
            var jsonPath = Path.Combine(directory, "catalog.json"); var sigPath = Path.Combine(directory, "catalog.sig"); Safe(jsonPath); Safe(sigPath);
            var bytes = ReadBounded(jsonPath, MfgDynamicSettingsCatalog.MaximumBytes);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != Path.GetFileName(directory)) throw new InvalidDataException("Settings catalog hash mismatch.");
            var candidate = MfgDynamicSettingsCatalog.Verify(bytes, ReadBounded(sigPath, 1024), verificationKeys);
            if (candidate.Revision > current.Revision) current = candidate;
            else if (candidate.Revision == current.Revision && candidate.Identity != current.Identity) throw new InvalidDataException("Conflicting settings catalog revisions.");
        }
        return current;
    }
    internal MfgDynamicSettingsCatalog Import(byte[] bytes, byte[] signature)
    {
        var candidate = MfgDynamicSettingsCatalog.Verify(bytes, signature, verificationKeys); var current = Load();
        if (candidate.Revision <= current.Revision) throw new InvalidDataException("Settings catalog revision must be newer.");
        var destination = Path.Combine(root, candidate.Identity); Safe(destination);
        Directory.CreateDirectory(root);
        // Use a non-discoverable staging directory; publish both authenticated files together.
        var temporary = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N")); Safe(temporary); Directory.CreateDirectory(temporary);
        void WriteNew(string name, byte[] content) { var path=Path.Combine(temporary,name);Safe(path);using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);output.Write(content);output.Flush(true); }
        try { WriteNew("catalog.json", bytes); WriteNew("catalog.sig", signature); Safe(temporary); Safe(destination); Directory.Move(temporary, destination); }
        finally { if (Directory.Exists(temporary)) { Safe(temporary); foreach (var name in new[]{"catalog.json","catalog.sig"}) {var file=Path.Combine(temporary,name);Safe(file);if(File.Exists(file))File.Delete(file);} Directory.Delete(temporary); } }
        return candidate;
    }
    internal static byte[] ReadBounded(string path, int maximum)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length > maximum) throw new InvalidDataException("Settings catalog is too large.");
        using var m = new MemoryStream(); f.CopyTo(m); if (m.Length > maximum) throw new InvalidDataException("Settings catalog is too large."); return m.ToArray();
    }
}

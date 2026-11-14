using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

/// <summary>Exports only controlled summaries. Raw log text, paths, process details and exceptions never enter the report.</summary>
internal static class RuntimeDiagnosticsExport
{
    internal const string Review = "Review the diagnostic report before saving.";
    internal const string Scope = "Includes diagnostic statuses, known component names and numeric versions, and recognized warning types. Raw logs and personal paths are excluded.";
    internal const string LocationScope = "Locations describe files on disk. Backup folder names alone do not verify a backup. Loaded or active copies require runtime evidence.";
    private static readonly Regex NumericVersion = new(@"\A[0-9]+(?:\.[0-9]+){1,3}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal static string LocationLabel(RuntimeDiskLocation location) => location switch
    {
        RuntimeDiskLocation.BesideExecutable => "Beside the game EXE",
        RuntimeDiskLocation.GameSubfolder => "Within the game folder",
        RuntimeDiskLocation.ConfirmedBackup => "Verified backup copy",
        _ => "Other or unknown location"
    };

    private static readonly (string Key, string Title)[] Fields =
    [
        ("DLL installed:", "Installation evidence"),
        ("Loaded modules:", "Loaded modules"),
        ("Generation configuration:", "Generation configuration"),
        ("NVIDIA Dynamic MFG:", "NVIDIA Dynamic MFG"),
        ("Intel XeMFG:", "Intel XeMFG"),
        ("XMX:", "XMX"),
        ("Restart:", "Restart evidence")
    ];

    internal static string Create(RuntimeDiagnosticReport report, UiLocalization language)
    {
        var output = new StringBuilder();
        output.AppendLine(language.Lookup("Runtime Diagnostics"));
        output.AppendLine(language.Lookup(Scope)).AppendLine();
        output.AppendLine(language.Lookup("Present on disk, installed files, loaded modules and active frame generation are separate evidence levels.")).AppendLine();
        foreach (var (key, title) in Fields)
        {
            var status = report.Statuses.FirstOrDefault(s => s.StartsWith(key, StringComparison.Ordinal));
            var summary = Classify(title, status);
            output.Append(language.Lookup(title)).Append(" · ").Append(language.Lookup(summary));
            if (title == "NVIDIA Dynamic MFG" && summary != "Not verified")
                output.Append(" · ").Append(language.Lookup("Active state: Not verified"));
            output.AppendLine();
        }

        output.AppendLine().AppendLine(language.Lookup("Files on disk"));
        output.AppendLine(language.Lookup(LocationScope));
        var components = new List<string>();
        foreach (var item in report.DiskComponents ?? LegacyInventory(report))
        {
            var canonical = NvidiaComponentScanner.KnownComponentNames.FirstOrDefault(n => n.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null) continue;
            var formatted = TechnicalVersionDisplay.Format(item.Version);
            var version = formatted.Length <= 64 && NumericVersion.IsMatch(formatted) ? formatted : language.Lookup("Unknown version");
            var location = language.Lookup(LocationLabel(item.Location));
            if (item.BackupFolderMarker && item.Location != RuntimeDiskLocation.ConfirmedBackup)
                location += " · " + language.Lookup("Backup folder marker; provenance unverified");
            components.Add(canonical + " · " + version + " · " + location);
        }
        foreach (var group in components.GroupBy(s => s, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.AppendLine(group.Key + (group.Count() > 1 ? " · " + language.Format("Copies: {0}", group.Count()) : ""));
        if (components.Count == 0) output.AppendLine(language.Lookup("No component inventory in this snapshot."));

        foreach (var warning in new[] { RuntimeDiagnosticsService.DuplicateAddonWarning, RuntimeDiagnosticsService.FactoryHookWarning,
            RuntimeDiagnosticsService.EarlyLoadWarning, RuntimeDiagnosticsService.ProviderGateWarning })
            if (report.Warnings.Contains(warning, StringComparer.Ordinal)) output.AppendLine(language.Lookup(warning));
        return output.ToString();
    }

    private static IReadOnlyList<RuntimeDiskComponent> LegacyInventory(RuntimeDiagnosticReport report) =>
        report.Evidence.Where(e => e.StartsWith("Disk · ", StringComparison.Ordinal))
            .Select(e => e.Split('\n', 2)[0].Split(" · "))
            .Where(fields => fields.Length == 3)
            .Select(fields => new RuntimeDiskComponent(fields[1], fields[2], RuntimeDiskLocation.Unknown)).ToArray();

    private static string Classify(string title, string? status)
    {
        if (status is null) return "Not verified";
        return title switch
        {
            "Installation evidence" when status == "DLL installed: verified against an authenticated current installation transaction" => "Verified",
            "Loaded modules" when status.StartsWith("Loaded modules: Module mapping reported by a matching current addon session", StringComparison.Ordinal) => "Confirmed current session",
            "Generation configuration" when status.StartsWith("Generation configuration: XMX (experimental)", StringComparison.Ordinal) => "XMX (experimental)",
            "Generation configuration" when status.StartsWith("Generation configuration: Intel XeMFG (CUDA)", StringComparison.Ordinal) => "Intel XeMFG (CUDA)",
            "Generation configuration" when status.StartsWith("Generation configuration: NVIDIA DLSS-G", StringComparison.Ordinal) => "NVIDIA DLSS-G",
            "NVIDIA Dynamic MFG" when status.StartsWith("NVIDIA Dynamic MFG: Enabled / accepted in recorded ReShade session", StringComparison.Ordinal) => "Enabled / accepted in recorded ReShade session",
            "NVIDIA Dynamic MFG" when status.StartsWith("NVIDIA Dynamic MFG: Error / unsupported reported", StringComparison.Ordinal) => "Error / unsupported reported in recorded ReShade session",
            "Intel XeMFG" when status == "Intel XeMFG: Active reported by a fresh matching XeMFG SDK output sample (not a monitor/FPS measurement)" => "Active reported by a fresh matching XeMFG SDK output sample (not a monitor/FPS measurement)",
            "Intel XeMFG" when status == "Intel XeMFG: Error / unsupported initialization reported; active Not verified" => "Error / unsupported initialization reported; active Not verified",
            "Intel XeMFG" when status == "Intel XeMFG: Error reported; active Not verified" => "Error reported; active Not verified",
            "Restart evidence" when status == "Restart: Restart observed: matching process session began after installation" => "Restart observed: matching process session began after installation",
            _ => "Not verified"
        };
    }
}

namespace RenoDXMfgUnlocker;

/// <summary>Disk-only setup assessment. Never grants installation or runtime compatibility.</summary>
public sealed record RecommendedSetupReport(string Status, string Summary, IReadOnlyList<string> Findings)
{
    public bool CanApply => false; // No reviewed production write profile is shipped.
}
public sealed class RecommendedSetupService
{
    public RecommendedSetupReport Assess(NvidiaComponentScanResult scan, string addonVersion, bool gameRunning)
    {
        var findings = new List<string>();
        if (gameRunning) return new("Blocked", "Close the selected game before changing files or settings.", findings);
        if (scan.Error is not null) return new("Needs review", scan.Error, findings);
        if (scan.Warnings.Count > 0) findings.Add("Scan is incomplete: " + string.Join("; ", scan.Warnings));
        findings.Add(string.IsNullOrWhiteSpace(addonVersion)
            ? "Addon not confirmed beside this EXE. Check ReShade's actual addon location before installing from Home."
            : $"Installed addon file version: {TechnicalVersionDisplay.Format(addonVersion)}. This does not confirm that ReShade loaded it.");
        var files = scan.Entries.Where(x => !x.IsBackup && x.Status == "Found").ToArray();
        foreach (var name in new[] { "sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "nvngx_dlssg.dll" })
        {
            var matches = files.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            findings.Add(matches.Length == 0
                ? $"{name}: not observed. This is not proof the game needs this file."
                : string.Join("\n", matches.Select(x => $"{name} · {TechnicalVersionDisplay.Format(x.Version)}\n{x.Path}\n{x.IntegritySummary}")));
        }
        findings.AddRange(scan.PotentialConflicts);
        findings.Add("Dynamic MFG: disk files cannot prove support. Verify D3D12, driver 595.41+, loaded Streamline 2.14.1/2.14.16 and DLSS-G 310.9.1/310.9.16, and bIsDynamicMFGSupported in the addon panel. New .16 stack still needs per-game validation. Use the panel to request Dynamic; toggle game FG off/on and verify acceptance. No driver override is changed here.");
        findings.Add("Intel XeMFG: use the addon's Diagnostics → Intel runtime files → Install / update files. That helper installs its pinned libxess_fg.dll + libxell.dll pair with backup/rollback. Their presence alone does not prove a working input bridge. Use borderless/windowed mode and verify the effective backend in the addon panel. XeSS-SR, XeSS-FG, DLSS-SR and DLSS-G are distinct.");
        findings.Add("Settings: this utility edits six restart-required ReShade.ini values only for addon 1.6.0.0. Dynamic and XeMFG backend controls remain in the original panel; no preset values are guessed.");
        findings.Add("No production file replacement profile is approved. SDK download/staging is not an installation or compatibility check. Existing native components should be retained pending exact game/build evidence.");
        return new("Needs review", "Review existing components and follow the supported next steps. No automatic setup has been authorized.", findings);
    }
}

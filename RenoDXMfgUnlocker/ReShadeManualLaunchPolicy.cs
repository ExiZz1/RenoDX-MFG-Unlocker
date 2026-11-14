using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RenoDXMfgUnlocker;

/// <summary>Consent for visible official setup only. It grants no proxy replacement or compatibility claim.</summary>
internal sealed record ReShadeManualRiskApproval(string Fingerprint);

internal static class ReShadeManualLaunchPolicy
{
    internal const string Warning = "Existing proxy DLLs may conflict with ReShade. Review the listed files and decide in the official installer. Compatibility remains unverified.";
    internal const string Consent = "I understand the proxy risks and want to open the official installer for manual review.";
    internal const string Changed = "The selected target or scan changed. Rescan and confirm the proxy risks again.";
    internal const string Blocked = "Target inspection is incomplete or uses custom paths. Resolve the path or access issue before launching setup.";

    internal static bool HasCriticalBlock(ReShadeDetectionReport report) =>
        report.CriticalInspectionFailure || report.AlternativeAddonPaths.Count > 0;

    internal static bool RequiresConsent(ReShadeDetectionReport report) => report.HasConflicts ||
        report.Files.Any(f => f.Kind is ReShadeProxyKind.IdentityNotVerified or ReShadeProxyKind.UnknownThirdParty
            or ReShadeProxyKind.IdentifiedThirdParty or ReShadeProxyKind.RecognizedReShade);

    internal static ReShadeManualRiskApproval Approve(string root, string exe, ReShadeGraphicsApi api, ReShadeDetectionReport report)
    {
        if (HasCriticalBlock(report)) throw new InvalidDataException(Blocked);
        return new(Fingerprint(root, exe, api, report));
    }

    internal static void Validate(string root, string exe, ReShadeGraphicsApi api, ReShadeDetectionReport report,
        ReShadeManualRiskApproval? approval)
    {
        if (HasCriticalBlock(report)) throw new InvalidDataException(Blocked);
        if (approval is not null && approval.Fingerprint != Fingerprint(root, exe, api, report))
            throw new InvalidDataException(Changed);
        if (RequiresConsent(report) && approval is null)
            throw new InvalidDataException(Consent);
    }

    private static string Fingerprint(string root, string exe, ReShadeGraphicsApi api, ReShadeDetectionReport report) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Root = Path.GetFullPath(root), Exe = Path.GetFullPath(exe), Api = api, Report = report
        }))));

    internal static string Describe(ReShadeDetectionReport report, UiLocalization language)
    {
        var lines = report.Files.Where(f => f.BlocksGuidedSetup || f.Kind == ReShadeProxyKind.RecognizedReShade)
            .Select(f => Path.GetFileName(f.Path) + " · " + language.Lookup(ReShadePresentation.KindText(f.Kind))
                + "\n" + language.Translate(f.Reason));
        return language.Lookup(Warning) + "\n\n" + string.Join("\n\n", lines);
    }
}

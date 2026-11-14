using System.IO;

namespace RenoDXMfgUnlocker;
public enum ReShadeScanPhase { NotScanned,InProgress,Completed,Failed }

/// <summary>Pure presentation of evidence; none of these labels grant installation permission.</summary>
internal static class ReShadePresentation
{
    internal static string ScanText(ReShadeScanPhase phase,ReShadeDetectionReport? report)=>phase switch
    {
        ReShadeScanPhase.NotScanned=>"Not scanned yet. Select the EXE and graphics API, then choose Detect / rescan ReShade.",
        ReShadeScanPhase.InProgress=>"Scanning ReShade files. No files are being changed.",
        ReShadeScanPhase.Failed=>"Scan failed. Review Details, check access to the selected folder, then rescan.",
        _=>report?.State switch
        {
            ReShadeDetectionState.NotFound=>"Scan completed: ReShade not detected beside this EXE. Review the API and official setup requirements.",
            ReShadeDetectionState.FullAddonVerified=>"Scan completed: reviewed Full Add-on Support binary verified on disk. Runtime loading is not confirmed.",
            ReShadeDetectionState.StandardVerified=>"Scan completed: Standard ReShade detected. Full add-on loading is unavailable.",
            ReShadeDetectionState.DetectedUnknownSupport=>"Scan completed: ReShade detected; Full Add-on Support is not verified. Review Details.",
            ReShadeDetectionState.Conflict=>"Scan completed: proxy files require review. See the file evidence and manual setup requirements below.",
            _=>"Scan completed: installation evidence remains unverified. Review Details before continuing."
        }
    };
    internal static string KindText(ReShadeProxyKind kind)=>kind switch
    {
        ReShadeProxyKind.FullAddonVerified=>"Verified ReShade Full Add-on Support component",
        ReShadeProxyKind.RecognizedReShade=>"Recognized ReShade component",
        ReShadeProxyKind.StandardVerified=>"Standard ReShade component",
        ReShadeProxyKind.IdentifiedThirdParty=>"Identified third-party proxy (metadata only)",
        ReShadeProxyKind.UnknownThirdParty=>"Unknown third-party proxy",
        _=>"File identity not verified"
    };
    internal static string BlockReason(ReShadeDetectionReport report)
    {
        var reasons=report.Files.Where(f=>f.BlocksGuidedSetup).Select(f=>$"Installation blocked: {Path.GetFileName(f.Path)} · {KindText(f.Kind)} · {TechnicalVersionDisplay.Format(f.Version)}\nLocation: {f.Path}\nReason: {f.Reason}").ToList();
        if(reasons.Count==0&&report.HasConflicts)reasons.Add("Installation blocked: configuration or proxy inspection is incomplete. Review detection Details.");
        if(report.AlternativeAddonPaths.Count>0)reasons.Add("Custom ReShade/RenoDX addon paths need manual review. Existing settings and packages are preserved.");
        if(reasons.Count==0)return "";
        return string.Join("\n\n",reasons)+"\nExisting files were not modified. Review Details and the official repair/uninstall workflow; do not blindly delete, rename or replace DLLs.";
    }
    internal static string FileDetails(ReShadeFileEvidence file)=>$"Component: {Path.GetFileName(file.Path)}\nClassification: {KindText(file.Kind)}\nFile version: {TechnicalVersionDisplay.Format(file.Version)}\nProduct metadata: {(string.IsNullOrEmpty(file.ProductName)?"Unknown":file.ProductName)}\nFile description: {(string.IsNullOrEmpty(file.FileDescription)?"Unknown":file.FileDescription)}\nTarget: {file.Path}\nSHA-256: {(string.IsNullOrEmpty(file.Sha256)?"Not checked":file.Sha256)}\nReason: {file.Reason}";
    internal static string PackageDetails(PreparedReShadeInstaller? prepared)
    {
        var known=ReShadeInstallerVerifier.Reviewed;
        return $"Source: {known.Url}\nFile version: {known.Version}\nReviewed package SHA-256: {known.Sha256}\nPublisher certificate: {known.PublisherThumbprint}\nPackage SHA-256 check: {(prepared is null?"Not checked":"Verified at last preparation")}\nSignature and publisher pin: {(prepared is null?"Not checked":"Verified at last preparation")}\nWindows certificate-chain trust: Not independently established\n"+
            (prepared is null?"Prepare the package before claiming hash or publisher verification.":"The package matches the reviewed SHA-256 and publisher certificate. Full Windows certificate-chain trust was not independently established.")+"\nNo certificate is installed. Unverified Windows CA trust alone does not establish that the reviewed package is unsafe. All package checks are repeated before launch.";
    }
}

internal sealed record ReShadeUsageAdvice(bool KnownAntiCheatConcern,string Message,string? EvidenceUrl);
internal static class ReShadeUsagePolicy
{
    // Warning evidence only, never a compatibility whitelist or an installation grant. Reviewed 2026-11-01.
    internal static ReShadeUsageAdvice ForSteamAppId(string? appId)=>appId=="2807960"
        ?new(true,"Battlefield 6 uses EA Javelin Anticheat. Full Add-on Support installation is not recommended without publisher/anti-cheat documentation confirming permission. This warning does not predict a ban.","https://help.ea.com/en/articles/battlefield/battlefield-6/play-by-the-rules/")
        :new(false,"Multiplayer/anti-cheat status is not established for this selection. A successful scan does not mean ReShade is permitted or safe in online play.",null);
    internal const string GeneralWarning="Full Add-on Support may trigger anti-cheat restrictions or violate game/service rules. Check publisher and anti-cheat documentation before multiplayer use; no game is approved by this scan.";
}

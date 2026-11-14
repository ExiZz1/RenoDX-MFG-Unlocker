using System.Collections.ObjectModel;

namespace RenoDXMfgUnlocker;
internal sealed record ReviewedReShadeRuntime(string FileVersion,string Sha256,ReShadeDetectionState Identity);
internal sealed record ReviewedReShadePackage(ReviewedReShadeInstaller Installer,string InstallerFileVersion,IReadOnlyList<ReviewedReShadeRuntime> Runtimes,string EvidenceUrl);

/// <summary>Compiled, reviewed data only. No remote execution configuration, arbitrary URLs, or user-supplied pins.</summary>
internal static class ReShadeReviewedCatalog
{
    internal const int SchemaVersion=1;
    internal static readonly ReviewedReShadePackage Current=new(
        new("6.8.0","ReShade_Setup_6.8.0_Addon.exe","https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe",4318424,"AFE4C8F13048306307983B8B3D41D5BF00A86820440B0E57DEA10950E1176445","589690208A5E52FB96980C4A6698F50ACD47C49F"),
        "6.8.0.0",Array.AsReadOnly(new[]{new ReviewedReShadeRuntime("6.8.0.2155","0CEE63F9C9F13F3AC909C5B4903F4DBB4B719A7AB3B4F13B0DEAF83C814B94F7",ReShadeDetectionState.FullAddonVerified)}),"https://reshade.me/");
    internal static readonly IReadOnlyList<ReviewedReShadePackage> Packages=Array.AsReadOnly(new[]{Current});
    internal static readonly IReadOnlyDictionary<string,ReShadeDetectionState> RuntimeHashes=new ReadOnlyDictionary<string,ReShadeDetectionState>(Packages.SelectMany(p=>p.Runtimes).ToDictionary(r=>r.Sha256,r=>r.Identity,StringComparer.OrdinalIgnoreCase));
}

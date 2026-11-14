using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public enum CompatibilityEvidenceStatus { Verified, PartiallyVerified, NeedsReview, Unsupported }
public enum TechnologySupportStatus { Supported, Unsupported, Unknown }
public enum IntegrationSupportStatus { Confirmed, PartiallyConfirmed, Unknown, Unsupported }
public enum CompatibilityAction { NoChangesNeeded, InstallMissingComponents, ReplaceExistingComponents, NeedsReview, Unsupported, Blocked }
public enum ComponentOperation { Add, Replace, Remove }

public sealed record GameConfigurationFingerprint(
    string? SteamAppId,
    string? SteamBuildId,
    string? ExecutableName,
    string? ExecutableVersion,
    string? GraphicsApi);

public sealed record CompatibilityEvidence(string Claim, string Source, DateOnly CheckedOn, bool IsPrimarySource);

public sealed record CompatibilityComponentReference(
    string FileName,
    string RelativePath,
    string Technology,
    string ExpectedVersion,
    long ExpectedSizeBytes,
    string ExpectedSha256,
    bool IsRequiredForProfile,
    IReadOnlyList<string> Dependencies,
    IReadOnlySet<ComponentOperation> AllowedOperations,
    string OperationRestriction);

public sealed record GameCompatibilityProfile(
    int SchemaVersion,
    string ProfileId,
    string DisplayName,
    string SteamAppId,
    string SteamBuildId,
    string ExecutableName,
    string ExecutableVersion,
    string GraphicsApi,
    string IntegrationMechanism,
    CompatibilityEvidenceStatus EvidenceStatus,
    IReadOnlyDictionary<string, TechnologySupportStatus> TechnologySupport,
    IntegrationSupportStatus IntegrationCompatibility,
    IReadOnlyList<CompatibilityComponentReference> Components,
    IReadOnlyList<string> KnownConflicts,
    IReadOnlyList<CompatibilityEvidence> Evidence,
    DateOnly CheckedOn,
    bool IsReadOnlyDiagnostic,
    IReadOnlyList<string> BlockedOperationReasons);

public sealed record CompatibilityComponentAssessment(
    string FileName,
    string State,
    string ObservedVersion,
    string ObservedPath,
    long? ObservedSizeBytes,
    string? ObservedSha256,
    string Message);

public sealed record GameCompatibilityAssessment(
    GameCompatibilityProfile? Profile,
    CompatibilityEvidenceStatus EvidenceStatus,
    CompatibilityAction RecommendedAction,
    TechnologySupportStatus MfgSupport,
    IntegrationSupportStatus IntegrationCompatibility,
    string ProfileLabel,
    string ConfigurationLabel,
    string Summary,
    string Details,
    IReadOnlyList<CompatibilityComponentAssessment> Components,
    IReadOnlyList<string> BlockedReasons);

public interface IGameCompatibilityProfileCatalog
{
    GameCompatibilityProfile? FindExact(GameConfigurationFingerprint configuration);
    GameCompatibilityProfile? FindByGameId(string? steamAppId);
}

/// <summary>
/// Versioned, evidence-backed profiles. Unknown game configurations deliberately have no permissions.
/// The Witcher reference is diagnostic only; observations are a local snapshot, not publisher-authenticated files.
/// </summary>
public sealed class BuiltInGameCompatibilityProfileCatalog : IGameCompatibilityProfileCatalog
{
    public static readonly DateOnly Witcher3SnapshotDate = new(2026, 10, 27);

    private static readonly GameCompatibilityProfile Witcher3Remastered = new(
        SchemaVersion: 1,
        ProfileId: "steam-292030-remastered-5.0.0.1048522-build-25773555",
        DisplayName: "The Witcher 3: Wild Hunt — Remastered",
        SteamAppId: "292030",
        SteamBuildId: "25773555",
        ExecutableName: "witcher3.exe",
        ExecutableVersion: "5.0.0.1048522",
        GraphicsApi: "DirectX 12",
        IntegrationMechanism: "Native NVIDIA DLSS 4.5 / DLSS-G integration; MFG support is supplied by the game and NVIDIA runtime.",
        EvidenceStatus: CompatibilityEvidenceStatus.PartiallyVerified,
        TechnologySupport: new Dictionary<string, TechnologySupportStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["DLSS Super Resolution"] = TechnologySupportStatus.Supported,
            ["DLSS Frame Generation"] = TechnologySupportStatus.Supported,
            ["DLSS Multi Frame Generation"] = TechnologySupportStatus.Supported,
            ["NVIDIA App MFG override for this exact executable"] = TechnologySupportStatus.Unknown
        },
        IntegrationCompatibility: IntegrationSupportStatus.PartiallyConfirmed,
        Components:
        [
            Component("sl.interposer.dll", "Streamline runtime", "2.14.1.0", 652928, "8c87c9499461da561edd529aa9bf7831d67d7b94ebb1c1a5ed54ef4934e1ea4c"),
            Component("sl.common.dll", "Streamline runtime", "2.14.1.0", 843392, "82924a8954dd671e09351c5de0eb87ad0eb25b944cc9f9ab955ca1d9950de15d"),
            Component("sl.dlss_g.dll", "DLSS Frame Generation / MFG plugin", "2.14.1.0", 636032, "f4a6b2b14dcc0b1485989e430d3b4e3a44ac1800b92ba1ad74f476e64fb2b09c"),
            Component("sl.reflex.dll", "Streamline Reflex plugin", "2.14.1.0", 388736, "0ce9725e3e03ea9e7f81d008b57f33ee365973d2e349131c8b1c3e3378fe2db0"),
            Component("sl.pcl.dll", "Streamline runtime", "2.14.1.0", 360064, "f13d51cfa05f4cd514df2026049e2db8adf359221713170ad386fd499915b582"),
            Component("nvngx_dlss.dll", "DLSS Super Resolution NGX runtime", "310.9.1.0", 58956912, "3975567b8943c53accee397f2b72380092f84f162d00b0d2c7d08a1025c563983"),
            Component("nvngx_dlssg.dll", "DLSS Frame Generation NGX runtime", "310.9.1.0", 7460976, "ff6e90eb78b827927dff5b4ecc6b1c870c2e9bca29ed9f48c7d348cc9e170b82")
        ],
        KnownConflicts: ["Replacing Streamline or NGX files is not authorized by this diagnostic profile."],
        Evidence:
        [
            new("The Remastered update includes DLSS 4.5 and Multi Frame Generation; the 2022 Next-Gen release is a distinct configuration.", "https://www.nvidia.com/en-us/geforce/news/witcher-3-wild-hunt-remastered-path-tracing-dlss-4-5-ray-reconstruction/", Witcher3SnapshotDate, true),
            new("NVIDIA documents MFG override as an NVIDIA App feature for compatible titles when in-game Frame Generation is enabled; this profile does not control NVIDIA App.", "https://nvidia.custhelp.com/app/answers/detail/a_id/5620", Witcher3SnapshotDate, true),
            new("Executable version, Steam build ID, component versions, sizes and hashes were read from one local installation; no runtime module-load proof was collected.", "Local read-only observation: D:\\Steam\\steamapps\\common\\The Witcher 3", Witcher3SnapshotDate, false)
        ],
        CheckedOn: Witcher3SnapshotDate,
        IsReadOnlyDiagnostic: true,
        BlockedOperationReasons:
        [
            "This profile authorizes no add, replace or remove operation.",
            "Local file hashes are observations, not an official NVIDIA/CD PROJEKT RED manifest or proof that the game loaded those files.",
            "NVIDIA App override availability for the exact executable has not been verified in NVIDIA App."
        ]);

    private readonly IReadOnlyList<GameCompatibilityProfile> profiles = [Witcher3Remastered];

    public GameCompatibilityProfile? FindExact(GameConfigurationFingerprint configuration) =>
        profiles.FirstOrDefault(profile => IsExact(profile, configuration));

    public GameCompatibilityProfile? FindByGameId(string? steamAppId) =>
        string.IsNullOrWhiteSpace(steamAppId) ? null : profiles.FirstOrDefault(profile => profile.SteamAppId == steamAppId);

    private static bool IsExact(GameCompatibilityProfile profile, GameConfigurationFingerprint configuration) =>
        profile.SteamAppId.Equals(configuration.SteamAppId, StringComparison.Ordinal)
        && profile.SteamBuildId.Equals(configuration.SteamBuildId, StringComparison.Ordinal)
        && profile.ExecutableName.Equals(configuration.ExecutableName, StringComparison.OrdinalIgnoreCase)
        && NormalizeVersion(profile.ExecutableVersion) == NormalizeVersion(configuration.ExecutableVersion)
        && profile.GraphicsApi.Equals(configuration.GraphicsApi, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = Regex.Match(value.Trim(), @"\d+(?:[.,]\d+)+", RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Replace(',', '.') : string.Empty;
    }

    private static CompatibilityComponentReference Component(string name, string technology, string version, long size, string sha256) =>
        new(name, "bin/x64_dx12/" + name, technology, version, size, sha256, true, Array.Empty<string>(),
            new HashSet<ComponentOperation>(), "Read-only reference snapshot; add/replace/remove are prohibited.");
}

public sealed class GameCompatibilityEvaluator(IGameCompatibilityProfileCatalog catalog)
{
    public GameCompatibilityAssessment Evaluate(GameConfigurationFingerprint configuration, NvidiaComponentScanResult? scan = null)
    {
        var exact = catalog.FindExact(configuration);
        if (exact is null)
        {
            var knownGame = catalog.FindByGameId(configuration.SteamAppId);
            if (knownGame is not null)
            {
                var mismatch = DescribeMismatch(knownGame, configuration);
                return new(knownGame, CompatibilityEvidenceStatus.NeedsReview, CompatibilityAction.NeedsReview,
                    TechnologySupportStatus.Unknown, IntegrationSupportStatus.Unknown,
                    knownGame.DisplayName + " · configuration not matched", Configuration(configuration),
                    "Needs review · this exact build/EXE/API is outside the checked profile.", mismatch,
                    Array.Empty<CompatibilityComponentAssessment>(), knownGame.BlockedOperationReasons);
            }
            return new(null, CompatibilityEvidenceStatus.NeedsReview, CompatibilityAction.NeedsReview,
                TechnologySupportStatus.Unknown, IntegrationSupportStatus.Unknown,
                "No reviewed profile", Configuration(configuration),
                "Needs review · no evidence-backed profile exists for this game configuration.",
                "No installation or replacement is authorized for unknown configurations.",
                Array.Empty<CompatibilityComponentAssessment>(), ["Unknown profiles have no permitted operations."]);
        }

        if (scan is null || scan.Error is not null)
        {
            return new(exact, exact.EvidenceStatus, CompatibilityAction.NeedsReview,
                exact.TechnologySupport["DLSS Multi Frame Generation"], exact.IntegrationCompatibility,
                exact.DisplayName + " · diagnostic profile", Configuration(configuration),
                "Profile matched · scan components to compare against the local reference snapshot.",
                "NVIDIA publicly documents native DLSS 4.5/MFG for Remastered. File presence does not establish runtime loading. " + exact.BlockedOperationReasons[0],
                Array.Empty<CompatibilityComponentAssessment>(), exact.BlockedOperationReasons);
        }

        var assessments = exact.Components.Select(reference =>
        {
            var expectedPath = Path.GetFullPath(Path.Combine(scan.RootDirectory!, reference.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var entry = scan.Entries.FirstOrDefault(item => item.Status == "Found" && !item.IsBackup
                && item.Name.Equals(reference.FileName, StringComparison.OrdinalIgnoreCase)
                && Path.GetFullPath(item.Path).Equals(expectedPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return new CompatibilityComponentAssessment(reference.FileName, "Not found at reference path", "—", expectedPath, null, null,
                    "Missing at the observed game location. This profile does not authorize installation.");
            var nonBackupCopies = scan.Entries.Where(item => item.Status == "Found" && !item.IsBackup
                && item.Name.Equals(reference.FileName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nonBackupCopies.Length > 1)
                return new CompatibilityComponentAssessment(reference.FileName, "Multiple game-folder copies need review", entry.Version, entry.Path,
                    entry.SizeBytes, entry.Sha256, $"Found at {nonBackupCopies.Length} non-backup locations. The scanner cannot determine which copy the game loads.");
            var versionMatches = BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(entry.Version) == BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(reference.ExpectedVersion);
            var sizeMatches = entry.SizeBytes == reference.ExpectedSizeBytes;
            var hashMatches = entry.Sha256?.Equals(reference.ExpectedSha256, StringComparison.OrdinalIgnoreCase) == true;
            var matches = versionMatches && sizeMatches && hashMatches;
            return new CompatibilityComponentAssessment(reference.FileName, matches ? "Matches local reference snapshot" : "Differs from local reference snapshot",
                entry.Version, entry.Path, entry.SizeBytes, entry.Sha256,
                matches ? "Observed version, size and SHA-256 match the one-machine reference snapshot; runtime loading is not confirmed."
                    : "Version/size/hash differs or could not be read. No operation is authorized.");
        }).ToArray();

        var matched = assessments.All(item => item.State == "Matches local reference snapshot");
        var action = matched ? CompatibilityAction.NoChangesNeeded : CompatibilityAction.NeedsReview;
        var summary = matched
            ? "No changes needed · observed files match the local reference snapshot; no DLL changes are authorized."
            : "Needs review · one or more components differ from the local reference snapshot; installation remains blocked.";
        var matchedCount = assessments.Count(item => item.State == "Matches local reference snapshot");
        var details = $"Reference snapshot: {matchedCount}/{assessments.Length} component(s) match. Game technology: DLSS Multi Frame Generation is documented for Remastered. Integration: native D3D12 path is documented; this scan cannot prove runtime loading. "
            + "NVIDIA App per-title override availability is not confirmed by this profile. " + string.Join(" ", exact.BlockedOperationReasons);
        return new(exact, exact.EvidenceStatus, action, exact.TechnologySupport["DLSS Multi Frame Generation"], exact.IntegrationCompatibility,
            exact.DisplayName + " · diagnostic profile", Configuration(configuration), summary, details, assessments, exact.BlockedOperationReasons);
    }

    private static string Configuration(GameConfigurationFingerprint value) =>
        $"Steam AppID {value.SteamAppId ?? "unknown"} · build {value.SteamBuildId ?? "unknown"} · {value.ExecutableName ?? "EXE unknown"} {TechnicalVersionDisplay.Format(value.ExecutableVersion)} · API {value.GraphicsApi ?? "unknown"}";

    private static string DescribeMismatch(GameCompatibilityProfile profile, GameConfigurationFingerprint value) =>
        $"Known title, but profile requires AppID {profile.SteamAppId}, build {profile.SteamBuildId}, {profile.ExecutableName} {profile.ExecutableVersion}, API {profile.GraphicsApi}; detected {Configuration(value)}. No changes were made.";
}

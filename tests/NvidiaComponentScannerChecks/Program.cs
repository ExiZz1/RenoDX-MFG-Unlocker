using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RenoDXMfgUnlocker;

if(args.Length==1&&args[0]=="--rc6-e2e-cleanup"){Environment.ExitCode=Rc6ProcessChecks.Run(true);return;}
if(args.Length==2&&args[0]=="--dev4-research"){Environment.ExitCode=Dev4UpstreamChecks.Research(args[1]);return;}
if(args.Length==2&&args[0]=="--dev4-reanalyze"){Environment.ExitCode=Dev4UpstreamChecks.Reanalyze(args[1]);return;}
if(args.Length==1&&args[0]=="--rc6-e2e"){Environment.ExitCode=Rc6ProcessChecks.Run();return;}
if(args.Length>0&&args[0]=="--research-reshade-cli"){Environment.ExitCode=ReShadeCliResearch.Run(args.Skip(1).ToArray());return;}
if(args.Length==2&&args[0]=="--root-lock-check"){try{using var operation=RootOperation.Enter(args[1]);Environment.ExitCode=0;}catch(IOException){Environment.ExitCode=23;}return;}

var checks = new List<(string Name, Action Run)>
{
    ("missing components are reported individually", MissingComponents),
    ("nested copies and file versions are discovered", NestedCopiesAndVersions),
    ("backup copies are classified separately", BackupCopies),
    ("backup filtering defaults hidden and toggles without rescanning", BackupFilteringAndToggle),
    ("backup folders are detected case-insensitively from full paths", BackupPathClassification),
    ("installation root is distinct from the EXE folder", InstallationRootClassification),
    ("version metadata failures are non-fatal", VersionFailure),
    ("inaccessible folders produce warnings", AccessFailure),
    ("missing EXE returns a clear failure", MissingExe),
    ("only official SDK archives are listed", StreamlineArchiveFilter),
    ("missing official digest is reported", MissingDigest),
    ("SHA-256 succeeds and rejects mismatch", DigestChecks),
    ("existing local SDK archive is recognized only after size and digest verification", ExistingLocalArchiveValidation),
    ("wrong local SDK digest is rejected", LocalArchiveRejectsWrongDigest),
    ("missing local SDK archive is reported without download", LocalArchiveMissing),
    ("switching selected SDK releases resolves each archive independently", LocalArchiveReleaseSwitch),
    ("corrupt ZIP is rejected without staging leftovers", CorruptZip),
    ("Zip Slip paths are rejected", ZipSlip),
    ("duplicate ZIP paths are rejected", DuplicateZipPaths),
    ("completed download verifies digest and digestless download reports limitation", SuccessfulDownloads),
    ("cancelled and interrupted downloads leave no partial files", DownloadCancellationAndInterruption),
    ("staging stays outside the game and extracts only allowed files", SafeStaging),
    ("install preview identifies new files without writing", PreviewNewFiles),
    ("install preview reports existing files and backup candidates read-only", PreviewExistingFiles),
    ("unknown existing file versions require review", PreviewUnknownVersion),
    ("missing staged source returns a clear review message", PreviewMissingSource),
    ("install preview rejects sources outside the verified staging root", PreviewRejectsOutsideSource),
    ("install preview rejects targets outside the selected game", PreviewRejectsOutsideTarget),
    ("refreshing an install preview does not change source or target files", PreviewRefreshIsReadOnly),
    ("transaction backs up and restores a replaced file", TransactionReplaceRestore),
    ("transaction rollback removes an unchanged file it created", TransactionNewFileRollback),
    ("transaction refuses to restore a file changed by another process", TransactionRejectsChangedTarget),
    ("transaction rejects corrupted and tampered manifests", TransactionRejectsManifestTampering),
    ("transaction rejects tampered backup bytes before touching targets", TransactionRejectsTamperedSnapshot),
    ("transaction rejects an unavailable manifest key without touching targets", TransactionRejectsMissingKey),
    ("transaction rejects traversal, absolute and duplicate target paths", TransactionRejectsUnsafePaths),
    ("transaction rejects reparse-point paths", TransactionRejectsReparsePoints),
    ("transaction rejects files changed or created after preparation", TransactionRejectsStartConflicts),
    ("rollback preserves a file changed after the transaction write", TransactionRollbackPreservesExternalChange),
    ("restore preflights all files before changing any", TransactionRestorePreflight),
    ("cancellation rolls back prior changes", TransactionCancellationRollback),
    ("a mid-transaction failure rolls back earlier changes", TransactionRollsBackPartialFailure),
    ("transactions preserve prior backups without overwriting", TransactionPreservesBackups)
    ,("Streamline staging evidence requires an official digest and records component hashes", StreamlineEvidenceCapture)
    ,("unknown game compatibility remains Needs review and cannot install", StreamlineNoCompatibilityGrant)
    ,("explicit reviewed test grant installs a new file and restores it", StreamlineInstallNewAndRestore)
    ,("explicit reviewed test grant backs up and replaces a file", StreamlineReplaceAndRestore)
    ,("changed staged sources and archives block installation", StreamlineChangedSourcesBlocked)
    ,("destination changes after preview require a new review", StreamlineTargetChangedAfterPreview)
    ,("pre-cancelled transaction does not write targets", TransactionCancelledBeforeStart)
    ,("interrupted in-progress transaction can be safely restored", TransactionInterruptedRecovery)
    ,("successive transactions preserve and restore each prior version", TransactionSuccessiveVersions)
    ,("game compatibility profile requires exact Steam build, EXE version and API", CompatibilityProfileExactFingerprint)
    ,("unknown or mismatched game configurations remain review-only", CompatibilityProfileUnknownConfigurations)
    ,("Witcher diagnostic reference reports no changes without granting file operations", CompatibilityProfileReferenceSnapshot)
    ,("missing or changed reference components require review", CompatibilityProfileComponentMismatch)
    ,("scanner records physical component size and SHA-256", ScannerIntegrityMetadata)
    ,("scanner reports duplicate sets and divergent copies without hiding them", ScannerDuplicateGroups)
    ,("MFG settings expose only documented restart-required values", MfgSettingsDefinitionsAndRead)
    ,("MFG settings update is backed up and restored transactionally", MfgSettingsTransactionalUpdate)
    ,("MFG settings reject stale config and unsupported addon versions", MfgSettingsRejectStaleAndUnsupported)
    ,("MFG settings reject invalid and duplicate values without losing INI data", MfgSettingsRejectInvalidAndDuplicate)
};

checks.Add(("setup review blocks running games", () => {
    var report = new RecommendedSetupService().Assess(new(null, [], [], 0, null), "1.6.0.0", true);
    if(report.Status != "Blocked" || report.CanApply) throw new Exception("Running game allowed");
}));
checks.Add(("setup review never authorizes an unknown configuration", () => {
    var report = new RecommendedSetupService().Assess(new("test", [], [], 1, null), "", false);
    if(report.Status != "Needs review" || report.CanApply || !report.Findings.Any(x => x.Contains("not observed"))) throw new Exception("Missing components incorrectly authorized");
}));
checks.Add(("setup review reports failed scans without authorizing writes", () => {
    var report = new RecommendedSetupService().Assess(new(null, [], [], 0, "access denied"), "", false);
    if(report.CanApply || report.Summary != "access denied") throw new Exception("Error lost");
}));
checks.Add(("setup review excludes backup DLLs from installed runtime evidence", () => {
    var report = new RecommendedSetupService().Assess(new("test", [new("sl.common.dll", "Found", "2.14.16", "backup/sl.common.dll", "Backup copy", true)], [], 1, null), "", false);
    if(!report.Findings.Any(x => x.StartsWith("sl.common.dll: not observed"))) throw new Exception("Backup treated as runtime");
}));
checks.Add(("staging rejects forged inventory hashes even with a valid archive", () => {
    var f = StreamlineInstallerFixture("forged-inventory");
    var bytes = Encoding.UTF8.GetBytes("substituted local payload");
    File.WriteAllBytes(Path.Combine(f.StagingDirectory,"bin","x64","sl.common.dll"), bytes);
    var forged = f.Evidence with { Files = [f.Evidence.Files[0] with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() }] };
    ExpectInvalid(() => new StreamlineComponentInstaller(f.Downloads,f.Staging,new BackupRestoreService(f.Transactions))
        .CreatePlanAsync(forged,"12345",f.GameRoot,f.GameRoot,new HashSet<string>{"sl.common.dll"}).GetAwaiter().GetResult());
}));
checks.Add(("staging evidence rejects altered bytes before inventory capture", () => {
    var f = StreamlineInstallerFixture("alter-before-capture");
    File.WriteAllText(Path.Combine(f.StagingDirectory,"bin","x64","sl.common.dll"),"substituted");
    ExpectInvalid(() => new StreamlineStagingEvidenceService().CaptureAsync(f.Downloads,f.Staging,f.ArchivePath,f.Evidence.Asset,f.StagingDirectory).GetAwaiter().GetResult());
}));
checks.Add(("offline package pipeline downloads stages installs verifies reinstalls and restores", OfflineComponentPipeline));
checks.Add(("advanced manual coherent set installs reinstalls and restores all originals", AdvancedCycle));
checks.Add(("advanced blocks missing core and unknown plugins", AdvancedDependencies));
checks.Add(("advanced blocks unknown API, running game and mismatched provider", AdvancedRequirements));
checks.Add(("advanced blocks altered source and stale target", AdvancedConflicts));
checks.Add(("local SDK import is digest-bound and reusable without overwrite", () => {
    var dir=Temp("local-import");Directory.CreateDirectory(dir);var zip=MakeZip(dir,("bin/x64/sl.common.dll","test"));var bytes=File.ReadAllBytes(zip);
    var asset=Asset("streamline-sdk-v1.zip","https://github.com/NVIDIA-RTX/Streamline/releases/download/v1/streamline-sdk-v1.zip",bytes.Length) with {Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()};
    var target=StreamlineLocalArchiveImporter.ImportAsync(zip,Path.Combine(dir,"downloads"),asset).GetAwaiter().GetResult();
    Assert(StreamlineLocalArchiveImporter.ImportAsync(zip,Path.Combine(dir,"downloads"),asset).GetAwaiter().GetResult()==target,"reuse failed");
    File.WriteAllText(target,"external");
    ExpectInvalid(()=>StreamlineLocalArchiveImporter.ImportAsync(zip,Path.Combine(dir,"downloads"),asset).GetAwaiter().GetResult());
    Assert(File.ReadAllText(target)=="external","stored archive overwritten");
}));
checks.Add(("local SDK import blocks unconfirmed digest", () => {
    var dir=Temp("local-import-bad");Directory.CreateDirectory(dir);var zip=MakeZip(dir,("bin/x64/sl.common.dll","test"));
    ExpectInvalid(()=>StreamlineLocalArchiveImporter.ImportAsync(zip,Path.Combine(dir,"downloads"),Asset("streamline-sdk-v1.zip","https://github.com/NVIDIA-RTX/Streamline/releases/download/v1/streamline-sdk-v1.zip",new FileInfo(zip).Length)).GetAwaiter().GetResult());
}));
checks.Add(("signed NVIDIA DLSS-G provider passes native verification and rejects wrong snapshot hash", ProviderAuthentic));
checks.Add(("provider verifier rejects modified signature and non-native files", ProviderCorrupt));
checks.Add(("manual provider and Streamline install in one transaction and restore six originals", ProviderCombinedCycle));
checks.Add(("manual provider mismatch is blocked before writes", ProviderMismatch));
checks.Add(("legacy signed NVIDIA DLSS-G 310.9.1 is accepted", () => {
    var path=Environment.GetEnvironmentVariable("RENODX_TEST_SIGNED_PROVIDER_LEGACY");
    if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)) throw new SkipCheckException("Provide an isolated genuine legacy provider fixture.");
    Assert(new DlssGProviderVerifier().Verify(path).Version=="310.9.1.0","legacy identity differs");
}));
checks.AddRange(IntelChecks.All());
checks.AddRange(RuntimeDiagnosticsChecks.All());
checks.AddRange(LocalizationChecks.All());
checks.AddRange(WizardChecks.All());
checks.AddRange(Rc8UxChecks.All());
checks.AddRange(UiCleanupChecks.All());
checks.AddRange(IntelSdkChecks.All());
checks.AddRange(Stage21Checks.All());
checks.AddRange(Stage3Checks.All());
checks.AddRange(Rc9Checks.All());
checks.AddRange(Rc10Checks.All());
checks.AddRange(Rc11Checks.All());
checks.AddRange(Rc12Checks.All());
checks.AddRange(Rc13Checks.All());
checks.AddRange(ReShadeChecks.All());
checks.AddRange(ReShadeAlpha2Checks.All());
checks.AddRange(ReShadeAlpha3Checks.All());
checks.AddRange(ReShadeAlpha4Checks.All());
checks.AddRange(Alpha6Checks.All());
checks.AddRange(Alpha7Checks.All());
checks.AddRange(Rc1SettingsChecks.All());
checks.AddRange(Rc2UxChecks.All());
checks.AddRange(Rc3GenerationChecks.All());
checks.AddRange(Rc4Checks.All());
checks.AddRange(Rc5Checks.All());
checks.AddRange(Rc6Checks.All());
checks.AddRange(ApprovedProductionTrustChecks.All());
checks.AddRange(StableReleaseChecks.All());
checks.AddRange(Mfg181CompatibilityChecks.All());
checks.AddRange(DynamicSettingsChecks.All());
checks.AddRange(Dev2SettingsChecks.All());
checks.AddRange(Dev3SettingsChecks.All());
checks.AddRange(Dev4UpstreamChecks.All());
checks.AddRange(Dev5LocalizationChecks.All());
checks.AddRange(Stable130Checks.All());
checks.AddRange(Upstream185Checks.All());
checks.AddRange(Dev14Checks.All());
checks.AddRange(Dev142Checks.All());
checks.AddRange(Dev143Checks.All());

checks.Add(("rc9 provider-only change leaves identical Streamline files untouched and restores provider",ProviderOnlyCycle));
checks.Add(("Windows symbolic link specifically is rejected (no junction fallback)",TransactionRejectsSymbolicLinkOnly));
if(args.Length==1&&args[0]=="--nonvisual"){
    var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tools/NonVisualChecks.json"));
    var names=JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))??throw new InvalidDataException("Missing nonvisual check selection");
    var selected=names.ToHashSet(StringComparer.Ordinal);
    checks=checks.Where(c=>selected.Contains(c.Name)).ToList();
    if(checks.Count!=selected.Count)throw new InvalidDataException("Nonvisual check selection contains missing or duplicate names");
}
else if(args.Length>0) checks=checks.Where(c=>c.Name.Contains(args[0],StringComparison.OrdinalIgnoreCase)).ToList();
var skippedChecks = 0;
var failedChecks = 0;
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception ex) when (ex is SkipCheckException or IntelFixtureUnavailableException)
    {
        skippedChecks++;
        Console.WriteLine($"SKIP {check.Name}: {ex.Message}");
    }
    catch (Exception ex) { failedChecks++; Console.WriteLine($"FAIL {check.Name}: {ex}"); }
}
Console.WriteLine($"Passed {checks.Count - skippedChecks - failedChecks} automated checks; failed {failedChecks}; skipped {skippedChecks}.");
WpfCheckHost.Close();
Environment.ExitCode = failedChecks == 0 ? 0 : 1;

static void MissingComponents()
{
    var (fs, exe, root) = Fixture();
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    Assert(result.Error is null, "unexpected scan error");
    Assert(result.RootDirectory == root, "incorrect game folder");
    Assert(result.Entries.Count == NvidiaComponentScanner.KnownComponentNames.Count, "expected every known component");
    Assert(result.Entries.All(x => x.Status == "Not found"), "missing DLLs should be reported as Not found");
}

static void NestedCopiesAndVersions()
{
    var (fs, exe, root) = Fixture();
    var nested = Path.Combine(root, "Binaries", "Win64");
    fs.AddDirectory(nested);
    var first = Path.Combine(root, "sl.common.dll");
    var second = Path.Combine(nested, "sl.common.dll");
    var dlssg = Path.Combine(nested, "nvngx_dlssg.dll");
    fs.AddFile(first, "1.2.3.4");
    fs.AddFile(second, null);
    fs.AddFile(dlssg, "310.9.16.0");
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    var copies = result.Entries.Where(x => x.Name == "sl.common.dll").ToArray();
    Assert(copies.Length == 2, "both DLL copies should be listed");
    Assert(copies.Any(x => x.Status == "Found" && x.Version == "1.2.3.4" && x.Path == first), "known version was not reported");
    Assert(copies.Any(x => x.Status == "Found" && x.Version == "Unknown version" && x.Path == second), "unknown version state was not reported");
    Assert(result.Entries.Any(x => x.Name == "nvngx_dlssg.dll" && x.Status == "Found" && x.Version == "310.9.16.0"), "DLSSG version missing");
}

static void BackupCopies()
{
    var (fs, exe, root) = Fixture();
    var backups = Path.Combine(root, "MFG_Backup_2026-10-27");
    var subfolder = Path.Combine(root, "SP");
    fs.AddDirectory(backups);
    fs.AddDirectory(subfolder);
    fs.AddFile(Path.Combine(root, "sl.common.dll"), "2.1");
    fs.AddFile(Path.Combine(subfolder, "sl.common.dll"), "2.2");
    fs.AddFile(Path.Combine(backups, "sl.common.dll"), "1.0");
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    var copies = result.Entries.Where(x => x.Name == "sl.common.dll").ToArray();
    Assert(copies.Length == 3, "all component copies should remain visible to callers");
    Assert(copies.Any(x => x.Location == "Game root" && !x.IsBackup), "root copy was not classified");
    Assert(copies.Any(x => x.Location == "Game subfolder" && !x.IsBackup), "game subfolder copy was not classified");
    Assert(copies.Any(x => x.Location == "Backup copy" && x.IsBackup), "MFG_Backup copy was not classified");
}

static void BackupFilteringAndToggle()
{
    var (fs, exe, root) = Fixture();
    var ordinary = Path.Combine(root, "SP");
    var backup = Path.Combine(root, "Backup_2026");
    var nearMatch = Path.Combine(root, "BackupTools");
    fs.AddDirectory(ordinary); fs.AddDirectory(backup); fs.AddDirectory(nearMatch);
    fs.AddFile(Path.Combine(root, "sl.common.dll"), "root");
    fs.AddFile(Path.Combine(ordinary, "sl.common.dll"), "sp");
    fs.AddFile(Path.Combine(backup, "sl.common.dll"), "backup");
    fs.AddFile(Path.Combine(nearMatch, "sl.common.dll"), "regular");

    var result = new NvidiaComponentScanner(fs).Scan(exe);
    Assert(result.Entries.Count(x => x.Name == "sl.common.dll" && x.Status == "Found") == 4, "same-named files in normal folders must not be collapsed");
    var enumerationsAfterScan = fs.EnumerationCalls;
    var defaultDisplay = NvidiaComponentDisplay.Create(result, showBackups: false);
    Assert(!defaultDisplay.ShowingBackups && defaultDisplay.GameFileCount == 3 && defaultDisplay.BackupFileCount == 1, "default display counts are incorrect");
    Assert(defaultDisplay.VisibleFileCount == 3 && defaultDisplay.TotalFileCount == 4, "default visible/total counts are incorrect");
    Assert(defaultDisplay.VisibleEntries.Where(x => x.Status == "Found").All(x => !x.IsBackup), "backup files should be hidden by default");
    Assert(defaultDisplay.VisibleEntries.Count(x => x.Name == "sl.common.dll" && x.Status == "Found") == 3, "normal root and subfolder copies should remain visible");
    Assert(defaultDisplay.Summary == "3 game files · 1 backup copy hidden", "hidden-copy summary is misleading or inaccurate");

    var shown = NvidiaComponentDisplay.Create(result, showBackups: true);
    Assert(shown.VisibleFileCount == 4 && shown.TotalFileCount == 4 && shown.VisibleEntries.Count(x => x.Status == "Found") == 4, "enabling the checkbox should show all backup entries");
    Assert(shown.Summary == "3 game files · 1 backup copy shown · 4 files visible", "shown-copy summary has wrong counts");
    var hiddenAgain = NvidiaComponentDisplay.Create(result, showBackups: false);
    Assert(hiddenAgain.VisibleFileCount == 3 && hiddenAgain.VisibleEntries.All(x => !x.IsBackup), "disabling the checkbox should hide backups again");
    Assert(fs.EnumerationCalls == enumerationsAfterScan, "changing the display filter must not rescan the game tree");
}

static void BackupPathClassification()
{
    var (fs, exe, root) = Fixture();
    var folders = new[] { "MFG_backup_2026", "BACKUP", "bAcKuPs", "Backup_daily", "backup-nightly" };
    foreach (var folder in folders)
    {
        var path = Path.Combine(root, folder);
        fs.AddDirectory(path);
        fs.AddFile(Path.Combine(path, "sl.reflex.dll"), folder);
    }
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    var copies = result.Entries.Where(x => x.Name == "sl.reflex.dll" && x.Status == "Found").ToArray();
    Assert(copies.Length == folders.Length && copies.All(x => x.Location == "Backup copy" && x.IsBackup), "recognized backup folder patterns must be case-insensitive and path-based");

    var parentRoot = Path.Combine(Path.GetTempPath(), "mFg_BaCkUp_parent", Guid.NewGuid().ToString("N"), "Game");
    var parentExe = Path.Combine(parentRoot, "Game.exe");
    var parentFs = new FakeFileSystem();
    parentFs.AddDirectory(parentRoot); parentFs.AddFile(parentExe, null); parentFs.AddFile(Path.Combine(parentRoot, "sl.common.dll"), "nested-root");
    var parentResult = new NvidiaComponentScanner(parentFs).Scan(parentExe);
    Assert(parentResult.Entries.Any(x => x.Name == "sl.common.dll" && x.IsBackup), "classification should inspect the full absolute path, including backup ancestors");
}

static void InstallationRootClassification()
{
    var root = Path.Combine(Path.GetTempPath(), "RenoDXScannerChecks", Guid.NewGuid().ToString("N"));
    var exeDirectory = Path.Combine(root, "bin", "Win64");
    var executable = Path.Combine(exeDirectory, "Game.exe");
    var subfolder = Path.Combine(root, "SP");
    var fs = new FakeFileSystem();
    fs.AddDirectory(exeDirectory);
    fs.AddDirectory(subfolder);
    fs.AddFile(executable, null);
    fs.AddFile(Path.Combine(root, "sl.common.dll"), "root-version");
    fs.AddFile(Path.Combine(exeDirectory, "sl.reflex.dll"), "exe-folder-version");
    fs.AddFile(Path.Combine(subfolder, "sl.common.dll"), "sp-version");
    var result = new NvidiaComponentScanner(fs).Scan(executable, root);
    Assert(result.RootDirectory == root, "installation root should be used as the scan root");
    Assert(result.Entries.Any(x => x.Name == "sl.common.dll" && x.Location == "Game root"), "DLL in installation root was not found/classified");
    Assert(result.Entries.Any(x => x.Name == "sl.reflex.dll" && x.Location == "Game subfolder"), "DLL beside nested EXE should be a game subfolder copy");
    Assert(result.Entries.Any(x => x.Name == "sl.common.dll" && x.Location == "Game subfolder" && x.Path.StartsWith(subfolder, StringComparison.OrdinalIgnoreCase)), "SP copy was not found/classified");
}
static void VersionFailure()
{
    var (fs, exe, root) = Fixture();
    var dll = Path.Combine(root, "sl.reflex.dll");
    fs.AddFile(dll, null, failVersion: true);
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    Assert(result.Entries.Any(x => x.Name == "sl.reflex.dll" && x.Status == "Found" && x.Version == "Unknown version"), "metadata failure should preserve found file");
    Assert(result.Warnings.Count > 0, "metadata failure should be surfaced as warning");
}

static void AccessFailure()
{
    var (fs, exe, root) = Fixture();
    var locked = Path.Combine(root, "locked");
    fs.AddDirectory(locked);
    fs.DenyDirectoryListing(locked);
    fs.AddFile(Path.Combine(root, "sl.pcl.dll"), "2.0");
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    Assert(result.Entries.Any(x => x.Name == "sl.pcl.dll" && x.Status == "Found"), "accessible root component should remain visible");
    Assert(result.Warnings.Any(x => x.Contains(locked, StringComparison.OrdinalIgnoreCase)), "inaccessible folder should warn with its path");
}

static void MissingExe()
{
    var (fs, exe, _) = Fixture();
    fs.Files.Remove(exe);
    var result = new NvidiaComponentScanner(fs).Scan(exe);
    Assert(result.Error is not null && result.Entries.Count == 0, "missing EXE should stop scan cleanly");
}

static void StreamlineArchiveFilter()
{
    Assert(StreamlineReleaseFilter.IsSdkArchiveName("streamline-sdk-v2.14.16.zip"), "expected SDK archive not accepted");
    Assert(StreamlineReleaseFilter.IsSdkArchiveName("STREAMLINE-SDK-V2.0.ZIP"), "case-insensitive SDK archive not accepted");
    Assert(!StreamlineReleaseFilter.IsSdkArchiveName("Source code.zip"), "source archive must be excluded");
    Assert(!StreamlineReleaseFilter.IsSdkArchiveName("Source code.tar.gz"), "source tarball must be excluded");
    Assert(!StreamlineReleaseFilter.IsSdkArchiveName("other-sdk-v1.zip"), "unrelated archive must be excluded");
    Assert(!StreamlineReleaseFilter.IsSdkArchiveName("streamline-sdk-v2.14.16-aarch64.zip"), "non-x64 SDK architecture must not be offered by the x64 Windows app");
    Assert(!StreamlineReleaseFilter.IsSdkArchiveName("streamline-sdk-v2.14.16-arm64ec.zip"), "ARM64EC SDK must not be offered by the x64 Windows app");
}

static void MissingDigest() => Assert(StreamlineDigestVerifier.Describe(null).Contains("not provided", StringComparison.OrdinalIgnoreCase), "missing digest state was not communicated");

static void DigestChecks()
{
    var dir = Temp("digest"); Directory.CreateDirectory(dir); var file = Path.Combine(dir, "data.bin"); File.WriteAllText(file, "test payload");
    var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test payload"))).ToLowerInvariant();
    Assert(StreamlineDigestVerifier.MatchesAsync(file, expected).GetAwaiter().GetResult(), "correct SHA-256 was rejected");
    Assert(!StreamlineDigestVerifier.MatchesAsync(file, new string('0', 64)).GetAwaiter().GetResult(), "incorrect SHA-256 was accepted");
}

static void ExistingLocalArchiveValidation()
{
    var root = Temp("local-sdk-valid");
    var payload = Encoding.UTF8.GetBytes("verified SDK fixture");
    var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var asset = new StreamlineSdkAsset("streamline-sdk-v2.14.16.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip", payload.Length, digest, "v2.14.16", "2.14.16", null, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16");
    var path = Path.Combine(root, "v2.14.16", asset.Name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, payload);
    var result = new StreamlineLocalArchiveValidator(root).CheckAsync(asset).GetAwaiter().GetResult();
    Assert(result.IsValid && result.FilePath == path, "valid existing archive was not recognized");
}

static void LocalArchiveRejectsWrongDigest()
{
    var root = Temp("local-sdk-bad-digest");
    var payload = Encoding.UTF8.GetBytes("wrong bytes");
    var asset = new StreamlineSdkAsset("streamline-sdk-v2.14.16.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip", payload.Length, new string('0', 64), "v2.14.16", "2.14.16", null, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16");
    var path = Path.Combine(root, "v2.14.16", asset.Name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, payload);
    var result = new StreamlineLocalArchiveValidator(root).CheckAsync(asset).GetAwaiter().GetResult();
    Assert(!result.IsValid && result.Message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase), "wrong digest was accepted");
}

static void LocalArchiveMissing()
{
    var root = Temp("local-sdk-missing");
    var payload = Encoding.UTF8.GetBytes("expected bytes");
    var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var asset = new StreamlineSdkAsset("streamline-sdk-v2.14.16.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip", payload.Length, digest, "v2.14.16", "2.14.16", null, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16");
    var result = new StreamlineLocalArchiveValidator(root).CheckAsync(asset).GetAwaiter().GetResult();
    Assert(!result.IsValid && result.Message.Contains("Download", StringComparison.OrdinalIgnoreCase), "missing archive was not reported");
}

static void LocalArchiveReleaseSwitch()
{
    var root = Temp("local-sdk-switch");
    var firstBytes = Encoding.UTF8.GetBytes("release one"); var secondBytes = Encoding.UTF8.GetBytes("release two");
    var first = new StreamlineSdkAsset("streamline-sdk-v2.14.16.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip", firstBytes.Length, Convert.ToHexString(SHA256.HashData(firstBytes)).ToLowerInvariant(), "v2.14.16", "2.14.16", null, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16");
    var second = new StreamlineSdkAsset("streamline-sdk-v2.15.0.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.15.0/streamline-sdk-v2.15.0.zip", secondBytes.Length, Convert.ToHexString(SHA256.HashData(secondBytes)).ToLowerInvariant(), "v2.15.0", "2.15.0", null, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.15.0");
    foreach (var pair in new[] { (first, firstBytes), (second, secondBytes) })
    {
        var path = Path.Combine(root, pair.Item1.ReleaseTag, pair.Item1.Name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, pair.Item2);
    }
    var validator = new StreamlineLocalArchiveValidator(root);
    var selectedFirst = validator.CheckAsync(first).GetAwaiter().GetResult();
    var selectedSecond = validator.CheckAsync(second).GetAwaiter().GetResult();
    var backToFirst = validator.CheckAsync(first).GetAwaiter().GetResult();
    Assert(selectedFirst.IsValid && selectedSecond.IsValid && backToFirst.FilePath == selectedFirst.FilePath && selectedSecond.FilePath != selectedFirst.FilePath, "release switching reused stale selection state");
}

static void TransactionReplaceRestore()
{
    var (root, store, service) = TransactionFixture("replace-restore");
    var target = Path.Combine(root, "bin", "sl.common.dll"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var original = Encoding.UTF8.GetBytes("original exact bytes"); var replacement = Encoding.UTF8.GetBytes("replacement bytes"); File.WriteAllBytes(target, original);
    var transaction = service.ExecuteAsync(root, [new("bin/sl.common.dll", replacement)]).GetAwaiter().GetResult();
    Assert(File.ReadAllBytes(target).SequenceEqual(replacement), "transaction did not apply fixture replacement");
    using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(transaction.TransactionDirectory, "manifest.json"))))
    {
        var entry = manifest.RootElement.GetProperty("Entries")[0];
        Assert(manifest.RootElement.GetProperty("FormatVersion").GetInt32() == 2 && entry.GetProperty("OriginalSha256").GetString() == Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(), "manifest omitted format/hash metadata");
        Assert(entry.GetProperty("BackupCreatedUtc").ValueKind == JsonValueKind.String, "manifest omitted backup timestamp");
    }
    var restored = service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult();
    Assert(restored.Status == "RolledBack" && File.ReadAllBytes(target).SequenceEqual(original), "original bytes were not restored exactly");
    service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllBytes(target).SequenceEqual(original), "repeated restore changed the already-restored target");
    Assert(Directory.EnumerateDirectories(store).Count() == 1, "transaction backup directory was unexpectedly removed");
}

static void TransactionNewFileRollback()
{
    var (root, _, service) = TransactionFixture("new-file");
    var transaction = service.ExecuteAsync(root, [new("sub/new.dll", Encoding.UTF8.GetBytes("new payload"))]).GetAwaiter().GetResult();
    var target = Path.Combine(root, "sub", "new.dll"); Assert(File.Exists(target), "new transaction file was not created");
    service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult();
    Assert(!File.Exists(target), "unchanged transaction-created file was not removed by rollback");
}

static void TransactionRejectsChangedTarget()
{
    var (root, _, service) = TransactionFixture("external-change");
    var target = Path.Combine(root, "sl.reflex.dll"); var original = Encoding.UTF8.GetBytes("original"); File.WriteAllBytes(target, original);
    var transaction = service.ExecuteAsync(root, [new("sl.reflex.dll", Encoding.UTF8.GetBytes("transaction version"))]).GetAwaiter().GetResult();
    File.WriteAllText(target, "third-party update");
    ExpectInvalid(() => service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult());
    Assert(File.ReadAllText(target) == "third-party update", "restore overwrote an external modification");
}

static void TransactionRejectsManifestTampering()
{
    var (root, _, service) = TransactionFixture("manifest-tamper");
    var target = Path.Combine(root, "item.dll"); File.WriteAllText(target, "before");
    var transaction = service.ExecuteAsync(root, [new("item.dll", Encoding.UTF8.GetBytes("installed"))]).GetAwaiter().GetResult();
    var manifestPath = Path.Combine(transaction.TransactionDirectory, "manifest.json");
    var pristine = File.ReadAllText(manifestPath);
    File.WriteAllText(manifestPath, "{ corrupted json");
    ExpectInvalid(() => service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult());
    Assert(File.ReadAllText(target) == "installed", "corrupt manifest changed target");
    File.WriteAllText(manifestPath, pristine.Replace("item.dll", "../outside.dll", StringComparison.Ordinal));
    ExpectInvalid(() => service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult());
    Assert(File.ReadAllText(target) == "installed", "traversal-tampered manifest changed target");

    foreach (var mutate in new Action<JsonObject>[]
    {
        json => json["Entries"]![0]!["RelativeTargetPath"] = "other.dll",
        json => json["Entries"]![0]!["InstalledSha256"] = new string('0', 64),
        json => { json["Status"] = "RolledBack"; json["Entries"]![0]!["Status"] = "Restored"; },
        json => json["Entries"] = new JsonArray()
    })
    {
        var tampered = JsonNode.Parse(pristine)!.AsObject(); mutate(tampered); File.WriteAllText(manifestPath, tampered.ToJsonString());
        ExpectInvalid(() => service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult());
        Assert(File.ReadAllText(target) == "installed", "tampered manifest metadata changed target");
    }
}

static void TransactionRejectsTamperedSnapshot()
{
    var (root, _, service) = TransactionFixture("snapshot-tamper");
    var first = Path.Combine(root, "first.dll"); var second = Path.Combine(root, "second.dll");
    File.WriteAllText(first, "old first"); File.WriteAllText(second, "old second");
    var transaction = service.ExecuteAsync(root, [new("first.dll", Encoding.UTF8.GetBytes("new first")), new("second.dll", Encoding.UTF8.GetBytes("new second"))]).GetAwaiter().GetResult();
    File.WriteAllText(Path.Combine(transaction.TransactionDirectory, "originals", "0000.bin"), "tampered backup");
    ExpectInvalid(() => service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult());
    Assert(File.ReadAllText(first) == "new first" && File.ReadAllText(second) == "new second", "restore changed targets before verifying all backups");
}

static void TransactionRejectsMissingKey()
{
    var (root, store, service) = TransactionFixture("missing-key");
    var target = Path.Combine(root, "item.dll"); File.WriteAllText(target, "original");
    var transaction = service.ExecuteAsync(root, [new("item.dll", Encoding.UTF8.GetBytes("installed"))]).GetAwaiter().GetResult();
    File.Delete(Path.Combine(store, ".manifest-key.dpapi"));
    ExpectInvalid(() => service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult());
    Assert(File.ReadAllText(target) == "installed", "restore changed a target without authenticating its manifest");
}

static void TransactionRejectsUnsafePaths()
{
    var (root, _, service) = TransactionFixture("unsafe-paths");
    ExpectInvalid(() => service.ExecuteAsync(root, [new("../outside.dll", Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    ExpectInvalid(() => service.ExecuteAsync(root, [new(Path.Combine(Path.GetTempPath(), "outside.dll"), Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    ExpectInvalid(() => service.ExecuteAsync(root, [new("alias.dll.", Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    ExpectInvalid(() => service.ExecuteAsync(root, [new("CON.txt", Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    ExpectInvalid(() => service.ExecuteAsync(root, [new("same.dll", [1]), new("SAME.DLL", [2])]).GetAwaiter().GetResult());
    Assert(!File.Exists(Path.Combine(Directory.GetParent(root)!.FullName, "outside.dll")), "unsafe path wrote outside test root");
}

static void TransactionRejectsReparsePoints()
{
    var (root, store, service) = TransactionFixture("reparse");
    var outside = Path.Combine(Path.GetDirectoryName(root)!, "reparse-outside"); Directory.CreateDirectory(outside);
    var link = Path.Combine(root, "linked");
    var rootAlias = Path.Combine(Path.GetDirectoryName(root)!, "root-alias");
    var storeAlias = Path.Combine(Path.GetDirectoryName(root)!, "store-alias");
    try
    {
        Directory.CreateSymbolicLink(link, outside);
        Directory.CreateSymbolicLink(rootAlias, root);
        Directory.CreateSymbolicLink(storeAlias, store);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
    {
        foreach (var (linkPath, targetPath) in new[] { (link, outside), (rootAlias, root), (storeAlias, store) })
        {
            if (Directory.Exists(linkPath)) continue;
            try
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath } });
                process?.WaitForExit();
            }
            catch { }
        }
        if (!Directory.Exists(link) || !Directory.Exists(rootAlias) || !Directory.Exists(storeAlias))
        {
            throw new SkipCheckException(ex.GetType().Name + " — " + ex.Message);
        }
    }
    ExpectInvalid(() => service.ExecuteAsync(root, [new("linked/escape.dll", Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    ExpectInvalid(() => service.ExecuteAsync(rootAlias, [new("inside.dll", Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    var linkedStoreService = new BackupRestoreService(storeAlias);
    ExpectInvalid(() => linkedStoreService.ExecuteAsync(root, [new("inside.dll", Encoding.UTF8.GetBytes("x"))]).GetAwaiter().GetResult());
    Assert(!File.Exists(Path.Combine(outside, "escape.dll")), "reparse-point path wrote outside test root");
}

static void TransactionRejectsSymbolicLinkOnly()
{
    var (root,_,service)=TransactionFixture("symlink-only");var outside=Path.Combine(Path.GetDirectoryName(root)!,"outside");Directory.CreateDirectory(outside);
    var link=Path.Combine(root,"symbolic");
    try {Directory.CreateSymbolicLink(link,outside);}
    catch(Exception ex) when(ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException){throw new SkipCheckException("Symbolic link creation unavailable: "+ex.Message);}
    ExpectInvalid(()=>service.ExecuteAsync(root,[new("symbolic/escape.dll",Encoding.UTF8.GetBytes("test"))]).GetAwaiter().GetResult());
    Assert(!File.Exists(Path.Combine(outside,"escape.dll")),"Symlink escaped the test root");
}

static void TransactionRollsBackPartialFailure()
{
    var (root, _, service) = TransactionFixture("partial-failure");
    var first = Path.Combine(root, "first.dll"); var second = Path.Combine(root, "second.dll");
    File.WriteAllText(first, "original one"); File.WriteAllText(second, "original two");
    using var blocker = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read);
    BackupTransactionException? failure = null;
    try { service.ExecuteAsync(root, [new("first.dll", Encoding.UTF8.GetBytes("new one")), new("second.dll", Encoding.UTF8.GetBytes("new two"))]).GetAwaiter().GetResult(); }
    catch (BackupTransactionException ex) { failure = ex; }
    Assert(failure is not null && failure.Message.Contains("rolled back", StringComparison.OrdinalIgnoreCase), "mid-transaction failure did not report successful rollback");
    Assert(File.ReadAllText(first) == "original one" && File.ReadAllText(second) == "original two", "partial transaction did not restore previous files");
    service.RestoreAsync(root, failure!.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllText(first) == "original one" && File.ReadAllText(second) == "original two", "recovery retry after rollback changed game state");
}

static void TransactionRejectsStartConflicts()
{
    var (root, _, service) = TransactionFixture("start-conflicts");
    var existing = Path.Combine(root, "existing.dll"); File.WriteAllText(existing, "original");
    BackupTransactionException? changed = null;
    try
    {
        service.ExecuteAsync(root, [new("existing.dll", Encoding.UTF8.GetBytes("replacement"))], _ => File.WriteAllText(existing, "changed after start"), null).GetAwaiter().GetResult();
    }
    catch (BackupTransactionException ex) { changed = ex; }
    Assert(changed is not null && File.ReadAllText(existing) == "changed after start", "file modified after transaction start was overwritten");

    var created = Path.Combine(root, "created-by-other.dll");
    BackupTransactionException? appeared = null;
    try
    {
        service.ExecuteAsync(root, [new("created-by-other.dll", Encoding.UTF8.GetBytes("transaction"))], _ => File.WriteAllText(created, "external file"), null).GetAwaiter().GetResult();
    }
    catch (BackupTransactionException ex) { appeared = ex; }
    Assert(appeared is not null && File.ReadAllText(created) == "external file", "new file appearing after transaction start was overwritten");

    File.WriteAllText(existing, "original again");
    BackupTransactionException? disappeared = null;
    try
    {
        service.ExecuteAsync(root, [new("existing.dll", Encoding.UTF8.GetBytes("replacement"))], _ => File.Delete(existing), null).GetAwaiter().GetResult();
    }
    catch (BackupTransactionException ex) { disappeared = ex; }
    Assert(disappeared is not null && !File.Exists(existing), "file removed after transaction start was recreated over the external change");
}

static void TransactionRollbackPreservesExternalChange()
{
    var (root, _, service) = TransactionFixture("rollback-external-change");
    var target = Path.Combine(root, "changed.dll"); File.WriteAllText(target, "original");
    BackupTransactionException? failure = null;
    try
    {
        service.ExecuteAsync(root, [new("changed.dll", Encoding.UTF8.GetBytes("installed"))], null, _ =>
        {
            File.WriteAllText(target, "external update");
            throw new IOException("simulated later component failure");
        }).GetAwaiter().GetResult();
    }
    catch (BackupTransactionException ex) { failure = ex; }
    Assert(failure is not null && failure.Message.Contains("review", StringComparison.OrdinalIgnoreCase), "rollback did not report the external-change conflict");
    Assert(File.ReadAllText(target) == "external update", "automatic rollback overwrote a post-install external change");
    try { service.RestoreAsync(root, failure!.TransactionId).GetAwaiter().GetResult(); throw new InvalidOperationException("restore should refuse the externally changed file"); }
    catch (IOException ex) when (ex.Message.Contains("changed", StringComparison.OrdinalIgnoreCase)) { }
    Assert(File.ReadAllText(target) == "external update", "recovery after interruption overwrote the external change");
}

static void TransactionRestorePreflight()
{
    var (root, _, service) = TransactionFixture("restore-preflight");
    var first = Path.Combine(root, "first.dll"); var second = Path.Combine(root, "second.dll");
    File.WriteAllText(first, "old first"); File.WriteAllText(second, "old second");
    var transaction = service.ExecuteAsync(root, [new("first.dll", Encoding.UTF8.GetBytes("new first")), new("second.dll", Encoding.UTF8.GetBytes("new second"))]).GetAwaiter().GetResult();
    File.Delete(second);
    try { service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult(); throw new InvalidOperationException("missing target should stop restore"); }
    catch (IOException ex) when (ex.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)) { }
    Assert(File.ReadAllText(first) == "new first", "restore partially changed files before preflighting every target");
}

static void TransactionCancellationRollback()
{
    var (root, _, service) = TransactionFixture("cancel-rollback");
    var first = Path.Combine(root, "first.dll"); var second = Path.Combine(root, "second.dll");
    File.WriteAllText(first, "old first"); File.WriteAllText(second, "old second");
    using var cancellation = new CancellationTokenSource();
    BackupTransactionException? failure = null;
    try
    {
        service.ExecuteAsync(root,
            [new("first.dll", Encoding.UTF8.GetBytes("new first")), new("second.dll", Encoding.UTF8.GetBytes("new second"))],
            null,
            index => { if (index == 0) cancellation.Cancel(); }, cancellation.Token).GetAwaiter().GetResult();
    }
    catch (BackupTransactionException ex) { failure = ex; }
    Assert(failure is not null && File.ReadAllText(first) == "old first" && File.ReadAllText(second) == "old second", "cancellation left a partial transaction installed");
}

static void TransactionCancelledBeforeStart()
{
    var (root, _, service) = TransactionFixture("cancel-before-start");
    var target = Path.Combine(root, "untouched.dll"); File.WriteAllText(target, "original");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { service.ExecuteAsync(root, [new("untouched.dll", Encoding.UTF8.GetBytes("new"))], cancellation.Token).GetAwaiter().GetResult(); throw new InvalidOperationException("pre-cancelled transaction unexpectedly succeeded"); }
    catch (OperationCanceledException) { }
    Assert(File.ReadAllText(target) == "original", "pre-cancelled transaction changed the target");
}

static void TransactionInterruptedRecovery()
{
    var (root, store, service) = TransactionFixture("interrupted-recovery");
    var target = Path.Combine(root, "component.dll"); File.WriteAllText(target, "original");
    var transaction = service.ExecuteAsync(root, [new("component.dll", Encoding.UTF8.GetBytes("installed"))]).GetAwaiter().GetResult();
    // Re-sign a fixture manifest with its test-local DPAPI key to represent a process exit after write, before final status persistence.
    var path = Path.Combine(transaction.TransactionDirectory, "manifest.json");
    var manifestType = typeof(BackupRestoreService).GetNestedType("TransactionManifest", System.Reflection.BindingFlags.NonPublic)!;
    var options = (JsonSerializerOptions)typeof(BackupRestoreService).GetField("JsonOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), manifestType, options)!;
    manifestType.GetProperty("Status")!.SetValue(manifest, "InProgress");
    var entries = (System.Collections.IList)manifestType.GetProperty("Entries")!.GetValue(manifest)!;
    entries[0]!.GetType().GetProperty("Status")!.SetValue(entries[0], "Applying");
    manifestType.GetProperty("IntegrityHmacSha256")!.SetValue(manifest, null);
    var unprotect = typeof(BackupRestoreService).GetMethod("DpapiUnprotect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var key = (byte[])unprotect.Invoke(null, [File.ReadAllBytes(Path.Combine(store, ".manifest-key.dpapi"))])!;
    var computeHmac = typeof(BackupRestoreService).GetMethod("ComputeManifestHmac", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var signature = (string)computeHmac.Invoke(null, [manifest, key])!;
    manifestType.GetProperty("IntegrityHmacSha256")!.SetValue(manifest, signature);
    File.WriteAllText(path, JsonSerializer.Serialize(manifest, manifestType, options));
    var preview = service.PreviewRestore(root, transaction.TransactionId);
    Assert(preview.ManifestVerified && preview.CanRestore, "an interrupted in-progress transaction did not produce a verified recovery preview");
    service.RestoreAsync(root, transaction.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllText(target) == "original", "interrupted transaction did not restore its original bytes");
}

static void TransactionSuccessiveVersions()
{
    var (root, _, service) = TransactionFixture("successive-versions");
    var target = Path.Combine(root, "versioned.dll"); File.WriteAllText(target, "initial");
    var first = service.ExecuteAsync(root, [new("versioned.dll", Encoding.UTF8.GetBytes("version one"))]).GetAwaiter().GetResult();
    var second = service.ExecuteAsync(root, [new("versioned.dll", Encoding.UTF8.GetBytes("version two"))]).GetAwaiter().GetResult();
    service.RestoreAsync(root, second.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllText(target) == "version one", "second transaction did not restore the first transaction's installed version");
    service.RestoreAsync(root, first.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllText(target) == "initial", "first transaction did not restore the original version");
}

static void TransactionPreservesBackups()
{
    var (root, store, service) = TransactionFixture("preserve-backups");
    var target = Path.Combine(root, "persist.dll"); File.WriteAllText(target, "original");
    var first = service.ExecuteAsync(root, [new("persist.dll", Encoding.UTF8.GetBytes("version one"))]).GetAwaiter().GetResult();
    var second = service.ExecuteAsync(root, [new("persist.dll", Encoding.UTF8.GetBytes("version two"))]).GetAwaiter().GetResult();
    Assert(first.TransactionId != second.TransactionId && Directory.EnumerateDirectories(store).Count() == 2, "transactions reused a backup directory");
    Assert(File.ReadAllText(Path.Combine(first.TransactionDirectory, "originals", "0000.bin")) == "original", "first backup was overwritten");
    Assert(File.ReadAllText(Path.Combine(second.TransactionDirectory, "originals", "0000.bin")) == "version one", "second transaction did not preserve its own original");
}

static void StreamlineEvidenceCapture()
{
    var fixture = StreamlineInstallerFixture("evidence");
    var evidence = fixture.Evidence;
    Assert(evidence.Asset.ReleaseTag == "v2.14.16" && evidence.Files.Count == 1, "release metadata or staged DLL list was not recorded");
    Assert(evidence.Files[0].Sha256 == Convert.ToHexString(SHA256.HashData(fixture.ComponentBytes)).ToLowerInvariant(), "staged DLL hash was not captured");
    Assert(evidence.Files[0].RelativePath == "bin/x64/sl.common.dll", "evidence contains an unexpected staged path");
}

static void StreamlineNoCompatibilityGrant()
{
    var fixture = StreamlineInstallerFixture("no-grant");
    var installer = new StreamlineComponentInstaller(fixture.Downloads, fixture.Staging, new BackupRestoreService(fixture.Transactions));
    var plan = installer.CreatePlanAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sl.common.dll" }).GetAwaiter().GetResult();
    Assert(plan.Entries.Count == 1 && plan.Entries[0].Action == "Needs review" && !plan.CanInstall, "unknown game compatibility was treated as authorization");
    Assert(!File.Exists(Path.Combine(fixture.GameRoot, "sl.common.dll")), "preview changed the game fixture");
}

static GameConfigurationFingerprint WitcherFingerprint(
    string appId = "292030", string buildId = "25773555", string exeName = "witcher3.exe",
    string version = "5.0.0.1048522 (Build Machine)", string api = "DirectX 12") =>
    new(appId, buildId, exeName, version, api);

static void CompatibilityProfileExactFingerprint()
{
    var catalog = new BuiltInGameCompatibilityProfileCatalog();
    var profile = catalog.FindExact(WitcherFingerprint());
    Assert(profile is not null && profile.ProfileId.Contains("25773555", StringComparison.Ordinal), "exact Witcher Remastered profile was not selected");
    Assert(profile!.EvidenceStatus == CompatibilityEvidenceStatus.PartiallyVerified && profile.IsReadOnlyDiagnostic, "the profile overstated evidence or was not read-only");
    Assert(profile.TechnologySupport["DLSS Multi Frame Generation"] == TechnologySupportStatus.Supported, "native MFG evidence is missing");
    Assert(profile.TechnologySupport["NVIDIA App MFG override for this exact executable"] == TechnologySupportStatus.Unknown, "unverified per-EXE override support was inferred");
}

static void CompatibilityProfileUnknownConfigurations()
{
    var catalog = new BuiltInGameCompatibilityProfileCatalog();
    Assert(catalog.FindExact(WitcherFingerprint(buildId: "older-build")) is null, "older Steam build matched the Remastered profile");
    Assert(catalog.FindExact(WitcherFingerprint(version: "4.04")) is null, "Next-Gen 4.x matched the Remastered profile");
    Assert(catalog.FindExact(WitcherFingerprint(api: "DirectX 11")) is null, "wrong graphics API matched the profile");
    var evaluator = new GameCompatibilityEvaluator(catalog);
    var report = evaluator.Evaluate(new GameConfigurationFingerprint("unknown-app", null, "game.exe", null, null));
    Assert(report.RecommendedAction == CompatibilityAction.NeedsReview && report.BlockedReasons.Count > 0, "unknown app configuration was granted an operation");
}

static void CompatibilityProfileReferenceSnapshot()
{
    var catalog = new BuiltInGameCompatibilityProfileCatalog();
    var profile = catalog.FindExact(WitcherFingerprint())!;
    var root = Path.Combine(Temp("profile-readonly-snapshot"), "game");
    Directory.CreateDirectory(root);
    var entries = profile.Components.Select(item => new NvidiaComponentEntry(item.FileName, "Found", item.ExpectedVersion,
        Path.Combine(root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)), "Game subfolder", false,
        item.ExpectedSizeBytes, item.ExpectedSha256)).ToArray();
    var scan = new NvidiaComponentScanResult(root, entries, Array.Empty<string>(), 1, null);
    var report = new GameCompatibilityEvaluator(catalog).Evaluate(WitcherFingerprint(), scan);
    Assert(report.RecommendedAction == CompatibilityAction.NoChangesNeeded, "matching diagnostic snapshot should recommend no changes");
    Assert(report.Components.Count == 7 && report.Components.All(item => item.State == "Matches local reference snapshot"), "component snapshot comparison was incomplete");
    Assert(profile.Components.All(item => item.AllowedOperations.Count == 0), "diagnostic profile unexpectedly grants install/replace/remove");
    Assert(report.IntegrationCompatibility == IntegrationSupportStatus.PartiallyConfirmed, "file scan was mistaken for runtime integration proof");
}

static void MfgSettingsDefinitionsAndRead()
{
    var root = Temp("mfg-settings-read");
    var folder = Path.Combine(root, "bin", "x64"); Directory.CreateDirectory(folder);
    var config = Path.Combine(folder, "ReShade.ini");
    File.WriteAllText(config, "[RenoDX.MFGUnlock]\nEnabled=0\nRuntimeSelectionMode=2\n[General]\nOther=preserve\n");
    var service = new MfgUnlockSettingsService();
    var snapshot = service.Load(config, root, "1.6.0.0");
    Assert(snapshot.IsSupportedAddonVersion, "supported addon version was not accepted");
    Assert(snapshot.Settings.Count == 6 && snapshot.Settings.All(item => item.Definition.RequiresRestart), "settings list must contain only six confirmed restart-required controls");
    Assert(snapshot.Settings.Single(item => item.Definition.Key == "Enabled").EffectiveValue == "0", "saved setting value was not parsed");
    Assert(snapshot.Settings.Single(item => item.Definition.Key == "TemporalFix").EffectiveValue == "1", "missing key must display documented default");
    Assert(snapshot.Settings.Single(item => item.Definition.Key == "TemporalFix").IsExplicitlySet == false, "default must not be presented as explicit config value");
    Assert(!service.Load(config, root, "1.6.0-beta.1").IsSupportedAddonVersion, "unknown addon version must not be editable");
}

static void MfgSettingsTransactionalUpdate()
{
    var root = Temp("mfg-settings-transaction"); var game = Path.Combine(root, "game"); var store = Path.Combine(root, "transactions");
    var folder = Path.Combine(game, "bin", "x64"); Directory.CreateDirectory(folder); Directory.CreateDirectory(store);
    var config = Path.Combine(folder, "ReShade.ini");
    var originalText = "; keep this\n[General]\n; general comment\nOther=preserve ; inline note\n[RenoDX.MFGUnlock]\n; enabled comment\nEnabled=1 ; keep enabled note\nRuntimeSelectionMode=0\nUnknownAddonKey=keep\n[Other]\nKeep=1\n";
    File.WriteAllText(config, originalText, new UTF8Encoding(false));
    var service = new MfgUnlockSettingsService(); var snapshot = service.Load(config, game, "1.6.0.0");
    var updated = service.CreateUpdatedBytes(snapshot, new Dictionary<string, string> { ["Enabled"] = "0", ["RuntimeSelectionMode"] = "1" });
    var backup = new BackupRestoreService(store);
    var relative = Path.GetRelativePath(game, config);
    var transaction = backup.ExecuteAsync(game, [new BackupTransactionFile(relative, updated, true, snapshot.ContentSha256)]).GetAwaiter().GetResult();
    var changed = File.ReadAllText(config);
    Assert(changed.Contains("; keep this\n[General]\n; general comment\nOther=preserve ; inline note", StringComparison.Ordinal), "unrelated section/comment content changed");
    Assert(changed.Contains("; enabled comment\nEnabled=0 ; keep enabled note\nRuntimeSelectionMode=1\nUnknownAddonKey=keep\n[Other]\nKeep=1\n", StringComparison.Ordinal), "changed setting comments, unknown keys or later section were not preserved");
    backup.RestoreAsync(game, transaction.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllText(config) == originalText, "transaction restore did not recover exact original bytes");
}

static void MfgSettingsRejectInvalidAndDuplicate()
{
    var root = Temp("mfg-settings-invalid"); Directory.CreateDirectory(root);
    var config = Path.Combine(root, "ReShade.ini");
    var service = new MfgUnlockSettingsService();
    File.WriteAllText(config, "[RenoDX.MFGUnlock]\nEnabled=maybe\n");
    var invalid = service.Load(config, root, "1.6.0.0");
    var enabled = invalid.Settings.Single(item => item.Definition.Key == "Enabled");
    Assert(!enabled.IsValid && enabled.ProposedValue is null && enabled.IsExplicitlySet, "invalid saved value must remain distinguishable and non-editable");
    try { service.CreateUpdatedBytes(invalid, new Dictionary<string, string> { ["Enabled"] = "0" }); throw new Exception("invalid setting should not be silently overwritten"); }
    catch (InvalidDataException) { }

    File.WriteAllText(config, "[RenoDX.MFGUnlock]\nEnabled=1\nEnabled=0\n");
    try { service.Load(config, root, "1.6.0.0"); throw new Exception("duplicate setting should be rejected"); }
    catch (InvalidDataException ex) { Assert(ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase), "duplicate-key rejection did not identify the problem"); }
}

static void MfgSettingsRejectStaleAndUnsupported()
{
    var root = Temp("mfg-settings-stale"); Directory.CreateDirectory(root);
    var config = Path.Combine(root, "ReShade.ini"); File.WriteAllText(config, "[RenoDX.MFGUnlock]\nEnabled=1\n");
    var service = new MfgUnlockSettingsService(); var snapshot = service.Load(config, root, "1.6.0.0");
    File.AppendAllText(config, "; changed after preview\n");
    try { service.CreateUpdatedBytes(snapshot, new Dictionary<string, string> { ["Enabled"] = "0" }); throw new Exception("stale config should be rejected"); }
    catch (IOException ex) { Assert(ex.Message.Contains("changed after", StringComparison.OrdinalIgnoreCase), "unexpected stale-config failure"); }
    var unknown = service.Load(config, root, "1.6.0-beta.1");
    try { service.CreateUpdatedBytes(unknown, new Dictionary<string, string> { ["Enabled"] = "0" }); throw new Exception("unsupported version should be rejected"); }
    catch (InvalidOperationException ex) { Assert(ex.Message.Contains("supported only", StringComparison.OrdinalIgnoreCase), "unexpected version-gate failure"); }
}

static void CompatibilityProfileComponentMismatch()
{
    var catalog = new BuiltInGameCompatibilityProfileCatalog();
    var profile = catalog.FindExact(WitcherFingerprint())!;
    var root = Path.Combine(Temp("profile-review-required"), "game");
    Directory.CreateDirectory(root);
    var entries = profile.Components.Select(item => new NvidiaComponentEntry(item.FileName, "Found", item.ExpectedVersion,
        Path.Combine(root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)), "Game subfolder", false,
        item.ExpectedSizeBytes, item.FileName == "sl.common.dll" ? new string('0', 64) : item.ExpectedSha256)).ToArray();
    var scan = new NvidiaComponentScanResult(root, entries, Array.Empty<string>(), 1, null);
    var report = new GameCompatibilityEvaluator(catalog).Evaluate(WitcherFingerprint(), scan);
    Assert(report.RecommendedAction == CompatibilityAction.NeedsReview, "mismatched component hash was treated as safe");
    Assert(report.Components.Single(item => item.FileName == "sl.common.dll").State == "Differs from local reference snapshot", "component hash conflict was not surfaced");
    var withoutDlssg = new NvidiaComponentScanResult(root, entries.Where(item => item.Name != "nvngx_dlssg.dll").ToArray(), Array.Empty<string>(), 1, null);
    Assert(new GameCompatibilityEvaluator(catalog).Evaluate(WitcherFingerprint(), withoutDlssg).RecommendedAction == CompatibilityAction.NeedsReview, "missing reference component was not sent to review");
    var duplicate = entries.Concat([entries.Single(item => item.Name == "sl.common.dll") with { Path = Path.Combine(root, "other", "sl.common.dll") }]).ToArray();
    var duplicateReport = new GameCompatibilityEvaluator(catalog).Evaluate(WitcherFingerprint(), new NvidiaComponentScanResult(root, duplicate, Array.Empty<string>(), 1, null));
    Assert(duplicateReport.Components.Single(item => item.FileName == "sl.common.dll").State == "Multiple game-folder copies need review", "duplicate active-folder candidates were not surfaced");
}

static void ScannerIntegrityMetadata()
{
    var root = Path.Combine(Temp("scanner-integrity"), "game");
    Directory.CreateDirectory(root);
    var exe = Path.Combine(root, "fixture.exe");
    var bytes = Encoding.UTF8.GetBytes("temporary scanner fixture only");
    File.WriteAllBytes(exe, [0x4d, 0x5a]);
    File.WriteAllBytes(Path.Combine(root, "sl.common.dll"), bytes);
    var scan = new NvidiaComponentScanner().Scan(exe, root);
    var entry = scan.Entries.Single(item => item.Name == "sl.common.dll" && item.Status == "Found");
    var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    Assert(entry.SizeBytes == bytes.Length && entry.Sha256 == expectedHash, "scanner did not record actual size and SHA-256");
}

static void ScannerDuplicateGroups()
{
    var root = Path.Combine(Temp("scanner-duplicate-groups"), "game");
    var subfolder = Path.Combine(root, "bin");
    var other = Path.Combine(root, "SP");
    Directory.CreateDirectory(subfolder); Directory.CreateDirectory(other);
    var exe = Path.Combine(root, "fixture.exe"); File.WriteAllBytes(exe, [0x4d, 0x5a]);
    var sharedBytes = Encoding.UTF8.GetBytes("same local fixture");
    File.WriteAllBytes(Path.Combine(root, "sl.common.dll"), sharedBytes);
    File.WriteAllBytes(Path.Combine(subfolder, "sl.common.dll"), sharedBytes);
    File.WriteAllText(Path.Combine(other, "sl.common.dll"), "different fixture version");
    var result = new NvidiaComponentScanner().Scan(exe, root);
    Assert(result.Entries.Count(item => item.Name == "sl.common.dll" && item.Status == "Found") == 3, "scanner hid one or more duplicate component copies");
    Assert(result.PotentialConflicts.Count == 1 && result.PotentialConflicts[0].Contains("divergent version/hash", StringComparison.Ordinal), "scanner did not identify conflicting component metadata");
}

static (AdvancedRuntimeInstaller Service, VerifiedStreamlineStage Stage, string Root, string Exe, BackupRestoreService Backup) AdvancedFixture(string name)
{
    var dir=Temp("advanced-"+name);Directory.CreateDirectory(dir);
    string[] names=["sl.interposer.dll","sl.common.dll","sl.dlss_g.dll","sl.reflex.dll","sl.pcl.dll"];
    var zip=MakeZip(dir,names.Select(n=>("bin/x64/"+n,"updated fixture "+n)).ToArray());
    var payload=File.ReadAllBytes(zip);
    var asset=Asset("streamline-sdk-v2.14.16.zip","https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip",payload.Length)
        with {ReleaseTag="v2.14.16",Sha256=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()};
    var download=new StreamlineDownloadService(Path.Combine(dir,"store"),new HttpClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)}))));
    var archive=download.DownloadAsync(asset).GetAwaiter().GetResult();
    var stage=new StreamlineArchiveStager().StageAsync(archive.FilePath,download.StagingDirectory,asset.ReleaseTag,default,asset.Sha256).GetAwaiter().GetResult();
    var evidence=new StreamlineStagingEvidenceService().CaptureAsync(download.DownloadsDirectory,download.StagingDirectory,archive.FilePath,asset,stage.DirectoryPath).GetAwaiter().GetResult();
    var root=Path.Combine(dir,"game");Directory.CreateDirectory(root);
    var exe=Path.Combine(root,"isolated.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
    foreach(var n in names)File.WriteAllText(Path.Combine(root,n),"original "+n);
    File.WriteAllText(Path.Combine(root,"nvngx_dlssg.dll"),"existing provider fixture");
    var backup=new BackupRestoreService(Path.Combine(dir,"transactions"));
    // Only this test assembly supplies synthetic version metadata; shipping GUI uses Windows file metadata.
    var service=new AdvancedRuntimeInstaller(download.DownloadsDirectory,download.StagingDirectory,backup,
        p=>Path.GetFileName(p)=="nvngx_dlssg.dll"?"310.9.16.0":p.StartsWith(stage.DirectoryPath,StringComparison.OrdinalIgnoreCase)?"2.14.16.0":"2.14.1.0", _=>false);
    return(service,evidence,root,exe,backup);
}
static void AdvancedCycle()
{
    var f=AdvancedFixture("cycle");
    var original=Directory.GetFiles(f.Root).ToDictionary(p=>p,File.ReadAllBytes);
    var plan=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult();
    Assert(plan.CanInstall&&plan.SelectedReplacements==5,"manual preview not ready");
    var first=f.Service.InstallAsync(f.Stage,f.Root,f.Exe,f.Root,true,plan).GetAwaiter().GetResult();
    Assert(first.InstalledFiles.Count==5,"incomplete set");
    foreach(var file in f.Stage.Files) Assert(File.ReadAllBytes(Path.Combine(f.Root,file.FileName)).SequenceEqual(File.ReadAllBytes(Path.Combine(f.Stage.StagingDirectory,file.RelativePath.Replace('/',Path.DirectorySeparatorChar)))),"written bytes differ");
    var secondPlan=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult();
    Assert(!secondPlan.CanInstall&&secondPlan.SelectedReplacements==0&&secondPlan.Entries.All(e=>e.Action=="No change"&&!e.IsSelected),"Identical runtime should be unchanged");
    try{f.Service.InstallAsync(f.Stage,f.Root,f.Exe,f.Root,true,secondPlan).GetAwaiter().GetResult();throw new Exception("No-op Apply should be unavailable");}catch(InvalidOperationException){}
    Assert(f.Backup.ListTransactions(f.Root).Count==1,"No-op created a transaction");
    f.Backup.RestoreAsync(f.Root,first.Transaction.TransactionId).GetAwaiter().GetResult();
    Assert(original.All(x=>File.ReadAllBytes(x.Key).SequenceEqual(x.Value)),"originals not fully restored");
}
static void AdvancedDependencies()
{
    var f=AdvancedFixture("deps");
    File.WriteAllText(Path.Combine(f.Root,"sl.unknown.dll"),"unknown");
    ExpectInvalid(()=>f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
    File.Delete(Path.Combine(f.Root,"sl.unknown.dll"));File.Delete(Path.Combine(f.Root,"sl.pcl.dll"));
    ExpectInvalid(()=>f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
}
static void AdvancedRequirements()
{
    var f=AdvancedFixture("requirements");
    ExpectInvalid(()=>f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,false).GetAwaiter().GetResult());
    var store=Path.GetDirectoryName(Path.GetDirectoryName(f.Stage.ArchivePath))!;
    var running=new AdvancedRuntimeInstaller(store,Path.GetDirectoryName(f.Stage.StagingDirectory)!,f.Backup,_=>"2.14.16.0",_=>true);
    ExpectInvalid(()=>running.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
    File.Delete(Path.Combine(f.Root,"nvngx_dlssg.dll"));
    ExpectInvalid(()=>f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
}
static void AdvancedConflicts()
{
    var f=AdvancedFixture("conflicts");
    var plan=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult();
    File.WriteAllText(Path.Combine(f.Root,"sl.common.dll"),"external");
    try { f.Service.InstallAsync(f.Stage,f.Root,f.Exe,f.Root,true,plan).GetAwaiter().GetResult(); throw new Exception("stale preview accepted"); }
    catch(InvalidOperationException ex) { Assert(ex.Message.Contains("preview changed"),"unexpected conflict"); }
    File.WriteAllText(Path.Combine(f.Stage.StagingDirectory,"bin","x64","sl.common.dll"),"corrupt");
    var changed=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult();
    Assert(!changed.CanInstall,"corrupt source authorized");
}
static string SignedProviderFixture()
{
    var path=Environment.GetEnvironmentVariable("RENODX_TEST_SIGNED_PROVIDER");
    if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)) throw new SkipCheckException("Provide RENODX_TEST_SIGNED_PROVIDER for genuine Windows Authenticode checks; no signature bypass is used.");
    return path;
}
static void ProviderAuthentic()
{
    var verifier=new DlssGProviderVerifier();
    var p=verifier.Verify(SignedProviderFixture());
    Assert(p.Signer=="NVIDIA Corporation" && p.Version=="310.9.16.0" && p.Sha256.Length==64,"wrong verified identity");
    ExpectInvalid(()=>verifier.Revalidate(p with {Sha256=new string('0',64)}));
}
static void ProviderCorrupt()
{
    var source=SignedProviderFixture();var dir=Temp("corrupt-provider");Directory.CreateDirectory(dir);
    var bytes=File.ReadAllBytes(source);bytes[0x500]^=1;
    var path=Path.Combine(dir,"nvngx_dlssg.dll");File.WriteAllBytes(path,bytes);
    ExpectInvalid(()=>new DlssGProviderVerifier().Verify(path));
    File.WriteAllBytes(path,new byte[50]);
    ExpectInvalid(()=>new DlssGProviderVerifier().Verify(path));
}
static void ProviderCombinedCycle()
{
    var provider=new DlssGProviderVerifier().Verify(SignedProviderFixture());
    var f=AdvancedFixture("combined-provider");
    var originals=Directory.GetFiles(f.Root).ToDictionary(p=>p,File.ReadAllBytes);
    var plan=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true,default,provider).GetAwaiter().GetResult();
    Assert(plan.CanInstall && plan.SelectedReplacements==6 && plan.Entries.Any(x=>x.FileName=="nvngx_dlssg.dll"),"provider not included");
    var result=f.Service.InstallAsync(f.Stage,f.Root,f.Exe,f.Root,true,plan,null,default,provider).GetAwaiter().GetResult();
    Assert(result.InstalledFiles.Count==6,"set not atomic/complete");
    Assert(File.ReadAllBytes(Path.Combine(f.Root,"nvngx_dlssg.dll")).SequenceEqual(File.ReadAllBytes(provider.Path)),"provider bytes differ");
    var repeat=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true,default,provider).GetAwaiter().GetResult();
    Assert(!repeat.CanInstall&&repeat.SelectedReplacements==0&&repeat.Entries.All(e=>e.Action=="No change"&&!e.IsSelected),"Identical provider was selected again");
    try{f.Service.InstallAsync(f.Stage,f.Root,f.Exe,f.Root,true,repeat,null,default,provider).GetAwaiter().GetResult();throw new Exception("No-op provider Apply should be unavailable");}catch(InvalidOperationException){}
    Assert(f.Backup.ListTransactions(f.Root).Count==1,"Provider no-op created a transaction");
    f.Backup.RestoreAsync(f.Root,result.Transaction.TransactionId).GetAwaiter().GetResult();
    Assert(originals.All(x=>File.ReadAllBytes(x.Key).SequenceEqual(x.Value)),"six-file original restore failed");
}
static void ProviderOnlyCycle()
{
    var provider=new DlssGProviderVerifier().Verify(SignedProviderFixture());var f=AdvancedFixture("provider-only");
    foreach(var file in f.Stage.Files)File.Copy(Path.Combine(f.Stage.StagingDirectory,file.RelativePath.Replace('/',Path.DirectorySeparatorChar)),Path.Combine(f.Root,file.FileName),true);
    var path=Path.Combine(f.Root,"nvngx_dlssg.dll");var original=File.ReadAllBytes(path);var plan=f.Service.PreviewAsync(f.Stage,f.Root,f.Exe,f.Root,true,default,provider).GetAwaiter().GetResult();
    Assert(plan.CanInstall&&plan.SelectedReplacements==1&&plan.Entries.Count(e=>e.Action=="No change")==5,"Provider-only plan rewrites identical core");
    var tx=f.Service.InstallAsync(f.Stage,f.Root,f.Exe,f.Root,true,plan,null,default,provider).GetAwaiter().GetResult();Assert(tx.InstalledFiles.Count==1,"Provider-only transaction scope differs");f.Backup.RestoreAsync(f.Root,tx.Transaction.TransactionId).GetAwaiter().GetResult();Assert(File.ReadAllBytes(path).SequenceEqual(original),"Provider original lost");
}
static void ProviderMismatch()
{
    var provider=new DlssGProviderVerifier().Verify(SignedProviderFixture());
    var f=AdvancedFixture("provider-mismatch");
    var source=f.Stage with {Asset=f.Stage.Asset with {ReleaseTag="v2.14.1"}};
    ExpectInvalid(()=>f.Service.PreviewAsync(source,f.Root,f.Exe,f.Root,true,default,provider).GetAwaiter().GetResult());
}
static void OfflineComponentPipeline()
{
    var dir = Temp("offline-pipeline"); Directory.CreateDirectory(dir);
    var zip = MakeZip(dir, ("bin/x64/sl.common.dll", "isolated runtime test bytes"), ("LICENSE.txt", "fixture license"));
    var payload = File.ReadAllBytes(zip);
    var asset = Asset("streamline-sdk-v1.zip","https://github.com/NVIDIA-RTX/Streamline/releases/download/v1/streamline-sdk-v1.zip",payload.Length)
        with { Sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant() };
    var requests = 0;
    var http = new HttpClient(new FakeHandler((_,_) => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){ Content = new ByteArrayContent(payload) }); }));
    var download = new StreamlineDownloadService(Path.Combine(dir,"store"),http);
    var downloaded = download.DownloadAsync(asset).GetAwaiter().GetResult();
    var stage = new StreamlineArchiveStager().StageAsync(downloaded.FilePath,download.StagingDirectory,"v1",default,asset.Sha256).GetAwaiter().GetResult();
    var evidence = new StreamlineStagingEvidenceService().CaptureAsync(download.DownloadsDirectory,download.StagingDirectory,downloaded.FilePath,asset,stage.DirectoryPath).GetAwaiter().GetResult();
    var recovered=new StreamlineStagingEvidenceService().RecoverLatestAsync(download.DownloadsDirectory,download.StagingDirectory,downloaded.FilePath,asset).GetAwaiter().GetResult();
    Assert(recovered?.StagingDirectory==evidence.StagingDirectory,"Verified local staging was not recovered");
    var game = Path.Combine(dir,"fixture-game");Directory.CreateDirectory(game);
    var exe = Path.Combine(game,"fixture.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
    var target = Path.Combine(game,"sl.common.dll");File.WriteAllText(target,"original fixture");
    var before = File.ReadAllBytes(target);
    var service = new BackupRestoreService(Path.Combine(dir,"transactions"));
    var installer = new StreamlineComponentInstaller(download.DownloadsDirectory,download.StagingDirectory,service,
        new TestStreamlineCompatibilityCatalog(new("12345","sl.common.dll",".",true,true,"isolated fixture only; not a game profile")));
    var selected = new HashSet<string>{"sl.common.dll"};
    var scan = new NvidiaComponentScanner().Scan(exe,game);
    Assert(scan.FoundCopies == 1,"initial scan failed");
    var plan = installer.CreatePlanAsync(evidence,"12345",game,game,selected).GetAwaiter().GetResult();
    var first = installer.InstallAsync(evidence,"12345",game,game,selected,plan).GetAwaiter().GetResult();
    var after = new NvidiaComponentScanner().Scan(exe,game);
    Assert(after.Entries.Single(x=>x.Name=="sl.common.dll").Sha256 == evidence.Files[0].Sha256,"installed hash differs");
    var secondPlan = installer.CreatePlanAsync(evidence,"12345",game,game,selected).GetAwaiter().GetResult();
    Assert(!secondPlan.CanInstall&&secondPlan.Entries.All(e=>e.Action=="No change"&&!e.IsSelected),"Reinstall should be a no-op");
    try{installer.InstallAsync(evidence,"12345",game,game,selected,secondPlan).GetAwaiter().GetResult();throw new Exception("Identical bytes were reinstalled");}catch(InvalidOperationException){}
    Assert(service.ListTransactions(game).Count==1,"No-op created unnecessary backup");
    service.RestoreAsync(game,first.Transaction.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllBytes(target).SequenceEqual(before),"exact original was not restored");
    download.DownloadAsync(asset).GetAwaiter().GetResult();
    Assert(requests==1,"valid archive was downloaded again");
    var blocked = new StreamlineComponentInstaller(download.DownloadsDirectory,download.StagingDirectory,service)
        .CreatePlanAsync(evidence,"12345",game,game,selected).GetAwaiter().GetResult();
    Assert(!blocked.CanInstall,"production catalog authorized fixture");
}
static void StreamlineInstallNewAndRestore()
{
    var fixture = StreamlineInstallerFixture("install-new");
    var transactions = new BackupRestoreService(fixture.Transactions);
    var installer = new StreamlineComponentInstaller(fixture.Downloads, fixture.Staging, transactions, new TestStreamlineCompatibilityCatalog(new("12345", "sl.common.dll", ".", true, true, "unit-test-only grant")));
    var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sl.common.dll" };
    var plan = installer.CreatePlanAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, selected).GetAwaiter().GetResult();
    Assert(plan.CanInstall && plan.SelectedNewFiles == 1, "reviewed fixture grant did not allow a new-file plan");
    var installed = installer.InstallAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, selected, plan).GetAwaiter().GetResult();
    var target = Path.Combine(fixture.GameRoot, "sl.common.dll");
    Assert(File.ReadAllBytes(target).SequenceEqual(fixture.ComponentBytes), "new staged DLL was not installed exactly");
    var preview = transactions.PreviewRestore(fixture.GameRoot, installed.Transaction.TransactionId);
    Assert(preview.CanRestore && preview.Files.Single().Action == "Remove transaction file", "restore preview did not describe removal of the transaction-created file");
    transactions.RestoreAsync(fixture.GameRoot, installed.Transaction.TransactionId).GetAwaiter().GetResult();
    Assert(!File.Exists(target), "restore did not remove the unchanged transaction-created fixture file");
}

static void StreamlineReplaceAndRestore()
{
    var fixture = StreamlineInstallerFixture("replace");
    var target = Path.Combine(fixture.GameRoot, "sl.common.dll"); var original = Encoding.UTF8.GetBytes("existing game fixture bytes"); File.WriteAllBytes(target, original);
    var transactions = new BackupRestoreService(fixture.Transactions);
    var installer = new StreamlineComponentInstaller(fixture.Downloads, fixture.Staging, transactions, new TestStreamlineCompatibilityCatalog(new("12345", "sl.common.dll", ".", true, true, "unit-test-only grant")));
    var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sl.common.dll" };
    var plan = installer.CreatePlanAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, selected).GetAwaiter().GetResult();
    Assert(plan.CanInstall && plan.SelectedReplacements == 1 && plan.Entries[0].ExistingSha256 is not null, "existing target lacked a hashed replacement plan");
    var installed = installer.InstallAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, selected, plan).GetAwaiter().GetResult();
    var backup = Path.Combine(installed.Transaction.TransactionDirectory, "originals", "0000.bin");
    Assert(File.ReadAllBytes(backup).SequenceEqual(original), "replace operation did not preserve original bytes");
    transactions.RestoreAsync(fixture.GameRoot, installed.Transaction.TransactionId).GetAwaiter().GetResult();
    Assert(File.ReadAllBytes(target).SequenceEqual(original), "restore did not recover exact original bytes");
}

static void StreamlineChangedSourcesBlocked()
{
    var fixture = StreamlineInstallerFixture("source-change");
    var installer = new StreamlineComponentInstaller(fixture.Downloads, fixture.Staging, new BackupRestoreService(fixture.Transactions), new TestStreamlineCompatibilityCatalog(new("12345", "sl.common.dll", ".", true, true, "unit-test-only grant")));
    File.WriteAllText(Path.Combine(fixture.StagingDirectory, "bin", "x64", "sl.common.dll"), "tampered staged source");
    var invalidSourcePlan = installer.CreatePlanAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sl.common.dll" }).GetAwaiter().GetResult();
    Assert(!invalidSourcePlan.CanInstall && invalidSourcePlan.Entries.Single().Action == "Blocked", "tampered staged DLL was not blocked by its saved hash");
    File.WriteAllText(fixture.ArchivePath, "archive changed after verification");
    ExpectInvalid(() => installer.CreatePlanAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sl.common.dll" }).GetAwaiter().GetResult());
}

static void StreamlineTargetChangedAfterPreview()
{
    var fixture = StreamlineInstallerFixture("target-change");
    var installer = new StreamlineComponentInstaller(fixture.Downloads, fixture.Staging, new BackupRestoreService(fixture.Transactions), new TestStreamlineCompatibilityCatalog(new("12345", "sl.common.dll", ".", true, false, "unit-test-only new-file grant")));
    var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sl.common.dll" };
    var plan = installer.CreatePlanAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, selected).GetAwaiter().GetResult();
    Assert(plan.CanInstall, "test fixture should start with a new-file plan");
    var target = Path.Combine(fixture.GameRoot, "sl.common.dll"); File.WriteAllText(target, "file appeared after preview");
    try { installer.InstallAsync(fixture.Evidence, "12345", fixture.GameRoot, fixture.GameRoot, selected, plan).GetAwaiter().GetResult(); throw new InvalidOperationException("stale preview should be rejected"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("preview", StringComparison.OrdinalIgnoreCase)) { }
    Assert(File.ReadAllText(target) == "file appeared after preview", "stale preview overwrote a newly appeared file");
}

static (string Downloads, string Staging, string StagingDirectory, string GameRoot, string Transactions, string ArchivePath, byte[] ComponentBytes, VerifiedStreamlineStage Evidence) StreamlineInstallerFixture(string name)
{
    var root = Temp("component-installer-" + name);
    var downloads = Path.Combine(root, "Downloads"); var staging = Path.Combine(root, "Staging");
    var stageDirectory = Path.Combine(staging, "v2.14.16-test"); var gameRoot = Path.Combine(root, "game-root"); var transactions = Path.Combine(root, "Transactions");
    Directory.CreateDirectory(Path.Combine(downloads, "v2.14.16")); Directory.CreateDirectory(Path.Combine(stageDirectory, "bin", "x64")); Directory.CreateDirectory(gameRoot); Directory.CreateDirectory(transactions);
    var archivePath = Path.Combine(downloads, "v2.14.16", "streamline-sdk-v2.14.16.zip"); byte[] archiveBytes;
    using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
    { using var writer = new StreamWriter(zip.CreateEntry("bin/x64/sl.common.dll").Open(), new UTF8Encoding(false)); writer.Write("production DLL fixture bytes"); }
    archiveBytes = File.ReadAllBytes(archivePath);
    var componentBytes = Encoding.UTF8.GetBytes("production DLL fixture bytes"); File.WriteAllBytes(Path.Combine(stageDirectory, "bin", "x64", "sl.common.dll"), componentBytes);
    var digest = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
    var asset = new StreamlineSdkAsset("streamline-sdk-v2.14.16.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip", archiveBytes.Length, digest, "v2.14.16", "2.14.16", DateTimeOffset.UtcNow, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16");
    var evidence = new StreamlineStagingEvidenceService().CaptureAsync(downloads, staging, archivePath, asset, stageDirectory).GetAwaiter().GetResult();
    return (downloads, staging, stageDirectory, gameRoot, transactions, archivePath, componentBytes, evidence);
}

static (string Root, string Store, BackupRestoreService Service) TransactionFixture(string name)
{
    // cmd/mklink does not handle the >260 character build-output path used by other fixtures.
    var parent = name=="reparse" ? Path.Combine(Path.GetTempPath(),"RdxLinkChecks",Guid.NewGuid().ToString("N")) : Temp("transaction-" + name); var root = Path.Combine(parent, "game-root"); var store = Path.Combine(parent, "transactions");
    Directory.CreateDirectory(root); Directory.CreateDirectory(store);
    return (root, store, new BackupRestoreService(store));
}

static void CorruptZip()
{
    var dir = Temp("badzip"); Directory.CreateDirectory(dir); var zip = Path.Combine(dir, "corrupt.zip"); File.WriteAllText(zip, "not a zip");
    ExpectInvalid(() => new StreamlineArchiveStager().StageAsync(zip, Path.Combine(dir, "stage"), "v1").GetAwaiter().GetResult());
    Assert(!Directory.Exists(Path.Combine(dir, "stage")) || !Directory.EnumerateFileSystemEntries(Path.Combine(dir, "stage")).Any(), "failed extraction left staging content");
}

static void ZipSlip()
{
    var dir = Temp("zipslip"); Directory.CreateDirectory(dir); var zip = MakeZip(dir, ("bin/x64/../../../../outside/sl.interposer.dll", "payload"));
    ExpectInvalid(() => new StreamlineArchiveStager().StageAsync(zip, Path.Combine(dir, "stage"), "v1").GetAwaiter().GetResult());
    Assert(!File.Exists(Path.Combine(dir, "outside", "sl.interposer.dll")), "Zip Slip wrote outside destination");
}

static void DuplicateZipPaths()
{
    var dir = Temp("duplicate"); Directory.CreateDirectory(dir); var zip = MakeZip(dir, ("bin/x64/sl.common.dll", "a"), ("bin/x64/SL.COMMON.dll", "b"));
    ExpectInvalid(() => new StreamlineArchiveStager().StageAsync(zip, Path.Combine(dir, "stage"), "v1").GetAwaiter().GetResult());
    var normalizedDir = Temp("duplicate-normalized"); Directory.CreateDirectory(normalizedDir); var normalizedZip = MakeZip(normalizedDir, ("bin/x64/sl.common.dll", "a"), ("bin//x64/sl.common.dll", "b"));
    ExpectInvalid(() => new StreamlineArchiveStager().StageAsync(normalizedZip, Path.Combine(normalizedDir, "stage"), "v1").GetAwaiter().GetResult());
}

static void DownloadCancellationAndInterruption()
{
    var dir = Temp("download");
    var cancelClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CancellingStream()) })));
    var asset = Asset("streamline-sdk-v1.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v1/streamline-sdk-v1.zip", 64);
    try { new StreamlineDownloadService(dir, cancelClient).DownloadAsync(asset).GetAwaiter().GetResult(); throw new InvalidOperationException("expected cancellation"); }
    catch (OperationCanceledException) { }
    Assert(!Directory.Exists(Path.Combine(dir, "Downloads")) || !Directory.EnumerateFiles(Path.Combine(dir, "Downloads"), "*", SearchOption.AllDirectories).Any(), "cancel left partial files");

    var shortClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("short")) })));
    try { new StreamlineDownloadService(Path.Combine(dir, "short"), shortClient).DownloadAsync(asset).GetAwaiter().GetResult(); throw new InvalidOperationException("expected truncated download failure"); }
    catch (EndOfStreamException) { }
    Assert(!Directory.Exists(Path.Combine(dir, "short", "Downloads")) || !Directory.EnumerateFiles(Path.Combine(dir, "short", "Downloads"), "*", SearchOption.AllDirectories).Any(), "interrupted download left partial files");
}

static void SuccessfulDownloads()
{
    var dir = Temp("download-ok"); var payload = Encoding.UTF8.GetBytes("official test archive bytes");
    var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));
    var asset = Asset("streamline-sdk-v1.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v1/streamline-sdk-v1.zip", payload.Length) with { Sha256 = digest };
    var verified = new StreamlineDownloadService(dir, client).DownloadAsync(asset).GetAwaiter().GetResult();
    Assert(File.Exists(verified.FilePath) && verified.DigestVerified, "successful SHA-256 download was not kept");

    var badDir = Temp("download-bad-digest");
    var badClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));
    try { new StreamlineDownloadService(badDir, badClient).DownloadAsync(asset with { Sha256 = new string('0', 64) }).GetAwaiter().GetResult(); throw new InvalidOperationException("expected digest mismatch failure"); }
    catch (InvalidDataException) { }
    Assert(!Directory.Exists(Path.Combine(badDir, "Downloads")) || !Directory.EnumerateFiles(Path.Combine(badDir, "Downloads"), "*", SearchOption.AllDirectories).Any(), "digest mismatch left a partial archive");

    var noDigestDir = Temp("download-no-digest");
    var noDigestClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));
    var noDigest = new StreamlineDownloadService(noDigestDir, noDigestClient).DownloadAsync(Asset("streamline-sdk-v1.zip", "https://github.com/NVIDIA-RTX/Streamline/releases/download/v1/streamline-sdk-v1.zip", payload.Length)).GetAwaiter().GetResult();
    Assert(File.Exists(noDigest.FilePath) && !noDigest.DigestVerified && noDigest.IntegrityMessage.Contains("did not provide", StringComparison.OrdinalIgnoreCase), "digestless asset was not retained with an explicit integrity limitation");
}

static void SafeStaging()
{
    var dir = Temp("stage-safe"); Directory.CreateDirectory(dir); var game = Path.Combine(dir, "game"); Directory.CreateDirectory(game); var zip = MakeZip(dir,
        ("bin/x64/sl.interposer.dll", "runtime"), ("bin/x64/development/sl.interposer.dll", "development runtime"), ("bin/x64/development/license.txt", "development license"), ("bin/x64/nvngx_dlssg.dll", "separate NGX component"), ("license.txt", "license"), ("docs/readme.md", "not staged"));
    var result = new StreamlineArchiveStager().StageAsync(zip, Path.Combine(dir, "Streamline", "Staging"), "v1", CancellationToken.None).GetAwaiter().GetResult();
    Assert(!result.DirectoryPath.StartsWith(game, StringComparison.OrdinalIgnoreCase), "staging was placed in game folder");
    Assert(File.Exists(Path.Combine(result.DirectoryPath, "bin", "x64", "sl.interposer.dll")), "approved runtime DLL was not staged");
    Assert(!File.Exists(Path.Combine(result.DirectoryPath, "bin", "x64", "development", "sl.interposer.dll")), "development Streamline DLL must not be staged");
    Assert(File.Exists(Path.Combine(result.DirectoryPath, "bin", "x64", "development", "license.txt")), "license notice inside development folder was not preserved");
    Assert(File.Exists(Path.Combine(result.DirectoryPath, "license.txt")), "license notice was not preserved");
    Assert(!File.Exists(Path.Combine(result.DirectoryPath, "bin", "x64", "nvngx_dlssg.dll")), "separate NGX DLL must not be treated as Streamline content");
    Assert(!File.Exists(Path.Combine(result.DirectoryPath, "docs", "readme.md")), "unknown/non-runtime file should not be extracted");
    Assert(result.FilesSkipped == 3, "skipped archive file count incorrect");
}

static void PreviewNewFiles()
{
    var (stagingRoot, staged, gameRoot, target) = PreviewFixture();
    var source = Path.Combine(staged, "bin", "x64", "sl.common.dll");
    File.WriteAllBytes(source, [1, 2, 3, 4]);
    var result = new InstallComponentsPreviewService(_ => null).CreatePreview(stagingRoot, staged, gameRoot, target);
    Assert(result.Entries.Count == 1 && result.Entries[0].Action == "New file", "missing destination should be reported as a new file");
    Assert(result.Entries[0].SourcePath == source && result.Entries[0].TargetPath == Path.Combine(target, "sl.common.dll"), "preview paths are incorrect");
    Assert(result.NewFiles == 1 && result.Replacements == 0 && result.NeedsReview == 0 && result.BackupCandidates == 0, "new-file counters are incorrect");
    Assert(!File.Exists(result.Entries[0].TargetPath), "preview created its destination file");
}

static void PreviewExistingFiles()
{
    var (stagingRoot, staged, gameRoot, target) = PreviewFixture();
    File.WriteAllBytes(Path.Combine(staged, "bin", "x64", "sl.common.dll"), [5, 6, 7]);
    var existing = Path.Combine(target, "sl.common.dll");
    var original = new byte[] { 21, 22, 23, 24 };
    File.WriteAllBytes(existing, original);
    var result = new InstallComponentsPreviewService(_ => "1.2.3.4").CreatePreview(stagingRoot, staged, gameRoot, target);
    Assert(result.Entries.Single().Action == "Needs review", "existing target with unverified provenance must require review");
    Assert(result.Entries.Single().ExistingVersion == "1.2.3.4", "existing file version was not shown");
    Assert(result.Replacements == 0 && result.NeedsReview == 1 && result.BackupCandidates == 1, "existing-file counters are incorrect");
    Assert(File.ReadAllBytes(existing).SequenceEqual(original), "preview changed existing target bytes");
}

static void PreviewUnknownVersion()
{
    var (stagingRoot, staged, gameRoot, target) = PreviewFixture();
    File.WriteAllBytes(Path.Combine(staged, "bin", "x64", "sl.common.dll"), [5]);
    File.WriteAllBytes(Path.Combine(target, "sl.common.dll"), [9]);
    var result = new InstallComponentsPreviewService(_ => null).CreatePreview(stagingRoot, staged, gameRoot, target);
    Assert(result.Entries.Single().Action == "Needs review", "unreadable existing version must require review");
    Assert(result.Entries.Single().ExistingVersion == "Unknown version" && result.NeedsReview == 1 && result.BackupCandidates == 1, "unknown version/review counters are incorrect");
}

static void PreviewMissingSource()
{
    var root = Temp("preview-missing"); var stagingRoot = Path.Combine(root, "app", "Staging"); Directory.CreateDirectory(stagingRoot);
    var missing = Path.Combine(stagingRoot, "not-here");
    var result = new InstallComponentsPreviewService().CreatePreview(stagingRoot, missing, Path.Combine(root, "game"), null);
    Assert(result.Entries.Count == 0 && result.Message?.Contains("missing", StringComparison.OrdinalIgnoreCase) == true, "missing source message was not clear");
}

static void PreviewRejectsOutsideSource()
{
    var root = Temp("preview-source-escape"); var stagingRoot = Path.Combine(root, "app", "Staging"); var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
    try { new InstallComponentsPreviewService().CreatePreview(stagingRoot, outside, Path.Combine(root, "game"), null); throw new InvalidOperationException("outside source was accepted"); }
    catch (InvalidDataException) { }
}

static void PreviewRejectsOutsideTarget()
{
    var (stagingRoot, staged, gameRoot, _) = PreviewFixture();
    var outside = Path.Combine(Path.GetDirectoryName(gameRoot)!, "outside"); Directory.CreateDirectory(outside);
    try { new InstallComponentsPreviewService().CreatePreview(stagingRoot, staged, gameRoot, outside); throw new InvalidOperationException("outside target was accepted"); }
    catch (InvalidDataException) { }
}

static void PreviewRefreshIsReadOnly()
{
    var (stagingRoot, staged, gameRoot, target) = PreviewFixture();
    var source = Path.Combine(staged, "bin", "x64", "sl.common.dll");
    var existing = Path.Combine(target, "sl.common.dll");
    var sourceBytes = new byte[] { 3, 1, 4, 1, 5 }; var targetBytes = new byte[] { 2, 7, 1, 8 };
    File.WriteAllBytes(source, sourceBytes); File.WriteAllBytes(existing, targetBytes);
    var service = new InstallComponentsPreviewService(_ => "7.8.9");
    var first = service.CreatePreview(stagingRoot, staged, gameRoot, target);
    var second = service.CreatePreview(stagingRoot, staged, gameRoot, target);
    Assert(first.Entries.Single().Action == second.Entries.Single().Action, "refresh changed preview classification");
    Assert(File.ReadAllBytes(source).SequenceEqual(sourceBytes), "refresh changed staged source bytes");
    Assert(File.ReadAllBytes(existing).SequenceEqual(targetBytes), "refresh changed target bytes");
    Assert(!File.Exists(Path.Combine(target, "sl.interposer.dll")), "refresh created an unplanned file");
}

static (string StagingRoot, string Staged, string GameRoot, string Target) PreviewFixture()
{
    var root = Temp("preview-fixture");
    var stagingRoot = Path.Combine(root, "app", "Staging"); var staged = Path.Combine(stagingRoot, "v2.14.16-test");
    var gameRoot = Path.Combine(root, "fake-game"); var target = Path.Combine(gameRoot, "bin");
    Directory.CreateDirectory(Path.Combine(staged, "bin", "x64")); Directory.CreateDirectory(target);
    return (stagingRoot, staged, gameRoot, target);
}

static StreamlineSdkAsset Asset(string name, string url, long size) => new(name, url, size, null, "v1", "v1", null, "https://github.com/NVIDIA-RTX/Streamline/releases/tag/v1");
static string Temp(string name) => Path.Combine(AppContext.BaseDirectory, "RenoDXStreamlineChecks", name + "-" + Guid.NewGuid().ToString("N"));
static string MakeZip(string dir, params (string Name, string Data)[] entries)
{
    var path = Path.Combine(dir, "test.zip"); using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var item in entries) { var entry = archive.CreateEntry(item.Name); using var writer = new StreamWriter(entry.Open()); writer.Write(item.Data); }
    return path;
}
static void ExpectInvalid(Action action) { try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; } throw new InvalidOperationException("expected invalid archive failure"); }

static (FakeFileSystem Fs, string Exe, string Root) Fixture()
{
    var root = Path.Combine(Path.GetTempPath(), "RenoDXScannerChecks", Guid.NewGuid().ToString("N"));
    var exe = Path.Combine(root, "Game.exe");
    var fs = new FakeFileSystem();
    fs.AddDirectory(root);
    fs.AddFile(exe, null);
    return (fs, exe, root);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class SkipCheckException(string message) : Exception(message);

sealed class FakeFileSystem : INvidiaComponentFileSystem
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> deniedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedVersionReads = new(StringComparer.OrdinalIgnoreCase);
    public int EnumerationCalls { get; private set; }

    public void AddDirectory(string path)
    {
        Directories.Add(path);
        var parent = Path.GetDirectoryName(path);
        if (parent is not null && parent != path) Directories.Add(parent);
    }

    public void AddFile(string path, string? version, bool failVersion = false)
    {
        Files[path] = version;
        var parent = Path.GetDirectoryName(path);
        if (parent is not null) Directories.Add(parent);
        if (failVersion) failedVersionReads.Add(path);
    }

    public void DenyDirectoryListing(string path) => deniedDirectories.Add(path);
    public bool FileExists(string path) => Files.ContainsKey(path);
    public bool DirectoryExists(string path) => Directories.Contains(path);
    public IEnumerable<string> EnumerateFiles(string directory)
    {
        EnumerationCalls++;
        if (deniedDirectories.Contains(directory)) throw new UnauthorizedAccessException("access denied");
        return Files.Keys.Where(path => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        EnumerationCalls++;
        return Directories.Where(path => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public FileAttributes GetAttributes(string path) => Directories.Contains(path) ? FileAttributes.Directory : FileAttributes.Normal;
    public string? ReadFileVersion(string path) => failedVersionReads.Contains(path) ? throw new UnauthorizedAccessException("metadata denied") : Files[path];
}

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
}

sealed class CancellingStream : Stream
{
    private bool first = true;
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => 100; public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (first) { first = false; buffer.Span[0] = 1; return ValueTask.FromResult(1); }
        return ValueTask.FromCanceled<int>(new CancellationToken(true));
    }
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class TestStreamlineCompatibilityCatalog(StreamlineCompatibilityGrant grant) : IStreamlineCompatibilityCatalog
{
    public StreamlineCompatibilityGrant? FindGrant(string steamAppId, string fileName) =>
        steamAppId == grant.SteamAppId && fileName.Equals(grant.FileName, StringComparison.OrdinalIgnoreCase) ? grant : null;
}

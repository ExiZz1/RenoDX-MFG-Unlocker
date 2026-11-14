using System.Diagnostics;
using System.IO;

namespace RenoDXMfgUnlocker;

public sealed record StagedStreamlineFile(string FileName, string RelativePath, string Sha256, string? Version);
public sealed record VerifiedStreamlineStage(StreamlineSdkAsset Asset, string ArchivePath, string StagingDirectory, DateTimeOffset VerifiedAtUtc, IReadOnlyList<StagedStreamlineFile> Files);
public sealed record StreamlineCompatibilityGrant(string SteamAppId, string FileName, string TargetRelativeDirectory, bool AllowNew, bool AllowReplace, string EvidenceReference);
public sealed record StreamlineComponentPlanEntry(string FileName, string SourcePath, string TargetPath, string SourceVersion, string Sha256, string ExistingVersion, string? ExistingSha256, string Action, bool IsSelected, bool CanSelect, string Message);
public sealed record StreamlineComponentPlan(string Message, IReadOnlyList<StreamlineComponentPlanEntry> Entries, int SelectedNewFiles, int SelectedReplacements, int SelectedNeedsReview, int SelectedBlocked, bool CanInstall, string? ContextFingerprint = null);
public sealed record StreamlineComponentInstallResult(BackupTransactionResult Transaction, IReadOnlyList<string> InstalledFiles);

public interface IStreamlineCompatibilityCatalog
{
    StreamlineCompatibilityGrant? FindGrant(string steamAppId, string fileName);
}

/// <summary>No per-game Streamline installation compatibility has been verified and approved in this build.</summary>
public sealed class NoVerifiedStreamlineCompatibilityCatalog : IStreamlineCompatibilityCatalog
{
    public StreamlineCompatibilityGrant? FindGrant(string steamAppId, string fileName) => null;
}

/// <summary>Captures a current-session inventory only after the official archive digest is rechecked.</summary>
public sealed class StreamlineStagingEvidenceService
{
    public async Task<VerifiedStreamlineStage?> RecoverLatestAsync(string downloadsRoot,string stagingRoot,string archivePath,StreamlineSdkAsset asset,CancellationToken token=default)
    {
        if(!Directory.Exists(stagingRoot))return null;
        EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(stagingRoot))!,stagingRoot);
        var directories=Directory.EnumerateDirectories(stagingRoot).Take(1001).ToArray();
        if(directories.Length>1000)throw new InvalidDataException("Too many local staging folders; prepare a fresh SDK explicitly.");
        var candidate=directories.Where(p=>Path.GetFileName(p).StartsWith(asset.ReleaseTag+"-",StringComparison.OrdinalIgnoreCase)&&!p.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)).OrderByDescending(Directory.GetCreationTimeUtc).FirstOrDefault();
        return candidate is null?null:await CaptureAsync(downloadsRoot,stagingRoot,archivePath,asset,candidate,token).ConfigureAwait(false);
    }
    public async Task<VerifiedStreamlineStage> CaptureAsync(
        string downloadsRoot,
        string stagingRoot,
        string archivePath,
        StreamlineSdkAsset asset,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!StreamlineReleaseFilter.IsSdkArchiveName(asset.Name) || asset.Sha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("The selected official release metadata has no valid SHA-256 digest; staged files cannot be authorized.");
        if (!IsOfficialAsset(asset)) throw new InvalidDataException("The archive metadata is not an official NVIDIA Streamline release asset.");
        var archive = Path.GetFullPath(archivePath);
        if (!IsSameOrChild(downloadsRoot, archive) || !File.Exists(archive)) throw new InvalidDataException("The verified SDK archive is missing from the local download store.");
        EnsureNoReparsePoints(Path.GetPathRoot(archive)!, archive);
        var archiveInfo = new FileInfo(archive);
        if (archiveInfo.Length != asset.Size || !await StreamlineDigestVerifier.MatchesAsync(archive, digest, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("The local SDK archive size or SHA-256 no longer matches its GitHub release asset.");

        var stage = Path.GetFullPath(stagingDirectory);
        if (!IsStrictChild(stagingRoot, stage) || !Directory.Exists(stage)) throw new InvalidDataException("The stage directory is missing or outside the app's Streamline staging store.");
        EnsureNoReparsePoints(Path.GetPathRoot(stage)!, stage);
        var runtimeRoot = Path.Combine(stage, "bin", "x64");
        if (!Directory.Exists(runtimeRoot)) throw new InvalidDataException("The staged SDK has no production bin/x64 directory.");
        EnsureNoReparsePoints(stage, runtimeRoot);

        var files = new List<StagedStreamlineFile>();
        foreach (var path in Directory.EnumerateFiles(runtimeRoot, "*.dll", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (!InstallComponentsPreviewService.AllowedRuntimeFiles.Contains(name)) continue;
            EnsureNoReparsePoints(runtimeRoot, path);
            var relative = Path.GetRelativePath(stage, path).Replace(Path.DirectorySeparatorChar, '/');
            var hash = await StreamlineDigestVerifier.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            string? version;
            try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch { version = null; }
            files.Add(new(name, relative, hash, string.IsNullOrWhiteSpace(version) ? null : version));
        }
        if (files.Count == 0) throw new InvalidDataException("No allowlisted production Streamline DLLs were found in the staged SDK.");
        if (files.Select(x => x.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("The stage contains ambiguous duplicate production DLL names.");
        await ValidateArchiveMembersAsync(archive, files, cancellationToken).ConfigureAwait(false);
        return new(asset, archive, stage, DateTimeOffset.UtcNow, files.AsReadOnly());
    }

    internal static async Task ValidateArchiveMembersAsync(string archivePath, IReadOnlyList<StagedStreamlineFile> files, CancellationToken token)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > 100_000) throw new InvalidDataException("SDK archive exceeds the entry limit.");
        var members = new Dictionary<string, System.IO.Compression.ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var path = StreamlineArchiveStager.ValidateEntryName(entry.FullName);
            total = checked(total + entry.Length);
            if (entry.Length > 512L * 1024 * 1024 || total > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("SDK archive exceeds expansion limits.");
            if (!members.TryAdd(path, entry)) throw new InvalidDataException("SDK archive has duplicate normalized paths.");
        }
        foreach (var file in files)
        {
            if (!members.TryGetValue(file.RelativePath, out var entry))
                throw new InvalidDataException($"Staged component '{file.FileName}' is not in the verified SDK archive.");
            await using var input = entry.Open();
            var digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token).ConfigureAwait(false));
            if (!digest.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Staged component '{file.FileName}' does not match its verified SDK archive member.");
        }
    }
    private static bool IsOfficialAsset(StreamlineSdkAsset asset) =>
        Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/NVIDIA-RTX/Streamline/releases/download/", StringComparison.OrdinalIgnoreCase);

    internal static void EnsureNoReparsePoints(string root, string path)
    {
        var basePath = Canonical(root);
        var target = Canonical(path);
        if (!IsSameOrChild(basePath, target)) throw new InvalidDataException("A path escapes its permitted root.");
        Check(basePath);
        var relative = Path.GetRelativePath(basePath, target);
        if (relative == ".") return;
        var current = basePath;
        foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Reparse point is not allowed: '{current}'.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void Check(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"Reparse point is not allowed: '{path}'."); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    internal static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var volume = Path.GetPathRoot(full)!;
        return full.Equals(volume, StringComparison.OrdinalIgnoreCase) ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static bool IsSameOrChild(string root, string path)
    {
        var basePath = Canonical(root);
        var candidate = Canonical(path);
        var prefix = basePath.EndsWith(Path.DirectorySeparatorChar) || basePath.EndsWith(Path.AltDirectorySeparatorChar) ? basePath : basePath + Path.DirectorySeparatorChar;
        return candidate.Equals(basePath, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsStrictChild(string root, string path) => !Canonical(root).Equals(Canonical(path), StringComparison.OrdinalIgnoreCase) && IsSameOrChild(root, path);
}

/// <summary>Preview and explicit installation workflow. The default catalog intentionally has no game grants.</summary>
public sealed class StreamlineComponentInstaller
{
    private readonly string downloadsRoot;
    private readonly string stagingRoot;
    private readonly BackupRestoreService transactions;
    private readonly IStreamlineCompatibilityCatalog compatibility;
    private readonly Action? preWriteCheck;

    public StreamlineComponentInstaller(string downloadsRoot, string stagingRoot, BackupRestoreService transactions, IStreamlineCompatibilityCatalog? compatibility = null, Action? preWriteCheck = null)
    {
        this.downloadsRoot = Path.GetFullPath(downloadsRoot);
        this.stagingRoot = Path.GetFullPath(stagingRoot);
        this.transactions = transactions;
        this.compatibility = compatibility ?? new NoVerifiedStreamlineCompatibilityCatalog();
        this.preWriteCheck = preWriteCheck;
    }

    public async Task<StreamlineComponentPlan> CreatePlanAsync(
        VerifiedStreamlineStage? stage,
        string? steamAppId,
        string? gameRoot,
        string? explicitTarget,
        IReadOnlySet<string> selectedFileNames,
        CancellationToken cancellationToken = default)
    {
        if (stage is null) return Empty("Stage an official digest-verified SDK in this session first.");
        await ValidateStageAsync(stage, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(steamAppId) || string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return Empty("Select an installed Steam game with a known App ID before choosing an installation target.");
        var root = Path.GetFullPath(gameRoot);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(root)!, root);
        if (string.IsNullOrWhiteSpace(explicitTarget))
            return Empty("Choose a destination folder explicitly inside the selected game's root.");
        var targetRoot = Path.GetFullPath(explicitTarget);
        if (!StreamlineStagingEvidenceService.IsSameOrChild(root, targetRoot) || !Directory.Exists(targetRoot))
            return Empty("The explicit destination must be an existing folder inside the selected game's root.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(root, targetRoot);

        var rows = new List<StreamlineComponentPlanEntry>();
        foreach (var source in stage.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.GetFullPath(Path.Combine(stage.StagingDirectory, source.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!StreamlineStagingEvidenceService.IsStrictChild(stage.StagingDirectory, sourcePath))
                throw new InvalidDataException("A staging-manifest path escapes the verified stage.");
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(stage.StagingDirectory, sourcePath);
            if (!File.Exists(sourcePath) || !StringComparer.OrdinalIgnoreCase.Equals(await StreamlineDigestVerifier.ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false), source.Sha256))
            {
                rows.Add(new(source.FileName, sourcePath, "—", source.Version ?? "Unknown", source.Sha256, "—", null, "Blocked", selectedFileNames.Contains(source.FileName), false, "Staged source is missing or its SHA-256 changed."));
                continue;
            }
            var targetPath = Path.GetFullPath(Path.Combine(targetRoot, source.FileName));
            if (!StreamlineStagingEvidenceService.IsStrictChild(root, targetPath))
                throw new InvalidDataException("A component target path escaped the selected game root.");
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(root, targetPath);
            if (Directory.Exists(targetPath))
            {
                rows.Add(new(source.FileName, sourcePath, targetPath, source.Version ?? "Unknown", source.Sha256, "Directory", null, "Blocked", selectedFileNames.Contains(source.FileName), false, "A directory occupies the target file path."));
                continue;
            }
            var exists = File.Exists(targetPath);
            var existingHash = exists ? await StreamlineDigestVerifier.ComputeSha256Async(targetPath, cancellationToken).ConfigureAwait(false) : null;
            if(existingHash is not null&&existingHash.Equals(source.Sha256,StringComparison.OrdinalIgnoreCase)){
                rows.Add(new(source.FileName,sourcePath,targetPath,source.Version??"Unknown",source.Sha256,ReadVersion(targetPath),existingHash,"No change",false,false,"Identical SHA-256; no write or backup is needed."));continue;
            }
            var grant = compatibility.FindGrant(steamAppId, source.FileName);
            var targetRelativeDirectory = Path.GetRelativePath(root, targetRoot).Replace(Path.DirectorySeparatorChar, '/');
            if (grant is null || !grant.TargetRelativeDirectory.Equals(targetRelativeDirectory, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new(source.FileName, sourcePath, targetPath, source.Version ?? "Unknown", source.Sha256,
                    exists ? ReadVersion(targetPath) : "—", existingHash, "Needs review", selectedFileNames.Contains(source.FileName), true,
                    $"No verified compatibility grant exists for Steam App ID {steamAppId} and this target folder. File presence/version alone is not compatibility evidence."));
                continue;
            }

            var action = exists
                ? grant.AllowReplace ? "Replace existing" : "Needs review"
                : grant.AllowNew ? "New file" : "Needs review";
            var reason = exists
                ? grant.AllowReplace ? "Profile permits this replacement; the original will be backed up." : "The verified profile does not authorize replacing an existing file."
                : grant.AllowNew ? "Profile permits this new file." : "The verified profile does not authorize adding this file.";
            rows.Add(new(source.FileName, sourcePath, targetPath, source.Version ?? "Unknown", source.Sha256, exists ? ReadVersion(targetPath) : "—", existingHash, action,
                selectedFileNames.Contains(source.FileName), true, reason));
        }

        return Summarize(rows);
    }

    public Task<StreamlineComponentInstallResult> InstallAsync(VerifiedStreamlineStage stage, string steamAppId, string gameRoot, string explicitTarget, IReadOnlySet<string> selectedFileNames, StreamlineComponentPlan displayedPlan, IProgress<int>? progress=null, CancellationToken cancellationToken=default) =>
        InstallCoreAsync(stage,steamAppId,gameRoot,explicitTarget,selectedFileNames,displayedPlan,progress,cancellationToken,null);
    internal Task<StreamlineComponentInstallResult> InstallWithProviderAsync(VerifiedStreamlineStage stage, string steamAppId, string gameRoot, string explicitTarget, IReadOnlySet<string> selectedFileNames, StreamlineComponentPlan displayedPlan, StreamlineComponentPlanEntry provider, IProgress<int>? progress, CancellationToken token) =>
        InstallCoreAsync(stage,steamAppId,gameRoot,explicitTarget,selectedFileNames,displayedPlan,progress,token,provider);
    private async Task<StreamlineComponentInstallResult> InstallCoreAsync(
        VerifiedStreamlineStage stage,
        string steamAppId,
        string gameRoot,
        string explicitTarget,
        IReadOnlySet<string> selectedFileNames,
        StreamlineComponentPlan displayedPlan,
        IProgress<int>? progress,
        CancellationToken cancellationToken, StreamlineComponentPlanEntry? additionalProvider)
    {
        var plan = await CreatePlanAsync(stage, steamAppId, gameRoot, explicitTarget, selectedFileNames, cancellationToken).ConfigureAwait(false);
        var providerOnly=additionalProvider is {IsSelected:true}&&!plan.Entries.Any(e=>e.IsSelected);
        if ((!plan.CanInstall&&!providerOnly) || (!displayedPlan.CanInstall&&!providerOnly) || !EquivalentSelectedPlan(plan, displayedPlan))
            throw new InvalidOperationException("The install preview changed or no longer passes validation. Refresh it and review the current file actions before confirming.");
        var selected = plan.Entries.Where(x => x.IsSelected).ToArray();
        if(additionalProvider is not null) {
            if(additionalProvider.FileName!="nvngx_dlssg.dll"
                || !Path.GetFullPath(additionalProvider.TargetPath).Equals(Path.GetFullPath(Path.Combine(explicitTarget,"nvngx_dlssg.dll")),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid additional provider target.");
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(gameRoot,additionalProvider.TargetPath);
            if(additionalProvider.IsSelected)selected=selected.Append(additionalProvider).ToArray();
        }
        var files = new List<BackupTransactionFile>(selected.Length);
        foreach (var entry in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(entry.SourcePath, cancellationToken).ConfigureAwait(false);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Staged source '{entry.FileName}' changed after preflight.");
            var relativeTarget = Path.GetRelativePath(gameRoot, entry.TargetPath).Replace(Path.DirectorySeparatorChar, '/');
            files.Add(new(relativeTarget, bytes, entry.ExistingSha256 is not null, entry.ExistingSha256));
        }
        preWriteCheck?.Invoke();
        var result = await transactions.ExecuteWithProgressAsync(gameRoot, files, progress, cancellationToken).ConfigureAwait(false);
        var installed = new List<string>();
        foreach (var entry in selected)
        {
            var actual = await StreamlineDigestVerifier.ComputeSha256Async(entry.TargetPath, CancellationToken.None).ConfigureAwait(false);
            if (!actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { await transactions.RestoreAsync(gameRoot, result.TransactionId, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception rollbackError) { throw new BackupTransactionException($"Post-write hash verification failed for '{entry.FileName}'. Rollback needs review: {rollbackError.Message}", result.TransactionId, rollbackError); }
                throw new BackupTransactionException($"Post-write hash verification failed for '{entry.FileName}'; transaction was rolled back.", result.TransactionId, new InvalidDataException("Installed hash mismatch."));
            }
            installed.Add(entry.TargetPath);
            progress?.Report(installed.Count * 100 / selected.Length);
        }
        return new(result, installed);
    }

    private static bool EquivalentSelectedPlan(StreamlineComponentPlan current, StreamlineComponentPlan displayed)
    {
        var expected = displayed.Entries.Where(x => x.IsSelected).OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        var actual = current.Entries.Where(x => x.IsSelected).OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        return expected.Length == actual.Length && expected.Zip(actual).All(pair =>
            pair.First.FileName.Equals(pair.Second.FileName, StringComparison.OrdinalIgnoreCase)
            && pair.First.TargetPath.Equals(pair.Second.TargetPath, StringComparison.OrdinalIgnoreCase)
            && pair.First.Action == pair.Second.Action
            && pair.First.Sha256.Equals(pair.Second.Sha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pair.First.ExistingSha256, pair.Second.ExistingSha256, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ValidateStageAsync(VerifiedStreamlineStage stage, CancellationToken token)
    {
        if (stage.Asset.Sha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit)) throw new InvalidDataException("The staging evidence has no valid official SHA-256 digest.");
        if (!StreamlineReleaseFilter.IsSdkArchiveName(stage.Asset.Name) || !Uri.TryCreate(stage.Asset.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/NVIDIA-RTX/Streamline/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staging evidence is not tied to an official Streamline SDK release asset.");
        if (!StreamlineStagingEvidenceService.IsSameOrChild(downloadsRoot, stage.ArchivePath) || !File.Exists(stage.ArchivePath))
            throw new InvalidDataException("The SDK archive is missing from the application's download store.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(stage.ArchivePath)!, stage.ArchivePath);
        var archive = new FileInfo(stage.ArchivePath);
        if (archive.Length != stage.Asset.Size || !await StreamlineDigestVerifier.MatchesAsync(stage.ArchivePath, digest, token).ConfigureAwait(false))
            throw new InvalidDataException("The SDK archive no longer matches its official size and SHA-256 digest.");
        if (!StreamlineStagingEvidenceService.IsStrictChild(stagingRoot, stage.StagingDirectory) || !Directory.Exists(stage.StagingDirectory))
            throw new InvalidDataException("The staging directory is missing or outside the application staging store.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(stage.StagingDirectory))!, stage.StagingDirectory);
        if (stage.Files.Count == 0 || stage.Files.Select(x => x.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != stage.Files.Count)
            throw new InvalidDataException("The staging inventory is empty or ambiguous.");
        await StreamlineStagingEvidenceService.ValidateArchiveMembersAsync(stage.ArchivePath, stage.Files, token).ConfigureAwait(false);
        foreach (var file in stage.Files)
        {
            if (!InstallComponentsPreviewService.AllowedRuntimeFiles.Contains(file.FileName)
                || !file.RelativePath.Equals("bin/x64/" + file.FileName, StringComparison.OrdinalIgnoreCase)
                || file.Sha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("The staging inventory contains an unapproved path, filename or SHA-256.");
        }
    }

    private static string ReadVersion(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "Unknown"; } catch { return "Unknown"; }
    }

    private static StreamlineComponentPlan Empty(string message) => new(message, Array.Empty<StreamlineComponentPlanEntry>(), 0, 0, 0, 0, false);

    private static StreamlineComponentPlan Summarize(List<StreamlineComponentPlanEntry> rows)
    {
        var selected = rows.Where(x => x.IsSelected).ToArray();
        var news = selected.Count(x => x.Action == "New file");
        var replacements = selected.Count(x => x.Action == "Replace existing");
        var reviews = selected.Count(x => x.Action == "Needs review");
        var blocked = selected.Count(x => x.Action == "Blocked");
        var canInstall = selected.Length > 0 && reviews == 0 && blocked == 0 && selected.All(x => x.Action is "New file" or "Replace existing");
        var message = canInstall
            ? $"Ready to install {news} new file(s) and replace {replacements} existing file(s). Confirm before writing."
            : reviews > 0
                ? $"Installation blocked: {reviews} selected component(s) need a verified per-game review."
                : blocked > 0
                    ? $"Installation blocked: {blocked} selected component(s) failed validation."
                    : "Select one or more components. No game files have been changed.";
        return new(message, rows.AsReadOnly(), news, replacements, reviews, blocked, canInstall);
    }
}

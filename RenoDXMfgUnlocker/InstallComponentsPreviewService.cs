using System.Diagnostics;
using System.IO;

namespace RenoDXMfgUnlocker;

public sealed record InstallComponentPreviewEntry(
    string FileName,
    string SourcePath,
    string TargetPath,
    string ExistingFileStatus,
    string ExistingVersion,
    string Action,
    string BackupNote,
    string ReviewNote)
{
    public string SourceSummary => "Source: " + SourcePath;
    public string TargetSummary => "Target: " + TargetPath;
    public string ExistingSummary => ExistingFileStatus + " · Existing version: " + TechnicalVersionDisplay.Format(ExistingVersion);
}

public sealed record InstallComponentsPreviewResult(
    string? StagingDirectory,
    string? TargetDirectory,
    IReadOnlyList<InstallComponentPreviewEntry> Entries,
    string? Message,
    int NewFiles,
    int Replacements,
    int NeedsReview,
    int BackupCandidates);

/// <summary>
/// Builds a read-only preview from a staging directory created by the app's verified stager.
/// It never creates directories or writes, copies, replaces, or removes files.
/// </summary>
public sealed class InstallComponentsPreviewService
{
    public static readonly IReadOnlySet<string> AllowedRuntimeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "sl.dlss.dll", "sl.dlss_d.dll", "sl.nis.dll"
    };

    private readonly Func<string, string?> versionReader;

    public InstallComponentsPreviewService(Func<string, string?>? versionReader = null)
    {
        this.versionReader = versionReader ?? (path => FileVersionInfo.GetVersionInfo(path).FileVersion);
    }

    public InstallComponentsPreviewResult CreatePreview(
        string applicationStagingRoot,
        string? verifiedStagingDirectory,
        string? selectedGameRoot,
        string? explicitTargetDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safeStagingRoot = Path.GetFullPath(applicationStagingRoot);
        if (string.IsNullOrWhiteSpace(verifiedStagingDirectory))
            return Empty(safeStagingRoot, null, "Stage a digest-verified Streamline archive in this session before creating a preview.");

        var sourceRoot = Path.GetFullPath(verifiedStagingDirectory);
        if (!IsStrictChildPath(safeStagingRoot, sourceRoot))
            throw new InvalidDataException("The source folder is outside the application's Streamline staging directory.");
        if (!Directory.Exists(sourceRoot))
            return Empty(safeStagingRoot, sourceRoot, "The verified staging folder is missing. Stage the archive again before previewing.");

        string? gameRoot = null;
        string? targetRoot = null;
        if (!string.IsNullOrWhiteSpace(selectedGameRoot)) gameRoot = Path.GetFullPath(selectedGameRoot);
        if (!string.IsNullOrWhiteSpace(explicitTargetDirectory))
        {
            if (gameRoot is null) throw new InvalidDataException("Select a game before choosing a component target folder.");
            targetRoot = Path.GetFullPath(explicitTargetDirectory);
            if (!IsSameOrChildPath(gameRoot, targetRoot))
                throw new InvalidDataException("The target folder must be inside the currently selected game's root folder.");
            if (!Directory.Exists(targetRoot))
                return Empty(safeStagingRoot, sourceRoot, "The chosen target folder no longer exists. Choose an existing folder inside the selected game.", targetRoot);
        }

        var runtimeRoot = Path.Combine(sourceRoot, "bin", "x64");
        if (!Directory.Exists(runtimeRoot))
            return Empty(safeStagingRoot, sourceRoot, "No production x64 runtime folder was found in the verified staging directory.", targetRoot);

        var entries = new List<InstallComponentPreviewEntry>();
        foreach (var sourcePath in Directory.EnumerateFiles(runtimeRoot, "*.dll", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(sourcePath);
            if (!AllowedRuntimeFiles.Contains(fileName)) continue;

            var fullSource = Path.GetFullPath(sourcePath);
            if (!IsStrictChildPath(runtimeRoot, fullSource))
                throw new InvalidDataException("A staged component path escaped the production x64 folder.");

            if (targetRoot is null)
            {
                entries.Add(new(fileName, fullSource, "Choose a target folder inside the selected game", "Target not selected", "—", "Needs review", "Not assessed", "The destination is not explicit."));
                continue;
            }

            var fullTarget = Path.GetFullPath(Path.Combine(targetRoot, fileName));
            if (!IsStrictChildPath(targetRoot, fullTarget))
                throw new InvalidDataException("A component target path escaped the selected game folder.");

            if (Directory.Exists(fullTarget))
            {
                entries.Add(new(fileName, fullSource, fullTarget, "A directory already uses this name", "Unknown version", "Needs review", "Not assessed", "The target path is a directory, not a file."));
                continue;
            }

            if (!File.Exists(fullTarget))
            {
                entries.Add(new(fileName, fullSource, fullTarget, "Not found", "—", "New file", "No backup needed", "This classification describes the file conflict only; it does not establish game compatibility."));
                continue;
            }

            string? existingVersion;
            try { existingVersion = versionReader(fullTarget); }
            catch { existingVersion = null; }
            if (string.IsNullOrWhiteSpace(existingVersion))
                entries.Add(new(fileName, fullSource, fullTarget, "Found", "Unknown version", "Needs review", "Potential backup candidate; no backup was created.", "The existing file's version could not be read."));
            else
                entries.Add(new(fileName, fullSource, fullTarget, "Found", existingVersion, "Needs review", "Potential backup candidate; no backup was created.", "The version is readable, but publisher/origin and game compatibility are not verified; replacement is not authorized by this preview."));
        }

        if (entries.Count == 0)
            return Empty(safeStagingRoot, sourceRoot, "No allowlisted production x64 Streamline DLLs were found in this staging folder.", targetRoot);

        return new(sourceRoot, targetRoot, entries,
            targetRoot is null ? "Choose an explicit destination folder to calculate exact target paths." : "Preview only. No game files were changed.",
            entries.Count(x => x.Action == "New file"),
            entries.Count(x => x.Action == "Replace existing"),
            entries.Count(x => x.Action == "Needs review"),
            entries.Count(x => x.BackupNote.StartsWith("Potential backup", StringComparison.Ordinal)));
    }

    private static InstallComponentsPreviewResult Empty(string stagingRoot, string? source, string message, string? target = null) =>
        new(source, target, Array.Empty<InstallComponentPreviewEntry>(), message, 0, 0, 0, 0);

    private static bool IsSameOrChildPath(string root, string path)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(canonicalRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrictChildPath(string root, string path) =>
        !Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        && IsSameOrChildPath(root, path);
}

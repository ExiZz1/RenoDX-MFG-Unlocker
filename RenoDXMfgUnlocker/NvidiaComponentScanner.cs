using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace RenoDXMfgUnlocker;

public sealed class NvidiaComponentScanner
{
    private static readonly string[] ComponentNames =
    [
        "sl.interposer.dll",
        "sl.common.dll",
        "sl.dlss_g.dll",
        "sl.reflex.dll",
        "sl.pcl.dll",
        "nvngx_dlssg.dll",
        // Conditional Streamline features documented by NVIDIA's SDK.
        "sl.dlss.dll",
        "nvngx_dlss.dll",
        "sl.dlss_d.dll",
        "nvngx_dlssd.dll",
        "sl.deepdvc.dll",
        "nvngx_deepdvc.dll",
        "sl.nis.dll",
        "sl.directsr.dll",
        // Separate Intel FG/latency runtime; not NVIDIA or XeSS Super Resolution.
        "libxess_fg.dll",
        "libxell.dll",
        "renodx-mfgunlock.addon64"
    ];

    private readonly INvidiaComponentFileSystem fileSystem;
    private readonly int maximumDirectories;
    private readonly bool reportSkippedLinks;
    private readonly bool skipBackupDirectories;

    public NvidiaComponentScanner(INvidiaComponentFileSystem? fileSystem = null, int maximumDirectories = 50000, bool reportSkippedLinks = false, bool skipBackupDirectories = false)
    {
        this.fileSystem = fileSystem ?? new PhysicalNvidiaComponentFileSystem();
        this.maximumDirectories = Math.Max(1, maximumDirectories);
        this.reportSkippedLinks = reportSkippedLinks;
        this.skipBackupDirectories = skipBackupDirectories;
    }

    public static IReadOnlyList<string> KnownComponentNames => ComponentNames;

    public NvidiaComponentScanResult Scan(string executablePath, string? installationRootPath = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !fileSystem.FileExists(executablePath))
            return NvidiaComponentScanResult.Failed("The selected executable is missing or unavailable.");

        string root;
        try
        {
            var fullExePath = Path.GetFullPath(executablePath);
            var exeDirectory = Path.GetDirectoryName(fullExePath) ?? string.Empty;
            root = exeDirectory;
            if (!string.IsNullOrWhiteSpace(installationRootPath))
            {
                var candidateRoot = Path.GetFullPath(installationRootPath);
                if (fileSystem.DirectoryExists(candidateRoot) && IsWithinRoot(fullExePath, candidateRoot))
                    root = candidateRoot;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return NvidiaComponentScanResult.Failed("The executable folder could not be resolved: " + ex.Message);
        }

        if (root.Length == 0 || !fileSystem.DirectoryExists(root))
            return NvidiaComponentScanResult.Failed("The executable folder does not exist or cannot be accessed.");

        var pathsByName = ComponentNames.ToDictionary(name => name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);
        var scannedDirectories = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (!visited.Add(directory)) continue;
            if (scannedDirectories >= maximumDirectories)
            {
                warnings.Add($"The scan stopped at the {maximumDirectories:N0}-folder safety limit.");
                break;
            }
            scannedDirectories++;

            try
            {
                foreach (var file in fileSystem.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    if (pathsByName.TryGetValue(name, out var matches)) matches.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or System.Security.SecurityException)
            {
                warnings.Add($"Could not list files in {directory}: {ex.Message}");
            }

            try
            {
                foreach (var child in fileSystem.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(skipBackupDirectories && IsBackupPath(Path.Combine(child,"__inventory__"))) continue;
                    try
                    {
                        if ((fileSystem.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(child);
                        else if(reportSkippedLinks) warnings.Add("Inventory excludes a reparse directory: " + child);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or System.Security.SecurityException)
                    {
                        warnings.Add($"Could not inspect folder {child}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or System.Security.SecurityException)
            {
                warnings.Add($"Could not list subfolders in {directory}: {ex.Message}");
            }
        }

        var entries = new List<NvidiaComponentEntry>();
        foreach (var name in ComponentNames)
        {
            var paths = pathsByName[name].Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length == 0)
            {
                entries.Add(new NvidiaComponentEntry(name, "Not found", "—", string.Empty, "—", false));
                continue;
            }

            foreach (var path in paths)
            {
                string? version = null;
                try { version = fileSystem.ReadFileVersion(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                    warnings.Add($"Could not read version metadata for {path}: {ex.Message}");
                }

                var hasVersion = !string.IsNullOrWhiteSpace(version);
                var fullPath = Path.GetFullPath(path);
                var isBackup = IsBackupPath(fullPath);
                var location = isBackup ? "Backup copy" : IsRootFile(root, fullPath) ? "Game root" : "Game subfolder";
                long? size = null;
                string? sha256 = null;
                try
                {
                    (size, sha256) = fileSystem.ReadIntegrity(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
                {
                    warnings.Add($"Could not read size or SHA-256 for {path}: {ex.Message}");
                }
                entries.Add(new NvidiaComponentEntry(name, "Found", hasVersion ? version!.Trim() : "Unknown version", path, location, isBackup, size, sha256){RelativePath=Path.GetRelativePath(root,fullPath)});
            }
        }

        return new NvidiaComponentScanResult(root, entries, warnings, scannedDirectories, null);
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsRootFile(string root, string fullPath) =>
        string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);

    internal static bool IsBackupPath(string fullPath)
    {
        var segments = Path.GetFullPath(fullPath).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Take(Math.Max(0, segments.Length - 1)).Any(segment =>
            segment.StartsWith("MFG_Backup_", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".mfg-dependency-backups", StringComparison.OrdinalIgnoreCase) ||
            segment.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) ||
            segment.StartsWith("backup-", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record NvidiaComponentDisplay(
    IReadOnlyList<NvidiaComponentEntry> VisibleEntries,
    int GameFileCount,
    int BackupFileCount,
    int VisibleFileCount,
    int TotalFileCount,
    bool ShowingBackups)
{
    public static NvidiaComponentDisplay Create(NvidiaComponentScanResult result, bool showBackups)
    {
        var found = result.Entries.Where(entry => entry.Status == "Found").ToArray();
        var gameFiles = found.Count(entry => !entry.IsBackup);
        var backupFiles = found.Count(entry => entry.IsBackup);
        var visibleFiles = gameFiles + (showBackups ? backupFiles : 0);
        var visibleEntries = result.Entries.Where(entry => !entry.IsBackup || showBackups).ToArray();
        return new(visibleEntries, gameFiles, backupFiles, visibleFiles, found.Length, showBackups);
    }

    public string Summary
    {
        get
        {
            var gameLabel = GameFileCount == 1 ? "game file" : "game files";
            var backupLabel = BackupFileCount == 1 ? "backup copy" : "backup copies";
            var visibleLabel = VisibleFileCount == 1 ? "file visible" : "files visible";
            if (BackupFileCount == 0) return $"{GameFileCount:N0} {gameLabel} · no backup copies found";
            if (ShowingBackups) return $"{GameFileCount:N0} {gameLabel} · {BackupFileCount:N0} {backupLabel} shown · {VisibleFileCount:N0} {visibleLabel}";
            return $"{GameFileCount:N0} {gameLabel} · {BackupFileCount:N0} {backupLabel} hidden";
        }
    }
}

public sealed record NvidiaComponentEntry(string Name, string Status, string Version, string Path, string Location, bool IsBackup, long? SizeBytes = null, string? Sha256 = null)
{
    public string RelativePath {get;init;}="";
    public string DisplayVersion=>TechnicalVersionDisplay.Format(Version);
    public string DisplayLocation=>Location+(RelativePath.Length==0?"":" · "+RelativePath);
    public string IntegritySummary => SizeBytes is long size && Sha256 is not null
        ? $"Size: {size:N0} bytes · SHA-256: {Sha256}"
        : "Size / SHA-256 unavailable";
}

public sealed record NvidiaComponentScanResult(
    string? RootDirectory,
    IReadOnlyList<NvidiaComponentEntry> Entries,
    IReadOnlyList<string> Warnings,
    int ScannedDirectories,
    string? Error)
{
    public int FoundCopies => Entries.Count(entry => entry.Status == "Found");
    public IReadOnlyList<string> PotentialConflicts => Entries
        .Where(entry => entry.Status == "Found" && !entry.IsBackup)
        .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group =>
        {
            var copies = group.ToArray();
            var hashes = copies.Select(copy => copy.Sha256).Where(hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var versions = copies.Select(copy => copy.Version).Where(version => !string.IsNullOrWhiteSpace(version) && version != "Unknown version" && version != "—").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var finding = hashes.Length > 1 || versions.Length > 1 ? "has divergent version/hash metadata" : hashes.Length == 1 && copies.All(copy => copy.Sha256 is not null) ? "has identical bytes at multiple locations" : "has multiple locations with incomplete metadata";
            return "Multiple copies detected — loaded module unknown. "+$"{group.Key} {finding} ({copies.Length} non-backup copies); the loaded copy is unknown.";
        })
        .ToArray();
    public static NvidiaComponentScanResult Failed(string error) => new(null, [], [], 0, error);
}

public interface INvidiaComponentFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateFiles(string directory);
    IEnumerable<string> EnumerateDirectories(string directory);
    FileAttributes GetAttributes(string path);
    string? ReadFileVersion(string path);
    (long? SizeBytes, string? Sha256) ReadIntegrity(string path) => (null, null);
}

internal sealed class PhysicalNvidiaComponentFileSystem : INvidiaComponentFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public IEnumerable<string> EnumerateFiles(string directory) => Directory.EnumerateFiles(directory);
    public IEnumerable<string> EnumerateDirectories(string directory) => Directory.EnumerateDirectories(directory);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public string? ReadFileVersion(string path) => FileVersionInfo.GetVersionInfo(path).FileVersion;
    public (long? SizeBytes, string? Sha256) ReadIntegrity(string path)
    {
        var info = new FileInfo(path);
        using var stream = File.OpenRead(path);
        return (info.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }
}

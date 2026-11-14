using System.IO.Compression;
using System.IO;

namespace RenoDXMfgUnlocker;

public sealed record StreamlineStagingResult(string DirectoryPath, int FilesExtracted, int FilesSkipped);

public sealed class StreamlineArchiveStager
{
    private static readonly HashSet<string> RuntimeFiles = new(StringComparer.OrdinalIgnoreCase)
    { "sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll", "sl.pcl.dll", "sl.dlss.dll", "sl.dlss_d.dll", "sl.nis.dll" };
    private const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private const long MaxSingleEntryBytes = 512L * 1024 * 1024;

    public Task<StreamlineStagingResult> StageAsync(string archivePath, string stagingRoot, string releaseTag, CancellationToken cancellationToken = default, string? expectedSha256 = null) => Task.Run(() => Stage(archivePath, stagingRoot, releaseTag, cancellationToken, expectedSha256), cancellationToken);

    private static StreamlineStagingResult Stage(string archivePath, string stagingRoot, string releaseTag, CancellationToken token, string? expectedSha256)
    {
        token.ThrowIfCancellationRequested();
        if (expectedSha256 is { Length: 64 } && !StreamlineDigestVerifier.MatchesAsync(archivePath, expectedSha256, token).GetAwaiter().GetResult())
            throw new InvalidDataException("The downloaded archive no longer matches the GitHub SHA-256 digest; staging was stopped.");
        Directory.CreateDirectory(stagingRoot);
        var finalRoot = Path.GetFullPath(Path.Combine(stagingRoot, SafeSegment(releaseTag) + "-" + Guid.NewGuid().ToString("N")));
        var tempRoot = finalRoot + ".partial";
        Directory.CreateDirectory(tempRoot);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("The archive contains too many entries.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedTotal = 0; var skippedFiles = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                var relative = ValidateEntryName(entry.FullName);
                if (!seen.Add(relative)) throw new InvalidDataException("The archive contains duplicate file paths.");
                if (entry.Length < 0 || entry.Length > MaxSingleEntryBytes) throw new InvalidDataException("The archive contains an entry larger than the safety limit.");
                expandedTotal = checked(expandedTotal + entry.Length);
                if (expandedTotal > MaxExpandedBytes) throw new InvalidDataException("The archive expands beyond the 2 GiB safety limit.");
                if (relative.EndsWith('/')) continue;
                var leaf = Path.GetFileName(relative);
                var isRuntime = IsProductionX64Runtime(relative, leaf);
                var isNotice = leaf.Contains("license", StringComparison.OrdinalIgnoreCase) || leaf.Contains("notice", StringComparison.OrdinalIgnoreCase) || leaf.Contains("third-party", StringComparison.OrdinalIgnoreCase);
                if (!isRuntime && !isNotice) { skippedFiles++; continue; }
                var destination = Path.GetFullPath(Path.Combine(tempRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive path escapes the staging directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                if (output.Length != entry.Length) throw new InvalidDataException("An archive entry was truncated while extracting.");
            }
            var extracted = Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories).Count();
            if (extracted == 0) throw new InvalidDataException("No supported Streamline runtime files or license notices were found in this archive.");
            Directory.Move(tempRoot, finalRoot);
            return new(finalRoot, extracted, skippedFiles);
        }
        catch
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            throw;
        }
    }

    internal static string ValidateEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains('\\') || entryName.StartsWith('/') || Path.IsPathRooted(entryName) || entryName.Contains(':')) throw new InvalidDataException("The archive contains an unsafe path.");
        var isDirectory = entryName.EndsWith('/');
        if (entryName.EndsWith("//", StringComparison.Ordinal)) throw new InvalidDataException("The archive contains a non-canonical path.");
        var body = isDirectory ? entryName[..^1] : entryName;
        var segments = body.Split('/');
        if (segments.Length == 0 || segments.Any(string.IsNullOrEmpty)) throw new InvalidDataException("The archive contains a non-canonical path.");
        if (segments.Any(s => s is ".." or ".")) throw new InvalidDataException("The archive contains a path traversal entry.");
        if (segments.Any(s => s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || s.EndsWith('.') || s.EndsWith(' '))) throw new InvalidDataException("The archive contains an invalid path segment.");
        return string.Join('/', segments) + (isDirectory ? "/" : "");
    }

    private static bool IsProductionX64Runtime(string relativePath, string leaf)
    {
        if (!RuntimeFiles.Contains(leaf)) return false;
        var segments = relativePath.Split('/');
        return segments.Length >= 3
            && segments[^3].Equals("bin", StringComparison.OrdinalIgnoreCase)
            && segments[^2].Equals("x64", StringComparison.OrdinalIgnoreCase)
            && !segments.Contains("development", StringComparer.OrdinalIgnoreCase);
    }

    private static string SafeSegment(string value)
    {
        var safe = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')).Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "release" : safe[..Math.Min(safe.Length, 80)];
    }
}

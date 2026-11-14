using System.IO;

namespace RenoDXMfgUnlocker;

public sealed record StreamlineLocalArchiveCheck(string? FilePath, bool IsValid, string Message);

/// <summary>Resolves and verifies an already-downloaded SDK archive without downloading it.</summary>
public sealed class StreamlineLocalArchiveValidator
{
    private readonly string downloadsRoot;

    public StreamlineLocalArchiveValidator(string downloadsRoot) => this.downloadsRoot = Path.GetFullPath(downloadsRoot);

    public async Task<StreamlineLocalArchiveCheck> CheckAsync(StreamlineSdkAsset asset, CancellationToken cancellationToken = default)
    {
        if (!StreamlineReleaseFilter.IsSdkArchiveName(asset.Name) || asset.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || asset.Name.Contains("..", StringComparison.Ordinal))
            return new(null, false, "The selected release archive name is invalid.");
        var folder = Path.Combine(downloadsRoot, SafeSegment(asset.ReleaseTag));
        var path = Path.GetFullPath(Path.Combine(folder, asset.Name));
        if (!IsInside(downloadsRoot, path)) return new(null, false, "The selected archive path is outside the Streamline download store.");
        if (!File.Exists(path)) return new(path, false, "No local archive exists for this release yet. Download it first.");
        var info = new FileInfo(path);
        if (asset.Size <= 0 || info.Length != asset.Size)
            return new(path, false, $"Local archive size does not match the selected GitHub asset ({info.Length:N0} of {asset.Size:N0} bytes).");
        if (asset.Sha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit))
            return new(path, false, "The selected GitHub asset has no valid SHA-256 digest. Refresh release metadata before staging.");
        if (!await StreamlineDigestVerifier.MatchesAsync(path, digest, cancellationToken).ConfigureAwait(false))
            return new(path, false, "Local archive SHA-256 does not match the selected GitHub release asset.");
        return new(path, true, "Existing local archive matches the selected asset size and GitHub SHA-256 digest.");
    }

    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeSegment(string value)
    {
        var safe = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')).Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "release" : safe[..Math.Min(safe.Length, 80)];
    }
}

using System.Security.Cryptography;
using System.IO;

namespace RenoDXMfgUnlocker;

public static class StreamlineDigestVerifier
{
    public static string Describe(string? expectedSha256) => expectedSha256 is { Length: 64 } digest && digest.All(Uri.IsHexDigit)
        ? "GitHub SHA-256 digest available"
        : "Official SHA-256 digest not provided; integrity cannot be confirmed against it.";

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    public static async Task<bool> MatchesAsync(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)) return false;
        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expectedSha256));
    }
}

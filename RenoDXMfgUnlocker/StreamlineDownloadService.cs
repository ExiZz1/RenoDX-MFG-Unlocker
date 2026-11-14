using System.Net.Http;
using System.IO;

namespace RenoDXMfgUnlocker;

public sealed record StreamlineDownloadProgress(long BytesReceived, long? TotalBytes);
public sealed record StreamlineDownloadResult(string FilePath, bool DigestVerified, string IntegrityMessage);

public sealed class StreamlineDownloadService
{
    private readonly HttpClient http;
    public string StorageRoot { get; }

    public StreamlineDownloadService(string? storageRoot = null, HttpClient? httpClient = null)
    {
        StorageRoot = storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "Streamline");
        http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string DownloadsDirectory => Path.Combine(StorageRoot, "Downloads");
    public string StagingDirectory => Path.Combine(StorageRoot, "Staging");

    public async Task<StreamlineDownloadResult> DownloadAsync(StreamlineSdkAsset asset, IProgress<StreamlineDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.StartsWith("/NVIDIA-RTX/Streamline/releases/download/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The download URL is not an official Streamline GitHub release asset URL.");
        if (!StreamlineReleaseFilter.IsSdkArchiveName(asset.Name) || asset.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || asset.Name.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("The SDK archive name is invalid.");
        if (asset.Size <= 0 || asset.Size > 1024L * 1024 * 1024) throw new InvalidDataException("The archive size is missing or exceeds the 1 GiB safety limit.");
        var releaseFolder = SafeSegment(asset.ReleaseTag);
        var targetFolder = Path.Combine(DownloadsDirectory, releaseFolder);
        Directory.CreateDirectory(targetFolder);
        var target = Path.Combine(targetFolder, asset.Name);
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length == asset.Size && asset.Sha256 is { Length: 64 } known && await StreamlineDigestVerifier.MatchesAsync(target, known, cancellationToken).ConfigureAwait(false))
                return new(target, true, "Existing archive matches the GitHub SHA-256 digest.");
            throw new IOException("A file with this name already exists. It was kept unchanged; remove or rename it before retrying.");
        }
        var partial = Path.Combine(targetFolder, $".{asset.Name}.{Guid.NewGuid():N}.partial");
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024]; long received = 0; var total = response.Content.Headers.ContentLength ?? asset.Size;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > asset.Size || received > 1024L * 1024 * 1024) throw new InvalidDataException("The server sent more data than the advertised archive size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new(received, total));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (received != asset.Size) throw new EndOfStreamException($"Download was incomplete ({received:N0} of {asset.Size:N0} bytes). Retry the download.");
            }
            var verified = false;
            var message = "GitHub did not provide a SHA-256 digest for this asset; integrity was not confirmed against an official digest.";
            if (asset.Sha256 is { Length: 64 } digest)
            {
                if (!await StreamlineDigestVerifier.MatchesAsync(partial, digest, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("The downloaded file does not match GitHub's SHA-256 digest. The incomplete file was discarded.");
                verified = true; message = "Downloaded file matches the SHA-256 digest returned by GitHub. This does not establish game compatibility or publisher authenticity by itself.";
            }
            File.Move(partial, target);
            return new(target, verified, message);
        }
        finally { try { if (File.Exists(partial)) File.Delete(partial); } catch { } }
    }

    private static string SafeSegment(string value)
    {
        var safe = string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')).Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "release" : safe[..Math.Min(safe.Length, 80)];
    }
}

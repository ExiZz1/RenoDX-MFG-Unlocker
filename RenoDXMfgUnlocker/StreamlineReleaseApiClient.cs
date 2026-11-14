using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

public sealed record StreamlineSdkAsset(string Name, string DownloadUrl, long Size, string? Sha256, string ReleaseTag, string ReleaseName, DateTimeOffset? PublishedAt, string ReleaseUrl);
public sealed record StreamlineReleaseEntry(string Tag, string Name, DateTimeOffset? PublishedAt, string ReleaseUrl, IReadOnlyList<StreamlineSdkAsset> Assets);
public sealed record StreamlineAssetRow(StreamlineSdkAsset Asset, string DisplayTitle, string Detail, string SizeText, string DigestStatus);

public static class StreamlineReleaseFilter
{
    private static readonly Regex SdkName = new("^streamline-sdk-v[0-9][^/\\\\]*\\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public static bool IsSdkArchiveName(string? name) => name is not null && SdkName.IsMatch(name) &&
        !name.Contains("source code", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith("-aarch64.zip", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith("-arm64ec.zip", StringComparison.OrdinalIgnoreCase);
}

public sealed class StreamlineReleaseApiClient
{
    private readonly HttpClient http;
    private const string ApiRoot = "https://api.github.com/repos/NVIDIA-RTX/Streamline/releases";

    public StreamlineReleaseApiClient(HttpClient? httpClient = null)
    {
        http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        if (!http.DefaultRequestHeaders.UserAgent.Any()) http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RenoDX-MFG-Unlocker", "1.0"));
        if (!http.DefaultRequestHeaders.Accept.Any()) http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<IReadOnlyList<StreamlineReleaseEntry>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<StreamlineReleaseEntry>();
        for (var page = 1; page <= 20; page++)
        {
            using var response = await http.GetAsync($"{ApiRoot}?per_page=100&page={page}", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var items = json.RootElement.EnumerateArray().ToArray();
            foreach (var item in items)
            {
                if (item.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
                var tag = GetString(item, "tag_name") ?? "Unknown tag";
                var name = GetString(item, "name") ?? tag;
                var releaseUrl = GetString(item, "html_url") ?? "https://github.com/NVIDIA-RTX/Streamline/releases";
                DateTimeOffset? published = DateTimeOffset.TryParse(GetString(item, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
                var assets = new List<StreamlineSdkAsset>();
                if (item.TryGetProperty("assets", out var assetsElement))
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    var assetName = GetString(asset, "name");
                    if (!StreamlineReleaseFilter.IsSdkArchiveName(assetName)) continue;
                    var url = GetString(asset, "browser_download_url");
                    if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) continue;
                    var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                    var digest = GetString(asset, "digest");
                    var hash = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && digest.Length == 71 ? digest[7..] : null;
                    assets.Add(new(assetName!, url, size, hash, tag, name, published, releaseUrl));
                }
                if (assets.Count > 0) results.Add(new(tag, name, published, releaseUrl, assets));
            }
            if (items.Length < 100) break;
        }
        return results;
    }

    private static string? GetString(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

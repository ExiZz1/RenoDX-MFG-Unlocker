using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RenoDXMfgUnlocker;

internal sealed record DownloadedSettingsCatalog(byte[] Json, byte[] Signature, MfgDynamicSettingsCatalog Catalog);

/// <summary>
/// Explicitly requested fallback catalog retrieval. Downloads data into bounded memory, never activates it.
/// A dedicated release tag and asset names keep this protocol separate from executable self-update.
/// </summary>
internal sealed class MfgSettingsCatalogUpdateClient : IDisposable
{
    internal const string Tag = "mfg-settings-catalog-v1";
    internal const string JsonName = "MFGUnlock.SettingsCatalog.json";
    internal const string SignatureName = JsonName + ".sig";
    internal const string RepositoryUrl = "https://github.com/ExiZz1/RenoDX-MFG-Unlocker";
    internal const string MetadataUrl = "https://api.github.com/repos/ExiZz1/RenoDX-MFG-Unlocker/releases/tags/" + Tag;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Func<byte[], byte[], MfgDynamicSettingsCatalog> verify;
    internal MfgSettingsCatalogUpdateClient(HttpClient? http = null, Func<byte[], byte[], MfgDynamicSettingsCatalog>? verify = null)
    {
        ownsHttp = http is null;
        this.http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { Timeout = TimeSpan.FromSeconds(30) };
        this.verify = verify ?? ((json, sig) => MfgDynamicSettingsCatalog.Verify(json, sig));
    }
    internal async Task<DownloadedSettingsCatalog?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await ReadAsync(new(MetadataUrl), 2 * 1024 * 1024, false, true, cancellationToken).ConfigureAwait(false);
        if (metadata is null) return null; // An unpublished channel is not a verified update.
        using var doc = JsonDocument.Parse(metadata, new() { MaxDepth = 16 });
        var root = doc.RootElement;
        if (root.GetProperty("tag_name").GetString() != Tag || root.GetProperty("draft").GetBoolean()
            || root.GetProperty("html_url").GetString() != RepositoryUrl + "/releases/tag/" + Tag) throw Invalid();
        var assets = root.GetProperty("assets");
        if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 100) throw Invalid();
        (Uri Url, int Size, string? Digest) Asset(string name, int maximum)
        {
            var matches = assets.EnumerateArray().Where(a => string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw Invalid();
            var a = matches[0];
            var expected = RepositoryUrl + "/releases/download/" + Tag + "/" + name;
            if (a.GetProperty("name").GetString() != name || a.GetProperty("state").GetString() != "uploaded"
                || a.GetProperty("browser_download_url").GetString() != expected || !a.GetProperty("size").TryGetInt32(out var size) || size < 1 || size > maximum) throw Invalid();
            string? digest = null;
            if (a.TryGetProperty("digest", out var d) && d.ValueKind != JsonValueKind.Null) {
                digest = d.GetString();
                if (digest is null || !System.Text.RegularExpressions.Regex.IsMatch(digest, @"\Asha256:[a-fA-F0-9]{64}\z")) throw Invalid();
            }
            return (new(expected), size, digest);
        }
        var jsonAsset = Asset(JsonName, MfgDynamicSettingsCatalog.MaximumBytes);
        var sigAsset = Asset(SignatureName, 1024);
        if (sigAsset.Size < 256) throw Invalid();
        async Task<byte[]> Download((Uri Url, int Size, string? Digest) a)
        {
            var bytes = await ReadAsync(a.Url, a.Size, true, false, cancellationToken).ConfigureAwait(false) ?? throw Invalid();
            if (bytes.Length != a.Size || a.Digest is not null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(a.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw Invalid();
            return bytes;
        }
        var json = await Download(jsonAsset).ConfigureAwait(false);
        var signature = await Download(sigAsset).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = verify(json, signature); // Always verify the catalog-specific signature, even when API digest is present.
        return new(json, signature, catalog);
    }
    private async Task<byte[]?> ReadAsync(Uri initial, int maximum, bool redirects, bool missingAllowed, CancellationToken token)
    {
        var uri = initial;
        for (var hop = 0; hop <= 3; hop++) {
            if (!SafeTransport(uri, initial, redirects)) throw Invalid();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("RenoDX-MFG-Unlocker/1.3.0-dev.2");
            request.Headers.Accept.ParseAdd(redirects ? "application/octet-stream" : "application/vnd.github+json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) {
                if (!redirects || hop == 3 || response.Headers.Location is null) throw Invalid();
                uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new(uri, response.Headers.Location);
                continue;
            }
            if (missingAllowed && response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximum) throw Invalid();
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0) {
                if (output.Length + read > maximum) throw Invalid();
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        throw Invalid();
    }
    private static bool SafeTransport(Uri uri, Uri initial, bool redirects) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri.AbsoluteUri == initial.AbsoluteUri || redirects && uri.Host == "release-assets.githubusercontent.com");
    private static InvalidDataException Invalid() => new("Invalid settings catalog release or download.");
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}

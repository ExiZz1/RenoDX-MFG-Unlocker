using System.IO;
using System.Net;
using System.Net.Http;

namespace RenoDXMfgUnlocker;

/// <summary>Acquires only the reviewed official Full Add-on package into app-owned storage.</summary>
public sealed class ReShadePackageService
{
    private readonly HttpClient http;
    private readonly ReShadeInstallerVerifier verifier=new();
    public string Store {get;}
    public string InstallerPath=>Path.Combine(Store,ReShadeInstallerVerifier.Reviewed.Version,ReShadeInstallerVerifier.Reviewed.FileName);
    public ReShadePackageService(string? store=null):this(store,new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3)}){}
    internal ReShadePackageService(string? store,HttpClient http){Store=Path.GetFullPath(store??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","ReShade"));this.http=http;}
    public static bool IsReviewedSource(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps&&uri.Host.Equals("reshade.me",StringComparison.OrdinalIgnoreCase)&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&url==ReShadeInstallerVerifier.Reviewed.Url;
    internal void ValidateGameRoot(string gameRoot){if(StreamlineStagingEvidenceService.IsSameOrChild(gameRoot,Store)||StreamlineStagingEvidenceService.IsSameOrChild(Store,gameRoot))throw new InvalidDataException("Installer cache and game folder must not overlap.");}
    public async Task<PreparedReShadeInstaller> PrepareAsync(string? gameRoot,IProgress<long>? progress=null,CancellationToken token=default,string? localInstaller=null)
    {
        token.ThrowIfCancellationRequested();
        if(gameRoot is not null)ValidateGameRoot(gameRoot);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Store)!,InstallerPath);
        if(File.Exists(InstallerPath))return await Task.Run(()=>verifier.Verify(InstallerPath),token).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);
        var partial=InstallerPath+"."+Guid.NewGuid().ToString("N")+".partial";
        try
        {
            if(localInstaller is not null)
            {
                await Task.Run(()=>verifier.Verify(localInstaller),token).ConfigureAwait(false);
                using var input=new FileStream(localInstaller,FileMode.Open,FileAccess.Read,FileShare.Read);
                using var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
                await input.CopyToAsync(output,token).ConfigureAwait(false);
            }
            else
            {
                if(!IsReviewedSource(ReShadeInstallerVerifier.Reviewed.Url))throw new InvalidDataException("Unreviewed ReShade download source.");
                using var response=await http.GetAsync(ReShadeInstallerVerifier.Reviewed.Url,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                if((int)response.StatusCode is >=300 and <400||response.RequestMessage?.RequestUri?.AbsoluteUri!=ReShadeInstallerVerifier.Reviewed.Url)throw new InvalidDataException("ReShade download redirects are not accepted.");
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is {} size&&size!=ReShadeInstallerVerifier.Reviewed.Size)throw new InvalidDataException("Unexpected ReShade package length.");
                await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
                var buffer=new byte[65536];long total=0;int read;
                while((read=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){total+=read;if(total>ReShadeInstallerVerifier.Reviewed.Size)throw new InvalidDataException("ReShade package exceeds its reviewed size.");await output.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);progress?.Report(total);}
                if(total!=ReShadeInstallerVerifier.Reviewed.Size)throw new InvalidDataException("ReShade download is incomplete.");
            }
            token.ThrowIfCancellationRequested();await Task.Run(()=>verifier.Verify(partial),token).ConfigureAwait(false);
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Store)!,InstallerPath);File.Move(partial,InstallerPath,false);
            return verifier.Verify(InstallerPath);
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public async Task<PreparedReShadeInstaller?> FindCachedAsync(string? gameRoot=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(gameRoot is not null)ValidateGameRoot(gameRoot);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Store)!,InstallerPath);
        if(!File.Exists(InstallerPath))return null;
        return await Task.Run(()=>verifier.Verify(InstallerPath),token).ConfigureAwait(false);
    }
    public PreparedReShadeInstaller Reverify(PreparedReShadeInstaller shown)
    {
        if(!Path.GetFullPath(shown.Path).Equals(InstallerPath,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use the verified ReShade application cache.");
        var fresh=verifier.Verify(InstallerPath);if(fresh!=shown)throw new InvalidDataException("Installer changed after preparation.");return fresh;
    }
}

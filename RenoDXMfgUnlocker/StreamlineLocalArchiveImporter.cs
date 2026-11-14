using System.IO;
namespace RenoDXMfgUnlocker;
public static class StreamlineLocalArchiveImporter
{
    public static async Task<string> ImportAsync(string source, string downloadsRoot, StreamlineSdkAsset asset, CancellationToken token=default)
    {
        if(asset.Sha256 is not {Length:64} digest || !digest.All(Uri.IsHexDigit)
            || !StreamlineReleaseFilter.IsSdkArchiveName(asset.Name) || Path.GetFileName(asset.Name)!=asset.Name
            || !Uri.TryCreate(asset.DownloadUrl,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com"
            || !uri.AbsolutePath.StartsWith("/NVIDIA-RTX/Streamline/releases/download/",StringComparison.Ordinal))
            throw new InvalidDataException("Unconfirmed local archive: official NVIDIA release metadata and digest are required.");
        var folder=Path.GetFullPath(Path.Combine(downloadsRoot,asset.ReleaseTag));
        if(!StreamlineStagingEvidenceService.IsStrictChild(downloadsRoot,folder)) throw new InvalidDataException("Invalid release storage path.");
        var target=Path.Combine(folder,asset.Name);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(folder)!,target);
        if(new FileInfo(source).Length!=asset.Size || !await StreamlineDigestVerifier.MatchesAsync(source,digest,token))
            throw new InvalidDataException("Unconfirmed local ZIP: size/SHA-256 differs from official metadata.");
        Directory.CreateDirectory(folder);
        if(File.Exists(target))
        {
            if(new FileInfo(target).Length==asset.Size && await StreamlineDigestVerifier.MatchesAsync(target,digest,token)) return target;
            throw new IOException("A different stored archive already exists; it was not overwritten.");
        }
        var partial=target+"."+Guid.NewGuid().ToString("N")+".partial";
        try {
            await using(var input=File.OpenRead(source))
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                await input.CopyToAsync(output,token);
            if(new FileInfo(partial).Length!=asset.Size || !await StreamlineDigestVerifier.MatchesAsync(partial,digest,token))
                throw new InvalidDataException("Local ZIP changed during import.");
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(folder)!,target);
            token.ThrowIfCancellationRequested(); File.Move(partial,target); return target;
        } finally { if(File.Exists(partial)) File.Delete(partial); }
    }
}

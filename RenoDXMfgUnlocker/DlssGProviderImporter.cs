using System.IO;
using System.IO.Compression;
namespace RenoDXMfgUnlocker;
public static class DlssGProviderImporter
{
    public static async Task<VerifiedDlssGProvider> FromSdkAsync(VerifiedStreamlineStage stage,string downloadsRoot,string stagingRoot,string providerStore,CancellationToken token=default)
    {
        // Reuse official metadata/digest and staging checks; NGX remains separate from the Streamline allowlist.
        await new StreamlineStagingEvidenceService().CaptureAsync(downloadsRoot,stagingRoot,stage.ArchivePath,stage.Asset,stage.StagingDirectory,token);
        Directory.CreateDirectory(providerStore);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(providerStore))!,providerStore);
        var file=Path.Combine(providerStore,Guid.NewGuid().ToString("N")+".dll");
        try {
            using(var zip=ZipFile.OpenRead(stage.ArchivePath)) {
                var entries=zip.Entries.Where(x=>StreamlineArchiveStager.ValidateEntryName(x.FullName).Equals("bin/x64/nvngx_dlssg.dll",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(entries.Length!=1 || entries[0].Length<4096 || entries[0].Length>128L*1024*1024) throw new InvalidDataException("The official SDK has no unique production DLSS-G provider.");
                using var input=entries[0].Open();
                await using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                await input.CopyToAsync(output,token);
            }
            token.ThrowIfCancellationRequested();
            var provider=await Task.Run(()=>new DlssGProviderVerifier().Verify(file),token);
            var expected=stage.Asset.ReleaseTag.TrimStart('v','V') switch { "2.14.16"=>"310.9.16.0","2.14.1"=>"310.9.1.0",_=>"" };
            if(provider.Version!=expected) throw new InvalidDataException("SDK and DLSS-G versions do not match the supported documented pair.");
            return provider;
        } catch { if(File.Exists(file)) File.Delete(file); throw; }
    }
}

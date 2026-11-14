using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

internal static class SelfUpdateTrust
{
    // Owner-approved public trust provisioned 2026-11-02; no private material.
    // Public SPKI SHA-256: BE25A13FB00FDE3126CC939C250B4937EE8A5C3D90B9255E8A131B10C1A20BA7
    // NEVER populate this from GitHub, a local file, an environment variable or CLI.
    internal static IReadOnlyDictionary<string,string> Keys {get;}=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(StringComparer.Ordinal) {
        ["owner-release-2026"]="MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAwZ6uPnIyoX/+tweDT3VyIAhyUEOiNzOB1RbwskzbbsLnaz8GgnTywEmUTfRefWHdJBwR8P0YODuoiLh+6qAjgUFPDVDoKplUP3YPe+3HvW/4YmmeHA9IE7pZAFLR4CsP5RjrwJamSKtUfePoj8pnTyz6jur0GrWCWe9idooJgrJHkqWNbi+jbm/sx9OT8dd/dXvDu7M9qC5k0laRR90EkBlG0mr63o8Kn2p6Oq2Z9TKNoTuTDBuaselqVztl39cWv6nsNXWHLB7nuau+xbyRKJuOEU/CzCkOch9IvrucibDDI2b3mjp9fTKWLg+H0AXzdy0S+WEtKfrqg2aqfmU0EJsfMtlLvKFrBAQ+WEgdo7Dpkh3ScnKjh/yitvqEzr9jrAGkTsI3L8UJCGXyV42Q2RpC6xGPgRknVzUW+3/t9skoxfoFqLfXMRLB0vYsoLArGB1PfYEB4W62SH0JffZVa25Fr1rROCGagimSDktd2ZCBFq4bnVRhxiZ/PByjlX8H3OrBFh0GUsTPFgQWhVf4w0c8JWtT/CNk9SKZ3yC+tRk0X/HypHUKmV/nHnV1Y0aIXJe+LEhEGR6aWxy5Y7ZURH4QDj9Dzqn0Hyd46P3U9jHd2x9ZEhQnbGJblenzZMBwcD7ezfQIlJNMA1e9EBLFnYAqXExrLkggV/pqHZgLpjECAwEAAQ=="
    });
    internal static bool Configured=>Keys.Count>0;
    // Set only after the isolated real-process acceptance suite has passed and been reviewed.
    internal static bool HelperAcceptanceVerified=>true;
    internal static bool AutomaticReady=>Configured&&HelperAcceptanceVerified;
    internal static string BlockReason=>!Configured?MissingKey:"Automatic installation is disabled until helper acceptance tests are complete. Use manual download.";
    internal const string MissingKey="Automatic installation is disabled: a trusted release signing key has not been configured. Use Download Update for a manual update.";
}

internal sealed record SelfUpdateFile(string Path,long Size,string Sha256);
internal sealed record SelfUpdateManifest(int Schema,string KeyId,string Repository,string Channel,string Platform,string Version,string FileName,long Size,string Sha256,SelfUpdateFile[] Files);
internal sealed record SignedUpdateMetadata(byte[] Manifest,byte[] Signature);
internal sealed record PreparedSelfUpdate(string Session,string Target,string CurrentVersion,string OldSha256,ApplicationRelease Release,SignedUpdateMetadata Signed);

/// <summary>All verification decisions are repeated by the helper; a staged path alone grants nothing.</summary>
internal sealed class SelfUpdateVerifier
{
    internal const string Executable="RenoDX MFG Unlocker.exe";
    private readonly IReadOnlyDictionary<string,string> keys;
    internal bool Configured=>keys.Count>0;
    internal SelfUpdateVerifier():this(SelfUpdateTrust.Keys){}
    internal SelfUpdateVerifier(IReadOnlyDictionary<string,string> keys)=>this.keys=keys;
    internal static readonly string[] AllowedFiles=[Executable,"README.md","THIRD_PARTY_NOTICES.md","CHANGELOG.md","RELEASE_NOTES.md","LICENSES/Microsoft.NETCore.App-LICENSE.txt","LICENSES/Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt","LICENSES/Microsoft.WindowsDesktop.App-LICENSE.txt"];
    private static readonly JsonSerializerOptions JsonOptions=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12,PropertyNameCaseInsensitive=false};
    internal static byte[] Canonical(SelfUpdateManifest manifest) {
        using var stream=new MemoryStream();using(var json=new Utf8JsonWriter(stream)) {
            json.WriteStartObject();json.WriteNumber("Schema",manifest.Schema);json.WriteString("KeyId",manifest.KeyId);json.WriteString("Repository",manifest.Repository);json.WriteString("Channel",manifest.Channel);json.WriteString("Platform",manifest.Platform);json.WriteString("Version",manifest.Version);json.WriteString("FileName",manifest.FileName);json.WriteNumber("Size",manifest.Size);json.WriteString("Sha256",manifest.Sha256);json.WriteStartArray("Files");
            foreach(var file in manifest.Files){json.WriteStartObject();json.WriteString("Path",file.Path);json.WriteNumber("Size",file.Size);json.WriteString("Sha256",file.Sha256);json.WriteEndObject();}json.WriteEndArray();json.WriteEndObject();
        }return stream.ToArray();
    }
    internal SelfUpdateManifest Verify(SignedUpdateMetadata signed,ApplicationRelease release,string installed) {
        if(!Configured)throw new InvalidDataException(SelfUpdateTrust.MissingKey);
        if(signed.Manifest.Length is 0 or >65536||signed.Signature.Length is 0 or >512)throw new InvalidDataException("Invalid signed update metadata size.");
        SelfUpdateManifest manifest;
        try{manifest=JsonSerializer.Deserialize<SelfUpdateManifest>(signed.Manifest,JsonOptions)??throw new InvalidDataException("Missing update manifest.");}
        catch(JsonException ex){throw new InvalidDataException("Invalid update manifest.",ex);}
        if(manifest.Files is null||manifest.Files.Length is 0 or >32||manifest.Files.Any(f=>f is null)||manifest.KeyId is null||!keys.TryGetValue(manifest.KeyId,out var publicKey)||!signed.Manifest.AsSpan().SequenceEqual(Canonical(manifest)))throw new InvalidDataException("Noncanonical or untrusted update manifest.");
        using(var rsa=RSA.Create()) {
            try{var spki=Convert.FromBase64String(publicKey);rsa.ImportSubjectPublicKeyInfo(spki,out var read);if(read!=spki.Length||rsa.KeySize is <3072 or >4096||!rsa.VerifyData(signed.Manifest,signed.Signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("Update signature verification failed.");}
            catch(Exception ex)when(ex is FormatException or CryptographicException){throw new InvalidDataException("Update signature verification failed.",ex);}
        }
        var version=ApplicationVersion.Parse(manifest.Version);var current=ApplicationVersion.Parse(installed);
        if(manifest.Schema!=1||manifest.Repository!=ApplicationUpdateService.Repository||manifest.Channel!="stable"||manifest.Platform!="windows-x64"||version is null||version.Prerelease.Length!=0||manifest.Version.StartsWith('v')||current is null||version.CompareTo(current)<=0||release.Tag.TrimStart('v')!=manifest.Version)throw new InvalidDataException("Update version, channel, platform or repository is not authorized.");
        if(manifest.FileName!="RenoDX_MFG_Unlocker_v"+manifest.Version+"_Windows_x64.zip"||manifest.FileName!=release.AssetName||manifest.Size<=0||manifest.Size>1024L*1024*1024||manifest.Size!=release.Size||!Hash(manifest.Sha256)||!manifest.Sha256.Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Signed update asset does not match official release metadata.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;
        foreach(var file in manifest.Files) {
            if(file is null||!AllowedFiles.Contains(file.Path,StringComparer.Ordinal)||!paths.Add(file.Path)||!Hash(file.Sha256)||file.Size<0||file.Size>512L*1024*1024)throw new InvalidDataException("Unauthorized or duplicate update file.");size=checked(size+file.Size);
        }
        if(size>512L*1024*1024||!paths.Contains(Executable))throw new InvalidDataException("Update file inventory is invalid.");
        return manifest;
    }
    private static bool Hash(string? value)=>value is not null&&Regex.IsMatch(value,@"^[0-9a-f]{64}$",RegexOptions.CultureInvariant);
    internal static string Sha(Stream stream){var position=stream.Position;stream.Position=0;var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();stream.Position=position;return hash;}
    internal static string Sha(string path){using var stream=SelfUpdatePaths.ReadFile(path);return Sha(stream);}
    internal SelfUpdateManifest VerifyPackage(string path,SignedUpdateMetadata signed,ApplicationRelease release,string installed,Action<string,int,int>? progress=null) {
        var manifest=Verify(signed,release,installed);
        using var stream=SelfUpdatePaths.ReadFile(path);
        if(stream.Length!=manifest.Size||Sha(stream)!=manifest.Sha256)throw new InvalidDataException("Signed package size or SHA-256 mismatch.");
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
        if(zip.Entries.Count>48)throw new InvalidDataException("Update ZIP has too many entries.");
        var expected=manifest.Files.ToDictionary(f=>f.Path,StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var done=0;
        foreach(var entry in zip.Entries) {
            // Reject Unix symlinks/special nodes and Windows reparse attributes, even for directories.
            var unix=(entry.ExternalAttributes>>16)&0xF000;
            if(unix is not (0 or 0x8000 or 0x4000)||(entry.ExternalAttributes&0x400)!=0||!seen.Add(entry.FullName)||entry.FullName.Contains('\\')||entry.FullName.Contains(':')||entry.FullName.Split('/').Any(p=>p is "." or ".."))throw new InvalidDataException("Unsafe update ZIP entry.");
            if(entry.FullName=="LICENSES/"&&entry.Length==0)continue;
            if(!expected.TryGetValue(entry.FullName,out var file)||entry.Length!=file.Size||(unix==0x4000)||entry.Length>1024*1024&&entry.Length>Math.Max(1,entry.CompressedLength)*200)throw new InvalidDataException("Unexpected or excessive update ZIP content.");
            using var content=entry.Open();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long actual=0;int read;
            while((read=content.Read(buffer))>0){actual+=read;if(actual>file.Size)throw new InvalidDataException("Update member exceeds its signed size.");hash.AppendData(buffer,0,read);}
            if(actual!=file.Size||Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()!=file.Sha256)throw new InvalidDataException("Update member SHA-256 mismatch.");
            progress?.Invoke("Verifying signed package…",++done,manifest.Files.Length);
        }
        if(expected.Keys.Any(p=>!seen.Contains(p)))throw new InvalidDataException("Signed update file is missing from ZIP.");
        return manifest;
    }
    internal void StageExecutable(string archive,string session,SignedUpdateMetadata signed,ApplicationRelease release,string installed,Action<string,int,int>? progress=null) {
        var manifest=VerifyPackage(archive,signed,release,installed,progress);
        var destination=System.IO.Path.Combine(session,"payload.exe");
        using var lease=SelfUpdatePaths.LockDirectory(session);
        using var source=SelfUpdatePaths.ReadFile(archive);
        if(Sha(source)!=manifest.Sha256)throw new InvalidDataException("Package changed after verification.");
        using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);using var member=zip.GetEntry(Executable)!.Open();
        using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){member.CopyTo(output);output.Flush(true);}
        if(Sha(destination)!=manifest.Files.Single(f=>f.Path==Executable).Sha256)throw new InvalidDataException("Staged executable does not match signed manifest.");
    }
}

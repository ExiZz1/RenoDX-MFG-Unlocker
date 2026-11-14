using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RenoDXMfgUnlocker;

internal static class ApprovedProductionTrustChecks
{
    private const string Owner="owner-release-2026";
    private const string Fingerprint="BE25A13FB00FDE3126CC939C250B4937EE8A5C3D90B9255E8A131B10C1A20BA7";
    internal static IEnumerable<(string Name,Action Run)> All() {
        yield return("approved trust pins exactly the owner's verified RSA4096 SPKI fingerprint",Pin);
        yield return("approved trust rejects unsigned truncated random and modified signatures",InvalidSignatures);
        yield return("approved trust rejects test-key signatures and owner-key-ID impersonation",ForeignSignatures);
        yield return("approved trust positive test signing remains isolated and detects tampering",IsolatedPositive);
        yield return("approved trust refusal cannot create staging or modify target application",BeforeWrites);
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Untrusted update unexpectedly accepted.");}
    private static void Pin(){Check(SelfUpdateTrust.Keys.Count==1&&SelfUpdateTrust.Keys.ContainsKey(Owner),"Unexpected trust catalog");var bytes=Convert.FromBase64String(SelfUpdateTrust.Keys[Owner]);Check(Convert.ToHexString(SHA256.HashData(bytes))==Fingerprint,"Embedded public fingerprint differs from owner approval");using var rsa=RSA.Create();rsa.ImportSubjectPublicKeyInfo(bytes,out var read);Check(read==bytes.Length&&rsa.KeySize==4096&&SelfUpdateTrust.AutomaticReady,"Invalid approved RSA or readiness");Check(!SelfUpdateTrust.Keys.ContainsKey("test-only")&&!SelfUpdateTrust.Keys.ContainsKey("e2e-local-only"),"Test key in production catalog");}
    private sealed class Fixture:IDisposable {
        internal readonly RSA TestKey=RSA.Create(4096);
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"RenoDX-ApprovedPublicTrust",Guid.NewGuid().ToString("N"));
        internal readonly byte[] Payload=Encoding.UTF8.GetBytes("inert owned test fixture; never executed");
        internal readonly string Archive;
        internal SelfUpdateManifest Manifest;
        internal ApplicationRelease Release;
        internal Fixture(){Directory.CreateDirectory(Root);Archive=Path.Combine(Root,"fixture.zip");using(var zip=ZipFile.Open(Archive,ZipArchiveMode.Create)){using var stream=zip.CreateEntry(SelfUpdateVerifier.Executable).Open();stream.Write(Payload);}var size=new FileInfo(Archive).Length;var hash=SelfUpdateVerifier.Sha(Archive);Manifest=new(1,Owner,ApplicationUpdateService.Repository,"stable","windows-x64","1.3.0","RenoDX_MFG_Unlocker_v1.3.0_Windows_x64.zip",size,hash,[new(SelfUpdateVerifier.Executable,Payload.Length,Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant())]);Release=new("v1.3.0","Test","",DateTimeOffset.UtcNow,ApplicationUpdateService.WebRoot+"/releases/tag/v1.3.0",Manifest.FileName,ApplicationUpdateService.WebRoot+"/releases/download/v1.3.0/"+Manifest.FileName,size,hash);}
        internal SignedUpdateMetadata Signed(){var data=SelfUpdateVerifier.Canonical(Manifest);return new(data,TestKey.SignData(data,HashAlgorithmName.SHA256,RSASignaturePadding.Pss));}
        public void Dispose()=>TestKey.Dispose();
    }
    private static void InvalidSignatures(){using var f=new Fixture();var bytes=SelfUpdateVerifier.Canonical(f.Manifest);var verifier=new SelfUpdateVerifier();foreach(var signature in new[]{Array.Empty<byte>(),new byte[1],new byte[512],RandomNumberGenerator.GetBytes(512)})Reject(()=>verifier.Verify(new(bytes,signature),f.Release,"1.2.0-rc.6"));var foreign=f.Signed();foreign.Signature[0]^=1;Reject(()=>verifier.Verify(foreign,f.Release,"1.2.0-rc.6"));}
    private static void ForeignSignatures(){using var f=new Fixture();var verifier=new SelfUpdateVerifier();Reject(()=>verifier.Verify(f.Signed(),f.Release,"1.2.0-rc.6"));foreach(var keyId in new[]{"test-only","e2e-local-only","unapproved","OWNER-RELEASE-2026"}){f.Manifest=f.Manifest with {KeyId=keyId};Reject(()=>verifier.Verify(f.Signed(),f.Release,"1.2.0-rc.6"));}Check(SelfUpdateTrust.Keys.Count==1,"Foreign verification mutated trust");}
    private static void IsolatedPositive(){using var f=new Fixture();f.Manifest=f.Manifest with {KeyId="isolated-positive-only"};var verifier=new SelfUpdateVerifier(new Dictionary<string,string>{{"isolated-positive-only",Convert.ToBase64String(f.TestKey.ExportSubjectPublicKeyInfo())}});var signed=f.Signed();Check(verifier.VerifyPackage(f.Archive,signed,f.Release,"1.2.0-rc.6").Version=="1.3.0","Valid dedicated-test signature refused");Reject(()=>new SelfUpdateVerifier().VerifyPackage(f.Archive,signed,f.Release,"1.2.0-rc.6"));var changed=signed.Manifest.ToArray();changed[^2]^=1;Reject(()=>verifier.Verify(signed with {Manifest=changed},f.Release,"1.2.0-rc.6"));var bytes=File.ReadAllBytes(f.Archive);bytes[0]^=1;File.WriteAllBytes(f.Archive,bytes);Reject(()=>verifier.VerifyPackage(f.Archive,signed,f.Release,"1.2.0-rc.6"));Check(!SelfUpdateTrust.Keys.ContainsKey("isolated-positive-only"),"Test positive trust leaked");}
    private static void BeforeWrites(){using var f=new Fixture();var installation=Path.Combine(f.Root,"installation");Directory.CreateDirectory(installation);var target=Path.Combine(installation,SelfUpdateVerifier.Executable);File.WriteAllText(target,"unchanged installed fixture");var staging=Path.Combine(f.Root,"must-not-exist");Reject(()=>SelfUpdateHandoff.Prepare(new SelfUpdateVerifier(),f.Archive,f.Signed(),f.Release,"1.2.0-rc.6",target,root:staging));Check(!Directory.Exists(staging)&&File.ReadAllText(target)=="unchanged installed fixture","Authentication failure performed file operations");}
}

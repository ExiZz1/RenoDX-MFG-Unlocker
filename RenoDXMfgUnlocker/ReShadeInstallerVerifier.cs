using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

namespace RenoDXMfgUnlocker;

public sealed record ReviewedReShadeInstaller(string Version,string FileName,string Url,long Size,string Sha256,string PublisherThumbprint);
public sealed record PreparedReShadeInstaller(string Path,string Version,string Sha256,long Size);

/// <summary>One reviewed Full Add-on Support package. No standard/new-version fallback and no extraction.</summary>
public sealed class ReShadeInstallerVerifier
{
    public static readonly ReviewedReShadeInstaller Reviewed=ReShadeReviewedCatalog.Current.Installer;
    public PreparedReShadeInstaller Verify(string path)
    {
        path=System.IO.Path.GetFullPath(path);StreamlineStagingEvidenceService.EnsureNoReparsePoints(System.IO.Path.GetPathRoot(path)!,path);
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length!=Reviewed.Size)throw new InvalidDataException("ReShade installer size does not match the reviewed Full Add-on Support package.");
        var bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(Reviewed.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("ReShade installer SHA-256 does not match the reviewed Full Add-on Support package.");
        VerifyPinnedSignature(bytes,Reviewed.PublisherThumbprint);
        var version=FileVersionInfo.GetVersionInfo(path);
        if(version.ProductName!="ReShade"||TechnicalVersionDisplay.Format(version.FileVersion)!=ReShadeReviewedCatalog.Current.InstallerFileVersion)throw new InvalidDataException("ReShade installer identity is not the reviewed version.");
        return new(path,Reviewed.Version,Reviewed.Sha256,Reviewed.Size);
    }
    internal static void VerifyPinnedSignature(byte[] bytes,string publisherThumbprint)
    {
        try
        {
            using var pe=new PEReader(new MemoryStream(bytes,false));var header=pe.PEHeaders.PEHeader??throw new InvalidDataException("Missing installer PE header.");
            var optional=checked(BitConverter.ToInt32(bytes,0x3c)+24);var checksum=checked(optional+64);var directory=checked(optional+(header.Magic==PEMagic.PE32Plus?112:96)+32);
            var cert=header.CertificateTableDirectory;var offset=cert.RelativeVirtualAddress;var size=cert.Size;
            if(checksum<0||directory<checksum+4||offset<directory+8||size<8||checked(offset+size)!=bytes.Length||offset%8!=0)throw new InvalidDataException("Unsupported Authenticode certificate layout.");
            var length=BitConverter.ToInt32(bytes,offset);
            if(length<8||length>size||((length+7)&~7)!=size||BitConverter.ToUInt16(bytes,offset+4)!=0x200||BitConverter.ToUInt16(bytes,offset+6)!=2)throw new InvalidDataException("Invalid Authenticode certificate table.");
            var cms=new SignedCms();cms.Decode(bytes.AsSpan(offset+8,length-8).ToArray());
            if(cms.ContentInfo.ContentType.Value!="1.3.6.1.4.1.311.2.1.4"||cms.SignerInfos.Count!=1)throw new InvalidDataException("Unsupported Authenticode signature.");
            var signer=cms.SignerInfos[0].Certificate??throw new InvalidDataException("Missing installer signer.");
            if(!signer.Thumbprint.Equals(publisherThumbprint,StringComparison.OrdinalIgnoreCase)||DateTime.UtcNow<signer.NotBefore.ToUniversalTime()||DateTime.UtcNow>signer.NotAfter.ToUniversalTime())throw new InvalidDataException("Installer publisher does not match the reviewed official certificate.");
            // Deliberately verify against the official publisher pin, not the Windows CA store.
            // The official self-signed certificate is not silently installed as a trusted Windows root.
            cms.CheckSignature(verifySignatureOnly:true);
            var reader=new AsnReader(cms.ContentInfo.Content,AsnEncodingRules.DER);var indirect=reader.ReadSequence();indirect.ReadEncodedValue();var digest=indirect.ReadSequence();var algorithm=digest.ReadSequence();var oid=algorithm.ReadObjectIdentifier();while(algorithm.HasData)algorithm.ReadEncodedValue();var expected=digest.ReadOctetString();digest.ThrowIfNotEmpty();indirect.ThrowIfNotEmpty();reader.ThrowIfNotEmpty();
            var name=oid switch{"1.3.14.3.2.26"=>HashAlgorithmName.SHA1,"2.16.840.1.101.3.4.2.1"=>HashAlgorithmName.SHA256,_=>throw new InvalidDataException("Unsupported installer content-digest algorithm.")};
            using var hash=IncrementalHash.CreateHash(name);
            // Reviewed package uses contiguous PE sections and its certificate table is last.
            // Hash every byte except CheckSum, certificate-directory entry and certificate table.
            var end=header.SizeOfHeaders;foreach(var section in pe.PEHeaders.SectionHeaders.OrderBy(s=>s.PointerToRawData))if(section.SizeOfRawData>0){if(section.PointerToRawData!=end)throw new InvalidDataException("Unsupported non-contiguous PE sections.");end=checked(end+section.SizeOfRawData);}
            if(end>offset)throw new InvalidDataException("Certificate overlaps installer data.");
            hash.AppendData(bytes.AsSpan(0,checksum));hash.AppendData(bytes.AsSpan(checksum+4,directory-checksum-4));hash.AppendData(bytes.AsSpan(directory+8,offset-directory-8));
            if(!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),expected))throw new InvalidDataException("Installer Authenticode content digest is invalid.");
        }
        catch(Exception ex)when(ex is CryptographicException or AsnContentException or BadImageFormatException or OverflowException or ArgumentOutOfRangeException){throw new InvalidDataException("Installer signature could not be verified.",ex);}
    }
}

using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace RenoDXMfgUnlocker;

public sealed record VerifiedDlssGProvider(string Path, string Version, long SizeBytes, string Sha256, string Signer);
public sealed class DlssGProviderVerifier
{
    public VerifiedDlssGProvider Verify(string path)
    {
        path=System.IO.Path.GetFullPath(path);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(System.IO.Path.GetPathRoot(path)!,path);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length<4096 || stream.Length>128L*1024*1024) throw new InvalidDataException("Unexpected DLSS-G file size.");
        using(var pe=new PEReader(stream,PEStreamOptions.LeaveOpen))
            if(pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64 || pe.PEHeaders.PEHeader?.Magic!=PEMagic.PE32Plus
                || pe.PEHeaders.CorHeader is not null || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll)==0)
                throw new InvalidDataException("DLSS-G must be a native x64 Windows DLL.");
        var info=FileVersionInfo.GetVersionInfo(path);
        var version=BuiltInGameCompatibilityProfileCatalog.NormalizeVersion(info.FileVersion);
        if(version is not ("310.9.1.0" or "310.9.16.0")
            || !(info.ProductName?.Contains("DLSS-G",StringComparison.OrdinalIgnoreCase)??false))
            throw new InvalidDataException("Only NVIDIA DLSS-G 310.9.1.0 and 310.9.16.0 providers are supported.");
        VerifyAuthenticode(path);
        using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        var signer=certificate.GetNameInfo(X509NameType.SimpleName,false);
        if(!signer.Equals("NVIDIA Corporation",StringComparison.Ordinal))
            throw new InvalidDataException("The trusted signature is not from NVIDIA Corporation.");
        stream.Position=0;
        return new(path,version,stream.Length,Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),signer);
    }
    public VerifiedDlssGProvider Revalidate(VerifiedDlssGProvider snapshot)
    {
        var current=Verify(snapshot.Path);
        if(current!=snapshot) throw new InvalidDataException("Imported DLSS-G changed since selection. Import it again.");
        return current;
    }
    internal static void VerifyAuthenticode(string path)
    {
        var file=new TrustFile {Size=(uint)Marshal.SizeOf<TrustFile>(),Path=path};
        var ptr=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(file,ptr,false);
        var data=new TrustData {Size=(uint)Marshal.SizeOf<TrustData>(),UI=2,Revocation=1,Choice=1,File=ptr,StateAction=1,Flags=0x80};
        var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try {
            var status=WinVerifyTrust(new IntPtr(-1),ref action,ref data);
            if(status!=0) throw new InvalidDataException($"Windows could not verify the DLL signature/revocation status (0x{status:X8}). No trust bypass is offered.");
        } finally {
            data.StateAction=2; WinVerifyTrust(new IntPtr(-1),ref action,ref data);
            Marshal.DestroyStructure<TrustFile>(ptr); Marshal.FreeHGlobal(ptr);
        }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle,KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData {
        public uint Size; public IntPtr Policy,Sip; public uint UI,Revocation,Choice;
        public IntPtr File; public uint StateAction; public IntPtr State,Url; public uint Flags,Context; public IntPtr Settings;
    }
    [DllImport("wintrust.dll",ExactSpelling=true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
}

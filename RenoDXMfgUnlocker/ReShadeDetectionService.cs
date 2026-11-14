using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace RenoDXMfgUnlocker;

public enum ReShadeGraphicsApi { Unknown,Direct3D11,Direct3D12,Vulkan }
public enum ReShadeDetectionState { NotFound,DetectedUnknownSupport,FullAddonVerified,StandardVerified,Conflict,NotVerified }
public enum ReShadeProxyKind { IdentityNotVerified,FullAddonVerified,RecognizedReShade,StandardVerified,IdentifiedThirdParty,UnknownThirdParty }
public sealed record ReShadeFileEvidence(string Path,string Version,string Sha256,bool IsReShade,bool IsNativeX64Dll,bool ReviewedStandard=false,
    string ProductName="",string FileDescription="",ReShadeProxyKind Kind=ReShadeProxyKind.IdentityNotVerified,string Reason="",bool BlocksGuidedSetup=false);
public sealed record ReShadeDetectionReport(ReShadeDetectionState State,IReadOnlyList<ReShadeFileEvidence> Files,IReadOnlyList<string> Warnings,IReadOnlyList<string> AlternativeAddonPaths,bool VulkanRegistrationVerified=false)
{
    public bool HasConflicts=>State==ReShadeDetectionState.Conflict;
    public bool CriticalInspectionFailure { get; init; }
    public string ConfigurationSha256 { get; init; } = "";
}

/// <summary>Bounded read-only inventory. Product names/exports identify candidates, never the full edition.</summary>
public sealed class ReShadeDetectionService
{
    private static readonly string[] Proxies=["dxgi.dll","d3d11.dll","d3d12.dll","d3d9.dll","d3d10.dll","opengl32.dll","ReShade64.dll"];
    // Populated only from an observed normal official installation, never from filenames/version strings.
    internal static readonly IReadOnlyDictionary<string,ReShadeDetectionState> ReviewedRuntimeHashes=ReShadeReviewedCatalog.RuntimeHashes;
    private readonly Func<string,ReShadeFileEvidence> inspect;
    private readonly IReadOnlyDictionary<string,ReShadeDetectionState> hashes;
    private readonly string vulkanModule;
    public ReShadeDetectionService():this(ReadEvidence,ReviewedRuntimeHashes,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ReShade","ReShade64","ReShade64.dll")){}
    internal ReShadeDetectionService(Func<string,ReShadeFileEvidence> inspect,IReadOnlyDictionary<string,ReShadeDetectionState> hashes,string vulkanModule){this.inspect=inspect;this.hashes=hashes;this.vulkanModule=vulkanModule;}
    public ReShadeDetectionReport Scan(string gameRoot,string executable,ReShadeGraphicsApi api)
    {
        var directory=Path.GetDirectoryName(Path.GetFullPath(executable))!;RuntimeTargetPolicy.Validate(gameRoot,executable,directory,false);
        var files=new List<ReShadeFileEvidence>();var warnings=new List<string>();var alternatives=new List<string>();bool conflict=false,critical=false;var configurationHash="";
        foreach(var path in Proxies.Select(n=>Path.Combine(directory,n)))
        {
            try{StreamlineStagingEvidenceService.EnsureNoReparsePoints(gameRoot,path);if(!File.Exists(path))continue;var evidence=Classify(inspect(path));files.Add(evidence);if(evidence.BlocksGuidedSetup)conflict=true;}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){conflict=critical=true;files.Add(new(path,"Unknown version","",false,false,Reason:"File identity could not be checked. Review Details before using official setup.",BlocksGuidedSetup:true));warnings.Add(Path.GetFileName(path)+": "+ex.Message);}
        }
        var localReShade=files.Count(f=>f.IsReShade);if(localReShade>1){conflict=true;for(var i=0;i<files.Count;i++)if(files[i].IsReShade)files[i]=files[i] with {BlocksGuidedSetup=true,Reason=files[i].Reason+" Potential overlapping ReShade proxies; the installation chain is not verified."};}
        var config=Path.Combine(directory,"ReShade.ini");
        try
        {
            StreamlineStagingEvidenceService.EnsureNoReparsePoints(gameRoot,config);
            if(File.Exists(config))
            {
                if(new FileInfo(config).Length>262144){conflict=critical=true;warnings.Add("ReShade.ini exceeds the read-only inspection limit.");}
                else
                {
                    configurationHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config)));
                    var section="";foreach(var source in File.ReadAllLines(config))
                    {
                        var line=source.Trim();if(line.StartsWith(';')||line.StartsWith('#'))continue;
                        if(line.StartsWith('[')&&line.EndsWith(']')){section=line[1..^1];continue;}
                        var equal=line.IndexOf('=');if(equal<=0)continue;var key=line[..equal].Trim();
                        if(section.Equals("ADDON",StringComparison.OrdinalIgnoreCase)&&key.Equals("AddonPath",StringComparison.OrdinalIgnoreCase)||section.Equals("INSTALL",StringComparison.OrdinalIgnoreCase)&&key.Equals("BasePath",StringComparison.OrdinalIgnoreCase))alternatives.Add("["+section+"] "+key+"="+line[(equal+1)..].Trim());
                    }
                }
            }
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){conflict=critical=true;warnings.Add("ReShade.ini: "+ex.Message);}
        if(api==ReShadeGraphicsApi.Vulkan)
        {
            try{StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(vulkanModule)!,vulkanModule);if(File.Exists(vulkanModule)){var file=Classify(inspect(vulkanModule));files.Add(file);if(file.BlocksGuidedSetup)conflict=true;}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){conflict=critical=true;warnings.Add("Vulkan layer: "+ex.Message);}
        }
        ReShadeDetectionReport Report(ReShadeDetectionState value)=>new(value,files,warnings,alternatives){CriticalInspectionFailure=critical,ConfigurationSha256=configurationHash};
        if(conflict)return Report(ReShadeDetectionState.Conflict);
        if(api==ReShadeGraphicsApi.Vulkan)return Report(ReShadeDetectionState.NotVerified);
        var identified=files.Where(f=>f.IsReShade&&f.IsNativeX64Dll).ToArray();
        var state=identified.Length==0?ReShadeDetectionState.NotFound:hashes.GetValueOrDefault(identified[0].Sha256,identified[0].ReviewedStandard?ReShadeDetectionState.StandardVerified:ReShadeDetectionState.DetectedUnknownSupport);
        return Report(state);
    }
    internal ReShadeFileEvidence Classify(ReShadeFileEvidence file)
    {
        // Ignore any incoming display classification; derive it from observed evidence again.
        if(!file.IsNativeX64Dll)return file with {Kind=ReShadeProxyKind.IdentityNotVerified,BlocksGuidedSetup=true,Reason="File identity is not verified as a native x64 proxy. Safe replacement is not established."};
        if(file.IsReShade)
        {
            if(hashes.GetValueOrDefault(file.Sha256)==ReShadeDetectionState.FullAddonVerified)return file with {Kind=ReShadeProxyKind.FullAddonVerified,BlocksGuidedSetup=false,Reason="Exact reviewed Full Add-on Support SHA-256 matched. This does not prove runtime loading."};
            if(file.ReviewedStandard)return file with {Kind=ReShadeProxyKind.StandardVerified,BlocksGuidedSetup=false,Reason="Reviewed signed Standard ReShade identified. Full add-on loading is unavailable."};
            return file with {Kind=ReShadeProxyKind.RecognizedReShade,BlocksGuidedSetup=false,Reason="ReShade product metadata and export identified; Full Add-on Support is not verified."};
        }
        // Resource strings can be copied/spoofed. Identify the declared product, never trust it as compatibility/authenticity.
        var scs=(file.ProductName.Equals("SCSKiller",StringComparison.OrdinalIgnoreCase)||file.ProductName.Equals("SCSKiller D3D12 proxy",StringComparison.OrdinalIgnoreCase))
            &&file.FileDescription.Equals("SCSKiller D3D12 proxy",StringComparison.OrdinalIgnoreCase);
        return scs?file with {Kind=ReShadeProxyKind.IdentifiedThirdParty,BlocksGuidedSetup=true,Reason="Product metadata identifies SCSKiller D3D12 proxy. Publisher authenticity and compatibility are not independently verified."}
            :file with {Kind=ReShadeProxyKind.UnknownThirdParty,BlocksGuidedSetup=true,Reason="Unknown third-party proxy. Ownership and the proposed installation chain are not verified."};
    }
    internal static ReShadeFileEvidence ReadEvidence(string path)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(input.Length>64*1024*1024)throw new InvalidDataException("Proxy DLL exceeds inspection limit.");
        var hash=Convert.ToHexString(SHA256.HashData(input));input.Position=0;
        try
        {
            using var pe=new PEReader(input,PEStreamOptions.LeaveOpen);var native=pe.PEHeaders.CoffHeader.Machine==Machine.Amd64&&pe.PEHeaders.CorHeader is null&&(pe.PEHeaders.CoffHeader.Characteristics&Characteristics.Dll)!=0;
            var info=FileVersionInfo.GetVersionInfo(path);bool exported=false;
            if(pe.PEHeaders.PEHeader?.ExportTableDirectory is {Size:>0} export)
            {
                var table=pe.GetSectionData(export.RelativeVirtualAddress).GetContent(0,40);var count=BitConverter.ToInt32(table.AsSpan(24,4));var rva=BitConverter.ToInt32(table.AsSpan(32,4));
                if(count<0||count>8192)throw new InvalidDataException("Invalid PE export count.");
                var names=pe.GetSectionData(rva).GetContent(0,checked(count*4));for(var i=0;i<count;i++){var address=BitConverter.ToInt32(names.AsSpan(i*4,4));var name=pe.GetSectionData(address).GetContent();var bound=Math.Min(name.Length,128);var zero=name.AsSpan(0,bound).IndexOf((byte)0);if(zero>=0&&Encoding.ASCII.GetString(name.AsSpan(0,zero))=="ReShadeVersion"){exported=true;break;}}
            }
            var reshade=info.ProductName=="ReShade"&&exported;bool standard=false;
            // Only the reviewed v6.8.0 Release Signed configuration is known to use RESHADE_ADDON=1.
            // Signature cryptography + pinned publisher + reviewed version, never a filename/version alone.
            if(reshade&&native&&TechnicalVersionDisplay.Format(info.FileVersion)=="6.8.0.2155"&&!ReviewedRuntimeHashes.ContainsKey(hash))
            {
                try{ReShadeInstallerVerifier.VerifyPinnedSignature(pe.GetEntireImage().GetContent().ToArray(),ReShadeInstallerVerifier.Reviewed.PublisherThumbprint);standard=true;}
                catch(InvalidDataException){}
            }
            return new(path,info.FileVersion??"Unknown version",hash,reshade,native,standard,info.ProductName??"",info.FileDescription??"");
        }
        catch(Exception ex)when(ex is BadImageFormatException or ArgumentOutOfRangeException or OverflowException){return new(path,"Unknown version",hash,false,false);}
    }
}

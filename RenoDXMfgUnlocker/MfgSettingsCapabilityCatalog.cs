namespace RenoDXMfgUnlocker;

/// <summary>Stable versions inherit capabilities from their introduction version; explicit exclusions can override them.</summary>
internal static class MfgSettingsCapabilityCatalog
{
    internal sealed record Schema(string Id,bool Main,bool XeFg,bool GenerationMethods=false,bool IntelD3D12=false,bool Vulkan=false,bool NativeLog=false);
    internal enum Feature { MainSettings, XeFgSettings, GenerationMethods, Rtx50Settings, IntelD3D12, Vulkan, NativeLog, DynamicMfgXmx }
    // Inclusive lower / exclusive upper bound. A null upper bound blocks all later
    // versions until a future compatibility correction updates the rule.
    internal sealed record Exclusion(Version From,Version? Until,string Reason);
    internal sealed record Rule(Version Minimum,IReadOnlyList<Exclusion> Exclusions);
    private static readonly IReadOnlyDictionary<Feature,Rule> Rules=new Dictionary<Feature,Rule>{
        [Feature.MainSettings]=new(new(1,6,0,0),[]),
        [Feature.XeFgSettings]=new(new(1,7,0,0),[]),
        [Feature.GenerationMethods]=new(new(1,8,0,0),[]),
        [Feature.Rtx50Settings]=new(new(1,7,0,0),[]),
        [Feature.IntelD3D12]=new(new(1,5,8,0),[]),
        [Feature.Vulkan]=new(new(1,7,0,0),[]),
        [Feature.NativeLog]=new(new(1,5,8,0),[]),
        [Feature.DynamicMfgXmx]=new(new(1,8,3,0),[])
    };
    internal static bool TryStableVersion(string? value,out Version parsed){
        parsed=new(0,0,0,0);
        if(value is null||value.Length>64)return false;
        var text=value.Trim();
        if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"\A[0-9]+(?<s>[.,])[0-9]+\k<s>[0-9]+(?:\k<s>[0-9]+)?\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50))
            ||!Version.TryParse(text.Replace(',','.'),out var numeric))return false;
        parsed=new(numeric.Major,numeric.Minor,numeric.Build,Math.Max(0,numeric.Revision));return true;
    }
    internal static bool Supports(string? version,Rule rule)=>TryStableVersion(version,out var parsed)&&parsed>=rule.Minimum&&!rule.Exclusions.Any(e=>parsed>=e.From&&(e.Until is null||parsed<e.Until));
    internal static bool Supports(string? version,Feature feature)=>Rules.TryGetValue(feature,out var rule)&&Supports(version,rule);
    internal static Schema? Find(string? version){
        if(!TryStableVersion(version,out _))return null;
        bool Has(Feature feature)=>Supports(version,feature);
        if(!Has(Feature.MainSettings)&&!Has(Feature.IntelD3D12)&&!Has(Feature.NativeLog))return null;
        return new(Has(Feature.GenerationMethods)?"mfg-1.8":Has(Feature.XeFgSettings)?"mfg-1.7":Has(Feature.MainSettings)?"mfg-1.6":"mfg-1.5.8",
            Has(Feature.MainSettings),Has(Feature.XeFgSettings),Has(Feature.GenerationMethods),Has(Feature.IntelD3D12),Has(Feature.Vulkan),Has(Feature.NativeLog));
    }
    internal static bool SupportsGenerationMethods(string? version)=>Find(version) is {XeFg:true,GenerationMethods:true};
    internal static bool Displays17Keys(string? version)=>Supports(version,Feature.Rtx50Settings);
    internal static bool CanWrite(string? version,bool xeFg)=>Find(version) is {} schema&&(xeFg?schema.XeFg:schema.Main);
}

using System.Text.Json;

namespace RenoDXMfgUnlocker;

/// <summary>Review classification only: never extends a version scope or grants write authority.
/// Both releases still pass the complete source client, finite parser, catalog validator and explicit approval.</summary>
internal static class MfgUpstreamApprovalReview
{
    internal const string New="New declaration candidate";
    internal const string Unchanged="Unchanged declaration — confirmation required for this release";
    internal const string Changed="Changed declaration — full metadata review required";
    internal sealed record Item(UpstreamSettingFinding Finding,string Kind);
    internal static IReadOnlyList<Item> Compare(UpstreamSettingsAnalysis current,MfgDynamicSettingsCatalog baseline,UpstreamSourceSnapshot? approved,UpstreamSettingsAnalysis? approvedAnalysis=null)
    {
        var previous=approved is null?null:approvedAnalysis??MfgUpstreamSettingsAnalyzer.Analyze(approved);
        var candidate=baseline.WithUpstream(current);
        var sourceIds=new[]{false,true}.SelectMany(x=>new MfgUnlockSettingsService(x,candidate).DefinitionsFor(current.Snapshot.Tag))
            .Where(d=>d.IsUpstreamSource).Select(d=>d.CatalogEntry!.File+"|"+d.Section+"|"+d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return current.Editable.Where(f=>sourceIds.Contains(f.Id)).Select(f=>{
            var old=previous?.Findings.FirstOrDefault(p=>p.Id.Equals(f.Id,StringComparison.OrdinalIgnoreCase))
                ??previous?.Findings.FirstOrDefault(p=>p.Key.Equals(f.Key,StringComparison.OrdinalIgnoreCase));
            var kind=old?.Definition is null?New:previous!.SchemaRecognized&&current.SchemaRecognized&&old.Source==f.Source&&Contract(old)==Contract(f)?Unchanged:Changed;
            return new Item(f,kind);
        }).ToArray();
    }
    // Release scope and evidence URL necessarily change; all declaration/write-contract data must agree.
    // Parsed framework identity is separately checked above against the same embedded reviewed contract.
    private static string Contract(UpstreamSettingFinding f)
    {
        var e=f.Definition!;
        return JsonSerializer.Serialize(new{e.File,e.Section,e.Key,e.Type,e.Default,e.Values,e.Minimum,e.Maximum,e.Requires,e.Exclusions,e.RequiresRestart,e.Category,DefinitionDescription=e.Description,f.Label,SourceDescription=f.Description});
    }
}

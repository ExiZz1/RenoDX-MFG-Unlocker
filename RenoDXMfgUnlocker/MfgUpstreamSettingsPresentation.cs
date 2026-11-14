using System.Globalization;

namespace RenoDXMfgUnlocker;

/// <summary>Localizes RenoDX analysis messages only. Keys, constraints, evidence and upstream quotations remain opaque data.</summary>
internal static class MfgUpstreamSettingsPresentation
{
    internal static string Render(UpstreamSettingsAnalysis? analysis,MfgDynamicSettingsCatalog baseline,UpstreamSourceSnapshot? approved,
        IReadOnlyList<UpstreamSettingChange> changes,UiLocalization language,UpstreamSettingsAnalysis? approvedAnalysis=null)
    {
        if(analysis is null)return language.Lookup("No upstream source has been checked.");
        var review=MfgUpstreamApprovalReview.Compare(analysis,baseline,approved,approvedAnalysis).ToDictionary(i=>i.Finding.Id,i=>i.Kind,StringComparer.OrdinalIgnoreCase);
        string Finding(UpstreamSettingFinding f)
        {
            var result="["+f.Section+"] "+f.Key+" · "+language.Lookup(f.Definition?.Type??"Read-only")+"\n"
                +language.Lookup(review.GetValueOrDefault(f.Id)??MfgUpstreamSettingsAnalyzer.CompareKnown(f,baseline,analysis.Snapshot.Tag));
            if(f.Definition is {} d){
                var values=d.Type=="Integer"?d.Minimum?.ToString(CultureInfo.InvariantCulture)+" … "+d.Maximum?.ToString(CultureInfo.InvariantCulture)
                    :string.Join(" / ",d.Type=="Boolean"?new[]{"0","1"}:d.Values??[]);
                result+="\n"+language.Format("Default: {0} · Values: {1} · Confirmed release: {2}",d.Default,values,d.MinimumAddon);
            }
            result+="\n"+language.Lookup(f.Reason);
            if(!string.IsNullOrEmpty(f.Description))result+="\n"+language.Lookup("Upstream description (original language)")+": "+f.Description;
            return result+"\n"+MfgUpstreamSettingsAnalyzer.Evidence(analysis.Snapshot.Commit,f.Source,f.Line);
        }
        return analysis.Snapshot.Tag+" · "+analysis.Snapshot.Commit+"\n"+language.Lookup("Analysis results")+"\n"
            +string.Join("\n\n",analysis.Findings.Select(Finding))
            +(changes.Count==0?"":"\n\n"+language.Lookup("Changes since the approved source snapshot:")+"\n"+string.Join("\n",changes.Select(d=>d.Id+" · "+language.Lookup(d.Kind))))
            +(approved is null?"":"\n\n"+language.Lookup("Approved source snapshot:")+" "+approved.Tag+" · "+approved.Commit);
    }
}

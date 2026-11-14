using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;
internal enum ReShadeVersionRelation { NotDetected,Current,Older,Newer,Unknown,Mixed }
internal sealed record ReShadeUpdateAssessment(ReShadeVersionRelation Relation,IReadOnlyList<string> InstalledVersions,string AvailableVersion,string NextAction);

internal static class ReShadeUpdatePolicy
{
    internal static ReShadeUpdateAssessment Assess(ReShadeDetectionReport? report)
    {
        var available=ReShadeInstallerVerifier.Reviewed.Version;
        var found=report?.Files.Where(f=>f.IsReShade&&f.IsNativeX64Dll).ToArray()??[];
        var versions=found.Select(f=>TechnicalVersionDisplay.Format(f.Version)).Distinct(StringComparer.Ordinal).ToArray();
        var relation=report is null?ReShadeVersionRelation.Unknown:found.Length==0?ReShadeVersionRelation.NotDetected:ReShadeVersionRelation.Unknown;
        // ReShade's DLL revision (e.g. 2155) is a file build, not the installer release patch.
        // Compare numeric release major.minor.patch only; preserve the complete original display value.
        static Version? Release(string text)=>Regex.IsMatch(text,@"^\d+\.\d+\.\d+(?:\.\d+)?$",RegexOptions.CultureInvariant)&&Version.TryParse(text,out var v)?new(v.Major,v.Minor,v.Build):null;
        var target=Release(available)!;
        if(found.Length>0)
        {
            var releases=versions.Select(Release).ToArray();
            if(releases.All(v=>v is not null))
            {
                var comparisons=releases.Select(v=>Math.Sign(v!.CompareTo(target))).Distinct().ToArray();
                relation=comparisons.Length>1?ReShadeVersionRelation.Mixed:comparisons[0] switch{0=>ReShadeVersionRelation.Current,<0=>ReShadeVersionRelation.Older,_=>ReShadeVersionRelation.Newer};
            }
        }
        var action=report is null?"Scan the selected game before choosing an update.":ReShadeManualLaunchPolicy.HasCriticalBlock(report)?ReShadeManualLaunchPolicy.Blocked:ReShadeManualLaunchPolicy.RequiresConsent(report)?ReShadeManualLaunchPolicy.Warning:relation switch
        {
            ReShadeVersionRelation.Current=>"Same release detected. No update is indicated by version; use official setup only for reviewed repair/reinstallation.",
            ReShadeVersionRelation.Older=>"A newer reviewed installer is available. A guided official update may be reviewed; addon compatibility is not established.",
            ReShadeVersionRelation.Newer=>"Installed release is newer than our reviewed installer. No automatic downgrade is offered.",
            ReShadeVersionRelation.NotDetected=>"No ReShade component detected. Review official installation requirements.",
            _=>"Release relationship is unknown or mixed. Review Details; no automatic version choice is made."
        };
        return new(relation,versions,available,action);
    }
    internal static string Describe(ReShadeDetectionReport? report,UiLocalization language)
    {
        var a=Assess(report);var relation=a.Relation switch{ReShadeVersionRelation.Current=>"Current release number",ReShadeVersionRelation.Older=>"Older release",ReShadeVersionRelation.Newer=>"Newer than reviewed release",ReShadeVersionRelation.NotDetected=>"Not detected",ReShadeVersionRelation.Mixed=>"Mixed release versions",_=>"Unknown version"};
        return language.Format("Installed ReShade: {0}",a.InstalledVersions.Count==0?language.Lookup("Unknown"):string.Join(" · ",a.InstalledVersions))+"\n"+language.Format("Available reviewed installer: {0}",a.AvailableVersion)+" · "+language.Lookup(relation)+"\n"+language.Lookup(a.NextAction);
    }
    internal static string EvidenceLevels(ReShadeDetectionReport? report,UiLocalization language)
    {
        var verified=report?.Files.Count(f=>f.Kind==ReShadeProxyKind.FullAddonVerified)??0;
        return (verified>0?language.Format("Full Add-on component identity: {0} verified file(s).",verified):language.Lookup("Full Add-on component identity: no verified component recorded."))+"\n"+
            language.Lookup(report?.HasConflicts==true||report?.AlternativeAddonPaths.Count>0?"Complete installation structure: requires review; proxy/configuration checks are unresolved.":"Complete installation structure: not independently verified.")+"\n"+
            language.Lookup("Runtime module loaded: not checked by this file scan.")+"\n"+language.Lookup("Addon operation in game: not checked by this file scan.");
    }
    internal static string LocalizeAppText(string text,UiLocalization language)=>string.Join("\n",text.Replace("\r\n","\n").Split('\n').Select(line=>line.StartsWith("Reason: ",StringComparison.Ordinal)?language.Lookup("Reason")+": "+language.Translate(line[8..]):language.Translate(line)));
    internal static string CompactSummary(ReShadeDetectionReport? report,UiLocalization language,bool targetPathSafe=true)
    {
        var a=Assess(report);var versions=a.InstalledVersions.Count==0?language.Lookup(report is null?"Not checked":"Not detected"):string.Join(" · ",a.InstalledVersions);
        var blocked=!targetPathSafe||report is not null&&ReShadeManualLaunchPolicy.HasCriticalBlock(report);
        var count=report?.Files.Count(f=>f.Kind==ReShadeProxyKind.FullAddonVerified)??0;
        return language.Format("Installed ReShade: {0}",versions)+" · "+language.Format("Available reviewed installer: {0}",a.AvailableVersion)+"\n"+
            (report is null?language.Lookup("Scan the selected game before choosing an update."):language.Format("Verified Full Add-on files: {0} · Setup: {1}",count,language.Lookup(blocked?"Blocked":"Manual review required"))+" · "+language.Lookup("Runtime module loaded: not checked by this file scan."));
    }
    internal static string LaunchLabel(ReShadeDetectionReport? report)
    {
        if(report is null)return "Run official guided installer";
        if(ReShadeManualLaunchPolicy.HasCriticalBlock(report))return "Setup blocked — review conflicts";
        if(ReShadeManualLaunchPolicy.RequiresConsent(report))return "Open official setup for review";
        return Assess(report).Relation switch{ReShadeVersionRelation.Current=>"Open official repair / reinstall",ReShadeVersionRelation.Older=>"Open official update review",ReShadeVersionRelation.NotDetected=>"Open official installation review",_=>"Open official setup for review"};
    }
}

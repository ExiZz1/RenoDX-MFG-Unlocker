using System.IO;
using System.Text;
using System.Text.Json;
using RenoDXMfgUnlocker;

internal static class Upstream185Checks
{
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or IOException or InvalidOperationException){return;}throw new Exception("Unsafe write accepted");}
    private static MfgDynamicSettingsCatalog Baseline=>MfgDynamicSettingsCatalog.ReviewedFallback();
    private static UpstreamSourceSnapshot Real(string version="1.8.5")=>Dev4UpstreamChecks.Snapshot(version);
    private static UpstreamSettingsAnalysis Analyze(UpstreamSourceSnapshot snapshot)=>MfgUpstreamSettingsAnalyzer.Analyze(snapshot);
    private static IReadOnlyList<MfgUpstreamApprovalReview.Item> Review(UpstreamSourceSnapshot before,UpstreamSourceSnapshot after)=>MfgUpstreamApprovalReview.Compare(Analyze(after),Baseline,before);
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("upstream185 real GenericGate is unchanged reconfirmation not a new parameter",RealReconfirmation);
        yield return("upstream185 arbitrary future 1.8.6 and 1.9.0 names reconfirm only equal complete declarations",Future);
        yield return("upstream185 changed type default range dependencies file and writer never count as unchanged",Changed);
        yield return("upstream185 prior approval cannot grant a new release write and confirmed scope saves restores exactly",Roundtrip);
        yield return("upstream185 DynamicMaxMultiplier metadata is reviewed but custom ReShade readers stay read-only",ReShade);
        yield return("upstream185 all 39 protected definitions signed R2 and merged semantic groups remain intact",Preservation);
        yield return("upstream185 eight-language new unchanged changed review distinguishes approval from write authority",Presentation);
    }
    private static void RealReconfirmation()
    {
        var old=Real("1.8.4");var current=Real();Check(current.Commit=="22415f1ba9830719cc7f588d6e04a43e08753897","Wrong release fixture");
        var items=Review(old,current);Check(items.Count==1&&items[0].Finding.Key=="GenericGate"&&items[0].Kind==MfgUpstreamApprovalReview.Unchanged,"GenericGate called new or changed");
        Check(items[0].Finding.Definition is {Default:"1",Type:"Boolean",MinimumAddon:"1.8.5"},"Fresh scope/metadata missing");
        var parsed=Analyze(current);Check(parsed.SchemaRecognized&&parsed.Editable.Count==29,"1.8.5 declarations no longer recognized");
    }
    private static UpstreamSourceSnapshot FutureSource(string tag,string name="FutureBudget",string fallback="4",string number="MFGUNLOCK_NUMBER(0,100)")
    {
        var s=Real();var files=new Dictionary<string,string>(s.Files);var entry="{L\"Diagnostics\",L\""+name+"\",\"Synthetic budget\",\"Synthetic future declaration, not an upstream capability.\",L\""+fallback+"\","+number+"},";
        files[MfgUpstreamSettingsAnalyzer.OptionPath]=files[MfgUpstreamSettingsAnalyzer.OptionPath].Replace("inline constexpr Option kDiagnostics[]{","inline constexpr Option kDiagnostics[]{\n"+entry,StringComparison.Ordinal);
        return new(tag,tag=="1.8.6"?new string('a',40):new string('b',40),files);
    }
    private static void Future()
    {
        foreach(var name in new[]{"FutureBudget","AnotherSettingName"}){
            var a=FutureSource("1.8.6",name);var b=FutureSource("1.9.0",name);
            Check(MfgUpstreamApprovalReview.Compare(Analyze(a),Baseline,Real()).Single(i=>i.Finding.Key==name).Kind==MfgUpstreamApprovalReview.New,"Unrelated new name missed");
            Check(Review(a,b).Single(i=>i.Finding.Key==name).Kind==MfgUpstreamApprovalReview.Unchanged,"Future version/key special case");
            var oldCatalog=Baseline.WithUpstream(Analyze(a));Check(!new MfgUnlockSettingsService(true,oldCatalog).DefinitionsFor("1.9.0").Any(d=>d.Key==name),"Future rights inherited merely from name");
            var newCatalog=Baseline.WithUpstream(Analyze(b));Check(new MfgUnlockSettingsService(true,newCatalog).DefinitionsFor("1.9.0").Single(d=>d.Key==name).IsUpstreamSource,"New evidence not independently supported");
        }
    }
    private static void Changed()
    {
        var before=FutureSource("1.8.6");
        foreach(var after in new[]{FutureSource("1.9.0",fallback:"5"),FutureSource("1.9.0",number:"MFGUNLOCK_NUMBER(0,200)"),FutureSource("1.9.0",fallback:"0",number:"MFGUNLOCK_CHOICES(kOnOff)")})
            Check(Review(before,after).Single(i=>i.Finding.Key=="FutureBudget").Kind==MfgUpstreamApprovalReview.Changed,"Changed contract reconfirmed");
        var analysis=Analyze(FutureSource("1.9.0"));var finding=analysis.Editable.Single(f=>f.Key=="FutureBudget");
        // Synthetic metadata-model mutations isolate each comparison field; not claims of parsing arbitrary C++.
        foreach(var e in new[]{finding.Definition! with{Requires=[new("XeFGFlipQueue","1")]},finding.Definition! with{File="ReShade.ini",Section="RenoDX.MFGUnlock"},finding.Definition! with{RequiresRestart=false}}){
            var changed=finding with{File=e.File,Section=e.Section,Definition=e};var variant=analysis with{Findings=analysis.Findings.Select(f=>f.Key==finding.Key?changed:f).ToArray()};
            Check(!MfgUpstreamApprovalReview.Compare(variant,Baseline,before).Any(i=>i.Finding.Key=="FutureBudget"&&i.Kind==MfgUpstreamApprovalReview.Unchanged),"Critical identity/dependency/restart mismatch ignored");
        }
        var files=new Dictionary<string,string>(FutureSource("1.9.0").Files);files[MfgUpstreamSettingsAnalyzer.OptionPath]=files[MfgUpstreamSettingsAnalyzer.OptionPath].Replace("inline void DrawOption(const Option& option) {","inline void DrawOption(const Option& option) { if (ChangedDependency()) return;");
        var unsafeSource=FutureSource("1.9.0") with{Files=files};Check(!Analyze(unsafeSource).SchemaRecognized&&!Review(before,unsafeSource).Any(i=>i.Finding.Key=="FutureBudget"),"Changed write framework carried authority");
    }
    private static void Roundtrip()
    {
        using var f=new Fixture();var game=Path.Combine(f.Root,"game");Directory.CreateDirectory(game);var config=Path.Combine(game,"MFGAdaUnlock.ini");var original=Encoding.UTF8.GetBytes("; preserve\r\n[DLSSG]\r\nGenericGate=1\r\n[Unknown]\r\nKeep=yes\r\n");File.WriteAllBytes(config,original);
        var store=new MfgUpstreamSettingsStore(Path.Combine(f.Root,"approval"));store.Save(Real("1.8.4"));var old=new MfgUnlockSettingsService(true,Baseline.WithUpstream(Analyze(store.Load()!)));
        var unapproved=old.Load(config,game,"1.8.5");Reject(()=>old.CreateUpdatedBytes(unapproved,new Dictionary<string,string>{{"GenericGate","0"}}));Check(File.ReadAllBytes(config).SequenceEqual(original),"Unapproved write changed file");
        Review(store.Load()!,Real());Check(store.Load()!.Tag=="1.8.4","Review itself approved new source");
        store.Save(Real());var fresh=new MfgUpstreamSettingsStore(Path.Combine(f.Root,"approval")).Load()!;var service=new MfgUnlockSettingsService(true,Baseline.WithUpstream(Analyze(fresh)));var snapshot=service.Load(config,game,"1.8.5");var backups=new BackupRestoreService(Path.Combine(f.Root,"transactions"));
        var tx=backups.ExecuteAsync(game,[service.PrepareTransaction(snapshot,new Dictionary<string,string>{{"GenericGate","0"}},()=>true)]).GetAwaiter().GetResult();Check(service.Load(config,game,"1.8.5").Settings.Single(s=>s.Definition.Key=="GenericGate").EffectiveValue=="0","Confirmed value not saved");backups.RestoreAsync(game,tx.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllBytes(config).SequenceEqual(original),"Restore not exact");Reject(()=>store.Save(Real("1.8.4")));
    }
    private static void ReShade()
    {
        var source=Real();var analysis=Analyze(source);var d=analysis.Findings.Single(f=>f.File=="ReShade.ini"&&f.Key=="DynamicMaxMultiplier");Check(d.Definition is null,"Arbitrary ReShade reader granted write authority");
        var definitions=new MfgUnlockSettingsService(false,Baseline.WithUpstream(analysis)).DefinitionsFor("1.8.5");Check(!definitions.Any(d=>d.Key=="DynamicMaxMultiplier"),"Unreviewed ReShade key editable");
        var drs=File.ReadAllText(Path.Combine(Root,"tests/NvidiaComponentScannerChecks/Fixtures/UpstreamDiscovery/1.8.5/drs_override.hpp"));Check(drs.Contains("g_max_multiplier{0}")&&drs.Contains("multiplier == 0 || (multiplier >= 2 && multiplier <= 6)"),"Actual default/allowed values evidence changed");
        foreach(var tag in new[]{"1.8.6","1.9.0"}){
            var files=source.Files.ToDictionary(p=>p.Key,p=>p.Value.Replace("DynamicMaxMultiplier","UnrelatedFutureCeiling",StringComparison.Ordinal));var a=Analyze(new(tag,new string('f',40),files));Check(a.Findings.Single(f=>f.Key=="UnrelatedFutureCeiling").Definition is null,"Future renamed arbitrary reader inferred authority");
        }
        using var f=new Fixture();var config=Path.Combine(f.Root,"ReShade.ini");File.WriteAllText(config,"[RenoDX.MFGUnlock]\nDynamicMaxMultiplier=0\n");var service=new MfgUnlockSettingsService(false,Baseline.WithUpstream(analysis));var snapshot=service.Load(config,f.Root,"1.8.5");Check(snapshot.UnknownSettings.Any(s=>s.Key=="DynamicMaxMultiplier"),"Unknown key lost");Reject(()=>service.CreateUpdatedBytes(snapshot,new Dictionary<string,string>{{"DynamicMaxMultiplier","4"}}));
    }
    private static void Preservation()
    {
        var candidate=Baseline.WithUpstream(Analyze(Real()));var count=0;var groups=new MfgSettingsGroupStore(new UiLocalization());
        foreach(var xe in new[]{false,true})foreach(var old in new MfgUnlockSettingsService(xe,Baseline).DefinitionsFor("1.8.5")){
            var actual=new MfgUnlockSettingsService(xe,candidate).DefinitionsFor("1.8.5").Single(d=>d.Key==old.Key);Check(JsonSerializer.Serialize(old)==JsonSerializer.Serialize(actual),"Protected metadata overwritten");count++;
        }
        var all=new[]{false,true}.SelectMany(x=>new MfgUnlockSettingsService(x,candidate).DefinitionsFor("1.8.5")).ToArray();Check(count==39&&all.Length==40&&all.Select(d=>d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()==40&&candidate.Revision==2,"Duplicate or baseline lost");
        var a=all.Single(d=>d.Key=="TemporalFix");var b=all.Single(d=>d.Key=="GenericGate");Check(ReferenceEquals(groups.For("ReShade.ini",a),groups.For("MFGAdaUnlock.ini",b)),"Duplicate NVIDIA group returned");Check(Baseline.Identity=="7EA564599435600158AC207873880A89AA304FF7814E2147B97011C0805AE977","Signed R2 changed");
    }
    private static void Presentation()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Resource parity broken");var a=Analyze(Real());foreach(var c in UiLocalization.Cultures){l.SetCulture(c,false);var text=MfgUpstreamSettingsPresentation.Render(a,Baseline,Real("1.8.4"),[],l);Check(text.Contains(l.Lookup(MfgUpstreamApprovalReview.Unchanged))&&text.Contains("GenericGate")&&text.Contains("DynamicMaxMultiplier"),"Review provenance not displayed");foreach(var key in new[]{MfgUpstreamApprovalReview.Unchanged,MfgUpstreamApprovalReview.Changed,"Each release is analyzed and approved separately. No write permission is carried forward automatically."})Check(c=="en-US"||l.Lookup(key)!=key,"Untranslated review message");}
    }
    private sealed class Fixture:IDisposable{internal string Root{get;}=Path.Combine(Path.GetTempPath(),"RenoDX-185-"+Guid.NewGuid().ToString("N"));internal Fixture(){Directory.CreateDirectory(Root);}public void Dispose(){Directory.Delete(Root,true);}}
}

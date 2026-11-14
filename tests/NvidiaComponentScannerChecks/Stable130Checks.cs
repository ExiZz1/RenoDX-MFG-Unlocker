using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Stable130Checks
{
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is IOException or InvalidDataException or JsonException or InvalidOperationException or System.Security.Cryptography.CryptographicException){return;}throw new Exception("Unsafe operation accepted");}
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("stable130 real 1.8.2 1.8.3 1.8.4 discovery finds GenericGate without hardcoded metadata",Discovery);
        yield return("stable130 approved GenericGate persists and exact INI bytes restore after restart",Roundtrip);
        yield return("stable130 source approval cannot override 39 protected definitions or downgrade catalog",Protected);
        yield return("stable130 groups merge both INI files across categories and preserve expansion language pending values",Groups);
        yield return("stable130 merged display uses original rows and shared Save workflow",ViewWiring);
        yield return("stable130 optional upstream notifications default off and throttle across service restarts",Throttle);
        yield return("stable130 concurrent checks and failures do not repeat metadata requests",Concurrent);
        yield return("stable130 corrupt cache invalid versions downgrade and cancellation fail safely",Corrupt);
        yield return("stable130 HTTP metadata only rejects redirects rate limits malformed tags and missing releases",Http);
        yield return("stable130 disabling notifications during HTTP cannot reactivate them",DisableDuringCheck);
        yield return("stable130 actual junction notification cache is rejected without outside writes",Junction);
        yield return("stable130 XMX Dynamic warning inherits confirmed 1.8.3 floor without changing mappings",Xmx);
        yield return("stable130 eight languages separate official source signed catalog and app updates",Languages);
    }
    private static MfgDynamicSettingsCatalog Candidate()=>MfgDynamicSettingsCatalog.ReviewedFallback().WithUpstream(MfgUpstreamSettingsAnalyzer.Analyze(Dev4UpstreamChecks.Snapshot("1.8.4")));
    private static void Discovery()
    {
        foreach(var version in new[]{"1.8.2","1.8.3","1.8.4"}){
            var source=Dev4UpstreamChecks.Snapshot(version);var a=MfgUpstreamSettingsAnalyzer.Analyze(source);
            Check(a.SchemaRecognized&&a.Editable.Count==(version=="1.8.4"?29:28),"Real declarations no longer recognized: "+version);
            Check(a.Editable.Any(f=>f.Key=="GenericGate")== (version=="1.8.4"),"GenericGate invented or missed");
        }
        var d=new MfgUnlockSettingsService(true,Candidate()).DefinitionsFor("1.8.4").Single(d=>d.Key=="GenericGate");
        Check(d.IsUpstreamSource&&d.Section=="DLSSG"&&d.CatalogEntry is {File:"MFGAdaUnlock.ini",Type:"Boolean",Default:"1",MinimumAddon:"1.8.4"}&&d.Choices.Select(c=>c.Value).SequenceEqual(new[]{"0","1"}),"Incorrect real metadata");
        Check(d.CatalogEntry!.Evidence.Contains("efc1fe7deb20772aa14e05e065c2bf57dc0111d6"),"Evidence not immutable");
        Check(!new MfgUnlockSettingsService(true,Candidate()).DefinitionsFor("1.8.3").Any(x=>x.Key=="GenericGate"),"GenericGate leaked to older addon");
    }
    private static void Roundtrip()
    {
        using var f=new Fixture();var game=Path.Combine(f.Root,"game");Directory.CreateDirectory(game);var config=Path.Combine(game,"MFGAdaUnlock.ini");
        var bytes=Encoding.UTF8.GetBytes("; keep original\r\n[Other]\r\nUnknown=stay\r\n[DLSSG]\r\nGenericGate=1\r\n");File.WriteAllBytes(config,bytes);
        var store=new MfgUpstreamSettingsStore(Path.Combine(f.Root,"approval"));store.Save(Dev4UpstreamChecks.Snapshot("1.8.4"));
        var fresh=new MfgUpstreamSettingsStore(Path.Combine(f.Root,"approval")).Load()!;var catalog=MfgDynamicSettingsCatalog.ReviewedFallback().WithUpstream(MfgUpstreamSettingsAnalyzer.Analyze(fresh));
        var service=new MfgUnlockSettingsService(true,catalog);var backup=new BackupRestoreService(Path.Combine(f.Root,"transactions"));
        var before=service.Load(config,game,"1.8.4");var transaction=backup.ExecuteAsync(game,[service.PrepareTransaction(before,new Dictionary<string,string>{{"GenericGate","0"}},()=>true)]).GetAwaiter().GetResult();
        Check(new MfgUnlockSettingsService(true,catalog).Load(config,game,"1.8.4").Settings.Single(s=>s.Definition.Key=="GenericGate").EffectiveValue=="0","Restart lost value");
        Check(File.ReadAllText(config).Contains("Unknown=stay")&&File.ReadAllText(config).StartsWith("; keep original"),"Unknown/comment lost");
        backup.RestoreAsync(game,transaction.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllBytes(config).SequenceEqual(bytes),"Byte-exact Restore failed");
        Reject(()=>service.CreateUpdatedBytes(before,new Dictionary<string,string>{{"GenericGate","2"}}));
        File.AppendAllText(config,"; external change\n");Reject(()=>service.CreateUpdatedBytes(before,new Dictionary<string,string>{{"GenericGate","0"}}));
    }
    private static void Protected()
    {
        var baseline=MfgDynamicSettingsCatalog.ReviewedFallback();var candidate=Candidate();var count=0;
        foreach(var xe in new[]{false,true})foreach(var old in new MfgUnlockSettingsService(xe,baseline).DefinitionsFor("1.8.4")){
            var d=new MfgUnlockSettingsService(xe,candidate).DefinitionsFor("1.8.4").Single(d=>d.Key==old.Key);
            Check(JsonSerializer.Serialize(d)==JsonSerializer.Serialize(old),"Protected definition changed");count++;
        }
        Check(count==39&&candidate.Revision==2&&candidate.Identity.StartsWith(baseline.Identity),"Baseline replaced");
        using var f=new Fixture();var store=new MfgSettingsCatalogStore(Path.Combine(f.Root,"catalog"));
        using var sig=typeof(UiLocalization).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.SettingsCatalog.reviewed-r2.sig")!;using var json=typeof(UiLocalization).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.SettingsCatalog.reviewed-r2.json")!;using var j=new MemoryStream();using var s=new MemoryStream();json.CopyTo(j);sig.CopyTo(s);
        var valid=j.ToArray();var signature=s.ToArray();Check(MfgDynamicSettingsCatalog.Verify(valid,signature).Revision==2,"Production catalog rejected");valid[Array.IndexOf(valid,(byte)'R')]^=1;Reject(()=>MfgDynamicSettingsCatalog.Verify(valid,signature));Reject(()=>MfgDynamicSettingsCatalog.Verify(j.ToArray(),new byte[512]));
        store.Import(j.ToArray(),signature);Check(store.Load().Revision==2,"Owner store lost revision");
        var approvals=new MfgUpstreamSettingsStore(Path.Combine(f.Root,"approval"));var newest=Dev4UpstreamChecks.Snapshot("1.8.4");approvals.Save(newest);
        var path=Path.Combine(f.Root,"approval","approved.dat");var original=File.ReadAllBytes(path);
        Reject(()=>approvals.Save(Dev4UpstreamChecks.Snapshot("1.8.3")));Reject(()=>approvals.Save(newest with{Commit=new string('a',40)}));
        Check(File.ReadAllBytes(path).SequenceEqual(original),"Rejected downgrade changed approval");
    }
    private static void Groups()
    {
        var l=new UiLocalization();var groups=new MfgSettingsGroupStore(l);var nvidia=new MfgUnlockSettingsService(false,Candidate()).DefinitionsFor("1.8.4").Single(d=>d.Key=="TemporalFix");var generic=new MfgUnlockSettingsService(true,Candidate()).DefinitionsFor("1.8.4").Single(d=>d.Key=="GenericGate");
        var a=groups.For("ReShade.ini",nvidia);var b=groups.For("MFGAdaUnlock.ini",generic);Check(ReferenceEquals(a,b),"Duplicate NVIDIA groups across files");a.IsExpanded=false;
        foreach(var c in UiLocalization.Cultures){l.SetCulture(c,false);groups.RefreshLanguage();Check(!b.IsExpanded&&ReferenceEquals(a,groups.For("MFGAdaUnlock.ini",generic)),"Language lost group state");}
        // File identity does not own presentation groups, in any category. No values are changed.
        foreach(var category in new[]{"General","Advanced","Other"})foreach(var section in new[]{"DLSSG","XeFG","Diagnostics","Compatibility"}){
            var d=generic with{CatalogEntry=generic.CatalogEntry! with{Category=category,Section=section},Section=section};
            var g=groups.For("MFGAdaUnlock.ini",d);var h=groups.For("MFGAdaUnlock.ini",d with{Key="AnotherKey"});Check(ReferenceEquals(g,h),"Duplicate within category/section");
        }
    }
    private static void ViewWiring()
    {
        var code=File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.MfgCategories.cs"));
        Check(code.Contains("MfgSettingsItemsControl.ItemsSource=displayedMfgRows")&&code.Contains("mfgSettingRows.Concat(xeMfgSettingRows)")&&code.Contains("displayedMfgRows.Add(row)")&&!code.Contains("new MfgUnlockSettingRow"),"Merged view clones or loses row identity");
        Check(code.Contains("PendingMfgEdits()=>mfgSettingsDocuments")&&code.Contains("XeMfgSettingsItemsControl.Visibility=Visibility.Collapsed"),"Shared writer authority/display split changed");
        var doc=XDocument.Load(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.xaml"));XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        foreach(var name in new[]{"MfgSettingsSaveButton","MfgSettingsDiscardButton","MfgSettingsReloadButton","CheckUpstreamSettingsButton","ImportSettingsCatalogButton"})Check(doc.Descendants().Any(e=>(string?)e.Attribute(x+"Name")==name),"Shared control removed");
    }
    private static void Throttle()
    {
        using var f=new Fixture();var now=DateTimeOffset.UtcNow;var calls=0;Task<string> Latest(CancellationToken _){calls++;return Task.FromResult("1.8.4");}
        var s=new MfgUpstreamReleaseNotice(f.Root,Latest,()=>now);Check(!s.Read().Enabled&&s.CheckIfDueAsync().GetAwaiter().GetResult() is null&&calls==0,"Not default off");s.SetEnabled(true);
        Check(s.CheckIfDueAsync().GetAwaiter().GetResult()=="1.8.4"&&calls==1,"Metadata not retrieved");
        var restart=new MfgUpstreamReleaseNotice(f.Root,Latest,()=>now);now=now.AddHours(23);restart.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==1,"Restart bypassed 24h");now=now.AddHours(1);restart.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==2,"24h check suppressed");
        Check(MfgUpstreamReleaseNotice.HasUnreviewedRelease("1.8.4","1.8.3")&&!MfgUpstreamReleaseNotice.HasUnreviewedRelease("1.8.4","1.8.4"),"Notification comparison wrong");
    }
    private static void Concurrent()
    {
        using var f=new Fixture();var calls=0;var pending=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> Latest(CancellationToken _){Interlocked.Increment(ref calls);return pending.Task;}
        var a=new MfgUpstreamReleaseNotice(f.Root,Latest);a.SetEnabled(true);var task=a.CheckIfDueAsync();var b=new MfgUpstreamReleaseNotice(f.Root,Latest);b.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==1,"Duplicate HTTP request");pending.SetException(new HttpRequestException("429 rate limited"));Check(task.GetAwaiter().GetResult() is null,"Failure not safe");a.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==1,"Failure retried within 24h");
    }
    private static void Corrupt()
    {
        using var f=new Fixture();var calls=0;var now=DateTimeOffset.UtcNow;
        Task<string> Latest(CancellationToken _){calls++;return Task.FromResult("bad-version");}
        File.WriteAllText(Path.Combine(f.Root,"metadata.json"),"{broken");var s=new MfgUpstreamReleaseNotice(f.Root,Latest,()=>now);s.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==0&&!s.Read().Enabled,"Corrupt cache enabled network");s.SetEnabled(true);s.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==0,"Corruption bypassed throttle");now=now.AddHours(25);Check(s.CheckIfDueAsync().GetAwaiter().GetResult() is null&&s.Read().Latest is null,"Invalid version accepted");
        s.SetEnabled(false);s.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==1,"Disabled still polls");
        using var cancel=new CancellationTokenSource();cancel.Cancel();using var g=new Fixture();var z=new MfgUpstreamReleaseNotice(g.Root,Latest);z.SetEnabled(true);try{z.CheckIfDueAsync(cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){}Check(calls==1&&z.Read().AttemptUtc is null,"Pre-cancelled check claimed/sent");
        File.WriteAllText(Path.Combine(g.Root,"metadata.json"),JsonSerializer.Serialize(new MfgUpstreamReleaseNotice.State(true,null,"2.0.0")));var downgrade=new MfgUpstreamReleaseNotice(g.Root,_=>Task.FromResult("1.8.4"));Check(downgrade.CheckIfDueAsync().GetAwaiter().GetResult()=="2.0.0","Metadata downgrade accepted");
    }
    private static void Http()
    {
        foreach(var kind in new[]{"ok","redirect","rate","missing","bad","prerelease","wrong-url","garbage"}){
            using var f=new Fixture();var calls=0;using var http=new HttpClient(new Handler(r=>{
                calls++;Check(r.RequestUri!.AbsoluteUri=="https://api.github.com/repos/mavismmg/MFGAdaUnlock-RenoDx/releases/latest","Automatic source download occurred");
                if(kind=="redirect")return new(HttpStatusCode.Redirect);if(kind=="rate")return new(HttpStatusCode.TooManyRequests);if(kind=="missing")return new(HttpStatusCode.NotFound);
                var json=kind=="garbage"?"broken":JsonSerializer.Serialize(new{tag_name=kind=="bad"?"1.8.4-preview":"1.8.4",draft=false,prerelease=kind=="prerelease",html_url=kind=="wrong-url"?"https://evil.example/":"https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/1.8.4"});return new(HttpStatusCode.OK){Content=new StringContent(json)};
            }));using var client=new MfgUpstreamSettingsClient(http);var s=new MfgUpstreamReleaseNotice(f.Root,client.LatestStableAsync);s.SetEnabled(true);var result=s.CheckIfDueAsync().GetAwaiter().GetResult();Check((result=="1.8.4")== (kind=="ok"),"Bad metadata accepted: "+kind);s.CheckIfDueAsync().GetAwaiter().GetResult();Check(calls==1,"Error was retried");
        }
    }
    private static void DisableDuringCheck()
    {
        using var f=new Fixture();var t=new TaskCompletionSource<string>();var s=new MfgUpstreamReleaseNotice(f.Root,_=>t.Task);s.SetEnabled(true);var check=s.CheckIfDueAsync();s.SetEnabled(false);t.SetResult("1.8.4");Check(check.GetAwaiter().GetResult() is null&&!s.Read().Enabled&&s.Read().Latest is null,"Late HTTP reactivated polling");
    }
    private static void Junction()
    {
        using var f=new Fixture();var outside=Path.Combine(f.Root,"outside");Directory.CreateDirectory(outside);var link=Path.Combine(f.Root,"link");
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};foreach(var s in new[]{"/d","/c","mklink","/J",link,outside})start.ArgumentList.Add(s);using var p=System.Diagnostics.Process.Start(start)!;if(!p.WaitForExit(10000)){p.Kill();throw new Exception("Junction timeout");}if(p.ExitCode!=0)throw new SkipCheckException("Windows junction unavailable: "+p.StandardError.ReadToEnd());
        try{var s=new MfgUpstreamReleaseNotice(link);Reject(()=>s.SetEnabled(true));Check(s.CheckIfDueAsync().GetAwaiter().GetResult() is null&&Directory.GetFileSystemEntries(outside).Length==0,"Junction wrote outside cache");}finally{Directory.Delete(link);}
    }
    private static void Xmx()
    {
        using var f=new Fixture();var path=Path.Combine(f.Root,"MFGAdaUnlock.ini");File.WriteAllText(path,"[Backend]\nSelected=DLSSG\n[DLSSG]\nXmxGenerator=1\n");
        foreach(var v in new[]{"1.8.0","1.8.2","1.8.3","1.8.4","2.0.0"}){
            var s=new MfgUnlockSettingsService(true).Load(path,f.Root,v);var values=s.Settings.ToDictionary(x=>x.Definition.Key,x=>(string?)x.EffectiveValue);var info=GenerationMethodPresenter.Describe(s,values)!;
            Check(info.ContextWarning==(Version.Parse(v)>=new Version(1,8,3)?GenerationMethodPresentation.DynamicXmxWarning:GenerationMethodPresentation.SelectedXmxWarning),"Wrong XMX capability warning: "+v);
            Check(!info.Pending&&info.SelectedXmx&&info.SavedXmx,"Mapping/saved semantics changed");
        }
        foreach(var bad in new[]{"unknown","1.8.3-preview","1.7.9"})Check(!MfgSettingsCapabilityCatalog.Supports(bad,MfgSettingsCapabilityCatalog.Feature.DynamicMfgXmx),"Invalid feature enabled");
    }
    private static void Languages()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Locale parity");foreach(var c in UiLocalization.Cultures){foreach(var k in new[]{"Official addon source","Signed RenoDX catalog (fallback)","Analyze official addon source","Check signed RenoDX catalog",GenerationMethodPresentation.DynamicXmxWarning})Check(l.GetCatalog(c).ContainsKey(k)&&(c=="en-US"||l.Lookup(k,c)!=k),"Missing localization: "+c+" "+k);}
        var s=File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MfgSettingsDisplay.cs"));Check(!s.Contains("(Development)"),"Development label still displayed");var main=File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.SettingsCatalog.cs"));Check(main.Contains("ShowThemeConfirmation(\"Approve official addon source\",summary)"),"Source approval mislabeled");
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(response(r));}}
    private sealed class Fixture:IDisposable{internal string Root{get;}=Path.Combine(Path.GetTempPath(),"RenoDX-stable130-"+Guid.NewGuid().ToString("N"));internal Fixture(){Directory.CreateDirectory(Root);}public void Dispose(){Directory.Delete(Root,true);}}
}

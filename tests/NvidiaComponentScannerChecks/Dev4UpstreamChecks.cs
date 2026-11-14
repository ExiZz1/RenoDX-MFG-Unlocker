using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Dev4UpstreamChecks
{
    internal static readonly string[] Versions=["1.6.0","1.7.0","1.7.2","1.8.0","1.8.1","1.8.2"];
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static string Fixtures=>Path.Combine(Root,"tests/NvidiaComponentScannerChecks/Fixtures/UpstreamDiscovery");
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or IOException or HttpRequestException or OperationCanceledException or CryptographicException or JsonException or DecoderFallbackException){return;}throw new Exception("Unsafe source accepted");}
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("dev4 six real historical source fixtures distinguish declarations and unresolved readers",History);
        yield return("dev4 real 1.8.0 table extends a simulated legacy-only model and writes restores a real option",HistoricalRoundtrip);
        yield return("dev4 finite grammar accepts new unrelated boolean integer and enum names without a whitelist",NewDeclarations);
        yield return("dev4 defaults ranges enum and disappearance diffs are detected without rewriting settings",Diff);
        yield return("dev4 changed framework dependencies macros and malformed C++ fail closed",Malformed);
        yield return("dev4 learned unsafe duplicate and invalid defaults receive no write authority",Unsafe);
        yield return("dev4 protected legacy owner metadata and all 39 current definitions are preserved",Protected);
        yield return("dev4 unsigned upstream scope is separate and limited to the confirmed addon release",VersionScope);
        yield return("dev4 signed revision 2 fallback uses the existing production key",OwnerFallback);
        yield return("dev4 upstream numeric writes backup restore and stale snapshots retain existing authority",Roundtrip);
        yield return("dev4 interrupted settings write rolls back exact temporary bytes",Rollback);
        yield return("dev4 DPAPI approval cache rejects tampering",Cache);
        yield return("dev4 approval cache rejects an actual Windows junction without touching its target",Junction);
        yield return("dev4 commit archive verifies Git blob identity and rejects traversal duplicates corruption",Archive);
        yield return("dev4 HTTP release commit tag binding redirects cancellation and limits fail closed",Transport);
        yield return("dev4 six-language resources provenance labels and shared workflow remain wired",Localization);
    }
    internal static UpstreamSourceSnapshot Snapshot(string version="1.8.2",string? header=null)
    {
        var dir=Path.Combine(Fixtures,version);using var identity=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"identity.json")));
        var files=new Dictionary<string,string>();
        foreach(var name in new[]{"advanced_settings.hpp","backend_selection.hpp","addon.cpp","xemfg_native_controls.cpp"}){
            var path=Path.Combine(dir,name);if(File.Exists(path))files[MfgUpstreamSettingsAnalyzer.Prefix+(name.StartsWith("xemfg_")?name.Replace("xemfg_","xemfg/"):name)]=File.ReadAllText(path);
        }
        if(header is not null)files[MfgUpstreamSettingsAnalyzer.OptionPath]=header;
        return new(version,identity.RootElement.GetProperty("Commit").GetString()!,files);
    }
    private static string Header=>Snapshot().Files[MfgUpstreamSettingsAnalyzer.OptionPath];
    private static string Added(string entry)=>Header.Replace("inline constexpr Option kDiagnostics[]{","inline constexpr Option kDiagnostics[]{\n"+entry+",",StringComparison.Ordinal);
    private const string Number="{L\"Diagnostics\",L\"FutureBudget\",\"Future budget\",\"Synthetic declaration only, never a real upstream capability.\",L\"4\",MFGUNLOCK_NUMBER(0,100)}";
    private static MfgDynamicSettingsCatalog Candidate(string? header=null)=>MfgDynamicSettingsCatalog.ReviewedFallback().WithUpstream(MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:header??Added(Number))));
    private static void History()
    {
        foreach(var v in Versions){var a=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(v));Check(a.Findings.Count>0,"No literal findings: "+v);
            if(Version.Parse(v)>=new Version(1,8,0))Check(a.SchemaRecognized&&a.Editable.Count==28,"Reviewed table not recognized: "+v);
            else Check(!a.SchemaRecognized&&a.Editable.Count==0,"Pre-table guessed authority: "+v);
        }
        var diffs=MfgUpstreamSettingsAnalyzer.Compare(MfgUpstreamSettingsAnalyzer.Analyze(Snapshot("1.7.2")),MfgUpstreamSettingsAnalyzer.Analyze(Snapshot("1.8.0")));
        Check(diffs.Any(d=>d.Kind=="New declaration/reader"),"Real declaration evolution missed");
        foreach(var (a,b) in new[]{("1.8.0","1.8.1"),("1.8.1","1.8.2")})Check(!MfgUpstreamSettingsAnalyzer.Compare(MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(a)),MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(b))).Any(d=>d.Kind is "Default changed" or "Range changed" or "Enum values changed"),"Historical table unexpectedly differs");
    }
    private static void NewDeclarations()
    {
        var header=Added(Number+",\n{L\"Diagnostics\",L\"UnrelatedToggle\",\"Unrelated toggle\",\"Synthetic boolean declaration.\",L\"0\",MFGUNLOCK_CHOICES(kOnOff)},\n{L\"Compatibility\",L\"FutureMode\",\"Future mode\",\"Synthetic enum declaration.\",L\"ready\",MFGUNLOCK_CHOICES(kFirstFrame)}");
        var a=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:header));Check(a.SchemaRecognized&&a.Editable.Count==31,"New declarations not generic");
        var definitions=new MfgUnlockSettingsService(true,Candidate(header)).DefinitionsFor("1.8.2");Check(definitions.Any(d=>d.Key=="FutureBudget"&&d.CatalogEntry!.Type=="Integer")&&definitions.Any(d=>d.Key=="UnrelatedToggle"&&d.CatalogEntry!.Type=="Boolean")&&definitions.Any(d=>d.Key=="FutureMode"&&d.CatalogEntry!.Type=="Enum"),"Types lost");
    }
    private static void HistoricalRoundtrip()
    {
        // The declaration source is real. The empty extension baseline models an older editor; it is not an old RenoDX binary.
        var create=typeof(MfgDynamicSettingsCatalog).GetMethod("Create",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var baseline=(MfgDynamicSettingsCatalog)create.Invoke(null,[new MfgCatalogDocument(1,MfgDynamicSettingsCatalog.Purpose,"historical-model-fixture",1,[]),"historical-model-fixture"])!;
        var analysis=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot("1.8.0"));var catalog=baseline.WithUpstream(analysis);
        var service=new MfgUnlockSettingsService(true,catalog);Check(new MfgUnlockSettingsService(true,baseline).DefinitionsFor("1.8.0").Count<service.DefinitionsFor("1.8.0").Count,"Real definitions did not extend model");
        var d=service.DefinitionsFor("1.8.0").Single(d=>d.Key=="XeFGFlipMeteringLeadUs");Check(d.CatalogEntry is {Default:"2500",Minimum:0,Maximum:8000}&&d.IsUpstreamSource,"Real range not extracted");
        using var f=new Fixture();var path=Path.Combine(f.Game,"MFGAdaUnlock.ini");var original=Encoding.UTF8.GetBytes("; real declaration, isolated file\n[XeFG]\nXeFGFlipMeteringLeadUs=2500\n");File.WriteAllBytes(path,original);
        var snapshot=service.Load(path,f.Game,"1.8.0");var backup=new BackupRestoreService(Path.Combine(f.Root,"transactions"));
        var result=backup.ExecuteAsync(f.Game,[service.PrepareTransaction(snapshot,new Dictionary<string,string>{{d.Key,"3000"}},()=>true)]).GetAwaiter().GetResult();
        Check(File.ReadAllText(path).Contains("XeFGFlipMeteringLeadUs=3000"),"Real option write failed");backup.RestoreAsync(f.Game,result.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllBytes(path).SequenceEqual(original),"Real option restore failed");
    }
    private static void Diff()
    {
        var original=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:Added(Number)));
        var changed=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:Added(Number.Replace("L\"4\"","L\"5\"").Replace("(0,100)","(0,200)"))));
        var diff=MfgUpstreamSettingsAnalyzer.Compare(original,changed);Check(diff.Any(d=>d.Kind=="Default changed")&&diff.Any(d=>d.Kind=="Range changed"),"Default/range diff missed");
        var enumSource=Header.Replace("{\"Up\",L\"2\"}","{\"Up\",L\"2\"},{\"Fourth\",L\"3\"}");
        Check(MfgUpstreamSettingsAnalyzer.Compare(MfgUpstreamSettingsAnalyzer.Analyze(Snapshot()),MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:enumSource))).Any(d=>d.Kind=="Enum values changed"),"Enum diff missed");
        Check(MfgUpstreamSettingsAnalyzer.Compare(original,MfgUpstreamSettingsAnalyzer.Analyze(Snapshot())).Any(d=>d.Id.EndsWith("|FutureBudget")&&d.Kind.StartsWith("No longer found")),"Disappearance missed");
    }
    private static void Malformed()
    {
        foreach(var header in new[]{Added(Number).Replace("inline void DrawOption(const Option& option) {","inline void DrawOption(const Option& option) { if (Prerequisite()) return;"),
            Added(Number).Replace("#define MFGUNLOCK_NUMBER(low,high) nullptr,0,low,high","#define MFGUNLOCK_NUMBER(low,high) nullptr,0,0,999"),
            Added(Number.Replace("L\"4\"","ComputeDefault()")),Added(Number).Replace("L\"FutureBudget\"","L\"FutureBudget\\x01\""),Added("#if ENABLE\n"+Number+"\n#endif")}){
            var a=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:header));Check(a.Editable.Count==0,"Malformed/framework change granted editing");
        }
    }
    private static void Unsafe()
    {
        foreach(var text in new[]{Number.Replace("(0,100)","(100,0)"),Number.Replace("L\"4\"","L\"999\""),Number+","+Number,Number.Replace("FutureBudget","../escape")}){
            var a=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:Added(text)));Check(!a.Editable.Any(f=>f.Key=="FutureBudget"),"Unsafe metadata accepted");
        }
        var internalState=Candidate(Added(Number.Replace("L\"Diagnostics\"","L\"XeFG.Learned\"")));Check(!new MfgUnlockSettingsService(true,internalState).DefinitionsFor("1.8.2").Any(d=>d.Key=="FutureBudget"),"Learned state granted");
        Reject(()=>MfgUpstreamSettingsAnalyzer.Analyze(Snapshot() with{Commit="main"}));Reject(()=>MfgUpstreamSettingsAnalyzer.Analyze(Snapshot() with{Tag="1.8.2-preview"}));
        var files=new Dictionary<string,string>(Snapshot().Files){{"../evil.cpp","text"}};Reject(()=>MfgUpstreamSettingsAnalyzer.Analyze(Snapshot() with{Files=files}));
        Reject(()=>MfgUpstreamSettingsAnalyzer.Analyze(Snapshot() with{Files=new(){{MfgUpstreamSettingsAnalyzer.OptionPath,new string('x',512*1024+1)}}}));
        Reject(()=>MfgUpstreamSettingsAnalyzer.Analyze(Snapshot() with{Files=Enumerable.Range(0,257).ToDictionary(i=>MfgUpstreamSettingsAnalyzer.Prefix+"f"+i+".hpp",_=>"x")}));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();Reject(()=>MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(),cancelled.Token));
    }
    private static void Protected()
    {
        var baseline=MfgDynamicSettingsCatalog.ReviewedFallback();var a=MfgUpstreamSettingsAnalyzer.Analyze(Snapshot(header:Added(Number).Replace("L\"XeFGFlipQueue\",\"Flip queue\",\"Experimental presentation queue. Off by default.\",L\"0\"","L\"XeFGFlipQueue\",\"Flip queue\",\"Experimental presentation queue. Off by default.\",L\"1\"")));
        var candidate=baseline.WithUpstream(a);var count=0;
        foreach(var xe in new[]{false,true})foreach(var d in new MfgUnlockSettingsService(xe,baseline).DefinitionsFor("1.8.2")){
            var actual=new MfgUnlockSettingsService(xe,candidate).DefinitionsFor("1.8.2").Single(x=>x.Key==d.Key);Check(JsonSerializer.Serialize(actual)==JsonSerializer.Serialize(d),"Existing definition changed: "+d.Key);count++;
        }
        Check(count==39&&candidate.Revision==2,"39 definitions/revision lost");Check(a.Findings.Any(f=>MfgUpstreamSettingsAnalyzer.CompareKnown(f,baseline,"1.8.2").StartsWith("Metadata conflict")),"Known metadata conflict hidden");
    }
    private static void VersionScope()
    {
        var c=Candidate();var service=new MfgUnlockSettingsService(true,c);
        Check(service.DefinitionsFor("1.8.2").Single(d=>d.Key=="FutureBudget") is {IsUpstreamSource:true,MetadataTrust:"Upstream source — locally approved, not owner-signed"},"Provenance lost");
        Check(service.DefinitionsFor("1.8.2").Single(d=>d.Key=="CpuTiming").MetadataTrust=="Owner-signed catalog metadata","Owner metadata misclassified");
        Check(new MfgUnlockSettingsService(false,c).DefinitionsFor("1.8.2").Single(d=>d.Key=="Enabled").MetadataTrust=="Built-in reviewed metadata","Legacy metadata misclassified");
        foreach(var v in new[]{"1.7.9","1.8.1","1.8.3","2.0.0","bad"})Check(!service.DefinitionsFor(v).Any(d=>d.Key=="FutureBudget"),"Source authority escaped release: "+v);
        Check(service.DefinitionsFor("2.0.0").Any(d=>d.Key=="CpuTiming"),"Signed minimum-version inheritance lost");
    }
    private static void OwnerFallback(){var c=MfgDynamicSettingsCatalog.ReviewedFallback();Check(c.Revision==2&&c.Identity=="7EA564599435600158AC207873880A89AA304FF7814E2147B97011C0805AE977","Owner fallback incorrect");}
    private sealed class Fixture:IDisposable
    {
        internal string Root=Path.Combine(Path.GetTempPath(),"RenoDX-dev4",Guid.NewGuid().ToString("N"));
        internal string Game=>Path.Combine(Root,"game");
        internal Fixture(){Directory.CreateDirectory(Game);}
        public void Dispose(){Directory.Delete(Root,true);}
    }
    private static void Roundtrip()
    {
        using var f=new Fixture();var file=Path.Combine(f.Game,"MFGAdaUnlock.ini");var original=Encoding.UTF8.GetBytes("; preserve\r\n[Diagnostics]\r\nFutureBudget=4\r\nOpaque=untouched\r\n");File.WriteAllBytes(file,original);
        var s=new MfgUnlockSettingsService(true,Candidate());var snapshot=s.Load(file,f.Game,"1.8.2");var changes=new Dictionary<string,string>{{"FutureBudget","7"}};
        var service=new BackupRestoreService(Path.Combine(f.Root,"transactions"));var transaction=service.ExecuteAsync(f.Game,[s.PrepareTransaction(snapshot,changes,()=>true)]).GetAwaiter().GetResult();
        Check(File.ReadAllText(file).Contains("FutureBudget=7")&&File.ReadAllText(file).Contains("Opaque=untouched"),"Write corrupted fixture");
        Reject(()=>s.CreateUpdatedBytes(snapshot,changes));service.RestoreAsync(f.Game,transaction.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllBytes(file).SequenceEqual(original),"Restore not exact");
        s.UseCatalog(MfgDynamicSettingsCatalog.ReviewedFallback());Reject(()=>s.CreateUpdatedBytes(snapshot,changes));
    }
    private static void Rollback()
    {
        using var f=new Fixture();var file=Path.Combine(f.Game,"MFGAdaUnlock.ini");File.WriteAllText(file,"; original\n[Diagnostics]\nFutureBudget=4\n");var original=File.ReadAllBytes(file);var s=new MfgUnlockSettingsService(true,Candidate());var snap=s.Load(file,f.Game,"1.8.2");
        var tx=s.PrepareTransaction(snap,new Dictionary<string,string>{{"FutureBudget","6"}},()=>true);var backup=new BackupRestoreService(Path.Combine(f.Root,"transactions"));
        var second=new BackupTransactionFile("second.txt",[1],false,null);var reached=false;
        Reject(()=>backup.ExecuteAsync(f.Game,[tx,second],i=>{if(i==1){Check(File.ReadAllText(file).Contains("FutureBudget=6"),"First write was not exercised");reached=true;throw new IOException("Injected mid-operation failure");}},null).GetAwaiter().GetResult());
        Check(reached&&File.ReadAllBytes(file).SequenceEqual(original)&&!File.Exists(Path.Combine(f.Game,"second.txt")),"Failure did not roll back original");
    }
    private static void Cache()
    {
        using var f=new Fixture();var store=new MfgUpstreamSettingsStore(Path.Combine(f.Root,"approval"));Check(store.Load() is null,"Unexpected cache");var source=Snapshot(header:Added(Number));store.Save(source);Check(store.Load()!.Commit==source.Commit,"Approval lost");
        Check(MfgDynamicSettingsCatalog.ReviewedFallback().WithUpstream(MfgUpstreamSettingsAnalyzer.Analyze(source)).Identity==MfgDynamicSettingsCatalog.ReviewedFallback().WithUpstream(MfgUpstreamSettingsAnalyzer.Analyze(store.Load()!)).Identity,"Snapshot identity changed after reload");
        var path=Path.Combine(f.Root,"approval","approved.dat");var bytes=File.ReadAllBytes(path);bytes[^1]^=1;File.WriteAllBytes(path,bytes);Reject(()=>store.Load());
    }
    private static void Junction()
    {
        using var f=new Fixture();var outside=Path.Combine(f.Root,"outside");Directory.CreateDirectory(outside);var link=Path.Combine(f.Root,"link");
        var start=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var part in new[]{"/d","/c","mklink","/J",link,outside})start.ArgumentList.Add(part);
        using var process=Process.Start(start)!;if(!process.WaitForExit(10000)){process.Kill();throw new Exception("Junction creation timeout");}
        if(process.ExitCode!=0)throw new SkipCheckException("Windows refused isolated junction creation; not tested: "+process.StandardError.ReadToEnd());
        try{var store=new MfgUpstreamSettingsStore(link);Reject(()=>store.Save(Snapshot()));Reject(()=>store.Load());Check(Directory.GetFileSystemEntries(outside).Length==0,"Junction target changed");}
        finally{Directory.Delete(link);}
    }
    private static byte[] Zip(string commit,Dictionary<string,string> files,bool duplicate=false,bool traversal=false)
    {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){
            foreach(var (name,text) in files){var entry=zip.CreateEntry("MFGAdaUnlock-RenoDx-"+commit+"/"+name);using var output=entry.Open();output.Write(Encoding.UTF8.GetBytes(text));}
            if(duplicate){var e=zip.CreateEntry("MFGAdaUnlock-RenoDx-"+commit+"/"+files.Keys.First());using var w=new StreamWriter(e.Open());w.Write("duplicate");}
            if(traversal)zip.CreateEntry("MFGAdaUnlock-RenoDx-"+commit+"/../outside.cpp");
        }return memory.ToArray();
    }
    private static Dictionary<string,string> Blobs(Dictionary<string,string> files)=>files.ToDictionary(p=>p.Key,p=>{var data=Encoding.UTF8.GetBytes(p.Value);return Convert.ToHexString(SHA1.HashData([..Encoding.ASCII.GetBytes("blob "+data.Length+"\0"),..data])).ToLowerInvariant();});
    private static void Archive()
    {
        var source=Snapshot();var expected=Blobs(source.Files);Check(MfgUpstreamSettingsClient.ReadArchive(Zip(source.Commit,source.Files),source.Commit,expected).Count==source.Files.Count,"Valid archive rejected");
        Reject(()=>MfgUpstreamSettingsClient.ReadArchive(Zip(source.Commit,source.Files,true),source.Commit,expected));Reject(()=>MfgUpstreamSettingsClient.ReadArchive(Zip(source.Commit,source.Files,false,true),source.Commit,expected));
        var bad=new Dictionary<string,string>(expected);bad[bad.Keys.First()]=new string('0',40);Reject(()=>MfgUpstreamSettingsClient.ReadArchive(Zip(source.Commit,source.Files),source.Commit,bad));Reject(()=>MfgUpstreamSettingsClient.ReadArchive([0,1,2],source.Commit,expected));
        var expanded=new Dictionary<string,string>{{MfgUpstreamSettingsAnalyzer.OptionPath,new string('x',512*1024+1)}};Reject(()=>MfgUpstreamSettingsClient.ReadArchive(Zip(source.Commit,expanded),source.Commit,Blobs(expanded)));
    }
    private static void Transport()
    {
        var source=Snapshot();var tree=new string('a',40);var sha=source.Commit;var zip=Zip(sha,source.Files);var blobs=Blobs(source.Files);
        HttpResponseMessage Response(HttpRequestMessage request,string variant){
            var uri=request.RequestUri!;
            if(variant=="redirect")return new(HttpStatusCode.Redirect){Headers={Location=new Uri("https://evil.example/code")}};
            if(variant=="missing")return new(HttpStatusCode.NotFound);
            if(uri.Host=="codeload.github.com")return new(HttpStatusCode.OK){Content=new ByteArrayContent(zip)};
            object data=uri.AbsolutePath.Contains("releases/")?new{tag_name=variant=="tag"?"1.8.1":"1.8.2",draft=false,prerelease=false,html_url="https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/1.8.2"}
                :uri.AbsolutePath.Contains("git/trees")?new{truncated=variant=="truncated",tree=source.Files.Select(p=>new{path=p.Key,type="blob",mode="100644",size=Encoding.UTF8.GetByteCount(p.Value),sha=blobs[p.Key]}).ToArray()}
                :new{sha,commit=new{tree=new{sha=tree}}};
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(data))};
        }
        foreach(var variant in new[]{"ok","redirect","missing","tag","truncated"}){
            using var http=new HttpClient(new MockHandler(r=>Response(r,variant)));using var client=new MfgUpstreamSettingsClient(http);
            if(variant=="ok"){Check(client.LatestStableAsync().GetAwaiter().GetResult()=="1.8.2","Latest release not read");Check(client.FetchAsync("1.8.2.0").GetAwaiter().GetResult().Commit==sha,"Valid official metadata rejected");}else{Reject(()=>client.FetchAsync("1.8.2").GetAwaiter().GetResult());if(variant!="truncated")Reject(()=>client.LatestStableAsync().GetAwaiter().GetResult());}
        }
        using var h=new HttpClient(new MockHandler(r=>Response(r,"ok")));using var c=new MfgUpstreamSettingsClient(h);using var cancelled=new CancellationTokenSource();cancelled.Cancel();Reject(()=>c.FetchAsync("1.8.2",cancelled.Token).GetAwaiter().GetResult());Reject(()=>c.FetchAsync("1.8.2-preview").GetAwaiter().GetResult());
        foreach(var declared in new[]{true,false}){
            using var hugeHttp=new HttpClient(new MockHandler(_=>{var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(declared?[1]:new byte[512*1024+1])};if(declared)response.Content.Headers.ContentLength=512*1024+1;return response;}));
            using var huge=new MfgUpstreamSettingsClient(hugeHttp);Reject(()=>huge.FetchAsync("1.8.2").GetAwaiter().GetResult());
        }
        var commitCalls=0;using var movingHttp=new HttpClient(new MockHandler(r=>{
            if(r.RequestUri!.AbsolutePath.Contains("/commits/")&&++commitCalls>1)return new(HttpStatusCode.OK){Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new{sha=new string('f',40)}))};
            return Response(r,"ok");
        }));using var moving=new MfgUpstreamSettingsClient(movingHttp);Reject(()=>moving.FetchAsync("1.8.2").GetAwaiter().GetResult());Check(commitCalls==2,"Moving-tag recheck not reached");
    }
    private static void Localization()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Locale parity broken");var d=new MfgUnlockSettingsService(true,Candidate()).DefinitionsFor("1.8.2").Single(d=>d.Key=="FutureBudget");
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);Check(l.GetCatalog(culture).ContainsKey("Check upstream settings (Development)")&&MfgSettingsDisplay.Description(d,l).Contains("Synthetic declaration"),"Source label/translation lost");}
        var xaml=XDocument.Load(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.xaml"));XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        Check(xaml.Descendants().Any(e=>(string?)e.Attribute(x+"Name")=="CheckUpstreamSettingsButton"&&e.Attribute("Click")?.Value=="CheckUpstreamSettings_Click"),"Button not wired");
        foreach(var name in new[]{"MfgSettingsSaveButton","MfgSettingsDiscardButton","MfgSettingsReloadButton"})Check(xaml.Descendants().Any(e=>(string?)e.Attribute(x+"Name")==name),"Shared workflow removed: "+name);
    }
    private sealed class MockHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(response(request));}}
    internal static int Research(string destination)
    {
        Directory.CreateDirectory(destination);var records=new List<object>();UpstreamSettingsAnalysis? previous=null;
        foreach(var version in Versions){
            using var client=new MfgUpstreamSettingsClient();var source=client.FetchAsync(version).GetAwaiter().GetResult();File.WriteAllText(Path.Combine(destination,version+"-verified-source.json"),JsonSerializer.Serialize(source));
            var a=MfgUpstreamSettingsAnalyzer.Analyze(source);var diff=previous is null?Array.Empty<UpstreamSettingChange>():MfgUpstreamSettingsAnalyzer.Compare(previous,a).ToArray();
            records.Add(new{Version=version,source.Commit,Files=source.Files.Count,Findings=a.Findings.Count,ExplicitTableMetadata=a.Editable.Count,ReadOnly=a.Findings.Count-a.Editable.Count,SchemaRecognized=a.SchemaRecognized,Changes=diff});previous=a;Console.WriteLine($"VERIFIED {version}: {source.Files.Count} source files, {a.Editable.Count} table declarations, {a.Findings.Count-a.Editable.Count} unresolved readers");
        }
        File.WriteAllText(Path.Combine(destination,"HISTORICAL_ANALYSIS.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
    internal static int Reanalyze(string destination)
    {
        var records=new List<object>();UpstreamSettingsAnalysis? previous=null;
        foreach(var version in Versions){var source=JsonSerializer.Deserialize<UpstreamSourceSnapshot>(File.ReadAllText(Path.Combine(destination,version+"-verified-source.json")))!;var a=MfgUpstreamSettingsAnalyzer.Analyze(source);var diff=previous is null?Array.Empty<UpstreamSettingChange>():MfgUpstreamSettingsAnalyzer.Compare(previous,a).ToArray();
            records.Add(new{Version=version,source.Commit,Files=source.Files.Count,Findings=a.Findings.Count,ExplicitTableMetadata=a.Editable.Count,ReadOnly=a.Findings.Count-a.Editable.Count,SchemaRecognized=a.SchemaRecognized,Changes=diff,
                NewEditableAgainstCurrent39=new MfgUnlockSettingsService(true,MfgDynamicSettingsCatalog.ReviewedFallback().WithUpstream(a)).DefinitionsFor(version).Count(d=>d.IsUpstreamSource),
                ReadOnlyFindings=a.Findings.Where(f=>f.Definition is null).Select(f=>new{f.File,f.Section,f.Key,f.Source,f.Line,f.Reason}).ToArray()});previous=a;
        }
        File.WriteAllText(Path.Combine(destination,"HISTORICAL_ANALYSIS.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Reanalyzed six previously verified full source snapshots; no repeat downloads.");return 0;
    }
}

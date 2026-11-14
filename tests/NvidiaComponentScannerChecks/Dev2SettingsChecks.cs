using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Dev2SettingsChecks
{
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return ("dev2 all 38 settings retain exact defaults ranges choices and dependencies", Contract);
        yield return ("dev2 23 friendly names and descriptions cover all six languages", Translations);
        yield return ("dev2 groups classify every setting once and keep diagnostic defaults collapsed", Groups);
        yield return ("dev2 actual row pending values survive group collapse category changes and language refresh", PendingRows);
        yield return ("dev2 grouped XAML retains shared editors details save discard and reload", XamlContract);
        yield return ("dev2 downloaded catalog verifies signature digests and persists through existing store", DownloadAndImport);
        yield return ("dev2 catalog discovery handles unpublished missing and duplicate assets", ReleaseMetadata);
        yield return ("dev2 catalog transport rejects foreign redirects HTTP and excessive content", Transport);
        yield return ("dev2 remote catalog rejects digest byte size signature and schema tampering", Tampering);
        yield return ("dev2 remote cancellation and network errors leave existing catalog untouched", Cancellation);
        yield return ("dev2 catalog signatures cannot be replaced with executable update signatures", Domain);
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is InvalidDataException or InvalidOperationException or JsonException or HttpRequestException or OperationCanceledException or CryptographicException){return;}throw new Exception("Unsafe input accepted");}
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static MfgUnlockSettingDefinition[] Definitions()=>new MfgUnlockSettingsService(false,MfgDynamicSettingsCatalog.BuiltIn).DefinitionsFor("1.8.2").Concat(new MfgUnlockSettingsService(true,MfgDynamicSettingsCatalog.BuiltIn).DefinitionsFor("1.8.2")).ToArray();
    private static void Contract()
    {
        var old=JsonNode.Parse(File.ReadAllText(Path.Combine(Root,"tests/NvidiaComponentScannerChecks/Fixtures/dev1-catalog-contract.json")))!;
        var current=JsonNode.Parse(File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/SettingsCatalog/builtin.json")))!;
        foreach(var node in current["Entries"]!.AsArray())node!.AsObject().Remove("Translations");
        Check(JsonNode.DeepEquals(old,current),"Catalog semantics changed during presentation work");
        foreach(var version in new[]{"1.8.0","1.8.1","1.8.2","1.9.0","2.0.0"})Check(new MfgUnlockSettingsService(false,MfgDynamicSettingsCatalog.BuiltIn).DefinitionsFor(version).Count+new MfgUnlockSettingsService(true,MfgDynamicSettingsCatalog.BuiltIn).DefinitionsFor(version).Count==38,"Missing inherited settings");
        Check(Definitions().Length==38,"Wrong settings total");
    }
    private static void Translations()
    {
        var entries=Definitions().Where(d=>d.CatalogEntry is not null).ToArray();Check(entries.Length==23,"Missing catalog entry");
        foreach(var d in entries)foreach(var culture in UiLocalization.Cultures){var language=new UiLocalization();language.SetCulture(culture,false);var label=MfgSettingsDisplay.Label(d,language);var description=MfgSettingsDisplay.Description(d,language);Check(label!=d.Key&&description!=d.Description&&label.Length<=120&&description.Length>15,"Missing meaningful presentation: "+d.Key+" / "+culture);}
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Language parity failed");
        foreach(var culture in UiLocalization.Cultures)foreach(var key in new[]{"Diagnostic option. Enable only for troubleshooting; diagnostic work can add overhead.","Check catalog updates (Development)","Experimental","Compatibility","Intel XeFG / XeMFG"})Check(l.GetCatalog(culture).ContainsKey(key),"Missing chrome: "+culture);
        foreach(var d in entries.Where(d=>d.CatalogEntry!.Section=="Diagnostics"||d.Key.StartsWith("Vulkan")))Check(MfgSettingsGroupStore.WarningFor(d).Length>0,"Missing risk context");
    }
    private static string FileFor(MfgUnlockSettingDefinition d)=>d.CatalogEntry?.File??(d.Section is not null||d.Key is "Vulkan" or "VulkanPresentMetering" or "UIStabilization" or "XeFGFlipQueue" or "XeFGStock2xModel"?"MFGAdaUnlock.ini":"ReShade.ini");
    private static void Groups()
    {
        var language=new UiLocalization();var groups=new MfgSettingsGroupStore(language);var defs=Definitions();
        foreach(var d in defs){var group=groups.For(FileFor(d),d);Check(ReferenceEquals(group,groups.For(FileFor(d),d)),"Group state recreated");Check(group.DisplayName.Length>0,"No group title");if(group.Id is MfgSettingsGroup.Diagnostics or MfgSettingsGroup.Experimental)Check(!group.IsExpanded,"Secondary group not collapsed");}
        Check(defs.Where(d=>!MfgSettingsLayout.IsMethodKey(d.Key)).GroupBy(d=>(FileFor(d),MfgSettingsLayout.CategoryFor(d),MfgSettingsGroupStore.GroupFor(FileFor(d),d))).Sum(g=>g.Count())==36,"Settings duplicated or omitted");
        Check(MfgSettingsGroupStore.GroupFor("MFGAdaUnlock.ini",defs.Single(d=>d.Key=="ForceDRED"))==MfgSettingsGroup.Diagnostics,"DRED misplaced");
        Check(MfgSettingsGroupStore.GroupFor("MFGAdaUnlock.ini",defs.Single(d=>d.Key=="VulkanDynamicRounding"))==MfgSettingsGroup.Vulkan,"Vulkan misplaced");
    }
    private static void PendingRows()
    {
        // Exercise actual row objects and grouping models without constructing a Window or WPF visual.
        var type=typeof(MainWindow).GetNestedType("MfgUnlockSettingRow",BindingFlags.NonPublic)!;var language=new UiLocalization();var store=new MfgSettingsGroupStore(language);
        foreach(var d in Definitions()){
            var row=Activator.CreateInstance(type,[d,"saved",d.DefaultValue,true,(Action)(()=>{})])!;
            var proposed=type.GetProperty("ProposedValue")!;proposed.SetValue(row,"pending-test-value");var state=store.For(FileFor(d),d);type.GetProperty("Group")!.SetValue(row,state);
            state.IsExpanded=false;
            foreach(var category in Enum.GetValues<MfgSettingsCategory>())foreach(var other in Definitions().Where(x=>MfgSettingsLayout.CategoryFor(x)==category))store.For(FileFor(other),other);
            foreach(var culture in UiLocalization.Cultures){language.SetCulture(culture,false);store.RefreshLanguage();type.GetMethod("RefreshLanguage")!.Invoke(row,null);Check((string?)proposed.GetValue(row)=="pending-test-value","Pending edit lost");if(d.CatalogEntry is {} entry)Check((string?)type.GetProperty("DisplayLabel")!.GetValue(row)==entry.Translations![LocalizedPresentation.Language.CultureName].Label,"Actual row did not use the current localized label");}
            Check(!state.IsExpanded,"Language/category change reset collapse state");state.IsExpanded=true;Check((string?)proposed.GetValue(row)=="pending-test-value","Expand discarded edit");
        }
    }
    private static void XamlContract()
    {
        XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";var xml=XDocument.Load(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.xaml"));
        var page=xml.Descendants().Single(n=>(string?)n.Attribute(x+"Name")=="MfgSettingsPage");
        Check(page.Descendants().Any(n=>n.Name.LocalName=="Expander"&&(string?)n.Attribute("IsExpanded")=="{Binding Name.IsExpanded, Mode=TwoWay}"),"Group state is not bound");
        Check(page.Descendants().Any(n=>n.Name.LocalName=="ItemsPresenter"),"No grouped item presenter");
        foreach(var name in new[]{"MfgSettingsSaveButton","MfgSettingsDiscardButton","MfgSettingsReloadButton","MfgGeneralTab","MfgAdvancedTab","MfgOtherTab"})Check(page.Descendants().Count(n=>(string?)n.Attribute(x+"Name")==name)==1,"Shared control changed or duplicated: "+name);
        var code=File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.MfgCategories.cs"));Check(code.Contains("d.Rows.Where(")&&!code.Contains("IsExpanded"),"Filtered or collapsed controls became edit authority");
        var catalog=File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.SettingsCatalog.cs"));Check(catalog.Contains("ApplySignedSettingsCatalogAsync(result.Json,result.Signature)")&&catalog.Contains("ShowThemeConfirmation")&&catalog.Contains("if(!CatalogChangesAllowed())return;"),"Download bypasses confirmation or pending edit gate");
    }
    private sealed class SignedFixture:IDisposable
    {
        private readonly RSA rsa=RSA.Create(2048);
        internal byte[] Json,Signature;
        internal IReadOnlyDictionary<string,string> Keys;
        internal SignedFixture()
        {
            var original=JsonNode.Parse(File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/SettingsCatalog/builtin.json")))!;original["KeyId"]="dev2-test-only";original["Revision"]=2;
            Json=System.Text.Encoding.UTF8.GetBytes(original.ToJsonString());byte[] payload=[..MfgDynamicSettingsCatalog.SignatureDomain,..Json];Signature=rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
            Keys=new Dictionary<string,string>{{"dev2-test-only",Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())}};
        }
        internal MfgDynamicSettingsCatalog Verify(byte[] json,byte[] sig)=>MfgDynamicSettingsCatalog.Verify(json,sig,Keys);
        internal byte[] WrongDomainSignature()=>rsa.SignData(Json,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
        public void Dispose()=>rsa.Dispose();
    }
    private static string Digest(byte[] bytes)=>"sha256:"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static JsonObject Metadata(SignedFixture s)
    {
        JsonObject Asset(string name,byte[] bytes)=>new(){["name"]=name,["state"]="uploaded",["size"]=bytes.Length,["digest"]=Digest(bytes),["browser_download_url"]=MfgSettingsCatalogUpdateClient.RepositoryUrl+"/releases/download/"+MfgSettingsCatalogUpdateClient.Tag+"/"+name};
        return new(){["tag_name"]=MfgSettingsCatalogUpdateClient.Tag,["draft"]=false,["html_url"]=MfgSettingsCatalogUpdateClient.RepositoryUrl+"/releases/tag/"+MfgSettingsCatalogUpdateClient.Tag,["assets"]=new JsonArray(Asset(MfgSettingsCatalogUpdateClient.JsonName,s.Json),Asset(MfgSettingsCatalogUpdateClient.SignatureName,s.Signature))};
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> run):HttpMessageHandler
    {
        internal List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Requests.Add(request.RequestUri!.AbsoluteUri);return Task.FromResult(run(request,token));}
    }
    private static HttpResponseMessage Bytes(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    private static Handler Responses(SignedFixture s,JsonObject? metadata=null)=>new((r,_)=>r.RequestUri!.AbsoluteUri==MfgSettingsCatalogUpdateClient.MetadataUrl?Bytes(System.Text.Encoding.UTF8.GetBytes((metadata??Metadata(s)).ToJsonString())):r.RequestUri.AbsolutePath.EndsWith(".sig",StringComparison.Ordinal)?Bytes(s.Signature):Bytes(s.Json));
    private static DownloadedSettingsCatalog? Fetch(Handler handler,Func<byte[],byte[],MfgDynamicSettingsCatalog>? verify=null){using var http=new HttpClient(handler);using var client=new MfgSettingsCatalogUpdateClient(http,verify);return client.CheckAsync().GetAwaiter().GetResult();}
    private static void DownloadAndImport()
    {
        using var s=new SignedFixture();var directory=Path.Combine(Path.GetTempPath(),"RenoDX-dev2-catalog",Guid.NewGuid().ToString("N"));try{
            var package=Fetch(Responses(s),s.Verify)!;Check(!Directory.Exists(directory),"Downloader wrote files before consent");var store=new MfgSettingsCatalogStore(directory,s.Keys);store.Import(package.Json,package.Signature);Check(store.Load().Revision==2,"Verified package not reusable by import");
            var metadata=Metadata(s);foreach(var a in metadata["assets"]!.AsArray())a!.AsObject().Remove("digest");Check(Fetch(Responses(s,metadata),s.Verify)!.Catalog.Revision==2,"Missing API digest disabled signature verification");
        }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private static void ReleaseMetadata()
    {
        using var s=new SignedFixture();Check(Fetch(new((_,_)=>new(HttpStatusCode.NotFound)),s.Verify) is null,"Unpublished channel considered an update");
        foreach(var change in new Action<JsonObject>[] {m=>m["draft"]=true,m=>m["tag_name"]="v1.3.0",m=>m["html_url"]="https://evil.invalid",m=>m["assets"]!.AsArray().RemoveAt(1),m=>m["assets"]!.AsArray().Add(m["assets"]![0]!.DeepClone()),m=>m["assets"]![0]!["browser_download_url"]="https://evil.invalid/catalog.json"}){var metadata=Metadata(s);change(metadata);Reject(()=>Fetch(Responses(s,metadata),s.Verify));}
    }
    private static void Transport()
    {
        using var s=new SignedFixture();
        foreach(var url in new[]{"http://release-assets.githubusercontent.com/file","https://evil.invalid/file","https://user@release-assets.githubusercontent.com/file","https://release-assets.githubusercontent.com:444/file"}){
            using var baseHandler=Responses(s);var h=new Handler((r,t)=>{if(r.RequestUri!.AbsoluteUri==MfgSettingsCatalogUpdateClient.MetadataUrl)return Bytes(System.Text.Encoding.UTF8.GetBytes(Metadata(s).ToJsonString()));var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri(url);return response;});Reject(()=>Fetch(h,s.Verify));Check(h.Requests.Count==2,"Unsafe redirect was followed");
        }
        var redirect=new Handler((r,_)=>{if(r.RequestUri!.AbsoluteUri==MfgSettingsCatalogUpdateClient.MetadataUrl)return Bytes(System.Text.Encoding.UTF8.GetBytes(Metadata(s).ToJsonString()));if(r.RequestUri.Host=="github.com"){var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://release-assets.githubusercontent.com/fixture/"+Path.GetFileName(r.RequestUri.AbsolutePath));return response;}return Bytes(r.RequestUri.AbsolutePath.EndsWith(".sig")?s.Signature:s.Json);});Check(Fetch(redirect,s.Verify) is not null,"Allowed bounded CDN redirect failed");
        var metadata=Metadata(s);metadata["assets"]![0]!["size"]=MfgDynamicSettingsCatalog.MaximumBytes+1;Reject(()=>Fetch(Responses(s,metadata),s.Verify));
        var oversize=new Handler((r,_)=>r.RequestUri!.AbsoluteUri==MfgSettingsCatalogUpdateClient.MetadataUrl?Bytes(System.Text.Encoding.UTF8.GetBytes(Metadata(s).ToJsonString())):Bytes(new byte[s.Json.Length+1]));Reject(()=>Fetch(oversize,s.Verify));
    }
    private static void Tampering()
    {
        using var s=new SignedFixture();Reject(()=>Fetch(Responses(s))); // Actual production trust refuses all test keys.
        foreach(var mutate in new Action<JsonObject>[] {m=>m["assets"]![0]!["digest"]="sha256:"+new string('0',64),m=>m["assets"]![0]!["size"]=s.Json.Length-1,m=>m["assets"]![1]!["digest"]="md5:bad"}){var m=Metadata(s);mutate(m);Reject(()=>Fetch(Responses(s,m),s.Verify));}
        var original=s.Signature;s.Signature=(byte[])original.Clone();s.Signature[0]^=1;Reject(()=>Fetch(Responses(s),s.Verify));s.Signature=original;
        s.Json=(byte[])s.Json.Clone();s.Json[10]^=1;Reject(()=>Fetch(Responses(s),s.Verify));
    }
    private static void Cancellation()
    {
        using var s=new SignedFixture();using var cts=new CancellationTokenSource();var handler=new Handler((r,_)=>{if(r.RequestUri!.AbsoluteUri==MfgSettingsCatalogUpdateClient.MetadataUrl){cts.Cancel();return Bytes(System.Text.Encoding.UTF8.GetBytes(Metadata(s).ToJsonString()));}throw new Exception("Download followed cancellation");});using var http=new HttpClient(handler);using var client=new MfgSettingsCatalogUpdateClient(http,s.Verify);Reject(()=>client.CheckAsync(cts.Token).GetAwaiter().GetResult());Check(handler.Requests.Count==1,"Cancellation ignored");
        Reject(()=>Fetch(new((_,_)=>throw new HttpRequestException("Synthetic network failure")),s.Verify));
    }
    private static void Domain()
    {
        using var s=new SignedFixture();s.Signature=s.WrongDomainSignature();Reject(()=>Fetch(Responses(s),s.Verify));Check(MfgDynamicSettingsCatalog.Purpose=="renodx-mfg-settings-catalog"&&SelfUpdateTrust.Keys.Count==1&&SelfUpdateTrust.Keys.ContainsKey("owner-release-2026"),"Trust or purpose changed");
    }
}

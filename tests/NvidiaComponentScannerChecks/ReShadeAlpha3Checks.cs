using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RenoDXMfgUnlocker;

internal static class ReShadeAlpha3Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("ReShade alpha3 R9 component identity is independent of structure loading and addon activity",Evidence);
        yield return("ReShade alpha3 current older newer mixed and vendor versions remain advisory only",Versions);
        yield return("ReShade alpha3 R10 six-language prefixed reasons translate without changing paths or metadata",Reasons);
        yield return("ReShade alpha3 R11 R12 package and game state stay separate with contextual warnings",UiState);
        yield return("ReShade alpha3 R12 genuine official installer cache survives game switches without download",RealCache);
        yield return("ReShade alpha3 missing corrupted and cancelled cache lookup never redownloads or rewrites games",CacheFailures);
        yield return("ReShade alpha3 compiled reviewed catalog retains immutable pins and rejects arbitrary packages",Catalog);
        yield return("ReShade alpha3 R13 CA wording and new update labels have six-language coverage",Localization);
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static string Temp(){var p=Path.Combine(Path.GetTempPath(),"RenoDX-ReShade-alpha3",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static ReShadeFileEvidence Full(string version="6.8.0.2155")=>new(@"C:\own\dxgi.dll",version,ReShadeReviewedCatalog.Current.Runtimes[0].Sha256,true,true,Kind:ReShadeProxyKind.FullAddonVerified);
    private static ReShadeDetectionReport Report(params ReShadeFileEvidence[] files)=>new(files.Any(f=>f.BlocksGuidedSetup)?ReShadeDetectionState.Conflict:ReShadeDetectionState.FullAddonVerified,files,[],[]);
    private static void Evidence()
    {
        var l=new UiLocalization();var full=Full();var third=new ReShadeFileEvidence(@"C:\own\d3d12.dll","1.4.3.0",new string('a',64),false,true,ProductName:"SCSKiller",FileDescription:"SCSKiller D3D12 proxy",Kind:ReShadeProxyKind.IdentifiedThirdParty,BlocksGuidedSetup:true);
        var report=Report(full,third);var text=ReShadeUpdatePolicy.EvidenceLevels(report,l);
        Check(text.Contains("1 verified file(s)")&&text.Contains("structure: requires review")&&text.Contains("Runtime module loaded: not checked")&&text.Contains("Addon operation in game: not checked"),"Full component erased or promoted by overall block");
        Check(!text.Contains("Full Add-on Support: Not verified")&&report.HasConflicts,"Contradictory full-support summary or relaxed block");
        foreach(var r in new ReShadeDetectionReport?[]{null,new(ReShadeDetectionState.NotFound,[],[],[]),Report(full with {Kind=ReShadeProxyKind.StandardVerified,ReviewedStandard=true}),Report(full with {Kind=ReShadeProxyKind.RecognizedReShade})})Check(ReShadeUpdatePolicy.EvidenceLevels(r,l).Contains("no verified component"),"Standard/unknown/missing promoted to full");
    }
    private static void Versions()
    {
        foreach(var(version,expected) in new[]{("6.8.0.2155",ReShadeVersionRelation.Current),("6.8.0",ReShadeVersionRelation.Current),("6.7.9.9999",ReShadeVersionRelation.Older),("6.9.0.0",ReShadeVersionRelation.Newer),("6.8.0-vendor",ReShadeVersionRelation.Unknown),("6.8",ReShadeVersionRelation.Unknown),("",ReShadeVersionRelation.Unknown)})
        {var r=Report(Full(version));var original=JsonSerializer.Serialize(r);var a=ReShadeUpdatePolicy.Assess(r);Check(a.Relation==expected&&a.AvailableVersion=="6.8.0"&&a.InstalledVersions.Single()==version,"Version relationship wrong: "+version);Check(JsonSerializer.Serialize(r)==original,"Assessment mutated evidence");}
        Check(ReShadeUpdatePolicy.Assess(Report(Full(),Full("6.7.0"))).Relation==ReShadeVersionRelation.Mixed,"Divergent releases hidden");Check(ReShadeUpdatePolicy.Assess(new(ReShadeDetectionState.NotFound,[],[],[])).Relation==ReShadeVersionRelation.NotDetected,"Missing release inferred");
        Check(ReShadeUpdatePolicy.Assess(Report(Full("6.9.0"))).NextAction.Contains("No automatic downgrade"),"Newer release requests blind replacement");Check(ReShadeUpdatePolicy.Assess(Report(Full("6.7.0"))).NextAction.Contains("compatibility is not established"),"Older release implied addon compatibility");
        var blocked=Report(Full() with {BlocksGuidedSetup=true});Check(ReShadeUpdatePolicy.Assess(blocked).NextAction == ReShadeManualLaunchPolicy.Warning,"Current version bypassed conflict warning");
    }
    private static void Reasons()
    {
        var l=new UiLocalization();const string reason="Product metadata identifies SCSKiller D3D12 proxy. Publisher authenticity and compatibility are not independently verified.";
        var f=new ReShadeFileEvidence(@"C:\own\Reason\d3d12.dll","1.4.3.0",new string('a',64),false,true,Kind:ReShadeProxyKind.IdentifiedThirdParty,Reason:reason,BlocksGuidedSetup:true);
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);var text=ReShadeUpdatePolicy.LocalizeAppText(ReShadePresentation.BlockReason(Report(f)),l);Check(text.Contains(l.Lookup("Reason")+": "+l.Lookup(reason)),"Prefixed reason not translated: "+culture);Check(text.Contains(f.Path)&&text.Contains(f.Version),"Opaque path/version rewritten");}
        Check(f.Reason==reason,"Service reason localized in place");
    }
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static void Set(MainWindow main,string name,object? value)=>typeof(MainWindow).GetField(name,Flags)!.SetValue(main,value);
    private static object? Get(MainWindow main,string name)=>typeof(MainWindow).GetField(name,Flags)!.GetValue(main);
    private static object? Call(MainWindow main,string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Flags)!.Invoke(main,args);
    private static IEnumerable<TextBlock> Texts(DependencyObject parent){for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var c=VisualTreeHelper.GetChild(parent,i);if(c is TextBlock t)yield return t;foreach(var t2 in Texts(c))yield return t2;}}
    private static void UiState()=>WpfCheckHost.Run(()=>
    {
        LocalizedPresentation.Language.SetCulture("en-US",false);var main=new MainWindow();try
        {
            var view=(ReShadeManagerView)main.FindName("ReShadeView");Check(view.PrepareButton.IsEnabled&&view.ImportButton.IsEnabled&&!view.RunButton.IsEnabled,"Package preparation requires a game or unsafe launch enabled");Check(view.AntiCheatPanel.Visibility==Visibility.Collapsed&&view.GeneralUsageWarning.Visibility==Visibility.Visible,"Unknown game gets large contextual warning");
            var p=new PreparedReShadeInstaller("Own presentation fixture","6.8.0",ReShadeInstallerVerifier.Reviewed.Sha256,4318424);Set(main,"reshadePrepared",p);
            var combo=(ComboBox)main.FindName("GamesCombo");var list=(IList)combo.ItemsSource;
            object Entry(string id){var root=Temp();File.WriteAllText(Path.Combine(root,"Own.exe"),"Own synthetic EXE; never executed");return Activator.CreateInstance(typeof(MainWindow).GetNestedType("Game",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,["Own fixture",root,id,null],null)!;}
            var a=Entry("manual");var b=Entry("2807960");list.Add(a);list.Add(b);combo.SelectedItem=a;view.ApiPicker.SelectedIndex=1;Call(main,"UpdateReShadeContext");Check(view.ApiPicker.SelectedIndex==1&&ReferenceEquals(Get(main,"reshadePrepared"),p),"Duplicate context loses explicit API or shared package");combo.SelectedItem=b;
            Check(ReferenceEquals(Get(main,"reshadePrepared"),p)&&view.ApiPicker.SelectedIndex==-1&&Get(main,"reshadeReport") is null,"Game switch lost package or leaked scan");Check(view.AntiCheatPanel.Visibility==Visibility.Visible&&view.GeneralUsageWarning.Visibility==Visibility.Collapsed&&view.UsageWarning.Text.Contains("EA Javelin Anticheat"),"Known anti-cheat context not prominent or official name incorrect");
            combo.SelectedItem=a;Check(view.AntiCheatPanel.Visibility==Visibility.Collapsed&&!view.RunButton.IsEnabled,"Unknown context approved or shared package bypassed preflight");
            var raw="Reason: vendor text 6,8,0; "+ReShadeInstallerVerifier.Reviewed.Sha256;Set(main,"reshadeReport",Report(Full() with {ProductName=raw,FileDescription="Select graphics API"}));var details=(FrameworkElement)Call(main,"ReShadeDetailsContent")!;
            var values=Texts(details).ToArray();Check(values.Any(t=>t.Text==raw&&LocalizedPresentation.GetPreserveOriginal(t))&&values.Any(t=>t.Text=="Select graphics API"&&LocalizedPresentation.GetPreserveOriginal(t)),"Vendor metadata is passed through automatic translation");
        }
        finally{Set(main,"reshadeBusy",false);main.Close();}
    });
    private sealed class NoNetwork:HttpMessageHandler{public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;throw new Exception("Cache lookup must not download");}}
    private static string GenuineInstaller(){var path=Environment.GetEnvironmentVariable("RENODX_TEST_RESHADE_INSTALLER");if(path is null||!File.Exists(path))throw new SkipCheckException("Real reviewed installer fixture required to claim cross-game cache reuse.");return path;}
    private static void RealCache()
    {
        var network=new NoNetwork();var cache=new ReShadePackageService(Temp(),new HttpClient(network));var a=Temp();var b=Temp();File.WriteAllText(Path.Combine(a,"ReShade.ini"),"; preserve A");File.WriteAllText(Path.Combine(b,"Preset.ini"),"; preserve B");
        var imported=cache.PrepareAsync(null,localInstaller:GenuineInstaller()).GetAwaiter().GetResult();var hash=Hash(imported.Path);var first=cache.FindCachedAsync(a).GetAwaiter().GetResult();var second=cache.FindCachedAsync(b).GetAwaiter().GetResult();var noGame=cache.FindCachedAsync().GetAwaiter().GetResult();
        Check(first==imported&&second==imported&&noGame==imported&&network.Calls==0&&Hash(imported.Path)==hash,"Genuine package not reused/reverified without download");Check(File.ReadAllText(Path.Combine(a,"ReShade.ini"))=="; preserve A"&&File.ReadAllText(Path.Combine(b,"Preset.ini"))=="; preserve B","Cache modified game configurations");Console.WriteLine("EVIDENCE alpha3 genuine reviewed ReShade 6.8.0 package imported to isolated cache; two synthetic game roots and no-game lookup reverified identical bytes; 0 network calls.");
    }
    private static void CacheFailures()
    {
        var network=new NoNetwork();var cache=new ReShadePackageService(Temp(),new HttpClient(network));Check(cache.FindCachedAsync().GetAwaiter().GetResult() is null&&network.Calls==0,"Missing cache triggered download");
        using var token=new CancellationTokenSource();token.Cancel();try{cache.FindCachedAsync(token:token.Token).GetAwaiter().GetResult();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        Directory.CreateDirectory(Path.GetDirectoryName(cache.InstallerPath)!);File.WriteAllText(cache.InstallerPath,"Own damaged cached package");var before=Hash(cache.InstallerPath);try{cache.FindCachedAsync(Temp()).GetAwaiter().GetResult();throw new Exception("Corrupt cache accepted");}catch(InvalidDataException){}Check(Hash(cache.InstallerPath)==before&&network.Calls==0,"Corrupt cache silently replaced/downloaded");
    }
    private static void Catalog()
    {
        Check(ReShadeReviewedCatalog.SchemaVersion==1&&ReShadeReviewedCatalog.Packages.Count==1&&ReShadeReviewedCatalog.Current.Installer==ReShadeInstallerVerifier.Reviewed,"Catalog changed package identity or trusts unknown packages");Check(ReShadeInstallerVerifier.Reviewed.Sha256=="AFE4C8F13048306307983B8B3D41D5BF00A86820440B0E57DEA10950E1176445"&&ReShadeInstallerVerifier.Reviewed.PublisherThumbprint=="589690208A5E52FB96980C4A6698F50ACD47C49F","Existing pins changed");
        try{((IDictionary<string,ReShadeDetectionState>)ReShadeReviewedCatalog.RuntimeHashes).Add("arbitrary",ReShadeDetectionState.FullAddonVerified);throw new Exception("Runtime catalog mutable");}catch(NotSupportedException){}
        Check(!ReShadePackageService.IsReviewedSource("https://reshade.me/downloads/ReShade_Setup_99.0.0_Addon.exe"),"Future filename automatically trusted");var path=Path.Combine(Temp(),"ReShade_Setup_99.0.0_Addon.exe");File.WriteAllText(path,"untrusted");try{new ReShadeInstallerVerifier().Verify(path);throw new Exception("Unreviewed local package accepted");}catch(InvalidDataException){}
    }
    private static void Localization()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Catalogs/format placeholders invalid");var messages=new List<string>{"A. Manage ReShade installer","B. Selected game and graphics API","Selected game: installation evidence","Review and run official installation / update","Package preparation is independent of game selection. Checked launch below still requires the selected EXE/API and a fresh scan.","EXE/API here are preflight inputs. Official Setup requires manual selection again because no supported preconfiguration contract was confirmed.","Anti-cheat permission is unverified. Check publisher rules before online use.","Existing cached Full Add-on installer reverified. No download was needed.","Cached installer could not be verified. Review package Details; no replacement was downloaded.","Checking the existing installer cache. No download or game changes.","Full Add-on component identity: {0} verified file(s).","Full Add-on component identity: no verified component recorded.","Complete installation structure: requires review; proxy/configuration checks are unresolved.","Complete installation structure: not independently verified.","Runtime module loaded: not checked by this file scan.","Addon operation in game: not checked by this file scan.","Installed ReShade: {0}","Available reviewed installer: {0}","Current release number","Older release","Newer than reviewed release","Mixed release versions","Installation evidence","Original diagnostics",ReShadeUsagePolicy.ForSteamAppId("2807960").Message};
        foreach(var r in new ReShadeDetectionReport?[]{null,new(ReShadeDetectionState.NotFound,[],[],[]),Report(Full()),Report(Full("6.7.0")),Report(Full("6.9.0")),Report(Full("vendor")),Report(Full() with {BlocksGuidedSetup=true})})messages.Add(ReShadeUpdatePolicy.Assess(r).NextAction);
        foreach(var text in messages.Distinct()){Check(l.GetCatalog("en-US").ContainsKey(text),"Missing new key: "+text);foreach(var culture in UiLocalization.Cultures.Skip(1))Check(l.Lookup(text,culture)!=text,"Untranslated new text: "+culture+" / "+text);}
        Check(l.Lookup("Not independently established","uk-UA")=="Не підтверджено незалежною перевіркою","R13 Ukrainian wording regressed");
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);Check(l.Translate(ReShadeUsagePolicy.ForSteamAppId("2807960").Message).Contains("EA Javelin Anticheat"),"Official anti-cheat name changed");var versions=ReShadeUpdatePolicy.Describe(Report(Full()),l);Check(versions.Contains("6.8.0.2155")&&versions.Contains("6.8.0"),"Version display changed under locale");}
    }
}

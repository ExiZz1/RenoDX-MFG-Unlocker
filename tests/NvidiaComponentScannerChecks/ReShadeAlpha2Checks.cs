using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using RenoDXMfgUnlocker;

internal static class ReShadeAlpha2Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("ReShade R1 R3 R8 alpha2 headless API selection and game switching reset stale evidence",Context);
        yield return("ReShade R3 alpha2 scan lifecycle distinguishes unscanned busy missing known conflicting and failed",ScanStates);
        yield return("ReShade R4 alpha2 each synthetic proxy retains independent identity and reason",FileEvidence);
        yield return("ReShade R4 alpha2 SCSKiller metadata naming and version cannot grant compatibility",ScsMetadata);
        yield return("ReShade R7 alpha2 exact hard block survives direct service and checkbox bypass attempts",Block);
        yield return("ReShade R5 R6 alpha2 package hash publisher and Windows chain statuses stay independent",Trust);
        yield return("ReShade R8 alpha2 multiplayer evidence is app-ID based and unknown titles are not approved",Warnings);
        yield return("ReShade R5 alpha2 all new text and split Details labels have six-language coverage",Localization);
    }
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static string Temp(){var p=Path.Combine(Path.GetTempPath(),"RenoDX-ReShade-alpha2",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static (string Root,string Exe) Game(){var root=Temp();var exe=Path.Combine(root,"OwnSynthetic.exe");File.Copy(Environment.ProcessPath!,exe);return(root,exe);}
    private static ReShadeFileEvidence Scs(string path)=>new(path,"1.4.3.0",new string('b',64),false,true,ProductName:"SCSKiller",FileDescription:"SCSKiller D3D12 proxy");
    private static ReShadeDetectionService Detector(Func<string,ReShadeFileEvidence> inspect)=>new(inspect,new Dictionary<string,ReShadeDetectionState>{{new string('a',64),ReShadeDetectionState.FullAddonVerified}},Path.Combine(Temp(),"NoLayer.dll"));
    private static object? Get(MainWindow main,string name)=>typeof(MainWindow).GetField(name,Flags)!.GetValue(main);
    private static void Set(MainWindow main,string name,object? value)=>typeof(MainWindow).GetField(name,Flags)!.SetValue(main,value);
    private static object? Call(MainWindow main,string method,params object?[] args)=>typeof(MainWindow).GetMethod(method,Flags)!.Invoke(main,args);
    private static void Context()=>WpfCheckHost.Run(()=>
    {
        // Construct and inspect WPF state only. No Show, native input, screenshot or visual review.
        var main=new MainWindow();try
        {
            var view=(ReShadeManagerView)main.FindName("ReShadeView");Check(view.ApiPicker.SelectedIndex==-1&&view.ApiPlaceholder.Visibility==Visibility.Visible&&!view.RunButton.IsEnabled,"Empty API state lost");
            var a=Game();var b=Game();var combo=(ComboBox)main.FindName("GamesCombo");var list=(IList)combo.ItemsSource;
            object Entry(string title,string root,string id)=>Activator.CreateInstance(typeof(MainWindow).GetNestedType("Game",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[title,root,id,null],null)!;
            var first=Entry("Own multiplayer metadata fixture",a.Root,"2807960");var second=Entry("Own unknown fixture",b.Root,"manual");list.Add(first);list.Add(second);combo.SelectedItem=first;
            Check(view.ApiPicker.SelectedIndex==-1&&!view.ScanButton.IsEnabled&&view.UsageWarning.Text.Contains("EA Javelin"),"Game silently confirmed API or lost known anti-cheat warning");
            view.ApiPicker.SelectedIndex=1;Check(view.ApiPlaceholder.Visibility==Visibility.Collapsed&&view.ScanButton.IsEnabled,"Explicit API did not enable scan");Call(main,"UpdateReShadeContext");Check(view.ApiPicker.SelectedIndex==1,"Reopening same context reset explicit API");
            var raw=Scs(Path.Combine(a.Root,"d3d12.dll"));var file=Detector(_=>raw).Classify(raw);Set(main,"reshadeReport",new ReShadeDetectionReport(ReShadeDetectionState.Conflict,[file],[],[]));Set(main,"reshadeScanPhase",ReShadeScanPhase.Completed);Set(main,"reshadePrepared",new PreparedReShadeInstaller("Never executed","6.8.0",new string('a',64),4318424));view.Consent.IsChecked=true;Call(main,"RenderReShade");
            Check(!view.RunButton.IsEnabled&&view.LaunchHint.Text.Contains("d3d12.dll")&&view.LaunchHint.Text.Contains(a.Root),"Checkbox bypassed block or exact reason missing");((Task)Call(main,"RunReShadeAsync")!).GetAwaiter().GetResult();
            Set(main,"reshadeLastError","own stale error");combo.SelectedItem=second;
            Check(Get(main,"reshadeReport") is null&&Get(main,"reshadeLastRun") is null&&Equals(Get(main,"reshadeScanPhase"),ReShadeScanPhase.NotScanned)&&Equals(Get(main,"reshadeLastError"),""),"Stale scan/error leaked across games");
            Check(view.ApiPicker.SelectedIndex==-1&&view.ApiPlaceholder.Visibility==Visibility.Visible&&view.Consent.IsChecked!=true&&view.VulkanConsent.IsChecked!=true&&!view.RunButton.IsEnabled,"API/approval leaked to another game");
            Check(!view.UsageWarning.Text.Contains("EA Javelin")&&view.UsageWarning.Text.Contains("not established"),"Known warning leaked or unknown game approved");
            combo.SelectedItem=first;Check(view.ApiPicker.SelectedIndex==-1&&Get(main,"reshadeReport") is null,"Returning to game silently reused a scan/API");
        }
        finally{Set(main,"reshadeBusy",false);main.Close();}
    });
    private static void ScanStates()
    {
        var values=new List<string>();foreach(var phase in new[]{ReShadeScanPhase.NotScanned,ReShadeScanPhase.InProgress,ReShadeScanPhase.Failed})values.Add(ReShadePresentation.ScanText(phase,null));
        foreach(var state in new[]{ReShadeDetectionState.NotFound,ReShadeDetectionState.FullAddonVerified,ReShadeDetectionState.StandardVerified,ReShadeDetectionState.DetectedUnknownSupport,ReShadeDetectionState.Conflict,ReShadeDetectionState.NotVerified})values.Add(ReShadePresentation.ScanText(ReShadeScanPhase.Completed,new(state,[],[],[])));
        Check(values.Distinct().Count()==9&&values.All(v=>!v.Contains("compatible",StringComparison.OrdinalIgnoreCase)),"Lifecycle states collapsed or claimed compatibility");Check(values[4].Contains("Runtime loading is not confirmed"),"Full binary scan claimed runtime loading");
    }
    private static void FileEvidence()
    {
        var(root,exe)=Game();var full=Path.Combine(root,"dxgi.dll");var third=Path.Combine(root,"d3d12.dll");File.WriteAllText(full,"Own fake ReShade fixture");File.WriteAllText(third,"Own fake proxy fixture");var original=new[]{Hash(full),Hash(third)};
        var report=Detector(p=>p==full?new(p,"6.8.0.2155",new string('a',64),true,true):Scs(p)).Scan(root,exe,ReShadeGraphicsApi.Direct3D12);
        Check(report.HasConflicts&&report.Files.Count==2,"Mixed proxy set incorrectly accepted");var known=report.Files.Single(f=>f.Path==full);var foreign=report.Files.Single(f=>f.Path==third);
        Check(known.Kind==ReShadeProxyKind.FullAddonVerified&&!known.BlocksGuidedSetup&&foreign.Kind==ReShadeProxyKind.IdentifiedThirdParty&&foreign.BlocksGuidedSetup,"General conflict hid individual identities");
        Check(ReShadePresentation.FileDetails(foreign).Contains("SCSKiller")&&foreign.Reason.Contains("not independently verified"),"Metadata declared authenticated");Check(Hash(full)==original[0]&&Hash(third)==original[1],"Scan wrote synthetic files");
    }
    private static void ScsMetadata()
    {
        var sample=Scs(Path.Combine(Temp(),"d3d12.dll"));var d=Detector(_=>sample);
        foreach(var f in new[]{sample with {ProductName=""},sample with {FileDescription=""},sample with {ProductName="different publisher"},sample with {IsNativeX64Dll=false}})Check(d.Classify(f).Kind!=ReShadeProxyKind.IdentifiedThirdParty&&d.Classify(f).BlocksGuidedSetup,"Filename/version/partial metadata identified trusted SCSKiller");
        var spoof=d.Classify(sample with {Version="999.1",Kind=ReShadeProxyKind.FullAddonVerified,BlocksGuidedSetup=false});Check(spoof.Kind==ReShadeProxyKind.IdentifiedThirdParty&&spoof.BlocksGuidedSetup&&spoof.Reason.Contains("compatibility"),"Metadata spoof or injected display classification bypassed safety");
        Check(d.Classify(sample with {ProductName="scskiller",FileDescription="scskiller d3d12 PROXY"}).Kind==ReShadeProxyKind.IdentifiedThirdParty,"Metadata match incorrectly case-sensitive");
    }
    private sealed class NeverProcess:IReShadeInstallerProcess{public int Calls;public Task<int?> RunAsync(string path){Calls++;throw new Exception("Hard block was bypassed");}}
    private static void Block()
    {
        var(root,exe)=Game();var path=Path.Combine(root,"d3d12.dll");File.WriteAllText(path,"own third-party bytes");var before=Hash(path);var d=Detector(Scs);var process=new NeverProcess();var service=new ReShadeGuidedInstaller(new ReShadePackageService(Temp()),d,process,_=>false);
        try{service.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,new("unverified path","6.8.0","bad hash",0),true,true).GetAwaiter().GetResult();throw new Exception("Service did not reject proxy");}
        catch(InvalidDataException ex){Check(ex.Message.Contains(path)&&ex.Message.Contains("d3d12.dll")&&ex.Message.Contains("1.4.3.0")&&ex.Message.Contains("SCSKiller")&&ex.Message.Contains("Review Details"),"Exact actionable blocking reason lost");}
        Check(process.Calls==0&&Hash(path)==before,"Service started external installer or changed file despite block");
    }
    private static void Trust()
    {
        var before=ReShadePresentation.PackageDetails(null);Check(before.Contains("Package SHA-256 check: Not checked")&&before.Contains("Signature and publisher pin: Not checked"),"Unprepared package claimed verified");
        var prepared=new PreparedReShadeInstaller("Own presentation fixture","6.8.0",ReShadeInstallerVerifier.Reviewed.Sha256,ReShadeInstallerVerifier.Reviewed.Size);var text=ReShadePresentation.PackageDetails(prepared);
        Check(text.Contains("Package SHA-256 check: Verified at last preparation")&&text.Contains("Signature and publisher pin: Verified at last preparation")&&text.Contains("Windows certificate-chain trust: Not independently established"),"Trust concepts conflated");
        Check(text.Contains("does not establish that the reviewed package is unsafe")&&text.Contains("All package checks are repeated before launch"),"Misleading unsafe label or missing recheck explanation");
        Check(text.Contains(ReShadeInstallerVerifier.Reviewed.Sha256)&&text.Contains(ReShadeInstallerVerifier.Reviewed.PublisherThumbprint),"Technical pins changed");
    }
    private static void Warnings()
    {
        var known=ReShadeUsagePolicy.ForSteamAppId("2807960");Check(known.KnownAntiCheatConcern&&known.Message.Contains("not recommended")&&known.EvidenceUrl!.StartsWith("https://help.ea.com/"),"Publisher warning evidence missing");
        foreach(var id in new string?[]{null,"manual","292030","bf6.exe","The Witcher 3","anything"}){var other=ReShadeUsagePolicy.ForSteamAppId(id);Check(!other.KnownAntiCheatConcern&&other.EvidenceUrl is null&&other.Message.Contains("not established")&&other.Message.Contains("does not mean"),"Filename guessed safety/anti-cheat or unknown profile approved");}
        Check(ReShadeUsagePolicy.GeneralWarning.Contains("may trigger")&&ReShadeUsagePolicy.GeneralWarning.Contains("no game is approved"),"Scan represented multiplayer permission");
    }
    private static void Localization()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Catalogs differ or placeholders invalid");
        var texts=new List<string>{"Select graphics API","Publisher certificate","Classification","ReShade status","Product metadata","File description","Reviewed package SHA-256","Package SHA-256 check","Signature and publisher pin","Windows certificate-chain trust","Not independently established","Verified at last preparation","Prepare the package before claiming hash or publisher verification.","The package matches the reviewed SHA-256 and publisher certificate. Full Windows certificate-chain trust was not independently established.","No certificate is installed. Unverified Windows CA trust alone does not establish that the reviewed package is unsafe. All package checks are repeated before launch.","External installer and Restore","Multiplayer / anti-cheat warning","Select this exact EXE and API manually in the official installer. Its original warnings remain visible.",ReShadeUsagePolicy.GeneralWarning,ReShadeUsagePolicy.ForSteamAppId("2807960").Message,ReShadeUsagePolicy.ForSteamAppId(null).Message};
        foreach(var phase in new[]{ReShadeScanPhase.NotScanned,ReShadeScanPhase.InProgress,ReShadeScanPhase.Failed})texts.Add(ReShadePresentation.ScanText(phase,null));foreach(var state in Enum.GetValues<ReShadeDetectionState>())texts.Add(ReShadePresentation.ScanText(ReShadeScanPhase.Completed,new(state,[],[],[])));foreach(var kind in Enum.GetValues<ReShadeProxyKind>())texts.Add(ReShadePresentation.KindText(kind));
        var sample=Scs(@"C:\own\game\d3d12.dll");var d=Detector(_=>sample);texts.Add(d.Classify(sample).Reason);texts.Add(d.Classify(sample with {ProductName=""}).Reason);texts.Add(d.Classify(sample with {IsNativeX64Dll=false}).Reason);texts.Add(d.Classify(sample with {IsReShade=true}).Reason);texts.Add(d.Classify(sample with {IsReShade=true,Sha256=new string('a',64)}).Reason);
        foreach(var text in texts.Distinct()){Check(l.GetCatalog("en-US").ContainsKey(text),"New text lacks central key: "+text);foreach(var culture in UiLocalization.Cultures.Skip(1))Check(l.Lookup(text,culture)!=text||(culture=="fr-FR"&&text=="Classification"),"Untranslated new text: "+culture+" / "+text);}
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);var details=l.Translate(ReShadePresentation.PackageDetails(new("own","6.8.0",ReShadeInstallerVerifier.Reviewed.Sha256,4318424)));Check(details.Contains(ReShadeInstallerVerifier.Reviewed.Sha256)&&details.Contains(ReShadeInstallerVerifier.Reviewed.PublisherThumbprint)&&details.Contains("6.8.0"),"Locale changed technical pin/version");var block=l.Translate(ReShadePresentation.BlockReason(new(ReShadeDetectionState.Conflict,[d.Classify(sample)],[],[])));Check(block.Contains(sample.Path)&&block.Contains("d3d12.dll")&&block.Contains("1.4.3.0"),"Localization changed blocked file/path/version");}
    }
}

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using RenoDXMfgUnlocker;

internal static class ReShadeAlpha4Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("ReShade alpha4 reviewed binary IL confirms argument dispatch and ANSI PEInfo imports",Binary);
        yield return("ReShade alpha4 cached isolated CLI observations prove overwrite and Unicode hazards",Observations);
        yield return("ReShade alpha4 production process retains visible no-argument launch only",ProductionLaunch);
        yield return("ReShade alpha4 ANSI path preflight blocks lossy paths independently of UI culture",Paths);
        yield return("ReShade alpha4 unsafe path cannot bypass direct service through consent",ServicePathBlock);
        yield return("ReShade alpha4 R14 R17 summary and action captions separate current update and blocked states",Summaries);
        yield return("ReShade alpha4 R15 R16 headless view retains compact summary and collapsed technical details",Ui);
        yield return("ReShade alpha4 six-language compact status and setup restriction messages",Localization);
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private static string Fixture(string name){var p=Environment.GetEnvironmentVariable(name);if(p is null||!File.Exists(p))throw new SkipCheckException("Required independently recorded research fixture: "+name);return p;}
    private static string Temp(){var p=Path.Combine(Path.GetTempPath(),"RenoDX-ReShade-alpha4",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    private static ReShadeDetectionReport Report(string version="6.8.0.2155",bool blocked=false)=>new(blocked?ReShadeDetectionState.Conflict:ReShadeDetectionState.FullAddonVerified,[new(@"C:\own\dxgi.dll",version,new string('a',64),true,true,Kind:ReShadeProxyKind.FullAddonVerified,BlocksGuidedSetup:blocked)],[],[]);
    private static void Binary()
    {
        var path=Fixture("RENODX_TEST_RESHADE_INSTALLER");new ReShadeInstallerVerifier().Verify(path);var data=JsonSerializer.SerializeToElement(ReShadeCliResearch.InspectBinary(path));var ctor=data.GetProperty("Methods").GetProperty(".ctor");var strings=ctor.GetProperty("Strings").EnumerateArray().Select(x=>x.GetString()).ToArray();var calls=ctor.GetProperty("Calls").EnumerateArray().Select(x=>x.GetString()).ToArray();
        foreach(var arg in new[]{"--api","d3d11","d3d12","--headless","--state","update"})Check(strings.Contains(arg),"Reviewed constructor lacks argument: "+arg);
        var routed=data.GetProperty("Methods").EnumerateObject().Where(x=>x.Name.StartsWith("<.ctor>",StringComparison.Ordinal)).SelectMany(x=>x.Value.GetProperty("Calls").EnumerateArray().Select(v=>v.GetString()));
        Check(calls.Contains("GetCommandLineArgs")&&calls.Contains("Exists")&&calls.Contains("RunTaskWithExceptionHandling")&&routed.Contains("InstallStep_CheckExistingInstallation"),"Strings are not confirmed constructor argument dispatch");Check(strings.Any(x=>x!.Contains("singleplayer games only")),"Original warning missing from binary");
        var marshal=data.GetProperty("NativeMarshaling").EnumerateArray().Select(x=>x.GetString()).ToArray();Check(marshal.Any(x=>x=="imageName:14"),"ANSI LPStr import not confirmed in actual binary");Console.WriteLine("EVIDENCE alpha4 reviewed binary MVID "+data.GetProperty("Mvid").GetString()+"; constructor args/calls and MapAndLoad imageName LPStr=0x14 confirmed without executing the installer.");
    }
    private static void Observations()
    {
        using var first=JsonDocument.Parse(File.ReadAllText(Fixture("RENODX_TEST_RESHADE_CLI_EVIDENCE")));using var next=JsonDocument.Parse(File.ReadAllText(Fixture("RENODX_TEST_RESHADE_CLI_ADDITIONAL")));
        foreach(var d in new[]{first,next}){Check(d.RootElement.GetProperty("InstallerSha256").GetString()==ReShadeInstallerVerifier.Reviewed.Sha256,"Observations concern another package");Check(d.RootElement.GetProperty("NoVulkanArguments").GetBoolean()&&d.RootElement.GetProperty("NoGameExecuted").GetBoolean(),"Probe scope unclear");}
        JsonElement Case(JsonDocument d,string name)=>d.RootElement.GetProperty("Runs").EnumerateArray().Single(x=>x.GetProperty("Name").GetString()==name);
        var safe=Case(first,"foreign-install");var unsafeUpdate=Case(first,"foreign-forced-update");var unicode=Case(first,"unicode-d3d12");var locked=Case(first,"locked-forced-update");
        Check(safe.GetProperty("ExitCode").GetInt32()==1&&safe.GetProperty("OriginalSha256").GetString()==safe.GetProperty("AfterSha256").GetString(),"Default conflict refusal not observed");
        Check(unsafeUpdate.GetProperty("ExitCode").GetInt32()==0&&unsafeUpdate.GetProperty("OriginalSha256").GetString()!=unsafeUpdate.GetProperty("AfterSha256").GetString(),"Forced update overwrite hazard not observed");Check(unicode.GetProperty("ExitCode").GetInt32()==0&&unicode.GetProperty("Machine").GetString()=="I386","Recorded Unicode architecture hazard missing");
        var target=unicode.GetProperty("Arguments")[0].GetString()!;using(var input=File.OpenRead(target))using(var pe=new System.Reflection.PortableExecutable.PEReader(input))Check(pe.PEHeaders.CoffHeader.Machine==System.Reflection.PortableExecutable.Machine.Amd64,"Unicode input was not actually x64");
        Check(locked.GetProperty("ExitCode").GetInt32()==1&&locked.GetProperty("OriginalSha256").GetString()==locked.GetProperty("AfterSha256").GetString(),"Locked-file failure not preserved");var spaces=Case(next,"spaces-d3d12");Check(spaces.GetProperty("ExitCode").GetInt32()==0&&spaces.GetProperty("Machine").GetString()=="Amd64","Quoted ASCII spaces/ampersand case failed");
        var reviewed=Case(next,"reviewed-update");Check(reviewed.GetProperty("IniBefore").GetString()!=reviewed.GetProperty("IniAfter").GetString()&&reviewed.GetProperty("PresetBefore").GetString()==reviewed.GetProperty("PresetAfter").GetString(),"Observed INI/preset result changed");
        Console.WriteLine("EVIDENCE Seven previously executed research-only headless cases are audited here; official Setup is NOT executed by this regression check. Unicode exit 0 still produced I386 for AMD64 input; --state update overwrote a rejected foreign proxy.");
    }
    private static void ProductionLaunch()
    {
        var start=OfficialReShadeInstallerProcess.CreateStartInfo(@"C:\own cache\ReShade Setup.exe");Check(start.ArgumentList.Count==0&&string.IsNullOrEmpty(start.Arguments)&&start.UseShellExecute&&start.WindowStyle==ProcessWindowStyle.Normal,"Production enabled unverified args/headless mode");Check(start.FileName==@"C:\own cache\ReShade Setup.exe","Installer path altered for quoting workaround");
    }
    private static void Paths()
    {
        foreach(var cp in new[]{1252,1251,65001})Check(ReShadeSetupPathPolicy.CanInspect(@"C:\Own folder & spaces\Own.exe",cp),"ASCII quoting confused with path encoding");
        Check(!ReShadeSetupPathPolicy.CanInspect(@"C:\Ігрова тека Ω é\Own.exe",1252)&&!ReShadeSetupPathPolicy.CanInspect(@"C:\Ігрова тека Ω é\Own.exe",1251),"Lossy ANSI conversion accepted");Check(ReShadeSetupPathPolicy.CanInspect(@"C:\Ігрова тека Ω é\Own.exe",65001),"UTF8-capable system incorrectly treated as legacy ANSI");
    }
    private sealed class Never:IReShadeInstallerProcess{public int Count;public Task<int?> RunAsync(string p){Count++;throw new Exception("Unsafe path launched");}}
    private static void ServicePathBlock()
    {
        if(ReShadeSetupPathPolicy.SystemCodePage==65001)throw new SkipCheckException("This host uses UTF-8 ACP; legacy lossy-path integration case is inapplicable, explicit codepage unit cases still run.");
        var root=Path.Combine(Temp(),"Own Україна Ω é");Directory.CreateDirectory(root);var exe=Path.Combine(root,"Own.exe");File.Copy(Environment.ProcessPath!,exe);Check(!ReShadeSetupPathPolicy.CanInspect(exe),"Fixture does not reproduce lossy path on this host");var process=new Never();var runner=new ReShadeGuidedInstaller(new ReShadePackageService(Temp()),new(),process,_=>false);
        try{runner.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,new("untrusted","6.8.0","none",0),true,true).GetAwaiter().GetResult();throw new Exception("Unsafe path was accepted");}catch(InvalidDataException ex){Check(ex.Message==ReShadeSetupPathPolicy.BlockMessage,"Wrong rejection reason");}Check(process.Count==0&&Directory.GetFiles(root).Length==1,"Path block modified target or started Setup");
    }
    private static void Summaries()
    {
        var l=new UiLocalization();Check(ReShadeUpdatePolicy.CompactSummary(Report(),l).Split('\n').Length==2,"Summary is no longer compact");Check(ReShadeUpdatePolicy.CompactSummary(Report(blocked:true) with { CriticalInspectionFailure = true },l).Contains("Blocked")&&ReShadeUpdatePolicy.CompactSummary(Report(),l,false).Contains("Blocked"),"Current version hides a hard restriction");
        Check(ReShadeUpdatePolicy.LaunchLabel(Report()).Contains("repair")&&ReShadeUpdatePolicy.LaunchLabel(Report("6.7.0")).Contains("update review")&&ReShadeUpdatePolicy.LaunchLabel(Report("6.9.0")).Contains("for review")&&ReShadeUpdatePolicy.LaunchLabel(Report(blocked:true)) == "Open official setup for review","Repair/update/unknown/conflict semantics conflated");
    }
    private static void Ui()=>WpfCheckHost.Run(()=>
    {
        LocalizedPresentation.Language.SetCulture("en-US",false);var main=new MainWindow();try{var view=(ReShadeManagerView)main.FindName("ReShadeView");Check(!view.TechnicalExpander.IsExpanded&&view.TechnicalExpander.Content is not null&&view.CompactSummaryText is not null&&view.UpdateActionText is not null,"Technical content removed or expanded by default");view.TechnicalExpander.IsExpanded=true;typeof(MainWindow).GetMethod("ResetReShadeScan",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,null);Check(!view.TechnicalExpander.IsExpanded,"Context reset leaves stale technical pane open");}finally{main.Close();}
    });
    private static void Localization()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Catalog integrity failed");var text=new[]{"1. Installer package","2. Selected game","3. Installation status","4. Official setup and recovery","Verified Full Add-on files: {0} · Setup: {1}","Manual review required","Setup blocked — review conflicts","Open official repair / reinstall","Open official update review","Open official installation review","Open official setup for review","Target path needs manual review; see setup restrictions.","Command-line preselection is not enabled for reviewed ReShade 6.8.0. Standard guided setup preserves its normal decisions.",ReShadeSetupPathPolicy.BlockMessage};
        foreach(var key in text)foreach(var culture in UiLocalization.Cultures){Check(l.GetCatalog(culture).ContainsKey(key),"Missing localized key: "+key);if(culture!="en-US")Check(l.Lookup(key,culture)!=key,"Untranslated alpha4 key: "+culture);}
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);Check(ReShadeUpdatePolicy.CompactSummary(Report(),l).Contains("6.8.0.2155"),"Summary changed technical version");}
    }
}

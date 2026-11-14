using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class ReShadeChecks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("ReShade alpha reviewed genuine Full Add-on installer passes publisher and content verification",GenuinePackage);
        yield return("ReShade alpha missing malformed changed and wrong-publisher packages fail closed",InvalidPackages);
        yield return("ReShade alpha download accepts only reviewed source and authentic package",Download);
        yield return("ReShade alpha redirects unavailable and truncated downloads leave no runnable cache",BadDownloads);
        yield return("ReShade alpha cancellation cleans partial download and preserves game files",CancelDownload);
        yield return("ReShade alpha preparation reuses verified cache and refuses tampered cache",Cache);
        yield return("ReShade alpha cache cannot overlap game root",CacheScope);
        yield return("ReShade alpha missing installation and separate EXE directories remain read-only",MissingAndSeparate);
        yield return("ReShade alpha actual official installed full binary matches reviewed hash",GenuineRuntime);
        yield return("ReShade alpha candidate metadata cannot prove full support and proxy conflicts block",UnknownAndConflicts);
        yield return("ReShade alpha standard classification fixture is distinct from full and unknown",StandardFixture);
        yield return("ReShade alpha custom paths and oversized config block without changing settings",CustomConfig);
        yield return("ReShade alpha junction paths block detection package preparation and guided targets",Junction);
        yield return("ReShade alpha Vulkan inventory does not claim registration activation or compatibility",Vulkan);
        yield return("ReShade alpha guided runner requires consent API valid x64 target and stopped game",RunnerGuards);
        yield return("ReShade alpha guided runner rechecks package and proxy conflicts before launch",RunnerRechecks);
        yield return("ReShade alpha exit cancellation and incomplete installation never establish success",RunnerIncomplete);
        yield return("ReShade alpha guided result verifies file evidence without claiming runtime or managed Restore",RunnerComplete);
        yield return("ReShade alpha UI navigation consent busy states and six language resources",Ui);
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static string Temp(){var p=Path.Combine(Path.GetTempPath(),"RenoDX-ReShade-Checks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    private static string Fixture(string name){var p=Environment.GetEnvironmentVariable(name);if(p is null||!File.Exists(p))throw new SkipCheckException("Missing independently acquired genuine fixture: "+name);return p;}
    private static string Installer=>Fixture("RENODX_TEST_RESHADE_INSTALLER");
    private static string Runtime=>Fixture("RENODX_TEST_RESHADE_FULL_DLL");
    private static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException){return;}throw new Exception("Unsafe input was accepted");}
    private static void GenuinePackage(){var v=new ReShadeInstallerVerifier().Verify(Installer);Check(v.Version=="6.8.0"&&v.Size==4318424&&v.Sha256==Hash(Installer),"Reviewed package identity mismatch");ReShadeInstallerVerifier.VerifyPinnedSignature(File.ReadAllBytes(Installer),ReShadeInstallerVerifier.Reviewed.PublisherThumbprint);}
    private static void InvalidPackages()
    {
        var root=Temp();var p=Path.Combine(root,"ReShade_Setup_6.8.0_Addon.exe");Reject(()=>new ReShadeInstallerVerifier().Verify(p));File.WriteAllText(p,"Standard/future package cannot substitute");Reject(()=>new ReShadeInstallerVerifier().Verify(p));
        var bytes=File.ReadAllBytes(Installer);bytes[1024]^=1;File.WriteAllBytes(p,bytes);Reject(()=>new ReShadeInstallerVerifier().Verify(p));Reject(()=>ReShadeInstallerVerifier.VerifyPinnedSignature(bytes,ReShadeInstallerVerifier.Reviewed.PublisherThumbprint));
        Reject(()=>ReShadeInstallerVerifier.VerifyPinnedSignature(File.ReadAllBytes(Installer),new string('0',40)));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> create):HttpMessageHandler
    {public int Count;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Count++;token.ThrowIfCancellationRequested();var response=create(request);response.RequestMessage=request;return Task.FromResult(response);}}
    private sealed class Progress(Action<long> report):IProgress<long>{public void Report(long value)=>report(value);}
    private static ReShadePackageService Service(string store,Handler handler)=>new(store,new HttpClient(handler));
    private static PreparedReShadeInstaller Prepare(ReShadePackageService s,string game,string? local=null)=>s.PrepareAsync(game,localInstaller:local).GetAwaiter().GetResult();
    private static void Download()
    {
        foreach(var bad in new[]{"http://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe","https://example.org/ReShade_Setup_6.8.0_Addon.exe",ReShadeInstallerVerifier.Reviewed.Url+"?x=1","https://reshade.me/downloads/ReShade_Setup_6.8.0.exe"})Check(!ReShadePackageService.IsReviewedSource(bad),"Unreviewed source accepted");
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(Installer))});var service=Service(Temp(),handler);var game=Temp();File.WriteAllText(Path.Combine(game,"untouched.ini"),"original");long progress=0;var result=service.PrepareAsync(game,new Progress(n=>progress=n)).GetAwaiter().GetResult();Check(result.Sha256==Hash(Installer)&&handler.Count==1&&progress==result.Size,"Download not verified/progress inaccurate");Check(File.ReadAllText(Path.Combine(game,"untouched.ini"))=="original"&&Directory.GetFiles(game).Length==1,"Preparation wrote game files");
    }
    private static void BadDownloads()
    {
        foreach(var response in new Func<HttpResponseMessage>[] {()=>new(HttpStatusCode.Redirect),()=>new(HttpStatusCode.NotFound),()=>new(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])},()=>new(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[4318424])}})
        {var service=Service(Temp(),new Handler(_=>response()));Reject(()=>Prepare(service,Temp()));Check(!File.Exists(service.InstallerPath)&&!Directory.GetFiles(service.Store,"*.partial",SearchOption.AllDirectories).Any(),"Failed package left runnable/partial file");}
    }
    private static void CancelDownload(){using var token=new CancellationTokenSource();var service=Service(Temp(),new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(Installer))}));Reject(()=>service.PrepareAsync(Temp(),new Progress(_=>token.Cancel()),token.Token).GetAwaiter().GetResult());Check(!File.Exists(service.InstallerPath)&&Directory.GetFiles(service.Store,"*.partial",SearchOption.AllDirectories).Length==0,"Cancelled package remained");}
    private static void Cache()
    {
        var handler=new Handler(_=>throw new Exception("Unexpected network request"));var service=Service(Temp(),handler);var game=Temp();var first=Prepare(service,game,Installer);Check(Prepare(service,game)==first&&handler.Count==0,"Cache not reused");
        var p=service.InstallerPath;var bytes=File.ReadAllBytes(p);bytes[1024]^=1;File.WriteAllBytes(p,bytes);var changed=Hash(p);Reject(()=>Prepare(service,game,Installer));Reject(()=>service.Reverify(first));Check(Hash(p)==changed&&handler.Count==0,"Tampered cache silently overwritten/redownloaded");
    }
    private static void CacheScope(){var p=Temp();var service=Service(Path.Combine(p,"cache"),new Handler(_=>throw new Exception("No network allowed")));Reject(()=>Prepare(service,p));Reject(()=>Prepare(service,Path.Combine(service.Store,"game")));}
    private static (string Root,string Exe) Game(){var root=Temp();var p=Path.Combine(root,"OwnGame.exe");File.Copy(Environment.ProcessPath!,p);return(root,p);}
    private static void MissingAndSeparate()
    {
        var (root,exe)=Game();var dll=Path.Combine(root,"dxgi.dll");var detector=new ReShadeDetectionService();Check(detector.Scan(root,exe,ReShadeGraphicsApi.Direct3D11).State==ReShadeDetectionState.NotFound,"Missing state wrong");
        var second=Path.Combine(root,"Other");Directory.CreateDirectory(second);var other=Path.Combine(second,"Other.exe");File.Copy(exe,other);File.Copy(Runtime,dll);Check(detector.Scan(root,other,ReShadeGraphicsApi.Direct3D12).State==ReShadeDetectionState.NotFound,"Other executable borrowed sibling proxy");Check(File.Exists(dll),"Read-only scan changed proxy");Reject(()=>detector.Scan(root,Path.Combine(Temp(),"outside.exe"),ReShadeGraphicsApi.Direct3D12));
    }
    private static void GenuineRuntime()
    {
        var (root,exe)=Game();var proxy=Path.Combine(root,"dxgi.dll");File.Copy(Runtime,proxy);var bytes=Hash(proxy);foreach(var api in new[]{ReShadeGraphicsApi.Direct3D11,ReShadeGraphicsApi.Direct3D12}){var report=new ReShadeDetectionService().Scan(root,exe,api);Check(report.State==ReShadeDetectionState.FullAddonVerified&&report.Files.Single().IsNativeX64Dll&&report.Files.Single().Version.StartsWith("6.8.0",StringComparison.Ordinal),"Actual native Full Add-on binary not recognized: "+System.Text.Json.JsonSerializer.Serialize(report));}Check(Hash(proxy)==bytes,"Scan modified official binary");
    }
    private static void UnknownAndConflicts()
    {
        var (root,exe)=Game();var p=Path.Combine(root,"dxgi.dll");File.WriteAllText(p,"own fixture");var detector=new ReShadeDetectionService(f=>new(f,"6.8.0.0",new string('a',64),true,true),new Dictionary<string,ReShadeDetectionState>(),Path.Combine(Temp(),"layer.dll"));Check(detector.Scan(root,exe,ReShadeGraphicsApi.Direct3D12).State==ReShadeDetectionState.DetectedUnknownSupport,"Version/name inferred full support");File.WriteAllText(Path.Combine(root,"d3d11.dll"),"another proxy");Check(detector.Scan(root,exe,ReShadeGraphicsApi.Direct3D12).HasConflicts,"Multiple proxies accepted");Check(new ReShadeDetectionService().Scan(root,exe,ReShadeGraphicsApi.Direct3D12).HasConflicts,"Unknown non-PE proxy accepted");
    }
    private static void StandardFixture(){var(root,exe)=Game();File.WriteAllText(Path.Combine(root,"dxgi.dll"),"synthetic classification only");var detector=new ReShadeDetectionService(p=>new(p,"6.8.0.0",new string('a',64),true,true,true),new Dictionary<string,ReShadeDetectionState>(),Path.Combine(Temp(),"layer.dll"));Check(detector.Scan(root,exe,ReShadeGraphicsApi.Direct3D11).State==ReShadeDetectionState.StandardVerified,"Standard classification lost");Console.WriteLine("EVIDENCE Standard positive classification uses injected metadata; genuine standard signed DLL not tested.");}
    private static void CustomConfig()
    {
        var(root,exe)=Game();var config=Path.Combine(root,"ReShade.ini");var contents="; keep comment\r\n[GENERAL]\r\nOther=7\r\n[aDdOn]\r\nAddonPath=..\\OwnCustom\r\n[INSTALL]\r\nBasePath=D:\\OwnAlternate\r\n";File.WriteAllText(config,contents);var detector=new ReShadeDetectionService();Check(detector.Scan(root,exe,ReShadeGraphicsApi.Direct3D12).AlternativeAddonPaths.Count==2&&File.ReadAllText(config)==contents,"Custom config modified/ignored");File.WriteAllText(config,new string('a',262145));Check(detector.Scan(root,exe,ReShadeGraphicsApi.Direct3D12).HasConflicts,"Oversized configuration failed open");
    }
    private static void Vulkan(){var(root,exe)=Game();var layer=Path.Combine(Temp(),"ReShade64.dll");File.Copy(Runtime,layer);var d=new ReShadeDetectionService(ReShadeDetectionService.ReadEvidence,ReShadeDetectionService.ReviewedRuntimeHashes,layer);var r=d.Scan(root,exe,ReShadeGraphicsApi.Vulkan);Check(r.State==ReShadeDetectionState.NotVerified&&!r.VulkanRegistrationVerified&&r.Files.Count==1,"Shared DLL claimed game-specific Vulkan registration");}
    private static void Junction()
    {
        var(root,exe)=Game();var outside=Temp();File.Copy(exe,Path.Combine(outside,"Own.exe"));var link=Path.Combine(root,"link");
        var start=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("/c");start.ArgumentList.Add("mklink");start.ArgumentList.Add("/J");start.ArgumentList.Add(link);start.ArgumentList.Add(outside);
        using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();if(process.ExitCode!=0)throw new SkipCheckException("Windows test junction could not be created: "+process.StandardError.ReadToEnd());
        try{Reject(()=>new ReShadeDetectionService().Scan(root,Path.Combine(link,"Own.exe"),ReShadeGraphicsApi.Direct3D12));var service=Service(Path.Combine(link,"cache"),new Handler(_=>throw new Exception("Junction must block before download")));Reject(()=>Prepare(service,Temp()));Reject(()=>new ReShadeInstallerVerifier().Verify(Path.Combine(link,"ReShade_Setup_6.8.0_Addon.exe")));Check(Directory.GetFiles(outside).Length==1,"Junction traversal wrote files outside allowed scope");}finally{Directory.Delete(link);}
    }
    private sealed class Process(Func<string,int?> run):IReShadeInstallerProcess{public int Count;public Task<int?> RunAsync(string installer){Count++;return Task.FromResult(run(installer));}}
    private static (ReShadePackageService Service,PreparedReShadeInstaller Package) Package(string root){var service=Service(Temp(),new Handler(_=>throw new Exception("No network required")));return(service,Prepare(service,root,Installer));}
    private static void RunnerGuards()
    {
        var(root,exe)=Game();var(s,p)=Package(root);var process=new Process(_=>0);var d=new ReShadeDetectionService();var r=new ReShadeGuidedInstaller(s,d,process,_=>false);
        foreach(var api in new[]{ReShadeGraphicsApi.Unknown,(ReShadeGraphicsApi)99,ReShadeGraphicsApi.Vulkan})Reject(()=>r.RunAsync(root,exe,api,p,true,false).GetAwaiter().GetResult());Reject(()=>r.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,false,false).GetAwaiter().GetResult());
        Reject(()=>new ReShadeGuidedInstaller(s,d,process,_=>true).RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,true,false).GetAwaiter().GetResult());using var token=new CancellationTokenSource();token.Cancel();Reject(()=>r.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,true,false,token.Token).GetAwaiter().GetResult());Check(process.Count==0,"Guard started official process");
    }
    private static void RunnerRechecks()
    {
        var(root,exe)=Game();var(s,p)=Package(root);var process=new Process(_=>0);var runner=new ReShadeGuidedInstaller(s,new(),process,_=>false);File.WriteAllText(Path.Combine(root,"dxgi.dll"),"third-party proxy");Reject(()=>runner.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,true,false).GetAwaiter().GetResult());File.Delete(Path.Combine(root,"dxgi.dll"));File.WriteAllText(s.InstallerPath,"changed");Reject(()=>runner.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,true,false).GetAwaiter().GetResult());Check(process.Count==0,"Stale package or conflict reached process");
    }
    private static void RunnerIncomplete()
    {
        foreach(int? code in new int?[]{0,1,null}){var(root,exe)=Game();var(s,p)=Package(root);var fake=new Process(_=>code);var result=new ReShadeGuidedInstaller(s,new(),fake,_=>false).RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,true,false).GetAwaiter().GetResult();Check(result.After.State==ReShadeDetectionState.NotFound&&result.ExitCode==code&&Directory.GetFiles(root).Length==1,"Exit code fabricated successful installation or managed changes");}
        var(g,e)=Game();var(service,package)=Package(g);var incomplete=new Process(_=>{File.WriteAllText(Path.Combine(g,"dxgi.dll"),"incomplete own proxy");return 1;});var failed=new ReShadeGuidedInstaller(service,new(),incomplete,_=>false).RunAsync(g,e,ReShadeGraphicsApi.Direct3D12,package,true,false).GetAwaiter().GetResult();Check(failed.After.HasConflicts&&File.ReadAllText(Path.Combine(g,"dxgi.dll"))=="incomplete own proxy","Partial external changes hidden or automatically deleted");
    }
    private static void RunnerComplete()
    {
        var(root,exe)=Game();var(s,p)=Package(root);var ini=Path.Combine(root,"ReShade.ini");File.WriteAllText(ini,"; preserve\n[GENERAL]\nPresetPath=OwnPreset.ini\n");var before=Hash(ini);var fake=new Process(_=>{File.Copy(Runtime,Path.Combine(root,"dxgi.dll"));return 0;});var runner=new ReShadeGuidedInstaller(s,new(),fake,_=>false);var result=runner.RunAsync(root,exe,ReShadeGraphicsApi.Direct3D12,p,true,false).GetAwaiter().GetResult();Check(result.Before.State==ReShadeDetectionState.NotFound&&result.After.State==ReShadeDetectionState.FullAddonVerified&&Hash(ini)==before,"Guided result/config preservation wrong");Check(!Directory.GetDirectories(root).Any(),"Fake managed backup/Restore created for external installer");Console.WriteLine("EVIDENCE Runner orchestration uses a fake external process; actual official setup was separately performed once in the isolated DX12 directory.");
    }
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static void Ui()=>WpfCheckHost.Run(()=>
    {
        var main=new MainWindow();main.Loaded-=typeof(MainWindow).GetMethod("Window_Loaded",Flags)!.CreateDelegate<RoutedEventHandler>(main);var trace=new StringWriter();var listener=new TextWriterTraceListener(trace);PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            main.Show();((Button)main.FindName("ReShadeNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var view=(ReShadeManagerView)main.FindName("ReShadeView");
            foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);main.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Check(view.Visibility==Visibility.Visible&&!view.RunButton.IsEnabled&&view.Consent.IsChecked!=true,"Missing game/consent allowed installer");Check((string)view.RunButton.Content==LocalizedPresentation.Language.Lookup("Run official guided installer"),"Run button not localized: "+culture);Check(view.DetectionText.Text==LocalizedPresentation.Language.Lookup(ReShadePresentation.ScanText(ReShadeScanPhase.NotScanned,null)),"Evidence status not localized");}
            view.ApiPicker.SelectedIndex=2;main.UpdateLayout();Check(view.VulkanConsent.Visibility==Visibility.Visible&&view.VulkanConsent.IsChecked!=true,"Vulkan shared-layer consent omitted");view.ApiPicker.SelectedIndex=1;Check(view.VulkanConsent.Visibility==Visibility.Collapsed,"Vulkan consent shown for Direct3D");
            typeof(MainWindow).GetField("reshadeBusy",Flags)!.SetValue(main,true);typeof(MainWindow).GetMethod("RenderReShadeButtons",Flags)!.Invoke(main,null);Check(!view.RunButton.IsEnabled&&!view.PrepareButton.IsEnabled&&!view.ApiPicker.IsEnabled&&!view.FinishButton.IsEnabled,"Busy states allow second operation");typeof(MainWindow).GetField("reshadeBusy",Flags)!.SetValue(main,false);listener.Flush();Check(!trace.ToString().Contains("Error:"),"ReShade WPF binding error: "+trace);
            var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"New catalogs invalid");foreach(var culture in UiLocalization.Cultures.Skip(1))Check(l.Lookup("Full Add-on Support: Not verified",culture)!="Full Add-on Support: Not verified","Untranslated critical full-support status");
        }
        finally{PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);typeof(MainWindow).GetField("reshadeBusy",Flags)!.SetValue(main,false);main.Close();LocalizedPresentation.Language.SetCulture("en-US",false);}
    });
}

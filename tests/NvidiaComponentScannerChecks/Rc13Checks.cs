using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Rc13Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("rc13 numeric version formatter preserves component count and vendor strings in six cultures",Versions);
        yield return("rc13 genuine NVIDIA resource commas and Intel resource dots display identically across cultures",GenuineResources);
        yield return("rc13 loaded NVIDIA and Intel Preview versions format without changing plans actions or hashes",Preview);
        yield return("rc13 scanner SDK inspector and Details use structured version display only",Inspectors);
        yield return("rc13 diagnostic structured versions format while raw logs paths dates and text stay exact",Diagnostics);
        yield return("rc13 version presentation leaves transaction manifests and target policy unchanged",Transactions);
    }
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static string Temp(){var p=Path.Combine(AppContext.BaseDirectory,"Rc13Checks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    private static readonly (string Input,string Expected)[] Numeric=[("2,14,16,0","2.14.16.0"),("310,9,16,0","310.9.16.0"),("1,3,1,78","1.3.1.78"),("1,3,2,10","1.3.2.10"),("1,7,0","1.7.0"),("1,6","1.6"),("2, 14, 16, 0","2.14.16.0"),("01,06","01.06"),("1.7.0","1.7.0"),("1.6","1.6")];
    private static void EachCulture(Action action){var before=CultureInfo.CurrentCulture;var ui=CultureInfo.CurrentUICulture;try{foreach(var name in UiLocalization.Cultures){CultureInfo.CurrentCulture=new(name);CultureInfo.CurrentUICulture=new(name);action();}}finally{CultureInfo.CurrentCulture=before;CultureInfo.CurrentUICulture=ui;}}
    private static void Versions()=>EachCulture(()=>{
        foreach(var (input,expected) in Numeric)Check(TechnicalVersionDisplay.Format(input)==expected,"Version mismatch: "+input);
        foreach(var value in new[]{"v1,7,0-rc.2","1.7.0-rc.2+NVIDIA","310,9,16,0 (vendor build)","release-2026,10,31","1,2.3,4","Unknown version","—","Not applicable","", " ","1,2,3,4,5"})Check(TechnicalVersionDisplay.Format(value)==value,"Vendor/missing identifier changed: "+value);
        Check(TechnicalVersionDisplay.Format(null)==""&&SetupWizardView.DisplayVersion("","own.dll")=="Unknown version"&&SetupWizardView.DisplayVersion("","LICENSE.txt")=="Not applicable","Missing version semantics changed");
    });
    private static void GenuineResources()
    {
        var provider=Environment.GetEnvironmentVariable("RENODX_TEST_SIGNED_PROVIDER");var intel=Environment.GetEnvironmentVariable("RENODX_TEST_INTEL_CACHE");
        if(provider is null||intel is null||!File.Exists(provider)||!Directory.Exists(intel))throw new SkipCheckException("Provide the existing isolated genuine NVIDIA and Intel fixtures.");
        var info=FileVersionInfo.GetVersionInfo(provider);Check(info.FileVersion=="310,9,16,0","Genuine regression fixture no longer reproduces vendor comma metadata; inspect fixture");
        var paths=new[]{provider,Path.Combine(intel,"libxess_fg.dll"),Path.Combine(intel,"libxell.dll")};var hashes=paths.Select(Hash).ToArray();
        EachCulture(()=>{foreach(var path in paths){var version=FileVersionInfo.GetVersionInfo(path);var fixedValue=FormattableString.Invariant($"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}");Check(TechnicalVersionDisplay.Format(version.FileVersion)==fixedValue,"Vendor resource display depends on culture");}});
        Check(paths.Select(Hash).SequenceEqual(hashes),"Reading version display changed a fixture DLL");
        Console.WriteLine("EVIDENCE NVIDIA resource FileVersion=310,9,16,0; fixed fields/display=310.9.16.0. Intel fixtures retain 1.3.1.78 and 1.3.2.10; no resource rewrite.");
    }
    private static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    private static IEnumerable<DependencyObject> Tree(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var n in Tree(c))yield return n;}}
    private static void Drain(Window w){w.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
    private static IEnumerable<string> Text(DependencyObject root)=>Tree(root).OfType<TextBlock>().Select(t=>t.Text).Concat(Tree(root).OfType<TextBox>().Select(t=>t.Text));
    private static void Event(MainWindow main,string method)=>typeof(MainWindow).GetMethod(method,Flags)!.Invoke(main,[main,new RoutedEventArgs()]);
    private static void Wpf(Action<MainWindow> action)=>WpfCheckHost.Run(()=>{
        LocalizedPresentation.Language.SetCulture("en-US",false);var main=new MainWindow();main.Loaded-=typeof(MainWindow).GetMethod("Window_Loaded",Flags)!.CreateDelegate<RoutedEventHandler>(main);var trace=new StringWriter();var listener=new TextWriterTraceListener(trace);PresentationTraceSources.DataBindingSource.Listeners.Add(listener);try{main.Show();action(main);listener.Flush();Check(!trace.ToString().Contains("Error:"),"Version-display binding errors: "+trace);}finally{PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);main.Close();}
    });
    private static void Preview()=>Wpf(main=>{
        var same=new string('a',64);var different=new string('b',64);var rows=new[]{
            new StreamlineComponentPlanEntry("sl.common.dll",@"C:\own\2,14,16,0\sl.common.dll",@"C:\own\game\sl.common.dll","2,14,16,0",same,"2.14.16.0",same,"Replace existing",false,false,"Keep arbitrary 1,234 text"),
            new StreamlineComponentPlanEntry("nvngx_dlssg.dll","source","target","310,9,16,0",same,"310,9,1,0",different,"Replace existing",true,true,"Synthetic UI fixture"),
            new StreamlineComponentPlanEntry("libxess_fg.dll","source-fg","target-fg","1,3,1,78",same,"1.3.0.1",different,"Replace existing",true,true,"Synthetic Intel UI fixture"),
            new StreamlineComponentPlanEntry("libxell.dll","source-xell","target-xell","1,3,2,10",same,"1.3.1.1",different,"Replace existing",true,true,"Synthetic Intel UI fixture")};
        var original=JsonSerializer.Serialize(rows);var view=new SetupWizardView();var review=view.Review(rows,"Own fixture","NVIDIA / Intel","2,14,16","310,9,16,0",_=>"Own source",_=>"Not checked yet");view.ContentPanel.Children.Add(review);var dialog=main.CreateInfoDialog("Preview",view,1100,800);
        try{dialog.Show();foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(dialog);foreach(var expected in new[]{"2.14.16.0","310.9.16.0","1.3.1.78","1.3.2.10"})Check(Text(review).Contains(expected),"Preview missing dot version: "+culture+" / "+expected);Check(Text(view).Any(t=>t.Contains("2.14.16 · 310.9.16.0")),"SDK/provider display not centralized");Check(JsonSerializer.Serialize(rows)==original&&MainWindow.PlanAction(rows[0])=="Unchanged","Display mutated plan/hash/no-op action");}}finally{dialog.Close();}
    });
    private static void Inspectors()=>Wpf(main=>{
        Event(main,"NvidiaComponents_Click");var entry=new NvidiaComponentEntry("sl.common.dll","Found","2,14,16,0",@"C:\own\2,14,16,0\sl.common.dll","Game root",false,42,new string('a',64));var items=(ItemsControl)main.FindName("NvidiaComponentsItemsControl");items.ItemsSource=new[]{entry};
        var row=new StreamlineComponentPlanEntry("nvngx_dlssg.dll","C:\\own\\310,9,16,0\\source.dll","target","310,9,16,0",new string('a',64),"310,9,1,0",new string('b',64),"Replace existing",true,true,"Keep 1,234 arbitrary text");
        var details=(string)typeof(MainWindow).GetMethod("PlanDetails",Flags)!.Invoke(main,[row,false,null,null])!;Check(details.Contains("Current version: 310.9.1.0")&&details.Contains("Proposed version: 310.9.16.0")&&details.Contains(row.SourcePath)&&details.Contains(row.Message),"Details formatted outside typed fields or missed version");
        var scanDetails=(string)typeof(MainWindow).GetMethod("ScanDetails",Flags)!.Invoke(null,[entry])!;Check(scanDetails.Contains("Installed version: 2.14.16.0")&&scanDetails.Contains(entry.Path),"Scan Details changed path or missed version");
        foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(main);Check(Text(items).Contains("2.14.16.0")&&entry.Version=="2,14,16,0","Inspector binding changed model or missed formatting");}
        typeof(MainWindow).GetMethod("SetComponentsProvider",Flags)!.Invoke(main,[true]);var intel=(IntelSdkManagerView)main.FindName("IntelSdkView");intel.LocalFiles.Children.Clear();
        var a=new IntelSdkAsset("Own SDK.zip","https://github.com/intel/xess/releases/download/own/Own.zip",1,new string('a',64),"3,0,2","Own SDK",null,"https://github.com/intel/xess/releases","",false,false);
        var snapshot=new IntelSdkStage(a,Temp(),[new("bin/libxess_fg.dll","own-fg","1,3,1,78",new string('a',64),1,"Native x64 DLL"),new("bin/libxell.dll","own-xell","1,3,2,10",new string('b',64),1,"Native x64 DLL")],0,false);
        typeof(MainWindow).GetMethod("RenderSelectedIntelStage",Flags)!.Invoke(main,[snapshot]);
        foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(main);Check(Text(intel.LocalFiles).Any(t=>t.Contains("1.3.1.78"))&&Text(intel.LocalFiles).Any(t=>t.Contains("1.3.2.10")),"Intel prepared display missed dots");Check(snapshot.Files[0].Version=="1,3,1,78"&&!snapshot.CatalogCompatible,"Display changed SDK identity/compatibility");}
    });
    private static void Diagnostics()=>Wpf(main=>{
        string? detail=null;var raw="1,234; decimal 1,6; version 310,9,16,0; 2026-10-31; C:\\own\\1,3,1,78";
        var report=new RuntimeDiagnosticReport([], ["Disk · own.dll · 310,9,16,0\nC:\\own\\2,14,16,0\\own.dll\nSHA-256: aabb", "Current process session matched · 2026-10-31\nEXE version 1,7,0 · addon 1,6", "Mapped module reported · libxess_fg.dll · 1,3,1,78"],[],[new("Own.log",raw,DateTimeOffset.UtcNow)]);var saved=JsonSerializer.Serialize(report);
        var panel=new RuntimeDiagnosticsPanel((_,value)=>detail=value);panel.Render(report);var dialog=main.CreateInfoDialog("Runtime Diagnostics",panel);
        void Tab(string name){Tree(panel).OfType<Button>().Single(b=>LocalizedPresentation.SourceText(b,ContentControl.ContentProperty)==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(dialog);}
        try{dialog.Show();foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Tab("Installed Components");Check(Text(panel).Any(t=>t.Contains("310.9.16.0")),"Diagnostic inventory unformatted");Tree(panel).OfType<Button>().Single(b=>LocalizedPresentation.SourceText(b,ContentControl.ContentProperty)=="Details").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(detail!.Contains("310.9.16.0")&&detail.Contains("C:\\own\\2,14,16,0\\own.dll"),"Diagnostic Details touched path");Tab("Session / Loaded Modules");Check(Text(panel).Any(t=>t.Contains("1.3.1.78")),"Mapped structured version unformatted");Tab("Logs");Check(Tree(panel).OfType<TextBox>().Single(t=>t.IsReadOnly).Text==raw,"Raw log rewritten");Check(LocalizedPresentation.Language.Translate(raw)==raw&&LocalizedPresentation.Language.Translate("1,6")=="1,6","Arbitrary decimal/text rewritten");Check(JsonSerializer.Serialize(report)==saved,"Diagnostic source snapshot changed");}}finally{dialog.Close();}
        Check(RuntimeDiagnosticVersionDisplay.Evidence("EXE version 1,7,0 · addon 1,6")=="EXE version 1.7.0 · addon 1.6","Structured EXE/addon evidence missed");
    });
    private static void Transactions()=>Wpf(main=>{
        var root=Temp();var exe=Path.Combine(root,"own.exe");File.WriteAllText(exe,"Own EXE; never launched");File.WriteAllText(Path.Combine(root,"a.dll"),"original");var service=new BackupRestoreService(Temp());var tx=service.ExecuteAsync(root,[new("a.dll",[1,2,3])]).GetAwaiter().GetResult();var saved=Directory.GetFiles(tx.TransactionDirectory,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Hash);var preview=service.PreviewRestore(root,tx.TransactionId);var content=main.RestorePreviewContent(preview,root,main);var dialog=main.CreateInfoDialog("Restore",content);
        try{dialog.Show();Drain(dialog);foreach(var (input,_) in Numeric)_=TechnicalVersionDisplay.Format(input);Check(saved.All(p=>Hash(p.Key)==p.Value),"Version display rewrote manifest/backups");RuntimeTargetPolicy.Validate(root,exe,root,false);var other=Path.Combine(root,"SP");Directory.CreateDirectory(other);try{RuntimeTargetPolicy.Validate(root,exe,other,false);throw new Exception("Target scope broadened");}catch(InvalidDataException){}service.RestoreAsync(root,tx.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllText(Path.Combine(root,"a.dll"))=="original","Restore original changed");}finally{dialog.Close();}
    });
}

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Stage3Checks
{
    public static IEnumerable<(string Name,Action Run)> All(){
        yield return("stage3 selected Intel SDK recovers a local archive across sessions without network",Recovery);
        yield return("stage3 Intel recovery rejects corrupted archives and does not borrow compatible cache",RecoveryRefusals);
        yield return("stage3 existing Intel ZIP import validates source digest and preserves prior files",Import);
        yield return("stage3 Intel import cancellation and missing digest cannot stage",ImportRefusals);
        yield return("stage3 loaded Wizard API requirements navigation and expert tools remain gated",Wizard);
        yield return("stage3 dynamic Components status fragments translate in six languages",Translations);
    }
    private static void Check(bool value,string text){if(!value)throw new Exception(text);}
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or OperationCanceledException){return;}throw new Exception("Expected refusal");}
    private static string Temp(){var path=Path.Combine(AppContext.BaseDirectory,"Stage3Checks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private sealed class Offline:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("Unexpected network access");}
    private static IntelSdkPackageService Service(string root)=>new(root,new HttpClient(new Offline()));
    private static byte[] Zip(){using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))foreach(var name in new[]{"bin/libxess_fg.dll","LICENSE.txt"}){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("Own synthetic fixture; not a vendor binary.");}return bytes.ToArray();}
    private static IntelSdkAsset Asset(byte[] bytes)=>new("XeSS_SDK_99.0.0.zip","https://github.com/intel/xess/releases/download/v99.0.0/XeSS_SDK_99.0.0.zip",bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),"v99.0.0","Own test SDK",null,"https://github.com/intel/xess/releases/tag/v99.0.0","Own fixture",false,false);
    private static string PutArchive(IntelSdkPackageService service,IntelSdkAsset asset,byte[] bytes){var path=Path.Combine(service.Downloads,asset.Tag,asset.Name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);return path;}
    private static void Recovery(){
        var bytes=Zip();var asset=Asset(bytes);var root=Temp();var service=Service(root);var path=PutArchive(service,asset,bytes);
        Check(!Directory.Exists(service.Staging),"Fixture already staged");var stage=service.InspectPreparedAsync(asset).GetAwaiter().GetResult();Check(stage is not null&&stage.Files.Count==2&&!stage.CatalogCompatible,"Archive not recovered or invented compatibility");Check(stage!.Files.Single(f=>f.RelativePath.EndsWith(".dll")).Version=="Unknown version","Synthetic bytes invented a DLL version");
        var again=Service(root).InspectPreparedAsync(asset).GetAwaiter().GetResult();Check(again?.Directory==stage.Directory&&File.ReadAllBytes(path).SequenceEqual(bytes),"New session lost snapshot or changed archive");Check(Service(root).InspectPreparedAsync(asset with {Tag="v98.0.0",Name="XeSS_SDK_98.0.0.zip",DownloadUrl="https://github.com/intel/xess/releases/download/v98.0.0/XeSS_SDK_98.0.0.zip",ReleaseUrl="https://github.com/intel/xess/releases/tag/v98.0.0"}).GetAwaiter().GetResult() is null,"Different SDK borrowed snapshot");
    }
    private static void RecoveryRefusals(){
        var bytes=Zip();var asset=Asset(bytes);var service=Service(Temp());Directory.CreateDirectory(service.Runtime.PackageDirectory);File.WriteAllText(Path.Combine(service.Runtime.PackageDirectory,"libxess_fg.dll"),"Independent pinned cache fixture");Check(service.InspectPreparedAsync(asset).GetAwaiter().GetResult() is null,"Pinned cache treated as selected SDK");
        var corrupt=(byte[])bytes.Clone();corrupt[0]^=1;var path=PutArchive(service,asset,corrupt);Reject(()=>service.InspectPreparedAsync(asset).GetAwaiter().GetResult());Check(File.ReadAllBytes(path).SequenceEqual(corrupt)&&!Directory.Exists(service.Staging),"Corrupt archive was replaced or staged");Check(service.InspectPreparedAsync(asset with {Sha256=null}).GetAwaiter().GetResult() is null,"Digestless local archive became verified");
    }
    private static void Import(){
        var bytes=Zip();var asset=Asset(bytes);var source=Path.Combine(Temp(),asset.Name);File.WriteAllBytes(source,bytes);var service=Service(Temp());var stage=service.ImportArchiveAsync(source,asset).GetAwaiter().GetResult();Check(stage.Files.Count==2&&File.ReadAllBytes(source).SequenceEqual(bytes),"Import failed or altered source");var before=File.ReadAllBytes(Path.Combine(service.Downloads,asset.Tag,asset.Name));
        File.WriteAllBytes(source,bytes.Select(b=>(byte)(b^1)).ToArray());Reject(()=>service.ImportArchiveAsync(source,asset).GetAwaiter().GetResult());Check(File.ReadAllBytes(Path.Combine(service.Downloads,asset.Tag,asset.Name)).SequenceEqual(before),"Rejected source damaged existing archive");
        var empty=Service(Temp());Reject(()=>empty.ImportArchiveAsync(source,asset).GetAwaiter().GetResult());Check(!Directory.EnumerateFiles(empty.Store,"*.partial",SearchOption.AllDirectories).Any(),"Failed import left partial data");Reject(()=>empty.ImportArchiveAsync(Path.Combine(Temp(),"missing.zip"),asset).GetAwaiter().GetResult());
    }
    private static void ImportRefusals(){var bytes=Zip();var asset=Asset(bytes);var source=Path.Combine(Temp(),asset.Name);File.WriteAllBytes(source,bytes);var service=Service(Temp());using var cancel=new CancellationTokenSource();cancel.Cancel();Reject(()=>service.ImportArchiveAsync(source,asset,cancel.Token).GetAwaiter().GetResult());Reject(()=>service.ImportArchiveAsync(source,asset with{Sha256=null}).GetAwaiter().GetResult());Check(!Directory.Exists(service.Staging)&&!Directory.EnumerateFiles(service.Store,"*.zip",SearchOption.AllDirectories).Any(),"Unverified/cancelled source was staged");}
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static IEnumerable<DependencyObject> Tree(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Tree(child))yield return nested;}}
    private static void Drain(MainWindow main){main.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);main.UpdateLayout();}
    private static void Wizard()=>WpfCheckHost.Run(()=>{
        var main=new MainWindow();var type=typeof(MainWindow);main.Loaded-=type.GetMethod("Window_Loaded",Flags)!.CreateDelegate<RoutedEventHandler>(main);main.Show();
        var log=new StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(log);System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        void Event(string method)=>type.GetMethod(method,Flags)!.Invoke(main,[main,new RoutedEventArgs()]);
        try{
            var root=Temp();var exe=Path.Combine(root,"Own-not-launched.exe");File.WriteAllText(exe,"Own scanner fixture");type.GetField("selectedGameRoot",Flags)!.SetValue(main,root);type.GetField("selectedExe",Flags)!.SetValue(main,exe);
            Event("SetupWizard_Click");var view=(SetupWizardView)((ContentControl)main.FindName("DllInstallationPage")).Content;var next=(Button)view.FooterPanel.Children[2];next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var until=DateTime.UtcNow.AddSeconds(5);while(!next.IsEnabled&&DateTime.UtcNow<until)Drain(main);Check(next.IsEnabled,"Read-only scan did not finish");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);
            Tree(view.ContentPanel).OfType<Button>().Single(b=>b.Content is Grid g&&Tree(g).OfType<TextBlock>().Any(t=>LocalizedPresentation.SourceText(t,TextBlock.TextProperty)=="Intel XeMFG")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);
            var picker=Tree(view.ContentPanel).OfType<ComboBox>().Single();picker.SelectedIndex=1;Drain(main);Check(!next.IsEnabled,"Vulkan advanced without requirements/target");var confirmations=Tree(view.ContentPanel).OfType<CheckBox>().ToArray();Check(confirmations.Length==2,"Experimental input confirmation removed");confirmations[0].IsChecked=true;confirmations[1].IsChecked=true;Check(!next.IsEnabled,"Missing explicit target accepted");
            foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(main);Check(Tree(view.ContentPanel).OfType<TextBlock>().Any(t=>t.Text==LocalizedPresentation.Language.Lookup("Both integration paths use the same pinned Intel DLL pair. The API changes compatibility requirements, not the runtime package.")),"API package distinction not localized");}
            picker=Tree(view.ContentPanel).OfType<ComboBox>().Single();picker.SelectedIndex=0;Drain(main);Check(Tree(view.ContentPanel).OfType<CheckBox>().Single().IsChecked==false&&!next.IsEnabled,"API change retained confirmation");Check(view.SidebarPanel.Children.Count==3,"No plan presented as successful zero changes");
            Event("NvidiaComponents_Click");Drain(main);Check(main.FindName("ExpertDiagnosticsExpander") is null,"Duplicate expert entry remains on Components");
            listener.Flush();Check(!log.ToString().Contains("Error:"),"WPF binding errors: "+log);Check(File.ReadAllText(exe)=="Own scanner fixture","UI check changed test target");
        }finally{System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);main.Close();}
    });
    private static void Translations(){var language=new UiLocalization();foreach(var culture in UiLocalization.Cultures.Where(c=>c!="en-US")){language.SetCulture(culture,false);foreach(var text in new[]{"Advanced Tools","Use existing SDK ZIP","Selected SDK Components","MFG Unlock Compatible","DLL Installation","Official Latest · v3.0.2 · SDK version is not a DLL version.","Prepared SDK snapshot · MFG Unlock Compatible · exact pinned runtime catalog","Downloading Intel SDK · 42 / 100 bytes","Integration path / Graphics API","Checking the selected SDK's local archive and staging…"}){var translated=language.Translate(text);Check(translated!=text,"English fallback: "+culture+" / "+text);Check(!translated.Contains("Official Latest")&&!translated.Contains("SDK version is not a DLL version")&&!translated.Contains("exact pinned runtime catalog"),"Partial English status: "+translated);}}}
}

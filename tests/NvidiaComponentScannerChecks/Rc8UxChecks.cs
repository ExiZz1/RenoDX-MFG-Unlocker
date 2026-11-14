using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Rc8UxChecks
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,EntryPoint="GetPrivateProfileIntW")]
    private static extern uint ReadIniInteger(string section,string key,int fallback,string file);
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return ("rc8 MFG settings XeFG integer and Boolean formats preserve saved data",Formats);
        yield return ("rc8 MFG settings reject unknown versions duplicate keys and unsafe paths",Rejections);
        yield return ("rc8 MFG settings reject stale files and game or addon guard changes",Guards);
        yield return ("rc8 MFG settings two INI transaction restores exact original bytes",()=>Cycle(false));
        yield return ("rc8 MFG settings missing XeFG INI is created only on save and removed on restore",()=>Cycle(true));
        yield return ("rc8 MFG settings partial two INI failure rolls back first file",Rollback);
        yield return ("rc8 embedded wizard preserves state and blocks navigation or close while writing",Navigation);
    }
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or InvalidOperationException){return;}throw new Exception("Expected refusal");}
    private static string Root(){var path=Path.Combine(AppContext.BaseDirectory,"Rc8Isolated",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static readonly Dictionary<string,string> Edit=new(){{"UIStabilization","1"},{"Vulkan","0"}};
    private static void Formats()
    {
        var root=Root();var file=Path.Combine(root,"MFGAdaUnlock.ini");var service=new MfgUnlockSettingsService(true);
        var input="; original\r\n[Other]\r\nUnknown=value ; keep\r\n[XeFG]\r\nVulkan=2\r\nVulkanPresentMetering=-1\r\nXeFGFlipQueue=0\r\nUIStabilization=On\r\nUnmanaged=7\r\n";File.WriteAllText(file,input);
        var snap=service.Load(file,root,"1.7.0.0");Check(snap.IsSupportedAddonVersion&&snap.Settings.Count==4,"Wrong XeFG gate");Check(snap.Settings.All(s=>s.IsValid),"Integer or Boolean form rejected");Check(snap.Settings.First(s=>s.Definition.Key=="Vulkan").RawValue=="2","Saved value lost");Check(snap.Settings.First(s=>s.Definition.Key=="UIStabilization").EffectiveValue=="1","Boolean alias not canonicalized");
        Check(System.Text.Encoding.UTF8.GetString(service.CreateUpdatedBytes(snap,new Dictionary<string,string>{{"XeFGFlipQueue","1"}})).Contains("Vulkan=2\r\nVulkanPresentMetering=-1"),"Unedited integer changed");Check(File.ReadAllText(file)==input,"Preview wrote configuration");
        File.WriteAllText(file,"[XeFG]\nVulkan=2 ; keep integer comment\nUIStabilization=off\n");Check(ReadIniInteger("XeFG","Vulkan",0,file)==2,"Native integer comment interpretation differs");
        var commented=service.Load(file,root,"1.7.0.0");Check(commented.Settings.Single(s=>s.Definition.Key=="Vulkan").IsValid,"Valid integer comment rejected");Check(System.Text.Encoding.UTF8.GetString(service.CreateUpdatedBytes(commented,new Dictionary<string,string>{{"Vulkan","0"}})).Contains("Vulkan=0 ; keep integer comment"),"Integer inline comment lost");
        foreach(var pair in new[]{("1","1"),("true","1"),("ON","1"),("0","0"),("False","0"),("off","0")}){File.WriteAllText(file,"[XeFG]\nUIStabilization="+pair.Item1);var state=service.Load(file,root,"1.7.0.0").Settings.Single(s=>s.Definition.Key=="UIStabilization");Check(state.IsValid&&state.EffectiveValue==pair.Item2,"Unsupported Boolean alias");}
        File.WriteAllText(file,"[XeFG]\nUIStabilization=2");var invalid=service.Load(file,root,"1.7.0.0");Check(!invalid.Settings.Single(s=>s.Definition.Key=="UIStabilization").IsValid,"UIStabilization treated as arbitrary integer");Reject(()=>service.CreateUpdatedBytes(invalid,Edit));
    }
    private static void Rejections()
    {
        var root=Root();var file=Path.Combine(root,"MFGAdaUnlock.ini");var service=new MfgUnlockSettingsService(true);File.WriteAllText(file,"[XeFG]\nVulkan=1\n");
        foreach(var version in new[]{"1.6.0.0","1.7.0-beta.1",""}){var s=service.Load(file,root,version);Check(!s.IsSupportedAddonVersion,"Unknown version writable");Reject(()=>service.CreateUpdatedBytes(s,Edit));}
        Reject(()=>service.Load(Path.Combine(Root(),"MFGAdaUnlock.ini"),root,"1.7.0.0"));Reject(()=>service.Load(Path.Combine(root,"ReShade.ini"),root,"1.7.0.0"));
        foreach(var text in new[]{"[XeFG]\nVulkan=1\nvUlKaN=0","[XeFG]\nVulkan=1\n[XeFG]\nVulkan=0","[XeFG] ; ambiguous heading\nVulkan=1"}){File.WriteAllText(file,text);Reject(()=>service.Load(file,root,"1.7.0.0"));Check(File.ReadAllText(file)==text,"Rejected file changed");}
        File.WriteAllText(file,"[XeFG]\nUIStabilization=true ; not a recognized ReadBool token");var inline=service.Load(file,root,"1.7.0.0");Check(!inline.Settings.Single(s=>s.Definition.Key=="UIStabilization").IsValid,"Ambiguous inline Boolean comment accepted");
    }
    private static void Guards()
    {
        var root=Root();var file=Path.Combine(root,"MFGAdaUnlock.ini");var service=new MfgUnlockSettingsService(true);File.WriteAllText(file,"[XeFG]\nVulkan=1\n");var snapshot=service.Load(file,root,"1.7.0.0");
        Reject(()=>service.PrepareTransaction(snapshot,Edit,()=>false));var calls=0;Reject(()=>service.PrepareTransaction(snapshot,Edit,()=>++calls==1));Check(File.ReadAllText(file)=="[XeFG]\nVulkan=1\n","Guard refusal wrote file");
        File.AppendAllText(file,"; external change\n");Reject(()=>service.PrepareTransaction(snapshot,Edit,()=>true));Check(File.ReadAllText(file).Contains("external change"),"Stale preview erased external data");
        snapshot=service.Load(file,root,"1.7.0.0");Reject(()=>service.CreateUpdatedBytes(snapshot,new Dictionary<string,string>{{"Vulkan","true"}}));Reject(()=>service.CreateUpdatedBytes(snapshot,new Dictionary<string,string>{{"Unknown","1"}}));
    }
    private static void Cycle(bool missing)
    {
        var root=Root();var game=Path.Combine(root,"game");Directory.CreateDirectory(game);var mainPath=Path.Combine(game,"ReShade.ini");var xePath=Path.Combine(game,"MFGAdaUnlock.ini");var original="; retained\r\n[Other]\r\nKey=abc ; note\r\n[RenoDX.MFGUnlock]\r\nEnabled=1\r\n";var xeOriginal="; XeFG retained\n[Other]\nKey=42\n[XeFG]\nVulkan=1\nUIStabilization=off\nUnknown=7\n";File.WriteAllText(mainPath,original);if(!missing)File.WriteAllText(xePath,xeOriginal);
        var main=new MfgUnlockSettingsService();var xe=new MfgUnlockSettingsService(true);var a=main.Load(mainPath,game,"1.7.0.0");var b=xe.Load(xePath,game,"1.7.0.0");Check(b.ConfigExists==!missing,"Missing INI status wrong");Check(File.Exists(xePath)==!missing,"Load created missing file");
        var files=new[]{main.PrepareTransaction(a,new Dictionary<string,string>{{"Enabled","0"}},()=>true),xe.PrepareTransaction(b,Edit,()=>true)};var backup=new BackupRestoreService(Path.Combine(root,"transactions"));var result=backup.ExecuteAsync(game,files).GetAwaiter().GetResult();Check(File.ReadAllText(xePath).Contains("UIStabilization=1"),"XeFG save omitted");Check(File.ReadAllText(mainPath).Contains("Enabled=0"),"Main save omitted");if(!missing)Check(File.ReadAllText(xePath).Contains("[Other]\nKey=42")&&File.ReadAllText(xePath).Contains("Unknown=7"),"Other data lost");
        backup.RestoreAsync(game,result.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllText(mainPath)==original,"Main restore differs");Check(missing?!File.Exists(xePath):File.ReadAllText(xePath)==xeOriginal,"XeFG restore differs");
    }
    private static void Rollback()
    {
        var root=Root();var game=Path.Combine(root,"game");Directory.CreateDirectory(game);var a=Path.Combine(game,"ReShade.ini");var b=Path.Combine(game,"MFGAdaUnlock.ini");File.WriteAllText(a,"[RenoDX.MFGUnlock]\nEnabled=1\n");File.WriteAllText(b,"[XeFG]\nVulkan=1\n");var originalA=File.ReadAllBytes(a);var originalB=File.ReadAllBytes(b);var main=new MfgUnlockSettingsService();var xe=new MfgUnlockSettingsService(true);var files=new[]{main.PrepareTransaction(main.Load(a,game,"1.7.0.0"),new Dictionary<string,string>{{"Enabled","0"}},()=>true),xe.PrepareTransaction(xe.Load(b,game,"1.7.0.0"),Edit,()=>true)};
        var backup=new BackupRestoreService(Path.Combine(root,"transactions"));Reject(()=>backup.ExecuteAsync(game,files,i=>{if(i==1)throw new IOException("Injected second file error");},null).GetAwaiter().GetResult());Check(File.ReadAllBytes(a).SequenceEqual(originalA)&&File.ReadAllBytes(b).SequenceEqual(originalB),"Partial INI transaction not rolled back");
    }
    private static void Navigation()=>WpfCheckHost.Run(()=>{
        var main=new MainWindow();var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(MainWindow);
        // Prevent Steam/network startup in this isolated WPF navigation check.
        main.Loaded-=type.GetMethod("Window_Loaded",flags)!.CreateDelegate<RoutedEventHandler>(main);
        void Invoke(string name)=>type.GetMethod(name,flags)!.Invoke(main,[main,new RoutedEventArgs()]);
        main.Show();Invoke("SetupWizard_Click");main.UpdateLayout();var page=(ContentControl)main.FindName("DllInstallationPage");var view=(SetupWizardView)page.Content;Check(main.OwnedWindows.Count==0,"Wizard still opens a separate Window");Check(page.Visibility==Visibility.Visible,"DLL page missing");
        view.Summary(null,null,null);Check(view.SidebarPanel.Children.Count==3,"Empty summary still contains counters");
        Invoke("Home_Click");Invoke("SetupWizard_Click");Check(ReferenceEquals(page.Content,view),"Returning duplicated or reset wizard");
        type.GetField("wizardOperationBusy",flags)!.SetValue(main,true);
        try{Invoke("Home_Click");Check(page.Visibility==Visibility.Visible,"Navigation escaped during write");main.Close();Check(main.IsVisible,"Main window closed during write");Invoke("SetupWizard_Click");Check(ReferenceEquals(page.Content,view),"Parallel wizard created");}finally{type.GetField("wizardOperationBusy",flags)!.SetValue(main,false);}
        Invoke("MfgSettings_Click");
        Invoke("SettingsGuide_Click");Check(!((Button)main.FindName("MfgSettingsSaveButton")).IsEnabled&&main.OwnedWindows.Count==0,"Unloaded Guide enabled writes or opened dialog");
        var rowsType=type.GetNestedType("MfgUnlockSettingRow",BindingFlags.NonPublic)!;
        object Row(MfgUnlockSettingDefinition definition){var row=Activator.CreateInstance(rowsType,[definition,"Not set",definition.DefaultValue,false,(Action)(()=>{})])!;rowsType.GetProperty("Details")!.SetValue(row,"Technical Key: "+definition.Key+"\nDefault: "+definition.DefaultValue);return row;}
        ((ItemsControl)main.FindName("MfgSettingsItemsControl")).ItemsSource=new[]{Row(new MfgUnlockSettingsService().Definitions[0])};
        ((ItemsControl)main.FindName("XeMfgSettingsItemsControl")).ItemsSource=new[]{Row(new MfgUnlockSettingsService(true).DefinitionsFor("1.7.0.0")[3])};
        var trace=new StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(trace);System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        main.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Invoke("SettingsGuide_Click");main.UpdateLayout();
        IEnumerable<DependencyObject> Descendants(DependencyObject parent){for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
        var expanders=Descendants((FrameworkElement)main.FindName("MfgSettingsPage")).OfType<Expander>().Where(e=>rowsType.IsInstanceOfType(e.DataContext)).ToArray();Check(expanders.Length==2&&expanders.All(e=>e.IsExpanded),"Guide did not expand both INI sections inline");Check(main.OwnedWindows.Count==0,"Guide created a separate dialog");
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);listener.Flush();Check(!trace.ToString().Contains("Error:"),"New settings binding errors: "+trace);
        Invoke("SetupWizard_Click");((Button)view.FooterPanel.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(page.Content is null&&page.Visibility==Visibility.Collapsed,"Close left stale embedded wizard state");main.Close();
    });
}

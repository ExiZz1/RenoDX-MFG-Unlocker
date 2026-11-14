using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Stage21Checks
{
    public static IEnumerable<(string Name,Action Run)> All(){yield return("stage21 selected Intel SDK and pinned catalog stay independent",IntelModes);yield return("stage21 NVIDIA install navigation goes to existing Wizard and preserves read-only tools",Navigation);yield return("stage21 NVIDIA SDK summary rejects stale different-release metadata",NvidiaSummary);}
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    private static MainWindow Shell(){var main=new MainWindow();main.Loaded-=typeof(MainWindow).GetMethod("Window_Loaded",Flags)!.CreateDelegate<RoutedEventHandler>(main);main.Show();return main;}
    private static void Invoke(MainWindow main,string method,params object?[] args)=>typeof(MainWindow).GetMethod(method,Flags)!.Invoke(main,args);
    private static void Event(MainWindow main,string method)=>Invoke(main,method,main,new RoutedEventArgs());
    private static void Drain(MainWindow main){main.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);main.UpdateLayout();}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}}
    private static string Text(DependencyObject root)=>string.Join("\n",Descendants(root).OfType<TextBlock>().Select(t=>t.Text));
    private static IntelSdkAsset Asset(string tag,string hash)=>new("XeSS_SDK_"+tag[1..]+".zip","https://github.com/intel/xess/releases/download/"+tag+"/XeSS_SDK_"+tag[1..]+".zip",100,hash,tag,"Own UI fixture "+tag,null,"https://github.com/intel/xess/releases/tag/"+tag,"Own metadata fixture",false,false);
    private static void IntelModes()=>WpfCheckHost.Run(()=>{
        var main=Shell();try{
            Event(main,"NvidiaComponents_Click");Invoke(main,"SetComponentsProvider",true);var view=(IntelSdkManagerView)main.FindName("IntelSdkView");var type=typeof(MainWindow).GetNestedType("IntelSdkRow",BindingFlags.NonPublic)!;
            var a=Asset("v33.0.2",new string('a',64));var b=Asset("v33.0.1",new string('b',64));var rowA=Activator.CreateInstance(type,[a])!;var rowB=Activator.CreateInstance(type,[b])!;view.ReleasesList.ItemsSource=new[]{rowA,rowB};view.ReleasesList.SelectedItem=rowA;Drain(main);
            Check(view.LocalFiles.Children.Count==0&&view.LocalStatus.Text==LocalizedPresentation.Language.Lookup("Download / Prepare the selected SDK to inspect actual DLL contents and versions."),"Unknown SDK borrowed pinned versions");
            // Presentation-only snapshot. No vendor DLL, ZIP or installation is read or written.
            var directory=Path.Combine(AppContext.BaseDirectory,"Stage21Ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            var snapshot=new IntelSdkStage(a,directory,[new("bin/libxess_fg.dll",Path.Combine(directory,"libxess_fg.dll"),"9.8.7.6",new string('c',64),100,"Native x64 DLL")],0,false);
            Invoke(main,"ShowIntelStage",snapshot);Drain(main);Check(Text(view.LocalFiles).Contains("9.8.7.6")&&view.LocalFiles.Children.Count==snapshot.Files.Count,"Selected SDK invented entries or ignored its own DLL metadata");
            view.CompatibleModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);var pinned=Text(view.LocalFiles);Check(pinned.Contains("1.3.1.78")&&pinned.Contains("1.3.2.10")&&!pinned.Contains("9.8.7.6"),"Pinned catalog borrowed SDK values");
            view.ReleasesList.SelectedItem=rowB;Drain(main);Check(Text(view.LocalFiles)==pinned,"SDK selection changed pinned catalog");
            view.SelectedSdkModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);Check(view.LocalFiles.Children.Count==0&&!view.OpenStagingButton.IsEnabled,"Unprepared SDK reused another SDK's files");
            view.ReleasesList.SelectedItem=rowA;Drain(main);Check(Text(view.LocalFiles).Contains("9.8.7.6"),"Verified presentation snapshot was lost on return");
            foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);view.CompatibleModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);Check(view.LocalTitle.Text==LocalizedPresentation.Language.Lookup("MFG Unlock Compatible"),"Catalog heading not translated");Check(view.PanelContextText.Text==LocalizedPresentation.Language.Lookup("Catalog versions are independent of SDK selection. Setup Wizard rechecks the pinned files before use."),"Catalog hint not translated");view.SelectedSdkModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);Check(view.LocalTitle.Text==LocalizedPresentation.Language.Lookup("Selected SDK Components")&&Text(view.LocalFiles).Contains("9.8.7.6"),"Switching language mixed modes");}
        }finally{main.Close();}
    });
    private static void Navigation()=>WpfCheckHost.Run(()=>{
        var main=Shell();try{
            Event(main,"NvidiaComponents_Click");Invoke(main,"SetComponentsProvider",false);Drain(main);
            ((Button)main.FindName("NvidiaInstallPreviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);Check(((FrameworkElement)main.FindName("DllInstallationPage")).Visibility==Visibility.Visible,"NVIDIA action opened duplicate installer");
            Event(main,"NvidiaComponents_Click");Event(main,"NvidiaInstallPreview_Click");Drain(main);
            Check(((FrameworkElement)main.FindName("AdvancedRuntimePage")).Visibility==Visibility.Visible&&((FrameworkElement)main.FindName("NvidiaComponentsPage")).Visibility==Visibility.Collapsed,"Advanced inspector remains on main Components page");
            Check(!((Button)main.FindName("InstallPreviewInstallButton")).IsVisible&&!((CheckBox)main.FindName("AdvancedRuntimeMode")).IsVisible,"Duplicate manual Apply/mode still visible");
            Check(((Button)main.FindName("InstallPreviewRefreshButton")).IsVisible,"Automatic profile inspector removed");
            Check(main.OwnedWindows.Count==0,"A separate Wizard window was created");
        }finally{main.Close();}
    });
    private static void NvidiaSummary()=>WpfCheckHost.Run(()=>{
        var main=Shell();try{
            var a=new StreamlineSdkAsset("streamline-sdk-v2.14.16.zip","https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.16/streamline-sdk-v2.14.16.zip",100,new string('a',64),"v2.14.16","Own UI fixture",null,"https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16");
            typeof(MainWindow).GetField("selectedStreamlineAsset",Flags)!.SetValue(main,a);typeof(MainWindow).GetField("verifiedStreamlineStage",Flags)!.SetValue(main,new VerifiedStreamlineStage(a,"not read","not read",DateTimeOffset.UtcNow,[new("sl.common.dll","bin/x64/sl.common.dll",new string('b',64),"7.6.5.4")]));Invoke(main,"UpdateNvidiaSdkSummary");Drain(main);
            Check(((TextBlock)main.FindName("NvidiaPreparedSdkText")).Text.Contains("1"),"Prepared SDK inventory omitted");typeof(MainWindow).GetField("selectedStreamlineAsset",Flags)!.SetValue(main,a with {ReleaseTag="v2.14.1",Sha256=new string('c',64)});Invoke(main,"UpdateNvidiaSdkSummary");Drain(main);
            Check(((TextBlock)main.FindName("NvidiaPreparedSdkText")).Text==LocalizedPresentation.Language.Lookup("Contained DLL names and versions are Unknown until this SDK is prepared."),"Different SDK inherited stale prepared metadata");
        }finally{main.Close();}
    });
}

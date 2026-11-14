using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class UiCleanupChecks
{
    public static IEnumerable<(string Name,Action Run)> All(){yield return("cleanup sizing respects work areas and simulated DPI cases",Sizing);yield return("cleanup real WPF dialogs help sections and live localization",Presentation);}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Sizing()
    {
        // Logical work-area simulations, not a change to actual Windows display settings.
        foreach(var (width,height,scale) in new[]{(1920d,1080d,1d),(1366d,768d,1d),(2560d,1440d,1d),(3840d,2160d,2d),(3840d,2160d,2.5d)}){var work=new Size(width/scale,(height-48*scale)/scale);var fit=DesktopWindowLayout.PreferredSize(work);Check(fit.Width<=work.Width&&fit.Height<=work.Height&&fit.Width<=1440&&fit.Height<=900,"Work-area fit failed");}
        Check(DesktopWindowLayout.PreferredSize(new Size(2000,1200))==new Size(1440,900),"Preferred dimensions changed");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private static void Presentation()=>WpfCheckHost.Run(()=>{
        var main=new MainWindow();var type=typeof(MainWindow);const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        main.Loaded-=type.GetMethod("Window_Loaded",flags)!.CreateDelegate<RoutedEventHandler>(main);main.Show();
        var trace=new System.IO.StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(trace);System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        void Drain(){main.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);main.UpdateLayout();}
        Window? info=null;Window? language=null;
        try{
            info=main.CreateInfoDialog("Details",main.ComponentDetailsContent("Component: isolated.dll\nInstalled version: 1.2.3\nTarget: C:\\isolated\\component.dll\nSHA-256: "+new string('a',64)));info.Show();Drain();Check(info.Content is Border {CornerRadius.TopLeft:>0},"Dialog lacks rounded shell");Check(info.Background==Brushes.Transparent&&info.SizeToContent==SizeToContent.Height&&info.ActualHeight<600,"Short details still reserves an empty fixed window");info.Close();info=null;
            language=main.CreateLanguageDialog();language.Show();Drain();var choices=Descendants(language).OfType<Button>().Where(b=>b.Tag is string tag&&UiLocalization.Cultures.Contains(tag)).ToArray();Check(choices.Length==UiLocalization.Cultures.Length&&language.ActualHeight<=language.MaxHeight,"Language choices or bounded height lost");
            type.GetMethod("MfgSettings_Click",flags)!.Invoke(main,[main,new RoutedEventArgs()]);
            var rowsType=type.GetNestedType("MfgUnlockSettingRow",BindingFlags.NonPublic)!;
            var sourceSections=new List<SettingHelpSection>();
            object Row(MfgUnlockSettingDefinition definition,string file,string section){var row=Activator.CreateInstance(rowsType,[definition,"Not set",definition.DefaultValue,false,(Action)(()=>{})])!;var sections=MainWindow.HelpSections(definition,file,section);sourceSections.AddRange(sections);rowsType.GetProperty("HelpSections")!.SetValue(row,sections);return row;}
            ((ItemsControl)main.FindName("MfgSettingsItemsControl")).ItemsSource=new MfgUnlockSettingsService().DefinitionsFor("1.7.0.0").Select(d=>Row(d,"ReShade.ini","RenoDX.MFGUnlock")).ToArray();
            ((ItemsControl)main.FindName("XeMfgSettingsItemsControl")).ItemsSource=new MfgUnlockSettingsService(true).Definitions.Select(d=>Row(d,"MFGAdaUnlock.ini","XeFG")).ToArray();
            foreach(var culture in UiLocalization.Cultures){
                choices.Single(c=>(string)c.Tag==culture).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain();
                type.GetMethod("SettingsGuide_Click",flags)!.Invoke(main,[main,new RoutedEventArgs()]);Drain();
                var texts=Descendants((FrameworkElement)main.FindName("MfgSettingsPage")).OfType<TextBlock>().Select(t=>t.Text).ToHashSet();var l=LocalizedPresentation.Language;
                foreach(var section in sourceSections){Check(l.GetCatalog("en-US").ContainsKey(section.Heading),"Missing heading resource: "+section.Heading);Check(texts.Contains(l.Lookup(section.Heading)),"Untranslated heading: "+culture+" / "+section.Heading);Check(texts.Contains(section.IsTechnical?section.Content:l.Translate(section.Content)),"Missing translated help body: "+culture+" / "+section.Content);if(!section.IsTechnical){Check(l.GetCatalog("en-US").ContainsKey(section.Content),"Missing help body resource: "+section.Content);var sharedSpanishLabel=culture=="es-ES"&&section.Content=="Vulkan XeMFG (experimental)";if(culture!="en-US"&&!sharedSpanishLabel)Check(l.Lookup(section.Content)!=section.Content,"English fallback in help: "+culture+" / "+section.Content);}}
                Check(texts.Contains("UIStabilization")&&texts.Contains("MFGAdaUnlock.ini · [XeFG]"),"Technical names localized");
            }
            language.Close();language=null;
            var work=DesktopWindowLayout.WorkArea(main);type.GetMethod("MaximizeRestore_Click",flags)!.Invoke(main,[main,new RoutedEventArgs()]);Drain();Check(main.WindowState==WindowState.Maximized&&main.ActualHeight<=work.Height+2&&main.ActualWidth<=work.Width+2,"Maximized shell exceeds current monitor work area");type.GetMethod("MaximizeRestore_Click",flags)!.Invoke(main,[main,new RoutedEventArgs()]);Drain();Check(main.WindowState==WindowState.Normal,"Restore did not return to normal");
            var home=(FrameworkElement)main.FindName("HomePage");Check(!Descendants(home).OfType<Button>().Any(b=>b.Content as string=="Setup Wizard"),"Duplicate Home wizard button retained");
            listener.Flush();Check(!trace.ToString().Contains("Error:"),"WPF binding error: "+trace);
        }finally{System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);language?.Close();info?.Close();main.Close();}
    });
}

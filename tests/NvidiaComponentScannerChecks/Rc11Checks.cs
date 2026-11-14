using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Rc11Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("rc11 restart distinguishes unknown verified pending current and historical sessions",Restart);
        yield return("rc11 evidence links reject unsupported malformed and command-like targets",Urls);
        yield return("rc11 Profile Details localizes labels and preserves attributed source text",Profile);
        yield return("rc11 evidence hyperlink and copy require explicit interaction",Links);
        yield return("rc11 Restore Details stays single-line in six languages at narrow width",RestoreDetails);
        yield return("rc11 transaction list bounds zero one and many entries without horizontal scroll",TransactionList);
        yield return("rc11 vector gear scales and profile tooltip anchors to status at supported sizes",GearAndTooltip);
    }
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Wpf(Action action)=>WpfCheckHost.Run(()=>{
        var trace=new StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(trace);var source=System.Diagnostics.PresentationTraceSources.DataBindingSource;source.Listeners.Add(listener);
        try{action();listener.Flush();Check(!trace.ToString().Contains("Error:"),"WPF binding errors: "+trace);}finally{source.Listeners.Remove(listener);listener.Dispose();}
    });
    private static IEnumerable<DependencyObject> Tree(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Tree(child))yield return nested;}}
    private static void Drain(Window w){w.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
    private static MainWindow Shell(){LocalizedPresentation.Language.SetCulture("en-US",false);var m=new MainWindow();m.Loaded-=typeof(MainWindow).GetMethod("Window_Loaded",Flags)!.CreateDelegate<RoutedEventHandler>(m);m.Show();return m;}
    private static void Restart()
    {
        var now=new DateTimeOffset(2026,10,31,12,0,0,TimeSpan.Zero);var exe=Path.Combine(Path.GetTempPath(),"Rc11-own.exe");
        RuntimeDiagnosticReport Read(bool verified,DateTimeOffset? installed,RuntimeProcessSnapshot process,bool invalid=false)=>RuntimeDiagnosticsService.Analyze(exe,"1.0.0.0",process,[new(Path.Combine(Path.GetTempPath(),"MFGAdaUnlock-XeMFG.log"),"{\"event\":\"native_session\",\"utc\":\"2026-10-31T11:58:00Z\",\"executable\":\"Rc11-own.exe\",\"executable_version\":\"1.0.0.0\",\"addon\":\"1.7.0\",\"pid\":42,\"tick\":1000}\n{\"event\":\"native_modules\",\"pid\":42,\"tick\":"+(invalid?"0":"1001")+",\"modules\":[{\"name\":\"sl.interposer.dll\",\"version\":\"2.14.16\"}]}",now)],now,installed,verified);
        var current=new RuntimeProcessSnapshot(42,now.AddMinutes(-2),"synthetic matching process");var absent=new RuntimeProcessSnapshot(null,null,"no process");
        Check(Read(false,null,absent).Statuses.Last().Contains("Not confirmed"),"Unknown restart implies required");
        Check(Read(false,now.AddMinutes(-3),current).Statuses.Last().Contains("Not confirmed"),"Timestamp alone proves restart");
        Check(Read(true,now.AddMinutes(-1),current).Statuses.Last().Contains("Verified file change"),"Pre-change session confirms pending change");
        Check(Read(true,now.AddMinutes(-3),absent).Statuses.Last().Contains("Verified file change"),"Historical session confirms restart");
        Check(Read(true,now.AddMinutes(-3),current).Statuses.Last().Contains("Restart observed"),"Matching later session lost");
        Check(!Read(true,now.AddMinutes(-3),current,true).Statuses.Last().Contains("Restart observed"),"Invalid ticks confirm restart");
        Check(Read(false,null,absent).Statuses.Any(s=>s.StartsWith("Loaded modules: Not verified for current launch")),"Previous module mapping is unclear");
        var missing=RuntimeDiagnosticsService.Analyze(exe,"1.0.0.0",absent,[],now);
        Check(missing.Statuses.Contains("Loaded modules: Not confirmed for the current session"),"Unknown module state says runtime loaded");
        var config=RuntimeDiagnosticsService.Analyze(exe,"1.0.0.0",absent,[],now,now.AddMinutes(-1),configurationHashesVerified:true);
        Check(config.Statuses.Last().Contains("Verified file change")&&config.Statuses[0].Contains("Not verified"),"Verified config change lost or mislabelled DLL installation");
    }
    private static void Urls()
    {
        foreach(var value in new[]{"https://github.com/NVIDIA-RTX/Streamline/releases","http://example.com/a?b=1#c"})Check(EvidenceLinkPolicy.TryGetHttpUri(value,out _),"Valid URL rejected");
        foreach(var value in new[]{"file:///C:/test.exe","javascript:alert(1)","cmd.exe /c test","https://","https://user:secret@example.com","https://example.com/\ncmd","https://example.com/%zz","https:\\example.com"," https://example.com","shell:AppsFolder"})Check(!EvidenceLinkPolicy.TryGetHttpUri(value,out _),"Unsafe URL accepted: "+value);
        var links=EvidenceLinkPolicy.Extract("[2026-10-27] Source (https://example.com/a). file:///x javascript:https://bad.example https://example.com/%zz");
        Check(links.Count==1&&links[0].Url=="https://example.com/a","Extraction accepted malformed/nested schemes or lost punctuation");
    }
    private const string Evidence="Game MFG support: Supported · integration: Partially confirmed · NVIDIA App override for this EXE: Unknown\nEvidence:\n[2026-10-27] Original external claim · https://example.com/source";
    private static void Profile()=>Wpf(()=>{
        var main=Shell();var content=main.ProfileEvidenceContent(Evidence, _=>{},_=>{});var dialog=main.CreateInfoDialog("Profile details",content);try{dialog.Show();foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(dialog);var labels=Tree(content).OfType<TextBlock>().ToArray();Check(labels.Any(t=>t.Text==LocalizedPresentation.Language.Translate(Evidence.Split('\n')[0])),"Profile static labels untranslated");Check(labels.Any(t=>t.Text==LocalizedPresentation.Language.Lookup("Evidence:")),"Evidence heading untranslated");if(culture!="en-US")Check(!labels.Any(t=>t.Text.StartsWith("Game MFG support:")),"English profile label remains");var linked=labels.Single(t=>t.Inlines.OfType<Hyperlink>().Any());Check(new TextRange(linked.ContentStart,linked.ContentEnd).Text==Evidence.Split('\n')[2],"External claim/date/URL changed");}}finally{dialog.Close();main.Close();}
    });
    private static void Links()=>Wpf(()=>{
        var main=Shell();string? opened=null,copied=null;var body=main.ProfileEvidenceContent(Evidence+"\nfile:///C:/untrusted.exe",s=>opened=s,s=>copied=s);var dialog=main.CreateInfoDialog("Profile details",body);try{dialog.Show();Drain(dialog);Check(opened is null&&copied is null,"Evidence action occurred without interaction");var link=Tree(body).OfType<TextBlock>().SelectMany(t=>t.Inlines.OfType<Hyperlink>()).Single();Check(link.Focusable,"Link not keyboard accessible");link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));Check(opened=="https://example.com/source","Link callback incorrect");Tree(body).OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(copied==opened,"Copy URL mismatch");}finally{dialog.Close();main.Close();}
    });
    private static void RestoreDetails()=>Wpf(()=>{
        var main=Shell();var preview=new BackupRestorePreview("own-id","Completed",true,true,"Own fixture",[new("some-long-relative-name.dll","Restore original","Matches installed SHA-256","Own test")]);
        var body=main.RestorePreviewContent(preview,Path.GetTempPath(),main);var window=new Window{Owner=main,Content=body,Width=600,Height=420};try{window.Show();foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(window);var button=Tree(body).OfType<Button>().Single(b=>LocalizedPresentation.SourceText(b,ContentControl.ContentProperty)=="Details");var text=Tree(button).OfType<TextBlock>().Single();Check(text.Text==LocalizedPresentation.Language.Lookup("Details")&&text.TextWrapping==TextWrapping.NoWrap,"Details not translated/single line");Check(button.ActualWidth>=104&&text.ActualWidth>=text.DesiredSize.Width-.5&&text.ActualHeight<24,"Details clipped at 600 DIP: "+culture);var point=button.TranslatePoint(new Point(0,0),window);Check(point.X>=0&&point.X+button.ActualWidth<=window.ActualWidth,"Details outside narrow window");}}finally{window.Close();main.Close();}
    });
    private static void TransactionList()=>Wpf(()=>{
        var main=Shell();var list=new ListBox {ItemTemplate=MainWindow.RestoreTransactionTemplate(),HorizontalContentAlignment=HorizontalAlignment.Stretch};ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);var panel=new Grid();panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new());panel.Children.Add(list);var win=new Window{Owner=main,Content=panel,Width=600,Height=560};try{win.Show();foreach(var count in new[]{0,1,12}){list.ItemsSource=Enumerable.Range(0,count).Select(i=>new{Created="2026-10-31 12:00",Status="Completed",Availability="Restore available",FileCount="Files: 1"}).ToArray();MainWindow.ConfigureRestoreList(list,count);Drain(win);Check(list.ActualHeight<=list.MaxHeight+.5&&list.ActualHeight<=(count<=1?94:170),"List reserves excess empty area");Check(ScrollViewer.GetHorizontalScrollBarVisibility(list)==ScrollBarVisibility.Disabled,"Nested horizontal scrollbar");if(count>0){list.SelectedIndex=count-1;list.ScrollIntoView(list.SelectedItem);Drain(win);Check(list.SelectedIndex==count-1,"Last transaction not selectable");}}}finally{win.Close();main.Close();}
    });
    private static void GearAndTooltip()=>Wpf(()=>{
        var main=Shell();try{
            foreach(var scale in new[]{1.0,1.5,2.0}){var path=new System.Windows.Shapes.Path{Data=(Geometry)main.FindResource("SettingsGearGeometry"),Stroke=Brushes.White,StrokeThickness=1.7,Width=16,Height=16,Stretch=Stretch.Uniform,LayoutTransform=new ScaleTransform(scale,scale)};path.Measure(new Size(100,100));Check(path.DesiredSize.Width>=16*scale-.1&&path.DesiredSize.Height>=16*scale-.1,"Vector gear failed layout scaling");}
            typeof(MainWindow).GetMethod("SetPage",Flags)!.Invoke(main,[Enum.Parse(typeof(MainWindow).GetNestedType("AppPage",BindingFlags.NonPublic)!,"NvidiaComponents")]);
            foreach(var size in new[]{new Size(900,650),new Size(1440,900)}){main.Width=size.Width;main.Height=size.Height;typeof(MainWindow).GetMethod("ApplyCompatibilityAssessment",Flags)!.Invoke(main,[null]);Drain(main);var status=(TextBlock)main.FindName("NvidiaCompatibilityStatusText");Check(ToolTipService.GetPlacement(status)==PlacementMode.Top&&status.HorizontalAlignment==HorizontalAlignment.Left,"Tooltip detached from actual trigger");Check(((TextBlock)main.FindName("NvidiaCompatibilityDetailsText")).ToolTip is null,"Large region still triggers profile hover");var popup=new ToolTip{Content=status.ToolTip,PlacementTarget=status,Placement=ToolTipService.GetPlacement(status),Style=(Style)main.FindResource(typeof(ToolTip))};popup.IsOpen=true;Drain(main);Check(popup.ActualWidth<=420&&popup.ActualHeight<=170&&popup.PlacementTarget==status,"Popup not bounded/anchored");popup.IsOpen=false;}
        }finally{main.Close();}
    });
}

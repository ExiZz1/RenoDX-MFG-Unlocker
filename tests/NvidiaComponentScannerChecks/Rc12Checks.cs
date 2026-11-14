using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Rc12Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("rc12 recursive root SP and backup inventory never authorizes another EXE destination",InventoryScope);
        yield return("rc12 runtime and API changes clear target confirmation and both Preview states",RuntimeChanges);
        yield return("rc12 changing the selected EXE resets the confirmed target and Preview",ExeChange);
        yield return("rc12 loaded numbered sections exact prerequisite hints and scan groups translate in six languages",Languages);
        yield return("rc12 requirement explanations preserve disabled and ready Continue semantics",Reasons);
    }
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static string Temp(){var path=Path.Combine(AppContext.BaseDirectory,"Rc12Checks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static (string Root,string Exe,string SpExe) Fixture()
    {
        var root=Temp();var exe=Path.Combine(root,"bf6.exe");var sp=Path.Combine(root,"SP","bf6.exe");Directory.CreateDirectory(Path.GetDirectoryName(sp)!);File.WriteAllText(exe,"Own root EXE; never launched");File.WriteAllText(sp,"Own SP EXE; never launched");
        foreach(var dir in new[]{root,Path.GetDirectoryName(sp)!,Path.Combine(root,"MFG_Backup_test"),Path.Combine(root,"copy")}){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"sl.common.dll"),"Own inventory fixture "+dir);}
        return(root,exe,sp);
    }
    private static void InventoryScope()
    {
        var f=Fixture();var before=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);var scan=new NvidiaComponentScanner().Scan(f.Exe,f.Root);Check(scan.Error is null,"Scan failed");
        var copies=scan.Entries.Where(e=>e.Name=="sl.common.dll"&&e.Status=="Found").ToArray();Check(copies.Length==4,"Recursive files disappeared");
        Check(copies.Count(e=>WizardScanPresentation.Group(e,f.Exe)==WizardScanPresentation.Selected)==1,"Root scan promotes extra directories");
        Check(copies.Count(e=>WizardScanPresentation.Group(e,f.Exe)==WizardScanPresentation.Other)==1,"SP not retained diagnostic-only");
        Check(copies.Count(e=>WizardScanPresentation.Group(e,f.Exe)==WizardScanPresentation.Copies)==2,"Backup/copy classification lost");
        Check(WizardScanPresentation.Group(copies.Single(e=>Path.GetDirectoryName(e.Path)==Path.GetDirectoryName(f.SpExe)),f.SpExe)==WizardScanPresentation.Selected,"Explicit SP EXE not independent");
        foreach(var intel in new[]{false,true}){
            RuntimeTargetPolicy.Validate(f.Root,f.Exe,f.Root,intel);RuntimeTargetPolicy.Validate(f.Root,f.SpExe,Path.GetDirectoryName(f.SpExe)!,intel);
            Reject(()=>RuntimeTargetPolicy.Validate(f.Root,f.Exe,Path.GetDirectoryName(f.SpExe)!,intel));Reject(()=>RuntimeTargetPolicy.Validate(f.Root,f.SpExe,f.Root,intel));
            foreach(var dir in new[]{"MFG_Backup_test","copy"})Reject(()=>RuntimeTargetPolicy.Validate(f.Root,f.Exe,Path.Combine(f.Root,dir),intel));
        }
        Check(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Read-only scan/policy changed fixture files");
    }
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected target-policy refusal");}
    private static IEnumerable<DependencyObject> Tree(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Tree(child))yield return nested;}}
    private static void Drain(Window w){w.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
    private static void Event(MainWindow main,string method)=>typeof(MainWindow).GetMethod(method,Fields)!.Invoke(main,[main,new RoutedEventArgs()]);
    private static Button Runtime(SetupWizardView view,string name)=>Tree(view.ContentPanel).OfType<Button>().Single(b=>b.Content is Grid g&&Tree(g).OfType<TextBlock>().Any(t=>LocalizedPresentation.SourceText(t,TextBlock.TextProperty)==name));
    private static void Wpf(Action<MainWindow,SetupWizardView,object,string,string,string> action)=>WpfCheckHost.Run(()=>{
        LocalizedPresentation.Language.SetCulture("en-US",false);var main=new MainWindow();main.Loaded-=typeof(MainWindow).GetMethod("Window_Loaded",Fields)!.CreateDelegate<RoutedEventHandler>(main);var trace=new StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(trace);System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try{main.Show();var f=Fixture();typeof(MainWindow).GetField("selectedExe",Fields)!.SetValue(main,f.Exe);typeof(MainWindow).GetField("selectedGameRoot",Fields)!.SetValue(main,f.Root);Event(main,"SetupWizard_Click");var view=(SetupWizardView)((ContentControl)main.FindName("DllInstallationPage")).Content;var next=(Button)view.FooterPanel.Children[2];
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var until=DateTime.UtcNow.AddSeconds(8);while(!next.IsEnabled&&DateTime.UtcNow<until)Drain(main);Check(next.IsEnabled,"Read-only scan timed out");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);
            // Only presentation state is injected below, to emulate the native folder confirmation without opening it.
            // These fake plans are never applied and grant no compatibility or file-write permission.
            var resume=(Action)typeof(MainWindow).GetField("resumeSetupWizard",Fields)!.GetValue(main)!;var state=resume.Target!;Check(state.GetType().GetField("target",Fields) is not null,"Wizard closure changed; update UI fixture explicitly");
            action(main,view,state,f.Root,f.Exe,f.SpExe);listener.Flush();Check(!trace.ToString().Contains("Error:"),"WPF binding errors: "+trace);
        }finally{System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);main.Close();}
    });
    private static void Set(object state,string name,object? value)=>state.GetType().GetField(name,Fields)!.SetValue(state,value);
    private static object? Get(object state,string name)=>state.GetType().GetField(name,Fields)!.GetValue(state);
    private static void SeedConfirmation(object state,string root,string exe)
    {
        RuntimeTargetPolicy.Validate(root,exe,Path.GetDirectoryName(exe)!,false);Set(state,"target",Path.GetDirectoryName(exe));Set(state,"targetConfirmedForExe",exe);Set(state,"inputsConfirmed",true);
        Set(state,"nvidiaPlan",new StreamlineComponentPlan("Synthetic UI snapshot",[],0,0,0,0,true));Set(state,"intelPlan",new IntelInstallPlan(root,exe,Path.GetDirectoryName(exe)!,"Synthetic UI snapshot",[]));Set(state,"consent",true);
    }
    private static void RuntimeChanges()=>Wpf((main,view,state,root,exe,sp)=>{
        var next=(Button)view.FooterPanel.Children[2];Check(!next.IsEnabled,"Unconfirmed integration continued");
        SeedConfirmation(state,root,exe);Runtime(view,"Intel XeMFG").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);
        Check(Get(state,"targetConfirmedForExe") is null&&Get(state,"nvidiaPlan") is null&&Get(state,"intelPlan") is null&&Get(state,"consent") is false,"Runtime switch retained target/plan approval");
        var confirm=Tree(view.ContentPanel).OfType<CheckBox>().Single();confirm.IsChecked=true;Drain(main);Check(!next.IsEnabled,"New runtime reused old target approval");
        var hint=Tree(view.FooterPanel).OfType<TextBlock>().Single(t=>t.Name=="WizardPrerequisiteHint");Check(LocalizedPresentation.SourceText(hint,TextBlock.TextProperty).StartsWith("Review and confirm"),"Exact changed-target requirement missing");
        SeedConfirmation(state,root,exe);confirm.IsChecked=false;confirm.IsChecked=true;Drain(main);Check(next.IsEnabled,"Valid current confirmations cannot continue");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);Check((int)Get(state,"step")! ==3,"Valid Intel confirmation failed to advance");
        ((Button)view.FooterPanel.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);var picker=Tree(view.ContentPanel).OfType<ComboBox>().Single();SeedConfirmation(state,root,exe);picker.SelectedIndex=1;Drain(main);
        Check(Get(state,"targetConfirmedForExe") is null&&Get(state,"intelPlan") is null&&!next.IsEnabled,"API change reused target/Preview");
        foreach(var cb in Tree(view.ContentPanel).OfType<CheckBox>())cb.IsChecked=true;Drain(main);Check(!next.IsEnabled,"Vulkan inputs bypassed target confirmation");
        Runtime(view,"NVIDIA Dynamic MFG").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);
        Check(Tree(view.ContentPanel).OfType<CheckBox>().All(cb=>cb.IsChecked==false)&&!next.IsEnabled,"Returning to NVIDIA auto-confirmed integration or expert mode");
    });
    private static void ExeChange()=>Wpf((main,view,state,root,exe,sp)=>{
        SeedConfirmation(state,root,exe);Event(main,"Home_Click");typeof(MainWindow).GetField("selectedExe",Fields)!.SetValue(main,sp);Event(main,"SetupWizard_Click");Drain(main);
        Check((int)Get(state,"step")! ==0&&Get(state,"target") is null&&Get(state,"targetConfirmedForExe") is null&&Get(state,"intelPlan") is null&&Get(state,"nvidiaPlan") is null,"EXE change retained plan/target");
        var next=(Button)view.FooterPanel.Children[2];next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var until=DateTime.UtcNow.AddSeconds(8);while(!next.IsEnabled&&DateTime.UtcNow<until)Drain(main);Drain(main);Check(Tree(view.ContentPanel).OfType<TextBlock>().Any(t=>t.Text.Contains(sp)),"New SP EXE not shown prominently");
    });
    private static void Languages()=>Wpf((main,view,state,root,exe,sp)=>{
        foreach(var size in new[]{new Size(900,650),new Size(1440,900)}){main.Width=size.Width;main.Height=size.Height;foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(main);
            var labels=Tree(view.ContentPanel).OfType<TextBlock>().ToArray();foreach(var key in new[]{"Choose runtime","Confirm integration","Confirm target directory"})Check(labels.Any(t=>t.Text==LocalizedPresentation.Language.Lookup(key)),"Numbered heading not translated: "+culture+" / "+key);
            foreach(var number in new[]{"1","2","3"})Check(labels.Any(t=>t.Text==number),"Section number missing");
            var hint=Tree(view.FooterPanel).OfType<TextBlock>().Single(t=>t.Name=="WizardPrerequisiteHint");Check(hint.Text==LocalizedPresentation.Language.Lookup("Confirm the integration method and required inputs."),"Hint untranslated");Check(hint.ActualWidth>0&&hint.TextWrapping==TextWrapping.Wrap,"Footer hint has no bounded layout");
            foreach(var title in labels.Where(t=>new[]{"Confirm integration","Confirm target directory"}.Contains(LocalizedPresentation.SourceText(t,TextBlock.TextProperty))))Check(title.ActualWidth<=view.ContentPanel.ActualWidth,"Numbered heading exceeds content width");
        }}
        ((Button)view.FooterPanel.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain(main);
        foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Drain(main);foreach(var key in new[]{WizardScanPresentation.Selected,WizardScanPresentation.Other,WizardScanPresentation.Copies})Check(Tree(view.ContentPanel).OfType<TextBlock>().Any(t=>t.Text==LocalizedPresentation.Language.Lookup(key)),"Scan group not localized: "+key);}
    });
    private static void Reasons()
    {
        Check(WizardScanPresentation.MissingRequirement(false,false,false,false)=="Select the current game EXE.","EXE reason missing");Check(WizardScanPresentation.MissingRequirement(true,false,false,false).Contains("integration"),"Integration reason missing");Check(WizardScanPresentation.MissingRequirement(true,true,false,false)=="Confirm the target directory before continuing.","Target reason missing");Check(WizardScanPresentation.MissingRequirement(true,true,true,true)=="","Ready still blocked");
        var language=new UiLocalization();foreach(var culture in UiLocalization.Cultures.Where(c=>c!="en-US")){language.SetCulture(culture,false);foreach(var key in new[]{WizardScanPresentation.Selected,WizardScanPresentation.Other,WizardScanPresentation.Copies,"Confirm integration","Confirm target directory","Review and confirm the target after changing the EXE, runtime or API."})Check(language.Lookup(key)!=key,"New English fallback: "+culture+" / "+key);Check(language.Translate("Selected EXE: C:\\own\\SP\\bf6.exe").Contains("C:\\own\\SP\\bf6.exe"),"Technical path changed");}
    }
}

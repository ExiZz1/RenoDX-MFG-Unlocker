using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class LocalizationChecks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return ("localization six embedded catalogs have matching keys and placeholders",Catalogs);
        yield return ("localization fallback, templates and technical values are preserved",Fallback);
        yield return ("localization language survives restart and malformed preference falls back",Persistence);
        yield return ("localization updates loaded WPF controls and bindings across all six languages",Controls);
        yield return ("localization quantity formatting preserves opaque technical values",Quantities);
        yield return ("compact preview retains blocked, skipped, unchanged, add and replace distinctions",PreviewActions);
        yield return ("premium scrollbar page surfaces remain transparent when disabled",ScrollSurface);
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Catalogs(){var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,string.Join("\n",l.ValidateCatalogs()));Check(l.KeyCount>150,"Catalog unexpectedly small");foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);Check(!string.IsNullOrEmpty(l.Lookup("Home")),culture);}}
    private static void Fallback()
    {
        var l=new UiLocalization();l.SetCulture("uk-UA",false);
        Check(l.Translate("Target: C:\\fixture\\file.dll")=="Ціль: C:\\fixture\\file.dll","Template/path changed");
        Check(l.Translate("New: 12")=="Нових: 12","Dynamic template not translated");
        Check(l.Translate("New: 12\nTarget: C:\\fixture\\file.dll")=="Нових: 12\nЦіль: C:\\fixture\\file.dll","Composed template swallowed report");
        Check(l.Lookup("Absent fallback sentence")=="Absent fallback sentence","Missing English fallback failed");
        var spanish=(Dictionary<string,string>)l.GetCatalog("es-ES");var original=spanish["Home"];spanish.Remove("Home");
        try{Check(l.Lookup("Home","es-ES")=="Home","Missing localized key did not fall back to English");}finally{spanish["Home"]=original;}
    }
    private static void Persistence()
    {
        var root=Path.Combine(Path.GetTempPath(),"RenoDX-Language-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"language.json");
        var gameConfig=Path.Combine(root,"ReShade.ini");File.WriteAllText(gameConfig,"[Untouched]\nValue=7\n");var original=File.ReadAllBytes(gameConfig);
        foreach(var culture in UiLocalization.Cultures){new UiLocalization(path).SetCulture(culture);Check(new UiLocalization(path).CultureName==culture,"Language preference lost");}
        File.WriteAllText(path,"{broken");Check(new UiLocalization(path).CultureName=="en-US","Invalid settings not handled");Check(original.SequenceEqual(File.ReadAllBytes(gameConfig)),"Locale changed game configuration");
    }
    private static void Quantities()
    {
        var l=new UiLocalization();l.SetCulture("de-DE",false);
        Check(l.Translate("New: 1,234")=="Neu: 1.234","Quantity separators not localized");
        Check(l.Translate("1,234")=="1,234","Opaque version-like text reformatted");
        Check(l.Translate("263.2 MB")=="263,2 MB","Size decimal not localized");
        Check(l.Translate("v2.14.16 · streamline-sdk-v2.14.16.zip · 276,016,170 bytes")=="v2.14.16 · streamline-sdk-v2.14.16.zip · 276.016.170 Bytes","SDK label count/version formatting changed");
        Check(l.Translate("Runtime loaded: NOT VERIFIED.")==l.Lookup("Runtime loaded: NOT VERIFIED."),"Verify completion status not localized");
        Check(l.Translate("Ready to restore 6 file(s). No files have been changed yet.\nManifest HMAC verified.")==l.Translate("Ready to restore 6 file(s). No files have been changed yet.")+"\n"+l.Lookup("Manifest HMAC verified."),"Restore validation composition not localized");
        Check(l.Translate("[ Select Game ]  ›  Scan")=="[ "+l.Lookup("Select Game")+" ]  ›  "+l.Lookup("Scan"),"Wizard navigation not localized");
        Check(l.Translate("Target: D:\\test\\sl.common.dll")=="Ziel: D:\\test\\sl.common.dll","Technical target was changed");
        Check(l.Translate("No component transactions were found for this game. Missing").StartsWith(l.Lookup("No component transactions were found for this game.")),"Concatenated sentence not translated");
    }
    private static void PreviewActions()
    {
        var row=new StreamlineComponentPlanEntry("test.dll","source","target","1.0","abc","1.0","abc","Replace existing",false,true,"message");
        Check(MainWindow.PlanAction(row)=="Unchanged","Matching unselected file not unchanged");
        Check(MainWindow.PlanAction(row with {ExistingSha256="different"})=="Skipped","Unselected mismatch not skipped");
        Check(MainWindow.PlanAction(row with {Action="Blocked"})=="Blocked","Blocked hidden as skipped");
        Check(MainWindow.PlanAction(row with {IsSelected=true})=="Replace","Replacement mapping failed");
        Check(MainWindow.PlanAction(row with {Action="New file",IsSelected=true})=="Add","New mapping failed");
        Check(row.Action=="Replace existing" && !row.IsSelected,"Display mutated plan");
    }
    private static void Controls()
    {
        WpfCheckHost.Run(()=>{
                var l=LocalizedPresentation.Language;
                var text=new TextBlock {Text="Home"};var button=new Button{Content="Cancel"};var panel=new StackPanel();panel.Children.Add(text);panel.Children.Add(button);
                var bindingSource=new TextBlock{Text="New: 3"};var bound=new TextBlock();bound.SetBinding(TextBlock.TextProperty,new Binding("Text"){Source=bindingSource});panel.Children.Add(bound);
                var editable=new TextBox {Text="Home"};panel.Children.Add(editable);
                var readOnlyBound=new TextBox {IsReadOnly=true};readOnlyBound.SetBinding(TextBox.TextProperty,new Binding("Text"){Source=bindingSource,Mode=BindingMode.TwoWay});panel.Children.Add(readOnlyBound);
                var itemText=new FrameworkElementFactory(typeof(TextBlock));itemText.SetBinding(TextBlock.TextProperty,new Binding("Meta"));
                var list=new ItemsControl {ItemTemplate=new DataTemplate {VisualTree=itemText},Height=55,Visibility=Visibility.Collapsed};panel.Children.Add(list);
                var options=new[]{"Off","On"};var combo=new ComboBox {ItemsSource=options,SelectedIndex=0};panel.Children.Add(combo);
                var window=new Window{Content=panel,Width=400,Height=200,ShowInTaskbar=false};window.Show();
                list.Visibility=Visibility.Visible;list.ItemsSource=new[]{new {Meta="Published 2026-10-27"}};
                Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);Check(text.Text==l.Lookup("Home"),"Loaded title did not update: "+culture+" actual="+text.Text+" loaded="+text.IsLoaded+" expected="+l.Lookup("Home"));Check((string)button.Content==l.Lookup("Cancel"),"Button did not update: "+culture);Check(editable.Text=="Home","Editable user text was translated");Check(BindingOperations.IsDataBound(bound,TextBlock.TextProperty),"Binding removed");}
                l.SetCulture("uk-UA",false);bindingSource.Text="New: 8";Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Check(bound.Text=="Нових: 8","Dynamic binding update lost");
                var item=list.ItemContainerGenerator.ContainerFromIndex(0);var rendered=FindText(item);
                Check(rendered?.Text==l.Translate("Published 2026-10-27"),"Generated release metadata not localized: "+rendered?.Text);
                var latePanel=new StackPanel();panel.Children.Add(latePanel);latePanel.Children.Add(new TextBlock {Text="Read-only inventory. Backup copies are labelled; files on disk are not proof of loaded runtime or compatibility."});
                LocalizedPresentation.RefreshGeneratedItems(latePanel);Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                Check(FindText(latePanel)!.Text==l.Lookup("Read-only inventory. Backup copies are labelled; files on disk are not proof of loaded runtime or compatibility."),"Late wizard panel not localized");
                combo.IsDropDownOpen=true;Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
                Check(FindText(popup.Child)!.Text==l.Lookup("Off"),"Late dropdown options not localized");Check(options[0]=="Off" && (string)combo.SelectedItem=="Off","Dropdown localization mutated selection data");combo.IsDropDownOpen=false;
                readOnlyBound.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Check(bindingSource.Text=="New: 8","Display localization wrote back through a two-way binding");
                window.Close();
        });
    }
    private static void ScrollSurface()
    {
        WpfCheckHost.Run(()=>{
                var app=Application.Current;
                var scroll=new System.Windows.Controls.Primitives.ScrollBar {Style=(Style)app.FindResource("PremiumScrollBar"),Maximum=100,Value=20,ViewportSize=30,Height=200};
                var window=new Window {Content=scroll,Width=250,Height=250,ShowInTaskbar=false};window.Show();scroll.ApplyTemplate();
                foreach(var orientation in new[]{Orientation.Vertical,Orientation.Horizontal})foreach(var enabled in new[]{true,false}){
                    scroll.Orientation=orientation;scroll.IsEnabled=enabled;window.UpdateLayout();
                    var track=(System.Windows.Controls.Primitives.Track)scroll.Template.FindName("PART_Track",scroll);
                    foreach(var page in new[]{track.DecreaseRepeatButton,track.IncreaseRepeatButton}){
                        Check(page.Template==(ControlTemplate)app.FindResource("ScrollPageSurface"),"Native page-button chrome inherited");page.ApplyTemplate();
                        var surface=System.Windows.Media.VisualTreeHelper.GetChild(page,0) as Border;
                        Check(surface?.Background is System.Windows.Media.SolidColorBrush brush && brush.Color.A==0,"Page surface became opaque: "+orientation+" enabled="+enabled);
                    }
                }
                var trace=new System.IO.StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(trace);System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                var view=new SetupWizardView();window.Content=view;window.Width=1480;window.Height=900;
                var row=new StreamlineComponentPlanEntry("fixture.dll",@"C:\isolated\source.dll",@"C:\isolated\target.dll","2.0",new string('a',64),"1.0",new string('b',64),"Replace existing",true,false,"Original file will be backed up in this transaction.");
                var runtimeCards=(Grid)view.RuntimeCards(false,_=>{});view.ContentPanel.Children.Add(runtimeCards);window.UpdateLayout();
                Check(Math.Abs(((Button)runtimeCards.Children[0]).ActualHeight-((Button)runtimeCards.Children[1]).ActualHeight)<1,"Provider card proportions differ");
                view.ContentPanel.Children.Clear();view.SetReviewLayout(true);
                var preview=view.Review(Enumerable.Range(0,30).Select(i=>row with {FileName=$"fixture-{i}.dll"}).ToArray(),"Isolated fixture","NVIDIA Dynamic MFG","2.14.16","310.9.16",_=>"Origin: Not verified",_=>"Not checked");
                view.ContentPanel.Children.Add(preview);view.Navigate(4,false,_=>{});
                foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);LocalizedPresentation.RefreshGeneratedItems(view);window.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Check(view.PageTitle.Text==LocalizedPresentation.Language.Lookup("Review Changes"),"Wizard stage not localized: "+culture);Check(view.SidebarPanel.Children.Count>12,"Details missing fields");Check(view.FooterPanel.ActualWidth<=window.ActualWidth,"Footer outside viewport");}
                window.Width=1040;window.Height=650;window.UpdateLayout();Check(view.ContentScroll.ViewportWidth>300&&view.SidebarPanel.ActualWidth>=200,"Narrow wizard loses useful columns");
                Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Check(view.ContentScroll.VerticalScrollBarVisibility==ScrollBarVisibility.Disabled&&preview.ActualHeight<=view.ContentScroll.ActualHeight,"Review creates a second scrolling viewport");
                var files=FindList(preview)!;Check(files.ActualHeight>40&&files.Items.Count==30,"Review table lost its viewport or rows");files.SelectedIndex=29;files.ScrollIntoView(files.SelectedItem);window.UpdateLayout();Check(view.SidebarPanel.Children.OfType<TextBlock>().Any(t=>t.Text=="fixture-29.dll"),"Last row does not update Details");
                FindFilter(view,"Add")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                Check(view.SidebarPanel.Children.OfType<TextBlock>().First().Text==LocalizedPresentation.Language.Lookup("No matching files"),"Late empty-filter message not localized");
                FindFilter(view,"All Files")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Check(row.IsSelected&&view.SidebarPanel.Children.Count>12,"Filtering mutated plan or lost Details");
                System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);listener.Flush();Check(!trace.ToString().Contains("Error:"),"WPF binding error: "+trace);
                window.Close();
        });
    }
    private static TextBlock? FindText(DependencyObject root){if(root is TextBlock text)return text;for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var found=FindText(System.Windows.Media.VisualTreeHelper.GetChild(root,i));if(found is not null)return found;}return null;}
    private static Button? FindFilter(DependencyObject root,string tag){if(root is Button {Tag:string value} button&&value==tag)return button;for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var found=FindFilter(System.Windows.Media.VisualTreeHelper.GetChild(root,i),tag);if(found is not null)return found;}return null;}
    private static ListBox? FindList(DependencyObject root){if(root is ListBox list)return list;for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var found=FindList(System.Windows.Media.VisualTreeHelper.GetChild(root,i));if(found is not null)return found;}return null;}
}

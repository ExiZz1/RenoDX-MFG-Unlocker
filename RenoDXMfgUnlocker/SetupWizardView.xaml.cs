using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;

namespace RenoDXMfgUnlocker;

/// <summary>Presentation only. The existing wizard retains exclusive ownership of preparation and writes.</summary>
public partial class SetupWizardView : UserControl
{
    private static readonly string[] Steps=["Select Game","Scan Game","Choose Runtime","Prepare Components","Review Changes","Install & Verify"];
    public SetupWizardView(){InitializeComponent();VersionText.Text="v"+typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;}
    internal void UseEmbeddedLayout(){var grid=(Grid)Content;grid.RowDefinitions[0].Height=new GridLength(0);grid.Children[0].Visibility=Visibility.Collapsed;}
    private void HeaderDrag(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)Window.GetWindow(this)?.DragMove();}
    internal static bool CanVisit(int current,int requested,bool busy)=>!busy && requested>=0 && requested<=current && requested<Steps.Length;
    internal static bool CanContinue(int step,bool busy,bool executable,bool targetAndInputs,bool packageChoice,bool approval)=>!busy && step switch {0=>executable,1=>true,2=>targetAndInputs,3=>targetAndInputs&&packageChoice,4=>targetAndInputs&&approval,_=>false};
    internal static IEnumerable<StreamlineComponentPlanEntry> Filter(IEnumerable<StreamlineComponentPlanEntry> rows,string filter)=>rows.Where(r=>filter=="All Files" || (filter=="Blocked / Skipped"?MainWindow.PlanAction(r) is "Blocked" or "Skipped" : MainWindow.PlanAction(r)==filter));
    internal static string DisplayVersion(string value,string fileName)=>!string.IsNullOrWhiteSpace(value)?TechnicalVersionDisplay.Format(value):Path.GetExtension(fileName).Equals(".dll",StringComparison.OrdinalIgnoreCase)?"Unknown version":"Not applicable";
    internal void SetReviewLayout(bool review)
    {
        ContentScroll.VerticalScrollBarVisibility=review?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
        SidebarBorder.VerticalAlignment=review?VerticalAlignment.Stretch:VerticalAlignment.Top;
        ReviewActionsPanel.Visibility=review?Visibility.Visible:Visibility.Collapsed;
    }
    internal void Navigate(int step,bool busy,Action<int> visit)
    {
        NavigationPanel.Children.Clear();PageTitle.Text=Steps[step];
        for(var i=0;i<Steps.Length;i++){
            var target=i;var row=new Grid {MinHeight=76};row.ColumnDefinitions.Add(new(){Width=new GridLength(48)});row.ColumnDefinitions.Add(new());
            if(i<Steps.Length-1)row.Children.Add(new Border {Width=2,Background=Brush(i<step?"#725096":"#35303F"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(17,46,0,-14)});
            var number=Label(i<step?"✓":(i+1).ToString("00"),14);number.Margin=new Thickness(0);number.FontWeight=FontWeights.SemiBold;number.HorizontalAlignment=HorizontalAlignment.Center;number.VerticalAlignment=VerticalAlignment.Center;
            var circle=new Border {Width=36,Height=36,CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(i==step?2:1),BorderBrush=Brush(i==step?"#B28AEC":"#4A405D"),Background=i==step?new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#A66CF3"),(Color)ColorConverter.ConvertFromString("#6438AF"),90):Brush(i<step?"#292135":"#1A1A24"),Child=number,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,12,0,0)};
            if(i==step)circle.Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=(Color)ColorConverter.ConvertFromString("#9258E8"),BlurRadius=16,ShadowDepth=0,Opacity=.45};row.Children.Add(circle);
            var caption=Label(Steps[i],13);caption.FontWeight=i==step?FontWeights.SemiBold:FontWeights.Normal;caption.Margin=new Thickness(0,18,2,16);caption.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(caption,1);row.Children.Add(caption);
            var b=new Button {Style=(Style)FindResource("RailButton"),Content=row,IsEnabled=CanVisit(step,i,busy),Background=i==step?new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#362347"),(Color)ColorConverter.ConvertFromString("#17121F"),0):Brush("#00000000")};
            b.Click+=(_,_)=>visit(target);NavigationPanel.Children.Add(b);
        }
        LocalizedPresentation.RefreshGeneratedItems(NavigationPanel);
    }
    internal static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    internal static TextBlock Label(string text,int size=13)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#EDE9F5"),Margin=new Thickness(0,3,0,6)};
    internal static Border Card(UIElement child,double padding=16)=>new(){Child=child,Padding=new Thickness(padding),Background=Brush("#15151F"),BorderBrush=Brush("#302C3C"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Margin=new Thickness(0,0,0,14)};
    internal void Summary(IEnumerable<StreamlineComponentPlanEntry>? rows,string? target,string? game)
    {
        SidebarPanel.Children.Clear();SidebarPanel.Children.Add(Label("Change summary",18));SidebarPanel.Children.Add(Label(game??"Not selected",12));
        var all=rows?.ToArray();
        if(all is null){SidebarPanel.Children.Add(Card(Label("Preview not prepared",13),12));LocalizedPresentation.RefreshGeneratedItems(SidebarPanel);return;}
        foreach(var action in new[]{"Replace","Add","Blocked / Skipped"}){
            var count=all is null?"—":Filter(all,action).Count().ToString();var panel=new Grid();panel.ColumnDefinitions.Add(new(){Width=new GridLength(44)});panel.ColumnDefinitions.Add(new());var value=Label(count,23);value.Margin=new Thickness(0);value.FontWeight=FontWeights.SemiBold;value.Foreground=Brush("#BE9AF5");value.VerticalAlignment=VerticalAlignment.Center;panel.Children.Add(value);var caption=Label(action,12);caption.Margin=new Thickness(4,0,0,0);caption.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(caption,1);panel.Children.Add(caption);var card=Card(panel,12);card.Margin=new Thickness(0,4,0,6);SidebarPanel.Children.Add(card);
        }
        SidebarPanel.Children.Add(Label(all is null?"Preview not prepared":"Backup planned",14));
        SidebarPanel.Children.Add(Label("Original bytes are saved before replacement. Restore checks for external changes.",12));
        SidebarPanel.Children.Add(Label("Integrity and compatibility are separate checks. Files on disk do not prove active MFG.",12));
        if(target is not null){SidebarPanel.Children.Add(Label("Target"));SidebarPanel.Children.Add(CopyValue(target));}
        LocalizedPresentation.RefreshGeneratedItems(SidebarPanel);
    }
    internal FrameworkElement RuntimeCards(bool intel,Action<bool> choose)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
        for(var i=0;i<2;i++){
            var isIntel=i==1;var selected=intel==isIntel;var body=new Grid {MinHeight=166};body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());
            var mark=Label(isIntel?"intel":"NVIDIA",23);mark.FontWeight=FontWeights.Bold;mark.Foreground=Brush(isIntel?"#69BFFF":"#A2CE5A");mark.Margin=new Thickness(0,0,32,16);body.Children.Add(mark);
            var radio=new Border {Width=18,Height=18,CornerRadius=new CornerRadius(9),BorderBrush=Brush(selected?"#B58DEC":"#666071"),BorderThickness=new Thickness(2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,5,0,0)};
            if(selected)radio.Child=new Border {Background=Brush("#D5BCFF"),CornerRadius=new CornerRadius(5),Margin=new Thickness(3)};body.Children.Add(radio);
            var title=Label(isIntel?"Intel XeMFG":"NVIDIA Dynamic MFG",16);title.FontWeight=FontWeights.SemiBold;Grid.SetRow(title,1);body.Children.Add(title);
            var text=new StackPanel();var subtitle=Label(isIntel?"XeSS Frame Generation + Xe Low Latency":"Streamline + DLSS Frame Generation",12);subtitle.Foreground=Brush("#BFB4CF");text.Children.Add(subtitle);var description=Label(isIntel?"Pinned Intel runtime. Requires a supported game input and addon.":"Maintains an existing Streamline integration with a documented DLSS-G pair.",12);description.Foreground=Brush("#9891A6");description.Margin=new Thickness(0,5,0,0);text.Children.Add(description);Grid.SetRow(text,2);body.Children.Add(text);
            var button=new Button {Style=(Style)FindResource("WizardCardButton"),Content=body,Padding=new Thickness(20,18,20,18),Margin=new Thickness(i==0?0:7,0,i==0?7:0,14),Background=selected?new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#2C213E"),(Color)ColorConverter.ConvertFromString("#191622"),45):Brush("#15151D"),BorderBrush=Brush(selected?"#A271F2":"#37303F"),BorderThickness=new Thickness(selected?2:1)};
            button.Click+=(_,_)=>choose(isIntel);Grid.SetColumn(button,i);grid.Children.Add(button);
        }
        return grid;
    }
    internal FrameworkElement Review(IReadOnlyList<StreamlineComponentPlanEntry> rows,string game,string runtime,string sdk,string provider,Func<StreamlineComponentPlanEntry,string> provenance,Func<StreamlineComponentPlanEntry,string> signature)
    {
        // The table owns scrolling. Its viewport follows the available body height, not the window size.
        var panel=new Grid();for(var i=0;i<4;i++)panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new());
        panel.SetBinding(HeightProperty,new Binding(nameof(ActualHeight)){Source=ContentScroll,Converter=new ReviewHeightConverter()});
        var cards=new UniformGrid {Columns=3,Margin=new Thickness(0,0,0,8)};
        foreach(var pair in new[]{("Target Game",game),("Runtime Provider",runtime),("Backup Status","Backup planned")}){
            var body=new StackPanel();var heading=Label(pair.Item1,10);heading.Foreground=Brush("#BD9BEF");heading.Margin=new Thickness(0,0,0,5);body.Children.Add(heading);var value=Label(pair.Item2,12);value.Margin=new Thickness(0);value.FontWeight=FontWeights.SemiBold;body.Children.Add(value);var card=Card(body,12);card.Margin=new Thickness(0,0,cards.Children.Count<2?8:0,0);cards.Children.Add(card);
        }
        panel.Children.Add(cards);var runtimeLine=Label(TechnicalVersionDisplay.Format(sdk)+" · "+TechnicalVersionDisplay.Format(provider),11);runtimeLine.Foreground=Brush("#AAA0BA");runtimeLine.Margin=new Thickness(0,0,0,8);Grid.SetRow(runtimeLine,1);panel.Children.Add(runtimeLine);
        var filters=new WrapPanel {Margin=new Thickness(0,0,0,5)};Grid.SetRow(filters,2);panel.Children.Add(filters);
        var list=new ListBox {Background=Brush("#111218"),BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        // Item selection is keyboard-accessible and affects only the Details panel, never plan selection.
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);list.SetResourceReference(StyleProperty,"WizardFilesList");
        Grid Columns(){var grid=new Grid();foreach(var weight in new[]{2.05,1.05,1.05,.95,.95})grid.ColumnDefinitions.Add(new(){Width=new GridLength(weight,GridUnitType.Star)});return grid;}
        var table=Columns();var headings=new[]{"File / Component","Current Version","New Version","Action","Status"};for(var i=0;i<5;i++){var text=Label(headings[i],10);text.Foreground=Brush("#A99BBF");text.Margin=new Thickness(0,0,6,0);text.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(text,i);table.Children.Add(text);}
        var header=Card(table,12);header.Margin=new Thickness(0,0,10,5);Grid.SetRow(header,3);panel.Children.Add(header);
        void Select(StreamlineComponentPlanEntry row){
            SidebarPanel.Children.Clear();SidebarPanel.Children.Add(Label("Selected file details",17));var file=Label(row.FileName,16);file.FontWeight=FontWeights.SemiBold;file.Margin=new Thickness(0,8,0,5);SidebarPanel.Children.Add(file);
            var component=Label(row.FileName.StartsWith("sl.",StringComparison.OrdinalIgnoreCase)?"Streamline plugin":row.FileName.Equals("nvngx_dlssg.dll",StringComparison.OrdinalIgnoreCase)?"DLSS Frame Generation NGX runtime":"Runtime / license",11);component.Foreground=Brush("#9F95AE");SidebarPanel.Children.Add(component);
            var versions=new UniformGrid {Columns=2,Margin=new Thickness(0,8,0,12)};
            foreach(var pair in new[]{("Current Version",DisplayVersion(row.ExistingVersion,row.FileName)),("New Version",DisplayVersion(row.SourceVersion,row.FileName))}){
                var body=new StackPanel();var label=Label(pair.Item1,10);label.Foreground=Brush("#A79AB9");label.Margin=new Thickness(0,0,0,6);body.Children.Add(label);body.Children.Add(Badge(pair.Item2,pair.Item1=="New Version"));var card=Card(body,10);card.Margin=new Thickness(0,0,versions.Children.Count==0?7:0,0);versions.Children.Add(card);
            }
            SidebarPanel.Children.Add(versions);
            void Section(params (string Key,string Value)[] fields){
                SidebarPanel.Children.Add(new Border {Height=1,Background=Brush("#2E2938"),Margin=new Thickness(0,2,0,12)});
                foreach(var pair in fields){var label=Label(pair.Key,10);label.Foreground=Brush("#A79AB9");label.Margin=new Thickness(0,0,0,6);SidebarPanel.Children.Add(label);var value=pair.Key is "Source" or "Target" or "Source SHA-256" or "Existing SHA-256"?CopyValue(pair.Value):Label(pair.Value,12);SidebarPanel.Children.Add(value);}
            }
            Section(("Source",row.SourcePath),("Target",row.TargetPath));
            Section(("Digital signature",signature(row)),("Integrity",row.Action=="Blocked"?"Verification Failed":provenance(row)=="Origin: Not verified"?"Not checked yet":"Verified SHA-256 (preview snapshot); rechecked before writing"));
            Section(("Source SHA-256",row.Sha256),("Existing SHA-256",row.ExistingSha256??"Not applicable"));
            Section(("Action",MainWindow.PlanAction(row)),("Reason",row.Message),("Backup Status",!row.IsSelected?"No write planned":row.ExistingSha256 is null?"No existing file":"Backup planned"));
            SidebarPanel.Children.Add(Label(provenance(row),10));LocalizedPresentation.RefreshGeneratedItems(SidebarPanel);
        }
        void Populate(string filter){list.Items.Clear();foreach(var row in Filter(rows,filter)){
            var grid=Columns();grid.MinHeight=30;
            var cells=new[]{row.FileName,DisplayVersion(row.ExistingVersion,row.FileName),DisplayVersion(row.SourceVersion,row.FileName),MainWindow.PlanAction(row),row.Action=="Blocked"?"Blocked":row.IsSelected?"Planned":"Not selected"};for(var i=0;i<cells.Length;i++){
                FrameworkElement cell;if(i is 1 or 2)cell=Badge(cells[i],i==2);else {var text=Label(cells[i],11);text.Margin=new Thickness(0);text.FontWeight=i==0?FontWeights.SemiBold:FontWeights.Normal;text.Foreground=Brush(i==3?"#C7A5F2":i==4?"#A69DB3":"#EDE9F5");cell=text;}
                if(i==0){var identity=new StackPanel();identity.Children.Add(cell);var location=Label(Path.GetDirectoryName(row.TargetPath)??row.TargetPath,10);location.Margin=new Thickness(0,3,0,0);location.Foreground=Brush("#9C93AB");location.Text=ShortTarget(row.TargetPath);location.ToolTip=row.TargetPath;identity.Children.Add(location);cell=identity;}
                cell.Margin=new Thickness(0,0,7,0);cell.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(cell,i);grid.Children.Add(cell);
            }
            list.Items.Add(new ListBoxItem{Content=grid,Tag=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,4)});
        }if(list.Items.Count>0)list.SelectedIndex=0;else {SidebarPanel.Children.Clear();SidebarPanel.Children.Add(Label("No matching files"));}LocalizedPresentation.RefreshGeneratedItems(panel);LocalizedPresentation.RefreshGeneratedItems(SidebarPanel);}
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is ListBoxItem {Tag:StreamlineComponentPlanEntry row})Select(row);};
        foreach(var filter in new[]{"All Files","Replace","Add","Unchanged","Blocked / Skipped"}){
            var b=new Button {Content=filter+" · "+Filter(rows,filter).Count(),Tag=filter,Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,5,5),FontSize=11,Background=Brush(filter=="All Files"?"#65419B":"#20212B")};b.Click+=(_,_)=>{foreach(var other in filters.Children.OfType<Button>())other.Background=Brush(other==b?"#65419B":"#20212B");Populate(filter);};filters.Children.Add(b);
        }
        Grid.SetRow(list,4);panel.Children.Add(list);Populate("All Files");return panel;
    }
    internal static string ShortTarget(string path){var directory=Path.GetDirectoryName(path);if(directory is null)return path;var parts=directory.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries);return string.Join(Path.DirectorySeparatorChar,parts.TakeLast(3));}
    private static Border Badge(string value,bool proposed)
    {
        var text=Label(value,11);text.Margin=new Thickness(0);text.FontWeight=FontWeights.SemiBold;text.Foreground=Brush(proposed?"#D8BDFE":"#C0BACD");
        return new Border {Child=text,Background=Brush(proposed?"#332249":"#252530"),CornerRadius=new CornerRadius(7),Padding=new Thickness(8,5,8,5),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
    }
    private sealed class ReviewHeightConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>Math.Max(0,(double)value-2);
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private static UIElement CopyValue(string value)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var text=new TextBox {Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brush("#00000000"),Padding=new Thickness(8),FontSize=11,Margin=new Thickness(0,0,4,0),VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};grid.Children.Add(text);
        var copy=new Button{Content="⧉",ToolTip="Copy",Padding=new Thickness(7,5,7,5),Margin=new Thickness(0,4,4,4),VerticalAlignment=VerticalAlignment.Top};copy.Click+=(_,_)=>{try{Clipboard.SetText(value);}catch(System.Runtime.InteropServices.ExternalException){copy.ToolTip=LocalizedPresentation.Language.Translate("Clipboard unavailable");}};Grid.SetColumn(copy,1);grid.Children.Add(copy);return new Border {Child=grid,Background=Brush("#101117"),BorderBrush=Brush("#2E2938"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,12)};
    }
}

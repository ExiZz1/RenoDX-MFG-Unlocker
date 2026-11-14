using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RenoDXMfgUnlocker;

internal sealed record SettingHelpSection(string Heading,string Content,bool IsTechnical=false):System.ComponentModel.INotifyPropertyChanged
{
    public Func<UiLocalization,string>? Formatter {get;init;}
    public string DisplayHeading=>HeadingFor(LocalizedPresentation.Language);
    public string DisplayContent=>ContentFor(LocalizedPresentation.Language);
    internal string HeadingFor(UiLocalization l)=>l.Lookup(Heading);
    internal string ContentFor(UiLocalization l)=>Formatter?.Invoke(l)??(IsTechnical?Content:l.Translate(Content));
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage(){PropertyChanged?.Invoke(this,new(nameof(DisplayHeading)));PropertyChanged?.Invoke(this,new(nameof(DisplayContent)));}
}
public partial class MainWindow
{
    private readonly Dictionary<string,bool> settingsExpansion=new(StringComparer.OrdinalIgnoreCase);
    private void RememberSettingsExpansion()
    {
        if(mfgSettingsExePath is null)return;
        foreach(var row in mfgSettingRows.Concat(xeMfgSettingRows))settingsExpansion[mfgSettingsExePath+"|"+row.Definition.Key]=row.IsExpanded;
    }
    internal Window CreateInfoDialog(string title,UIElement body,double width=760,double maximumHeight=680,Window? owner=null)
    {
        var work=DesktopWindowLayout.WorkArea(owner??this);
        var window=new Window {Title=title,Owner=owner??this,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Foreground=(Brush)FindResource("Text"),ResizeMode=ResizeMode.NoResize,SizeToContent=SizeToContent.Height,Width=Math.Max(1,Math.Min(width,work.Width-32)),MaxHeight=Math.Max(1,Math.Min(maximumHeight,work.Height-32)),ShowInTaskbar=false,Icon=Icon};
        var layout=new Grid();layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,16)};
        header.Children.Add(new Image {Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/RenoDX.png")),Width=30,Height=30,Margin=new Thickness(0,0,12,0)});
        header.Children.Add(new TextBlock {Text=title,FontSize=20,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=Math.Max(80,window.Width-112)});
        header.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed)window.DragMove();};layout.Children.Add(header);
        var scroll=new ScrollViewer {Content=body,MaxHeight=Math.Max(60,window.MaxHeight-150),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};scroll.Resources.Add(typeof(ScrollBar),FindResource("PremiumScrollBar"));Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var close=new Button {Content="Close",MinWidth=100,Padding=new Thickness(18,9,18,9),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0),Background=SetupWizardView.Brush("#493064"),BorderBrush=SetupWizardView.Brush("#8963B8")};close.Click+=(_,_)=>window.Close();Grid.SetRow(close,2);layout.Children.Add(close);
        window.Content=new Border {Child=layout,Background=SetupWizardView.Brush("#15151E"),BorderBrush=SetupWizardView.Brush("#514065"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=new Thickness(22)};
        window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;window.Close();}};window.Loaded+=(_,_)=>close.Focus();return window;
    }
    internal Window CreateLanguageDialog()
    {
        var body=new StackPanel();body.Children.Add(ToolText("Language",14));var choices=new System.Windows.Controls.Primitives.UniformGrid {Columns=2,Margin=new Thickness(0,8,0,8)};
        foreach(var (culture,index) in UiLocalization.Cultures.Select((c,i)=>(c,i))){
            var choice=new Button {Content=UiLocalization.LanguageNames[index],Tag=culture,Padding=new Thickness(14,14,14,14),Margin=new Thickness(0,0,index%2==0?8:0,8),MinHeight=46};LocalizedPresentation.SetPreserveOriginal(choice,true);StyleLanguageChoice(choice,culture==LocalizedPresentation.Language.CultureName);
            choice.Click+=(_,_)=>{if(LanguageChangeBlocked){ShowThemeDialog("Application settings","Finish the active operation before changing language.");return;}try{LocalizedPresentation.Language.SetCulture(culture);foreach(var item in choices.Children.OfType<Button>())StyleLanguageChoice(item,(string)item.Tag==culture);}catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException){ShowThemeDialog("Application settings",ex.Message,true);}};choices.Children.Add(choice);
        }
        body.Children.Add(choices);body.Children.Add(ToolText("Language changes apply immediately and do not change game settings."));
        body.Children.Add(ToolText("Official addon source",14));
        var upstreamAutomatic=new CheckBox{Content="Notify about official addon releases (at most once per 24 hours)",IsChecked=upstreamReleaseNotice.Read().Enabled,Margin=new Thickness(0,6,0,10)};
        upstreamAutomatic.Click+=(_,_)=>{try{upstreamReleaseNotice.SetEnabled(upstreamAutomatic.IsChecked==true);if(upstreamAutomatic.IsChecked==true)_=CheckUpstreamReleaseNoticeAsync();else{latestUpstreamNotice=null;RenderUpstreamReleaseNotice();}}catch(Exception ex)when(ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException){upstreamAutomatic.IsChecked=upstreamReleaseNotice.Read().Enabled;ShowThemeDialog("Official addon source",ex.Message,true);}};
        body.Children.Add(upstreamAutomatic);body.Children.Add(ToolText("Metadata only. Source analysis, approval and INI changes always require your action."));
        body.Children.Add(ToolText("Application updates",14));
        var automatic=new CheckBox{Content="Check for stable updates at launch",IsChecked=applicationPreferences.CheckUpdatesAtLaunch,Margin=new Thickness(0,6,0,10)};
        automatic.Click+=(_,_)=>{try{applicationPreferences.SetUpdateCheck(automatic.IsChecked==true);}catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException){automatic.IsChecked=applicationPreferences.CheckUpdatesAtLaunch;ShowThemeDialog("Application updates",ex.Message,true);}};
        body.Children.Add(automatic);var update=new Button{Content="Check for Updates",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,8,12,8)};
        var dialog=CreateInfoDialog("Application settings",body,600,680);update.Click+=(_,_)=>ShowApplicationUpdates(dialog);body.Children.Add(update);return dialog;
    }
    internal FrameworkElement ComponentDetailsContent(string text)
    {
        var body=new StackPanel();
        foreach(var line in text.Replace("\r\n","\n").Split('\n')){
            if(string.IsNullOrWhiteSpace(line))continue;
            var split=line.IndexOf(": ",StringComparison.Ordinal);
            if(split>0&&split<45){var section=new StackPanel();var label=ToolText(line[..split],10);label.Foreground=SetupWizardView.Brush("#B79DDC");section.Children.Add(label);section.Children.Add(ToolText(line[(split+2)..],13));var card=SetupWizardView.Card(section,12);card.Margin=new Thickness(0,0,0,8);body.Children.Add(card);}
            else body.Children.Add(ToolText(line,13));
        }return body;
    }
    internal static IReadOnlyList<SettingHelpSection> HelpSections(MfgUnlockSettingDefinition setting,string file,string section)=>setting.CatalogEntry is {} e?[
        new("Metadata trust",setting.MetadataTrust),
        new("Technical Key",file+" В· ["+section+"] "+setting.Key,true),
        new("Upstream description (original language)",e.Description,true),
        new("Catalog constraints",e.Type+" В· "+e.Minimum+" вЂ¦ "+e.Maximum+" В· в‰Ґ"+e.MinimumAddon+" В· "+string.Join(" / ",e.Values??[]),true){Formatter=l=>MfgSettingsDisplay.Constraints(e,l)},
        new("Default",e.Default,true),new("Dependencies",string.Join("; ",(e.Requires??[]).Select(d=>d.Key+"="+d.Value)),true),
        new("Evidence",e.Evidence,true),new("Restart Required","Changes apply after restarting the game.")
    ]:[
        new("Metadata trust",setting.MetadataTrust),
        new("Display Name",setting.Label),new("Technical Key",setting.Key,true),new("Config File / Section",file+" В· ["+section+"]",true),
        new("What it does",setting.Description),
        new("Effect on Game",section=="XeFG"?"These options affect the XeMFG path only. They do not install dependencies or enable the backend.":"These saved preferences are read by the addon. They do not prove runtime loading or active frame generation."),
        new("When to Change",GuideAdvice(setting.Key)),new("Default",setting.DefaultValue,true),new("Recommended","No game-specific recommendation is established."),
        new("Possible Side Effects",section=="XeFG"?"Experimental behavior can affect pacing, latency or image quality. Restore the original setting if problems occur.":"Changing runtime policy or experimental quality paths can alter behavior. Use controlled troubleshooting and retain a backup."),
        new("Supported format",section=="XeFG"?(setting.Key=="XeFGStock2xModel"?"Only the integer 1 enables the stock 2x model; other integer values disable it. This utility writes 0 or 1.":setting.Key=="UIStabilization"?"UIStabilization accepts 0/1, true/false and on/off. This utility writes 0 or 1. Live controls remain in the original addon panel; external changes require a restart.":"Upstream reads an integer: 0 disables it, nonzero enables it. This utility writes 0 or 1 and preserves untouched saved values."):"Saved values are not live runtime state. Close the game before editing. No performance gain is guaranteed."),
        new("When changes apply","Changes apply after restarting the game.")
    ];
    private void SettingsDetails_Expanded(object sender,RoutedEventArgs e){if(sender is FrameworkElement element)LocalizedPresentation.RefreshGeneratedItems(element);}
    private void MaximizeRestore_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void PopulateAboutFeatures()
    {
        foreach(var pair in AboutFeatureCatalog.Features){
            var body=new StackPanel();var heading=ToolText(pair.Item1,15);heading.FontWeight=FontWeights.SemiBold;heading.Foreground=SetupWizardView.Brush("#C8ADF1");body.Children.Add(heading);body.Children.Add(ToolText(pair.Item2));var card=SetupWizardView.Card(body,16);card.Margin=new Thickness(0,0,10,10);AboutFeaturesPanel.Children.Add(card);
        }
    }
}

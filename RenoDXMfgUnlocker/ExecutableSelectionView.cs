using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace RenoDXMfgUnlocker;
internal sealed record ExecutableChoice(string FullPath)
{
    public string FileName=>Path.GetFileName(FullPath);
    public string DirectoryPath=>Path.GetDirectoryName(FullPath)??FullPath;
    public override string ToString()=>FullPath;
}

/// <summary>Display and explicit choice only; the existing discovery and SetExe validation remain authoritative.</summary>
internal sealed class ExecutableSelectionView:Grid
{
    internal ListBox Candidates {get;}=new();
    internal Button Choose {get;}=new(){Content="Use selected EXE",MinWidth=130,Padding=new Thickness(16,9,16,9),IsEnabled=false};
    internal Button Cancel {get;}=new(){Content="Cancel",MinWidth=90,Padding=new Thickness(16,9,16,9),Margin=new Thickness(0,0,8,0)};
    internal CheckBox ShowAll {get;}=new(){Content="Show all executables (including tools and copies)",Margin=new Thickness(0,12,0,0)};
    internal string? SelectedPath=>(Candidates.SelectedItem as ExecutableChoice)?.FullPath;
    internal event Action<string>? Confirmed;
    internal event Action? Cancelled;
    internal static Size DialogSize(Size workArea)=>new(Math.Max(1,Math.Min(740,workArea.Width-32)),Math.Max(1,Math.Min(520,workArea.Height-32)));
    internal ExecutableSelectionView(string gameName,IReadOnlyList<string> candidates,Func<IReadOnlyList<string>> showAll)
    {
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})RowDefinitions.Add(new(){Height=height});
        Children.Add(SetupWizardView.Label("Choose the main game executable",19));
        var subtitle=new StackPanel{Margin=new Thickness(0,7,0,12)};var game=SetupWizardView.Label(gameName,12);LocalizedPresentation.SetPreserveOriginal(game,true);subtitle.Children.Add(game);subtitle.Children.Add(SetupWizardView.Label("Choose a candidate explicitly. No Steam launch metadata recommendation is available.",11));SetRow(subtitle,1);Children.Add(subtitle);
        Candidates.Style=(Style)Application.Current.FindResource(typeof(ListBox));
        Candidates.Background=SetupWizardView.Brush("#0F1116");Candidates.BorderBrush=SetupWizardView.Brush("#343642");Candidates.BorderThickness=new Thickness(1);Candidates.Padding=new Thickness(4);
        ScrollViewer.SetHorizontalScrollBarVisibility(Candidates,ScrollBarVisibility.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(Candidates,ScrollBarVisibility.Auto);
        Candidates.Resources.Add(typeof(ScrollBar),Application.Current.FindResource("PremiumScrollBar"));
        var style=new Style(typeof(ListBoxItem),Candidates.ItemContainerStyle);style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(LocalizedPresentation.PreserveOriginalProperty,true));style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(nameof(ExecutableChoice.FullPath))));style.Setters.Add(new Setter(AutomationProperties.HelpTextProperty,new Binding(nameof(ExecutableChoice.FullPath))));Candidates.ItemContainerStyle=style;
        var body=new FrameworkElementFactory(typeof(StackPanel));body.SetValue(LocalizedPresentation.PreserveOriginalProperty,true);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding(nameof(ExecutableChoice.FileName)));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.FontSizeProperty,14d);name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);body.AppendChild(name);
        var path=new FrameworkElementFactory(typeof(TextBlock));path.SetBinding(TextBlock.TextProperty,new Binding(nameof(ExecutableChoice.DirectoryPath)));path.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);path.SetValue(TextBlock.FontSizeProperty,11d);path.SetValue(TextBlock.ForegroundProperty,SetupWizardView.Brush("#AAA4B9"));path.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,0));body.AppendChild(path);
        Candidates.ItemTemplate=new DataTemplate{VisualTree=body};SetCandidates(candidates);SetRow(Candidates,2);Children.Add(Candidates);
        SetRow(ShowAll,3);Children.Add(ShowAll);ShowAll.Checked+=(_,_)=>SetCandidates(showAll());ShowAll.Unchecked+=(_,_)=>SetCandidates(candidates);
        Candidates.SelectionChanged+=(_,_)=>Choose.IsEnabled=SelectedPath is not null;
        Choose.Click+=(_,_)=>{if(Choose.IsEnabled&&SelectedPath is {} selected)Confirmed?.Invoke(selected);};Cancel.Click+=(_,_)=>Cancelled?.Invoke();
        Candidates.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&Choose.IsEnabled){e.Handled=true;Choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
        Candidates.MouseDoubleClick+=(_,e)=>{if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(Candidates,source) is ListBoxItem){e.Handled=true;Choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};footer.Children.Add(Cancel);footer.Children.Add(Choose);SetRow(footer,4);Children.Add(footer);
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Cancelled?.Invoke();}};
    }
    internal void SetCandidates(IReadOnlyList<string> paths){Candidates.ItemsSource=paths.Select(p=>new ExecutableChoice(p)).ToArray();Candidates.SelectedIndex=-1;Choose.IsEnabled=false;}
}

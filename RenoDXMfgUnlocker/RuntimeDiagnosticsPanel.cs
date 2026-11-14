using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace RenoDXMfgUnlocker;

/// <summary>Display grouping only; the diagnostic service remains the authority for activity/session judgments.</summary>
internal sealed class RuntimeDiagnosticsPanel(Action<string,string>? details=null) : StackPanel
{
    private RuntimeDiagnosticReport? report;
    private string section="Overview",search="";
    internal Button ExpertButton {get;}=new(){Content="Verified Profile Inspector",Padding=new Thickness(12,8,12,8),HorizontalAlignment=HorizontalAlignment.Left};
    internal void Render(RuntimeDiagnosticReport value){report=value;Render();}
    internal static string Category(string text)=>text.StartsWith("Runtime detected on disk:",StringComparison.Ordinal)?"Files on disk":text.StartsWith("DLL installed:",StringComparison.Ordinal)?"Installation evidence":(text.StartsWith("Runtime loaded:",StringComparison.Ordinal)||text.StartsWith("Loaded modules:",StringComparison.Ordinal))?"Loaded modules":text.StartsWith("NVIDIA Dynamic MFG:",StringComparison.Ordinal)?"NVIDIA Dynamic MFG":text.StartsWith("Intel XeMFG:",StringComparison.Ordinal)?"Intel XeMFG":text.StartsWith("XMX:",StringComparison.Ordinal)?"XMX":text.StartsWith("Generation configuration:",StringComparison.Ordinal)?"Generation configuration":"Restart evidence";
    private static TextBlock Label(string text,int size=12)=>SetupWizardView.Label(text,size);
    private void Card(string title,string content,string? full=null)
    {
        var body=new StackPanel();var heading=Label(title,13);heading.FontWeight=FontWeights.SemiBold;heading.Foreground=SetupWizardView.Brush("#CDB4EF");body.Children.Add(heading);body.Children.Add(Label(content));
        if(full is not null&&details is not null){var button=new Button {Content="Details",Padding=new Thickness(10,6,10,6),HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>details(title,full);body.Children.Add(button);}var card=SetupWizardView.Card(body,12);card.Margin=new Thickness(0,5,0,4);Children.Add(card);
    }
    private void Render()
    {
        if(ExpertButton.Parent is ContentControl parent)parent.Content=null;
        Children.Clear();var tabs=new WrapPanel();foreach(var name in new[]{"Overview","Installed Components","Session / Loaded Modules","Logs","Warnings"}){var button=new Button {Content=name,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,6,6),Background=SetupWizardView.Brush(name==section?"#493361":"#20222C")};button.Click+=(_,_)=>{section=name;Render();};tabs.Children.Add(button);}Children.Add(tabs);
        if(report is null)return;
        if(section=="Overview"){
            var group=new UniformGrid {Columns=2};
            foreach(var status in report.Statuses){var body=new StackPanel();var title=Label(Category(status),13);title.Foreground=SetupWizardView.Brush("#CDB4EF");title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);body.Children.Add(Label(status));var card=SetupWizardView.Card(body,12);card.Margin=new Thickness(0,4,8,4);group.Children.Add(card);}Children.Add(group);
            Children.Add(Label("Present on disk, installed files, loaded modules and active frame generation are separate evidence levels."));
            if(report.Logs is {Count:>0})Children.Add(Label("Latest log write · "+report.Logs.Max(l=>l.LastWriteUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")));
            if(report.Warnings.Count>0)Card("Warnings",report.Warnings[0],string.Join("\n\n",report.Warnings));
            Children.Add(new Expander {Header="Technical Details",Content=ExpertButton,Margin=new Thickness(0,12,0,0)});
        }else if(section=="Installed Components"){
            var disk=report.Evidence.Where(e=>e.StartsWith("Disk · ",StringComparison.Ordinal)).ToArray();
            foreach(var line in disk){var lines=line.Split('\n');var fields=lines[0].Split(" · ");var name=fields.Length>1?fields[1]:"Unknown";var version=TechnicalVersionDisplay.Format(fields.Length>2?fields[2]:"Unknown version");var location=lines.Length>1?SetupWizardView.ShortTarget(lines[1]):"Unknown";Card(name,version+" · "+location+"\nPresent on disk",RuntimeDiagnosticVersionDisplay.Evidence(line));}
            if(disk.Length==0)Children.Add(Label("No component inventory in this snapshot."));
        }else if(section=="Session / Loaded Modules"){
            var context="Unconfirmed evidence";var provenance="";
            foreach(var line in report.Evidence.Where(e=>!e.StartsWith("Disk · ",StringComparison.Ordinal))){
                if(line.StartsWith("Current process session matched",StringComparison.Ordinal)){context="Confirmed current session";provenance=line;}
                else if(line.StartsWith("Previous / unverified session",StringComparison.Ordinal)||line.StartsWith("ReShade recorded session",StringComparison.Ordinal)){context="Historical / unconfirmed session";provenance=line;}
                Card(context,RuntimeDiagnosticVersionDisplay.Evidence(line.Split('\n')[0]),RuntimeDiagnosticVersionDisplay.Evidence(provenance+"\n\n"+line));
            }
        }else if(section=="Warnings"){
            if(report.Warnings.Count==0)Children.Add(Label("No warnings reported by this snapshot."));foreach(var warning in report.Warnings)Card("Warnings",warning);
        }else RenderLogs();
        LocalizedPresentation.RefreshGeneratedItems(this);
    }
    private void RenderLogs()
    {
        var query=new TextBox {Text=search,ToolTip="Find in logs",Margin=new Thickness(0,8,0,6)};var next=new Button {Content="Find next",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,6,10,6)};var result=Label("");Children.Add(query);Children.Add(next);Children.Add(result);
        var viewers=new List<TextBox>();var current=0;var offset=0;query.TextChanged+=(_,_)=>{search=query.Text;current=0;offset=0;result.Text="";};
        foreach(var log in report!.Logs??[]){Children.Add(Label(log.Path+"\n"+log.LastWriteUtc.ToString("O")));var text=new TextBox {Text=log.Text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,6,0,12)};LocalizedPresentation.SetPreserveOriginal(text,true);viewers.Add(text);Children.Add(text);}
        next.Click+=(_,_)=>{if(search.Length==0||viewers.Count==0)return;for(var i=0;i<=viewers.Count;i++){var text=viewers[current];var index=text.Text.IndexOf(search,offset,StringComparison.OrdinalIgnoreCase);if(index>=0){text.Focus();text.Select(index,search.Length);var line=text.GetLineIndexFromCharacterIndex(index);if(line>=0)text.ScrollToLine(line);offset=index+search.Length;return;}current=(current+1)%viewers.Count;offset=0;}result.Text="No match found";};
        if(viewers.Count==0)Children.Add(Label("No readable bounded log snapshots are available."));
    }
}

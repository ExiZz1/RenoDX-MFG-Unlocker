using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    internal static void ConfigureRestoreList(ListBox list,int count)
    {
        list.VerticalAlignment=VerticalAlignment.Top;
        list.MinHeight=48;
        list.MaxHeight=count==0?52:count==1?94:170;
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
    }

    internal FrameworkElement ProfileEvidenceContent(string text,Action<string>? open=null,Action<string>? copy=null)
    {
        open??=OpenExternalLink;
        copy??=url=>{try{Clipboard.SetText(url);}catch(System.Runtime.InteropServices.ExternalException){ShowThemeDialog("Copy URL","Clipboard unavailable",true);}};
        var body=new StackPanel();bool evidence=false;
        foreach(var line in text.Replace("\r\n","\n").Split('\n'))
        {
            if(string.IsNullOrWhiteSpace(line))continue;
            if(line=="Evidence:")evidence=true;
            var links=EvidenceLinkPolicy.Extract(line);
            var label=ToolText(line,13);
            if(links.Count==0){if(evidence&&line!="Evidence:")LocalizedPresentation.SetPreserveOriginal(label,true);body.Children.Add(label);continue;}
            // Preserve external claims, dates, paths and URLs exactly; only our surrounding labels localize.
            LocalizedPresentation.SetPreserveOriginal(label,true);label.Text="";int position=0;
            foreach(var (start,url) in links)
            {
                label.Inlines.Add(new Run(line[position..start]));
                var link=new Hyperlink(new Run(url)){Foreground=SetupWizardView.Brush("#C9B4FF"),Focusable=true,ToolTip=url};
                System.Windows.Automation.AutomationProperties.SetName(link,url);
                link.Click+=(_,_)=>{if(EvidenceLinkPolicy.TryGetHttpUri(url,out _))open(url);};
                label.Inlines.Add(link);position=start+url.Length;
            }
            label.Inlines.Add(new Run(line[position..]));body.Children.Add(label);
            foreach(var (_,url) in links)
            {
                var button=new Button {Content="Copy URL",ToolTip=url,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,0,10)};
                button.Click+=(_,_)=>copy(url);body.Children.Add(button);
            }
        }
        return body;
    }
}

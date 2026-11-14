using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private async void RuntimeDiagnostics_Click(object sender,RoutedEventArgs e)
    {
        try {await ShowRuntimeDiagnosticsAsync(this);}
        catch(Exception ex){ShowThemeDialog("Runtime diagnostics",ex.Message,true);}
    }
    private Task ShowRuntimeDiagnosticsAsync(Window owner)
    {
        var exe=selectedExe;var root=selectedGameRoot;
        if(exe is null || root is null)throw new InvalidDataException("Select a game and its EXE first. Diagnostics never starts a game.");
        var window=new Window {Title="Runtime Diagnostics",Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=Math.Min(820,SystemParameters.WorkArea.Width-40),Height=Math.Min(640,SystemParameters.WorkArea.Height-40),
            MinWidth=650,MinHeight=450,Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("Text"),Icon=Icon,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize};
        var grid=new Grid {Margin=new Thickness(22)};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new TextBlock {Text="Runtime Diagnostics · read-only",FontSize=23,Margin=new Thickness(0,0,0,14)};
        heading.MouseLeftButtonDown+=(_,_)=>window.DragMove();grid.Children.Add(heading);
        var body=new StackPanel();var scroll=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        scroll.Resources.Add(typeof(ScrollBar),FindResource("PremiumScrollBar"));Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        var footer=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var refresh=new Button {Content="Refresh evidence",Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,8,0)};
        RuntimeDiagnosticReport? currentReport = null;
        var export = new Button { Content = "Export diagnostic report", IsEnabled = false, Padding = new Thickness(14,8,14,8), Margin = new Thickness(0,0,8,0) };
        export.Click += (_, _) =>
        {
            if (currentReport is null) return;
            var language = LocalizedPresentation.Language;
            var reportText = RuntimeDiagnosticsExport.Create(currentReport, language);
            var previewText = new TextBox { Text = reportText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 260,
                MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            LocalizedPresentation.SetPreserveOriginal(previewText, true);
            var previewBody = new StackPanel();
            previewBody.Children.Add(previewText);
            var save = new Button { Content = language.Lookup("Save diagnostic report"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0) };
            var actions = new WrapPanel();
            actions.Children.Add(save);
            var copy = new Button { Content = language.Lookup("Copy redacted report"), Margin = new Thickness(8,8,0,0) };
            actions.Children.Add(copy);
            previewBody.Children.Add(actions);
            var copyStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
            previewBody.Children.Add(copyStatus);
            var preview = CreateInfoDialog(language.Lookup(RuntimeDiagnosticsExport.Review), previewBody, 680, 650, window);
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(reportText); copyStatus.Text = language.Lookup("Redacted report copied."); }
                catch (System.Runtime.InteropServices.ExternalException) { copyStatus.Text = language.Lookup("Could not copy the report. Try again."); }
            };
            save.Click += (_, _) =>
            {
                var picker = new Microsoft.Win32.SaveFileDialog { FileName = "RenoDX-Diagnostics.txt", DefaultExt = ".txt", Filter = language.Lookup("Text file (*.txt)") + "|*.txt", OverwritePrompt = true,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
                if (ShowNativeDialog(picker, preview) != true) return;
                try { File.WriteAllText(picker.FileName, reportText); preview.Close(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { ShowThemeDialog("Runtime diagnostics", language.Lookup("The diagnostic report could not be saved."), true); }
            };
            preview.ShowDialog();
        };
        footer.Children.Add(export);
        var close=new Button {Content="Close",Padding=new Thickness(14,8,14,8)};close.Click+=(_,_)=>window.Close();footer.Children.Add(refresh);footer.Children.Add(close);Grid.SetRow(footer,2);grid.Children.Add(footer);window.Content=new Border {Child=grid,Background=(Brush)FindResource("Panel"),BorderBrush=(Brush)FindResource("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16)};
        void Add(string value,bool status=false)=>body.Children.Add(new TextBlock {Text=value,TextWrapping=TextWrapping.Wrap,Foreground=status?(Brush)FindResource("Purple"):(Brush)FindResource("Text"),FontSize=status?13:11,Margin=new Thickness(0,4,12,10)});
        async Task Load()
        {
            refresh.IsEnabled=false;export.IsEnabled=false;currentReport=null;body.Children.Clear();Add("Reading bounded log snapshots and current file inventory…",true);
            try {
                DateTimeOffset? installed=null;bool verified=false,configurationVerified=false;
                RuntimeDiskComponent[] verifiedBackups = [];
                try {
                    var transactions=await Task.Run(()=>CreateBackupService(root).ListTransactions(root));
                    verifiedBackups = RuntimeDiagnosticsService.BackupInventory(transactions);
                    var latest=transactions.Where(t=>t.Status=="Completed" && t.ManifestVerified).OrderByDescending(t=>t.CreatedUtc).FirstOrDefault();
                    if(latest is not null){installed=latest.CreatedUtc;var matches=latest.Files.Count>0&&latest.Files.All(f=>f.CurrentStatus=="Matches installed SHA-256");verified=matches&&latest.Files.Any(f=>f.RelativeTargetPath.EndsWith(".dll",StringComparison.OrdinalIgnoreCase));configurationVerified=matches&&latest.Files.Any(f=>System.IO.Path.GetFileName(f.RelativeTargetPath) is "ReShade.ini" or "MFGAdaUnlock.ini");}
                }catch(IOException){}catch(System.Security.Cryptography.CryptographicException){}catch(UnauthorizedAccessException){}
                var report=await new RuntimeDiagnosticsService().InspectAsync(exe,root,installed,verified,configurationHashesVerified:configurationVerified);
                report = report with { DiskComponents = [..report.DiskComponents ?? [], ..verifiedBackups] };
                if(!window.IsVisible)return;
                currentReport=report;export.IsEnabled=true;
                body.Children.Clear();Add(exe);var panel=new RuntimeDiagnosticsPanel((title,text)=>ShowComponentDetails(title,text,window));panel.Render(report);panel.ExpertButton.Click+=(_,_)=>{window.Close();NvidiaInstallPreview_Click(this,new RoutedEventArgs());};body.Children.Add(panel);

            }catch(Exception ex){body.Children.Clear();Add("Not verified · "+ex.Message,true);}
            finally{refresh.IsEnabled=true;LocalizedPresentation.RefreshGeneratedItems(body);}
        }
        refresh.Click+=async (_,_)=>await Load();window.Loaded+=async (_,_)=>await Load();
        window.ShowDialog();
        return Task.CompletedTask;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    internal static DataTemplate RestoreTransactionTemplate()=>(DataTemplate)System.Windows.Markup.XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid Margin="6,5"><Grid.RowDefinitions><RowDefinition/><RowDefinition/></Grid.RowDefinitions><Grid.ColumnDefinitions><ColumnDefinition Width="1.4*"/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Text="{Binding Created}" FontSize="12" TextWrapping="Wrap"/><TextBlock Grid.Column="1" Text="{Binding Status}" Foreground="#C9B9FF" FontSize="12" Margin="10,0" TextWrapping="Wrap"/><TextBlock Grid.Column="2" Text="{Binding Availability}" FontSize="12" TextWrapping="Wrap"/><TextBlock Grid.Row="1" Text="{Binding FileCount}" FontSize="11" Foreground="#AAA2B6" Margin="0,5,0,0"/></Grid></DataTemplate>
        """);
    internal static string RestoreSummary(BackupRestorePreview preview, UiLocalization language)
    {
        var blocked = preview.Files.Where(f=>f.Action=="Blocked").ToArray();
        if(blocked.Length==0) return language.Translate(preview.Message);
        return string.Join("\n", new[] { language.Lookup("Restore is blocked.") }
            .Concat(blocked.Select(f=>f.RelativeTargetPath+" · "+language.Translate(f.Message)))
            .Append(language.Lookup("No files have been changed.")));
    }
    internal static string RestoreStatus(string status,UiLocalization language)=>language.Lookup(status switch
    {
        "InProgress" => "Transaction in progress",
        "RollbackNeedsReview" => "Rollback requires review",
        "Invalid" => "Invalid transaction",
        _ => status
    });
    internal static string RestoreConfirmationText(string root,BackupRestorePreview preview)
    {
        var language=LocalizedPresentation.Language;
        return language.Lookup("Restore original files / remove transaction-created files")+"\n"+
            language.Format("Game folder: {0}\nTransaction: {1}\nAffected files: {2}",root,preview.TransactionId,preview.Files.Count(f=>f.Action is "Restore original" or "Remove transaction file"))+
            "\n\n"+RestoreSummary(preview,language)+"\n"+language.Lookup("Each file is checked again immediately before it is changed.");
    }
    private string profileDetails="";
    private void ProfileDetails_Click(object sender,RoutedEventArgs e)=>CreateInfoDialog("Profile details",ProfileEvidenceContent(profileDetails),780,720).ShowDialog();
    internal FrameworkElement RestorePreviewContent(BackupRestorePreview preview,string root,Window owner)
    {
        var body=new StackPanel();body.Children.Add(ToolText(preview.CanRestore?"Restore available":"Conflict / review",14));
        var language=LocalizedPresentation.Language;
        var rows=preview.Files.Select(file=>(new[]{file.RelativeTargetPath,language.Lookup(file.Action),language.Lookup(file.CurrentStatus),file.OriginalBackupVerified?"Verified SHA-256":"See Details","Details"},(Action)(()=>ShowComponentDetails(file.RelativeTargetPath,$"Transaction: {preview.TransactionId}\nTarget: {Path.Combine(root,file.RelativeTargetPath)}\nAction: {language.Lookup(file.Action)}\n{language.Lookup(file.CurrentStatus)}\n{language.Translate(file.Message)}",owner))));
        body.Children.Add(CompactTable(["File / Component","Action","Destination state","Backup integrity","Details"],rows,singleLineDetails:true));
        var technical=new Button {Content="Transaction Details",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};
        technical.Click+=(_,_)=>ShowComponentDetails("Transaction Details",$"Transaction: {preview.TransactionId}\nStatus: {RestoreStatus(preview.Status,language)}\n{RestoreSummary(preview,language)}\n"+language.Lookup(preview.ManifestVerified?"Manifest HMAC verified.":"Manifest is NOT verified."),owner);body.Children.Add(technical);
        var scroll=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};scroll.Resources.Add(typeof(ScrollBar),FindResource("PremiumScrollBar"));return scroll;
    }
    private void ShowIntelSdkReleaseDetails(IntelSdkAsset asset)
    {
        var body=new StackPanel();body.Children.Add(ToolText("SDK version: "+TechnicalVersionDisplay.Format(asset.Tag),15));body.Children.Add(ToolText(asset.Name));
        body.Children.Add(ToolText("SDK version is not a DLL version. Runtime compatibility is checked separately."));
        var metadata=new Expander {Header="Technical Details",Content=ComponentDetailsContent($"Source: {asset.DownloadUrl}\nSize: {asset.Size} bytes\nSHA-256: {asset.Sha256??"Unknown"}"),Margin=new Thickness(0,6,0,12)};body.Children.Add(metadata);
        var viewer=new FlowDocumentScrollViewer {Document=BuildReleaseNotesDocument(asset.Notes),IsToolBarVisible=false,Background=System.Windows.Media.Brushes.Transparent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=360};LocalizedPresentation.SetPreserveOriginal(viewer,true);body.Children.Add(viewer);CreateInfoDialog(asset.Title,body,780,740).ShowDialog();
    }
}

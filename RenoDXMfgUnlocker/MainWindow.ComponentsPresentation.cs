using System.Windows;
namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    private bool NvidiaStageMatchesSelection()=>selectedStreamlineAsset is {} asset&&verifiedStreamlineStage is {} stage&&stage.Asset.ReleaseTag==asset.ReleaseTag&&stage.Asset.Name==asset.Name&&stage.Asset.Sha256==asset.Sha256&&stage.Asset.Size==asset.Size&&stage.Asset.DownloadUrl==asset.DownloadUrl;
    private void UpdateNvidiaSdkSummary()
    {
        if(NvidiaSelectedSdkText is null)return;
        NvidiaSdkDetailsButton.IsEnabled=selectedStreamlineAsset is not null;
        StreamlineDownloadButton.Content=selectedStreamlineDownloadPath is null?"Download SDK":"Reverify archive";
        StreamlineStageButton.Content=NvidiaStageMatchesSelection()?"Reprepare SDK":"Prepare SDK";
        NvidiaSelectedSdkText.Text=selectedStreamlineAsset is {} asset?"Selected SDK · "+TechnicalVersionDisplay.Format(asset.ReleaseTag)+" · "+asset.Name:"Selected SDK: not selected";
        NvidiaPreparedSdkText.Text=NvidiaStageMatchesSelection()?"Local / Prepared Components · "+verifiedStreamlineStage!.Files.Count+" · Game compatibility is not established by SDK preparation.":"Contained DLL names and versions are Unknown until this SDK is prepared.";
        LocalizedPresentation.RefreshGeneratedItems(StreamlinePackagesPanel);
    }
    private void NvidiaSdkDetails_Click(object sender,RoutedEventArgs e)
    {
        if(selectedStreamlineAsset is not {} asset)return;
        var text=$"SDK version: {TechnicalVersionDisplay.Format(asset.ReleaseTag)}\nSource: {asset.DownloadUrl}\nSHA-256: {asset.Sha256??"Unknown"}\nSDK version is not a DLL version. Runtime compatibility is checked separately.";
        if(NvidiaStageMatchesSelection())text+="\n\n"+string.Join("\n\n",verifiedStreamlineStage!.Files.Select(file=>$"Component: {file.FileName}\nFile version: {TechnicalVersionDisplay.Format(file.Version??"Unknown version")}\nLocation: {file.RelativePath}\nSHA-256: {file.Sha256}"));
        else text+="\n\nContained DLL names and versions are Unknown until this SDK is prepared.";
        ShowComponentDetails("Technical Details",text);
    }
}

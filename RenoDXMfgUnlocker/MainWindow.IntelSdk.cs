using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private readonly IntelSdkCatalogClient intelSdkCatalog=new(Http);
    private readonly IntelSdkPackageService intelSdkPackages=new(http:Http);
    private bool intelSdkBusy,intelSdkLoaded;
    private CancellationTokenSource? intelSdkCancellation;
    private IntelSdkStage? selectedIntelStage;
    private int intelSdkSelection;
    private CancellationTokenSource? intelSdkInspection;
    private string intelSdkLastError="";
    private bool intelCompatibleMode,intelCompatiblePrepared;
    private string? selectedIntelInspectionError;
    private IntelSdkDownload? selectedIntelUnverifiedArchive;
    private readonly Dictionary<string,IntelSdkStage> intelSdkDisplaySnapshots = new(StringComparer.OrdinalIgnoreCase);
    private static string IntelSdkIdentity(IntelSdkAsset asset)=>asset.Tag+"|"+asset.Name+"|"+asset.Size+"|"+asset.Sha256;
    private sealed record IntelSdkRow(IntelSdkAsset Asset)
    {
        public string Title=>Asset.Title;
        public string Summary=>Asset.Name+" · "+(Asset.Size/1024d/1024).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" MB · "+(Asset.PublishedAt?.ToString("yyyy-MM-dd")??"Unknown");
        public string Classification=>(Asset.OfficialLatest?"Official Latest · ":"")+"Experimental / Unverified"+(Asset.Sha256 is null?" · Official digest unavailable":"");
    }
    private void InitializeIntelSdkManager()
    {
        IntelSdkView.CompatibleText.Text="MFG Unlock Compatible · XeSS-FG "+TechnicalVersionDisplay.Format(IntelRuntimeService.Assets[0].Version)+" / XeLL "+TechnicalVersionDisplay.Format(IntelRuntimeService.Assets[1].Version);
        IntelSdkView.SelectedSdkModeButton.Click+=(_,_)=>SetIntelPanelMode(false);
        IntelSdkView.CompatibleModeButton.Click+=(_,_)=>SetIntelPanelMode(true);
        IntelSdkView.CatalogEvidenceButton.Click+=(_,_)=>ShowComponentDetails("Catalog evidence","Catalog source: https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/7fa2e69fc3fec7e8c90249c55e247de9dc4a0b5e/src/addons/mfgunlock/xemfg/dependency_catalog.json\n"+"Pinned official Intel catalog\n"+IntelRuntimeService.BaseUrl+"\nCatalog versions are independent of SDK selection. Setup Wizard rechecks the pinned files before use.");
        SetIntelPanelMode(false);
        IntelSdkView.RefreshButton.Click+=async (_,_)=>await LoadIntelSdks();
        IntelSdkView.ReleasesList.SelectionChanged+=async (_,_)=>await InspectIntelSelection();
        IntelSdkView.DownloadButton.Click+=async (_,_)=>{
            if(IntelSdkView.ReleasesList.SelectedItem is not IntelSdkRow row)return;
            await RunIntelSdk(async token=>{
                var download=await intelSdkPackages.DownloadAsync(row.Asset,new Progress<StreamlineDownloadProgress>(p=>{IntelSdkView.StatusText.Text="Downloading Intel SDK · "+p.BytesReceived.ToString("N0")+" / "+row.Asset.Size.ToString("N0")+" bytes";IntelSdkView.Progress.IsIndeterminate=false;IntelSdkView.Progress.Value=100d*p.BytesReceived/row.Asset.Size;}),token);
                if(!download.OfficialDigestVerified){selectedIntelUnverifiedArchive=download;RenderIntelPanel();IntelSdkView.StatusText.Text="Official digest unavailable. Archive downloaded as Unverified; staging is blocked.";return;}
                IntelSdkView.StatusText.Text="Checking Intel SDK and staging allowed files…";IntelSdkView.Progress.IsIndeterminate=true;
                selectedIntelStage=await intelSdkPackages.StageAsync(row.Asset,token);
                if(selectedIntelStage.CatalogCompatible){await intelSdkPackages.PrepareCompatibleAsync(selectedIntelStage,token);intelCompatiblePrepared=true;}
                ShowIntelStage(selectedIntelStage);IntelSdkView.StatusText.Text="Intel SDK prepared outside the game. No installation was performed.";
            });
        };
        IntelSdkView.ImportArchiveButton.Click+=async (_,_)=>{
            if(IntelSdkView.ReleasesList.SelectedItem is not IntelSdkRow row)return;
            var dialog=new OpenFileDialog {Title="Select the matching official Intel SDK ZIP",Filter="SDK ZIP (*.zip)|*.zip",CheckFileExists=true};
            if(ShowNativeDialog(dialog,this)!=true)return;
            await RunIntelSdk(async token=>{
                IntelSdkView.StatusText.Text="Checking Intel SDK and staging allowed files…";
                var stage=await intelSdkPackages.ImportArchiveAsync(dialog.FileName,row.Asset,token);
                ShowIntelStage(stage);IntelSdkView.StatusText.Text="Intel SDK prepared outside the game. No installation was performed.";
            });
        };
        IntelSdkView.PrepareCompatibleButton.Click+=async (_,_)=>await RunIntelSdk(async token=>{
            var package=await intelSdkPackages.Runtime.PrepareAsync(new Progress<string>(s=>IntelSdkView.StatusText.Text=s),token);
            ShowPinnedIntelCache();IntelSdkView.StatusText.Text="Compatible runtime cache prepared. Use DLL Installation to review game changes.";LocalizedPresentation.RefreshGeneratedItems(IntelSdkView);
        });
        IntelSdkView.ReleaseDetailsButton.Click+=(_,_)=>{if(IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow row)ShowIntelSdkReleaseDetails(row.Asset);};
        IntelSdkView.CancelButton.Click+=(_,_)=>intelSdkCancellation?.Cancel();
        IntelSdkView.StatusDetailsButton.Click+=(_,_)=>ShowComponentDetails("Intel XeSS SDK",intelSdkLastError);
        IntelSdkView.WizardButton.Click+=SetupWizard_Click;
        IntelSdkView.OfficialButton.Click+=(_,_)=>OpenExternalLink("https://github.com/intel/xess/releases");
        IntelSdkView.OpenDownloadsButton.Click+=(_,_)=>OpenIntelFolder(intelSdkPackages.Downloads);
        IntelSdkView.OpenStagingButton.Click+=(_,_)=>{if(selectedIntelStage is not null)OpenIntelFolder(selectedIntelStage.Directory);};
    }
    private void OpenIntelFolder(string path){try{if(!Directory.Exists(path))throw new DirectoryNotFoundException("No prepared files found.");System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){ShowThemeDialog("Local Components",ex.Message,true);}}
    private void SetComponentsProvider(bool intel)
    {
        IntelSdkView.Visibility=intel?Visibility.Visible:Visibility.Collapsed;NvidiaManagerBody.Visibility=intel?Visibility.Collapsed:Visibility.Visible;NvidiaGameContext.Visibility=intel?Visibility.Collapsed:Visibility.Visible;NvidiaManagerActions.Visibility=intel?Visibility.Collapsed:Visibility.Visible;NvidiaOfficialLinkButton.Visibility=!intel&&NvidiaInventoryPanel.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;NvidiaScanStatusText.Visibility=!intel&&NvidiaInventoryPanel.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
        SetNavButton(NvidiaProviderButton,!intel);SetNavButton(IntelProviderButton,intel);
        NvidiaPageDisclaimerText.Text=intel?"SDK preparation never installs game files. Official Latest and MFG Unlock Compatible are separate states.":"SDK version, DLSS-G version and local files are separate facts. A compatible game configuration requires independent evidence.";
        LocalizedPresentation.RefreshGeneratedItems(NvidiaComponentsPage);
    }
    private void NvidiaProvider_Click(object sender,RoutedEventArgs e)=>SetComponentsProvider(false);
    private async void IntelProvider_Click(object sender,RoutedEventArgs e){SetComponentsProvider(true);if(!intelSdkLoaded)await LoadIntelSdks();}
    private async Task LoadIntelSdks()=>await RunIntelSdk(async token=>{
        IntelSdkView.StatusText.Text="Loading official Intel SDK metadata…";var catalog=await intelSdkCatalog.GetAsync(token);
        IntelSdkView.ReleasesList.ItemsSource=catalog.Assets.Select(a=>new IntelSdkRow(a)).ToArray();intelSdkLoaded=true;
        IntelSdkView.StatusText.Text=catalog.Assets.Count==0?"No official Intel SDK archives available.":"Official Latest · "+TechnicalVersionDisplay.Format(catalog.LatestTag??"Unknown")+" · SDK version is not a DLL version.";
        if(catalog.Assets.Count>0)IntelSdkView.ReleasesList.SelectedIndex=0;
    });
    private async Task RunIntelSdk(Func<CancellationToken,Task> action)
    {
        if(intelSdkBusy||AnyFileOperationBusy)return;intelSdkInspection?.Cancel();intelSdkBusy=true;intelSdkCancellation=new();IntelSdkView.WizardButton.IsEnabled=false;IntelSdkView.ImportArchiveButton.IsEnabled=false;IntelSdkView.StatusDetailsButton.Visibility=Visibility.Collapsed;IntelSdkView.CancelButton.IsEnabled=true;IntelSdkView.Progress.Visibility=Visibility.Visible;IntelSdkView.Progress.IsIndeterminate=true;IntelSdkView.ReleasesList.IsEnabled=false;IntelSdkView.RefreshButton.IsEnabled=false;IntelSdkView.DownloadButton.IsEnabled=false;IntelSdkView.PrepareCompatibleButton.IsEnabled=false;
        try{await action(intelSdkCancellation.Token);}catch(OperationCanceledException){IntelSdkView.StatusText.Text="Intel SDK operation cancelled. Existing cache files were kept.";}catch(Exception ex){intelSdkLastError=ex.Message;IntelSdkView.StatusDetailsButton.Visibility=Visibility.Visible;IntelSdkView.StatusText.Text="Intel SDK operation failed. Retry with Refresh or Download / Prepare.";Log("Intel SDK: "+ex.Message);}
        finally{intelSdkCancellation.Dispose();intelSdkCancellation=null;intelSdkBusy=false;IntelSdkView.WizardButton.IsEnabled=true;IntelSdkView.CancelButton.IsEnabled=false;IntelSdkView.Progress.Visibility=Visibility.Collapsed;IntelSdkView.ReleasesList.IsEnabled=true;IntelSdkView.RefreshButton.IsEnabled=true;IntelSdkView.PrepareCompatibleButton.IsEnabled=true;IntelSdkView.DownloadButton.IsEnabled=IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow;IntelSdkView.ImportArchiveButton.IsEnabled=IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow {Asset.Sha256:not null};LocalizedPresentation.RefreshGeneratedItems(IntelSdkView);}
    }
    private async Task InspectIntelSelection()
    {
        var generation=++intelSdkSelection;selectedIntelStage=null;selectedIntelInspectionError=null;selectedIntelUnverifiedArchive=null;
        intelSdkInspection?.Cancel();intelSdkInspection=null;
        IntelSdkView.ReleaseDetailsButton.IsEnabled=IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow;IntelSdkView.DownloadButton.IsEnabled=!intelSdkBusy&&IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow;
        IntelSdkView.ImportArchiveButton.IsEnabled=!intelSdkBusy&&IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow {Asset.Sha256:not null};
        if(!intelCompatibleMode)RenderIntelPanel();
        if(IntelSdkView.ReleasesList.SelectedItem is not IntelSdkRow row)return;
        if(intelSdkDisplaySnapshots.TryGetValue(IntelSdkIdentity(row.Asset),out var cached)&&Directory.Exists(cached.Directory)){selectedIntelStage=cached;if(!intelCompatibleMode)RenderIntelPanel();return;}
        using var inspection=new CancellationTokenSource();intelSdkInspection=inspection;
        try{
            if(!intelCompatibleMode)IntelSdkView.LocalStatus.Text="Checking the selected SDK's local archive and staging…";
            var stage=await intelSdkPackages.InspectPreparedAsync(row.Asset,inspection.Token);if(generation!=intelSdkSelection)return;
            if(stage is not null){selectedIntelStage=stage;intelSdkDisplaySnapshots[IntelSdkIdentity(row.Asset)]=stage;}
        }catch(OperationCanceledException){return;}
        catch(Exception ex){if(generation!=intelSdkSelection)return;selectedIntelInspectionError=ex.Message;intelSdkLastError=ex.Message;IntelSdkView.StatusDetailsButton.Visibility=Visibility.Visible;}
        finally{if(ReferenceEquals(intelSdkInspection,inspection))intelSdkInspection=null;}
        if(!intelCompatibleMode)RenderIntelPanel();
    }
    private void ShowIntelStage(IntelSdkStage stage)
    {
        intelSdkDisplaySnapshots[IntelSdkIdentity(stage.Asset)]=stage;
        if(IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow row&&IntelSdkIdentity(row.Asset)==IntelSdkIdentity(stage.Asset)){selectedIntelStage=stage;selectedIntelUnverifiedArchive=null;selectedIntelInspectionError=null;}
        RenderIntelPanel();
    }
    private void RenderSelectedIntelStage(IntelSdkStage stage)
    {
        IntelSdkView.OpenStagingButton.IsEnabled=true;
        IntelSdkView.LocalStatus.Text="Prepared SDK snapshot · "+(stage.CatalogCompatible?"MFG Unlock Compatible · exact pinned runtime catalog":"Experimental / Unverified · not eligible for Setup Wizard");
        foreach(var file in stage.Files){var name=Path.GetFileName(file.RelativePath);AddIntelFile(name,file.Version,file.Version=="Unknown version"?"Unknown version":stage.CatalogCompatible?"MFG Unlock Compatible":"Experimental / Unverified",$"SDK version: {TechnicalVersionDisplay.Format(stage.Asset.Tag)}\nSource: {stage.Asset.DownloadUrl}\nTarget: {file.Path}\nFile version: {TechnicalVersionDisplay.Format(file.Version)}\nSHA-256: {file.Sha256}\nArchitecture: {file.Architecture}\nOfficial archive digest verified; compatibility is separate.");}
        LocalizedPresentation.RefreshGeneratedItems(IntelSdkView);
    }
    private void AddIntelFile(string name,string version,string state,string details)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var body=new StackPanel();body.Children.Add(ToolText(name,13));body.Children.Add(ToolText(TechnicalVersionDisplay.Format(version)+" · "+state,11));grid.Children.Add(body);var button=new Button {Content="Details",Padding=new Thickness(9,6,9,6),VerticalAlignment=VerticalAlignment.Center};button.Click+=(_,_)=>ShowComponentDetails(name,details);Grid.SetColumn(button,1);grid.Children.Add(button);IntelSdkView.LocalFiles.Children.Add(SetupWizardView.Card(grid,12));
    }
    private void ShowPinnedIntelCache(){intelCompatiblePrepared=true;SetIntelPanelMode(true);}
    private void SetIntelPanelMode(bool compatible){intelCompatibleMode=compatible;RenderIntelPanel();}
    private void RenderIntelPanel()
    {
        IntelSdkView.LocalFiles.Children.Clear();IntelSdkView.OpenStagingButton.IsEnabled=false;
        SetNavButton(IntelSdkView.SelectedSdkModeButton,!intelCompatibleMode);SetNavButton(IntelSdkView.CompatibleModeButton,intelCompatibleMode);
        IntelSdkView.LocalTitle.Text=intelCompatibleMode?"MFG Unlock Compatible":"Selected SDK Components";
        IntelSdkView.CatalogEvidenceButton.Visibility=intelCompatibleMode?Visibility.Visible:Visibility.Collapsed;
        IntelSdkView.OpenStagingButton.Visibility=intelCompatibleMode?Visibility.Collapsed:Visibility.Visible;
        if(intelCompatibleMode){
            IntelSdkView.PanelContextText.Text="Catalog versions are independent of SDK selection. Setup Wizard rechecks the pinned files before use.";
            IntelSdkView.LocalStatus.Text=intelCompatiblePrepared?"Compatible runtime cache prepared. Use DLL Installation to review game changes.":"Catalog values only. Local cache verification occurs when preparing or installing.";
            foreach(var asset in IntelRuntimeService.Assets)AddIntelFile(asset.Name,asset.Version.Length==0?"License / notice":asset.Version,"MFG Unlock Compatible",$"Catalog source: https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/7fa2e69fc3fec7e8c90249c55e247de9dc4a0b5e/src/addons/mfgunlock/xemfg/dependency_catalog.json\nSource: {IntelRuntimeService.BaseUrl+asset.Source}\nTarget: {Path.Combine(intelSdkPackages.Runtime.PackageDirectory,asset.Name)}\nSHA-256: {asset.Sha256}\nCatalog values only. Local cache verification occurs when preparing or installing.");
        }else if(IntelSdkView.ReleasesList.SelectedItem is IntelSdkRow row){
            IntelSdkView.PanelContextText.Text="Selected SDK · "+TechnicalVersionDisplay.Format(row.Asset.Tag)+" · "+row.Asset.Name;
            if(selectedIntelStage is {} stage&&IntelSdkIdentity(stage.Asset)==IntelSdkIdentity(row.Asset))RenderSelectedIntelStage(stage);
            else if(selectedIntelUnverifiedArchive is {} archive){IntelSdkView.LocalStatus.Text="Official digest unavailable. Archive downloaded as Unverified; staging is blocked.";AddIntelFile(row.Asset.Name,"Not applicable","Unverified",$"Source: {row.Asset.DownloadUrl}\nTarget: {archive.Path}\nSHA-256: {archive.Sha256}");}
            else IntelSdkView.LocalStatus.Text=selectedIntelInspectionError is null?"Download / Prepare the selected SDK to inspect actual DLL contents and versions.":"Unverified · prepared files could not be validated. See Details.";
        }else{IntelSdkView.PanelContextText.Text="Selected SDK: not selected";IntelSdkView.LocalStatus.Text="Select a release to inspect prepared files.";}
        LocalizedPresentation.RefreshGeneratedItems(IntelSdkView);
    }
    internal static string ExplainRuntimeError(string message)
    {
        const string prefix="Missing existing integration component ",suffix="; adding a new integration is not supported.";
        if(!message.StartsWith(prefix,StringComparison.Ordinal)||!message.EndsWith(suffix,StringComparison.Ordinal))return message;
        var name=message[prefix.Length..^suffix.Length];
        return "Supported NVIDIA updater layout is incomplete in this destination: "+name+".\nThis checks only the layout supported by this updater, not every possible Streamline integration.\nCopying SDK DLLs cannot create game Frame Generation integration or add native Frame Generation support.\nIntel XeMFG has separate compatibility and input requirements.";
    }
}

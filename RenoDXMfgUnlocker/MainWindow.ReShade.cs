using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private readonly ReShadePackageService reshadePackages=new();
    private readonly ReShadeDetectionService reshadeDetection=new();
    private PreparedReShadeInstaller? reshadePrepared;
    private ReShadeDetectionReport? reshadeReport;
    private ReShadeManualRiskApproval? reshadeRiskApproval;
    private readonly ReShadeSetupSession reshadeSession = new();
    private ReShadeSessionLog reshadeLifecycle = new();
    private bool reshadeCompleting;
    private bool reshadeRan;
    private int? reshadeExitCode;
    private string? reshadeContextExe,reshadeContextRoot;
    private bool reshadeBusy,reshadeCheckingCache;
    private bool reshadeInitialized;
    private ReShadeScanPhase reshadeScanPhase=ReShadeScanPhase.NotScanned;
    private int reshadeContextRevision;
    private string? reshadeContextAppId;
    private string reshadeLastError="";
    private string reshadePackageError="";
    private CancellationTokenSource? reshadeDownload;
    private ReShadeGraphicsApi ReShadeApi=>ReShadeView.ApiPicker.SelectedIndex switch{0=>ReShadeGraphicsApi.Direct3D11,1=>ReShadeGraphicsApi.Direct3D12,2=>ReShadeGraphicsApi.Vulkan,_=>ReShadeGraphicsApi.Unknown};
    private void InitializeReShade()
    {
        ReShadeView.ApiPicker.Template=GamesCombo.Template;ReShadeView.ApiPicker.ItemsSource=new[]{"Direct3D 11","Direct3D 12","Vulkan"};ReShadeView.ApiPicker.SelectedIndex=-1;
        ReShadeView.PackageText.Text="ReShade — Full Add-on Support · "+ReShadeInstallerVerifier.Reviewed.Version;
        ReShadeView.ApiPicker.SelectionChanged+=(_,_)=>{ResetReShadeScan();RenderReShade();};
        ReShadeView.HomeButton.Click+=Home_Click;
        ReShadeView.ScanButton.Click+=async(_,_)=>await RefreshReShadeAsync();
        ReShadeView.PrepareButton.Click+=async(_,_)=>await PrepareReShadeAsync(null);
        ReShadeView.ImportButton.Click+=async(_,_)=>{var dialog=new OpenFileDialog{Title=LocalizedPresentation.Language.Lookup("Select the reviewed Full Add-on Support installer"),Filter=LocalizedPresentation.Language.Lookup("ReShade installer (*.exe)")+"|*.exe",CheckFileExists=true};if(ShowNativeDialog(dialog,this)==true)await PrepareReShadeAsync(dialog.FileName);};
        ReShadeView.CancelPrepareButton.Click+=(_,_)=>reshadeDownload?.Cancel();
        ReShadeView.Consent.Checked+=(_,_)=>RenderReShadeButtons();ReShadeView.Consent.Unchecked+=(_,_)=>RenderReShadeButtons();ReShadeView.VulkanConsent.Checked+=(_,_)=>RenderReShadeButtons();ReShadeView.VulkanConsent.Unchecked+=(_,_)=>RenderReShadeButtons();
        ReShadeView.ProxyRiskConsent.Checked+=(_,_)=>
        {
            if(reshadeReport is not null && selectedGameRoot is not null && selectedExe is not null && !ReShadeManualLaunchPolicy.HasCriticalBlock(reshadeReport))
                reshadeRiskApproval=ReShadeManualLaunchPolicy.Approve(selectedGameRoot,selectedExe,ReShadeApi,reshadeReport);
            RenderReShadeButtons();
        };
        ReShadeView.ProxyRiskConsent.Unchecked+=(_,_)=>{reshadeRiskApproval=null;RenderReShadeButtons();};
        ReShadeView.OfficialButton.Click+=(_,_)=>OpenExternalLink("https://reshade.me/#download");
        ReShadeView.CacheButton.Click+=(_,_)=>{try{if(Directory.Exists(reshadePackages.Store))Process.Start(new ProcessStartInfo{FileName=reshadePackages.Store,UseShellExecute=true});}catch(Exception ex){reshadePackageError=ex.Message;ReShadeView.StatusText.Text="Cannot open the installer cache. See package verification details.";}};
        ReShadeView.PackageDetailsButton.Click+=(_,_)=>ShowComponentDetails("ReShade package verification",ReShadePresentation.PackageDetails(reshadePrepared)+"\n"+reshadePackageError);
        ReShadeView.DetectionDetailsButton.Click+=(_,_)=>CreateInfoDialog("ReShade detection details",ReShadeDetailsContent(),780,720).ShowDialog();
        ReShadeView.RunButton.Click+=async(_,_)=>await RunReShadeAsync();
        ReShadeView.FinishButton.Click+=async(_,_)=>await FinishReShadeAsync();
        LocalizedPresentation.SetPreserveOriginal(ReShadeView.LaunchHint,true);LocalizedPresentation.SetPreserveOriginal(ReShadeView.AddonText,true);LocalizedPresentation.SetPreserveOriginal(ReShadeView.VersionText,true);
        LocalizedPresentation.SetPreserveOriginal(ReShadeView.CompactSummaryText,true);
        EventHandler languageChanged=(_,_)=>{if(Dispatcher.CheckAccess())RenderReShade();else Dispatcher.BeginInvoke(new Action(RenderReShade));};
        LocalizedPresentation.Language.Changed+=languageChanged;Closed+=(_,_)=>LocalizedPresentation.Language.Changed-=languageChanged;
        reshadeInitialized=true;RenderReShade();
    }
    private async void ReShade_Click(object sender,RoutedEventArgs e)
    {
        if(AnyFileOperationBusy)return;SetPage(AppPage.ReShade);UpdateReShadeContext();await FindReShadeCacheAsync();
    }
    private void UpdateReShadeContext()
    {
        if(!reshadeInitialized)return;
        var id=(GamesCombo.SelectedItem as Game)?.AppId;
        if(reshadeContextExe!=selectedExe||reshadeContextRoot!=selectedGameRoot||reshadeContextAppId!=id){reshadeContextExe=selectedExe;reshadeContextRoot=selectedGameRoot;reshadeContextAppId=id;ResetReShadeScan();ReShadeView.ApiPicker.SelectedIndex=-1;}
        RenderReShade();
    }
    private void ResetReShadeScan()
    {
        ResetReShadeRiskConsent();
        reshadeContextRevision++;reshadeReport=null;reshadeRan=false;reshadeExitCode=null;reshadeScanPhase=ReShadeScanPhase.NotScanned;reshadeLastError="";
        ReShadeView.Consent.IsChecked=false;ReShadeView.VulkanConsent.IsChecked=false;ReShadeView.StatusText.Text="ReShade inspection is read-only. No game is launched.";
        ReShadeView.TechnicalExpander.IsExpanded=false;
    }
    private void ResetReShadeRiskConsent()
    {
        reshadeRiskApproval=null;
        ReShadeView.ProxyRiskConsent.IsChecked=false;
    }
    private void RenderReShade()
    {
        ReShadeView.EvidenceCaution.Text=LocalizedPresentation.Language.Lookup("Proxy DLL names and version strings do not prove Full Add-on Support or runtime loading.");
        ReShadeView.DetectionDetailsButton.Content=LocalizedPresentation.Language.Lookup("Detection details");
        ReShadeView.GameText.Text="Game: "+((GamesCombo.SelectedItem as Game)?.Name??"Not selected");ReShadeView.ExeText.Text="Selected EXE: "+(selectedExe??"Not selected");
        ReShadeView.DetectionText.Text=ReShadePresentation.ScanText(reshadeScanPhase,reshadeReport);
        ReShadeView.AddonText.Text=LocalizedPresentation.Language.Lookup(reshadeReport?.HasConflicts==true||reshadeReport?.AlternativeAddonPaths.Count>0?"Complete installation structure: requires review; proxy/configuration checks are unresolved.":"Complete installation structure: not independently verified.");
        ReShadeView.VersionText.Text="";
        var pathSafe=selectedExe is null||ReShadeSetupPathPolicy.CanInspect(selectedExe);
        ReShadeView.CompactSummaryText.Text=ReShadeUpdatePolicy.CompactSummary(reshadeReport,LocalizedPresentation.Language,pathSafe);
        ReShadeView.UpdateActionText.Text=pathSafe?ReShadeUpdatePolicy.Assess(reshadeReport).NextAction:"Target path needs manual review; see setup restrictions.";
        ReShadeView.FileRows.Children.Clear();foreach(var file in reshadeReport?.Files??[]){var row=new StackPanel();
            void Evidence(string value,int size){var text=ToolText(value,size);LocalizedPresentation.SetPreserveOriginal(text,true);row.Children.Add(text);}
            Evidence(Path.GetFileName(file.Path)+" · "+TechnicalVersionDisplay.Format(file.Version)+" · "+LocalizedPresentation.Language.Lookup(ReShadePresentation.KindText(file.Kind)),13);
            Evidence(LocalizedPresentation.Language.Translate(file.Reason),12);Evidence(file.Path,11);ReShadeView.FileRows.Children.Add(row);}
        if(reshadeReport?.AlternativeAddonPaths.Count>0)ReShadeView.FileRows.Children.Add(ToolText("Custom ReShade/RenoDX addon paths need manual review. Existing settings and packages are preserved."));
        if(ReShadeApi==ReShadeGraphicsApi.Vulkan)ReShadeView.FileRows.Children.Add(ToolText("Vulkan uses a shared layer. Selected-game registration and activation are Not verified by a local proxy scan.",11));
        ReShadeView.VulkanWarning.Visibility=ReShadeView.VulkanConsent.Visibility=ReShadeApi==ReShadeGraphicsApi.Vulkan?Visibility.Visible:Visibility.Collapsed;
        ReShadeView.ApiPlaceholder.Visibility=ReShadeView.ApiPicker.SelectedIndex<0?Visibility.Visible:Visibility.Collapsed;
        ReShadeView.AntiCheatWarning.Text=ReShadeUsagePolicy.GeneralWarning;
        var usage=ReShadeUsagePolicy.ForSteamAppId((GamesCombo.SelectedItem as Game)?.AppId);ReShadeView.UsageWarning.Text=usage.Message;
        ReShadeView.AntiCheatPanel.Visibility=usage.KnownAntiCheatConcern?Visibility.Visible:Visibility.Collapsed;
        ReShadeView.GeneralUsageWarning.Visibility=usage.KnownAntiCheatConcern?Visibility.Collapsed:Visibility.Visible;
        ReShadeView.ProxyRiskPanel.Visibility=reshadeReport is not null && !ReShadeManualLaunchPolicy.HasCriticalBlock(reshadeReport) && ReShadeManualLaunchPolicy.RequiresConsent(reshadeReport)?Visibility.Visible:Visibility.Collapsed;
        ReShadeView.ProxyRiskText.Text=reshadeReport is null?"":ReShadeManualLaunchPolicy.Describe(reshadeReport,LocalizedPresentation.Language);
        LocalizedPresentation.SetPreserveOriginal(ReShadeView.ProxyRiskText,true);
        RenderReShadeButtons();LocalizedPresentation.RefreshGeneratedItems(ReShadeView);
    }
    private void RenderReShadeButtons()
    {
        var busy = reshadeBusy || reshadeSession.BlocksChanges;
        var game=HasCurrentExecutable()&&selectedGameRoot is not null;
        var pathSafe=!game||ReShadeSetupPathPolicy.CanInspect(selectedExe!);
        ReShadeView.PrepareButton.IsEnabled=ReShadeView.ImportButton.IsEnabled=!busy;
        ReShadeView.ScanButton.IsEnabled=game&&!busy&&ReShadeApi!=ReShadeGraphicsApi.Unknown;
        ReShadeView.ApiPicker.IsEnabled=ReShadeView.Consent.IsEnabled=ReShadeView.VulkanConsent.IsEnabled=!busy;
        ReShadeView.ProxyRiskConsent.IsEnabled=!busy;
        ReShadeView.CancelPrepareButton.IsEnabled=reshadeDownload is not null;
        ReShadeView.FinishButton.IsEnabled = reshadeSession.CanConfirm && !reshadeCompleting;
        ReShadeView.FinishButton.Visibility = reshadeSession.CanConfirm ? Visibility.Visible : Visibility.Collapsed;
        ReShadeView.HomeButton.IsEnabled = !busy;
        var allowed=game&&pathSafe&&!busy&&reshadePrepared is not null&&reshadeScanPhase==ReShadeScanPhase.Completed&&reshadeReport is not null&&!ReShadeManualLaunchPolicy.HasCriticalBlock(reshadeReport)&&(!ReShadeManualLaunchPolicy.RequiresConsent(reshadeReport)||reshadeRiskApproval is not null)&&ReShadeApi!=ReShadeGraphicsApi.Unknown&&ReShadeView.Consent.IsChecked==true&&(ReShadeApi!=ReShadeGraphicsApi.Vulkan||ReShadeView.VulkanConsent.IsChecked==true);
        ReShadeView.RunButton.IsEnabled=allowed;
        ReShadeView.RunButton.Content=ReShadeUpdatePolicy.LaunchLabel(reshadeReport);
        ReShadeView.LaunchHint.Text=reshadeScanPhase==ReShadeScanPhase.InProgress?"Scanning ReShade files. No files are being changed.":reshadeDownload is not null?"Preparing and verifying the reviewed Full Add-on Support installer…":busy?"Close the official installer normally; do not terminate it during writes.":reshadeReport is not null&&(reshadeReport.HasConflicts||reshadeReport.AlternativeAddonPaths.Count>0)?ReShadePresentation.BlockReason(reshadeReport):!game?"Select the current game EXE.":ReShadeApi==ReShadeGraphicsApi.Unknown?"Select the graphics API; it is not inferred from DLL presence.":reshadeReport is null?ReShadePresentation.ScanText(reshadeScanPhase,null):reshadePrepared is null?"Prepare and verify the official Full Add-on Support installer.":!allowed?"Review and confirm the official installer warnings before running setup.":"Ready for guided official setup. Installation and runtime activity remain separate results.";
        ReShadeView.LaunchHint.Text=ReShadeUpdatePolicy.LocalizeAppText(ReShadeView.LaunchHint.Text,LocalizedPresentation.Language);
        if(!busy&&reshadeReport is not null)
        {
            if(ReShadeManualLaunchPolicy.HasCriticalBlock(reshadeReport)) ReShadeView.LaunchHint.Text=LocalizedPresentation.Language.Lookup(ReShadeManualLaunchPolicy.Blocked);
            else if(ReShadeManualLaunchPolicy.RequiresConsent(reshadeReport)) ReShadeView.LaunchHint.Text=LocalizedPresentation.Language.Lookup(reshadeRiskApproval is null?ReShadeManualLaunchPolicy.Consent:"Manual official setup can be opened after the required confirmations. Proxy compatibility remains unverified.");
        }
        if(reshadeCheckingCache)ReShadeView.LaunchHint.Text=LocalizedPresentation.Language.Lookup("Checking the existing installer cache. No download or game changes.");
        if(!reshadeBusy&&!pathSafe)ReShadeView.LaunchHint.Text=LocalizedPresentation.Language.Lookup(ReShadeSetupPathPolicy.BlockMessage);
    }
    private async Task RefreshReShadeAsync(bool finishing = false)
    {
        if(!reshadeBusy && (!reshadeSession.BlocksChanges || finishing))ResetReShadeRiskConsent();
        if(reshadeBusy||!finishing&&reshadeSession.BlocksChanges||selectedExe is null||selectedGameRoot is null||ReShadeApi==ReShadeGraphicsApi.Unknown)return;var exe=selectedExe;var root=selectedGameRoot;var api=ReShadeApi;var revision=reshadeContextRevision;reshadeBusy=true;reshadeScanPhase=ReShadeScanPhase.InProgress;reshadeReport=null;ReShadeView.Consent.IsChecked=false;RenderReShade();
        try{var report=await Task.Run(()=>reshadeDetection.Scan(root,exe,api));if(selectedExe!=exe||selectedGameRoot!=root||revision!=reshadeContextRevision)return;reshadeReport=report;reshadeScanPhase=ReShadeScanPhase.Completed;ReShadeView.StatusText.Text=reshadeRan && reshadeExitCode is not null and not 0?"The installer reported an error or cancellation. File evidence is shown separately; use official repair guidance."+"\n"+InstallerExitText():!reshadeRan?"Read-only ReShade scan completed. Files on disk do not prove runtime loading.":report.State==ReShadeDetectionState.FullAddonVerified?"Full Add-on Support binary verified after setup. Runtime loading is not confirmed; no managed Restore record was created.":"Official setup ended; its result is Not verified. Review file details or use official repair guidance.";}
        catch(Exception ex){if(revision==reshadeContextRevision){reshadeLastError=ex.Message;reshadeReport=null;reshadeScanPhase=ReShadeScanPhase.Failed;ReShadeView.StatusText.Text="ReShade inspection failed. See detection/package details.";}}
        finally{reshadeBusy=false;RenderReShade();}
    }
    private async Task PrepareReShadeAsync(string? local)
    {
        if(reshadeBusy||reshadeSession.BlocksChanges)return;reshadeBusy=true;reshadeDownload=new();reshadePackageError="";ReShadeView.DownloadProgress.Visibility=Visibility.Visible;ReShadeView.DownloadProgress.Value=0;ReShadeView.PackageStatusText.Text="Preparing and verifying the reviewed Full Add-on Support installer…";RenderReShadeButtons();
        try{var result=await reshadePackages.PrepareAsync(selectedGameRoot,new Progress<long>(n=>ReShadeView.DownloadProgress.Value=100d*n/ReShadeInstallerVerifier.Reviewed.Size),reshadeDownload.Token,local);reshadePrepared=result;ReShadeView.PackageStatusText.Text="Reviewed Full Add-on package SHA-256 and pinned publisher signature verified.";}
        catch(OperationCanceledException){ReShadeView.PackageStatusText.Text="ReShade preparation cancelled; no game files changed.";}
        catch(Exception ex){reshadePackageError=ex.Message;reshadePrepared=null;ReShadeView.PackageStatusText.Text="ReShade preparation failed. See package verification details.";}
        finally{reshadeDownload.Dispose();reshadeDownload=null;reshadeBusy=false;ReShadeView.DownloadProgress.Visibility=Visibility.Collapsed;RenderReShade();}
    }
    private async Task FindReShadeCacheAsync()
    {
        if(reshadeBusy||reshadeSession.BlocksChanges)return;reshadeBusy=reshadeCheckingCache=true;RenderReShadeButtons();
        try{reshadePrepared=await reshadePackages.FindCachedAsync(selectedGameRoot);reshadePackageError="";ReShadeView.PackageStatusText.Text=reshadePrepared is null?"Installer not prepared.":"Existing cached Full Add-on installer reverified. No download was needed.";}
        catch(Exception ex){reshadePrepared=null;reshadePackageError=ex.Message;ReShadeView.PackageStatusText.Text="Cached installer could not be verified. Review package Details; no replacement was downloaded.";}
        finally{reshadeBusy=reshadeCheckingCache=false;RenderReShade();}
    }
    private FrameworkElement ReShadeDetailsContent()
    {
        var body=new StackPanel();body.Children.Add(ToolText(ReShadePresentation.ScanText(reshadeScanPhase,reshadeReport),13));
        void Field(string label,string value,bool opaque=false){body.Children.Add(ToolText(label,11));var text=ToolText(value,13);if(opaque)LocalizedPresentation.SetPreserveOriginal(text,true);body.Children.Add(text);}
        foreach(var file in reshadeReport?.Files??[])
        {
            Field("Component",Path.GetFileName(file.Path),true);Field("Classification",LocalizedPresentation.Language.Lookup(ReShadePresentation.KindText(file.Kind)),true);Field("File version",TechnicalVersionDisplay.Format(file.Version),true);
            Field("Product metadata",file.ProductName,true);Field("File description",file.FileDescription,true);Field("Target",file.Path,true);Field("SHA-256",file.Sha256,true);Field("Reason",LocalizedPresentation.Language.Translate(file.Reason),true);
        }
        Field("Installation evidence",ReShadeUpdatePolicy.EvidenceLevels(reshadeReport,LocalizedPresentation.Language),true);
        if(reshadeRan)body.Children.Add(ToolText(InstallerExitText()));
        Field("Original diagnostics",string.Join("\n",(reshadeReport?.Warnings??[]).Concat(reshadeReport?.AlternativeAddonPaths??[]))+"\n"+reshadeLastError,true);
        return body;
    }
    private async Task RunReShadeAsync()
    {
        if(AnyFileOperationBusy||!ReShadeView.RunButton.IsEnabled||reshadePrepared is null||selectedExe is null||selectedGameRoot is null)return;
        if(!ShowThemeConfirmation("Run official ReShade setup?",ReShadeUsagePolicy.GeneralWarning+"\n\n"+ReShadeUsagePolicy.ForSteamAppId((GamesCombo.SelectedItem as Game)?.AppId).Message+"\n\n"+ReShadeUpdatePolicy.Describe(reshadeReport,LocalizedPresentation.Language)+"\n\n"+$"Selected EXE: {selectedExe}\nAPI: {ReShadeApi}\n"+"The official installer opens normally, with no unattended switches. Select the displayed EXE and API yourself. Its changes are outside our transaction manager. Continue?"))return;
        var exe = selectedExe;
        var root = selectedGameRoot;
        var api = ReShadeApi;
        var prepared = reshadePrepared;
        var approved = ReShadeView.Consent.IsChecked == true;
        var vulkanApproved = ReShadeView.VulkanConsent.IsChecked == true;
        var riskApproval = reshadeRiskApproval;
        reshadeLifecycle = new ReShadeSessionLog();
        var lifecycle = reshadeLifecycle;
        void Record(string value) => _ = Task.Run(() => lifecycle.Write(value));
        Record("launch-request approved");
        reshadeRan = false;
        reshadeExitCode = null;
        reshadeLastError = "";
        try
        {
            var runner = new ReShadeGuidedInstaller(reshadePackages, reshadeDetection, new OfficialReShadeInstallerProcess(lifecycle.Write), p => IsSelectedGameProcessRunning(p, out _));
            reshadeExitCode = await reshadeSession.LaunchAsync(async started =>
            {
                System.Windows.Threading.DispatcherOperation? notice = null;
                try
                {
                    var result = await Task.Run(() => runner.LaunchAsync(root, exe, api, prepared, approved, vulkanApproved,
                        () => notice = Dispatcher.InvokeAsync(() => { reshadeRan = true; started(); }),riskApproval: riskApproval));
                    return result.Code;
                }
                finally { if (notice is not null) await notice; }
            }, () => { Record($"phase {reshadeSession.Phase} locked={reshadeSession.BlocksChanges}"); RenderReShadeButtons(); });
            SetReShadeSetupStatus("The initial installer process exited. Close any remaining setup windows, then confirm and rescan; exit alone is not installation proof.");
        }
        catch (Exception ex)
        {
            Record($"launch-or-wait-error type={ex.GetType().Name} code={ex.HResult:X8}");
            reshadeLastError = ex.Message;
            SetReShadeSetupStatus(reshadeSession.CanConfirm
                ? "Guided ReShade setup stopped or failed. Review details and rescan; no automatic rollback is claimed."
                : ex.Message is ReShadeManualLaunchPolicy.Changed or ReShadeManualLaunchPolicy.Blocked
                    ? ex.Message
                    : "Official ReShade Setup could not start. Review the detection details and retry.");
        }
        finally
        {
            ReShadeView.Consent.IsChecked = false;
            ResetReShadeRiskConsent();
            RenderReShade();
        }
        if (reshadeSession.CanConfirm && reshadeExitCode.HasValue)
        {
            var quiet = await reshadeSession.WaitForQuietExitAsync(
                () => Task.Run(() => OfficialReShadeInstallerProcess.MayStillBeRunning(prepared.Path, lifecycle.Write)),
                () => Task.Delay(1250));
            if (quiet && ReferenceEquals(lifecycle, reshadeLifecycle))
                await CompleteReShadeAsync("automatic");
        }
    }

    private Task FinishReShadeAsync() => CompleteReShadeAsync("manual");

    private async Task CompleteReShadeAsync(string reason)
    {
        if (reshadeCompleting || !reshadeSession.CanConfirm || reshadePrepared is not {} prepared) return;
        reshadeCompleting = true;
        RenderReShadeButtons();
        var lifecycle = reshadeLifecycle;
        const string warning = "Close all official installer windows, including elevated child windows, before rescanning. Our Restore does not control their changes.";
        try
        {
            await Task.Run(() => lifecycle.Write($"completion-request {reason}"));
            if (await Task.Run(() => OfficialReShadeInstallerProcess.MayStillBeRunning(prepared.Path, lifecycle.Write)))
            {
                SetReShadeSetupStatus(warning);
                return;
            }
            if (reason == "manual")
            {
                await Task.Delay(1250);
                if (await Task.Run(() => OfficialReShadeInstallerProcess.MayStillBeRunning(prepared.Path, lifecycle.Write)))
                { SetReShadeSetupStatus(warning); return; }
            }
            if (!await reshadeSession.FinishAsync(
                () => OfficialReShadeInstallerProcess.MayStillBeRunning(prepared.Path),
                async () =>
                {
                    await Task.Run(() => lifecycle.Write("rescan-start"));
                    await RefreshReShadeAsync(finishing: true);
                    await Task.Run(() => lifecycle.Write($"rescan-result {reshadeScanPhase}"));
                },
                () =>
                {
                    var state = $"phase {reshadeSession.Phase} locked={reshadeSession.BlocksChanges}";
                    _ = Task.Run(() => lifecycle.Write(state));
                    RenderReShadeButtons();
                }))
                SetReShadeSetupStatus(warning);
        }
        finally
        {
            reshadeCompleting = false;
            RenderReShadeButtons();
        }
    }

    private void SetReShadeSetupStatus(string key) =>
        ReShadeView.StatusText.Text = LocalizedPresentation.Language.Lookup(key);

    private string InstallerExitText() => $"Official installer exit code: {reshadeExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Exit status unavailable"}. This does not establish installation success.";
}

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private NvidiaManualDllView? manualDllView;
    private bool manualDllMode,manualDllBusy,manualDllRendering,manualDllAll,manualDllPreview;
    private string? manualDllExe,manualDllRoot,manualDllTarget;
    private NvidiaManualPackage? manualDllPackage;
    private NvidiaManualPlan? manualDllPlan;
    private CancellationTokenSource? manualDllCancel;
    private readonly HashSet<string> manualDllSelection=new(StringComparer.OrdinalIgnoreCase);
    private FrameworkElement NvidiaModePicker(bool manual)
    {
        var panel=new WrapPanel();foreach(var pair in new[]{("Verified Update",false),("Manual DLL Installation",true)}){
            var button=new Button{Content=pair.Item1,Padding=new Thickness(14,9,14,9),Margin=new Thickness(0,0,8,5),Background=SetupWizardView.Brush(manual==pair.Item2?"#392950":"#20212B")};
            button.Click+=(_,_)=>{if(manualDllBusy||AnyFileOperationBusy)return;if(pair.Item2)OpenManualNvidia();else{
                manualDllMode=false;if(activeSetupWizard is not null){DllInstallationPage.Content=activeSetupWizard;resumeSetupWizard?.Invoke();SetPage(AppPage.DllInstallation);}else SetupWizard_Click(this,new RoutedEventArgs());
            }};panel.Children.Add(button);
        }return panel;
    }
    private NvidiaSdkManualManager ManualManager()=>new(streamlineDownloads.DownloadsDirectory,Path.Combine(streamlineDownloads.StorageRoot,"ManualSdk","Staging"),CreateBackupService(selectedGameRoot??throw new InvalidDataException("Select the current game EXE.")));
    private void OpenManualNvidia()
    {
        if(AnyFileOperationBusy||manualDllBusy)return;invalidateSetupWizard?.Invoke();manualDllMode=true;InvalidateManualNvidia();
        if(manualDllView is null){
            var view=manualDllView=new NvidiaManualDllView();view.ModePanel.Children.Add(NvidiaModePicker(true));
            view.DisclaimerText.Text=NvidiaSdkManualManager.Disclaimer;view.Consent.Content=NvidiaSdkManualManager.Consent;
            view.GamePicker.ItemsSource=games;view.GamePicker.Template=GamesCombo.Template;view.SdkPicker.Template=GamesCombo.Template;
            LocalizedPresentation.SetPreserveOriginal(view.GamePicker,true);
            view.GamePicker.SelectionChanged+=(_,_)=>{if(!manualDllRendering&&view.GamePicker.SelectedItem is Game game&&!ReferenceEquals(game,GamesCombo.SelectedItem)){GamesCombo.SelectedItem=game;SyncManualNvidiaContext();RenderManualNvidia();}};
            view.SdkPicker.SelectionChanged+=(_,_)=>{if(manualDllRendering)return;manualDllPackage=null;manualDllSelection.Clear();InvalidateManualNvidia();RenderManualNvidia();};
            view.BrowseExeButton.Click+=(_,_)=>{
                var dialog=new OpenFileDialog{Title=LocalizedPresentation.Language.Lookup("Choose the actual game executable"),Filter="EXE (*.exe)|*.exe"};if(ShowNativeDialog(dialog,this)!=true)return;
                if(selectedGameRoot is null||!IsPathWithinRoot(selectedGameRoot,dialog.FileName)){
                    if(!ShowThemeConfirmation("Confirm manual game root","Use this EXE directory as the allowed game root?\n"+dialog.FileName))return;
                    var game=new Game("Manual · "+Path.GetFileNameWithoutExtension(dialog.FileName),Path.GetDirectoryName(dialog.FileName)!,"manual");games.Add(game);GamesCombo.SelectedItem=game;
                }
                SetExe(dialog.FileName);RememberConfirmedExecutable();SyncManualNvidiaContext();RenderManualNvidia();
            };
            view.TargetButton.Click+=(_,_)=>{
                if(!HasCurrentExecutable()||selectedGameRoot is null)return;var dialog=new OpenFolderDialog{Title=LocalizedPresentation.Language.Lookup("Explicitly choose the runtime destination inside the selected game"),InitialDirectory=Path.GetDirectoryName(selectedExe!)};
                if(ShowNativeDialog(dialog,this)!=true)return;try{ManualManager().ValidateDestination(selectedGameRoot,selectedExe!,dialog.FolderName);
                    if(!ShowThemeConfirmation("Confirm runtime destination",dialog.FolderName+"\n\n"+NvidiaSdkManualManager.Disclaimer))return;
                    manualDllTarget=dialog.FolderName;InvalidateManualNvidia();RenderManualNvidia();
                }catch(Exception ex){view.StatusText.Text=ex.Message;}
            };
            view.RefreshButton.Click+=async(_,_)=>await RunManualNvidia(async token=>{
                var releases=await streamlineApi.GetReleasesAsync(token);var assets=releases.SelectMany(r=>r.Assets).Where(a=>a.Sha256 is {Length:64}).Select(a=>new WizardSdkChoice(a,$"{TechnicalVersionDisplay.Format(a.ReleaseTag)} · {a.Name} · {UiNumber(a.Size)} bytes")).ToArray();
                manualDllPackage=null;manualDllSelection.Clear();InvalidateManualNvidia();manualDllRendering=true;view.SdkPicker.ItemsSource=assets;view.SdkPicker.SelectedIndex=-1;manualDllRendering=false;
                view.StatusText.Text=assets.Length==0?"No official digest-verified SDK releases are available.":"Select an official SDK; no runtime files are selected automatically.";
            });
            view.PrepareButton.Click+=async(_,_)=>await PrepareManualNvidia(null);
            view.ImportButton.Click+=async(_,_)=>{var dialog=new OpenFileDialog{Title=LocalizedPresentation.Language.Lookup("Select the matching official SDK ZIP"),Filter="ZIP (*.zip)|*.zip",CheckFileExists=true};if(ShowNativeDialog(dialog,this)==true)await PrepareManualNvidia(dialog.FileName);};
            view.CommonButton.Click+=(_,_)=>{manualDllAll=false;manualDllPreview=false;RenderManualNvidia();};view.AllButton.Click+=(_,_)=>{manualDllAll=true;manualDllPreview=false;RenderManualNvidia();};view.SelectionButton.Click+=(_,_)=>{manualDllPreview=false;RenderManualNvidia();};
            view.PreviewButton.Click+=async(_,_)=>await RunManualNvidia(async token=>{
                SyncManualNvidiaContext();if(manualDllPackage is null||manualDllTarget is null||selectedExe is null||selectedGameRoot is null)throw new InvalidDataException("Prepare a verified SDK and explicitly confirm the current EXE destination first.");
                var root=selectedGameRoot;var exe=selectedExe;var target=manualDllTarget;
                var plan=await ManualManager().PreviewAsync(manualDllPackage,root,exe,target,manualDllSelection,view.Consent.IsChecked==true,token);
                if(selectedExe!=exe||selectedGameRoot!=root||manualDllTarget!=target)throw new InvalidDataException("Game selection changed; refresh the manual DLL Preview.");
                manualDllPlan=plan;manualDllPreview=true;RenderManualNvidia();
            });
            view.Consent.Checked+=(_,_)=>UpdateManualConsent();view.Consent.Unchecked+=(_,_)=>UpdateManualConsent();
            view.ApplyButton.Click+=async(_,_)=>{
                SyncManualNvidiaContext();
                if(manualDllPlan is not {CanApply:true} reviewed||manualDllPackage is not {} package||!view.ApplyButton.IsEnabled||!ManualContextMatches(reviewed)||view.Consent.IsChecked!=true)return;
                if(!ShowThemeConfirmation("Apply reviewed runtime files?",NvidiaSdkManualManager.Disclaimer+"\n\n"+string.Join("\n",reviewed.Entries.Where(r=>r.IsSelected).Select(r=>PlanAction(r)+": "+r.TargetPath))))return;
                await RunManualNvidia(async token=>{
                    if(!ManualContextMatches(reviewed)||!ReferenceEquals(package,manualDllPackage)||!manualDllSelection.SetEquals(reviewed.Selection))throw new InvalidDataException("Game selection changed; refresh the manual DLL Preview.");
                    var result=await ManualManager().ApplyAsync(package,reviewed,new Progress<int>(n=>{view.Progress.IsIndeterminate=false;view.Progress.Value=n;}),token);
                    InvalidateManualNvidia();view.StatusText.Text=$"Files installed and hashes verified. Transaction {result.Transaction.TransactionId}: {result.Transaction.Status}.";RenderManualNvidia();
                });
            };
            view.RestoreButton.Click+=async(_,_)=>{if(selectedGameRoot is null)return;await RunManualNvidia(async _=>{await ShowRestoreManagerAsync(selectedGameRoot,CreateBackupService(selectedGameRoot),this);InvalidateManualNvidia();RenderManualNvidia();});};
            view.CancelButton.Click+=(_,_)=>{if(manualDllBusy)manualDllCancel?.Cancel();else{manualDllMode=false;SetPage(AppPage.Home);}};
            EventHandler language=(_,_)=>{if(Dispatcher.CheckAccess())RenderManualNvidia();else Dispatcher.BeginInvoke(new Action(RenderManualNvidia));};LocalizedPresentation.Language.Changed+=language;Closed+=(_,_)=>LocalizedPresentation.Language.Changed-=language;
        }
        DllInstallationPage.Content=manualDllView;SyncManualNvidiaContext();RenderManualNvidia();SetPage(AppPage.DllInstallation);
    }
    private void SyncManualNvidiaContext()
    {
        if(manualDllExe==selectedExe&&manualDllRoot==selectedGameRoot)return;
        manualDllExe=selectedExe;manualDllRoot=selectedGameRoot;manualDllTarget=null;manualDllSelection.Clear();InvalidateManualNvidia();
    }
    private bool ManualContextMatches(NvidiaManualPlan plan)=>selectedExe is not null&&selectedGameRoot is not null&&manualDllTarget is not null
        &&Path.GetFullPath(selectedExe).Equals(plan.Executable,StringComparison.OrdinalIgnoreCase)
        &&StreamlineStagingEvidenceService.Canonical(selectedGameRoot).Equals(StreamlineStagingEvidenceService.Canonical(plan.Root),StringComparison.OrdinalIgnoreCase)
        &&StreamlineStagingEvidenceService.Canonical(manualDllTarget).Equals(StreamlineStagingEvidenceService.Canonical(plan.Destination),StringComparison.OrdinalIgnoreCase);
    private void InvalidateManualNvidia()
    {
        manualDllPlan=null;manualDllPreview=false;
        if(manualDllView is {} view){manualDllRendering=true;view.Consent.IsChecked=false;manualDllRendering=false;view.ApplyButton.IsEnabled=false;}
    }
    private void UpdateManualConsent(){if(manualDllRendering||manualDllView is null)return;if(manualDllPlan is {} plan)manualDllPlan=NvidiaSdkManualManager.Acknowledge(plan,manualDllView.Consent.IsChecked==true);RenderManualButtons();}
    private async Task RunManualNvidia(Func<CancellationToken,Task> action)
    {
        if(manualDllBusy||AnyFileOperationBusy)return;var view=manualDllView!;manualDllBusy=wizardOperationBusy=true;manualDllCancel=new();view.Progress.Visibility=Visibility.Visible;view.Progress.IsIndeterminate=true;RenderManualButtons();
        try{await action(manualDllCancel.Token);}
        catch(OperationCanceledException){InvalidateManualNvidia();view.StatusText.Text="Cancelled. Completed writes are rolled back where safe; inspect Restore if an error is reported.";}
        catch(Exception ex){InvalidateManualNvidia();view.StatusText.Text="Stopped: "+ex.Message;Log("Manual NVIDIA DLLs: "+ex.Message);}
        finally{manualDllCancel.Dispose();manualDllCancel=null;manualDllBusy=wizardOperationBusy=false;view.Progress.Visibility=Visibility.Collapsed;SyncManualNvidiaContext();RenderManualNvidia();}
    }
    private async Task PrepareManualNvidia(string? local)=>await RunManualNvidia(async token=>{
        var view=manualDllView!;var asset=(view.SdkPicker.SelectedItem as WizardSdkChoice)?.Asset??throw new InvalidDataException("Select official release metadata first.");
        if(local is not null)await StreamlineLocalArchiveImporter.ImportAsync(local,streamlineDownloads.DownloadsDirectory,asset,token);
        var result=await streamlineDownloads.DownloadAsync(asset,new Progress<StreamlineDownloadProgress>(p=>view.StatusText.Text=$"Download / verify {asset.Name}: {UiNumber(p.BytesReceived)} / {UiNumber(asset.Size)} bytes"),token);
        // Inventory preparation is independent of game selection and never writes to a game.
        var manager=new NvidiaSdkManualManager(streamlineDownloads.DownloadsDirectory,Path.Combine(streamlineDownloads.StorageRoot,"ManualSdk","Staging"),new BackupRestoreService(Path.Combine(streamlineDownloads.StorageRoot,"ManualSdk","UnusedTransactions")));
        manualDllPackage=await manager.PrepareAsync(asset,result.FilePath,token);manualDllSelection.Clear();InvalidateManualNvidia();view.StatusText.Text="SDK inventory prepared. Select individual files, then verify selected DLLs.";
    });
    private void RenderManualButtons()
    {
        if(manualDllView is not {} v)return;foreach(var c in new Control[]{v.GamePicker,v.BrowseExeButton,v.TargetButton,v.SdkPicker,v.RefreshButton,v.ImportButton,v.CommonButton,v.AllButton,v.SelectionButton,v.PreviewButton,v.Consent,v.RestoreButton})c.IsEnabled=!manualDllBusy;
        v.ModePanel.IsEnabled=v.FileRows.IsEnabled=!manualDllBusy;v.PrepareButton.IsEnabled=!manualDllBusy&&v.SdkPicker.SelectedItem is WizardSdkChoice;v.TargetButton.IsEnabled=!manualDllBusy&&HasCurrentExecutable();v.PreviewButton.IsEnabled=!manualDllBusy&&manualDllPackage is not null&&manualDllTarget is not null&&manualDllSelection.Count>0;
        v.ApplyButton.IsEnabled=!manualDllBusy&&manualDllPlan is {CanApply:true} plan&&ManualContextMatches(plan);v.RestoreButton.IsEnabled=!manualDllBusy&&selectedGameRoot is not null;v.CancelButton.Content=manualDllBusy?"Cancel operation":"Close";
        var l=LocalizedPresentation.Language;v.WorkflowText.Text=l.Lookup(NvidiaManualWorkflow.Steps);v.ApplyReasonText.Text=l.Lookup(NvidiaManualWorkflow.DisabledReason(manualDllPlan,manualDllPlan is {} current&&ManualContextMatches(current),manualDllBusy));
        v.PreviewButton.Background=SetupWizardView.Brush(manualDllPlan is null&&manualDllSelection.Count>0?"#704BD0":"#20222C");LocalizedPresentation.SetPreserveOriginal(v.WorkflowText,true);LocalizedPresentation.SetPreserveOriginal(v.ApplyReasonText,true);
    }
    private void RenderManualNvidia()
    {
        if(manualDllView is not {} v)return;SyncManualNvidiaContext();var l=LocalizedPresentation.Language;manualDllRendering=true;v.GamePicker.SelectedItem=GamesCombo.SelectedItem;manualDllRendering=false;
        v.ExeText.Text=l.Lookup("Selected EXE")+": "+(selectedExe??l.Lookup("Not selected"));v.TargetText.Text=manualDllTarget??l.Lookup("Not confirmed");
        v.PackageText.Text=manualDllPackage is {} package?l.Format("SDK {0} · {1} production DLLs · official archive and NVIDIA signatures checked",TechnicalVersionDisplay.Format(package.Asset.ReleaseTag),package.Components.Count):l.Lookup("Prepare the selected SDK to discover its actual runtime DLLs. SDK and DLL versions are different values.");
        v.FileRows.Children.Clear();
        if(manualDllPreview&&manualDllPlan is {} plan){foreach(var row in plan.Entries){var source=manualDllPackage!.Components.Single(c=>c.FileName==row.FileName);v.FileRows.Children.Add(ManualRow(source,row));}}
        else if(manualDllPackage is {} prepared){foreach(var component in prepared.Components.Where(c=>manualDllAll||c.Common))v.FileRows.Children.Add(ManualRow(component,null));}
        v.SummaryText.Text=manualDllPlan is {} summary?l.Format("Add: {0} · Replace: {1} · No change: {2} · Blocked: {3}",summary.Entries.Count(r=>r.Action=="New file"),summary.Entries.Count(r=>r.Action=="Replace existing"),summary.Entries.Count(r=>r.Action=="No change"),summary.Entries.Count(r=>r.Action=="Blocked")):l.Format("Selected files: {0}. Preview not prepared.",manualDllSelection.Count);
        v.WarningsText.Text=manualDllPlan is {} warnings?string.Join("\n\n",warnings.Warnings.Select(l.Translate)):l.Lookup(NvidiaSdkManualManager.Disclaimer);
        foreach(var text in new[]{v.ExeText,v.TargetText,v.PackageText,v.SummaryText,v.WarningsText})LocalizedPresentation.SetPreserveOriginal(text,true);
        RenderManualButtons();LocalizedPresentation.RefreshGeneratedItems(v);
    }
    private FrameworkElement ManualRow(NvidiaSdkComponent component,StreamlineComponentPlanEntry? plan)
    {
        var l=LocalizedPresentation.Language;var grid=new Grid();foreach(var width in new[]{new GridLength(28),new GridLength(2.3,GridUnitType.Star),new GridLength(1,GridUnitType.Star),new GridLength(1,GridUnitType.Star),GridLength.Auto})grid.ColumnDefinitions.Add(new(){Width=width});
        var checkbox=new CheckBox{IsChecked=manualDllSelection.Contains(component.FileName),IsEnabled=component.Eligible&&plan is null,VerticalAlignment=VerticalAlignment.Center,ToolTip=l.Lookup(component.Reason)};grid.Children.Add(checkbox);
        void Select(bool enabled){if(enabled)manualDllSelection.Add(component.FileName);else manualDllSelection.Remove(component.FileName);InvalidateManualNvidia();RenderManualNvidia();}
        checkbox.Checked+=(_,_)=>Select(true);checkbox.Unchecked+=(_,_)=>Select(false);
        var identity=new StackPanel();identity.Children.Add(ToolText(component.FileName,13));identity.Children.Add(ToolText(l.Lookup(component.Category),11));Grid.SetColumn(identity,1);grid.Children.Add(identity);
        var source=ToolText(l.Lookup("New Version")+": "+ManualDllVersion(component.Version)+"\n"+(component.Architecture=="x64"?"x64":l.Lookup(component.Architecture)),11);Grid.SetColumn(source,2);grid.Children.Add(source);
        string current="Not checked";if(plan is not null)current=plan.ExistingVersion;else if(manualDllTarget is not null){try{var path=Path.Combine(manualDllTarget,component.FileName);StreamlineStagingEvidenceService.EnsureNoReparsePoints(manualDllRoot!,path);current=File.Exists(path)?TechnicalVersionDisplay.Format(FileVersionInfo.GetVersionInfo(path).FileVersion):"Not found";if(string.IsNullOrWhiteSpace(current))current="Unknown";}catch{current="Not checked";}}
        var state=ToolText(l.Lookup("Current Version")+": "+ManualDllVersion(current)+"\n"+l.Lookup(plan is null?component.Eligible?"Available":"Blocked":ManualDllAction(plan)),11);Grid.SetColumn(state,3);grid.Children.Add(state);
        var details=new Button{Content="Details",Padding=new Thickness(8,6,8,6),VerticalAlignment=VerticalAlignment.Center};details.Click+=(_,_)=>ShowManualDetails(component,plan);Grid.SetColumn(details,4);grid.Children.Add(details);
        return SetupWizardView.Card(grid,10);
    }
    private void ShowManualDetails(NvidiaSdkComponent component,StreamlineComponentPlanEntry? plan)
    {
        var l=LocalizedPresentation.Language;var body=new StackPanel();void Field(string label,string value,bool opaque=false){body.Children.Add(ToolText(l.Lookup(label),11));var text=ToolText(opaque?value:l.Translate(value),13);LocalizedPresentation.SetPreserveOriginal(text,true);body.Children.Add(text);}
        Field("Component",component.FileName,true);Field("SDK version",TechnicalVersionDisplay.Format(manualDllPackage!.Asset.ReleaseTag),true);Field("Component type",component.Category);Field("New Version",ManualDllVersion(component.Version),true);Field("Architecture",component.Architecture=="x64"?"x64":l.Lookup(component.Architecture),true);Field("Source",component.SourcePath,true);Field("Official source",manualDllPackage.Asset.DownloadUrl,true);Field("Source SHA-256",component.Sha256,true);Field("Digital signature",component.Signature);Field("Reason",component.Reason);Field("Product metadata",component.Product,true);Field("File description",component.Description,true);
        if(plan is not null){Field("Target",plan.TargetPath,true);Field("Current Version",ManualDllVersion(plan.ExistingVersion),true);Field("Existing SHA-256",plan.ExistingSha256??l.Lookup("Not applicable"),true);Field("Action",ManualDllAction(plan));Field("Reason",plan.Message);}
        CreateInfoDialog("Manual DLL details",body,760,700).ShowDialog();
    }
    internal static string ManualDllAction(StreamlineComponentPlanEntry row)=>row.Action=="No change"?"No Change":PlanAction(row);
    internal static string ManualDllVersion(string value,UiLocalization? language=null)=>value is "Unknown" or "Not found" or "Not checked"?(language??LocalizedPresentation.Language).Lookup(value):TechnicalVersionDisplay.Format(value);
}

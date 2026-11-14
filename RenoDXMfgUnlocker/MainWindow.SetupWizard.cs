using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    private SetupWizardView? activeSetupWizard;
    private Action? resumeSetupWizard;
    private Action? invalidateSetupWizard;
    private void SetupWizard_Click(object sender, RoutedEventArgs e)
    {
        if(AnyFileOperationBusy || streamlineBusy || intelSdkBusy)return;
        if(manualDllMode){OpenManualNvidia();return;}
        if(activeSetupWizard is not null) {resumeSetupWizard?.Invoke();SetPage(AppPage.DllInstallation);return;}
        var wizard=this;
        var view=new SetupWizardView();var content=view.ContentPanel;var scroll=view.ContentScroll;var status=view.StatusText;var footer=view.FooterPanel;
        view.SubtitleText.Text="Review the exact runtime files before approving any changes. Compatibility is not inferred from disk files.";
        view.SettingsButton.Click+=ApplicationSettings_Click;
        Button Btn(string text,Action action) { var b=new Button{Content=text,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,4,8,0),HorizontalAlignment=HorizontalAlignment.Left};b.Click+=(_,_)=>action();return b; }
        var back=Btn("Back",()=>{});var cancel=Btn("Close",()=>{});var next=Btn("Next",()=>{});
        footer.Children.Add(back);footer.Children.Add(cancel);footer.Children.Add(next);
        var prerequisite=new TextBlock {Name="WizardPrerequisiteHint",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=SetupWizardView.Brush("#C7B5DE"),Margin=new Thickness(14,4,8,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(prerequisite,2);footer.Children.Add(prerequisite);
        Grid.SetColumn(cancel,1);Grid.SetColumn(next,3);next.MinWidth=180;back.MinWidth=90;cancel.MinWidth=90;next.Margin=new Thickness(12,4,0,0);
        next.Background=new LinearGradientBrush(Color.FromRgb(131,77,227),Color.FromRgb(91,56,182),0);
        view.UseEmbeddedLayout();DllInstallationPage.Content=view;
        int step=0;bool busy=false;bool intel=false;bool inputsConfirmed=false;bool consent=false;bool targetNeedsReview=false;
        var integration=IntelIntegration.D3D12;bool vulkanRequirementsConfirmed=false;bool intelCacheFailed=false;
        string? target=null;string? targetConfirmedForExe=null;IntelInstallPlan? intelPlan=null;StreamlineComponentPlan? nvidiaPlan=null;
        VerifiedIntelRuntime? intelPackage=null;VerifiedStreamlineStage? stage=null;VerifiedDlssGProvider? provider=null;
        StreamlineSdkAsset? asset=null;CancellationTokenSource? operation=null;
        var intelService=new IntelRuntimeService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","Intel"));
        var gamePicker=new ComboBox {ItemsSource=games,DisplayMemberPath="Name",SelectedItem=GamesCombo.SelectedItem,Margin=new Thickness(0,6,0,10),HorizontalAlignment=HorizontalAlignment.Stretch};
        var sdkPicker=new ComboBox {DisplayMemberPath="Label",Margin=new Thickness(0,10,0,10),HorizontalAlignment=HorizontalAlignment.Stretch};
        LocalizedPresentation.SetPreserveOriginal(gamePicker,true);
        gamePicker.Template=GamesCombo.Template;sdkPicker.Template=GamesCombo.Template;gamePicker.Height=40;sdkPicker.Height=40;gamePicker.HorizontalAlignment=HorizontalAlignment.Stretch;sdkPicker.HorizontalAlignment=HorizontalAlignment.Stretch;
        foreach(var picker in new[]{gamePicker,sdkPicker}) {
            var itemStyle=new Style(typeof(ComboBoxItem));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(25,27,35))));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,7,10,7)));
            picker.ItemContainerStyle=itemStyle;
        }
        string resultText="";bool recoveryReviewed=false;bool showBackupCopies=false;
        TextBlock Text(string value,int size=12)=>new(){Text=value,TextWrapping=TextWrapping.Wrap,FontSize=size,Margin=new Thickness(0,5,12,8),Foreground=(Brush)FindResource("Text")};
        void AddText(string value,int size=12)=>content.Children.Add(Text(value,size));
        bool InputsReady()=>inputsConfirmed && (!intel || integration!=IntelIntegration.VulkanExperimental || vulkanRequirementsConfirmed);
        void RefreshNext(){next.IsEnabled=SetupWizardView.CanContinue(step,busy,HasCurrentExecutable(),InputsReady()&&CurrentTarget(),intel||asset is not null,(intel?intelPlan is not null:nvidiaPlan is {CanInstall:true})&&consent);prerequisite.Text=step==2&&!busy?WizardScanPresentation.MissingRequirement(HasCurrentExecutable(),InputsReady(),CurrentTarget(),targetNeedsReview):"";}
        void ResetTargetConfirmation(){targetConfirmedForExe=null;targetNeedsReview=true;}
        void NumberedSection(int number,string title){var row=new Grid {Margin=new Thickness(0,8,0,4)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());var badge=Text(number.ToString(),12);badge.Margin=new Thickness(0);badge.VerticalAlignment=VerticalAlignment.Center;badge.HorizontalAlignment=HorizontalAlignment.Center;row.Children.Add(new Border {Child=badge,Width=26,Height=26,CornerRadius=new CornerRadius(13),Background=SetupWizardView.Brush("#39274F"),BorderBrush=SetupWizardView.Brush("#8E65C3"),BorderThickness=new Thickness(1)});var label=Text(title,14);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(9,0,0,0);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);content.Children.Add(row);}
        void Invalidate(){intelPlan=null;nvidiaPlan=null;consent=false;RefreshNext();if(step>=2)status.Text="Plan invalidated. Reconfirm integration and target requirements, then prepare a fresh Preview.";}
        void Context(){
            AddText($"Game: {(GamesCombo.SelectedItem as Game)?.Name ?? "Not selected"}\nEXE: {(selectedExe is null?"Not selected":Path.GetFileName(selectedExe))}\nTarget: {target ?? "Not confirmed"}");
            if(step is 1 or 2){AddText("Selected EXE: "+(selectedExe??"Not selected"),13);AddText("EXE directory: "+(selectedExe is null?"Not selected":Path.GetDirectoryName(selectedExe)),12);}
            content.Children.Add(Btn("Game / location details",()=>ShowComponentDetails("Game / location details",$"Game: {(GamesCombo.SelectedItem as Game)?.Name ?? "Not selected"}\nRoot: {selectedGameRoot ?? "Not selected"}\nEXE: {selectedExe ?? "Not selected"}\nTarget: {target ?? "Not confirmed"}",wizard)));
        }
        bool CurrentTarget()=>selectedExe is not null && target is not null && targetConfirmedForExe==selectedExe;
        string? renderedExe=null;
        void CloseWizard(){if(busy)return;DllInstallationPage.Content=null;activeSetupWizard=null;resumeSetupWizard=null;invalidateSetupWizard=null;SetPage(AppPage.Home);}
        void Render()
        {
            // The same pickers are reused after leaving a section card.
            foreach(var picker in new[]{gamePicker,sdkPicker})if(picker.Parent is Panel parent)parent.Children.Remove(picker);
            renderedExe=selectedExe;
            content.Children.Clear();view.ApprovalPanel.Children.Clear();view.ReviewActionsPanel.Children.Clear();view.SetReviewLayout(step==4);scroll.ScrollToTop();
            view.Navigate(step,false,requested=>{if(!SetupWizardView.CanVisit(step,requested,busy)||requested==step)return;step=requested;Invalidate();Render();});
            view.Summary(intel?intelPlan?.Entries:nvidiaPlan?.Entries,target,(GamesCombo.SelectedItem as Game)?.Name);
            var currentRows=intel?intelPlan?.Entries:nvidiaPlan?.Entries;
            view.SubtitleText.Text=step==4&&currentRows is {Count:>0}&&currentRows.All(e=>!e.IsSelected&&e.ExistingSha256 is not null&&e.ExistingSha256.Equals(e.Sha256,StringComparison.OrdinalIgnoreCase))?"No changes needed. Identical files are not rewritten; runtime activity remains unverified.":"Review the exact runtime files before approving any changes. Compatibility is not inferred from disk files.";
            status.Text="";back.IsEnabled=step>0 && !busy;next.IsEnabled=!busy;
            next.Visibility=step==5?Visibility.Collapsed:Visibility.Visible;
            next.Content=step switch {0=>"Scan game",1=>"Choose runtime",2=>"Continue",3=>"Download / Prepare",4=>"Apply reviewed changes",_=>"Next"};
            cancel.Content=busy?"Cancel operation":"Close";
            if(step is not 2 and not 4)Context();
            if(step==0)content.Children.Insert(0,NvidiaModePicker(false));
            if(step==0) {
                AddText("Use a discovered Steam game or browse to the actual running game EXE. The EXE is never launched by this wizard.");
                AddText("Graphics API: not detected. Runtime requirements must be explicitly confirmed.",11);
                content.Children.Add(gamePicker);
                content.Children.Add(Btn("Browse game EXE",()=>{
                    var file=new OpenFileDialog {Title="Choose the actual game executable",Filter="Game executable (*.exe)|*.exe",CheckFileExists=true};
                    if(ShowNativeDialog(file,wizard)!=true) return;
                    if(selectedGameRoot is null || !IsPathWithinRoot(selectedGameRoot,file.FileName)) {
                        if(!ShowThemeConfirmation("Confirm manual game root", "Use this EXE directory as the allowed game root?\n"+file.FileName,wizard)) return;
                        var game=new Game("Manual · "+Path.GetFileNameWithoutExtension(file.FileName),Path.GetDirectoryName(file.FileName)!,"manual");games.Add(game);GamesCombo.SelectedItem=game;gamePicker.SelectedItem=game;
                    }
                    SetExe(file.FileName);RememberConfirmedExecutable();target=null;ResetTargetConfirmation();inputsConfirmed=false;vulkanRequirementsConfirmed=false;Invalidate();Render();
                }));
            } else if(step==1) {
                AddText("Read-only inventory. Backup copies are labelled; files on disk are not proof of loaded runtime or compatibility.");
                if(currentNvidiaScanResult is {} scan) {
                    var backups=new CheckBox {Content="Show backup copies",IsChecked=showBackupCopies,Margin=new Thickness(0,4,0,10)};
                    backups.Checked+=(_,_)=>{showBackupCopies=true;Render();};backups.Unchecked+=(_,_)=>{showBackupCopies=false;Render();};content.Children.Add(backups);
                    var display=NvidiaComponentDisplay.Create(scan,showBackupCopies);AddText(display.Summary);
                    AddText("Only the confirmed EXE destination can enter Preview; other discovered files are diagnostic evidence, not loaded-module proof.",11);
                    foreach(var heading in new[]{WizardScanPresentation.Selected,WizardScanPresentation.Other,WizardScanPresentation.Copies,WizardScanPresentation.Missing}){
                        var entries=display.VisibleEntries.Where(entry=>WizardScanPresentation.Group(entry,selectedExe)==heading).ToArray();if(entries.Length==0)continue;
                        AddText(heading,14);
                        if(heading==WizardScanPresentation.Selected)AddText("Potential destination only; explicit confirmation and safety checks are still required.",11);
                        content.Children.Add(CompactTable(["Component","Installed version","Status","Location","Details"],entries.Select(entry=>(new[]{entry.Name,entry.Version,entry.Status,entry.DisplayLocation,"Details"},(Action)(()=>ShowComponentDetails(entry.Name,ScanDetails(entry),wizard))))));
                    }
                    foreach(var warning in scan.Warnings.Concat(scan.PotentialConflicts)) AddText(warning,11);
                }
            } else if(step==2) {
                NumberedSection(1,"Choose runtime");
                content.Children.Add(view.RuntimeCards(intel,isIntel=>{if(intel==isIntel)return;intel=isIntel;inputsConfirmed=false;vulkanRequirementsConfirmed=false;ResetTargetConfirmation();stage=null;provider=null;intelPackage=null;Invalidate();Render();}));
                if(!intel)content.Children.Add(NvidiaModePicker(false));
                AddText(intel?"Intel uses the pinned 1.3.1.78 / 1.3.2.10 pair. NVIDIA DLSS-G is not required. D3D12 needs a supported input; experimental Vulkan additionally requires addon 1.7.0 or newer, ReShade Vulkan addon support and game DLSS SR/RR inputs.":"NVIDIA: maintains an existing Streamline 2.x integration. Documented pairs: 2.14.1 / 310.9.1 and 2.14.16 / 310.9.16. Unknown plugins/dependencies block installation.");
                NumberedSection(2,"Confirm integration");
                if(intel){
                    AddText("Integration path / Graphics API",13);
                    AddText("Both integration paths use the same pinned Intel DLL pair. The API changes compatibility requirements, not the runtime package.",11);
                    var apiPicker=new ComboBox {Template=GamesCombo.Template,ItemsSource=new[]{"D3D12","Vulkan (Experimental)"},SelectedIndex=integration==IntelIntegration.D3D12?0:1,Height=36,Width=240,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,8)};
                    content.Children.Add(apiPicker);apiPicker.SelectionChanged+=(_,_)=>{integration=apiPicker.SelectedIndex==1?IntelIntegration.VulkanExperimental:IntelIntegration.D3D12;inputsConfirmed=false;vulkanRequirementsConfirmed=false;ResetTargetConfirmation();Invalidate();Render();};
                    if(integration==IntelIntegration.VulkanExperimental){
                        AddText("Experimental Vulkan replaces the game swap chain. Unknown games can crash or show a black screen. Runtime file installation does not install ReShade or prove Vulkan integration.");
                        var vulkanConfirm=new CheckBox {Content="I confirm ReShade Vulkan addon support and enabled DLSS Super Resolution / Ray Reconstruction in the game",IsChecked=vulkanRequirementsConfirmed,Margin=new Thickness(0,8,0,8)};
                        vulkanConfirm.Checked+=(_,_)=>{vulkanRequirementsConfirmed=true;Invalidate();};vulkanConfirm.Unchecked+=(_,_)=>{vulkanRequirementsConfirmed=false;Invalidate();};content.Children.Add(vulkanConfirm);
                    }
                }
                var confirmed=new CheckBox {Content=intel&&integration==IntelIntegration.VulkanExperimental?"I accept the experimental Vulkan integration requirements":"I confirm D3D12 and a supported integration / input path",IsChecked=inputsConfirmed,Margin=new Thickness(0,10,0,8)};
                content.Children.Add(confirmed);
                confirmed.Checked+=(_,_)=>{inputsConfirmed=true;Invalidate();};confirmed.Unchecked+=(_,_)=>{inputsConfirmed=false;Invalidate();};
                NumberedSection(3,"Confirm target directory");
                AddText("Selected EXE: "+(selectedExe??"Not selected"));
                AddText("Target: "+(target??(selectedExe is null?"Not confirmed":Path.GetDirectoryName(selectedExe))));
                AddText("Explicit target confirmation is required before Continue. A displayed path is not confirmation.",11);
                content.Children.Add(Btn("Confirm target folder",()=>{
                    var folder=new OpenFolderDialog {Title="Explicitly choose the runtime destination inside the selected game",InitialDirectory=selectedExe is null?selectedGameRoot:Path.GetDirectoryName(selectedExe)};
                    if(ShowNativeDialog(folder,wizard)!=true) return;
                    if(selectedGameRoot is null || !IsPathWithinRoot(selectedGameRoot,folder.FolderName)) {status.Text="Target must remain inside the selected game root.";return;}
                    try{RuntimeTargetPolicy.Validate(selectedGameRoot!,selectedExe!,folder.FolderName,intel);}catch(Exception ex)when(ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException){status.Text=ExplainRuntimeError(ex.Message);return;}
                    if(!ShowThemeConfirmation("Confirm runtime destination",folder.FolderName+"\n\nNVIDIA: choose the existing Streamline runtime folder. Intel: choose the actual EXE directory or existing _storage_ pair. Other scanned copies remain read-only and are not installation targets.",wizard)) return;
                    target=folder.FolderName;targetConfirmedForExe=selectedExe;targetNeedsReview=false;Invalidate();Render();
                }));
            } else if(step==3) {
                if(intel) {
                    AddText("Prepare the pinned Intel runtime and its license notices. Cached files are verified before use; no game changes occur during preparation.");
                    AddText(IntelCachePresentation.Status(intelService.PackageDirectory,intelPackage is not null,intelCacheFailed),13);
                    foreach(var a in IntelRuntimeService.Assets) AddText(a.Name+" · "+(a.Version.Length==0?"License / notice":TechnicalVersionDisplay.Format(a.Version))+" · "+(intelPackage is null?"Verification pending":"Verified SHA-256"),12);
                    var technical=new Expander {Header="Technical Details",Margin=new Thickness(0,8,0,0)};technical.Content=Text(string.Join("\n\n",IntelRuntimeService.Assets.Select(a=>$"{a.Name} · {UiNumber(a.Size)} bytes\nSHA-256 {a.Sha256}\n{IntelRuntimeService.BaseUrl+a.Source}")),11);content.Children.Add(technical);
                } else {
                    AddText("Select an official SDK. Download / Prepare reuses a size + SHA-256 verified archive, stages production files, and verifies its matching NVIDIA-signed DLSS-G provider. No game files change until Apply.");
                    content.Children.Add(sdkPicker);
                    content.Children.Add(Btn("Refresh official SDKs",()=>_ = LoadSdks()));
                    content.Children.Add(Btn("Import matching local SDK ZIP",()=>_ = Run(async token=>{
                        if(asset is null) throw new InvalidDataException("Select official release metadata first.");
                        var dialog=new OpenFileDialog{Filter="SDK ZIP (*.zip)|*.zip",Title="Select the matching official SDK ZIP"};
                        if(ShowNativeDialog(dialog,wizard)==true) {await StreamlineLocalArchiveImporter.ImportAsync(dialog.FileName,streamlineDownloads.DownloadsDirectory,asset,token);status.Text="Local archive digest verified. Continue with Download / Prepare.";}
                    })));
                    content.Children.Add(Btn("Import DLSS-G DLL (optional)",()=>_ = Run(async token=>{
                        var dialog=new OpenFileDialog {Title="Select a NVIDIA-signed DLSS-G provider",Filter="DLSS-G DLL (*.dll)|*.dll"};
                        if(ShowNativeDialog(dialog,wizard)==true) {provider=await Task.Run(()=>new DlssGProviderVerifier().Verify(dialog.FileName),token);status.Text=$"Selected DLSS-G {TechnicalVersionDisplay.Format(provider.Version)}; SHA-256 {provider.Sha256}. Pair checked during Preview.";}
                    })));
                }
            } else if(step==4) {
                var rows=intel?intelPlan?.Entries:nvidiaPlan?.Entries;
                if(rows is not null) {
                    content.Children.Add(view.Review(rows,(GamesCombo.SelectedItem as Game)?.Name??Path.GetFileName(selectedExe!),intel?"Intel XeMFG":"NVIDIA Dynamic MFG",intel?"Intel XeSS-FG 1.3.1.78 / XeLL 1.3.2.10":asset?.ReleaseTag??"Not selected",intel?"Pinned official Intel catalog":provider?.Version??"Not checked",row=>OriginSnapshot(row,intel,stage,provider),row=>SignatureSnapshot(row,provider)));
                }
                var ready=intel?intelPlan is not null:nvidiaPlan is {CanInstall:true};
                var approval=new CheckBox {Content="I reviewed the exact files and targets and approve this transaction",IsChecked=false,IsEnabled=ready,Margin=new Thickness(0,12,0,12)};
                consent=false;next.IsEnabled=false;view.ApprovalPanel.Children.Add(approval);
                approval.Checked+=(_,_)=>{consent=true;next.IsEnabled=!busy;};approval.Unchecked+=(_,_)=>{consent=false;next.IsEnabled=false;};
                view.ReviewActionsPanel.Children.Add(Btn("Refresh preview",()=>_ = Run(async token=>{await Preview(token);Render();})));
                view.ReviewActionsPanel.Children.Add(Btn("Restore / previous transactions",()=>_ = Run(async _=>{await ShowRestoreManagerAsync(selectedGameRoot!,CreateBackupService(selectedGameRoot!),wizard);Invalidate();Render();status.Text="Refresh preview after recovery before approving another installation.";})));
            } else {
                view.SidebarPanel.Children.Clear();view.SidebarPanel.Children.Add(SetupWizardView.Label("Installation record",18));view.SidebarPanel.Children.Add(SetupWizardView.Label(resultText));view.SidebarPanel.Children.Add(SetupWizardView.Label("Runtime loaded: NOT VERIFIED."));view.SidebarPanel.Children.Add(SetupWizardView.Label("Open Transactions to recheck current files and backups."));
                AddText(resultText,14);
                AddText((recoveryReviewed?"Current file state: refresh Preview after recovery review.":"The completion result above is historical. Open Transactions to recheck current files.")+"\nRuntime loaded: NOT VERIFIED.\nDynamic MFG / XeMFG active: NOT VERIFIED.\nRestart required before runtime verification. Select the backend in the MFG Unlock in-game panel; this wizard does not change driver overrides or addon settings.");
                content.Children.Add(Btn("Runtime diagnostics (read-only)",()=>_ = Run(async _=>await ShowRuntimeDiagnosticsAsync(wizard))));
                content.Children.Add(Btn("Restore / review transactions",()=>_ = Run(async _=>{await ShowRestoreManagerAsync(selectedGameRoot!,CreateBackupService(selectedGameRoot!),wizard);Invalidate();recoveryReviewed=true;resultText="Recovery review closed. The previous installation result is historical; refresh Preview to inspect the current files.";Render();status.Text="Use Back and refresh Preview before another installation.";})));
                content.Children.Add(Btn("Open game folder",()=>OpenFolder_Click(this,new RoutedEventArgs())));
                content.Children.Add(Btn("MFG Settings",()=>MfgSettings_Click(this,new RoutedEventArgs())));
            }
            if(step==0){var section=new StackPanel();var children=content.Children.Cast<UIElement>().ToArray();content.Children.Clear();foreach(var child in children)section.Children.Add(child);content.Children.Add(SetupWizardView.Card(section,18));}
            LocalizedPresentation.RefreshGeneratedItems(view);
            RefreshNext();

        }
        async Task Run(Func<CancellationToken,Task> action)
        {
            if(busy) return;busy=true;wizardOperationBusy=true;operation=new CancellationTokenSource();content.IsEnabled=false;view.ReviewActionsPanel.IsEnabled=false;view.NavigationPanel.IsEnabled=false;back.IsEnabled=false;next.IsEnabled=false;cancel.Content="Cancel operation";view.OperationProgress.IsIndeterminate=true;view.OperationProgress.Visibility=Visibility.Visible;
            try {await action(operation.Token);}
            catch(OperationCanceledException){Invalidate();if(step==4||(step==3&&intel))Render();status.Text="Cancelled. Completed writes are rolled back where safe; inspect Restore if an error is reported.";}
            catch(Exception ex){Invalidate();if(step==4||(step==3&&intel))Render();status.Text="Stopped: "+ExplainRuntimeError(ex.Message);Log("Setup Wizard: "+ex.Message);}
            finally {operation.Dispose();operation=null;busy=false;wizardOperationBusy=false;content.IsEnabled=true;view.ReviewActionsPanel.IsEnabled=true;view.NavigationPanel.IsEnabled=true;view.OperationProgress.Visibility=Visibility.Collapsed;back.IsEnabled=step>0;RefreshNext();cancel.Content="Close";}
        }
        async Task LoadSdks()=>await Run(async token=>{
            status.Text="Loading official SDK metadata…";
            var releases=await streamlineApi.GetReleasesAsync(token);
            var available=releases.SelectMany(r=>r.Assets).Where(a=>a.ReleaseTag.TrimStart('v','V') is "2.14.1" or "2.14.16" && a.Sha256 is {Length:64}).ToArray();
            sdkPicker.ItemsSource=available.Select(a=>new WizardSdkChoice(a,$"{TechnicalVersionDisplay.Format(a.ReleaseTag)} · {a.Name} · {UiNumber(a.Size)} bytes")).ToArray();
            if(available.Length==0) throw new InvalidDataException("No supported official SDK with a SHA-256 digest was returned. Retry metadata loading.");
            sdkPicker.SelectedIndex=0;status.Text="Official metadata loaded. Choose a documented runtime pair.";
            LocalizedPresentation.RefreshGeneratedItems(sdkPicker);
        });
        async Task Preview(CancellationToken token)
        {
            if(!CurrentTarget()) throw new InvalidDataException("Select a game and explicitly confirm its target folder again.");
            if(intel) intelPlan=await intelService.PreviewAsync(intelPackage ?? throw new InvalidDataException("Prepare Intel runtime first."),selectedGameRoot!,selectedExe!,target!,InputsReady(),integration,token);
            else nvidiaPlan=await GetAdvancedInstaller(selectedGameRoot).PreviewAsync(stage ?? throw new InvalidDataException("Prepare the SDK first."),selectedGameRoot!,selectedExe!,target!,inputsConfirmed,token,provider);
        }
        gamePicker.SelectionChanged+=(_,_)=>{if(gamePicker.SelectedItem is Game game && !ReferenceEquals(GamesCombo.SelectedItem,game)){GamesCombo.SelectedItem=game;target=null;ResetTargetConfirmation();inputsConfirmed=false;vulkanRequirementsConfirmed=false;Invalidate();if(step==0&&!busy)Render();}};
        sdkPicker.SelectionChanged+=(_,_)=>{asset=(sdkPicker.SelectedItem as WizardSdkChoice)?.Asset;stage=null;provider=null;Invalidate();};
        back.Click+=(_,_)=>{if(!busy && step>0){step--;Invalidate();Render();if(step==4) _=Run(async token=>{await Preview(token);Render();});}};
        cancel.Click+=(_,_)=>{if(busy)operation?.Cancel();else CloseWizard();};
        next.Click+=async (_,_)=>await Run(async token=>{
            if(step==0) {
                if(!HasCurrentExecutable()) throw new InvalidDataException("Select the actual game EXE first.");
                status.Text="Scanning component paths, versions and hashes…";
                currentNvidiaScanResult=await Task.Run(()=>nvidiaComponentScanner.Scan(selectedExe!,selectedGameRoot,token),token);
                if(currentNvidiaScanResult.Error is not null) throw new IOException(currentNvidiaScanResult.Error);
                step=1;Render();
            } else if(step==1) {step=2;Render();}
            else if(step==2) {
                if(!InputsReady() || !CurrentTarget()) throw new InvalidDataException("Confirm the selected integration/input path and target directory first.");
                step=3;Render();
                // Already prepared SDK metadata can be reused, otherwise the explicit refresh button loads it.
                if(!intel && sdkPicker.Items.Count==0) {
                    status.Text="Loading official SDK release metadata…";
                    var releases=await streamlineApi.GetReleasesAsync(token);
                    sdkPicker.ItemsSource=releases.SelectMany(r=>r.Assets).Where(a=>(a.ReleaseTag.TrimStart('v','V') is "2.14.1" or "2.14.16") && a.Sha256 is {Length:64}).Select(a=>new WizardSdkChoice(a,$"{TechnicalVersionDisplay.Format(a.ReleaseTag)} · {a.Name} · {UiNumber(a.Size)} bytes")).ToArray();
                    if(sdkPicker.Items.Count>0){sdkPicker.SelectedIndex=0;status.Text="Official metadata loaded. Choose a documented runtime pair.";}else status.Text="No supported official SDK metadata. Refresh to retry.";
                    LocalizedPresentation.RefreshGeneratedItems(sdkPicker);
                }
            } else if(step==3) {
                var progress=new Progress<string>(value=>status.Text=value);
                if(intel){intelPackage=null;intelCacheFailed=false;try{intelPackage=await intelService.PrepareAsync(progress,token);}catch(OperationCanceledException){throw;}catch{intelCacheFailed=true;throw;}}
                else {
                    var chosen=asset ?? throw new InvalidDataException("Select an official SDK with SHA-256 metadata.");
                    var download=await streamlineDownloads.DownloadAsync(chosen,new Progress<StreamlineDownloadProgress>(p=>status.Text=$"Download / verify {chosen.Name}: {UiNumber(p.BytesReceived)} / {UiNumber(chosen.Size)} bytes"),token);
                    if(!download.DigestVerified) throw new InvalidDataException("Official SDK digest is required.");
                    status.Text="Staging and verifying production files…";
                    var prepared=await streamlineStager.StageAsync(download.FilePath,streamlineDownloads.StagingDirectory,chosen.ReleaseTag,token,chosen.Sha256);
                    stage=await streamlineEvidenceService.CaptureAsync(streamlineDownloads.DownloadsDirectory,streamlineDownloads.StagingDirectory,download.FilePath,chosen,prepared.DirectoryPath,token);
                    provider ??=await DlssGProviderImporter.FromSdkAsync(stage,streamlineDownloads.DownloadsDirectory,streamlineDownloads.StagingDirectory,Path.Combine(streamlineDownloads.StorageRoot,"Providers"),token);
                }
                status.Text="Building read-only Preview…";await Preview(token);step=4;Render();
            } else if(step==4) {
                if(!consent || !CurrentTarget()) throw new InvalidDataException("Refresh and explicitly approve Preview first.");
                if(!ShowThemeConfirmation("Apply reviewed runtime files?","This writes ONLY the exact Preview list, with original-byte backups and Restore. Game compatibility is unconfirmed. No game will be launched. Continue?",wizard))return;
                recoveryReviewed=false;
                view.OperationProgress.IsIndeterminate=false;view.OperationProgress.Value=0;
                var progress=new Progress<int>(p=>{status.Text=$"Applying transaction: {p}%";view.OperationProgress.Value=p;});
                if(intel) {
                    var result=await intelService.InstallAsync(intelPackage!,intelPlan!,InputsReady(),CreateBackupService(selectedGameRoot!),progress,token);
                    resultText=result is null?"Already configured: every pinned file hash matches. No transaction was needed.":$"Files installed and hashes verified. Transaction {result.TransactionId}: {result.Status}.";
                } else {
                    var result=await GetAdvancedInstaller(selectedGameRoot).InstallAsync(stage!,selectedGameRoot!,selectedExe!,target!,inputsConfirmed,nvidiaPlan!,progress,token,provider);
                    resultText=$"{result.InstalledFiles.Count} files installed and hashes verified. Transaction {result.Transaction.TransactionId}: {result.Transaction.Status}.";
                }
                step=5;Render();
            }
        });
        activeSetupWizard=view;
        invalidateSetupWizard=()=>{Invalidate();if(step>3)step=3;Render();};
        resumeSetupWizard=()=>{if(!string.Equals(renderedExe,selectedExe,StringComparison.OrdinalIgnoreCase)){step=0;target=null;ResetTargetConfirmation();inputsConfirmed=false;vulkanRequirementsConfirmed=false;stage=null;provider=null;intelPackage=null;Invalidate();gamePicker.SelectedItem=GamesCombo.SelectedItem;Render();}};
        Render();SetPage(AppPage.DllInstallation);
    }
    private sealed record WizardSdkChoice(StreamlineSdkAsset Asset,string Label);
}

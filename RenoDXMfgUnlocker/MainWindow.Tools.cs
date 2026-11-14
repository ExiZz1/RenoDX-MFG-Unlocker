using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private static string UiNumber(long value)=>value.ToString("N0",System.Globalization.CultureInfo.InvariantCulture);
    private bool wizardOperationBusy;
    private bool LanguageChangeBlocked=>reshadeBusy || reshadeSession.BlocksChanges || intelSdkBusy || wizardOperationBusy || addonInstallBusy || mfgSettingsBusy || streamlineBusy || installOperationCancellation is not null;
    private void ApplicationSettings_Click(object sender,RoutedEventArgs e)=>CreateLanguageDialog().ShowDialog();
    private void StyleLanguageChoice(Button button,bool selected){button.Background=SetupWizardView.Brush(selected?"#44305F":"#20212D");button.BorderBrush=SetupWizardView.Brush(selected?"#B18BEF":"#3B354A");button.BorderThickness=new Thickness(selected?2:1);}
    private TextBlock ToolText(string text,int size=12)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,8,8),Foreground=(Brush)FindResource("Text")};
    private void ShowComponentDetails(string title,string text,Window? owner=null)=>CreateInfoDialog(title,ComponentDetailsContent(text),780,720,owner).ShowDialog();
    internal static string PlanAction(StreamlineComponentPlanEntry row)=>row.Action=="Blocked"?"Blocked":!row.IsSelected?(row.ExistingSha256 is not null && row.ExistingSha256.Equals(row.Sha256,StringComparison.OrdinalIgnoreCase)?"Unchanged":"Skipped"):row.Action switch {"New file"=>"Add","Replace existing"=>"Replace",_=>row.Action};
    private static bool? ShowNativeDialog(Microsoft.Win32.CommonDialog dialog,Window owner)
    {
        if(dialog is Microsoft.Win32.FileDialog file){file.Title=LocalizedPresentation.Language.Translate(file.Title);var pieces=file.Filter.Split('|');for(var i=0;i<pieces.Length;i+=2)pieces[i]=LocalizedPresentation.Language.Translate(pieces[i]);file.Filter=string.Join('|',pieces);}
        if(dialog is Microsoft.Win32.OpenFolderDialog folder)folder.Title=LocalizedPresentation.Language.Translate(folder.Title);
        return dialog.ShowDialog(owner);
    }
    private string PlanDetails(StreamlineComponentPlanEntry row,bool intel=false,VerifiedStreamlineStage? sdk=null,VerifiedDlssGProvider? provider=null)=>$"Component: {row.FileName}\nCurrent version: {TechnicalVersionDisplay.Format(row.ExistingVersion)}\nProposed version: {TechnicalVersionDisplay.Format(row.SourceVersion)}\nSource: {row.SourcePath}\nTarget: {row.TargetPath}\nCurrent SHA-256: {row.ExistingSha256??"Missing"}\nNew SHA-256: {row.Sha256}\nAction: {PlanAction(row)}\n{row.Message}\n"+(row.Action=="Replace existing"?"Original file will be backed up in this transaction.":"No existing file is scheduled for replacement.")+"\nSource validation and target hashes are checked again before writing. File maintenance does not certify game compatibility.\n"+(intel?"Required origin: the pinned official Intel catalog and its license notices.":row.FileName.Equals("nvngx_dlssg.dll",StringComparison.OrdinalIgnoreCase)?"Required origin: NVIDIA-signed native x64 DLSS-G with the documented version pair; import path is shown above.":"Required origin: an official Streamline SDK archive checked against GitHub size and SHA-256, with verified production members.")+"\n\n"+OriginSnapshot(row,intel,sdk??verifiedStreamlineStage,provider??selectedDlssGProvider);
    private static string SignatureSnapshot(StreamlineComponentPlanEntry row,VerifiedDlssGProvider? provider)=>!row.FileName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)?"Not applicable":row.FileName.Equals("nvngx_dlssg.dll",StringComparison.OrdinalIgnoreCase)&&provider is not null&&provider.Sha256.Equals(row.Sha256,StringComparison.OrdinalIgnoreCase)?"Verified Publisher Signature · NVIDIA":"Not checked yet";
    private static string OriginSnapshot(StreamlineComponentPlanEntry row,bool intel,VerifiedStreamlineStage? sdk,VerifiedDlssGProvider? provider)
    {
        if(intel)return "Origin snapshot: official pinned Intel catalog\n"+IntelRuntimeService.BaseUrl;
        if(row.FileName.Equals("nvngx_dlssg.dll",StringComparison.OrdinalIgnoreCase) && provider is not null && provider.Path.Equals(row.SourcePath,StringComparison.OrdinalIgnoreCase) && provider.Sha256.Equals(row.Sha256,StringComparison.OrdinalIgnoreCase))
            return "Origin snapshot: NVIDIA-signed provider\n"+provider.Signer+"\n"+provider.Path+"\nLocal import or SDK extraction; acquisition path is shown above. Signature and file integrity are separate checks.";
        if(sdk is not null && sdk.Files.Any(f=>f.FileName.Equals(row.FileName,StringComparison.OrdinalIgnoreCase) && f.Sha256.Equals(row.Sha256,StringComparison.OrdinalIgnoreCase)))return "Origin snapshot: official Streamline SDK\n"+sdk.Asset.DownloadUrl+"\nArchive SHA-256: "+sdk.Asset.Sha256+"\nSource snapshot: "+sdk.VerifiedAtUtc.ToString("O");
        return "Origin: Not verified";
    }
    private void ScanDetails_Click(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:NvidiaComponentEntry entry})ShowComponentDetails(entry.Name,ScanDetails(entry));
    }
    private static string ScanDetails(NvidiaComponentEntry entry)=>$"Component: {entry.Name}\nComponent Type: {ComponentKind(entry.Name)}\nInstalled version: {TechnicalVersionDisplay.Format(entry.Version)}\nStatus: {entry.Status}\nLocation: {entry.Location}\nTarget: {entry.Path}\n{entry.IntegritySummary}\nRequirement unknown. Disk detection does not establish runtime loading or compatibility.";
    private static string ComponentKind(string name)=>name.StartsWith("sl.",StringComparison.OrdinalIgnoreCase)?"Streamline plugin":name.Equals("nvngx_dlssg.dll",StringComparison.OrdinalIgnoreCase)?"DLSS Frame Generation NGX runtime":name.Equals("nvngx_dlss.dll",StringComparison.OrdinalIgnoreCase)?"DLSS Super Resolution":name.Equals("libxess_fg.dll",StringComparison.OrdinalIgnoreCase)?"Intel XeMFG":name.Equals("libxell.dll",StringComparison.OrdinalIgnoreCase)?"Intel XeLL":"Runtime / license";
    private void PlanDetails_Click(object sender,RoutedEventArgs e){if(sender is FrameworkElement {DataContext:ComponentPlanRow row})ShowComponentDetails(row.FileName,PlanDetails(row.Entry));}
    private FrameworkElement CompactTable(string[] headings,IEnumerable<(string[] Cells,Action Details)> rows,bool singleLineDetails=false)
    {
        var table=new StackPanel();
        Grid Row(string[] cells,bool header,Action? details=null){
            var grid=new Grid {Margin=new Thickness(0,0,0,2)};foreach(var weight in new[]{2.1,1.1,1.1,1.4,.9})grid.ColumnDefinitions.Add(new(){Width=singleLineDetails&&weight==.9?GridLength.Auto:new GridLength(weight,GridUnitType.Star),MinWidth=singleLineDetails&&weight==.9?112:0});
            for(var i=0;i<cells.Length;i++){
                FrameworkElement element;
                if(!header && i==cells.Length-1){var button=new Button {Content="Details",Padding=new Thickness(8,6,8,6),Margin=new Thickness(4)};if(singleLineDetails){button.MinWidth=104;button.ContentTemplate=(DataTemplate)FindResource("SingleLineButtonLabel");}button.Click+=(_,_)=>details?.Invoke();element=button;}
                else {var text=ToolText(!header&&headings[i] is "Installed version" or "Current Version" or "New Version"?TechnicalVersionDisplay.Format(cells[i]):cells[i],header?10:11);text.VerticalAlignment=VerticalAlignment.Center;if(header)text.Foreground=(Brush)FindResource("Purple");element=text;}
                Grid.SetColumn(element,i);grid.Children.Add(element);
            }return grid;
        }
        table.Children.Add(Row(headings,true));foreach(var row in rows)table.Children.Add(new Border {Background=(Brush)FindResource("Panel2"),CornerRadius=new CornerRadius(7),Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,2,8,3),Child=Row(row.Cells,false,row.Details)});
        return table;
    }
    private void SettingsGuide_Click(object sender,RoutedEventArgs e)
    {
        if(mfgSettingsDocuments.Count==0&&mfgSettingRows.Count==0){
            foreach(var service in new[]{mfgUnlockSettings,xeMfgSettings})foreach(var setting in service.Definitions){
                var rows=service==xeMfgSettings?xeMfgSettingRows:mfgSettingRows;
                rows.Add(new MfgUnlockSettingRow(setting,"No configuration loaded · settings remain read-only.",null,false,UpdateMfgSettingsButtons){Group=mfgSettingsGroups.For(service.ConfigFileName,setting),Details=SettingDetails(setting,service.ConfigFileName,service.ConfigSection),HelpSections=MainWindow.HelpSections(setting,service.ConfigFileName,service.ConfigSection)});
            }
            SetMfgText(XeMfgConfigText,"XeMFG advanced editing requires addon 1.7.0 or newer with XeFG configuration support.");
        }
        UpdateMfgCategoryPresentation();MfgSettingsPage.UpdateLayout();
        void Expand(DependencyObject parent){for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is Expander expander)expander.IsExpanded=true;Expand(child);}}
        Expand(MfgSettingsPage);LocalizedPresentation.RefreshGeneratedItems(MfgSettingsPage);
    }
    private static string SettingDetails(MfgUnlockSettingDefinition setting,string file,string section)
    {
        var xe=section=="XeFG";
        var format=xe?(setting.Key=="XeFGStock2xModel"?"Only the integer 1 enables the stock 2x model; other integer values disable it. This utility writes 0 or 1.":setting.Key=="UIStabilization"?"UIStabilization accepts 0/1, true/false and on/off. This utility writes 0 or 1. Live controls remain in the original addon panel; external changes require a restart.":"Upstream reads an integer: 0 disables it, nonzero enables it. This utility writes 0 or 1 and preserves untouched saved values."):"Saved values are not live runtime state. Close the game before editing. No performance gain is guaranteed.";
        return "Display Name: "+setting.Label+"\nTechnical Key: "+setting.Key+"\n"+file+" · ["+section+"]\n\nDescription: "+setting.Description+"\n\nEffect on game: "+(xe?"These options affect the XeMFG path only. They do not install dependencies or enable the backend.":"These saved preferences are read by the addon. They do not prove runtime loading or active frame generation.")+"\n\nWhen to Change: "+GuideAdvice(setting.Key)+"\n\nDefault: "+setting.DefaultValue+"\nRecommended: Not established for this game.\n\nPossible side effects: "+(xe?"Experimental behavior can affect pacing, latency or image quality. Restore the original setting if problems occur.":"Changing runtime policy or experimental quality paths can alter behavior. Use controlled troubleshooting and retain a backup.")+"\n\n"+format+"\n\nRestart Required: Yes";
    }
    private static string GuideAdvice(string key)=>key switch {
        "Selected" or "XmxGenerator"=>"Use the generation-method selector to save both exact INI keys together. XMX is experimental and needs D3D12 plus native DLSS Frame Generation. Restart; then verify in the original addon panel.",
        "ForceBorderless"=>"Change only to troubleshoot the DLSS-G window presentation. Restart required.",
        "XeFGStock2xModel"=>"Use only for a controlled 2x comparison. This does not enable higher multipliers.",
        "Vulkan"=>"Change only when deliberately testing the experimental Vulkan XeMFG path with addon 1.7.0, ReShade Vulkan addon support and enabled game DLSS SR/RR. Disable it if initialization fails.",
        "VulkanPresentMetering"=>"Leave the default unless comparing Vulkan presentation pacing with VSync off and a driver that exposes present metering.",
        "XeFGFlipQueue"=>"Keep the default off unless performing an advanced pacing comparison. Upstream reports additional latency in its standalone host.",
        "UIStabilization"=>"Test only for static HUD interpolation artifacts. Moving and transparent HUD elements are not fully stabilized. The addon panel also offers a live control.",
        "Enabled"=>"Use 1 to allow the addon on the next launch; 0 disables it. This does not ensure Frame Generation is available in the game.",
        "ForceFlipMeteringOff"=>"Advanced workaround. Keep 0 unless investigating a documented higher-multiplier freeze. Software pacing can change frame delivery; compare behavior in your game.",
        "TemporalFix"=>"Default 1 retains the temporal interpolation correction. Change only for controlled troubleshooting; no universal image-quality result is guaranteed.",
        "BlackwellFrameworkKernels"=>"Default 1 enables exact-fingerprint replacement kernels where supported, with fallback. It does not make an unknown provider compatible.",
        "ExperimentalAdaptiveQuality"=>"Experimental feature, default 1 in the supported addon. Evaluate artifacts in your own game; no guaranteed performance or visual benefit.",
        "Rtx50QualityFixes"=>"Keep 0 unless deliberately testing the experimental RTX 50 warp-blend fix with a supported provider. RTX 40 behavior is unchanged.",
        _=>"Default 0 preserves game runtime policy and is the conservative choice without specific evidence. Mode 1 prefers a complete local runtime; mode 2 forces NVIDIA OTA flags. These change runtime selection, not driver profiles or compatibility."};
}

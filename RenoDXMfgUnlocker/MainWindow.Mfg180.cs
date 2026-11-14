using System.Windows;
using System.Windows.Controls;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private bool generationModeUpdating;
    private bool generationTechnicalExpanded;
    private void UpdateGenerationMode()
    {
        if(generationModeUpdating)return;
        generationModeUpdating=true;
        try {
            var backend=xeMfgSettingRows.FirstOrDefault(r=>r.Definition.Key=="Selected");
            var xmx=xeMfgSettingRows.FirstOrDefault(r=>r.Definition.Key=="XmxGenerator");
            if(backend is null||xmx is null){GenerationModePanel.Children.Clear();return;}
            var valid=backend.ProposedValue is not null&&xmx.ProposedValue is not null;
            var selected=!valid?"Unknown":backend.ProposedValue=="XeMFG"?"XeMFG":xmx.ProposedValue=="1"?"XMX":"DLSSG";
            if(GenerationModePanel.Children.Count==0){
            var modes=new WrapPanel();
            foreach(var pair in new[]{("DLSSG","NVIDIA DLSS-G"),("XeMFG","Intel XeMFG (CUDA)"),("XMX","XeMFG via DLSS-G (XMX, experimental)")}){
                var button=new Button{Content=pair.Item2,Tag=pair.Item1,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,7,6)};
                button.Click+=(_,_)=>{if(button.IsEnabled)SelectGenerationMethod(pair.Item1);};
                modes.Children.Add(button);
            }
            GenerationModePanel.Children.Add(modes);
            GenerationModePanel.Children.Add(new Border{Background=SetupWizardView.Brush("#171920"),BorderBrush=SetupWizardView.Brush("#393044"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(0,2,0,9)});
            GenerationModePanel.Children.Add(ToolText(""));
            }
            foreach(var button in ((WrapPanel)GenerationModePanel.Children[0]).Children.OfType<Button>()){
                button.Content=LocalizedPresentation.Language.Lookup((string)button.Tag switch{"DLSSG"=>"NVIDIA DLSS-G","XeMFG"=>"Intel XeMFG (CUDA)",_=>"XeMFG via DLSS-G (XMX, experimental)"});
                button.IsEnabled=backend.CanEdit&&xmx.CanEdit&&!AnyFileOperationBusy;
                button.Background=SetupWizardView.Brush(selected==(string)button.Tag?"#493361":"#20222C");
            }
            foreach(var row in mfgSettingRows){
                var locked=selected=="XMX"&&MfgUnlockSettingsService.NvidiaGeneratorOnly(row.Definition.Key);
                row.CanEdit=row.BaseCanEdit&&!locked;
            }
            UpdateGenerationInfo();
            LocalizedPresentation.RefreshGeneratedItems(GenerationModePanel);
        } finally {generationModeUpdating=false;}
    }
    private void UpdateGenerationInfo()
    {
        var snapshot=mfgSettingsDocuments.FirstOrDefault(d=>d.Service.ConfigFileName=="MFGAdaUnlock.ini")?.Snapshot;
        var info=snapshot is null?null:GenerationMethodPresenter.Describe(snapshot,xeMfgSettingRows.ToDictionary(r=>r.Definition.Key,r=>r.ProposedValue,StringComparer.Ordinal));
        var card=(Border)GenerationModePanel.Children[1];card.Tag=info;
        var language=LocalizedPresentation.Language;var body=new StackPanel();var output=body;
        void Text(string text,bool technical=false){var label=ToolText(text,12);LocalizedPresentation.SetPreserveOriginal(label,true);if(technical)label.FontFamily=new System.Windows.Media.FontFamily("Consolas");output.Children.Add(label);}
        void Heading(string key){var label=ToolText(language.Lookup(key),12);label.FontWeight=FontWeights.SemiBold;label.Foreground=SetupWizardView.Brush("#CDB4EF");label.Margin=new Thickness(0,10,0,4);LocalizedPresentation.SetPreserveOriginal(label,true);output.Children.Add(label);}
        if(info is null)Text(language.Lookup("Generation values are unavailable or invalid. Reload the reviewed configuration."));
        else {
            Text(language.Format("Selected method: {0}",language.Lookup(info.SelectedMethod)));
            Text(language.Format("Saved method: {0}",language.Lookup(info.SavedMethod)));
            Text(language.Lookup(info.Pending?"Unsaved configuration change":info.DefaultsInUse?"Addon defaults in use; no generation-method change is pending.":"Matches saved configuration; no generation-method change is pending."));
            Text(language.Lookup(info.Description));
            Text(language.Lookup("Saving updates configuration only. Restart the game. No DLLs are installed and no runtime activity is verified."));
            if(PendingNvidiaEditsBlockedByXmx())Text(language.Lookup(PendingXmxConflict));
            var details=new Expander{Header=language.Lookup("Technical Details"),IsExpanded=generationTechnicalExpanded,Margin=new Thickness(0,6,0,0),Foreground=SetupWizardView.Brush("#CDB4EF")};
            LocalizedPresentation.SetPreserveOriginal(details,true);
            details.Expanded+=(_,_)=>generationTechnicalExpanded=true;details.Collapsed+=(_,_)=>generationTechnicalExpanded=false;
            var technicalBody=new StackPanel();details.Content=technicalBody;body.Children.Add(details);output=technicalBody;
            Heading("Automatically configured by method selection");
            Text(language.Lookup("The method buttons change only these generation preferences (saved → proposed):"));
            Text(System.IO.Path.GetFileName(snapshot!.ConfigPath)+"\n"+info.IniValues,true);
            Heading("Additional settings — configured independently");
            Text(language.Lookup("HUD stabilization, Vulkan options, NVIDIA quality, Intel stock 2x model and borderless presentation remain independent in General / Advanced / Other. Method selection does not enable or disable their values."));
            Text(language.Lookup("Not every option applies to every generator. Existing capability lockouts still apply. Most experimental options can stay at existing/default values unless troubleshooting a specific issue."));
            Text(language.Lookup("Other generator-related settings are available under Advanced and Other."));

        }
        card.Child=body;
        var warning=(TextBlock)GenerationModePanel.Children[2];warning.Text=info?.ContextWarning is {} message?language.Lookup(message):"";warning.Visibility=info?.ContextWarning is null?Visibility.Collapsed:Visibility.Visible;LocalizedPresentation.SetPreserveOriginal(warning,true);
    }
    private const string PendingXmxConflict="Pending NVIDIA generator changes are preserved but cannot be saved with selected XMX. Switch back to NVIDIA DLSS-G to save them, or use Discard changes.";
    private bool PendingNvidiaEditsBlockedByXmx()=>xeMfgSettingRows.Any(r=>r.Definition.Key=="Selected"&&r.ProposedValue=="DLSSG")&&xeMfgSettingRows.Any(r=>r.Definition.Key=="XmxGenerator"&&r.ProposedValue=="1")
        &&mfgSettingsDocuments.Any(d=>d.Service.ConfigFileName=="ReShade.ini"&&d.Rows.Any(r=>MfgUnlockSettingsService.NvidiaGeneratorOnly(r.Definition.Key)&&r.ProposedValue is not null&&r.ProposedValue!=d.Snapshot.Settings.First(s=>s.Definition.Key==r.Definition.Key).EffectiveValue));
    private void SelectGenerationMethod(string method)
    {
        var backend=xeMfgSettingRows.FirstOrDefault(r=>r.Definition.Key=="Selected");
        var xmx=xeMfgSettingRows.FirstOrDefault(r=>r.Definition.Key=="XmxGenerator");
        if(method is not ("DLSSG" or "XeMFG" or "XMX")||backend?.CanEdit!=true||xmx?.CanEdit!=true||AnyFileOperationBusy||mfgSettingsExePath is null||mfgSettingsExePath!=selectedExe||!HasCurrentExecutable()||IsSelectedGameProcessRunning(mfgSettingsExePath,out _))return;
        generationModeUpdating=true;
        try{backend.ProposedValue=method=="XeMFG"?"XeMFG":"DLSSG";xmx.ProposedValue=method=="XMX"?"1":"0";}
        finally{generationModeUpdating=false;}
        UpdateMfgSettingsButtons();
    }
}

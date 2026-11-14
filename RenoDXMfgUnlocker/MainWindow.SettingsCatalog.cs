using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    private readonly MfgSettingsCatalogStore settingsCatalogStore = new(MfgSettingsCatalogStore.UserRoot);
    private bool settingsCatalogLoaded;
    private string? settingsCatalogError;
    private CancellationTokenSource? catalogCheckCancellation;
    private readonly MfgUpstreamSettingsStore upstreamSettingsStore=new(MfgUpstreamSettingsStore.UserRoot);
    private MfgDynamicSettingsCatalog? signedSettingsBaseline;
    private UpstreamSourceSnapshot? approvedUpstreamSnapshot;
    private UpstreamSettingsAnalysis? upstreamAnalysis;
    private UpstreamSettingsAnalysis? approvedUpstreamAnalysis;
    private IReadOnlyList<UpstreamSettingChange> upstreamChanges=[];

    private bool CatalogChangesAllowed()
    {
        if(AnyFileOperationBusy)return false;
        if(PendingMfgEdits().Length==0)return true;
        ShowThemeDialog("MFG Settings","Save or discard pending settings before changing metadata.");return false;
    }

    private async Task ApplySignedSettingsCatalogAsync(byte[] bytes,byte[] signature)
    {
        var candidate=MfgDynamicSettingsCatalog.Verify(bytes,signature);
        var current=signedSettingsBaseline??mfgUnlockSettings.Catalog;
        if(candidate.Revision==current.Revision&&candidate.Identity==current.Identity){SetMfgText(MfgSettingsStatusText,"Settings catalog is up to date.");return;}
        if(candidate.Revision<=current.Revision)throw new InvalidDataException("Settings catalog revision must be newer.");
        if(!ShowThemeConfirmation("Signed RenoDX catalog (fallback)","Import this owner-signed settings catalog? Game files are not changed. Reloaded definitions replace the previous catalog."+"\n\n"+candidate.Revision+" · SHA-256 "+candidate.Identity))return;
        var catalog=settingsCatalogStore.Import(bytes,signature);
        signedSettingsBaseline=catalog;settingsCatalogError=null;ApplyUpstreamOverlay();settingsCatalogLoaded=true;
        await LoadMfgSettingsAsync();
    }

    private void LoadSettingsCatalog()
    {
        if (settingsCatalogLoaded) return;
        settingsCatalogLoaded = true;
        try {
            var catalog = settingsCatalogStore.Load();var fallback=MfgDynamicSettingsCatalog.ReviewedFallback();
            if(catalog.Revision<fallback.Revision)catalog=fallback;
            else if(catalog.Revision==fallback.Revision&&catalog.Identity!=fallback.Identity)throw new InvalidDataException("Conflicting settings catalog revisions.");
            signedSettingsBaseline=catalog;
            // An invalid optional upstream cache must never discard a valid signed baseline.
            try{approvedUpstreamSnapshot=upstreamSettingsStore.Load();}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException){settingsCatalogError=ex.Message;}
            ApplyUpstreamOverlay();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or FormatException) { settingsCatalogError = ex.Message; }
    }
    private void ApplyUpstreamOverlay()
    {
        var baseline=signedSettingsBaseline??MfgDynamicSettingsCatalog.BuiltIn;
        mfgUnlockSettings.UseCatalog(baseline);xeMfgSettings.UseCatalog(baseline);
        if(approvedUpstreamSnapshot is {} source){
            try{upstreamAnalysis=MfgUpstreamSettingsAnalyzer.Analyze(source);approvedUpstreamAnalysis=upstreamAnalysis;var active=baseline.WithUpstream(upstreamAnalysis);mfgUnlockSettings.UseCatalog(active);xeMfgSettings.UseCatalog(active);}
            catch(Exception ex)when(ex is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or TimeoutException){settingsCatalogError=ex.Message;}
        }
    }
    private async void CheckUpstreamSettings_Click(object sender,RoutedEventArgs e)
    {
        if(!CatalogChangesAllowed())return;
        var version=mfgSettingsDocuments.FirstOrDefault()?.Snapshot.AddonVersion;
        if(!MfgSettingsCapabilityCatalog.TryStableVersion(version,out _)){ShowThemeDialog("Official addon source",LocalizedPresentation.Language.Lookup("Load a confirmed addon version before checking upstream settings."));return;}
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(65));catalogCheckCancellation=cancellation;
        mfgSettingsBusy=true;UpdateMfgSettingsButtons();SetMfgText(MfgSettingsStatusText,"Checking official addon release source on upstream GitHub…");
        try{
            using var client=new MfgUpstreamSettingsClient();var latest=await client.LatestStableAsync(cancellation.Token);
            MfgSettingsCapabilityCatalog.TryStableVersion(version,out var installed);MfgSettingsCapabilityCatalog.TryStableVersion(latest,out var available);
            var source=await client.FetchAsync(available>installed?latest:version!,cancellation.Token);
            var analysis=await Task.Run(()=>MfgUpstreamSettingsAnalyzer.Analyze(source,cancellation.Token),cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();
            var baseline=signedSettingsBaseline??MfgDynamicSettingsCatalog.BuiltIn;
            upstreamChanges=approvedUpstreamSnapshot is null?[]:MfgUpstreamSettingsAnalyzer.Compare(MfgUpstreamSettingsAnalyzer.Analyze(approvedUpstreamSnapshot),analysis);
            upstreamAnalysis=analysis;UpdateUpstreamPresentation();
            var review=MfgUpstreamApprovalReview.Compare(analysis,baseline,approvedUpstreamSnapshot,approvedUpstreamAnalysis);
            var language=LocalizedPresentation.Language;
            string Entries(string kind,string title){var items=review.Where(i=>i.Kind==kind).ToArray();return language.Lookup(title)+" "+items.Length+"\n"+string.Join("\n",items.Select(i=>"["+i.Finding.Section+"] "+i.Finding.Key));}
            var summary=language.Lookup("Upstream source is not owner-signed. Only recognized standalone settings can be added; existing definitions remain unchanged.")+"\n\n"+source.Tag+" · "+source.Commit+"\n"
                +Entries(MfgUpstreamApprovalReview.New,"New editable settings:")+"\n\n"
                +Entries(MfgUpstreamApprovalReview.Unchanged,"Unchanged declarations to reconfirm:")+"\n\n"
                +Entries(MfgUpstreamApprovalReview.Changed,"Changed declarations — review before approval:")+"\n\n"
                +language.Lookup("Each release is analyzed and approved separately. No write permission is carried forward automatically.")+"\n\n"
                +language.Lookup("New source settings require the matching addon version; current settings are retained.")+"\n\n"+language.Lookup("No game configuration is changed by approval. Review source evidence in technical details.");
            cancellation.Token.ThrowIfCancellationRequested();cancellation.CancelAfter(Timeout.InfiniteTimeSpan); // The bounded retrieval is complete; let the owner review the evidence.
            if(!ShowThemeConfirmation("Approve official addon source",summary))return;
            cancellation.Token.ThrowIfCancellationRequested();
            // Cache approval only. Existing writer/transactions remain the sole authority for deliberate Save.
            upstreamSettingsStore.Save(source);approvedUpstreamSnapshot=source;settingsCatalogError=null;ApplyUpstreamOverlay();
            await LoadMfgSettingsAsync();
        }catch(OperationCanceledException){SetMfgText(MfgSettingsStatusText,"Catalog check cancelled or timed out. Existing settings were preserved.");}
        catch(Exception ex){ShowThemeDialog("Official addon source",LocalizedPresentation.Language.Lookup("Upstream check failed. Existing settings were preserved.")+"\n"+MfgSettingsDisplay.Error(ex.Message,LocalizedPresentation.Language),true);}
        finally{catalogCheckCancellation=null;mfgSettingsBusy=false;UpdateMfgSettingsButtons();}
    }
    private void UpdateUpstreamPresentation()
    {
        UpstreamSettingsDetails.Text=MfgUpstreamSettingsPresentation.Render(upstreamAnalysis,signedSettingsBaseline??MfgDynamicSettingsCatalog.BuiltIn,approvedUpstreamSnapshot,upstreamChanges,LocalizedPresentation.Language,approvedUpstreamAnalysis);
        LocalizedPresentation.SetPreserveOriginal(UpstreamSettingsDetails,true);
    }
    private async void ImportSettingsCatalog_Click(object sender, RoutedEventArgs e)
    {
        if(!CatalogChangesAllowed())return;
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try {
            var bytes = MfgSettingsCatalogStore.ReadBounded(dialog.FileName, MfgDynamicSettingsCatalog.MaximumBytes);
            var signature = MfgSettingsCatalogStore.ReadBounded(dialog.FileName + ".sig", 1024);
            await ApplySignedSettingsCatalogAsync(bytes,signature);
        } catch (Exception ex) { ShowThemeDialog("Signed RenoDX catalog (fallback)", LocalizedPresentation.Language.Lookup("Catalog rejected. Existing settings and catalog were preserved.") + "\n" + MfgSettingsDisplay.Error(ex.Message,LocalizedPresentation.Language), true); }
    }

    private async void CheckSettingsCatalog_Click(object sender,RoutedEventArgs e)
    {
        if(!CatalogChangesAllowed())return;
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(30));catalogCheckCancellation=cancellation;
        mfgSettingsBusy=true;UpdateMfgSettingsButtons();SetMfgText(MfgSettingsStatusText,"Checking the signed RenoDX catalog…");
        try{
            using var client=new MfgSettingsCatalogUpdateClient();var result=await client.CheckAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(result is null)SetMfgText(MfgSettingsStatusText,"No signed RenoDX catalog is available on the configured channel.");
            else await ApplySignedSettingsCatalogAsync(result.Json,result.Signature);
        }catch(OperationCanceledException){SetMfgText(MfgSettingsStatusText,"Catalog check cancelled or timed out. Existing settings were preserved.");}
        catch(Exception ex){ShowThemeDialog("Signed RenoDX catalog (fallback)",LocalizedPresentation.Language.Lookup("Catalog check failed. Existing settings and catalog were preserved.")+"\n"+MfgSettingsDisplay.Error(ex.Message,LocalizedPresentation.Language),true);}
        finally{catalogCheckCancellation=null;mfgSettingsBusy=false;UpdateMfgSettingsButtons();}
    }
    private void CancelSettingsCatalogCheck_Click(object sender,RoutedEventArgs e)=>catalogCheckCancellation?.Cancel();
    private void UpdateSettingsCatalogPresentation()
    {
        var language = LocalizedPresentation.Language;
        CheckSettingsCatalogButton.IsEnabled=!AnyFileOperationBusy;ImportSettingsCatalogButton.IsEnabled=!AnyFileOperationBusy;CheckUpstreamSettingsButton.IsEnabled=!AnyFileOperationBusy;
        CancelSettingsCatalogCheckButton.Visibility=catalogCheckCancellation is null?Visibility.Collapsed:Visibility.Visible;
        SettingsCatalogStatus.Text = settingsCatalogError is null ? language.Lookup("Signed RenoDX catalog (fallback)") + " · " + mfgUnlockSettings.Catalog.Revision + " · " + mfgUnlockSettings.Catalog.Identity
            : language.Lookup("Catalog rejected. Existing settings and catalog were preserved.") + "\n" + MfgSettingsDisplay.Error(settingsCatalogError,language);
        LocalizedPresentation.SetPreserveOriginal(SettingsCatalogStatus, true);
        var unknown = mfgSettingsDocuments.SelectMany(d => d.Snapshot.UnknownSettings.Select(s => d.Service.ConfigFileName + " · [" + s.Section + "] " + s.Key + "=" + s.RawValue)).ToArray();
        UnknownSettingsText.Text = unknown.Length == 0 ? language.Lookup("No unknown settings found.") : string.Join("\n", unknown);
        LocalizedPresentation.SetPreserveOriginal(UnknownSettingsText, true);
        foreach (var row in mfgSettingRows.Concat(xeMfgSettingRows)) row.RefreshLanguage();
        UpdateUpstreamPresentation();RenderUpstreamReleaseNotice();
    }
    private void UpdateCatalogEditors()
    {
        var current=mfgSettingsExePath is not null&&mfgSettingsExePath==selectedExe&&HasCurrentExecutable();
        var closed=current&&!IsSelectedGameProcessRunning(mfgSettingsExePath!,out _);
        var backend=mfgSettingsDocuments.FirstOrDefault(d=>d.Service.ConfigFileName=="MFGAdaUnlock.ini");
        var xmx=xeMfgSettingRows.Any(r=>r.Definition.Key=="Selected"&&r.ProposedValue=="DLSSG")&&xeMfgSettingRows.Any(r=>r.Definition.Key=="XmxGenerator"&&r.ProposedValue=="1")||backend is not null&&MfgUnlockSettingsService.XmxSelected(backend.Snapshot.Settings);
        foreach(var d in mfgSettingsDocuments){
            var proposed=d.Rows.Where(r=>r.ProposedValue is not null).ToDictionary(r=>r.Definition.Key,r=>r.ProposedValue!,StringComparer.OrdinalIgnoreCase);
            foreach(var row in d.Rows)row.SetAvailability(MfgSettingsAvailability.Evaluate(d.Service,d.Snapshot,row.Definition,proposed,row.BaseCanEdit,current,closed,AnyFileOperationBusy,xmx));
        }
        if(GenerationModePanel.Children.Count>0&&GenerationModePanel.Children[0] is System.Windows.Controls.WrapPanel panel){
            var allowed=xeMfgSettingRows.Where(r=>MfgSettingsLayout.IsMethodKey(r.Definition.Key)).ToArray();
            foreach(var button in panel.Children.OfType<System.Windows.Controls.Button>())button.IsEnabled=allowed.Length==2&&allowed.All(r=>r.CanEdit)&&!AnyFileOperationBusy;
        }
    }
}

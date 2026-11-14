using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    private readonly ApplicationUpdateService applicationUpdates=new();
    private readonly CancellationTokenSource applicationUpdateLifetime=new();
    private UpdateIndicatorState applicationUpdateState;
    private string? availableApplicationVersion;
    private static string CurrentApplicationVersion=>typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    private void InitializeApplicationUpdates() {
        LocalizedPresentation.SetPreserveOriginal(ApplicationUpdateBadge,true);
        EventHandler language=(_,_)=>RenderApplicationUpdateIndicator();LocalizedPresentation.Language.Changed+=language;
        Closed+=(_,_)=>LocalizedPresentation.Language.Changed-=language;RenderApplicationUpdateIndicator();
        var button=new Button{Content="Check for Updates",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,10)};
        button.Click+=(_,_)=>ShowApplicationUpdates(this);
        // Keep the feature-card grid intact (the update control lives directly above it).
        if(AboutFeaturesPanel.Parent is Panel parent)parent.Children.Insert(parent.Children.IndexOf(AboutFeaturesPanel),button);
        Closed+=(_,_)=>applicationUpdateLifetime.Cancel();
    }
    private void ApplicationUpdateBadge_Click(object sender,RoutedEventArgs e)=>ShowApplicationUpdates(this);
    internal void ShowPreviousUpdateFailure()=>ShowThemeDialog("Application updates",LocalizedPresentation.Language.Lookup("The update did not complete. The previous application has reopened. Check for Updates or use manual download."),true);
    private void RenderApplicationUpdateIndicator() {
        var presentation=UpdateIndicatorPresentation.Describe(applicationUpdateState,availableApplicationVersion,LocalizedPresentation.Language);
        ApplicationUpdateBadgeDot.Visibility=presentation.Notify?Visibility.Visible:Visibility.Collapsed;ApplicationUpdateBadge.ToolTip=presentation.Tooltip;
        System.Windows.Automation.AutomationProperties.SetName(ApplicationUpdateBadge,presentation.Tooltip);
    }
    private void AcceptApplicationUpdateCheck(ApplicationUpdateCheck result) {
        availableApplicationVersion=result.Latest?.Tag;applicationUpdateState=result.UpdateAvailable?UpdateIndicatorState.Available:UpdateIndicatorState.Current;RenderApplicationUpdateIndicator();
    }
    private void RememberConfirmedExecutable() {
        if(!HasCurrentExecutable()||selectedGameRoot is null)return;
        try{applicationPreferences.ConfirmExe(selectedGameRoot,selectedExe!);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Log("Could not save the executable preference: "+ex.Message);}
    }
    private void ShowApplicationUpdates(Window owner) {
        var destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","RenoDX MFG Unlocker Updates");
        using var view=new ApplicationUpdateView(applicationUpdates,CurrentApplicationVersion,destination,
            url=>{try{if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&ApplicationUpdateService.SafeUri(uri)&&uri.AbsoluteUri.StartsWith(ApplicationUpdateService.WebRoot+"/releases/tag/",StringComparison.Ordinal))Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){ShowThemeDialog("Application updates",ex.Message,true);}},
            folder=>{try{if(Directory.Exists(folder)){StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(folder)!,folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}}catch(Exception ex){ShowThemeDialog("Application updates",ex.Message,true);}},
            ()=>SelfUpdateTrust.AutomaticReady,InstallApplicationUpdateAsync);
        view.CheckStarted+=()=>{applicationUpdateState=UpdateIndicatorState.Checking;RenderApplicationUpdateIndicator();};
        view.CheckCompleted+=AcceptApplicationUpdateCheck;
        view.CheckFailed+=()=>{applicationUpdateState=UpdateIndicatorState.Unavailable;RenderApplicationUpdateIndicator();};
        var dialog=CreateInfoDialog("Application updates",view,780,810,owner);
        dialog.Closing+=(_,e)=>{if(view.CriticalRestart)e.Cancel=true;};
        dialog.Loaded+=async(_,_)=>await view.CheckAsync();dialog.ShowDialog();
    }
    private async Task<bool> InstallApplicationUpdateAsync(ApplicationRelease release,string archive,SignedUpdateMetadata signed,IProgress<string> progress,CancellationToken token) {
        if(!SelfUpdateTrust.AutomaticReady)throw new IOException(SelfUpdateTrust.BlockReason);
        if(LanguageChangeBlocked)throw new IOException("Finish active file operations before updating the application.");
        var target=Path.GetFullPath(Environment.ProcessPath??throw new IOException("Current executable unavailable."));
        var identity=SelfUpdateReplacement.ReadIdentity(target);
        if(identity.Product!="RenoDX MFG Unlocker"||identity.Version!=CurrentApplicationVersion||identity.Machine!=0x8664)throw new IOException("Current application identity is invalid.");
        PreparedSelfUpdate? prepared=null;var handedOff=false;
        try {
            progress.Report("Verifying signed package…");
            prepared=await Task.Run(()=>SelfUpdateHandoff.Prepare(new SelfUpdateVerifier(),archive,signed,release,CurrentApplicationVersion,target,selectedGameRoot,progress:(_,done,count)=>{token.ThrowIfCancellationRequested();progress.Report($"Verifying signed files: {done} / {count}");}),token);
            token.ThrowIfCancellationRequested();
            if(!ShowThemeConfirmation("Replace application and restart?",LocalizedPresentation.Language.Format("Authenticated update: {0} → {1}. RenoDX will close, back up its EXE, replace it and restart. Game files and local settings are not changed. Continue?",CurrentApplicationVersion,release.Tag)))return false;
            if(LanguageChangeBlocked)throw new IOException("Finish active file operations before updating the application.");
            progress.Report("Restarting after verified update…");handedOff=await SelfUpdateHandoff.StartAsync(prepared,token);
            if(handedOff)Application.Current.Shutdown();return handedOff;
        }finally{if(prepared is not null&&!handedOff)SelfUpdatePaths.CleanupSession(prepared.Session);}
    }
    private async Task CheckApplicationUpdatesAtLaunchAsync() {
        try {
            applicationUpdateState=UpdateIndicatorState.Checking;RenderApplicationUpdateIndicator();
            var result=await applicationUpdates.CheckAsync(CurrentApplicationVersion,false,applicationUpdateLifetime.Token);
            if(!applicationUpdateLifetime.IsCancellationRequested)AcceptApplicationUpdateCheck(result);
        }catch(OperationCanceledException){}
        catch(Exception){applicationUpdateState=UpdateIndicatorState.Unavailable;RenderApplicationUpdateIndicator();/* No startup error dialog. */}
    }
}

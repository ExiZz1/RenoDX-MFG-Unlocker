using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RenoDXMfgUnlocker;

/// <summary>Network work is explicit/cancellable; opening a folder or browser is always a user event.</summary>
internal sealed class ApplicationUpdateView:StackPanel,IDisposable
{
    internal readonly Button CheckButton=new(),DownloadButton=new(),InstallButton=new(),ReleaseButton=new(),FolderButton=new(),CancelButton=new();
    internal readonly TextBlock Status=new(),VersionText=new(),ProgressText=new();
    internal readonly TextBox Notes=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=175,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    internal readonly ProgressBar Progress=new(){Height=7,Minimum=0,Maximum=100,Margin=new Thickness(0,8,0,4)};
    private readonly TextBox errorDetails=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ApplicationUpdateService service;
    private readonly string installed,directory;
    private readonly Action<string> openRelease,openFolder;
    private readonly EventHandler languageHandler;
    private CancellationTokenSource? operation;
    private ApplicationUpdateCheck? result;
    private string state="Check for stable application updates. Download only; update manually.";
    private string? downloaded;
    private long received,total;
    private readonly Func<bool> signingReady;
    private readonly Func<ApplicationRelease,string,SignedUpdateMetadata,IProgress<string>,CancellationToken,Task<bool>>? install;
    private readonly TextBlock security=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,10)};
    internal event Action? CheckStarted;
    internal event Action<ApplicationUpdateCheck>? CheckCompleted;
    internal event Action? CheckFailed;
    internal bool CriticalRestart {get;private set;}
    internal bool Busy=>operation is not null;
    internal ApplicationUpdateView(ApplicationUpdateService service,string installed,string directory,Action<string> openRelease,Action<string> openFolder,Func<bool>? signingReady=null,Func<ApplicationRelease,string,SignedUpdateMetadata,IProgress<string>,CancellationToken,Task<bool>>? install=null) {
        this.service=service;this.installed=installed;this.directory=directory;this.openRelease=openRelease;this.openFolder=openFolder;
        this.signingReady=signingReady??(()=>SelfUpdateTrust.AutomaticReady);this.install=install;
        foreach(var text in new[]{VersionText,Status,ProgressText}){text.TextWrapping=TextWrapping.Wrap;text.Margin=new Thickness(0,5,0,8);Children.Add(text);LocalizedPresentation.SetPreserveOriginal(text,true);}
        Children.Add(Progress);
        var buttons=new WrapPanel();foreach(var button in new[]{CheckButton,InstallButton,DownloadButton,CancelButton,ReleaseButton,FolderButton}){button.Margin=new Thickness(0,4,8,8);button.Padding=new Thickness(12,8,12,8);buttons.Children.Add(button);LocalizedPresentation.SetPreserveOriginal(button,true);}Children.Add(buttons);
        LocalizedPresentation.SetPreserveOriginal(security,true);Children.Add(security);
        var notice=new TextBlock{Text=ApplicationUpdateService.IntegrityNotice,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,10)};Children.Add(notice);
        var notesHeading=new TextBlock{Text="Release Notes",Margin=new Thickness(0,8,0,5)};Children.Add(notesHeading);Children.Add(Notes);LocalizedPresentation.SetPreserveOriginal(Notes,true);
        Notes.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Application.Current.FindResource("PremiumScrollBar"));
        LocalizedPresentation.SetPreserveOriginal(errorDetails,true);Children.Add(new Expander{Header="Technical Details",Content=errorDetails,Margin=new Thickness(0,8,0,0)});
        CheckButton.Click+=async(_,_)=>await CheckAsync(true);DownloadButton.Click+=async(_,_)=>await DownloadAsync();InstallButton.Click+=async(_,_)=>await DownloadAndInstallAsync();CancelButton.Click+=(_,_)=>{if(!CriticalRestart)operation?.Cancel();};
        ReleaseButton.Click+=(_,_)=>{if(result?.Latest is {} release)openRelease(release.ReleaseUrl);};
        FolderButton.Click+=(_,_)=>{if(downloaded is not null)openFolder(Path.GetDirectoryName(downloaded)!);};
        languageHandler=(_,_)=>Refresh();LocalizedPresentation.Language.Changed+=languageHandler;Refresh();
    }
    private void Refresh() {
        var l=LocalizedPresentation.Language;
        CheckButton.Content=l.Lookup("Check for Updates");InstallButton.Content=l.Lookup("Download and Install");DownloadButton.Content=l.Lookup("Download Update");ReleaseButton.Content=l.Lookup("View Release on GitHub");FolderButton.Content=l.Lookup("Open download folder");CancelButton.Content=l.Lookup("Cancel");
        VersionText.Text=l.Format("Current application version: {0}",installed);
        if(result?.Latest is {} release){VersionText.Text+="\n"+l.Format("Latest stable release: {0} · Published {1}",release.Tag,release.Published.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture));if(release.AssetName is not null)VersionText.Text+="\n"+l.Format("Release asset: {0} · {1} bytes",release.AssetName,release.Size);}
        Status.Text=l.Translate(state);ProgressText.Text=total>0?l.Format("Downloaded {0} / {1} bytes",received,total):"";
        CheckButton.IsEnabled=!Busy;DownloadButton.IsEnabled=!Busy&&result is {UpdateAvailable:true,Latest.Sha256:not null,Latest.DownloadUrl:not null};
        security.Text=l.Lookup(!signingReady()?SelfUpdateTrust.BlockReason:result?.Latest is {Manifest:not null,Signature:not null}?"Automatic installation requires a valid signed manifest. Downloaded bytes alone do not authorize replacement.":"This release has no signed update manifest. Manual download remains available.");
        InstallButton.IsEnabled=!Busy&&install is not null&&signingReady()&&result is {UpdateAvailable:true,Latest:{Sha256:not null,DownloadUrl:not null,Manifest:not null,Signature:not null}};
        InstallButton.ToolTip=security.Text;ToolTipService.SetShowOnDisabled(InstallButton,true);
        CancelButton.IsEnabled=Busy&&!CriticalRestart;ReleaseButton.IsEnabled=result?.Latest is not null;FolderButton.IsEnabled=downloaded is not null;
        Progress.Value=total>0?100.0*received/total:0;
        Progress.IsIndeterminate=Busy&&state.StartsWith("Verifying",StringComparison.Ordinal);
        Progress.Visibility=Busy&&(state.StartsWith("Downloading",StringComparison.Ordinal)||state.StartsWith("Verifying",StringComparison.Ordinal)||CriticalRestart)?Visibility.Visible:Visibility.Collapsed;
    }
    internal async Task CheckAsync(bool force=false) {
        if(Busy)return;operation=new();state="Checking official GitHub releases…";errorDetails.Text="";Refresh();
        CheckStarted?.Invoke();
        try {
            result=await service.CheckAsync(installed,force,operation.Token);
            Notes.Text=UpdateNotesSummary.Readable(result.Latest?.Notes);CheckCompleted?.Invoke(result);
            state=result.Latest is null?"No eligible stable application release was found.":!result.UpdateAvailable?"No newer stable version is available.":result.Latest.Sha256 is null||result.Latest.DownloadUrl is null?"Update available. Automatic download requires the official Windows x64 ZIP and GitHub SHA-256 digest. Use the release page for manual review.":"A newer stable version is available. Download the ZIP, then update manually.";
        }catch(OperationCanceledException){state="Update operation cancelled or timed out. You can retry.";CheckFailed?.Invoke();}
        catch(Exception ex){result=null;state="Update check failed. Check your connection or GitHub rate limits, then retry.";errorDetails.Text=ex.Message;CheckFailed?.Invoke();}
        finally{operation.Dispose();operation=null;Refresh();}
    }
    internal async Task DownloadAndInstallAsync() {
        if(Busy||install is null||!signingReady()||result is not {UpdateAvailable:true,Latest:{Manifest:not null,Signature:not null,Sha256:not null} release})return;
        operation=new();state="Downloading official release ZIP…";errorDetails.Text="";received=0;total=release.Size;Refresh();
        try {
            downloaded=await service.DownloadAsync(release,directory,new Progress<StreamlineDownloadProgress>(p=>{received=p.BytesReceived;total=p.TotalBytes??release.Size;Refresh();}),operation.Token);
            state="Verifying signed package…";Refresh();var signed=await service.GetSignedMetadataAsync(release,operation.Token);
            var progress=new Progress<string>(message=>{state=message;CriticalRestart=message=="Restarting after verified update…";Refresh();});
            var committed=await install(release,downloaded,signed,progress,operation.Token);
            state=committed?"Restarting after verified update…":"Update cancelled. The verified ZIP remains available for manual installation.";
        }catch(OperationCanceledException){state="Update operation cancelled or timed out. You can retry.";}
        catch(Exception ex){state="Automatic update was blocked or failed. The current installation is preserved; use manual download if needed.";errorDetails.Text=ex.Message;}
        finally{CriticalRestart=false;operation.Dispose();operation=null;Refresh();}
    }
    internal async Task DownloadAsync() {
        if(Busy||result is not {UpdateAvailable:true,Latest:{} release})return;operation=new();state="Downloading official release ZIP…";errorDetails.Text="";received=0;total=release.Size;Refresh();
        try {
            var progress=new Progress<StreamlineDownloadProgress>(p=>{received=p.BytesReceived;total=p.TotalBytes??release.Size;Refresh();});
            downloaded=await service.DownloadAsync(release,directory,progress,operation.Token);
            received=total;state="Update ZIP verified and saved. Nothing was installed or executed. Close this app before updating manually.";
        }catch(OperationCanceledException){state="Update operation cancelled or timed out. You can retry.";}
        catch(Exception ex){state="Update download failed. Existing completed files were preserved. Check Details and retry.";errorDetails.Text=ex.Message;}
        finally{operation.Dispose();operation=null;Refresh();}
    }
    public void Dispose(){LocalizedPresentation.Language.Changed-=languageHandler;operation?.Cancel();}
}

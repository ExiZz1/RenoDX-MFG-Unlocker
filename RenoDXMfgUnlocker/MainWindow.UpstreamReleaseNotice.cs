using System.Windows;

namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    private readonly MfgUpstreamReleaseNotice upstreamReleaseNotice=new();
    private readonly CancellationTokenSource upstreamNoticeLifetime=new();
    private string? latestUpstreamNotice;
    private void InitializeUpstreamReleaseNotice()
    {
        EventHandler language=(_,_)=>{if(Dispatcher.CheckAccess())RenderUpstreamReleaseNotice();else Dispatcher.BeginInvoke(new Action(RenderUpstreamReleaseNotice));};
        LocalizedPresentation.Language.Changed+=language;
        Closed+=(_,_)=>{upstreamNoticeLifetime.Cancel();LocalizedPresentation.Language.Changed-=language;};
    }
    private async Task CheckUpstreamReleaseNoticeAsync()
    {
        try{
            var tag=await upstreamReleaseNotice.CheckIfDueAsync(upstreamNoticeLifetime.Token);
            if(upstreamNoticeLifetime.IsCancellationRequested)return;
            latestUpstreamNotice=tag;RenderUpstreamReleaseNotice();
        }catch(OperationCanceledException){}
        catch(Exception ex)when(ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException){/* Optional notifications cannot block startup. */}
    }
    private void RenderUpstreamReleaseNotice()
    {
        var notify=MfgUpstreamReleaseNotice.HasUnreviewedRelease(latestUpstreamNotice,approvedUpstreamSnapshot?.Tag);
        UpstreamReleaseNoticeText.Visibility=notify?Visibility.Visible:Visibility.Collapsed;
        UpstreamReleaseNoticeText.Text=notify?LocalizedPresentation.Language.Format("Official addon release {0} is available for review. Analyze its source to inspect settings; nothing has been imported.",latestUpstreamNotice!):"";
    }
}

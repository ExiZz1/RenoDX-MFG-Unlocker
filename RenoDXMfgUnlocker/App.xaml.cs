namespace RenoDXMfgUnlocker;
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        if(e.Args.Length>0&&e.Args[0]=="--self-update-helper") {
            ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
            Shutdown(await SelfUpdateHandoff.RunWorkerAsync(e.Args));return;
        }
        if(SelfUpdateTrust.Configured&&e.Args.Length==2&&e.Args[0]=="--self-update-complete"&&Guid.TryParseExact(e.Args[1],"N",out _)) {
            var session=System.IO.Path.Combine(SelfUpdatePaths.ProductionRoot,e.Args[1]);
            _=Task.Run(async()=>{await Task.Delay(TimeSpan.FromSeconds(5));SelfUpdatePaths.CleanupSession(session,true);});
        }
        LocalizedPresentation.Initialize(UiLocalization.LoadUserPreference());
        _ = Task.Run(()=>SelfUpdatePaths.CleanupStaleSessions());
        base.OnStartup(e);
        var main=new MainWindow();MainWindow=main;main.Show();
        if(e.Args.Length==2&&e.Args[0]=="--self-update-failed"&&Guid.TryParseExact(e.Args[1],"N",out _)) {
            _ = Dispatcher.BeginInvoke(new Action(()=>{
                if(MainWindow is MainWindow window)window.ShowPreviousUpdateFailure();
            }),System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}

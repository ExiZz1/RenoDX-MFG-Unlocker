using System.Windows;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        application.Startup+=async(_,_)=>{
            try{application.Shutdown(await FixtureScenario.Run(args));}
            catch(Exception ex){Console.Error.WriteLine(ex);application.Shutdown(96);}
        };
        return application.Run();
    }
}
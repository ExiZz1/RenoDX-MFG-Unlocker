using System.IO;
using System.Windows;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

// All loaded WPF checks share one STA dispatcher, just like the application.
// Ending an STA between checks while WPF still owns HWND callbacks is unsafe.
internal static class WpfCheckHost
{
    private static Dispatcher? dispatcher;
    private static Thread? thread;
    public static void Run(Action action)
    {
        if(dispatcher is null){
            using var ready=new ManualResetEventSlim();Exception? error=null;
            thread=new Thread(()=>{try{
                var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var source=System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory,"AppResources.xaml"));var resources=source.Root!.Elements().Single();
                app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"+string.Join("",resources.Elements())+"</ResourceDictionary>");
                LocalizedPresentation.Initialize(new UiLocalization());dispatcher=Dispatcher.CurrentDispatcher;ready.Set();Dispatcher.Run();
            }catch(Exception ex){error=ex;ready.Set();}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.Wait();if(error is not null)throw error;
        }
        dispatcher!.Invoke(action);
    }
    public static void Close(){if(dispatcher is null)return;dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);if(!thread!.Join(TimeSpan.FromSeconds(10)))throw new TimeoutException("WPF test dispatcher did not stop.");}
}

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class StableReleaseChecks
{
    internal static IEnumerable<(string Name,Action Run)> All(){
        yield return("stable polish derives Stable alpha beta RC and test labels from version metadata",Versions);
        yield return("stable polish Home has no hardcoded ALPHA and starts with a hidden badge",Source);
        yield return("stable polish badge hides Stable and preserves live six-language translation unshown WPF",Badge);
        yield return("stable polish prerelease catalogs have six-language key parity",Catalogs);
    }
    private static Type Status=>typeof(MainWindow).Assembly.GetType("RenoDXMfgUnlocker.ApplicationReleaseStatus",true)!;
    private const BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    private static string? Label(string? value)=>(string?)Status.GetMethod("Label",Flags)!.Invoke(null,[value]);
    private static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    private static void Versions(){
        foreach(var version in new[]{"1.2.0","v1.2.0","1.2.0+local","1.3.0"})Check(Label(version) is null,"Stable incorrectly marked prerelease: "+version);
        foreach(var (version,label) in new[]{("1.2.0-alpha.7","ALPHA"),("1.2.0-beta.2","BETA"),("1.2.0-rc.6","RC"),("1.3.0-RC.1","RC"),("1.3.0-test.1","TEST"),("1.3.0-dev.1","PREVIEW")})Check(Label(version)==label,"Wrong version-derived channel: "+version);
        Check(Label(null)=="Unknown"&&Label("invalid")=="Unknown","Unknown version falsely presented as Stable");
    }
    private static void Source(){
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        var xaml=XDocument.Load(Path.Combine(root,"RenoDXMfgUnlocker","MainWindow.xaml"));XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var badge=xaml.Descendants().Single(n=>(string?)n.Attribute(x+"Name")=="HomeReleaseBadge");
        Check((string?)badge.Attribute("Visibility")=="Collapsed","Stable badge visible before initialization");
        Check(!badge.Descendants().Any(n=>(string?)n.Attribute("Text")=="ALPHA"),"Hardcoded alpha remains");
        var code=File.ReadAllText(Path.Combine(root,"RenoDXMfgUnlocker","MainWindow.xaml.cs"));
        Check(code.Contains("ApplicationReleaseStatus.Apply(HomeReleaseBadge,HomeReleaseStatusText,appVersion)"),"Badge not connected to actual informational version");
    }
    private static void Badge()=>WpfCheckHost.Run(()=>{
        var language=LocalizedPresentation.Language;var original=language.CultureName;
        var text=new TextBlock();var badge=new Border{Child=text};var apply=Status.GetMethod("Apply",Flags)!;
        using var hidden=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Release badge test - no visible window") {Width=1,Height=1,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000)});
        try{
            apply.Invoke(null,[badge,text,"1.2.0-alpha.1"]);hidden.RootVisual=badge;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(text.IsLoaded,"Hidden presentation source did not load the test control");
            // HwndSource has no normal Window Loaded traversal; attach the real localization
            // handler explicitly, then exercise its actual live refresh without showing UI.
            typeof(LocalizedPresentation).GetMethod("Attach",Flags)!.Invoke(null,[text]);
            foreach(var key in new[]{"ALPHA","BETA","RC","TEST","PREVIEW"}){
                var suffix=key=="PREVIEW"?"dev":key.ToLowerInvariant();apply.Invoke(null,[badge,text,"1.3.0-"+suffix+".1"]);
                foreach(var culture in UiLocalization.Cultures){language.SetCulture(culture,false);Check(badge.Visibility==Visibility.Visible&&text.Text==language.Lookup(key),"Incorrect live prerelease translation: "+culture+" / "+key+"; actual="+text.Text+"; expected="+language.Lookup(key)+"; visibility="+badge.Visibility);}
            }
            apply.Invoke(null,[badge,text,"1.2.0"]);
            foreach(var culture in UiLocalization.Cultures){language.SetCulture(culture,false);Check(badge.Visibility==Visibility.Collapsed&&text.Text.Length==0,"Stale prerelease marker in Stable: "+culture);}
        }finally{hidden.RootVisual=null;System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);language.SetCulture(original,false);}
    });
    private static void Catalogs(){var language=new UiLocalization();Check(language.ValidateCatalogs().Count==0,"Catalog parity failed");foreach(var culture in UiLocalization.Cultures)foreach(var key in new[]{"ALPHA","BETA","RC","TEST","PREVIEW"})Check(language.GetCatalog(culture).ContainsKey(key)&&language.Lookup(key,culture).Length>0,"Missing channel label: "+culture+" / "+key);}
}

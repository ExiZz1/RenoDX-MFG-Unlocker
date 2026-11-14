using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Dev5LocalizationChecks
{
    private static readonly string[] Cultures=["en-US","uk-UA","pl-PL","de-DE","es-ES","fr-FR","cs-CZ","pt-BR"];
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private sealed record ExceptionEntry(string Culture,string Key,string Reason);
    private static ExceptionEntry[] Exceptions(){using var stream=typeof(UiLocalization).Assembly.GetManifestResourceStream("RenoDXMfgUnlocker.Localization.technical-exceptions.json")!;return JsonSerializer.Deserialize<ExceptionEntry[]>(stream)!;}
    internal static bool Allowed(string culture,string key)=>Exceptions().Any(e=>e.Culture==culture&&e.Key==key&&!string.IsNullOrWhiteSpace(e.Reason));
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("dev5 eight locale resources names keys placeholders and format strings are complete",Integrity);
        yield return("dev5 identical English values require reviewed technical or native-language exceptions",English);
        yield return("dev5 English sentence detection rejects injected untranslated prose but permits named technologies",Detection);
        yield return("dev5 all 39 current setting names descriptions and dependencies render in eight languages",Definitions);
        yield return("dev5 upstream messages localize while code keys evidence and original quotations remain exact",Upstream);
        yield return("dev5 future upstream labels are never replaced with invented translations",Future);
        yield return("dev5 eight language changes preserve actual row pending edits choices expansion and INI bytes",Pending);
        yield return("dev5 fallback and temporary language persistence preserve unknown technical values",Fallback);
        yield return("dev5 count-neutral grammar quantity formatting and dot-separated versions remain correct",Numbers);
        yield return("dev5 original signed revision 2 schema and 39 definition constraints are unchanged",SignedData);
        yield return("dev5 localization cannot create catch-all templates or mutate SDK record identifiers",Templates);
    }
    private static void Integrity()
    {
        Check(UiLocalization.Cultures.SequenceEqual(Cultures)&&UiLocalization.LanguageNames.Length==8,"Locale list changed");Check(UiLocalization.LanguageNames[6]=="Čeština"&&UiLocalization.LanguageNames[7]=="Português (Brasil)","Wrong dialect/name");
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,string.Join("\n",l.ValidateCatalogs()));
        foreach(var c in Cultures){Check(CultureInfo.GetCultureInfo(c).Name==c,"Culture unavailable");var data=l.GetCatalog(c);Check(data.Count==l.KeyCount&&data.Values.All(v=>!string.IsNullOrWhiteSpace(v)),"Missing/empty strings");foreach(var (key,value) in data){Check(CompositeFormat.Parse(value).MinimumArgumentCount==CompositeFormat.Parse(key).MinimumArgumentCount,"Format arguments changed: "+c+" "+key);Check(!Regex.IsMatch(value,@"ZXQ\d{4}QXZ|^\s*\d{4}\s*[|｜]"),"Draft marker leaked");}}
    }
    private static void English()
    {
        var l=new UiLocalization();foreach(var c in Cultures.Skip(1))foreach(var (key,value) in l.GetCatalog(c))if(value==l.GetCatalog("en-US")[key])Check(Allowed(c,key),"Unreviewed English: "+c+" / "+key);
        foreach(var c in Cultures.Skip(1))foreach(var (key,value) in l.GetCatalog(c))Check(!MixedEnglish(value),"Possible mixed English prose: "+c+" / "+key);
        foreach(var e in Exceptions())Check(l.GetCatalog(e.Culture).ContainsKey(e.Key)&&e.Reason.Length>10,"Stale/empty exception");
    }
    private static bool MixedEnglish(string value)
    {
        var text=Regex.Replace(value,@"The Witcher 3(?:: Wild Hunt [—–-] Remastered)?","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return Regex.IsMatch(text,@"\b(?:the|must|requires|should|does not|read-only|No changes|Current version|Saved value|New value)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
    private static bool Suspect(string culture,string key,string value)=>culture!="en-US"&&key==value&&!Allowed(culture,key);
    private static void Detection()
    {
        foreach(var c in Cultures.Skip(1)){Check(Suspect(c,"The file has not been verified. Do not install it.","The file has not been verified. Do not install it."),"English prose missed");Check(!Suspect(c,"NVIDIA DLSS-G","NVIDIA DLSS-G"),"Brand treated as English error");}
        Check(MixedEnglish("Soubor: The current file has not been verified."),"Mixed prose missed");Check(!MixedEnglish("C:\\Games\\The Witcher 3\\witcher3.exe"),"Opaque game name flagged");
        Check(!Suspect("pt-BR","Experimental","Experimental"),"Correct Portuguese cognate rejected");
    }
    private static void Definitions()
    {
        var catalog=MfgDynamicSettingsCatalog.ReviewedFallback();var l=new UiLocalization();
        var definitions=new MfgUnlockSettingsService(false,catalog).DefinitionsFor("1.8.2").Concat(new MfgUnlockSettingsService(true,catalog).DefinitionsFor("1.8.2")).ToArray();Check(definitions.Length==39,"Definitions lost");
        var root=Path.Combine(Path.GetTempPath(),"RenoDX-dev5-definitions-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{foreach(var c in Cultures){l.SetCulture(c,false);foreach(var d in definitions){Check(!string.IsNullOrWhiteSpace(MfgSettingsDisplay.Label(d,l))&&!string.IsNullOrWhiteSpace(MfgSettingsDisplay.Description(d,l)),"Missing setting presentation");if(c!="en-US"){Check(MfgSettingsDisplay.Label(d,l)!=d.Key,"Technical key used as title: "+d.Key);var english=d.CatalogEntry?.Translations?.GetValueOrDefault("en-US")?.Description??d.Description;Check(MfgSettingsDisplay.Description(d,l)!=english,"English description: "+c+" "+d.Key);}}
            var service=new MfgUnlockSettingsService(true,catalog);var state=service.Load(Path.Combine(root,"MFGAdaUnlock.ini"),root,"1.8.2");
            var queue=state.Settings.Single(s=>s.Definition.Key=="XeFGFlipQueueBufferPct").Definition;var availability=MfgSettingsAvailability.Evaluate(service,state,queue,new Dictionary<string,string>(),true,true,true,false,false);Check(!availability.CanEdit&&availability.Message(l).Contains(MfgSettingsDisplay.Label(state.Settings.Single(s=>s.Definition.Key=="XeFGFlipQueue").Definition,l)),"Dependency explanation missing");
        }}finally{Directory.Delete(root,true);}
        l.SetCulture("cs-CZ",false);Check(MfgSettingsDisplay.Label(definitions.Single(d=>d.Key=="XeFGFlipMeteringLeadUs"),l).Contains("Předstih"),"Submission lead mistranslated");
    }
    private static void Upstream()
    {
        var raw=MfgUpstreamSettingsAnalyzer.Analyze(Dev4UpstreamChecks.Snapshot());var f=raw.Findings.First(e=>e.Definition is not null) with{Description="Original upstream quotation: 1.8.2 [XeFG] enabled=1; do not translate."};var analysis=raw with{Findings=[f]};var l=new UiLocalization();var baseline=MfgDynamicSettingsCatalog.ReviewedFallback();
        foreach(var c in Cultures){l.SetCulture(c,false);var text=MfgUpstreamSettingsPresentation.Render(analysis,baseline,raw.Snapshot,[new(f.Id,"Default changed")],l);
            Check(text.Contains(l.Lookup(f.Reason))&&text.Contains(l.Lookup("Default changed")),"Generated messages not localized");Check(text.Contains(f.Description)&&text.Contains(raw.Snapshot.Commit)&&text.Contains("["+f.Section+"] "+f.Key)&&text.Contains(f.Definition!.Evidence),"Source evidence altered");if(c!="en-US")Check(!text.Contains(f.Reason),"English analyzer message leaked");
        }
    }
    private static void Future()
    {
        var entry=new MfgCatalogEntry("MFGAdaUnlock.ini","Diagnostics","FutureOriginal","Boolean","0","2.0.0",null,"Advanced","Do not invent a translation of future source text.","https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/3b8889f98cd0cf50e4f7d5daca730fc6084ff57d/src/addons/mfgunlock/advanced_settings.hpp",true,Translations:new(){{"en-US",new("Home","Do not invent a translation of future source text.")}});
        var d=new MfgUnlockSettingDefinition(entry.Key,"Home",entry.Description,"0",true,[],"Diagnostics",entry){IsUpstreamSource=true};var l=new UiLocalization();foreach(var c in Cultures){l.SetCulture(c,false);Check(MfgSettingsDisplay.Label(d,l)=="Home"&&MfgSettingsDisplay.Description(d,l).EndsWith(entry.Description),"Future source text guessed");}
    }
    private static void Pending()
    {
        var root=Path.Combine(Path.GetTempPath(),"RenoDX-dev5-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"MFGAdaUnlock.ini");File.WriteAllText(file,"; untouched\n[Backend]\nSelected=DLSSG\n[DLSSG]\nXmxGenerator=0\n");var before=File.ReadAllBytes(file);
        // Isolated display-language model: do not dispatch application/window refreshes from a console test thread.
        var languageProperty=typeof(LocalizedPresentation).GetProperty("Language",BindingFlags.Public|BindingFlags.Static)!;
        var previous=(UiLocalization)languageProperty.GetValue(null)!;var l=new UiLocalization();languageProperty.SetValue(null,l);
        try{
            var service=new MfgUnlockSettingsService(true,MfgDynamicSettingsCatalog.ReviewedFallback());var snapshot=service.Load(file,root,"1.8.2");var d=snapshot.Settings.Single(s=>s.Definition.Key=="XeFGNativeLatency").Definition;
            var rowType=typeof(MainWindow).GetNestedType("MfgUnlockSettingRow",BindingFlags.NonPublic)!;var row=Activator.CreateInstance(rowType,[d,"saved","3",true,(Action)(()=>{})])!;var proposed=rowType.GetProperty("ProposedValue")!;var changed=new Dictionary<string,string>{{"XeFGNativeLatency","3"}};var expected=service.CreateUpdatedBytes(snapshot,changed);
            var choices=rowType.GetProperty("DisplayChoices")!.GetValue(row);var groups=new MfgSettingsGroupStore(l);var group=groups.For(service.ConfigFileName,d);
            foreach(var c in Cultures){l.SetCulture(c,false);group.IsExpanded=!group.IsExpanded;rowType.GetMethod("RefreshLanguage")!.Invoke(row,null);Check((string?)proposed.GetValue(row)=="3"&&ReferenceEquals(choices,rowType.GetProperty("DisplayChoices")!.GetValue(row)),"Pending/choice objects lost");Check(expected.SequenceEqual(service.CreateUpdatedBytes(snapshot,changed))&&File.ReadAllBytes(file).SequenceEqual(before),"Language affected INI");}
        }finally{languageProperty.SetValue(null,previous);Directory.Delete(root,true);}
    }
    private static void Fallback()
    {
        var l=new UiLocalization();foreach(var c in Cultures){l.SetCulture(c,false);Check(l.Lookup("Home","not-a-culture")=="Home","English fallback missing");foreach(var opaque in new[]{"[Backend] Selected=DLSSG","C:\\Unknown\\On\\nvngx_dlssg.dll","f1234abc","vendor-version-z"})Check(l.Lookup(opaque)==opaque,"Opaque fallback mutated");}
        var root=Path.Combine(Path.GetTempPath(),"RenoDX-dev5-lang-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{var path=Path.Combine(root,"language.json");foreach(var c in Cultures){new UiLocalization(path).SetCulture(c);Check(new UiLocalization(path).CultureName==c,"Persistence failed");}}finally{Directory.Delete(root,true);}
    }
    private static void Numbers()
    {
        var l=new UiLocalization();foreach(var c in Cultures){l.SetCulture(c,false);foreach(var n in new[]{0,1,2,5,21,1234})Check(l.Translate(n.ToString(CultureInfo.InvariantCulture)+" game files")==l.Format("{0} game files",n.ToString("N0",l.Culture)),"Count-neutral grammar failed: "+c);Check(TechnicalVersionDisplay.Format("1,3,1,78")=="1.3.1.78"&&TechnicalVersionDisplay.Format("310,9,16,0")=="310.9.16.0","Version data changed");}
    }
    private static void SignedData()
    {
        var a=typeof(MfgDynamicSettingsCatalog).Assembly;byte[] Read(string name){using var s=a.GetManifestResourceStream("RenoDXMfgUnlocker.SettingsCatalog."+name)!;using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
        var json=Read("reviewed-r2.json");var signature=Read("reviewed-r2.sig");Check(Convert.ToHexString(SHA256.HashData(json))=="7EA564599435600158AC207873880A89AA304FF7814E2147B97011C0805AE977"&&Convert.ToHexString(SHA256.HashData(signature))=="BED1C94359C28A1F3745220D31D7EA5540654F3FC2ED4A8B2D70A8F55B3B5C4D","Signed files changed");Check(MfgDynamicSettingsCatalog.Verify(json,signature).Revision==2,"Signature failed");using var d=JsonDocument.Parse(json);Check(d.RootElement.GetProperty("Entries").GetArrayLength()==24,"Entry count changed");
    }
    private static void Templates()
    {
        var l=new UiLocalization();Check(!l.GetCatalog("en-US").Keys.Any(k=>Regex.IsMatch(k,@"^\s*\{\d+\}\s*$")),"Catch-all template added");
        foreach(var c in Cultures){var text=l.Lookup("XeMFG SDK output at {0} · enabled={1} · frames_presented={2} · interpolation_result={3} · tensor_launches={4}",c);foreach(var field in new[]{"enabled={1}","frames_presented={2}","interpolation_result={3}","tensor_launches={4}"})Check(text.Contains(field),"SDK identifier translated");}
        var xaml=XDocument.Load(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.xaml"));XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";Check(xaml.Descendants().Any(e=>(string?)e.Attribute(x+"Name")=="CheckUpstreamSettingsButton"),"Upstream check removed");
        var code=File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.SettingsCatalog.cs"));Check(code.Contains("MfgUpstreamSettingsPresentation.Render("),"Late upstream text bypasses localized presenter");
    }
}

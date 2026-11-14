using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Dev3SettingsChecks
{
    private static readonly string[] QueueKeys=["XeFGFlipQueueBufferPct","XeFGFlipQueueFirst","XeFGNativeBuffers"];
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("dev3 three queue fields expose exact disabled prerequisite without changing values",QueueDefaults);
        yield return("dev3 saved and pending queue enable disable agrees with writer authority",QueueTransitions);
        yield return("dev3 queue version floor and future capability inheritance remain unchanged",Versions);
        yield return("dev3 unavailable explanations distinguish process invalid version XMX and missing dependency",Reasons);
        yield return("dev3 all 38 display labels descriptions and help headings localize in six languages",AllText);
        yield return("dev3 choices refresh labels in place without changing exact selection values",Choices);
        yield return("dev3 saved default invalid and opaque values retain exact technical payloads",SavedValues);
        yield return("dev3 typed constraints localize words while preserving ranges and versions",Constraints);
        yield return("dev3 late MFG text bindings and status renderers preserve localization source keys",XamlText);
        yield return("dev3 actual row retains pending value and choice objects when availability changes",RowState);
        yield return("dev3 localized display cannot enter INI writes and original bytes restore after rollback",Roundtrip);
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException or IOException){return;}throw new Exception("Invalid edit accepted");}
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private sealed class Fixture:IDisposable
    {
        internal string Base=Path.Combine(Path.GetTempPath(),"RenoDX-dev3",Guid.NewGuid().ToString("N"));
        internal string Game=>Path.Combine(Base,"game");
        internal string File=>Path.Combine(Game,"MFGAdaUnlock.ini");
        internal MfgUnlockSettingsService Service=new(true,MfgDynamicSettingsCatalog.BuiltIn);
        internal Fixture(string? text=null){Directory.CreateDirectory(Game);if(text is not null)System.IO.File.WriteAllText(File,text,new UTF8Encoding(true));}
        internal MfgUnlockSettingsSnapshot Load(string version="1.8.2")=>Service.Load(File,Game,version);
        public void Dispose(){Directory.Delete(Base,true);}
    }
    private static MfgSettingAvailability Availability(Fixture f,MfgUnlockSettingsSnapshot s,string key,Dictionary<string,string>? proposed=null,bool allowed=true,bool current=true,bool closed=true,bool busy=false,bool xmx=false)
        =>MfgSettingsAvailability.Evaluate(f.Service,s,f.Service.DefinitionsFor("1.8.2").Single(d=>d.Key==key),proposed??[],allowed,current,closed,busy,xmx);
    private static void QueueDefaults()
    {
        using var f=new Fixture();var s=f.Load();var before=s.OriginalBytes.ToArray();
        foreach(var key in QueueKeys){var state=s.Settings.Single(x=>x.Definition.Key==key);var e=state.Definition.CatalogEntry!;Check(e.MinimumAddon=="1.8.0"&&e.Requires is [{Key:"XeFGFlipQueue",Value:"1"}],"Unexpected prerequisite");var availability=Availability(f,s,key);Check(!availability.CanEdit&&availability.Required?.Key=="XeFGFlipQueue"&&availability.MessageKey=="Unavailable: enable {0} first.","No precise prerequisite explanation");Check(!f.Service.CanEdit(state.Definition,s.Settings),"Service disagrees with UI");}
        Check(s.Settings.Single(x=>x.Definition.Key=="XeFGFlipQueue").EffectiveValue=="0"&&before.SequenceEqual(s.OriginalBytes)&&!System.IO.File.Exists(f.File),"Inspection changed defaults or created configuration");
    }
    private static void QueueTransitions()
    {
        using var f=new Fixture("; keep\n[XeFG]\nXeFGFlipQueue=0\nXeFGFlipQueueBufferPct=50\n");var s=f.Load();var before=System.IO.File.ReadAllBytes(f.File);
        var proposed=new Dictionary<string,string>{{"XeFGFlipQueue","1"}};
        foreach(var key in QueueKeys)Check(Availability(f,s,key,proposed).CanEdit,"Pending queue enable not recognized");
        Check(proposed.Count==1&&proposed["XeFGFlipQueue"]=="1","UI inserted dependency values");
        var bytes=f.Service.CreateUpdatedBytes(s,proposed);Check(!Encoding.UTF8.GetString(bytes).Contains("XeFGNativeBuffers="),"Default buffer was written automatically");
        System.IO.File.WriteAllBytes(f.File,bytes);s=f.Load();foreach(var key in QueueKeys)Check(Availability(f,s,key).CanEdit,"Saved queue enable not recognized");
        proposed["XeFGFlipQueue"]="0";foreach(var key in QueueKeys)Check(!Availability(f,s,key,proposed).CanEdit,"Pending disable ignored");
        proposed["XeFGFlipQueueBufferPct"]="60";Reject(()=>f.Service.CreateUpdatedBytes(s,proposed));Check(System.IO.File.ReadAllBytes(f.File).SequenceEqual(bytes),"Rejected dependent edit changed file");
    }
    private static void Versions()
    {
        using var f=new Fixture();foreach(var version in new[]{"1.8.0","1.8.1","1.8.2","1.9.0","2.0.0"})foreach(var key in QueueKeys)Check(Availability(f,f.Load(version),key).Required?.Key=="XeFGFlipQueue","Future version incorrectly blocked");
        Check(!f.Load("1.7.9").Settings.Any(x=>QueueKeys.Contains(x.Definition.Key)),"Pre-1.8 schema gained fields");Check(!Availability(f,f.Load("1.7.9"),QueueKeys[0]).CanEdit,"Version floor bypassed");Check(Availability(f,f.Load("unknown"),QueueKeys[0]).MessageKey.Contains("version"),"Unknown version has misleading dependency message");
    }
    private static void Reasons()
    {
        using var f=new Fixture();var s=f.Load();Check(Availability(f,s,QueueKeys[0],closed:false).MessageKey.Contains("close the game"),"No process reason");Check(Availability(f,s,QueueKeys[0],busy:true).MessageKey.Contains("operation"),"No busy reason");Check(Availability(f,s,QueueKeys[0],current:false).MessageKey.Contains("select the game"),"No stale selection reason");Check(Availability(f,s,QueueKeys[0],allowed:false).MessageKey.Contains("reload"),"No reload reason");
        var missing=s with{Settings=s.Settings.Where(x=>x.Definition.Key!="XeFGFlipQueue").ToArray()};Check(Availability(f,missing,QueueKeys[0]).MessageKey.Contains("missing or invalid"),"Missing prerequisite misreported");
        System.IO.File.WriteAllText(f.File,"[XeFG]\nXeFGFlipQueue=not-an-integer\n");Check(Availability(f,f.Load(),QueueKeys[0]).MessageKey.Contains("invalid values"),"Invalid parent ignored");
        var main=new MfgUnlockSettingsService();var path=Path.Combine(f.Game,"ReShade.ini");var mainSnapshot=main.Load(path,f.Game,"1.8.2");var temporal=mainSnapshot.Settings.Single(x=>x.Definition.Key=="TemporalFix").Definition;
        Check(!MfgSettingsAvailability.Evaluate(main,mainSnapshot,temporal,new Dictionary<string,string>(),true,true,true,false,true).CanEdit,"XMX lockout bypassed");
        var l=new UiLocalization();foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);foreach(var key in QueueKeys){var message=Availability(f,s,key).Message(l);Check(message.Contains(MfgSettingsDisplay.Label(s.Settings.Single(x=>x.Definition.Key=="XeFGFlipQueue").Definition,l))&&!message.Contains("XeFGFlipQueue"),"Dependency caption was not localized");if(culture!="en-US")Check(!message.StartsWith("Unavailable:"),"English disabled explanation");}}
    }
    private static IEnumerable<(MfgUnlockSettingsService Service,MfgUnlockSettingDefinition Definition)> Definitions()=>new[]{new MfgUnlockSettingsService(false,MfgDynamicSettingsCatalog.BuiltIn),new MfgUnlockSettingsService(true,MfgDynamicSettingsCatalog.BuiltIn)}.SelectMany(s=>s.DefinitionsFor("1.8.2").Select(d=>(s,d)));
    private static void AllText()
    {
        var all=Definitions().ToArray();Check(all.Length==38,"Definition count changed");var l=new UiLocalization();var strings=new MfgSettingsStrings(l);var changed=false;strings.PropertyChanged+=(_,e)=>changed|=e.PropertyName=="Item[]";Check(l.ValidateCatalogs().Count==0,"Catalog parity failure");
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);changed=false;strings.RefreshLanguage();Check(changed,"Static MFG text did not notify language refresh");foreach(var text in MfgSettingsStrings.ResourceKeys)Check(strings[text.Key]==l.Lookup(text.Value),"Late-bound static text not localized");foreach(var (service,d) in all){Check(MfgSettingsDisplay.Label(d,l).Length>0&&MfgSettingsDisplay.Description(d,l).Length>0,"Missing description");if(d.CatalogEntry is null&&culture!="en-US")Check(MfgSettingsDisplay.Description(d,l)!=d.Description,"Untranslated legacy description: "+d.Key+" / "+culture);
            foreach(var help in MainWindow.HelpSections(d,service.ConfigFileName,service.SectionFor(d))){Check(l.GetCatalog(culture).ContainsKey(help.Heading),"Missing help heading: "+help.Heading);var content=help.ContentFor(l);if(help.IsTechnical&&help.Formatter is null)Check(content==help.Content,"Technical/source evidence translated");else if(help.Formatter is null&&!string.IsNullOrWhiteSpace(help.Content)){Check(l.GetCatalog(culture).ContainsKey(help.Content),"Untranslated help content: "+help.Content);if(culture!="en-US"&&help.Content.Contains(' ')&&!Dev5LocalizationChecks.Allowed(culture,help.Content))Check(content!=help.Content,"English help fallback: "+culture+" / "+d.Key+" / "+help.Heading+" / "+help.Content);}}
        }
        foreach(var key in MfgSettingsStrings.ResourceKeys.Values)Check(l.GetCatalog(culture).ContainsKey(key),"Missing static text key: "+key);
        }
    }
    private static void Choices()
    {
        var l=new UiLocalization();var source=Definitions().SelectMany(x=>x.Definition.Choices).ToArray();var choices=source.Select(c=>new MfgSettingChoiceDisplay(c,l)).ToArray();var values=choices.Select(x=>x.Value).ToArray();
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);for(var i=0;i<choices.Length;i++){var c=choices[i];c.RefreshLanguage();Check(c.Value==values[i],"Serialized choice changed");Check(c.Label==(source[i].Label==source[i].Value?source[i].Label:l.Lookup(source[i].Label)),"Choice caption not localized");if(c.Label==l.Lookup("Off")||c.Label==l.Lookup("On"))Check(c.Value is "0" or "1","Boolean display changed the stored value");}}
        var token=new MfgSettingChoiceDisplay(new("On","On"),l);Check(token.Label=="On"&&token.Value=="On","Exact vendor token translated");
    }
    private static void SavedValues()
    {
        var d=Definitions().First().Definition;var l=new UiLocalization();foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);foreach(var raw in new[]{"On","Off","Enabled","Disabled","1.2.3","C:\\Games\\On\\file.ini","abc123#comment"}){var s=new MfgUnlockSettingState(d,raw,raw,raw,true,true);Check(MfgSettingsDisplay.Saved(s,l)==raw,"Opaque saved value localized");Check(MfgSettingsDisplay.Saved(s with{IsExplicitlySet=false},l)==l.Format("Not set · addon default {0}",raw),"Default payload changed");Check(MfgSettingsDisplay.Saved(s with{IsValid=false},l)==l.Format("Invalid value · {0}",raw),"Invalid payload changed");Check(MfgSettingsDisplay.Error("Unsupported MFG Unlock setting or value: Test="+raw+".",l)==l.Format("Unsupported MFG Unlock setting or value: {0}={1}.","Test",raw),"Error translated technical input");}}
    }
    private static void Constraints()
    {
        var e=Definitions().Single(x=>x.Definition.Key=="XeFGFlipQueueBufferPct").Definition.CatalogEntry!;var l=new UiLocalization();foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);var text=MfgSettingsDisplay.Constraints(e,l);Check(text.Contains("0 … 100")&&text.Contains("≥1.8.0"),"Range/version localized");if(culture!="en-US")Check(!text.Contains("Integer")&&!text.StartsWith("Type:"),"Untranslated type label");}
    }
    private static void XamlText()
    {
        var xml=XDocument.Load(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.xaml"));XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";var page=xml.Descendants().Single(n=>(string?)n.Attribute(x+"Name")=="MfgSettingsPage");
        var reason=page.Descendants().Single(n=>(string?)n.Attribute("Text")=="{Binding UnavailableMessage}");Check(reason.Parent!.Elements().Any(n=>n.Name.LocalName=="ComboBox")&&reason.Parent.Elements().Any(n=>n.Name.LocalName=="TextBox"),"Reason is not directly under the editor");
        var combo=page.Descendants().Single(n=>(string?)n.Attribute("ItemsSource")=="{Binding DisplayChoices}");Check(combo.Attributes().Any(a=>a.Name.LocalName=="LocalizedPresentation.PreserveOriginal"&&a.Value=="True"),"Global translation can alter exact choice tokens");
        foreach(var key in new[]{"Saved","New","Details","Restart","Reload","Discard","Save"})Check(page.DescendantsAndSelf().Attributes().Any(a=>a.Value.Contains("MfgStrings["+key+"]")),"Late text not explicitly bound: "+key);
        var code=System.IO.File.ReadAllText(Path.Combine(Root,"RenoDXMfgUnlocker/MainWindow.MfgText.cs"));Check(code.Contains("mfgTextKeys")&&code.Contains("l.Format(key,arguments)"),"Statuses lost original localization keys or opaque arguments");
        var strings=new MfgSettingsStrings(new UiLocalization());Check(strings["Saved"].Length>0,"Static text unavailable");
    }
    private static void RowState()
    {
        var d=Definitions().Single(x=>x.Definition.Key=="XeFGFlipQueueBufferPct").Definition;var type=typeof(MainWindow).GetNestedType("MfgUnlockSettingRow",BindingFlags.NonPublic)!;var row=Activator.CreateInstance(type,[d,"saved","50",true,(Action)(()=>{})])!;var proposed=type.GetProperty("ProposedValue")!;proposed.SetValue(row,"65");var choices=type.GetProperty("DisplayChoices")!.GetValue(row);type.GetMethod("SetAvailability")!.Invoke(row,[new MfgSettingAvailability(false,"Unavailable: enable {0} first.",Definitions().Single(x=>x.Definition.Key=="XeFGFlipQueue").Definition,"1")]);type.GetMethod("RefreshLanguage")!.Invoke(row,null);Check((string?)proposed.GetValue(row)=="65"&&ReferenceEquals(choices,type.GetProperty("DisplayChoices")!.GetValue(row)),"Disable/refresh recreated selection objects or discarded input");Check(!(bool)type.GetProperty("CanEdit")!.GetValue(row)!,"Field not disabled");
    }
    private static void Roundtrip()
    {
        using var f=new Fixture("; original\r\n[XeFG]\r\nXeFGFlipQueue=0 ; pacing\r\nFuture=On\r\n[Foreign]\r\nKeep=Disabled\r\n");var before=System.IO.File.ReadAllBytes(f.File);var snapshot=f.Load();var edits=new Dictionary<string,string>{{"XeFGFlipQueue","1"},{"XeFGFlipQueueBufferPct","60"},{"XeFGFlipQueueFirst","buffer"},{"XeFGNativeBuffers","12"}};
        var backups=new BackupRestoreService(Path.Combine(f.Base,"transactions"));var tx=backups.ExecuteAsync(f.Game,[f.Service.PrepareTransaction(snapshot,edits,()=>true)]).GetAwaiter().GetResult();var saved=f.Load();foreach(var edit in edits)Check(saved.Settings.Single(x=>x.Definition.Key==edit.Key).EffectiveValue==edit.Value,"Serialized edit differs");Check(System.IO.File.ReadAllText(f.File).Contains("Future=On\r\n")&&System.IO.File.ReadAllText(f.File).Contains("[Foreign]\r\nKeep=Disabled"),"Technical unknown content changed");backups.RestoreAsync(f.Game,tx.TransactionId).GetAwaiter().GetResult();Check(System.IO.File.ReadAllBytes(f.File).SequenceEqual(before),"Original bytes not restored");
        Reject(()=>backups.ExecuteAsync(f.Game,[f.Service.PrepareTransaction(snapshot,edits,()=>true),new("fixture.bin",[1],false)],i=>{if(i==1)throw new IOException("Injected failure");},null).GetAwaiter().GetResult());Check(System.IO.File.ReadAllBytes(f.File).SequenceEqual(before),"Partial error damaged original INI");
        Reject(()=>f.Service.CreateUpdatedBytes(f.Load(),new Dictionary<string,string>{{"XeFGFlipQueueBufferPct","60"}}));
    }
}

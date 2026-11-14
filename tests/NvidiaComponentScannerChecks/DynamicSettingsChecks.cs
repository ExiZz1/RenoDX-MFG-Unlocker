using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RenoDXMfgUnlocker;

internal static class DynamicSettingsChecks
{
    internal static IEnumerable<(string Name,Action Run)> All()
    {
        yield return ("dynamic settings builtin evidence catalog extends all existing definitions", BuiltIn);
        yield return ("dynamic settings typed numeric and enum edits preserve unknown keys and comments", TypedRoundtrip);
        yield return ("dynamic settings unknown keys are discovered read-only without inferred types", Unknown);
        yield return ("dynamic settings catalog signature domain tamper and untrusted keys fail closed", Signature);
        yield return ("dynamic settings catalog rejects code paths duplicate keys protected mappings and invalid defaults", InvalidMetadata);
        yield return ("dynamic settings signed revisions persist without downgrade or snapshot reuse", Revisions);
        yield return ("dynamic settings dependency checks repeat inside writer with no implicit resets", Dependencies);
        yield return ("dynamic settings type range and default changes do not rewrite stale configurations", SchemaChanges);
        yield return ("dynamic settings future versions inherit minimum and exclusion rules", Versions);
        yield return ("dynamic settings boolean integer float and enum values reject injection and locale guessing", Types);
        yield return ("dynamic settings malformed configurations and unsafe paths cannot produce writes", Corrupt);
        yield return ("dynamic settings numeric changes backup restore and injected failure preserve original bytes", Transaction);
        yield return ("dynamic settings shared UI binds data editors and six new language resources", UiContract);
        yield return ("dynamic settings catalog store rejects a reparse target without writing outside", Reparse);
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is InvalidDataException or InvalidOperationException or IOException or JsonException or CryptographicException or FormatException or DecoderFallbackException){return;}throw new Exception("Unsafe input accepted");}
    private sealed class Fixture:IDisposable
    {
        private string directory=Path.Combine(Path.GetTempPath(),"RenoDX-dynamic-settings",Guid.NewGuid().ToString("N"));
        internal string Root=>Path.Combine(directory,"game");
        internal string File=>Path.Combine(Root,"MFGAdaUnlock.ini");
        internal Fixture(){Directory.CreateDirectory(Root);}
        public void Dispose(){Directory.Delete(directory,true);}
    }
    private static readonly MfgCatalogEntry Integer=new("MFGAdaUnlock.ini","Synthetic","TestNumber","Integer","3","1.8.0",null,"Other","Synthetic fixture, not an upstream setting.","https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/3b8889f98cd0cf50e4f7d5daca730fc6084ff57d/tests/config_store_tests.cpp",true,Minimum:0,Maximum:10);
    private static (byte[] Json,byte[] Sig,IReadOnlyDictionary<string,string> Keys) Signed(long revision=2,params MfgCatalogEntry[] entries)
    {
        using var rsa=RSA.Create(2048);
        return SignedWith(rsa,revision,entries);
    }
    private static (byte[] Json,byte[] Sig,IReadOnlyDictionary<string,string> Keys) SignedWith(RSA rsa,long revision,params MfgCatalogEntry[] entries)
    {
        var json=JsonSerializer.SerializeToUtf8Bytes(new MfgCatalogDocument(1,MfgDynamicSettingsCatalog.Purpose,"isolated-test-only",revision,entries.Length==0?[Integer]:entries));
        byte[] data=[..MfgDynamicSettingsCatalog.SignatureDomain,..json];
        return(json,rsa.SignData(data,HashAlgorithmName.SHA256,RSASignaturePadding.Pss),new Dictionary<string,string>{{"isolated-test-only",Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())}});
    }
    private static MfgDynamicSettingsCatalog Catalog(params MfgCatalogEntry[] entries){var s=Signed(2,entries);return MfgDynamicSettingsCatalog.Verify(s.Json,s.Sig,s.Keys);}
    private static Dictionary<string,string> Change(string value)=>new(){{"TestNumber",value}};
    private static void BuiltIn()
    {
        foreach(var version in new[]{"1.8.0","1.8.1","1.8.2","1.9.0","2.0.0"})foreach(var xe in new[]{false,true}){
            using var f=new Fixture();var legacy=new MfgUnlockSettingsService(xe);var service=new MfgUnlockSettingsService(xe,MfgDynamicSettingsCatalog.BuiltIn);
            var defs=service.DefinitionsFor(version);Check(defs.Count==legacy.DefinitionsFor(version).Count+(xe?23:0),"Catalog coverage changed");
            foreach(var old in legacy.DefinitionsFor(version)){var now=defs.Single(d=>d.Key==old.Key);Check(now.Key==old.Key&&now.Section==old.Section&&now.DefaultValue==old.DefaultValue&&now.RequiresRestart==old.RequiresRestart&&now.Choices.SequenceEqual(old.Choices),"Builtin mapping changed");}
            var path=Path.Combine(f.Root,service.ConfigFileName);var snapshot=service.Load(path,f.Root,version);
            Check(snapshot.Settings.All(s=>s.IsValid),"Defaults invalid");
            if(xe)foreach(var d in defs.Where(d=>d.CatalogEntry is {} e&&e.Type!="ReadOnly")){
                // Supply declared prerequisite values in one shared operation; no automatic mutation.
                var edits=(d.CatalogEntry!.Requires??[]).ToDictionary(r=>r.Key,r=>r.Value);edits[d.Key]=d.DefaultValue;
                var bytes=service.CreateUpdatedBytes(snapshot,edits);Check(bytes.Length>0,"No rendered edit");
            }
        }
    }
    private static void TypedRoundtrip()
    {
        using var f=new Fixture();var text="; header\r\n[Synthetic]\r\nTestNumber = 3 ; keep inline\r\nUnknown = unchanged\r\n[Foreign]\r\nKeep=yes\r\n";
        // Existing unknown comments remain exact. Known numeric value comments follow the native integer convention.
        File.WriteAllText(f.File,text,new UTF8Encoding(true));var service=new MfgUnlockSettingsService(true,Catalog());var s=service.Load(f.File,f.Root,"1.8.2");
        Check(s.Settings.Single(x=>x.Definition.Key=="TestNumber").IsValid,"Inline comment rejected");
        var bytes=service.CreateUpdatedBytes(s,Change("7"));var output=new UTF8Encoding(true).GetString(bytes);
        Check(output.Contains("TestNumber = 7 ; keep inline")&&output.Contains("Unknown = unchanged\r\n[Foreign]\r\nKeep=yes\r\n"),"Formatting or unknown values changed");
        Check(File.ReadAllText(f.File)==text,"Preview wrote target");Check(bytes.Take(3).SequenceEqual(new byte[]{239,187,191}),"BOM lost");
    }
    private static void Unknown()
    {
        using var f=new Fixture();File.WriteAllText(f.File,"[Future]\nUndocumented=true\nUndocumented=1.25\n[XeFG.Learned]\nExecutable=c:/unknown/game.exe\n");var svc=new MfgUnlockSettingsService(true,MfgDynamicSettingsCatalog.BuiltIn);var s=svc.Load(f.File,f.Root,"1.8.2");
        Check(s.UnknownSettings.Count==3&&!s.Settings.Any(x=>x.Definition.Key=="Undocumented"),"Unknown key inferred");Reject(()=>svc.CreateUpdatedBytes(s,new Dictionary<string,string>{{"Undocumented","0"}}));
        Check(svc.CreateUpdatedBytes(s,new Dictionary<string,string>{{"UIStabilization","1"}}).Length>0,"Unknown data blocked unrelated safe edit");
    }
    private static void Signature()
    {
        var s=Signed();Check(MfgDynamicSettingsCatalog.Verify(s.Json,s.Sig,s.Keys).Revision==2,"Signature rejected");Reject(()=>MfgDynamicSettingsCatalog.Verify(s.Json,s.Sig));
        var changed=(byte[])s.Json.Clone();changed[^2]^=1;Reject(()=>MfgDynamicSettingsCatalog.Verify(changed,s.Sig,s.Keys));var sig=(byte[])s.Sig.Clone();sig[0]^=1;Reject(()=>MfgDynamicSettingsCatalog.Verify(s.Json,sig,s.Keys));
        using var rsa=RSA.Create(2048);var keys=new Dictionary<string,string>{{"isolated-test-only",Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())}};
        Reject(()=>MfgDynamicSettingsCatalog.Verify(s.Json,rsa.SignData(s.Json,HashAlgorithmName.SHA256,RSASignaturePadding.Pss),keys));
        Check(SelfUpdateTrust.Keys.Count==1&&SelfUpdateTrust.Keys.ContainsKey("owner-release-2026"),"Production trust changed");
    }
    private static void InvalidMetadata()
    {
        foreach(var entry in new[]{Integer with{File="../other.ini"},Integer with{Section="../../x"},Integer with{Type="Execute"},Integer with{Default="99"},Integer with{Minimum=11},Integer with{Key="Selected"},Integer with{Evidence="https://evil.invalid/setting"},Integer with{MinimumAddon="unknown"},Integer with{MaximumAddonExclusive="1.7.0"},Integer with{Requires=[new("Missing","1")]},Integer with{Type="Enum",Values=["same","SAME"]}})Reject(()=>Catalog(entry));
        Reject(()=>Catalog(Integer,Integer));Reject(()=>Catalog(Integer,Integer with{Section="Another"}));
        Reject(()=>Catalog(Integer with{Key="A",Requires=[new("B","3")]},Integer with{Key="B",Requires=[new("A","3")]}));
        var doc=Encoding.UTF8.GetBytes("{\"Schema\":1,\"Schema\":2}");Reject(()=>MfgDynamicSettingsCatalog.Verify(doc,new byte[256],new Dictionary<string,string>()));
    }
    private static void Revisions()
    {
        using var f=new Fixture();using var rsa=RSA.Create(2048);var s=SignedWith(rsa,2);var store=new MfgSettingsCatalogStore(Path.Combine(f.Root,"catalogs"),s.Keys);Check(store.Load().Revision==1,"Builtin missing");store.Import(s.Json,s.Sig);Check(store.Load().Identity==Convert.ToHexString(SHA256.HashData(s.Json)),"Persisted catalog not verified");Reject(()=>store.Import(s.Json,s.Sig));
        var next=SignedWith(rsa,3,Integer with{Default="4",Maximum=5});store.Import(next.Json,next.Sig);var loaded=new MfgUnlockSettingsService(true,store.Load()).Load(f.File,f.Root,"1.8.2");Check(loaded.Settings.Single(x=>x.Definition.Key=="TestNumber").EffectiveValue=="4","New signed revision did not supply updated definitions without a new EXE");Reject(()=>store.Import(s.Json,s.Sig));
        var svc=new MfgUnlockSettingsService(true,Catalog());var snapshot=svc.Load(f.File,f.Root,"1.8.2");svc.UseCatalog(MfgDynamicSettingsCatalog.BuiltIn);Reject(()=>svc.CreateUpdatedBytes(snapshot,Change("4")));
        var signedFile=Path.Combine(f.Root,"catalogs",Convert.ToHexString(SHA256.HashData(s.Json)),"catalog.json");File.AppendAllText(signedFile," ");Reject(()=>store.Load());
    }
    private static void Dependencies()
    {
        using var f=new Fixture();var entry=Integer with{Requires=[new("XeFGFlipQueue","1")]};var svc=new MfgUnlockSettingsService(true,Catalog(entry));var s=svc.Load(f.File,f.Root,"1.8.2");Reject(()=>svc.CreateUpdatedBytes(s,Change("4")));
        Check(!svc.CanEdit(s.Settings.Single(x=>x.Definition.Key=="TestNumber").Definition,s.Settings),"Dependency UI not gated");
        var edits=Change("4");edits["XeFGFlipQueue"]="1";var output=Encoding.UTF8.GetString(svc.CreateUpdatedBytes(s,edits));Check(output.Contains("TestNumber=4")&&output.Contains("XeFGFlipQueue=1")&&!output.Contains("Vulkan="),"Unrelated settings enabled");
    }
    private static void SchemaChanges()
    {
        using var f=new Fixture();File.WriteAllText(f.File,"[Synthetic]\nTestNumber=8\n");var svc=new MfgUnlockSettingsService(true,Catalog());var s=svc.Load(f.File,f.Root,"1.8.2");svc.UseCatalog(Catalog(Integer with{Type="Enum",Values=["3","4"],Default="4"}));Reject(()=>svc.CreateUpdatedBytes(s,Change("4")));var changed=svc.Load(f.File,f.Root,"1.8.2");Check(!changed.Settings.Single(x=>x.Definition.Key=="TestNumber").IsValid,"Invalid old value replaced with default");Reject(()=>svc.CreateUpdatedBytes(changed,new Dictionary<string,string>{{"UIStabilization","1"}}));
        Check(File.ReadAllText(f.File).Contains("TestNumber=8"),"Schema update wrote game configuration");
        File.Delete(f.File);var defaults=svc.Load(f.File,f.Root,"1.8.2");Check(defaults.Settings.Single(x=>x.Definition.Key=="TestNumber") is {EffectiveValue:"4",IsExplicitlySet:false},"Revised default misreported as saved");
    }
    private static void Versions()
    {
        var c=Catalog(Integer with{Exclusions=[new("2.0.0","2.1.0")]});
        foreach(var v in new[]{"1.8.0","1.8.1","1.8.2","1.9.0","2.1.0","5.0.0"})Check(c.Definitions("MFGAdaUnlock.ini",v).Count==1,"Minimum inheritance failed");
        foreach(var v in new[]{"1.7.9","2.0.0","bad","v1.8.2-preview","1.8"})Check(c.Definitions("MFGAdaUnlock.ini",v).Count==0,"Unknown/excluded schema accepted");
    }
    private static void Types()
    {
        var definitions=new[]{Integer,Integer with{Key="Flag",Type="Boolean",Default="0"},Integer with{Key="Ratio",Type="Float",Default="0.5",Minimum=0,Maximum=1},Integer with{Key="Choice",Type="Enum",Default="auto",Values=["auto","off","on"]},Integer with{Key="Observed",Type="ReadOnly",Default="vendor"}};
        using var f=new Fixture();var svc=new MfgUnlockSettingsService(true,Catalog(definitions));var snap=svc.Load(f.File,f.Root,"1.8.2");
        foreach(var culture in UiLocalization.Cultures){var old=System.Globalization.CultureInfo.CurrentCulture;try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(culture);Check(svc.CreateUpdatedBytes(snap,new Dictionary<string,string>{{"Ratio","0.75"},{"Choice","on"},{"Flag","1"}}).Length>0,"Culture dependent parsing");}finally{System.Globalization.CultureInfo.CurrentCulture=old;}}
        foreach(var bad in new[]{"-1","11","1.5","1,5","NaN","1\nOther=1","1;other","99999999999999999999999999999999"})Reject(()=>svc.CreateUpdatedBytes(snap,Change(bad)));
        Reject(()=>svc.CreateUpdatedBytes(snap,new Dictionary<string,string>{{"Ratio","0,5"}}));Reject(()=>svc.CreateUpdatedBytes(snap,new Dictionary<string,string>{{"Observed","different"}}));
    }
    private static void Corrupt()
    {
        using var f=new Fixture();var svc=new MfgUnlockSettingsService(true,Catalog());
        foreach(var text in new[]{"[Synthetic]\nTestNumber=3\nTestNumber=4\n","[Synthetic]\nTestNumber=3\n[Synthetic]\nTestNumber=4\n","[ Synthetic ]\nTestNumber=3\n"}){File.WriteAllText(f.File,text);Reject(()=>svc.Load(f.File,f.Root,"1.8.2"));}
        File.WriteAllBytes(f.File,[0xff,0xfe,1]);Reject(()=>svc.Load(f.File,f.Root,"1.8.2"));Reject(()=>svc.Load(Path.Combine(f.Root,"..","MFGAdaUnlock.ini"),f.Root,"1.8.2"));
        File.Delete(f.File);var s=svc.Load(f.File,f.Root,"1.8.2");Reject(()=>svc.PrepareTransaction(s,Change("4"),()=>false));File.WriteAllText(f.File,";new");Reject(()=>svc.CreateUpdatedBytes(s,Change("4")));
    }
    private static void Transaction()
    {
        using var f=new Fixture();File.WriteAllText(f.File,";original\r\n[Synthetic]\r\nTestNumber=3\r\n",new UTF8Encoding(true));var before=File.ReadAllBytes(f.File);var svc=new MfgUnlockSettingsService(true,Catalog());var s=svc.Load(f.File,f.Root,"1.8.2");
        var backups=new BackupRestoreService(Path.Combine(Path.GetDirectoryName(f.Root)!,"transactions"));var tx=backups.ExecuteAsync(f.Root,[svc.PrepareTransaction(s,Change("6"),()=>true)]).GetAwaiter().GetResult();Check(svc.Load(f.File,f.Root,"1.8.2").Settings.Single(x=>x.Definition.Key=="TestNumber").EffectiveValue=="6","Write verification failed");backups.RestoreAsync(f.Root,tx.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllBytes(f.File).SequenceEqual(before),"Restore changed originals");
        Reject(()=>backups.ExecuteAsync(f.Root,[svc.PrepareTransaction(s,Change("5"),()=>true),new("synthetic.txt",[1,2,3],false)],i=>{if(i==1)throw new IOException("Injected write failure");},null).GetAwaiter().GetResult());
        Check(File.ReadAllBytes(f.File).SequenceEqual(before)&&!File.Exists(Path.Combine(f.Root,"synthetic.txt")),"Partial failure did not roll back dynamic settings");
        var stale=svc.Load(f.File,f.Root,"1.8.2");File.AppendAllText(f.File,";external");Reject(()=>svc.PrepareTransaction(stale,Change("7"),()=>true));Check(File.ReadAllText(f.File).EndsWith(";external"),"External edit overwritten");
    }
    private static void UiContract()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));var xaml=File.ReadAllText(Path.Combine(root,"RenoDXMfgUnlocker/MainWindow.xaml"));Check(xaml.Contains("Binding NumericEditor")&&xaml.Contains("Binding ProposedValue")&&xaml.Contains("ImportSettingsCatalog_Click")&&xaml.Contains("UnknownSettingsText"),"Missing UI wiring");
        var code=File.ReadAllText(Path.Combine(root,"RenoDXMfgUnlocker/MainWindow.xaml.cs"));Check(code.Contains("MfgSettingsDiscardButton.IsEnabled=safeContext")&&code.Contains("r.InputValid"),"Invalid input must block Save while preserving Discard");
        var language=new UiLocalization();Check(language.ValidateCatalogs().Count==0,"Localization integrity failure");foreach(var c in UiLocalization.Cultures)foreach(var key in new[]{"Settings catalog","Import signed catalog","Unknown settings — read-only","Catalog-defined setting. See Details for the upstream description, constraints and evidence."})Check(language.GetCatalog(c).ContainsKey(key),"Missing catalog localization: "+c+" / "+key);
    }
    private static void Reparse()
    {
        using var f=new Fixture();var outside=Path.Combine(Path.GetDirectoryName(f.Root)!,"outside");Directory.CreateDirectory(outside);var link=Path.Combine(f.Root,"catalog-link");
        try{Directory.CreateSymbolicLink(link,outside);}catch(Exception ex)when(ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException){throw new SkipCheckException("OS did not permit an isolated symbolic link; catalog reparse test was not executed: "+ex.Message);}
        try{var store=new MfgSettingsCatalogStore(link);Reject(()=>store.Load());Check(!Directory.EnumerateFileSystemEntries(outside).Any(),"External path was changed");}finally{Directory.Delete(link);}
    }
}

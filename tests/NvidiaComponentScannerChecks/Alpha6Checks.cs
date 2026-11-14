using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using RenoDXMfgUnlocker;

internal static class Alpha6Checks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return("alpha6 genuine official SDK inventory and NVIDIA signatures",Inventory);
        yield return("alpha6 SDK inventory excludes development samples tools and fictional files",Production);
        yield return("alpha6 genuine signed development DLL cannot substitute a production archive member",Development);
        yield return("alpha6 staging rejects traversal duplicate file-directory collision and corrupt ZIP",UnsafeArchives);
        yield return("alpha6 manual mode adds individually selected DLLs without addon or existing integration",Add);
        yield return("alpha6 genuine provider replacement mixed plan and exact Restore",Mixed);
        yield return("alpha6 identical files are No Change and create no transaction",NoOp);
        yield return("alpha6 modified added DLL blocks Restore without deleting unmanaged files",ChangedRestore);
        yield return("alpha6 unknown and renamed third party targets cannot be overwritten",UnknownTarget);
        yield return("alpha6 missing and modified source fail closed",SourceChanges);
        yield return("alpha6 archive size hash digest and provenance are mandatory",Archive);
        yield return("alpha6 native architecture and file type are mandatory",Architecture);
        yield return("alpha6 unknown roles stay blocked and official optional modules remain manually selectable",Unavailable);
        yield return("alpha6 missing dependency warnings do not impersonate compatibility approval",Dependencies);
        yield return("alpha6 manual destination remains inside current EXE root and outside backups copies helpers",Targets);
        yield return("alpha6 actual junction target is rejected",Junction);
        yield return("alpha6 changed EXE and appeared target reject stale Apply",Stale);
        yield return("alpha6 game process guards are checked again during partial Apply with rollback",PartialFailure);
        yield return("alpha6 explicit uncertainty acknowledgement cannot be omitted",Acknowledgement);
        yield return("alpha6 cancellation before writes preserves all destination bytes",Cancellation);
        yield return("alpha6 game state and component selections stay independent",MultiGame);
        yield return("alpha6 root exclusion applies to another process and Restore",Concurrency);
        yield return("alpha6 verified manual switching and checkbox state unshown WPF",UiModes);
        yield return("alpha6 navigation gear order and retained ReShade R18 R19 R20",UiNavigation);
        yield return("alpha6 six-language new resources and technical versions",Localization);
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or BadImageFormatException or UnauthorizedAccessException or OperationCanceledException){return;}throw new Exception("Expected refusal");}
    private static string Temp(){var path=Path.Combine(Path.GetTempPath(),"RenoDX-alpha6",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
    private sealed record Source(string Downloads,string Staging,NvidiaManualPackage Package);
    private static readonly Lazy<Source> Official=new(()=>{
        var archive=Environment.GetEnvironmentVariable("RENODX_TEST_STREAMLINE_ARCHIVE");var metadata=Environment.GetEnvironmentVariable("RENODX_TEST_STREAMLINE_METADATA");
        if(archive is null||metadata is null||!File.Exists(archive)||!File.Exists(metadata))throw new SkipCheckException("Existing genuine official SDK ZIP and independently queried API metadata are required; no fabricated source acceptance.");
        using var json=JsonDocument.Parse(File.ReadAllText(metadata));var m=json.RootElement;
        var asset=new StreamlineSdkAsset(m.GetProperty("name").GetString()!,m.GetProperty("download").GetString()!,m.GetProperty("size").GetInt64(),m.GetProperty("digest").GetString()![7..],m.GetProperty("tag").GetString()!,"Official SDK fixture",null,m.GetProperty("release").GetString()!);
        var downloads=Path.GetDirectoryName(archive)!;var staging=Temp();var manager=new NvidiaSdkManualManager(downloads,staging,new BackupRestoreService(Temp()),_=>false);
        var package=manager.PrepareAsync(asset,archive).GetAwaiter().GetResult();Console.WriteLine("EVIDENCE genuine SDK "+asset.ReleaseTag+" size "+asset.Size+" SHA256 "+asset.Sha256+"; "+package.Components.Count+" production DLLs / "+package.Notices+" notices; no installer or DLL executed.");
        return new(downloads,staging,package);
    });
    private sealed record Fixture(string Root,string Exe,BackupRestoreService Backup,NvidiaSdkManualManager Manager)
    {
        public NvidiaManualPlan Preview(params string[] files)=>Manager.PreviewAsync(Official.Value.Package,Root,Exe,Root,files.ToHashSet(StringComparer.OrdinalIgnoreCase),true).GetAwaiter().GetResult();
        public StreamlineComponentInstallResult Apply(NvidiaManualPlan plan)=>Manager.ApplyAsync(Official.Value.Package,plan).GetAwaiter().GetResult();
    }
    private static Fixture Create(Func<string,bool>? process=null)
    {
        var root=Temp();var exe=Path.Combine(root,"Own-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Environment.ProcessPath!,exe);
        var backup=new BackupRestoreService(Temp());var s=Official.Value;return new(root,exe,backup,new(s.Downloads,s.Staging,backup,process??(_=>false)));
    }
    private static NvidiaSdkComponent Component(string name)=>Official.Value.Package.Components.Single(c=>c.FileName.Equals(name,StringComparison.OrdinalIgnoreCase));
    private static void Inventory()
    {
        var p=Official.Value.Package;Check(p.Asset.ReleaseTag=="v2.14.16"&&p.Components.Count==16,"Unexpected independently reviewed fixture");
        Check(p.Components.All(c=>c.Signature=="NVIDIA signature verified"&&c.Architecture=="x64"),"Real SDK signatures/architecture not verified");
        Check(Component("sl.common.dll").Version=="2.14.16.0"&&Component("nvngx_dlssg.dll").Version=="310.9.16.0","SDK version confused with DLL resource version");
        Check(Component("NvLowLatencyVk.dll").Version=="Unknown"&&Component("NvLowLatencyVk.dll").Eligible,"Metadata-free signed support library invented a version");
    }
    private static void Production()
    {
        var p=Official.Value.Package;Check(p.Components.All(c=>Path.GetRelativePath(p.Directory,c.SourcePath).Replace('\\','/').Equals("bin/x64/"+c.FileName,StringComparison.Ordinal)),"Development/sample file inventoried");
        Check(p.Components.Any(c=>c.FileName=="sl.deepdvc.dll")&&p.Components.Any(c=>c.FileName=="sl.directsr.dll")&&p.Components.Any(c=>c.FileName=="sl.nvperf.dll"),"Expanded actual inventory lost advanced modules");Check(p.Notices==15&&File.Exists(Path.Combine(p.Directory,"external","json","LICENSE.MIT"))&&File.Exists(Path.Combine(p.Directory,"NVIDIA Nsight Perf SDK License (28Sept2022).pdf")),"SDK notices omitted");
        Check(p.Components.Count(c=>c.Common)<p.Components.Count,"Common/All are not different views");
    }
    private static void Development()
    {
        var source=Official.Value;var clone=Clone("sl.common.dll");var target=clone.Components[0].SourcePath;using var zip=System.IO.Compression.ZipFile.OpenRead(source.Package.ArchivePath);var entry=zip.GetEntry("bin/x64/development/sl.common.dll")??throw new Exception("Reviewed development fixture missing");using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);
        var component=NvidiaSdkManualManager.Inspect(target,true);Check(component.Sha256!=Component("sl.common.dll").Sha256,"Development fixture is not distinct from production");
        // The real dev DLL can still say PRODUCTION in vendor metadata. Only archive-member identity authorizes it.
        clone=clone with {Components=new[]{component}};var f=Create();Reject(()=>f.Manager.PreviewAsync(clone,f.Root,f.Exe,f.Root,new HashSet<string>{"sl.common.dll"},true).GetAwaiter().GetResult());Check(Directory.GetFiles(f.Root).Length==1,"Development member entered the game");
    }
    private static void UnsafeArchives()
    {
        // Own hostile ZIP data and injected metadata exercise parsing only, not official provenance acceptance.
        foreach(var names in new[]{new[]{"../escape.dll"},new[]{"bin/x64/sl.common.dll","BIN/X64/SL.COMMON.dll"},new[]{"bin/x64/sl.common.dll","bin/x64/sl.common.dll/"},Array.Empty<string>()}){
            var downloads=Temp();var path=Path.Combine(downloads,"streamline-sdk-v9.9.zip");if(names.Length==0)File.WriteAllText(path,"own corrupt ZIP fixture");else using(var zip=System.IO.Compression.ZipFile.Open(path,System.IO.Compression.ZipArchiveMode.Create))foreach(var n in names){using var output=zip.CreateEntry(n).Open();output.Write([1,2,3]);}
            var asset=new StreamlineSdkAsset("streamline-sdk-v9.9.zip","https://github.com/NVIDIA-RTX/Streamline/releases/download/v9.9/streamline-sdk-v9.9.zip",new FileInfo(path).Length,Hash(path),"v9.9","Own malicious input; not an official fixture",null,"");var staging=Temp();var manager=new NvidiaSdkManualManager(downloads,staging,new(Temp()),_=>false);
            Reject(()=>manager.PrepareAsync(asset,path).GetAwaiter().GetResult());Check(!Directory.EnumerateFileSystemEntries(staging).Any(),"Unsafe archive left staged files");
        }
    }
    private static void Add()
    {
        var f=Create();var selected=new[]{"sl.interposer.dll","sl.common.dll","sl.dlss.dll","sl.nvperf.dll"};Check(Directory.GetFiles(f.Root).Length==1,"Fixture has addon/runtime before manual install");
        var plan=f.Preview(selected);Check(plan.CanApply&&plan.Entries.Count==selected.Length&&plan.Entries.All(r=>r.Action=="New file"),"Missing integration or addon incorrectly required");
        var tx=f.Apply(plan);Check(tx.InstalledFiles.Count==selected.Length&&selected.All(n=>Hash(Path.Combine(f.Root,n))==Component(n).Sha256),"Individual Add scope/hash wrong");
        Check(!File.Exists(Path.Combine(f.Root,"nvngx_dlssg.dll")),"Unselected provider auto-installed");f.Backup.RestoreAsync(f.Root,tx.Transaction.TransactionId).GetAwaiter().GetResult();Check(Directory.GetFiles(f.Root).SequenceEqual(new[]{f.Exe}),"Restore failed to remove owned Add");
    }
    private static string Legacy(){var p=Environment.GetEnvironmentVariable("RENODX_TEST_SIGNED_PROVIDER_LEGACY");if(p is null||!File.Exists(p))throw new SkipCheckException("Genuine signed legacy provider fixture required for Replace");return p;}
    private static void Mixed()
    {
        var f=Create();var old=Path.Combine(f.Root,"nvngx_dlssg.dll");File.Copy(Legacy(),old);var original=Hash(old);File.Copy(Component("sl.common.dll").SourcePath,Path.Combine(f.Root,"sl.common.dll"));
        var plan=f.Preview("sl.common.dll","sl.interposer.dll","nvngx_dlssg.dll");Check(plan.Entries.Select(r=>r.Action).ToHashSet().SetEquals(new[]{"New file","No change","Replace existing"}),"Mixed plan actions wrong");
        var tx=f.Apply(plan);Check(tx.InstalledFiles.Count==2,"No Change entered transaction");f.Backup.RestoreAsync(f.Root,tx.Transaction.TransactionId).GetAwaiter().GetResult();Check(Hash(old)==original&&!File.Exists(Path.Combine(f.Root,"sl.interposer.dll"))&&File.Exists(Path.Combine(f.Root,"sl.common.dll")),"Mixed Restore destroyed initial state");
    }
    private static void NoOp()
    {
        var f=Create();var path=Path.Combine(f.Root,"sl.common.dll");File.Copy(Component("sl.common.dll").SourcePath,path);var stamp=File.GetLastWriteTimeUtc(path);var plan=f.Preview("sl.common.dll");Check(!plan.CanApply&&plan.Entries.Single().Action=="No change","No-op still writable");Reject(()=>f.Apply(plan));Check(File.GetLastWriteTimeUtc(path)==stamp,"No-op rewrote bytes");
        Check(Directory.GetFileSystemEntries(f.Root).Length==2,"No-op changed directory");
    }
    private static void ChangedRestore()
    {
        var f=Create();var tx=f.Apply(f.Preview("sl.common.dll"));var path=Path.Combine(f.Root,"sl.common.dll");File.AppendAllText(path,"own external change");File.WriteAllText(Path.Combine(f.Root,"user.txt"),"keep");var hash=Hash(path);
        Reject(()=>f.Backup.RestoreAsync(f.Root,tx.Transaction.TransactionId).GetAwaiter().GetResult());Check(Hash(path)==hash&&File.ReadAllText(Path.Combine(f.Root,"user.txt"))=="keep","Restore destroyed unmanaged/outside changes");
    }
    private static void UnknownTarget()
    {
        var f=Create();var path=Path.Combine(f.Root,"sl.interposer.dll");File.Copy(f.Exe,path);var hash=Hash(path);var plan=f.Preview("sl.interposer.dll");Check(!plan.CanApply&&plan.Entries.Single().Action=="Blocked","Third-party proxy/name collision allowed");Reject(()=>f.Apply(plan));Check(Hash(path)==hash,"Blocked proxy overwritten");
        File.Delete(path);File.Copy(Legacy(),path);Check(!f.Preview("sl.interposer.dll").CanApply,"Different signed NVIDIA component accepted by filename alone");
    }
    private static NvidiaManualPackage Clone(string name)
    {
        var s=Official.Value;var dir=Path.Combine(s.Staging,Guid.NewGuid().ToString("N"));var runtime=Path.Combine(dir,"bin","x64");Directory.CreateDirectory(runtime);var c=Component(name);var path=Path.Combine(runtime,c.FileName);File.Copy(c.SourcePath,path);return s.Package with {Directory=dir,Components=new[]{c with {SourcePath=path}}};
    }
    private static void SourceChanges()
    {
        var f=Create();var package=Clone("sl.common.dll");File.Delete(package.Components[0].SourcePath);Reject(()=>f.Manager.PreviewAsync(package,f.Root,f.Exe,f.Root,new HashSet<string>{"sl.common.dll"},true).GetAwaiter().GetResult());
        package=Clone("sl.common.dll");File.AppendAllText(package.Components[0].SourcePath,"corrupt");Reject(()=>f.Manager.PreviewAsync(package,f.Root,f.Exe,f.Root,new HashSet<string>{"sl.common.dll"},true).GetAwaiter().GetResult());Check(Directory.GetFiles(f.Root).Length==1,"Invalid source wrote target");
    }
    private static void Archive()
    {
        var f=Create();var p=Official.Value.Package;
        foreach(var a in new[]{p.Asset with {Sha256=null},p.Asset with {Sha256=new string('a',64)},p.Asset with {Size=p.Asset.Size+1},p.Asset with {DownloadUrl="https://unverified.invalid/sdk.zip"}})Reject(()=>f.Manager.PreviewAsync(p with {Asset=a},f.Root,f.Exe,f.Root,new HashSet<string>{"sl.common.dll"},true).GetAwaiter().GetResult());
    }
    private static void Architecture()
    {
        var f=Create();File.WriteAllText(f.Exe,"Own invalid EXE, never executed");Reject(()=>f.Preview("sl.common.dll"));
        var own=Path.Combine(Temp(),"sl.common.dll");File.Copy(Environment.ProcessPath!,own);Check(!NvidiaSdkManualManager.Inspect(own,true).Eligible,"Native executable accepted as DLL");
        var bytes=File.ReadAllBytes(Component("sl.common.dll").SourcePath);var pe=BitConverter.ToInt32(bytes,0x3c);bytes[pe+4]=0x4c;bytes[pe+5]=0x01;File.WriteAllBytes(own,bytes);Check(!NvidiaSdkManualManager.Inspect(own,true).Eligible,"x86 source accepted");
    }
    private static void Unavailable(){var f=Create();var p=f.Preview("sl.nvperf.dll");Check(Component("sl.nvperf.dll").Eligible&&p.CanApply&&p.Warnings.Any(w=>w.StartsWith("Profiling plugin only")),"Documented optional production module arbitrarily blocked or tool warning missing");var unknown=Path.Combine(Temp(),"sl.unreviewed.dll");File.Copy(Component("sl.common.dll").SourcePath,unknown);Check(!NvidiaSdkManualManager.Inspect(unknown,true).Eligible,"Unknown component identity permitted");Check(f.Preview("sl.directsr.dll","sl.deepdvc.dll","NvLowLatencyVk.dll").CanApply,"Supported manual file operations still require MFG integration");}
    private static void Dependencies(){var f=Create();var p=f.Preview("sl.dlss_g.dll");Check(p.CanApply&&p.Warnings.Any(w=>w.Contains("Incomplete dependency selection"))&&p.Warnings.Any(w=>w.Contains("does not add game integration")),"Incomplete selection is silently certified or MFG-gated");Check(p.Entries.Count==1,"Dependency silently auto-selected");}
    private static void Targets()
    {
        var f=Create();var p=Official.Value.Package;var other=Temp();Reject(()=>f.Manager.PreviewAsync(p,f.Root,f.Exe,other,new HashSet<string>{"sl.common.dll"},true).GetAwaiter().GetResult());
        foreach(var label in new[]{"MFG_Backup_own","copy","helper"}){var root=Path.Combine(Temp(),label);Directory.CreateDirectory(root);var exe=Path.Combine(root,"Own.exe");File.Copy(Environment.ProcessPath!,exe);Reject(()=>f.Manager.PreviewAsync(p,root,exe,root,new HashSet<string>{"sl.common.dll"},true).GetAwaiter().GetResult());}
        var helper=Path.Combine(f.Root,"OwnSetup.exe");File.Copy(f.Exe,helper);Reject(()=>f.Manager.ValidateDestination(f.Root,helper,f.Root));
        // Pure path decisions: no vendor/system folders are read, created or modified by these assertions.
        Check(NvidiaSdkManualManager.IsProtectedPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","own-probe"))&&NvidiaSdkManualManager.IsProtectedPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"NVIDIA","NGX","own-probe")),"Driver/application directories not protected");
        Check(!NvidiaSdkManualManager.IsProtectedPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam","steamapps","common","Own Game")),"Normal game library denied by protected-path rule");
        Reject(()=>f.Manager.ValidateDestination(Path.GetPathRoot(f.Root)!,f.Exe,f.Root));
    }
    private static void Junction()
    {
        var f=Create();var link=Path.Combine(Temp(),"own-link");using var process=Process.Start(new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/c","mklink","/J",link,f.Root}})!;if(!process.WaitForExit(10000)){process.Kill();throw new Exception("Own junction creation timed out");}if(process.ExitCode!=0)throw new SkipCheckException("Actual junction creation unavailable; not passed");
        Reject(()=>f.Manager.ValidateDestination(link,Path.Combine(link,Path.GetFileName(f.Exe)),link));
    }
    private static void Stale()
    {
        var f=Create();var p=f.Preview("sl.common.dll");File.WriteAllText(Path.Combine(f.Root,"sl.common.dll"),"appeared");Reject(()=>f.Apply(p));Check(File.ReadAllText(Path.Combine(f.Root,"sl.common.dll"))=="appeared","Appeared target overwritten");
        f=Create();p=f.Preview("sl.common.dll");File.AppendAllText(f.Exe,"changed");Reject(()=>f.Apply(p));Check(!File.Exists(Path.Combine(f.Root,"sl.common.dll")),"Stale EXE accepted");
    }
    private static void PartialFailure()
    {
        var calls=0;var f=Create(_=>Interlocked.Increment(ref calls)>=4);var p=f.Preview("sl.common.dll","sl.interposer.dll");
        try{f.Apply(p);throw new Exception("Injected process-start failure not observed");}catch(BackupTransactionException e){Check(e.Message.Contains("rolled back"),"Partial Apply not rolled back");}
        Check(Directory.GetFiles(f.Root).SequenceEqual(new[]{f.Exe}),"Partial Apply retained managed Add");
        f=Create(_=>true);Reject(()=>f.Preview("sl.common.dll"));
    }
    private static void Acknowledgement(){var f=Create();var p=f.Manager.PreviewAsync(Official.Value.Package,f.Root,f.Exe,f.Root,new HashSet<string>{"sl.common.dll"},false).GetAwaiter().GetResult();Check(!p.CanApply,"Unacknowledged Preview enabled Apply");Reject(()=>f.Apply(p));Check(NvidiaSdkManualManager.Acknowledge(p,true).CanApply,"Acknowledgement cannot enable valid plan");}
    private static void Cancellation(){var f=Create();var p=f.Preview("sl.common.dll");using var stop=new CancellationTokenSource();stop.Cancel();Reject(()=>f.Manager.ApplyAsync(Official.Value.Package,p,token:stop.Token).GetAwaiter().GetResult());Check(Directory.GetFiles(f.Root).SequenceEqual(new[]{f.Exe}),"Cancelled preflight wrote target");}
    private static void MultiGame(){var a=Create();var b=Create();var p=a.Preview("sl.common.dll");var q=b.Preview("sl.interposer.dll");Check(p.ContextFingerprint!=q.ContextFingerprint&&p.Selection.Single()=="sl.common.dll"&&q.Selection.Single()=="sl.interposer.dll","Game/selection contexts mixed");var forged=p with {Root=b.Root,Executable=b.Exe,Destination=b.Root};Reject(()=>a.Manager.ApplyAsync(Official.Value.Package,forged).GetAwaiter().GetResult());Check(Directory.GetFiles(b.Root).Length==1,"Another game written through old plan");}
    private static void Concurrency()
    {
        var f=Create();using var operation=RootOperation.Enter(f.Root);var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--root-lock-check");start.ArgumentList.Add(f.Root);
        using var child=Process.Start(start)!;if(!child.WaitForExit(10000)){child.Kill();throw new Exception("Own lock probe timed out");}Check(child.ExitCode==23,"Another process acquired an active root");Reject(()=>f.Backup.RestoreAsync(f.Root,Guid.NewGuid().ToString("N")).GetAwaiter().GetResult());
    }
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static object? Get(MainWindow m,string n)=>typeof(MainWindow).GetField(n,Flags)!.GetValue(m);
    private static void Set(MainWindow m,string n,object? v)=>typeof(MainWindow).GetField(n,Flags)!.SetValue(m,v);
    private static void Call(MainWindow m,string n,params object?[] args)=>typeof(MainWindow).GetMethod(n,Flags)!.Invoke(m,args);
    private static void UiModes()=>WpfCheckHost.Run(()=>{
        LocalizedPresentation.Language.SetCulture("en-US",false);var main=new MainWindow();try{
            var f=Create();Set(main,"selectedExe",f.Exe);Set(main,"selectedGameRoot",f.Root);Call(main,"SetupWizard_Click",main,new RoutedEventArgs());var verified=(SetupWizardView)((ContentControl)main.FindName("DllInstallationPage")).Content;
            Call(main,"OpenManualNvidia");var manual=(NvidiaManualDllView)((ContentControl)main.FindName("DllInstallationPage")).Content;Check(!ReferenceEquals(verified,manual)&&manual.Consent.IsChecked!=true,"Manual and verified state not separate");
            Set(main,"manualDllPackage",Official.Value.Package);Set(main,"manualDllTarget",f.Root);Call(main,"RenderManualNvidia");Check(manual.FileRows.Children.Count==Official.Value.Package.Components.Count(c=>c.Common),"Common filter wrong");
            manual.AllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(manual.FileRows.Children.Count==16,"All SDK filter omits actual entries");
            var selection=(HashSet<string>)Get(main,"manualDllSelection")!;Check(selection.Count==0,"Missing DLL auto-selected");selection.Add("sl.common.dll");
            Set(main,"manualDllPlan",f.Preview("sl.common.dll"));manual.Consent.IsChecked=true;Call(main,"UpdateManualConsent");Check(manual.ApplyButton.IsEnabled,"Valid acknowledged manual plan not enabled");
            var returnButton=(Button)((WrapPanel)manual.ModePanel.Children[0]).Children[0];returnButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(ReferenceEquals(((ContentControl)main.FindName("DllInstallationPage")).Content,verified),"Verified workflow lost");
            var other=Create();Set(main,"selectedExe",other.Exe);Set(main,"selectedGameRoot",other.Root);Call(main,"OpenManualNvidia");Check(Get(main,"manualDllTarget") is null&&Get(main,"manualDllPlan") is null&&selection.Count==0&&!manual.ApplyButton.IsEnabled,"Another game inherited target/selection/approval");
        }finally{main.Close();}
    });
    private static void UiNavigation()=>WpfCheckHost.Run(()=>{
        var main=new MainWindow();try{
            var home=(Button)main.FindName("HomeNavButton");var panel=(Panel)home.Parent;var controls=panel.Children.OfType<Button>().ToArray();var expected=new[]{"HomeNavButton","ReShadeNavButton","DllInstallationNavButton","NvidiaComponentsNavButton","MfgSettingsNavButton","AboutNavButton","ApplicationSettingsNavButton"};Check(controls.Take(7).Select(b=>b.Name).SequenceEqual(expected)&&controls[7].Content as string=="—","Settings gear misplaced or navigation wrong");
            var view=(ReShadeManagerView)main.FindName("ReShadeView");Check(view.AntiCheatPanel.Visibility==Visibility.Collapsed&&view.GeneralUsageWarning.FontSize==11&&view.VersionText.Visibility==Visibility.Collapsed,"R18/R19 lost");
            foreach(var culture in UiLocalization.Cultures){LocalizedPresentation.Language.SetCulture(culture,false);Call(main,"RenderReShade");Check(view.EvidenceCaution.Text==LocalizedPresentation.Language.Lookup("Proxy DLL names and version strings do not prove Full Add-on Support or runtime loading.")&&view.DetectionDetailsButton.Content as string==LocalizedPresentation.Language.Lookup("Detection details"),"R20 late technical text untranslated");}
        }finally{main.Close();LocalizedPresentation.Language.SetCulture("en-US",false);}
    });
    private static void Localization()
    {
        var l=new UiLocalization();Check(l.ValidateCatalogs().Count==0,"Catalog integrity");var keys=new[]{"Verified Update","Manual DLL Installation","NVIDIA SDK — Manual DLL Management","Common Runtime Components","All SDK Components",NvidiaSdkManualManager.Disclaimer,NvidiaSdkManualManager.Consent,"Confirmed destination","Manual DLL details","SDK inventory prepared. Select individual files, then refresh Preview."};
        foreach(var culture in UiLocalization.Cultures){l.SetCulture(culture,false);foreach(var key in keys){Check(l.GetCatalog(culture).ContainsKey(key),"Missing key "+culture+" "+key);if(culture!="en-US")Check(l.Lookup(key)!=key,"Untranslated "+culture+" "+key);}Check(Component("sl.common.dll").Version=="2.14.16.0"&&Component("nvngx_dlssg.dll").Version=="310.9.16.0","Technical version changed with culture");
            Check(MainWindow.ManualDllVersion("Unknown",l)==l.Lookup("Unknown")&&MainWindow.ManualDllVersion("Not found",l)==l.Lookup("Not found"),"Missing-version display is untranslated");Check(MainWindow.ManualDllVersion("2,14,16,0",l)=="2.14.16.0"&&MainWindow.ManualDllVersion("Select graphics API",l)=="Select graphics API","Version formatter translates arbitrary vendor identifiers");}
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using RenoDXMfgUnlocker;

internal static class IntelChecks
{
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return ("Intel Vulkan 1.7 isolated file cycle with synthetic addon identity and genuine pinned runtime",VulkanCycle);
        yield return ("Intel rejects arbitrary source/catalog and corrupt native payload", RejectUntrusted);
        yield return ("Intel verifies genuine pinned x64 pair and notices", Genuine);
        yield return ("Intel downloader verifies local HTTP fixtures and reuses cache offline", Cache);
        yield return ("Intel incomplete/cancelled downloads leave no partial files", Incomplete);
        yield return ("Intel mismatched SHA and wrong architecture are refused", Corrupt);
        yield return ("Intel installs only confirmed destination without DLSS-G, reinstalls and restores", Cycle);
        yield return ("Intel rejects stale previews and leaves all target bytes unchanged", Stale);
        yield return ("Intel blocks unknown addon, missing inputs, running EXE and unsafe targets", Preconditions);
        yield return ("Intel preserves backup copies and unrelated files", Backups);
        yield return ("Intel cancellation during apply rolls back all completed writes", CancelApply);
        yield return ("rc9 Intel unknown target DLL version blocks all writes", UnknownTarget);
    }
    private static string Temp()=>Path.Combine(AppContext.BaseDirectory,"RenoDX-Intel-Checks",Guid.NewGuid().ToString("N"));
    private static string Fixtures()=>Environment.GetEnvironmentVariable("RENODX_TEST_INTEL_CACHE") is { } p && Directory.Exists(p)?p:throw new IntelFixtureUnavailableException();
    private static void Check(bool value,string text){if(!value)throw new Exception(text);}
    private static void Refuses(Action action){try{action();}catch(Exception ex) when(ex is IOException or InvalidDataException or OperationCanceledException){return;}throw new Exception("Expected refusal");}
    private static void RejectUntrusted()
    {
        var dir=Temp();Directory.CreateDirectory(dir);var path=Path.Combine(dir,"bad.dll");File.WriteAllText(path,"MZ not a DLL");var service=new IntelRuntimeService(dir);
        Refuses(()=>service.VerifyFileAsync(path,IntelRuntimeService.Assets[0]).GetAwaiter().GetResult());
        Refuses(()=>service.VerifyFileAsync(path,IntelRuntimeService.Assets[0] with{Sha256=new string('a',64)}).GetAwaiter().GetResult());
    }
    private static string Populate()
    {
        var dir=Temp();Directory.CreateDirectory(Path.Combine(dir,IntelRuntimeService.Commit));
        foreach(var asset in IntelRuntimeService.Assets) File.Copy(Path.Combine(Fixtures(),asset.Name),Path.Combine(dir,IntelRuntimeService.Commit,asset.Name));
        return dir;
    }
    private static void Genuine()
    {
        var service=new IntelRuntimeService(Temp());foreach(var a in IntelRuntimeService.Assets)service.VerifyFileAsync(Path.Combine(Fixtures(),a.Name),a).GetAwaiter().GetResult();
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> handler):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;var r=handler(request);r.RequestMessage=request;return Task.FromResult(r);}
    }
    private static void Cache()
    {
        var fixture=Fixtures();var dir=Temp();var handler=new Handler(r=>{
            var a=IntelRuntimeService.Assets.Single(a=>r.RequestUri!.AbsolutePath.EndsWith(a.Source,StringComparison.Ordinal));
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(Path.Combine(fixture,a.Name)))};
        });
        var service=new IntelRuntimeService(dir,new HttpClient(handler));service.PrepareAsync().GetAwaiter().GetResult();Check(handler.Calls==4,"Expected exact 4 pinned assets");
        service.PrepareAsync().GetAwaiter().GetResult();Check(handler.Calls==4,"Cache re-downloaded files");
    }
    private static void Incomplete()
    {
        var dir=Temp();var service=new IntelRuntimeService(dir,new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])})));
        Refuses(()=>service.PrepareAsync().GetAwaiter().GetResult());Check(!Directory.EnumerateFiles(dir,"*.partial",SearchOption.AllDirectories).Any(),"Partial left behind");
        using var cancel=new CancellationTokenSource();cancel.Cancel();Refuses(()=>service.PrepareAsync(token:cancel.Token).GetAwaiter().GetResult());
    }
    private static void Corrupt()
    {
        var dir=Populate();var asset=IntelRuntimeService.Assets[0];var path=Path.Combine(dir,IntelRuntimeService.Commit,asset.Name);
        var bytes=File.ReadAllBytes(path);bytes[100]^=1;File.WriteAllBytes(path,bytes);
        Refuses(()=>new IntelRuntimeService(dir).PrepareAsync().GetAwaiter().GetResult());
        // The exact pinned digest rejects architecture edits before any PE/version trust decision.
        File.WriteAllBytes(path,new byte[asset.Size]);Refuses(()=>new IntelRuntimeService(dir).PrepareAsync().GetAwaiter().GetResult());
    }
    private sealed record Fixture(string Root,string Exe,string Store,IntelRuntimeService Service,VerifiedIntelRuntime Package);
    private static Fixture Create()
    {
        var dir=Temp();var root=Path.Combine(dir,"game");Directory.CreateDirectory(root);
        var exe=Path.Combine(root,"FixtureGame.exe");File.WriteAllText(exe,"isolated fixture");File.WriteAllText(Path.Combine(root,"renodx-mfgunlock.addon64"),"fixture addon");
        var cache=Populate();var service=new IntelRuntimeService(cache,null,_=>"1.6.0.0",_=>false);
        return new(root,exe,Path.Combine(dir,"transactions"),service,new(Path.Combine(cache,IntelRuntimeService.Commit)));
    }
    private static IntelInstallPlan Preview(Fixture f)=>f.Service.PreviewAsync(f.Package,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult();
    private static void VulkanCycle()
    {
        var f=Create();var addonVersion="1.7.0.0";var service=new IntelRuntimeService(Path.GetDirectoryName(f.Package.DirectoryPath)!,null,_=>addonVersion,_=>false);var old=Path.Combine(f.Root,"libxell.dll");File.WriteAllText(old,"original Intel bytes");
        Refuses(()=>service.PreviewAsync(f.Package,f.Root,f.Exe,f.Root,false,IntelIntegration.VulkanExperimental).GetAwaiter().GetResult());
        var plan=service.PreviewAsync(f.Package,f.Root,f.Exe,f.Root,true,IntelIntegration.VulkanExperimental).GetAwaiter().GetResult();Check(plan.Integration==IntelIntegration.VulkanExperimental,"API lost from plan");addonVersion="1.6.0.0";Refuses(()=>service.InstallAsync(f.Package,plan,true,new(f.Store)).GetAwaiter().GetResult());Check(File.ReadAllText(old)=="original Intel bytes","Blocked version wrote files");addonVersion="1.7.0.0";
        var backup=new BackupRestoreService(f.Store);var transaction=service.InstallAsync(f.Package,plan,true,backup).GetAwaiter().GetResult()!;var again=service.PreviewAsync(f.Package,f.Root,f.Exe,f.Root,true,IntelIntegration.VulkanExperimental).GetAwaiter().GetResult();Check(service.InstallAsync(f.Package,again,true,backup).GetAwaiter().GetResult() is null,"Repeated Vulkan file install not no-op");backup.RestoreAsync(f.Root,transaction.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllText(old)=="original Intel bytes"&&!File.Exists(Path.Combine(f.Root,"libxess_fg.dll")),"Vulkan file restore failed");
    }
    private static void Cycle()
    {
        var f=Create();var sub=Path.Combine(f.Root,"SP");Directory.CreateDirectory(sub);File.WriteAllText(Path.Combine(sub,"libxell.dll"),"old Intel copy");
        File.WriteAllText(Path.Combine(f.Root,"libxess_fg.dll"),"original");var backup=new BackupRestoreService(f.Store);var p=Preview(f);
        Check(!File.Exists(Path.Combine(f.Root,"nvngx_dlssg.dll")),"Test unexpectedly depends on NVIDIA");Check(p.Entries.Count==4&&p.Entries.All(e=>Path.GetDirectoryName(e.TargetPath)==f.Root),"Unconfirmed copies entered the plan");
        var result=f.Service.InstallAsync(f.Package,p,true,backup).GetAwaiter().GetResult()!;
        foreach(var row in p.Entries)Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(row.TargetPath))).Equals(row.Sha256,StringComparison.OrdinalIgnoreCase),"Installed hash differs");
        var again=Preview(f);Check(again.Entries.All(e=>e.Action=="No change"),"Reinstall should be already configured");Check(f.Service.InstallAsync(f.Package,again,true,backup).GetAwaiter().GetResult() is null,"Reinstall created transaction");
        backup.RestoreAsync(f.Root,result.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllText(Path.Combine(f.Root,"libxess_fg.dll"))=="original","Original not restored");Check(!File.Exists(Path.Combine(f.Root,"libxell.dll")),"New file not removed");Check(File.ReadAllText(Path.Combine(sub,"libxell.dll"))=="old Intel copy","Nested original not restored");
    }
    private static void Stale()
    {
        var f=Create();var p=Preview(f);var path=Path.Combine(f.Root,"libxell.dll");File.WriteAllText(path,"appeared after Preview");
        Refuses(()=>f.Service.InstallAsync(f.Package,p,true,new(f.Store)).GetAwaiter().GetResult());Check(File.ReadAllText(path)=="appeared after Preview","Conflict overwritten");Check(!File.Exists(Path.Combine(f.Root,"libxess_fg.dll")),"Partial installation");
        var fresh=Preview(f);File.WriteAllText(Path.Combine(f.Root,"renodx-mfgunlock.addon64"),"changed addon");Refuses(()=>f.Service.InstallAsync(f.Package,fresh,true,new(f.Store)).GetAwaiter().GetResult());
    }
    private static void Preconditions()
    {
        var f=Create();Refuses(()=>f.Service.PreviewAsync(f.Package,f.Root,f.Exe,f.Root,false).GetAwaiter().GetResult());
        Refuses(()=>new IntelRuntimeService(Path.GetDirectoryName(f.Package.DirectoryPath)!,null,_=>"1.4.0.0",_=>false).PreviewAsync(f.Package,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
        Refuses(()=>new IntelRuntimeService(Path.GetDirectoryName(f.Package.DirectoryPath)!,null,_=>"1.6.0.0",_=>true).PreviewAsync(f.Package,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
        var target=Path.Combine(f.Root,"nested");Directory.CreateDirectory(target);Refuses(()=>f.Service.PreviewAsync(f.Package,f.Root,f.Exe,target,true).GetAwaiter().GetResult());
        Refuses(()=>f.Service.PreviewAsync(f.Package,f.Root,f.Exe,Path.Combine(f.Root,".."),true).GetAwaiter().GetResult());
        Refuses(()=>f.Service.PreviewAsync(new(Path.GetTempPath()),f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());
    }
    private static void Backups()
    {
        var f=Create();var folder=Path.Combine(f.Root,"mfg_backup_original");Directory.CreateDirectory(folder);var original=Path.Combine(folder,"libxell.dll");File.WriteAllText(original,"backup");var extra=Path.Combine(f.Root,"user.txt");File.WriteAllText(extra,"keep");
        var copies=new[]{"bin/x64_dx12_backup","bin/x64_dx12 - copy","bin/x64_dx12нгг – копія","nested/unrelated"}.Select(n=>Path.Combine(f.Root,n,"libxess_fg.dll")).ToArray();foreach(var copy in copies){Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.WriteAllText(copy,"unrelated original");}
        var p=Preview(f);Check(p.Entries.Count==4,"Backup files incorrectly selected");var backup=new BackupRestoreService(f.Store);var tx=f.Service.InstallAsync(f.Package,p,true,backup).GetAwaiter().GetResult()!;backup.RestoreAsync(f.Root,tx.TransactionId).GetAwaiter().GetResult();Check(File.ReadAllText(original)=="backup" && File.ReadAllText(extra)=="keep","Other files changed");
        Check(copies.All(p=>File.ReadAllText(p)=="unrelated original"),"Apply or Restore changed an unconfirmed copy");
    }
    private static void UnknownTarget()
    {
        var f=Create();var path=Path.Combine(f.Root,"libxell.dll");File.WriteAllText(path,"unidentifiable original");
        var service=new IntelRuntimeService(Path.GetDirectoryName(f.Package.DirectoryPath)!,null,p=>Path.GetExtension(p)==".addon64"?"1.7.0.0":"",_=>false);
        Refuses(()=>service.PreviewAsync(f.Package,f.Root,f.Exe,f.Root,true).GetAwaiter().GetResult());Check(File.ReadAllText(path)=="unidentifiable original"&&!File.Exists(Path.Combine(f.Root,"libxess_fg.dll")),"Unknown target caused a partial write");
    }
    private sealed class CancelProgress(CancellationTokenSource cancel):IProgress<int>{public void Report(int value){if(value>0)cancel.Cancel();}}
    private static void CancelApply()
    {
        var f=Create();var original=Path.Combine(f.Root,"libxess_fg.dll");File.WriteAllText(original,"original bytes");
        var p=Preview(f);using var cancel=new CancellationTokenSource();
        Refuses(()=>f.Service.InstallAsync(f.Package,p,true,new(f.Store),new CancelProgress(cancel),cancel.Token).GetAwaiter().GetResult());
        Check(File.ReadAllText(original)=="original bytes","Cancellation lost original");
        Check(!File.Exists(Path.Combine(f.Root,"libxell.dll")),"Cancellation left a new DLL");
        Check(new BackupRestoreService(f.Store).ListTransactions(f.Root).Single().Status=="RolledBack","Cancellation did not record rollback");
    }
}
internal sealed class IntelFixtureUnavailableException:Exception {public IntelFixtureUnavailableException():base("Set RENODX_TEST_INTEL_CACHE to the genuine pinned official Intel pair + notices; no download is required for unit tests."){} }

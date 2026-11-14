using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RenoDXMfgUnlocker;

internal static class Rc6ProcessChecks
{
    internal static int Run(bool cleanupSubset=false) {
        var source=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        var root=Path.GetFullPath(Path.Combine(source,"../../work/rc6-e2e-"+Guid.NewGuid().ToString("N")[..10]));Directory.CreateDirectory(root);
        using var key=RSA.Create(3072);var publicFile=Path.Combine(root,"public-test.spki.txt");File.WriteAllText(publicFile,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));var scope=Path.Combine(root,"root.txt");File.WriteAllText(scope,root);
        var results=new List<object>();var passed=0;var failed=0;
        Console.WriteLine("E2E_ROOT "+root);
        try {
            var old=Build("old","1.2.0-rc.6");var next=Build("new","1.3.0");
            var zip=Path.Combine(root,"RenoDX_MFG_Unlocker_v1.3.0_Windows_x64.zip");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){archive.CreateEntryFromFile(next,SelfUpdateVerifier.Executable,CompressionLevel.Fastest);using var writer=new StreamWriter(archive.CreateEntry("README.md").Open());writer.Write("Harmless isolated E2E fixture; never a public update.");}
            var zipHash=SelfUpdateVerifier.Sha(zip);var nextHash=SelfUpdateVerifier.Sha(next);var oldHash=SelfUpdateVerifier.Sha(old);
            SelfUpdateFile[] members;
            using(var archive=ZipFile.OpenRead(zip))members=archive.Entries.Select(e=>{using var stream=e.Open();return new SelfUpdateFile(e.FullName,e.Length,Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());}).ToArray();
            foreach(var scenario in cleanupSubset?new[]{"success","exit-before-commit"}:new[]{"success","invalid-signature","tampered","wrong-repository","wrong-version","cancel","lock","readonly","rollback","conflict","exit-before-commit","unexpected-exit"}) {
                var watch=Stopwatch.StartNew();
                try{Scenario(scenario);passed++;results.Add(new{Scenario=scenario,Passed=true,Milliseconds=watch.ElapsedMilliseconds});Console.WriteLine("PASS rc6 real process "+scenario);}
                catch(Exception ex){failed++;results.Add(new{Scenario=scenario,Passed=false,Error=ex.ToString()});Console.WriteLine("FAIL rc6 real process "+scenario+": "+ex);break;}
            }
            File.WriteAllText(Path.Combine(root,"evidence.json"),JsonSerializer.Serialize(new{Root=root,OldSha256=oldHash,NewSha256=nextHash,ZipSha256=zipHash,ProductionKeyUsed=false,RealGameChanges=false,LiveNetwork=false,Results=results},new JsonSerializerOptions{WriteIndented=true}));
            void Scenario(string scenario) {
                var install=Path.Combine(root,"cases",scenario,"install");Directory.CreateDirectory(install);var target=Path.Combine(install,SelfUpdateVerifier.Executable);File.Copy(old,target);
                const string settings="{\"language\":\"uk-UA\",\"unchanged\":true}";File.WriteAllText(Path.Combine(install,"local-settings.json"),settings);File.WriteAllText(Path.Combine(install,"unrelated.dat"),"do not change");File.WriteAllText(Path.Combine(install,"fixture-mode.txt"),scenario);
                var inputs=Path.Combine(root,"inputs",scenario);Directory.CreateDirectory(inputs);var package=Path.Combine(inputs,Path.GetFileName(zip));File.Copy(zip,package);
                var version=scenario=="wrong-version"?"1.1.0":"1.3.0";
                var manifest=new SelfUpdateManifest(1,"e2e-local-only",scenario=="wrong-repository"?"attacker/repo":ApplicationUpdateService.Repository,"stable","windows-x64",version,Path.GetFileName(zip),new FileInfo(zip).Length,zipHash,members);
                var canonical=SelfUpdateVerifier.Canonical(manifest);var signature=key.SignData(canonical,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);if(scenario=="invalid-signature")signature[0]^=1;
                File.WriteAllBytes(package+".update.json",canonical);File.WriteAllBytes(package+".sig",signature);
                if(scenario=="tampered"){using var file=new FileStream(package,FileMode.Open,FileAccess.ReadWrite);file.Position=30;var b=file.ReadByte();file.Position=30;file.WriteByte((byte)(b^1));}
                object Asset(long id,string name,long size,string? digest=null)=>new{id,name,state="uploaded",size,digest,url=ApplicationUpdateService.ApiRoot+"/releases/assets/"+id,browser_download_url=ApplicationUpdateService.WebRoot+"/releases/download/v1.3.0/"+name};
                var metadata=new[]{new{id=1,url=ApplicationUpdateService.ApiRoot+"/releases/1",html_url=ApplicationUpdateService.WebRoot+"/releases/tag/v1.3.0",tag_name="v1.3.0",name="Harmless local fixture",body="# Local test\n- No real update or game",published_at="2026-11-02T00:00:00Z",draft=false,prerelease=false,assets=new[]{Asset(1,Path.GetFileName(zip),new FileInfo(zip).Length,"sha256:"+zipHash),Asset(2,Path.GetFileName(package)+".update.json",canonical.Length),Asset(3,Path.GetFileName(package)+".sig",signature.Length)}}};
                File.WriteAllText(Path.Combine(inputs,"releases.json"),JsonSerializer.Serialize(metadata));
                FileStream? locked=null;if(scenario=="lock")locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);if(scenario=="readonly")File.SetAttributes(target,FileAttributes.ReadOnly);
                try {
                    var start=new ProcessStartInfo(target){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=install,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--run-update");start.ArgumentList.Add(scenario);
                    using var process=Process.Start(start)!;if(!process.WaitForExit(45000)){process.Kill();throw new Exception("Old fixture exit timed out.");}
                    var shouldUpdate=scenario is "success" or "unexpected-exit";var shouldFallback=scenario is "lock" or "readonly" or "rollback" or "conflict";
                    if(shouldUpdate||shouldFallback) {
                        var receipt=Path.Combine(install,shouldUpdate?"new-started.json":"fallback-started.json");Wait(()=>File.Exists(receipt),TimeSpan.FromSeconds(35),"New/fallback application was not launched. "+process.StandardError.ReadToEnd());
                        using var parsed=ReadJson(receipt);var reported=parsed.RootElement;
                        Check(reported.GetProperty("Version").GetString()==(shouldUpdate?"1.3.0":"1.2.0-rc.6"),"Actual restarted process has wrong version.");Check(reported.GetProperty("Settings").GetString()==settings,"Local settings changed across restart.");
                        if(reported.TryGetProperty("ProcessId",out var pid))WaitForOwnedExit(pid.GetInt32());
                    }
                    if(File.Exists(Path.Combine(install,"helper-started.json"))){using var helper=ReadJson(Path.Combine(install,"helper-started.json"));WaitForOwnedExit(helper.RootElement.GetProperty("ProcessId").GetInt32());}
                    Check(SelfUpdateVerifier.Sha(target)==(shouldUpdate?nextHash:oldHash),"Final executable bytes incorrect.");Check(File.ReadAllText(Path.Combine(install,"local-settings.json"))==settings&&File.ReadAllText(Path.Combine(install,"unrelated.dat"))=="do not change","Unrelated/local data changed.");
                    if(scenario=="exit-before-commit") {
                        Check(!File.Exists(Path.Combine(install,"committed.json"))&&!File.Exists(Path.Combine(install,"new-started.json")),"Uncommitted parent exit applied an update.");
                        using var pending=ReadJson(Path.Combine(install,"prepared.json"));var session=pending.RootElement.GetProperty("Session").GetString()!;
                        Check(!File.Exists(Path.Combine(session,"package.zip"))&&!File.Exists(Path.Combine(session,"payload.exe")),"Uncommitted exit left staged package/payload.");
                    }
                    if(shouldUpdate) {
                        using var prepared=ReadJson(Path.Combine(install,"prepared.json"));var session=prepared.RootElement.GetProperty("Session").GetString()!;
                        Wait(()=>!File.Exists(Path.Combine(session,"worker.exe"))&&!File.Exists(Path.Combine(session,"payload.exe"))&&!File.Exists(Path.Combine(session,"package.zip")),TimeSpan.FromSeconds(5),"Verified temporary files were not cleaned.");
                        Check(SelfUpdateVerifier.Sha(Path.Combine(session,"original.exe"))==oldHash,"Original recovery snapshot was lost.");
                    }
                    if(scenario=="rollback") {using var prepared=ReadJson(Path.Combine(install,"prepared.json"));using var status=ReadJson(Path.Combine(prepared.RootElement.GetProperty("Session").GetString()!,"result.json"));Check(status.RootElement.GetProperty("Restored").GetBoolean(),"Real process rollback did not restore.");}
                    if(scenario=="conflict")Check(Directory.GetFiles(install,"*.backup").Any(p=>File.ReadAllText(p)=="foreign fixture backup - preserve"),"Foreign backup conflict lost.");
                }finally{locked?.Dispose();if(File.Exists(target))File.SetAttributes(target,FileAttributes.Normal);}
            }
            string Build(string name,string version) {
                var output=Path.Combine(root,"build-"+name);var start=new ProcessStartInfo("dotnet"){WorkingDirectory=source,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var argument in new[]{"publish","tests/SelfUpdateE2EFixture/SelfUpdateE2EFixture.csproj","-c","Release","-r","win-x64","--self-contained","true","-p:PublishSingleFile=true","-p:IncludeNativeLibrariesForSelfExtract=true","-p:DebugType=None","-p:DebugSymbols=false","-p:NuGetAudit=false","-p:FixtureVersion="+version,"-p:FixturePublicKeyFile="+publicFile,"-p:FixtureRootFile="+scope,"-o",output,"-v","minimal"})start.ArgumentList.Add(argument);
                using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(120000)){process.Kill(true);throw new Exception("Fixture build timeout.");}var text=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(root,"build-"+name+".log"),text);Check(process.ExitCode==0,"Fixture build failed: "+text);Console.WriteLine("BUILT harmless "+name+" "+version);return Path.Combine(output,"SelfUpdateE2EFixture.exe");
            }
        }catch(Exception ex){failed++;Console.WriteLine("FAIL rc6 E2E preparation: "+ex);File.WriteAllText(Path.Combine(root,"preparation-error.txt"),ex.ToString());}
        Console.WriteLine($"E2E passed {passed}; failed {failed}; skipped 0. Evidence: {root}");return failed==0?0:1;
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static JsonDocument ReadJson(string path){using var stream=File.OpenRead(path);return JsonDocument.Parse(stream);}
    private static void Wait(Func<bool> condition,TimeSpan timeout,string message){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed>timeout)throw new TimeoutException(message);Thread.Sleep(50);}}
    private static void WaitForOwnedExit(int pid){try{using var process=Process.GetProcessById(pid);Check(process.WaitForExit(10000),"Owned fixture process did not exit.");}catch(ArgumentException){}}
}

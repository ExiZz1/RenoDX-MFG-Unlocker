using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using RenoDXMfgUnlocker;

internal static class FixtureScenario
{
internal static async Task<int> Run(string[] args)
{
// Harmless windowless WPF fixture only. No MainWindow, game/vendor execution, production keys,
// live network, registry or actual user data. Root and public TEST key are compiled resources.
string Resource(string name){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SelfUpdateE2EFixture."+name)!;using var reader=new StreamReader(stream);return reader.ReadToEnd().Trim();}
var root=Path.GetFullPath(Resource("Root"));var key=Resource("PublicKey");var storage=Path.Combine(root,"staging");
var verifier=new SelfUpdateVerifier(new Dictionary<string,string>{{"e2e-local-only",key}});
var exe=Path.GetFullPath(Environment.ProcessPath!);var version=Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
if(!SelfUpdatePaths.Within(root,exe))return 90;
void Record(string folder,string name,object value){using var stream=new FileStream(Path.Combine(folder,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(stream,value);stream.Flush(true);}
if(args.Length>0&&args[0]=="--self-update-helper") {
    var session=Path.GetDirectoryName(exe)!;
    Record(session,"fixture-worker.json",new{ProcessId=Environment.ProcessId,Version=version});
    var code=await SelfUpdateHandoff.RunWorkerForHostAsync(args,verifier,storage,(update,phase)=>{
        var mode=File.ReadAllText(Path.Combine(Path.GetDirectoryName(update.Target)!,"fixture-mode.txt"));
        if(mode=="rollback"&&phase=="AfterReplace")throw new IOException("Explicit E2E post-replacement fault injection.");
        if(mode=="conflict"&&phase=="BeforeReplace")File.WriteAllText(Path.Combine(Path.GetDirectoryName(update.Target)!,".renodx-update-"+Path.GetFileName(session)+".backup"),"foreign fixture backup - preserve");
    });
    Record(session,"fixture-worker-exit.json",new{Code=code});return code;
}
var install=Path.GetDirectoryName(exe)!;
if(args.Length==2&&args[0] is "--self-update-complete" or "--self-update-failed") {
    var session=Path.Combine(storage,args[1]);
    if(!Guid.TryParseExact(args[1],"N",out _)||!Directory.Exists(session))return 91;
    Record(install,args[0]=="--self-update-complete"?"new-started.json":"fallback-started.json",new{Version=version,ProcessId=Environment.ProcessId,ExeSha256=SelfUpdateVerifier.Sha(exe),Settings=File.ReadAllText(Path.Combine(install,"local-settings.json"))});
    // Use production cleanup after the copied worker has really exited.
    await Task.Delay(500);SelfUpdatePaths.CleanupSession(session,true);return 0;
}
if(args.Length!=2||args[0]!="--run-update"||!System.Text.RegularExpressions.Regex.IsMatch(args[1],"^[a-z-]+$"))return 92;
var scenario=args[1];var modeFile=Path.Combine(install,"fixture-mode.txt");
if(!File.Exists(modeFile)||File.ReadAllText(modeFile)!=scenario)return 93;
Record(install,"old-started.json",new{Version=version,ProcessId=Environment.ProcessId});
var inputs=Path.Combine(root,"inputs",scenario);var json=File.ReadAllText(Path.Combine(inputs,"releases.json"));
using var client=new HttpClient(new LocalFixtureTransport(json,inputs));using var service=new ApplicationUpdateService(client);
PreparedSelfUpdate? prepared=null;
try {
    var result=await service.CheckAsync(version);
    if(!result.UpdateAvailable||result.Latest is not {} release)return 94;
    using var cancellation=new CancellationTokenSource();if(scenario=="cancel")cancellation.Cancel();
    var archive=await service.DownloadAsync(release,Path.Combine(root,"downloads",scenario),token:cancellation.Token);
    var signed=await service.GetSignedMetadataAsync(release,cancellation.Token);
    prepared=SelfUpdateHandoff.Prepare(verifier,archive,signed,release,version,exe,root:storage);
    Record(install,"prepared.json",new{prepared.Session});
    await SelfUpdateHandoff.StartForHostAsync(prepared,verifier,observer:phase=>{
        if(phase.StartsWith("HelperStarted:",StringComparison.Ordinal))Record(install,"helper-started.json",new{ProcessId=int.Parse(phase[14..])});
        if(phase=="Ready"&&scenario=="exit-before-commit")Environment.Exit(24);
    });
    Record(install,"committed.json",new{Version=version});
    if(scenario=="unexpected-exit")Environment.Exit(23);
    return 0;
}catch(Exception ex){Record(install,"refused.json",new{Error=ex.GetType().Name,ex.Message});if(prepared is not null)SelfUpdatePaths.CleanupSession(prepared.Session);return 20;}

}
}

internal sealed class LocalFixtureTransport(string json,string inputs):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        token.ThrowIfCancellationRequested();var uri=request.RequestUri!;
        if(uri.Host=="api.github.com"&&uri.AbsolutePath=="/repos/ExiZz1/RenoDX-MFG-Unlocker/releases")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        if(uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/ExiZz1/RenoDX-MFG-Unlocker/releases/download/",StringComparison.Ordinal))throw new IOException("Fixture attempted live or unofficial transport.");
        var file=Path.GetFileName(uri.AbsolutePath);var path=Path.Combine(inputs,file);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(path))});
    }
}

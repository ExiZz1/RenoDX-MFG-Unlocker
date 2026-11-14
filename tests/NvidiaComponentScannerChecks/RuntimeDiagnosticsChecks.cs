using System.IO;
using System.Text.Json;
using RenoDXMfgUnlocker;

internal static class RuntimeDiagnosticsChecks
{
    private static readonly DateTimeOffset Now=new(2026,10,29,12,0,0,TimeSpan.Zero);
    private static readonly string Exe=Path.Combine(Path.GetTempPath(),"DiagnosticsFixture","Game.exe");
    private static readonly RuntimeProcessSnapshot Current=new(42,Now.AddMinutes(-2),"synthetic current process");
    public static IEnumerable<(string Name,Action Run)> All()
    {
        yield return ("diagnostics missing logs never implies runtime or MFG activity",Missing);
        yield return ("diagnostics isolates latest session and does not reuse previous activity",Previous);
        yield return ("diagnostics recognizes fresh direct SDK output only for matching current process",Fresh);
        yield return ("diagnostics rejects stale PID, EXE version and installation-before-restart evidence",Mismatch);
        yield return ("diagnostics malformed, wrong-type, duplicate and oversized data fail closed",Malformed);
        yield return ("diagnostics disabled/error/ambiguous output supersedes earlier activity",Superseded);
        yield return ("diagnostics does not confuse Dynamic capability or fixed MFG with active Dynamic",Dynamic);
        yield return ("diagnostics records Dynamic accepted separately from active and rejection",DynamicAccepted);
        yield return ("diagnostics inspection is read-only and handles missing logs",ReadOnly);
        yield return ("Intel addon policy uses minimum 1.5.8 and rejects malformed versions",IntelVersions);
        yield return ("diagnostics 1.7 schema retains process correlation and never infers activity from version",Schema17);
    }
    private static RuntimeLogInput Native(params string[] extra)=>new(Path.Combine(Path.GetDirectoryName(Exe)!,"MFGAdaUnlock-XeMFG.log"),
        JsonSerializer.Serialize(new{@event="native_session",utc="2026-10-29T11:58:00Z",executable="Game.exe",executable_version="1.0.0.0",addon="1.6.0",pid=42,tick=1000})+"\n"+string.Join("\n",extra),Now);
    private const string Modules="{\"event\":\"native_modules\",\"pid\":42,\"tick\":1001,\"modules\":[{\"name\":\"libxess_fg.dll\",\"version\":\"1.3.1.78\"},{\"name\":\"libxell.dll\",\"version\":\"1.3.2.10\"}]}";
    private const string Output="{\"event\":\"output_status\",\"pid\":42,\"tick\":120000,\"handle\":1,\"enabled\":1,\"frames_presented\":4,\"interpolation_result\":0,\"tensor_launches\":300}";
    private static RuntimeDiagnosticReport Inspect(RuntimeLogInput log,RuntimeProcessSnapshot? process=null,string version="1.0.0.0",DateTimeOffset? installed=null)=>
        RuntimeDiagnosticsService.Analyze(Exe,version,process??Current,[log],Now,installed,installationHashesVerified:installed is not null);
    private static void Check(bool v,string m){if(!v)throw new Exception(m);}
    private static bool Active(RuntimeDiagnosticReport r)=>r.Statuses.Any(s=>s.Contains(": Active reported",StringComparison.Ordinal));
    private static void Missing(){var r=RuntimeDiagnosticsService.Analyze(Exe,"1.0.0.0",new(null,null,"not running"),[],Now);Check(!Active(r)&&r.Warnings.Any(w=>w.StartsWith("No supported logs")),"Missing logs inferred activity");}
    private static void Previous()
    {
        var log=Native(Modules,Output);Check(!Active(Inspect(log,new(null,null,"No game process"))),"Historical activity was current");
        log=log with{Text=log.Text+"\n"+JsonSerializer.Serialize(new{@event="native_session",utc="2026-10-29T11:59:00Z",executable="Game.exe",executable_version="1.0.0.0",addon="1.6.0",pid=43,tick=200000})};
        Check(!Active(Inspect(log)),"Earlier session reused");
        log=log with{Text=log.Text.Replace("\"executable\":\"Game.exe\"","\"executable\":\"Other.exe\"")};Check(!Active(Inspect(log)),"Wrong EXE accepted");
    }
    private static void Fresh(){var r=Inspect(Native(Modules,Output));Check(Active(r),"Fresh SDK output not recognized");Check(r.Statuses.Any(s=>s.StartsWith("Loaded modules: Module mapping reported")),"Mapped report not recognized");}
    private static void Mismatch()
    {
        var log=Native(Modules,Output);
        Check(!Active(Inspect(log,Current with{Id=43})),"Wrong PID accepted");Check(!Active(Inspect(log,version:"2.0.0.0")),"Wrong version accepted");
        Check(!Active(Inspect(log,Current with{StartedUtc=Now.AddSeconds(-3)})),"PID reuse accepted");
        var r=Inspect(log,installed:Now.AddMinutes(-1));Check(!Active(r)&&r.Statuses.Last().Contains("Verified file change"),"Pre-install session verified new files");
        r=Inspect(log,installed:Now.AddMinutes(-3));Check(r.Statuses.Last().Contains("Restart observed"),"New matching session not recognized");
        Check(!Active(Inspect(Native(Modules,Output.Replace("120000","1002")))),"Old SDK output was current");
    }
    private static void Malformed()
    {
        foreach(var bad in new[]{"{bad",Output.Replace("\"handle\":1,",""),Output.Replace("\"tick\":120000","\"tick\":\"bad\""),Output.Replace("\"tick\":120000","\"tick\":9223372036854775807"),Output.Replace("\"enabled\":1","\"enabled\":1,\"enabled\":0"),Output.Replace("\"frames_presented\":4","\"frames_presented\":null")})
            Check(!Active(Inspect(Native(Modules,bad))),"Malformed output inferred activity");
        var huge=Native() with{Text=new string('x',8*1024*1024+1)};Check(!Active(Inspect(huge)),"Oversized data inferred activity");
    }
    private static void Superseded()
    {
        Check(!Active(Inspect(Native(Modules,Output,Output.Replace("\"enabled\":1","\"enabled\":0")))),"Later disabled ignored");
        Check(!Active(Inspect(Native(Modules,Output,"{\"event\":\"startup_rejected\",\"pid\":42,\"tick\":120001,\"reason\":\"unreviewed_Intel_runtime_pair\"}"))),"Error ignored");
        Check(!Active(Inspect(Native(Modules,Output,Output.Replace("\"handle\":1","\"handle\":2")))),"Ambiguous contexts inferred global active");
    }
    private static RuntimeLogInput Reshade(string lines)=>new(Path.Combine(Path.GetDirectoryName(Exe)!,"ReShade.log"),
        "12:00:00:000 [ 42] | INFO | Initializing crosire's ReShade version '6.8.0' (64-bit) loaded from 'dxgi.dll' into '"+Exe+"' (0x1234) ...\n"+lines,Now);
    private static void Dynamic()
    {
        var r=Inspect(Reshade("mfgunlock: slDLSSGGetState confirms NVIDIA Dynamic MFG support.\nmfgunlock: slDLSSGGetState reports 4 frame(s) actually presented since its previous call."));
        Check(!Active(r)&&r.Statuses.Any(s=>s.StartsWith("NVIDIA Dynamic MFG: Not verified")),"Capability/fixed count inferred Dynamic active");
    }
    private static void DynamicAccepted()
    {
        var r=Inspect(Reshade("mfgunlock: NVIDIA Dynamic MFG accepted (target 120 FPS); multiplier selection and pacing remain provider-controlled."));
        Check(!Active(r)&&r.Statuses.Any(s=>s.Contains("Enabled / accepted in recorded")),"Accepted options not distinguished");
        r=Inspect(Reshade("mfgunlock: NVIDIA Dynamic MFG accepted (target 120 FPS);\nmfgunlock: NVIDIA Dynamic MFG was rejected with sl::Result 21;"));
        Check(r.Statuses.Any(s=>s.Contains("Error / unsupported reported")),"Later rejection ignored");
    }
    private static void ReadOnly()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"DiagnosticsFixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var exe=Path.Combine(root,"Game.exe");File.Copy(Environment.ProcessPath!,exe);var log=Path.Combine(root,"ReShade.log");File.WriteAllText(log,"unrecognized partial log");var original=File.ReadAllBytes(log);
        var r=new RuntimeDiagnosticsService().InspectAsync(exe,root).GetAwaiter().GetResult();Check(File.ReadAllBytes(log).SequenceEqual(original)&&Directory.GetFiles(root).Length==2,"Read-only inspector changed files");Check(!Active(r),"Unrecognized log active");
        using(var locked=new FileStream(log,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {r=new RuntimeDiagnosticsService().InspectAsync(exe,root).GetAwaiter().GetResult();Check(r.Warnings.Any(w=>w.Contains("ReShade.log")),"Locked log not reported");}
    }
    private static void IntelVersions()
    {
        Check(IntelRuntimeService.SupportsAddon("1.5.8.0")&&IntelRuntimeService.SupportsAddon("1.6.0.0")&&IntelRuntimeService.SupportsAddon("1.7.0.0"),"Reviewed version rejected");
        foreach(var v in new[]{"1.5.2.0","1.5.7.0","1.6.0-beta.1","invalid 2.0.0.0","","nonsense"})Check(!IntelRuntimeService.SupportsAddon(v),"Unreviewed addon allowed");
    }
    private static void Schema17(){var log=Native(Modules,Output);log=log with{Text=log.Text.Replace("\"addon\":\"1.6.0\"","\"addon\":\"1.7.0\"")};Check(Active(Inspect(log)),"Reviewed 1.7 schema not recognized");Check(!Active(Inspect(log,new(null,null,"not running"))),"Version promoted historical logs");var empty=Native();empty=empty with{Text=empty.Text.Replace("1.6.0","1.7.0")};Check(!Active(Inspect(empty)),"Version alone inferred activity");}
}

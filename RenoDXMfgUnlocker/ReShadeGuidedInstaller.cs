using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;

namespace RenoDXMfgUnlocker;

public interface IReShadeInstallerProcess { Task<int?> RunAsync(string installer); }
internal sealed class OfficialReShadeInstallerProcess:IReShadeInstallerProcess
{
    private readonly Action<string> log;
    internal OfficialReShadeInstallerProcess(Action<string>? log = null) => this.log = log ?? (_ => { });
    internal static ProcessStartInfo CreateStartInfo(string installer)=>new(){FileName=installer,UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(installer),WindowStyle=ProcessWindowStyle.Normal};
    public Task<int?> RunAsync(string installer) => RunAsync(installer, () => { });

    internal async Task<int?> RunAsync(string installer, Action started)
    {
        // No target/API/headless switches: retain the official start page and all its warnings.
        using var process=Process.Start(CreateStartInfo(installer));
        log(process is null ? "shell-result no-handle" : $"process-start pid={process.Id}");
        // A null handle cannot rule out shell handoff. Keep the confirmation step.
        started();
        if (process is null) return null;
        // Poll the actual handle independently of shell/dispatcher event delivery. No elapsed
        // duration marks a process as exited.
        while (!process.HasExited) await Task.Delay(400).ConfigureAwait(false);
        var exitCode = process.ExitCode;
        log($"process-exit pid={process.Id} code={exitCode}");
        return exitCode;
    }

    internal static bool MayStillBeRunning(string installer, Action<string>? log = null)
    {
        // Reviewed Setup relaunches its own assembly for elevation.
        // Do not try to read MainModule on elevated copies. A same-name process blocks conservatively.
        try
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(installer));
            try
            {
                var active = processes.Where(p=>!p.HasExited).Select(p=>p.Id).ToArray();
                log?.Invoke($"handoff-probe active={string.Join(',',active)}");
                return active.Length != 0;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            log?.Invoke($"handoff-probe failed={ex.GetType().Name}");
            return true;
        }
    }
}
public sealed record ReShadeGuidedResult(int? ExitCode,ReShadeDetectionReport Before,ReShadeDetectionReport After);
public sealed class ReShadeGuidedInstaller
{
    private readonly ReShadePackageService packages;private readonly ReShadeDetectionService detector;private readonly IReShadeInstallerProcess process;private readonly Func<string,bool> running;
    internal ReShadeGuidedInstaller(ReShadePackageService packages,ReShadeDetectionService detector,IReShadeInstallerProcess process,Func<string,bool> running){this.packages=packages;this.detector=detector;this.process=process;this.running=running;}
    public async Task<ReShadeGuidedResult> RunAsync(string root,string exe,ReShadeGraphicsApi api,PreparedReShadeInstaller prepared,bool approved,bool vulkanApproved,CancellationToken token=default)
    {
        var launch = await LaunchAsync(root, exe, api, prepared, approved, vulkanApproved, () => { }, token).ConfigureAwait(false);
        return new(launch.Code, launch.Before, detector.Scan(root, exe, api));
    }

    internal async Task<(int? Code, ReShadeDetectionReport Before)> LaunchAsync(string root, string exe, ReShadeGraphicsApi api,
        PreparedReShadeInstaller prepared, bool approved, bool vulkanApproved, Action started, CancellationToken token = default,
        ReShadeManualRiskApproval? riskApproval = null)
    {
        token.ThrowIfCancellationRequested();if(!approved||!Enum.IsDefined(api)||api==ReShadeGraphicsApi.Unknown||api==ReShadeGraphicsApi.Vulkan&&!vulkanApproved)throw new InvalidDataException("Confirm the selected game, graphics API and official installer warnings.");
        packages.ValidateGameRoot(root);RuntimeTargetPolicy.Validate(root,exe,Path.GetDirectoryName(exe)!,false);if(running(exe))throw new IOException("Close the selected game before opening ReShade setup.");
        if(!ReShadeSetupPathPolicy.CanInspect(exe))throw new InvalidDataException(ReShadeSetupPathPolicy.BlockMessage);
        using(var game=new FileStream(exe,FileMode.Open,FileAccess.Read,FileShare.Read))using(var pe=new PEReader(game))if(pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64)throw new InvalidDataException("MFG Unlock requires a 64-bit game executable.");
        var before=detector.Scan(root,exe,api);
        ReShadeManualLaunchPolicy.Validate(root,exe,api,before,riskApproval);
        packages.Reverify(prepared);token.ThrowIfCancellationRequested();
        using var locked=new FileStream(prepared.Path,FileMode.Open,FileAccess.Read,FileShare.Read);packages.Reverify(prepared);
        if(running(exe))throw new IOException("Close the selected game before opening ReShade setup.");
        token.ThrowIfCancellationRequested();
        ReShadeManualLaunchPolicy.Validate(root,exe,api,detector.Scan(root,exe,api),riskApproval);
        if(running(exe))throw new IOException("Close the selected game before opening ReShade setup.");
        if(process is OfficialReShadeInstallerProcess && OfficialReShadeInstallerProcess.MayStillBeRunning(prepared.Path))
            throw new IOException("Close all official installer windows, including elevated child windows, before rescanning. Our Restore does not control their changes.");
        var code = process is OfficialReShadeInstallerProcess official
            ? await official.RunAsync(prepared.Path, started).ConfigureAwait(false)
            : await process.RunAsync(prepared.Path).ConfigureAwait(false);
        return (code, before);
    }
}

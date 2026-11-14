using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RenoDXMfgUnlocker;

internal static class SelfUpdateHandoff
{
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint id);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint id);
    internal static PreparedSelfUpdate Prepare(SelfUpdateVerifier verifier,string archive,SignedUpdateMetadata signed,ApplicationRelease release,string current,string target,string? forbiddenGameRoot=null,string? root=null,Action<string,int,int>? progress=null) {
        if(!verifier.Configured)throw new InvalidDataException(SelfUpdateTrust.MissingKey);
        if(forbiddenGameRoot is not null&&(target.Equals(forbiddenGameRoot,StringComparison.OrdinalIgnoreCase)||SelfUpdatePaths.Within(forbiddenGameRoot,target)))throw new IOException("The application is inside the selected game. Use manual update outside the game folder.");
        verifier.Verify(signed,release,current);
        var stagingRoot=Path.GetFullPath(root??SelfUpdatePaths.ProductionRoot);
        // Reject an unsafe storage location before even creating the cache directory.
        SelfUpdatePaths.ValidateTarget(target,Path.Combine(stagingRoot,"pending-session"));
        var session=SelfUpdatePaths.NewSession(stagingRoot);
        try {
            SelfUpdatePaths.ValidateTarget(target,session);
            using var targetLease=SelfUpdatePaths.LockDirectory(Path.GetDirectoryName(target)!);
            using var sessionLease=SelfUpdatePaths.LockDirectory(session);
            using var installed=SelfUpdatePaths.ReadFile(target);var oldHash=SelfUpdateVerifier.Sha(installed);
            using(var source=SelfUpdatePaths.ReadFile(archive))using(var copy=new FileStream(Path.Combine(session,"package.zip"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){source.CopyTo(copy);copy.Flush(true);}
            verifier.StageExecutable(Path.Combine(session,"package.zip"),session,signed,release,current,progress);
            return new(session,Path.GetFullPath(target),current,oldHash,release,signed);
        }catch{SelfUpdatePaths.CleanupSession(session);throw;}
    }
    private static async Task WriteFrame(Stream stream,byte[] bytes,CancellationToken token) {
        if(bytes.Length>196608)throw new InvalidDataException("Update handoff exceeds limit.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length),token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
    }
    private static async Task<byte[]> ReadFrame(Stream stream,CancellationToken token) {
        var prefix=new byte[4];await stream.ReadExactlyAsync(prefix,token);var size=BitConverter.ToInt32(prefix);if(size is <1 or >196608)throw new InvalidDataException("Invalid update handoff size.");var bytes=new byte[size];await stream.ReadExactlyAsync(bytes,token);return bytes;
    }
    internal static Task<bool> StartAsync(PreparedSelfUpdate update,CancellationToken token=default) {
        if(!SelfUpdateTrust.AutomaticReady)throw new InvalidDataException(SelfUpdateTrust.BlockReason);
        return StartForHostAsync(update,new SelfUpdateVerifier(),token);
    }
    // The production entry above supplies embedded production trust. A separate, non-shipping
    // test host supplies its own compiled-in test trust, never a key from arguments/files/env.
    internal static async Task<bool> StartForHostAsync(PreparedSelfUpdate update,SelfUpdateVerifier verifier,CancellationToken token=default,Action<string>? observer=null) {
        if(!verifier.Configured)throw new InvalidDataException(SelfUpdateTrust.MissingKey);
        var current=Path.GetFullPath(Environment.ProcessPath??throw new IOException("Current executable unavailable."));
        if(!current.Equals(update.Target,StringComparison.OrdinalIgnoreCase))throw new IOException("Self-update may replace only the running application.");
        verifier.VerifyPackage(Path.Combine(update.Session,"package.zip"),update.Signed,update.Release,update.CurrentVersion);
        using var parent=Process.GetCurrentProcess();using var directoryLease=SelfUpdatePaths.LockDirectory(update.Session);using var currentFile=SelfUpdatePaths.ReadFile(current);
        if(SelfUpdateVerifier.Sha(currentFile)!=update.OldSha256)throw new IOException("Running application changed before helper handoff.");
        var worker=Path.Combine(update.Session,"worker.exe");using(var copy=new FileStream(worker,FileMode.CreateNew,FileAccess.Write,FileShare.None)){currentFile.CopyTo(copy);copy.Flush(true);}
        using var workerFile=SelfUpdatePaths.ReadFile(worker);if(SelfUpdateVerifier.Sha(workerFile)!=update.OldSha256)throw new IOException("Helper copy integrity failed.");
        var pipeName="RenoDX-SelfUpdate-"+Guid.NewGuid().ToString("N");
        using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var start=new ProcessStartInfo(worker){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=update.Session};
        start.ArgumentList.Add("--self-update-helper");start.ArgumentList.Add(pipeName);start.ArgumentList.Add(parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var helper=Process.Start(start)??throw new IOException("Update helper did not start.");
        observer?.Invoke("HelperStarted:"+helper.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var committed=false;
        try {
            await pipe.WaitForConnectionAsync(timeout.Token);
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out var client)||client!=helper.Id)throw new IOException("Untrusted update helper pipe client.");
            await WriteFrame(pipe,JsonSerializer.SerializeToUtf8Bytes(update),timeout.Token);
            if(Encoding.UTF8.GetString(await ReadFrame(pipe,timeout.Token))!="READY")throw new IOException("Update helper rejected the handoff.");
            observer?.Invoke("Ready");
            // Returning true commits the already confirmed restart. Caller immediately closes
            // the modal and shuts down; helper must still wait for the exact parent to exit.
            await WriteFrame(pipe,Encoding.UTF8.GetBytes("APPLY"),timeout.Token);committed=true;return true;
        }finally {
            if(!committed) {
                // Only the verified child created above is terminated. Parent is still alive,
                // so this child cannot yet have entered the file replacement phase.
                try{if(!helper.HasExited)helper.Kill();using var exit=new CancellationTokenSource(TimeSpan.FromSeconds(5));await helper.WaitForExitAsync(exit.Token);}catch(Exception){}
            }
        }
    }
    internal static Task<int> RunWorkerAsync(string[] args)=>SelfUpdateTrust.AutomaticReady?RunWorkerForHostAsync(args,new SelfUpdateVerifier(),SelfUpdatePaths.ProductionRoot):Task.FromResult(78);
    internal static async Task<int> RunWorkerForHostAsync(string[] args,SelfUpdateVerifier verifier,string allowedStorage,Action<PreparedSelfUpdate,string>? fault=null) {
        if(!verifier.Configured||args.Length!=4||args[0]!="--self-update-helper"||!args[1].StartsWith("RenoDX-SelfUpdate-",StringComparison.Ordinal)||!Guid.TryParseExact(args[1][18..],"N",out _)||!int.TryParse(args[2],out var parentId)||!long.TryParse(args[3],out var startTicks))return 78;
        string? verifiedSession=null;
        try {
            var worker=Path.GetFullPath(Environment.ProcessPath!);var session=Path.GetDirectoryName(worker)!;
            if(Path.GetFileName(worker)!="worker.exe"||!Guid.TryParseExact(Path.GetFileName(session),"N",out _)||!Path.GetDirectoryName(session)!.Equals(Path.GetFullPath(allowedStorage),StringComparison.OrdinalIgnoreCase))return 79;
            using var parent=Process.GetProcessById(parentId);
            if(parent.StartTime.ToUniversalTime().Ticks!=startTicks)return 80;
            var target=Path.GetFullPath(parent.MainModule?.FileName??throw new IOException("Parent executable unavailable."));
            using var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await pipe.ConnectAsync(timeout.Token);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var server)||server!=parentId)return 81;
            var update=JsonSerializer.Deserialize<PreparedSelfUpdate>(await ReadFrame(pipe,timeout.Token),new JsonSerializerOptions{MaxDepth=20})??throw new InvalidDataException("Missing update handoff.");
            if(!target.Equals(update.Target,StringComparison.OrdinalIgnoreCase)||!session.Equals(update.Session,StringComparison.OrdinalIgnoreCase)||SelfUpdateVerifier.Sha(worker)!=update.OldSha256||SelfUpdateVerifier.Sha(target)!=update.OldSha256)return 82;
            verifier.VerifyPackage(Path.Combine(session,"package.zip"),update.Signed,update.Release,update.CurrentVersion);
            verifiedSession=session;
            await WriteFrame(pipe,Encoding.UTF8.GetBytes("READY"),timeout.Token);
            var approved=Encoding.UTF8.GetString(await ReadFrame(pipe,timeout.Token))=="APPLY";
            var replacement=new SelfUpdateReplacement(verifier,fault:phase=>fault?.Invoke(update,phase));
            var result=await AfterParentExitAsync(approved,async cancellation=>{await parent.WaitForExitAsync(cancellation);},()=>replacement.Apply(update),TimeSpan.FromMinutes(2));
            if(result is null||result.Status=="Installed")SelfUpdatePaths.CleanupSession(session,true);
            else if(result is not null)replacement.RestartPrevious(update);
            return result?.Status=="Installed"?0:83;
        }catch(Exception){if(verifiedSession is not null)SelfUpdatePaths.CleanupSession(verifiedSession,true);return 84;}
    }
    // Isolated tests use an explicit parent-exit task and an inert replacement callback.
    // Production uses the start-time-bound OS Process handle, never a PID-only polling loop.
    internal static async Task<SelfUpdateResult?> AfterParentExitAsync(bool approved,Func<CancellationToken,Task> wait,Func<SelfUpdateResult> apply,TimeSpan timeout) {
        if(!approved)return null;using var cancellation=new CancellationTokenSource(timeout);
        await wait(cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();return apply();
    }
}

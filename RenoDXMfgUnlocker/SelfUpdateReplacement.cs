using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RenoDXMfgUnlocker;

internal sealed record SelfUpdateIdentity(string Product,string Version,ushort Machine);
internal sealed record SelfUpdateResult(string Status,string Message,bool Restarted,bool Restored);

/// <summary>Single-EXE update transaction, entirely separate from game-file BackupRestoreService.</summary>
internal sealed class SelfUpdateReplacement
{
    private readonly SelfUpdateVerifier verifier;
    private readonly Func<string,SelfUpdateIdentity> identity;
    private readonly Action<string,string> launch;
    private readonly Action<string,string> launchPrevious;
    private readonly Action<string>? fault;
    internal SelfUpdateReplacement(SelfUpdateVerifier verifier,Func<string,SelfUpdateIdentity>? identity=null,Action<string,string>? launch=null,Action<string>? fault=null) {
        this.verifier=verifier;this.identity=identity??ReadIdentity;this.launch=launch??Launch;launchPrevious=launch??LaunchPrevious;this.fault=fault;
    }
    internal static SelfUpdateIdentity ReadIdentity(string path) {
        using var stream=SelfUpdatePaths.ReadFile(path);using var reader=new BinaryReader(stream);
        if(reader.ReadUInt16()!=0x5A4D)throw new InvalidDataException("Update executable is not PE.");stream.Position=60;var pe=reader.ReadInt32();if(pe<64||pe>stream.Length-6)throw new InvalidDataException("Invalid executable header.");stream.Position=pe;if(reader.ReadUInt32()!=0x4550)throw new InvalidDataException("Invalid PE signature.");var machine=reader.ReadUInt16();
        var version=FileVersionInfo.GetVersionInfo(path);return new(version.ProductName??"",version.ProductVersion??"",machine);
    }
    private static void Launch(string target,string session) {
        var info=new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target)!,CreateNoWindow=true};
        info.ArgumentList.Add("--self-update-complete");info.ArgumentList.Add(Path.GetFileName(session));
        using var child=Process.Start(info)??throw new IOException("The updated application did not start.");
    }
    private static void LaunchPrevious(string target,string session) {
        var info=new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target)!,CreateNoWindow=true};
        info.ArgumentList.Add("--self-update-failed");info.ArgumentList.Add(Path.GetFileName(session));
        using var child=Process.Start(info)??throw new IOException("The previous application did not restart.");
    }
    internal bool RestartPrevious(PreparedSelfUpdate update) {
        try {
            verifier.Verify(update.Signed,update.Release,update.CurrentVersion);SelfUpdatePaths.ValidateTarget(update.Target,update.Session);
            using var directory=SelfUpdatePaths.LockDirectory(Path.GetDirectoryName(update.Target)!);using var current=SelfUpdatePaths.ReadFile(update.Target);
            if(SelfUpdateVerifier.Sha(current)!=update.OldSha256)return false;VerifyIdentity(update.Target,update.CurrentVersion);launchPrevious(update.Target,update.Session);return true;
        }catch(Exception){return false;}
    }
    private void VerifyIdentity(string path,string expected) {
        var actual=identity(path);
        if(actual.Product!="RenoDX MFG Unlocker"||actual.Machine!=0x8664||actual.Version!=expected)throw new InvalidDataException("Executable identity, version or architecture does not match the signed update.");
    }
    internal SelfUpdateResult Apply(PreparedSelfUpdate update) {
        var manifest=verifier.Verify(update.Signed,update.Release,update.CurrentVersion);
        SelfUpdatePaths.ValidateTarget(update.Target,update.Session);
        using var targetDirectory=SelfUpdatePaths.LockDirectory(Path.GetDirectoryName(update.Target)!);
        using var stagingDirectory=SelfUpdatePaths.LockDirectory(update.Session);
        var original=Path.Combine(update.Session,"original.exe");var backup=Path.Combine(Path.GetDirectoryName(update.Target)!,".renodx-update-"+Path.GetFileName(update.Session)+".backup");
        var incoming=Path.Combine(Path.GetDirectoryName(update.Target)!,".renodx-update-"+Path.GetFileName(update.Session)+".new");
        var rollback=Path.Combine(Path.GetDirectoryName(update.Target)!,".renodx-update-"+Path.GetFileName(update.Session)+".restore");
        var signedFile=manifest.Files.Single(f=>f.Path==SelfUpdateVerifier.Executable);var replaced=false;var restored=false;var incomingOwned=false;var rollbackOwned=false;
        try {
            verifier.VerifyPackage(Path.Combine(update.Session,"package.zip"),update.Signed,update.Release,update.CurrentVersion);
            using var payload=SelfUpdatePaths.ReadFile(Path.Combine(update.Session,"payload.exe"));
            if(payload.Length!=signedFile.Size||SelfUpdateVerifier.Sha(payload)!=signedFile.Sha256)throw new InvalidDataException("Staged payload changed.");
            VerifyIdentity(Path.Combine(update.Session,"payload.exe"),manifest.Version);
            using(var old=SelfUpdatePaths.ReadFile(update.Target)) {
                if(SelfUpdateVerifier.Sha(old)!=update.OldSha256)throw new IOException("The installed application changed after update preparation.");VerifyIdentity(update.Target,update.CurrentVersion);
                if(new DriveInfo(Path.GetPathRoot(update.Target)!).AvailableFreeSpace<signedFile.Size+old.Length+16L*1024*1024)throw new IOException("Insufficient disk space for update and recovery.");
                using(var recovery=new FileStream(original,FileMode.CreateNew,FileAccess.Write,FileShare.None)){old.CopyTo(recovery);recovery.Flush(true);}
            }
            if(SelfUpdateVerifier.Sha(original)!=update.OldSha256)throw new IOException("Original executable backup verification failed.");
            using(var output=new FileStream(incoming,FileMode.CreateNew,FileAccess.Write,FileShare.None)){incomingOwned=true;payload.Position=0;payload.CopyTo(output);output.Flush(true);}
            if(SelfUpdateVerifier.Sha(incoming)!=signedFile.Sha256)throw new IOException("Replacement copy verification failed.");
            fault?.Invoke("BeforeReplace");
            if(SelfUpdateVerifier.Sha(update.Target)!=update.OldSha256||File.Exists(backup))throw new IOException("Application changed or a recovery-name conflict appeared before replacement.");
            using(var journal=new FileStream(Path.Combine(update.Session,"operation.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                JsonSerializer.Serialize(journal,new{Schema=1,Phase="Prepared",Target=update.Target,OldSha256=update.OldSha256,NewSha256=signedFile.Sha256,Version=manifest.Version});journal.Flush(true);
            }
            File.Replace(incoming,update.Target,backup,false);replaced=true;
            // ReplaceFile preserves the displaced bytes; detect a last-moment external change.
            if(SelfUpdateVerifier.Sha(backup)!=update.OldSha256)throw new IOException("The displaced application changed during replacement; preserve and restore those bytes.");
            fault?.Invoke("AfterReplace");
            using(var installed=SelfUpdatePaths.ReadFile(update.Target)) {
                if(SelfUpdateVerifier.Sha(installed)!=signedFile.Sha256)throw new IOException("Installed update SHA-256 mismatch.");VerifyIdentity(update.Target,manifest.Version);
                fault?.Invoke("BeforeLaunch");launch(update.Target,update.Session);
            }
            // This means the OS accepted process creation, not healthy startup or GUI acceptance.
            var success=new SelfUpdateResult("Installed","Authenticated files replaced; restart process created. Application health is not verified.",true,false);Record(update.Session,success);return success;
        } catch(Exception ex) {
            if(!replaced&&File.Exists(original)) {
                // ReplaceFile may report an error after partially moving its operands.
                // Inspect actual bytes; never infer that an exception means "unchanged".
                if(!File.Exists(update.Target)) {
                    try {
                        using(var old=SelfUpdatePaths.ReadFile(original)) {
                            if(SelfUpdateVerifier.Sha(old)!=update.OldSha256)throw new IOException("Original recovery snapshot changed.");
                            using(var copy=new FileStream(rollback,FileMode.CreateNew,FileAccess.Write,FileShare.None)){rollbackOwned=true;old.CopyTo(copy);copy.Flush(true);}
                        }
                        File.Move(rollback,update.Target,false);restored=true;
                    }catch(Exception recovery){var incomplete=new SelfUpdateResult("Recovery required",ex.Message+"\n"+recovery.Message,false,false);Record(update.Session,incomplete);return incomplete;}
                } else {
                    var actual=SelfUpdateVerifier.Sha(update.Target);
                    if(actual==signedFile.Sha256&&File.Exists(backup))replaced=true;
                    else if(actual!=update.OldSha256){var conflict=new SelfUpdateResult("Recovery required",ex.Message+"\nInstalled file changed; automatic recovery is blocked.",false,false);Record(update.Session,conflict);return conflict;}
                }
            }
            if(replaced) {
                try {
                    if(!File.Exists(backup)||SelfUpdateVerifier.Sha(update.Target)!=signedFile.Sha256)throw new IOException("Installed file changed after replacement; automatic rollback would destroy external changes.");
                    // Never restore unauthenticated or changed recovery bytes. Retain the
                    // displaced file on any conflict instead of guessing which bytes to keep.
                    using(var recovery=SelfUpdatePaths.ReadFile(backup)) {
                        if(SelfUpdateVerifier.Sha(recovery)!=update.OldSha256)throw new IOException("Recovery executable changed; manual recovery is required.");
                        using(var copy=new FileStream(rollback,FileMode.CreateNew,FileAccess.Write,FileShare.None)){rollbackOwned=true;recovery.CopyTo(copy);copy.Flush(true);}
                    }
                    if(SelfUpdateVerifier.Sha(rollback)!=update.OldSha256||SelfUpdateVerifier.Sha(update.Target)!=signedFile.Sha256)throw new IOException("Recovery conflict before rollback.");
                    File.Replace(rollback,update.Target,null,false);restored=true;
                }catch(Exception recovery){var conflict=new SelfUpdateResult("Recovery required",ex.Message+"\n"+recovery.Message,false,false);Record(update.Session,conflict);return conflict;}
            }
            var failed=new SelfUpdateResult("Failed",ex.Message,false,restored);Record(update.Session,failed);return failed;
        }finally{if(incomingOwned)SelfUpdatePaths.DeleteIfHash(incoming,signedFile.Sha256);if(rollbackOwned)SelfUpdatePaths.DeleteIfHash(rollback,update.OldSha256);}
    }
    private static void Record(string session,SelfUpdateResult result) {
        try{var path=Path.Combine(session,"result.json");using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(stream,result);stream.Flush(true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

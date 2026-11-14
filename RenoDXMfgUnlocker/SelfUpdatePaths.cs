using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RenoDXMfgUnlocker;

internal static class SelfUpdatePaths
{
    private const uint Reparse=0x400,DirectoryAttribute=0x10;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [StructLayout(LayoutKind.Sequential)]private struct AttributeTag{public uint Attributes;public uint Tag;}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int type,out AttributeTag info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetFileInformationByHandle(SafeFileHandle file,int type,ref int delete,uint length);
    private static SafeFileHandle Open(string path,uint access,uint share,bool directory) {
        var file=CreateFileW(path,access,share,IntPtr.Zero,3,0x00200000u|(directory?0x02000000u:0),IntPtr.Zero);
        if(file.IsInvalid){file.Dispose();throw new IOException("Unable to lock the update path.",new Win32Exception(Marshal.GetLastWin32Error()));}
        if(!GetFileInformationByHandleEx(file,9,out var info,8)||(info.Attributes&Reparse)!=0||((info.Attributes&DirectoryAttribute)!=0)!=directory){file.Dispose();throw new IOException("Reparse points and unexpected update path types are forbidden.");}
        return file;
    }
    internal static FileStream ReadFile(string path)=>new(Open(Path.GetFullPath(path),0x80000000,1,false),FileAccess.Read);
    internal static void DeleteIfHash(string path,string expected) {
        try {
            using var stream=new FileStream(Open(path,0x80010000,1,false),FileAccess.Read);
            if(SelfUpdateVerifier.Sha(stream)!=expected)return;
            var delete=1;if(!SetFileInformationByHandle(stream.SafeFileHandle,4,ref delete,4))throw new IOException("Unable to remove the verified updater temporary file.");
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    private sealed class DirectoryLease(List<SafeFileHandle> handles):IDisposable {public void Dispose(){foreach(var h in handles.AsEnumerable().Reverse())h.Dispose();}}
    internal static IDisposable LockDirectory(string path) {
        var full=Path.GetFullPath(path);var root=Path.GetPathRoot(full)!;
        if(!root.Contains(':')||full.StartsWith("\\",StringComparison.Ordinal))throw new IOException("Only local Windows update paths are supported.");
        var handles=new List<SafeFileHandle>();
        try{var current=root;handles.Add(Open(current,0x81,3,true));foreach(var part in Path.GetRelativePath(root,full).Split(Path.DirectorySeparatorChar)){if(part==".")continue;current=Path.Combine(current,part);handles.Add(Open(current,0x81,3,true));}return new DirectoryLease(handles);}
        catch{foreach(var h in handles)h.Dispose();throw;}
    }
    internal static bool Within(string parent,string child)=>Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    internal static void ValidateTarget(string target,string session) {
        if(!Path.IsPathFullyQualified(target)||Path.GetFileName(target)!=SelfUpdateVerifier.Executable||!File.Exists(target)||Within(Path.GetDirectoryName(target)!,session)||Within(session,target))throw new IOException("Unsafe self-update target or staging directory.");
        var normalized=target.Replace('/','\\');
        if(normalized.Split('\\').Any(p=>p.Equals("steamapps",StringComparison.OrdinalIgnoreCase)||p.Equals("Transactions",StringComparison.OrdinalIgnoreCase)||p.Equals("Streamline",StringComparison.OrdinalIgnoreCase)||p.Equals("Intel",StringComparison.OrdinalIgnoreCase)||p.StartsWith("MFG_Backup",StringComparison.OrdinalIgnoreCase)))throw new IOException("Self-update cannot target game, SDK or recovery storage.");
        foreach(var blocked in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.System)})if(!string.IsNullOrEmpty(blocked)&&Within(blocked,target))throw new IOException("System directories cannot be updated.");
    }
    internal static string ProductionRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RenoDX MFG Unlocker","SelfUpdate");
    internal static string NewSession(string root) {
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(Path.GetFullPath(root))!,root);
        Directory.CreateDirectory(root);using var held=LockDirectory(root);
        var session=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(session);return session;
    }
    // Never follow arbitrary paths or recursively delete an untrusted directory tree.
    internal static void CleanupSession(string session,bool keepRecovery=false) {
        try{
            using(var held=LockDirectory(session))foreach(var name in new[]{"package.zip","payload.exe","worker.exe"}) {
                var file=Path.Combine(session,name);if(!File.Exists(file))continue;
                using(var check=ReadFile(file)){}File.Delete(file);
            }
            if(!keepRecovery&&!Directory.EnumerateFileSystemEntries(session).Any())Directory.Delete(session);
        }catch(IOException){/* A running helper, denied path or recovery file is retained, never force-deleted. */}
        catch(UnauthorizedAccessException){}
    }
    internal static void CleanupStaleSessions(string? storageRoot=null) {
        var root=Path.GetFullPath(storageRoot??ProductionRoot);if(!Directory.Exists(root))return;
        try {
            using var held=LockDirectory(root);
            foreach(var session in Directory.EnumerateDirectories(root).Take(32)) {
                if(!Guid.TryParseExact(Path.GetFileName(session),"N",out _)||Directory.GetLastWriteTimeUtc(session)>DateTime.UtcNow.AddDays(-1))continue;
                // Known temporary names only. Originals, journals, receipts and unrelated
                // files are retained; open/linked files fail closed in CleanupSession.
                CleanupSession(session,true);
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

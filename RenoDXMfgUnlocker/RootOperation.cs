using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RenoDXMfgUnlocker;

/// <summary>Non-waiting same-root transaction exclusion, including other instances in this Windows session.</summary>
internal sealed class RootOperation : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> Active = new(StringComparer.OrdinalIgnoreCase);
    private readonly string root;
    private readonly Mutex mutex;
    private RootOperation(string root, Mutex mutex) { this.root=root;this.mutex=mutex; }
    internal static RootOperation Enter(string root)
    {
        root=StreamlineStagingEvidenceService.Canonical(root);
        const string message="Another managed file operation is already using this game root. Wait for it to finish, then refresh Preview.";
        if(!Active.TryAdd(root,0))throw new IOException(message);
        Mutex? mutex=null;
        try {
            mutex=new Mutex(false,@"Local\RenoDX-GameRoot-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))));
            bool owned;try {owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
            if(!owned)throw new IOException(message);
            return new(root,mutex);
        } catch {mutex?.Dispose();Active.TryRemove(root,out _);throw;}
    }
    public void Dispose(){try{mutex.ReleaseMutex();}finally{mutex.Dispose();Active.TryRemove(root,out _);}}
}

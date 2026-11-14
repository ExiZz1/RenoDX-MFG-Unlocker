using System.IO;
namespace RenoDXMfgUnlocker;

/// <summary>Read inventories never grant write authority to other discovered locations.</summary>
internal static class RuntimeTargetPolicy
{
    internal static void Validate(string root,string executable,string target,bool allowIntelStorage)
    {
        root=StreamlineStagingEvidenceService.Canonical(root);executable=Path.GetFullPath(executable);target=StreamlineStagingEvidenceService.Canonical(target);
        if(!File.Exists(executable)||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!Directory.Exists(target)
            ||!StreamlineStagingEvidenceService.IsStrictChild(root,executable)||!StreamlineStagingEvidenceService.IsSameOrChild(root,target))
            throw new InvalidDataException("EXE/target must exist inside the selected game root.");
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(root)!,executable);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(root)!,target);
        var directory=StreamlineStagingEvidenceService.Canonical(Path.GetDirectoryName(executable)!);
        if(target.Equals(directory,StringComparison.OrdinalIgnoreCase))return;
        if(allowIntelStorage&&target.Equals(Path.Combine(directory,"_storage_"),StringComparison.OrdinalIgnoreCase)
            &&IntelRuntimeService.Assets.Take(2).All(a=>File.Exists(Path.Combine(target,a.Name))))return;
        throw new InvalidDataException("Only the explicitly confirmed EXE directory or a documented existing Intel _storage_ pair is supported. Other scanned copies are read-only.");
    }
}

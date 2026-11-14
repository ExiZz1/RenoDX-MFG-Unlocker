using System.IO;
using System.Text.RegularExpressions;
namespace RenoDXMfgUnlocker;
internal static class ExecutableCandidatePolicy
{
    internal static bool IsAuxiliary(string name)=>new[]{"crash","installer","setup","uninstall","anticheat","anti-cheat","bootstrap","updater","repair","redist","bugsplat","d3dconfig","gameservicelauncher","vconsole","redprelauncher","witcherscriptmerger","7za.exe","7z.exe","launcher","reporter"}.Any(t=>name.Contains(t,StringComparison.OrdinalIgnoreCase));
    internal static bool IsCopyDirectory(string path)=>Regex.IsMatch(path,@"(?:^|[\\/\s_–-])(backup\w*|backups|mfg_backup\w*|copy|copies|копія|копия|kopie|copie|copia)(?:$|[\\/\s_–-])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    internal static IReadOnlyList<string> Discover(string root,bool showAll=false)
    {
        var files=new List<string>();if(!Directory.Exists(root))return files;var pending=new Stack<string>();pending.Push(Path.GetFullPath(root));var visited=0;
        while(pending.Count>0&&visited++<50000){var directory=pending.Pop();try{
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
            foreach(var path in Directory.EnumerateFiles(directory,"*.exe"))if((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0&&(showAll||!IsAuxiliary(Path.GetFileName(path))))files.Add(Path.GetFullPath(path));
            foreach(var child in Directory.EnumerateDirectories(directory))if(showAll||!IsCopyDirectory(Path.GetRelativePath(root,child)))pending.Push(child);
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}}
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static int KnownScore(string executable,string appId)=>appId=="292030"&&Path.GetFileName(executable).Equals("witcher3.exe",StringComparison.OrdinalIgnoreCase)?160+(executable.Contains("x64_dx12",StringComparison.OrdinalIgnoreCase)?30:0):0;
}

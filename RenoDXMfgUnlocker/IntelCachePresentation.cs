using System.IO;
namespace RenoDXMfgUnlocker;
/// <summary>Presence is not validation. Current-session verification is supplied only by the verified runtime workflow.</summary>
internal static class IntelCachePresentation
{
    internal static string Status(string directory,bool verified,bool failed)=>failed?"Failed verification":verified?"Verified in current session":IntelRuntimeService.Assets.Any(a=>File.Exists(Path.Combine(directory,a.Name)))?"Cached, verification pending":"No local cache";
}

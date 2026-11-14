using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

/// <summary>Evidence text is data, never a command. Only explicit HTTP(S) links are actionable.</summary>
internal static class EvidenceLinkPolicy
{
    private static readonly Regex Links=new("(?<![\\p{L}\\p{N}_:/])https?://[^\\s<>\"']+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static bool TryGetHttpUri(string text,out Uri? uri)
    {
        uri=null;
        if(string.IsNullOrWhiteSpace(text)||text.Length>8192||text.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c))||text.Contains('\\')
            ||Regex.IsMatch(text,"%(?![0-9a-fA-F]{2})"))return false;
        return Uri.TryCreate(text,UriKind.Absolute,out uri)&&(uri.Scheme==Uri.UriSchemeHttps||uri.Scheme==Uri.UriSchemeHttp)
            &&!string.IsNullOrEmpty(uri.Host)&&string.IsNullOrEmpty(uri.UserInfo)&&Uri.CheckHostName(uri.Host)!=UriHostNameType.Unknown;
    }
    internal static IReadOnlyList<(int Start,string Url)> Extract(string text)
    {
        if(text.Length>262144)return [];
        return Links.Matches(text).Select(m=>(m.Index,m.Value.TrimEnd('.',',',';',')',']','}')))
            .Where(m=>TryGetHttpUri(m.Item2,out _)).Select(m=>(m.Index,m.Item2)).ToArray();
    }
}

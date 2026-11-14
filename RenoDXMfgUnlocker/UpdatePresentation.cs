using System.Net;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;
internal enum UpdateIndicatorState { Neutral,Checking,Available,Current,Unavailable }
internal sealed record UpdateIndicatorPresentation(bool Notify,string Tooltip)
{
    internal static UpdateIndicatorPresentation Describe(UpdateIndicatorState state,string? version,UiLocalization language)=>new(state==UpdateIndicatorState.Available,state switch {
        UpdateIndicatorState.Checking=>language.Lookup("Checking official GitHub releases…"),
        UpdateIndicatorState.Available=>language.Format("Application update available: {0}",version??""),
        UpdateIndicatorState.Current=>language.Lookup("No newer stable version is available."),
        UpdateIndicatorState.Unavailable=>language.Lookup("Update check unavailable. Click to retry."),
        _=>language.Lookup("Check for Updates")});
}
internal static class UpdateNotesSummary
{
    internal static string Readable(string? markdown) {
        if(string.IsNullOrEmpty(markdown))return "";
        var text=markdown.Length>24000?markdown[..24000]+"\n…":markdown;
        text=Regex.Replace(text,@"!\[([^\]]*)\]\([^\r\n)]*\)","$1");
        text=Regex.Replace(text,@"\[([^\]]+)\]\((https?://[^\r\n)]*)\)","$1 ($2)");
        text=Regex.Replace(text,@"(?m)^\s{0,3}#{1,6}\s+","");
        text=Regex.Replace(text,@"(?m)^\s*[-*+]\s+","• ");
        text=Regex.Replace(text,@"(?m)^\s*>\s?","");
        text=Regex.Replace(text,@"(?m)^\s*```[^\r\n]*","");
        text=Regex.Replace(text,@"\*\*([^*\r\n]+)\*\*","$1");
        text=Regex.Replace(text,@"`([^`\r\n]+)`","$1");
        text=Regex.Replace(text,@"<[^>\r\n]*>","");
        // Output is only assigned to a plain read-only TextBox, never HTML/XAML.
        return WebUtility.HtmlDecode(text).Trim();
    }
}

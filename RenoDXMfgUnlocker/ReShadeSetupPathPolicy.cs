using System.Runtime.InteropServices;
using System.Text;

namespace RenoDXMfgUnlocker;
/// <summary>The reviewed Setup PEInfo.MapAndLoad marshals filenames as LPStr, not UTF-16.</summary>
internal static class ReShadeSetupPathPolicy
{
    [DllImport("kernel32.dll")] private static extern uint GetACP();
    internal static int SystemCodePage=>checked((int)GetACP());
    internal const string BlockMessage="Installation blocked: this EXE path cannot be represented in the ANSI code page used by reviewed ReShade Setup. Its architecture detection is unreliable for this path. No game files were changed.";
    internal static bool CanInspect(string path)=>CanInspect(path,SystemCodePage);
    internal static bool CanInspect(string path,int codePage)
    {
        try{var encoding=codePage==65001?new UTF8Encoding(false,true):CodePagesEncodingProvider.Instance.GetEncoding(codePage,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);return encoding is not null&&encoding.GetString(encoding.GetBytes(path)).Equals(path,StringComparison.Ordinal);}
        catch(Exception ex)when(ex is EncoderFallbackException or DecoderFallbackException or ArgumentException or NotSupportedException){return false;}
    }
}

using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

/// <summary>Display only. Call with a structured version field, never a report, path or raw log.
/// Resource strings such as "310,9,16,0" are vendor text, not culture-formatted numbers.
/// Preserve component count/digits. Nonstandard vendor identifiers remain byte-for-byte text.</summary>
internal static class TechnicalVersionDisplay
{
    private static readonly Regex CommaVersion=new(@"\A[0-9]+(?:,[ \t]*[0-9]+){1,3}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
    internal static string Format(string? value)=>value is null?"":value.Length<=256&&CommaVersion.IsMatch(value)?string.Join(".",value.Split(',').Select(part=>part.TrimStart(' ','\t'))):value;
}

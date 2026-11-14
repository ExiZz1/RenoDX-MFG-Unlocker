namespace RenoDXMfgUnlocker;

/// <summary>Presentation adapter for the service's own structured evidence lines, not a raw-log rewriter.</summary>
internal static class RuntimeDiagnosticVersionDisplay
{
    internal static string Evidence(string text)=>string.Join("\n",text.Split('\n').Select(Line));
    private static string Line(string line)
    {
        if(line.StartsWith("Disk · ",StringComparison.Ordinal)||line.StartsWith("Mapped module reported · ",StringComparison.Ordinal)||line.StartsWith("Mapped version reported · ",StringComparison.Ordinal))
        {
            var fields=line.Split(" · ");
            if(fields.Length==3)return fields[0]+" · "+fields[1]+" · "+TechnicalVersionDisplay.Format(fields[2]);
        }
        if(line.StartsWith("EXE version ",StringComparison.Ordinal))
        {
            var fields=line[12..].Split(" · addon ");
            if(fields.Length==2)return "EXE version "+TechnicalVersionDisplay.Format(fields[0])+" · addon "+TechnicalVersionDisplay.Format(fields[1]);
        }
        return line;
    }
}

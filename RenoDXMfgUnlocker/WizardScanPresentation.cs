using System.IO;

namespace RenoDXMfgUnlocker;

/// <summary>EXE-relative inventory labels only. Discovery never grants installation permission.</summary>
internal static class WizardScanPresentation
{
    internal const string Selected="Selected EXE target directory";
    internal const string Other="Other discovered directories — diagnostic only";
    internal const string Copies="Backup/copy directories — diagnostic only";
    internal const string Missing="Not found in scan";
    internal static string Group(NvidiaComponentEntry entry,string? executable)
    {
        if(string.IsNullOrWhiteSpace(entry.Path))return Missing;
        if(entry.IsBackup||ExecutableCandidatePolicy.IsCopyDirectory(Path.GetDirectoryName(entry.Path)??entry.Path))return Copies;
        if(!string.IsNullOrWhiteSpace(executable)&&string.Equals(Path.GetDirectoryName(Path.GetFullPath(entry.Path)),Path.GetDirectoryName(Path.GetFullPath(executable)),StringComparison.OrdinalIgnoreCase))return Selected;
        return Other;
    }
    internal static string MissingRequirement(bool executable,bool integration,bool target,bool changed)=>!executable?"Select the current game EXE.":!integration?"Confirm the integration method and required inputs.":!target?(changed?"Review and confirm the target after changing the EXE, runtime or API.":"Confirm the target directory before continuing."):"";
}

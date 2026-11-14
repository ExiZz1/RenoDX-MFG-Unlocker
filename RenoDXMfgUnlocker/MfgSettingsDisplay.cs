using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

/// <summary>Only human-facing text is localized. Never translate serialized keys, choices, hashes or user values.</summary>
internal static class MfgSettingsDisplay
{
    internal static string Label(MfgUnlockSettingDefinition d,UiLocalization l)=>d.IsUpstreamSource
        ?d.CatalogEntry?.Translations?.GetValueOrDefault("en-US")?.Label??d.Label
        :d.CatalogEntry?.Translations?.GetValueOrDefault(l.CultureName)?.Label??l.Lookup(d.CatalogEntry?.Translations?.GetValueOrDefault("en-US")?.Label??d.Label);
    internal static string Description(MfgUnlockSettingDefinition d,UiLocalization l)=>d.IsUpstreamSource
        ?l.Lookup("Upstream source (original language; not owner-signed):")+" "+d.CatalogEntry!.Description
        :d.CatalogEntry?.Translations?.GetValueOrDefault(l.CultureName)?.Description??l.Lookup(d.CatalogEntry?.Translations?.GetValueOrDefault("en-US")?.Description??d.Description);
    internal static string Saved(MfgUnlockSettingState s,UiLocalization l)=>!s.IsExplicitlySet?l.Format("Not set · addon default {0}",s.EffectiveValue):!s.IsValid?l.Format("Invalid value · {0}",s.RawValue):s.RawValue??"";
    internal static string Constraints(MfgCatalogEntry e,UiLocalization l)
    {
        var values=e.Type is "Integer" or "Float"?(e.Minimum?.ToString(CultureInfo.InvariantCulture)??"?")+" … "+(e.Maximum?.ToString(CultureInfo.InvariantCulture)??"?")
            :string.Join(" / ",e.Type=="Boolean"?new[]{"0","1"}:e.Values??[]);
        return l.Format("Type: {0} · Values: {1} · Addon: {2}",l.Lookup(e.Type),values,"≥"+e.MinimumAddon);
    }
    internal static string Error(string source,UiLocalization l)
    {
        if(l.GetCatalog("en-US").ContainsKey(source))return l.Lookup(source);
        // Error arguments can contain exact config tokens/paths. Never recursively translate them.
        foreach(var template in new[]{"Unsupported MFG Unlock setting or value: {0}={1}.","{0} contains an invalid MFG Unlock setting. Reload and correct the configuration before saving; no values were changed.","{0} changed after the settings preview. Reload it and review the new values before saving.","Only {0} files inside the selected game's folder can be edited.","{0} is a reparse point; no change was made."}){
            var pattern="\\A"+Regex.Replace(Regex.Escape(template),@"\\\{(\d+)}",m=>"(?<p"+m.Groups[1].Value+">.+?)")+"\\z";
            var match=Regex.Match(source,pattern,RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if(match.Success){var count=Regex.Matches(template,@"\{\d+}").Count;return l.Format(template,Enumerable.Range(0,count).Select(i=>(object?)match.Groups["p"+i].Value).ToArray());}
        }
        return l.Lookup("Technical error details (original language):")+" "+source;
    }
}

internal sealed class MfgSettingChoiceDisplay(MfgUnlockSettingChoice choice,UiLocalization language):INotifyPropertyChanged
{
    public string Value=>choice.Value;
    // If label IS the configuration token (e.g. ready, buffer), retain it verbatim.
    public string Label=>choice.Label==choice.Value?choice.Label:language.Lookup(choice.Label);
    internal void RefreshLanguage()=>PropertyChanged?.Invoke(this,new(nameof(Label)));
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed record MfgSettingAvailability(bool CanEdit,string MessageKey="",MfgUnlockSettingDefinition? Required=null,string? Value=null)
{
    internal string Message(UiLocalization l)=>MessageKey.Length==0?"":Required is null?l.Lookup(MessageKey):l.Format(MessageKey,MfgSettingsDisplay.Label(Required,l),Value);
}

/// <summary>Explains existing service authority. It never sets prerequisites or modifies a configuration.</summary>
internal static class MfgSettingsAvailability
{
    internal static MfgSettingAvailability Evaluate(MfgUnlockSettingsService service,MfgUnlockSettingsSnapshot snapshot,MfgUnlockSettingDefinition definition,
        IReadOnlyDictionary<string,string> proposed,bool baseAllowed,bool currentGame,bool closed,bool busy,bool selectedOrSavedXmx)
    {
        if(busy)return new(false,"Unavailable: finish the current operation first.");
        if(!currentGame)return new(false,"Unavailable: select the game again and reload its settings.");
        if(!snapshot.IsSupportedAddonVersion||!service.SupportsVersion(snapshot.AddonVersion))return new(false,"Unavailable: the addon version is unknown or does not support these settings.");
        if(!closed)return new(false,"Unavailable: close the game and reload its settings.");
        if(snapshot.Settings.Any(s=>!s.IsValid))return new(false,"Unavailable: the configuration contains invalid values. Correct it and reload.");
        if(!baseAllowed)return new(false,"Unavailable: reload the confirmed addon and configuration before editing.");
        if(service.ConfigFileName=="ReShade.ini"&&MfgUnlockSettingsService.NvidiaGeneratorOnly(definition.Key)&&selectedOrSavedXmx)
            return new(false,"Unavailable: NVIDIA generator options do not apply to XMX. Save a different method and reload first.");
        if(definition.CatalogEntry is {} e){
            if(!MfgDynamicSettingsCatalog.Supports(e,snapshot.AddonVersion))return new(false,"Unavailable: this catalog definition does not support the installed addon version.");
            if(e.Type=="ReadOnly")return new(false,"Read-only: the catalog does not authorize editing this parameter.");
            foreach(var dependency in e.Requires??[]){
                var target=snapshot.Settings.FirstOrDefault(s=>s.Definition.Key.Equals(dependency.Key,StringComparison.OrdinalIgnoreCase));
                if(target is null||!target.IsValid)return new(false,"Unavailable: a required parameter is missing or invalid. Reload a compatible configuration.");
                var value=proposed.TryGetValue(target.Definition.Key,out var pending)?pending:target.EffectiveValue;
                if(value!=dependency.Value){
                    var boolean=target.Definition.Choices.Select(c=>c.Value).SequenceEqual(new[]{"0","1"});
                    return new(false,boolean&&dependency.Value=="1"?"Unavailable: enable {0} first.":"Unavailable: set {0} to {1} first.",target.Definition,dependency.Value);
                }
            }
        }
        return service.CanEdit(definition,snapshot.Settings,proposed)?new(true):new(false,"Unavailable: the catalog prerequisites are not satisfied.");
    }
}

/// <summary>Explicit bindings for static MFG-page text, including elements created after a language switch.</summary>
public sealed class MfgSettingsStrings(UiLocalization language):INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string,string> Keys=new Dictionary<string,string>{
        ["Game"]="Select a game executable on Home.",["ReShadeNotLoaded"]="ReShade.ini · not loaded",["XeNotLoaded"]="MFGAdaUnlock.ini · not loaded",["NotLoaded"]="Settings are not loaded.",
        ["Eyebrow"]="ADDON CONFIGURATION",["Title"]="MFG Unlock Settings",["Intro"]="Documented settings from ReShade.ini and MFGAdaUnlock.ini.",
        ["Import"]="Import signed RenoDX catalog",["Check"]="Check signed RenoDX catalog",["CheckUpstream"]="Analyze official addon source",["UpstreamDetails"]="Official addon source — discoveries",["Cancel"]="Cancel",["ConfigDetails"]="Configuration details",
        ["UpstreamTitle"]="Official addon source",["UpstreamHint"]="Find new settings in Mateus Jorge’s source code. Analysis and approval are always manual.",["SignedTitle"]="Signed RenoDX catalog (fallback)",["SignedHint"]="Owner-reviewed definitions for settings the source analyzer cannot safely recognize.",
        ["General"]="General",["Advanced"]="Advanced",["Other"]="Other",["Unknown"]="Unknown settings — read-only",
        ["UnknownDescription"]="Unknown keys are preserved. Their type, defaults and compatibility are not inferred.",
        ["Saved"]="SAVED VALUE",["New"]="NEW VALUE",["Restart"]="Changes apply after restarting the game.",["Details"]="Details",
        ["ReadOnly"]="Read-only. See the configuration status for the reason.",["Invalid"]="Invalid value. Use the catalog type and range.",
        ["Activity"]="Runtime activity is separate from saved settings.",["Diagnostics"]="Runtime Diagnostics",["Reload"]="Reload",
        ["Discard"]="Discard changes",["Save"]="Save settings",["SaveTooltip"]="Only settings for the confirmed installed addon version can be changed."
    };
    public string this[string key]=>language.Lookup(Keys[key]);
    internal static IReadOnlyDictionary<string,string> ResourceKeys=>Keys;
    internal void RefreshLanguage()=>PropertyChanged?.Invoke(this,new("Item[]"));
    public event PropertyChangedEventHandler? PropertyChanged;
}

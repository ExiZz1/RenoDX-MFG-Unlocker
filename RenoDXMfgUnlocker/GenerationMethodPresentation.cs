namespace RenoDXMfgUnlocker;

internal sealed record GenerationMethodPresentation(string SelectedMethod,string SavedMethod,bool Pending,bool DefaultsInUse,string Description,string IniValues,bool SelectedXmx=false,bool SavedXmx=false,bool DynamicXmxSupported=false)
{
    internal const string SelectedXmxWarning="With selected XMX, NVIDIA Dynamic MFG and NVIDIA generator quality controls are unavailable. Their saved values are preserved; independent options are not automatically changed.";
    internal const string SavedXmxWarning="XMX is still the saved method. Save the new method and restart the game to change configuration. XMX limitations apply to the saved choice; active runtime state is not verified.";
    internal const string DynamicXmxWarning="With addon 1.8.3 or newer, Dynamic MFG with XMX is experimental and requires the documented Streamline / DLSS-G stack. NVIDIA generator quality controls still do not apply to XMX. Saved configuration does not prove runtime activity.";
    internal string? ContextWarning=>SelectedXmx?(DynamicXmxSupported?DynamicXmxWarning:SelectedXmxWarning):SavedXmx?SavedXmxWarning:null;
}
internal static class GenerationMethodPresenter
{
    internal static GenerationMethodPresentation? Describe(MfgUnlockSettingsSnapshot snapshot,IReadOnlyDictionary<string,string?> proposed)
    {
        if(!snapshot.IsSupportedAddonVersion||!MfgUnlockSettingsService.HasGenerationMethods(snapshot.AddonVersion))return null;
        var keys=snapshot.Settings.Where(s=>MfgSettingsLayout.IsMethodKey(s.Definition.Key)).ToArray();
        if(keys.Length!=2||keys.Any(s=>!s.IsValid))return null;
        var current=keys.Select(s=>s with{EffectiveValue=proposed.GetValueOrDefault(s.Definition.Key)??""}).ToArray();
        if(current.Any(s=>!s.Definition.Choices.Any(c=>c.Value==s.EffectiveValue)))return null;
        string Method(MfgUnlockSettingState[] values){var backend=values.Single(s=>s.Definition.Key=="Selected");return MfgUnlockSettingsService.XmxSelected(values)?values.Single(s=>s.Definition.Key=="XmxGenerator").Definition.Label:backend.Definition.Choices.Single(c=>c.Value==backend.EffectiveValue).Label;}
        var xmx=MfgUnlockSettingsService.XmxSelected(current);
        var description=xmx?current.Single(s=>s.Definition.Key=="XmxGenerator").Definition.Description:current.Single(s=>s.Definition.Key=="Selected").EffectiveValue=="XeMFG"?
            "Uses Intel XeMFG with its own output path. Requires a reviewed addon, supported game inputs and the pinned XeSS-FG / XeLL pair. Vulkan is a separate experimental path.":
            "Uses NVIDIA DLSS-G for generation and presentation. Requires a supported existing DLSS Frame Generation integration and a compatible NVIDIA runtime. Copying DLLs cannot create game integration.";
        var ini=string.Join("\n",current.Select(s=>"["+s.Definition.Section+"] "+s.Definition.Key+": "+keys.Single(k=>k.Definition.Key==s.Definition.Key).EffectiveValue+" → "+s.EffectiveValue));
        return new(Method(current),Method(keys),current.Any(s=>keys.Single(k=>k.Definition.Key==s.Definition.Key).EffectiveValue!=s.EffectiveValue),keys.All(s=>!s.IsExplicitlySet),description,ini,xmx,MfgUnlockSettingsService.XmxSelected(keys),MfgSettingsCapabilityCatalog.Supports(snapshot.AddonVersion,MfgSettingsCapabilityCatalog.Feature.DynamicMfgXmx));
    }
}

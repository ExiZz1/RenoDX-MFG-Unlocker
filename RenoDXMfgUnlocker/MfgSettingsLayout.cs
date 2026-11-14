namespace RenoDXMfgUnlocker;

/// <summary>Display placement only. Schema, editable values and write authority remain in the existing catalog/service.</summary>
internal enum MfgSettingsCategory { General, Advanced, Other }
internal static class MfgSettingsLayout
{
    internal static MfgSettingsCategory CategoryFor(MfgUnlockSettingDefinition definition)=>definition.CatalogEntry is {} entry&&Enum.TryParse<MfgSettingsCategory>(entry.Category,out var category)?category:CategoryFor(definition.Key);
    internal static bool IsMethodKey(string key)=>key is "Selected" or "XmxGenerator";
    internal static MfgSettingsCategory CategoryFor(string key)=>key switch {
        "Enabled" or "Selected" or "XmxGenerator" or "UIStabilization"=>MfgSettingsCategory.General,
        "TemporalFix" or "BlackwellFrameworkKernels" or "ExperimentalAdaptiveQuality" or "Rtx50QualityFixes" or "RuntimeSelectionMode" or "Vulkan" or "VulkanPresentMetering"=>MfgSettingsCategory.Advanced,
        _=>MfgSettingsCategory.Other
    };
}

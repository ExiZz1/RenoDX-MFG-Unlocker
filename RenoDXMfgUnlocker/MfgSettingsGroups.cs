using System.ComponentModel;

namespace RenoDXMfgUnlocker;

internal enum MfgSettingsGroup { Addon, Nvidia, Intel, Vulkan, Compatibility, Diagnostics, Experimental, Additional }

/// <summary>Presentation state only. Group visibility never owns setting values or write authority.</summary>
internal sealed class MfgSettingsGroupState(MfgSettingsGroup id, UiLocalization language, MfgSettingsCategory category = MfgSettingsCategory.General) : INotifyPropertyChanged
{
    public MfgSettingsGroup Id { get; } = id;
    public string DisplayName => (searchMode ? language.Lookup(category.ToString()) + " · " : "") + language.Lookup(Id switch {
        MfgSettingsGroup.Addon => "MFG Unlock",
        MfgSettingsGroup.Nvidia => "NVIDIA / DLSS-G",
        MfgSettingsGroup.Intel => "Intel XeFG / XeMFG",
        MfgSettingsGroup.Vulkan => "Vulkan",
        MfgSettingsGroup.Compatibility => "Compatibility",
        MfgSettingsGroup.Diagnostics => "Diagnostics",
        MfgSettingsGroup.Experimental => "Experimental",
        _ => "Additional settings"
    });
    private bool expanded = id is not (MfgSettingsGroup.Diagnostics or MfgSettingsGroup.Experimental or MfgSettingsGroup.Compatibility or MfgSettingsGroup.Additional);
    private bool searchMode, searchExpanded = true;
    public bool IsExpanded
    {
        get => searchMode ? searchExpanded : expanded;
        set
        {
            if (IsExpanded == value) return;
            if (searchMode) searchExpanded = value; else expanded = value;
            PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
        }
    }
    internal void SetSearchMode(bool value)
    {
        if (searchMode == value) return;
        searchMode = value;
        if (value) searchExpanded = true;
        PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
    }
    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class MfgSettingsGroupStore(UiLocalization language)
{
    private readonly Dictionary<(MfgSettingsCategory Category, MfgSettingsGroup Group), MfgSettingsGroupState> groups = [];
    private bool searchMode;
    internal MfgSettingsGroupState For(string file, MfgUnlockSettingDefinition definition)
    {
        var id = (MfgSettingsLayout.CategoryFor(definition), GroupFor(file, definition));
        if (!groups.TryGetValue(id, out var state))
        {
            groups[id] = state = new(id.Item2, language, id.Item1);
            state.SetSearchMode(searchMode);
        }
        return state;
    }
    internal void RefreshLanguage() { foreach (var group in groups.Values) group.RefreshLanguage(); }
    internal void SetSearchMode(bool value)
    {
        searchMode = value;
        foreach (var group in groups.Values) group.SetSearchMode(value);
    }
    internal static MfgSettingsGroup GroupFor(string file, MfgUnlockSettingDefinition d)
    {
        var section = d.Section ?? (file == "ReShade.ini" ? "RenoDX.MFGUnlock" : "XeFG");
        if (section == "Diagnostics") return MfgSettingsGroup.Diagnostics;
        if (section == "Compatibility" || d.Key is "InvertedDepth" or "JitteredMotion") return MfgSettingsGroup.Compatibility;
        if (d.Key.StartsWith("Vulkan", StringComparison.Ordinal)) return MfgSettingsGroup.Vulkan;
        if (d.Key is "UIStabilization" or "XeFGStock2xModel") return MfgSettingsGroup.Experimental;
        if (d.Key == "Enabled") return MfgSettingsGroup.Addon;
        if (file == "ReShade.ini" || section == "DLSSG") return MfgSettingsGroup.Nvidia;
        if (section is "XeFG" or "Adapters") return MfgSettingsGroup.Intel;
        return MfgSettingsGroup.Additional;
    }
    internal static string WarningFor(MfgUnlockSettingDefinition d) => d.CatalogEntry?.Section == "Diagnostics"
        ? "Diagnostic option. Enable only for troubleshooting; diagnostic work can add overhead."
        : d.Key.StartsWith("Vulkan", StringComparison.Ordinal) || d.Key.StartsWith("XeFGFlipQueue", StringComparison.Ordinal) || d.Key is "XeFGNativeBuffers" or "XeFGStock2xModel" or "UIStabilization"
        ? "Experimental option. Use a supported integration and retain the original value for recovery."
        : d.Key is "InvertedDepth" or "JitteredMotion" or "RetainNativeOutput"
        ? "Compatibility override. Change only for a specific issue; incorrect assumptions can affect rendering or overlays."
        : "";
}

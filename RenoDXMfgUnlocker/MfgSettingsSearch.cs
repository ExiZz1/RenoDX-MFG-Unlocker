using System.Globalization;

namespace RenoDXMfgUnlocker;

internal enum MfgSettingFilter { All, Upstream, Pending, Editable, ReadOnly, Favorites }

internal static class MfgSettingsSearch
{
    internal const string Prompt = "Search MFG settings";
    internal static string Label(MfgSettingFilter filter) => filter switch
    {
        MfgSettingFilter.Upstream => "Upstream settings",
        MfgSettingFilter.Pending => "Unsaved changes",
        MfgSettingFilter.Editable => "Editable settings",
        MfgSettingFilter.ReadOnly => "Unavailable and read-only",
        MfgSettingFilter.Favorites => "Favorite settings",
        _ => "All settings"
    };

    internal static bool Matches(MfgSettingFilter filter, string query, UiLocalization language,
        MfgUnlockSettingDefinition definition, bool canEdit, bool pending, bool favorite = false, string? file = null)
    {
        if (!Includes(filter, definition.IsUpstreamSource, canEdit, pending, favorite)) return false;
        file ??= definition.CatalogEntry?.File;
        return Contains(query, language, definition.Key, definition.Section ?? (file == "MFGAdaUnlock.ini" ? "XeFG" : MfgUnlockSettingsService.SectionName), file,
            definition.Label, definition.Description, definition.CatalogEntry?.Description,
            definition.CatalogEntry?.Translations?.GetValueOrDefault("en-US")?.Label,
            MfgSettingsDisplay.Label(definition, language), MfgSettingsDisplay.Description(definition, language));
    }

    internal static bool Includes(MfgSettingFilter filter, bool upstream, bool canEdit, bool pending, bool favorite = false) => filter switch
    {
        MfgSettingFilter.Upstream => upstream,
        MfgSettingFilter.Pending => pending,
        MfgSettingFilter.Editable => canEdit,
        MfgSettingFilter.ReadOnly => !canEdit,
        MfgSettingFilter.Favorites => favorite,
        _ => true
    };

    internal static bool Contains(string query, UiLocalization language, params string?[] fields) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(term =>
            fields.Any(field => field is not null &&
                language.Culture.CompareInfo.IndexOf(field, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
}

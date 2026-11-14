using System.Windows;
using System.Windows.Controls;

namespace RenoDXMfgUnlocker;

// Presentation only: use the existing version parser, never an independently stored channel.
internal static class ApplicationReleaseStatus
{
    internal static string? Label(string? version)
    {
        var parsed=ApplicationVersion.Parse(version);
        if(parsed is null)return "Unknown";
        if(parsed.Prerelease.Length==0)return null;
        return parsed.Prerelease[0].ToLowerInvariant() switch {
            "alpha"=>"ALPHA", "beta"=>"BETA", "rc"=>"RC", "test"=>"TEST", _=>"PREVIEW"
        };
    }

    internal static void Apply(Border badge,TextBlock text,string? version)
    {
        var label=Label(version);
        text.Text=label??"";
        badge.Visibility=label is null?Visibility.Collapsed:Visibility.Visible;
    }
}

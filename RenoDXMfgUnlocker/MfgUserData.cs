using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXMfgUnlocker;

internal sealed record MfgProfileValue(string File, string Section, string Key, string Value)
{
    internal string Identity => File + "|" + Section + "|" + Key;
    internal string Caption => File + " · [" + Section + "] " + Key;
}

internal sealed record MfgUserProfile(int Schema, string Name, MfgProfileValue[] Values);
internal sealed record MfgHistoryChange(string Setting, string Before, string After);
internal sealed record MfgHistoryEntry(string Game, string Target, string Transaction, DateTimeOffset SavedUtc, MfgHistoryChange[] Changes);

internal static class MfgUserData
{
    private const int MaxBytes = 512 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 12,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    internal static T Read<T>(string path)
    {
        CheckPath(path);
        using var file = File.OpenRead(path);
        if (file.Length is < 1 or > MaxBytes) throw new InvalidDataException("Invalid MFG settings data.");
        using var reader = new BinaryReader(file);
        var bytes = reader.ReadBytes(MaxBytes + 1);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Invalid MFG settings data.");
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Invalid MFG settings data.");
    }

    internal static void Write<T>(string path, T data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Invalid MFG settings data.");
        CheckPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            CheckPath(path);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static MfgUserProfile Validate(MfgUserProfile profile)
    {
        if (profile is null || profile.Schema != 1 || !IsProfileNameValid(profile.Name) || profile.Values is not { Length: > 0 and <= 256 } ||
            profile.Values.Any(v => v is null || v.File is not ("ReShade.ini" or "MFGAdaUnlock.ini") ||
                !Identifier(v.Section) || !Identifier(v.Key) || v.Value is not { Length: > 0 and <= 128 } || v.Value.Any(char.IsControl)) ||
            profile.Values.Select(v => v.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.Values.Length)
            throw new InvalidDataException("Invalid MFG settings data.");
        return profile;
    }

    internal static bool IsProfileNameValid(string? name) => name is { Length: > 0 and <= 80 } &&
        !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);

    internal static bool MatchesHistoryFiles(string root, string executable, IReadOnlyList<BackupRestoreFilePreview> files)
    {
        var directory = Path.GetDirectoryName(executable)!;
        var known = new[] { "ReShade.ini", "MFGAdaUnlock.ini" }.Select(name =>
            Path.GetRelativePath(root, Path.Combine(directory, name)).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return files.Count > 0 && files.All(file => known.Contains(file.RelativeTargetPath));
    }

    private static bool Identifier(string? value) => value is { Length: > 0 and <= 80 } &&
        Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant);

    private static void CheckPath(string path)
    {
        path = Path.GetFullPath(path);
        StreamlineStagingEvidenceService.EnsureNoReparsePoints(Path.GetPathRoot(path)!, path);
    }
}

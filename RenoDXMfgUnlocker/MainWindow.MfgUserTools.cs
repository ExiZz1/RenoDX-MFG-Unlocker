using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RenoDXMfgUnlocker;

public partial class MainWindow
{
    private static readonly string MfgUserDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "SettingsTools");
    private static string UserDataPath(string name) => Path.Combine(MfgUserDirectory, name + ".json");
    private HashSet<string>? mfgFavorites;

    private IEnumerable<(MfgSettingsDocument Document, MfgUnlockSettingRow Row, MfgProfileValue Display)> ProfileRows() =>
        mfgSettingsDocuments.SelectMany(d => d.Rows.Select(r => (d, r, new MfgProfileValue(
            d.Service.ConfigFileName, d.Service.SectionFor(r.Definition), r.Definition.Key, r.ProposedValue ?? ""))));

    private string? ProfileIdentity(MfgUnlockSettingRow row) =>
        ProfileRows().FirstOrDefault(item => ReferenceEquals(item.Row, row)).Display?.Identity;

    private bool IsMfgFavorite(MfgUnlockSettingRow row) => ProfileIdentity(row) is {} id && Favorites().Contains(id);

    private HashSet<string> Favorites() => mfgFavorites ??= new(
        ReadUserData<string>("favorites").Where(x => x is { Length: > 0 and <= 200 }), StringComparer.OrdinalIgnoreCase);

    private static T[] ReadUserData<T>(string name)
    {
        try { return MfgUserData.Read<T[]>(UserDataPath(name)); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not read MFG user data: {0}", name);
            return [];
        }
    }

    private bool CanStageSettings() => !AnyFileOperationBusy && mfgSettingsExePath is not null &&
        mfgSettingsExePath == selectedExe && HasCurrentExecutable() && !IsSelectedGameProcessRunning(mfgSettingsExePath, out _);

    private static bool IsProfileValueValid(MfgUnlockSettingDefinition definition, string value) =>
        definition.CatalogEntry is {} entry
            ? entry.Type != "ReadOnly" && MfgDynamicSettingsCatalog.Canonical(entry, value) == value
            : definition.Choices.Any(choice => choice.Value == value);

    private void MfgRowMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MfgUnlockSettingRow row, ContextMenu: {} menu }) return;
        var language = LocalizedPresentation.Language;
        if (menu.Items[0] is MenuItem favorite) favorite.Header = language.Lookup(IsMfgFavorite(row) ? "Remove from favorites" : "Add to favorites");
        if (menu.Items[1] is MenuItem reset)
        {
            reset.Header = language.Lookup("Use documented default");
            reset.IsEnabled = CanStageSettings() && row.CanEdit &&
                IsProfileValueValid(row.Definition, row.Definition.DefaultValue) && row.ProposedValue != row.Definition.DefaultValue;
        }
    }

    private static MfgUnlockSettingRow? MenuRow(object sender) =>
        sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu
            { PlacementTarget: FrameworkElement { DataContext: MfgUnlockSettingRow row } } ? row : null;

    private void MfgFavorite_Click(object sender, RoutedEventArgs e)
    {
        var row = sender is Button { DataContext: MfgUnlockSettingRow clicked } ? clicked : MenuRow(sender);
        if (row is null || ProfileIdentity(row) is not {} id) return;
        var next = new HashSet<string>(Favorites(), StringComparer.OrdinalIgnoreCase);
        if (!next.Remove(id)) next.Add(id);
        try
        {
            if (next.Count > 1024) throw new InvalidDataException();
            MfgUserData.Write(UserDataPath("favorites"), next.Order(StringComparer.Ordinal).ToArray());
            mfgFavorites = next;
            row.IsFavorite = next.Contains(id);
            QueueMfgSearchRefresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { ShowThemeDialog("MFG favorites", "Could not save MFG settings data.", true); }
    }

    private void MfgDefault_Click(object sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is {} row) StageMfgValues(new Dictionary<MfgUnlockSettingRow,string> { [row] = row.Definition.DefaultValue });
    }

    private void StageMfgValues(IReadOnlyDictionary<MfgUnlockSettingRow,string> changes)
    {
        if (!CanStageSettings() || changes.Any(c => !c.Key.CanEdit || !IsProfileValueValid(c.Key.Definition,c.Value))) return;
        foreach (var (row, value) in changes) row.ProposedValue = value;
        QueueMfgEditUpdate();
    }

    private void MfgProfiles_Click(object sender, RoutedEventArgs e)
    {
        var language = LocalizedPresentation.Language;
        List<MfgUserProfile> profiles;
        try { profiles = MfgUserData.Read<MfgUserProfile[]>(UserDataPath("profiles")).Select(MfgUserData.Validate).ToList(); }
        catch (FileNotFoundException) { profiles = []; }
        catch (DirectoryNotFoundException) { profiles = []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { ShowThemeDialog("MFG profiles", "Could not read the profile library.", true); return; }
        var body = new StackPanel();
        body.Children.Add(ToolText("Profiles preview values for the selected game. Use Save settings to write them."));
        var name = new TextBox { MaxLength = 80, Margin = new Thickness(0, 4, 0, 8),
            ToolTip = language.Lookup("Profile name") };
        body.Children.Add(ToolText("Profile name"));
        body.Children.Add(name);
        var nameStatus = ToolText("");
        LocalizedPresentation.SetPreserveOriginal(nameStatus, true);
        body.Children.Add(nameStatus);
        var list = new ListBox { DisplayMemberPath = nameof(MfgUserProfile.Name), MinHeight = 65, MaxHeight = 170 };
        body.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(actions);
        var window = CreateInfoDialog("MFG profiles", body, 680, 510);
        void Refresh(MfgUserProfile? current = null)
        {
            list.ItemsSource = profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            list.SelectedItem = current;
        }
        void Store(MfgUserProfile profile)
        {
            MfgUserData.Validate(profile);
            var next = profiles.Where(p => !string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)).Append(profile).ToArray();
            if (next.Length > 64) throw new InvalidDataException();
            MfgUserData.Write(UserDataPath("profiles"), next);
            profiles = next.ToList();
            Refresh(profile);
        }
        Button Add(string label, Action click)
        {
            var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 6) };
            actions.Children.Add(button);
            button.Click += (_, _) =>
            {
                try { click(); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
                { ShowThemeDialog("MFG profiles", "The profile data is invalid or exceeds the allowed size.", true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { ShowThemeDialog("MFG profiles", "Could not write the profile.", true); }
            };
            return button;
        }
        var save = Add("Save local profile", () =>
        {
            var values = ProfileRows().Where(x => x.Row.CanEdit && IsProfileValueValid(x.Row.Definition, x.Display.Value))
                .Select(x => x.Display).ToArray();
            if (!MfgUserData.IsProfileNameValid(name.Text.Trim())) return;
            if (values.Length == 0)
            { ShowThemeDialog("MFG profiles", "No editable settings are available to save in a profile.", true); return; }
            var profile = MfgUserData.Validate(new(1, name.Text.Trim(), values));
            if (profiles.Any(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)) &&
                !ShowThemeConfirmation("Replace local profile?", profile.Name, window)) return;
            Store(profile);
        });
        var preview = Add("Preview profile", () =>
        {
            if (list.SelectedItem is MfgUserProfile profile) PreviewMfgProfile(profile, window);
        });
        Add("Import profile", () =>
        {
            var picker = new OpenFileDialog { Filter = "MFG profile (*.json)|*.json", CheckFileExists = true };
            if (ShowNativeDialog(picker, window) != true) return;
            MfgUserProfile imported;
            try { imported = MfgUserData.Validate(MfgUserData.Read<MfgUserProfile>(picker.FileName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { ShowThemeDialog("MFG profiles", "Could not read the selected profile.", true); return; }
            if (profiles.Any(p => string.Equals(p.Name, imported.Name, StringComparison.OrdinalIgnoreCase)) &&
                !ShowThemeConfirmation("Replace local profile?", imported.Name, window)) return;
            Store(imported);
            PreviewMfgProfile(imported, window);
        });
        var export = Add("Export profile", () =>
        {
            if (list.SelectedItem is not MfgUserProfile profile) return;
            var picker = new SaveFileDialog { Filter = "MFG profile (*.json)|*.json", FileName = "RenoDX-MFG-Profile.json",
                DefaultExt = ".json", OverwritePrompt = true };
            if (ShowNativeDialog(picker, window) != true) return;
            if (!Path.GetExtension(picker.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                selectedGameRoot is {} root && StreamlineStagingEvidenceService.IsStrictChild(root, picker.FileName))
                throw new InvalidDataException();
            MfgUserData.Write(picker.FileName, MfgUserData.Validate(profile));
        });
        var delete = Add("Delete local profile", () =>
        {
            if (list.SelectedItem is not MfgUserProfile profile || !ShowThemeConfirmation("Delete local profile?", profile.Name, window)) return;
            var next = profiles.Where(p => !ReferenceEquals(p,profile)).ToArray();
            MfgUserData.Write(UserDataPath("profiles"), next);
            profiles = next.ToList();
            Refresh();
        });
        void SelectionChanged()
        {
            preview.IsEnabled = export.IsEnabled = delete.IsEnabled = list.SelectedItem is MfgUserProfile;
            if (list.SelectedItem is MfgUserProfile profile) name.Text = profile.Name;
        }
        list.SelectionChanged += (_, _) => SelectionChanged();
        void UpdateName()
        {
            save.IsEnabled = MfgUserData.IsProfileNameValid(name.Text.Trim());
            nameStatus.Text = save.IsEnabled ? "" : language.Lookup("Enter a profile name of 1 to 80 characters.");
            nameStatus.Visibility = save.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        }
        name.TextChanged += (_, _) => UpdateName();
        Refresh();
        SelectionChanged();
        UpdateName();
        LocalizedPresentation.RefreshGeneratedItems(body);
        window.ShowDialog();
    }

    private void PreviewMfgProfile(MfgUserProfile profile, Window owner)
    {
        MfgUserData.Validate(profile);
        UpdateMfgSettingsButtons();
        var current = ProfileRows().ToDictionary(i => i.Display.Identity, StringComparer.OrdinalIgnoreCase);
        var eligible = CanStageSettings();
        var staged = new Dictionary<MfgUnlockSettingRow,string>();
        var lines = new List<string>();
        var language = LocalizedPresentation.Language;
        foreach (var value in profile.Values)
        {
            if (!eligible || !current.TryGetValue(value.Identity, out var entry) || !entry.Row.CanEdit ||
                !IsProfileValueValid(entry.Row.Definition, value.Value))
            {
                lines.Add(value.Caption + " · " + language.Lookup("Skipped: unavailable or incompatible"));
                continue;
            }
            if (entry.Row.ProposedValue == value.Value) continue;
            staged.Add(entry.Row, value.Value);
            lines.Add(value.Caption + " · " + entry.Row.ProposedValue + " → " + value.Value);
        }
        if (staged.Count == 0)
        {
            ShowComponentDetails("Preview profile", lines.Count == 0 ? language.Lookup("No profile changes for this game.") : string.Join("\n", lines), owner);
            return;
        }
        var appliedMethod = xeMfgSettingRows.FirstOrDefault(r => r.Definition.Key == "Selected");
        var appliedXmx = xeMfgSettingRows.FirstOrDefault(r => r.Definition.Key == "XmxGenerator");
        string? Value(MfgUnlockSettingRow? row) => row is null ? null : staged.TryGetValue(row, out var value) ? value : row.ProposedValue;
        bool affectsMethod = staged.Keys.Any(r => MfgSettingsLayout.IsMethodKey(r.Definition.Key));
        bool pendingXmx = Value(appliedMethod) == "DLSSG" && Value(appliedXmx) == "1";
        if ((affectsMethod && Value(appliedMethod) != "DLSSG" && Value(appliedXmx) == "1") ||
            (pendingXmx && mfgSettingRows.Any(r => MfgUnlockSettingsService.NvidiaGeneratorOnly(r.Definition.Key) &&
                Value(r) != r.State?.EffectiveValue)))
        {
            ShowComponentDetails("Preview profile", language.Lookup("Profile changes conflict with the selected generation method.") + "\n\n" + string.Join("\n",lines), owner);
            return;
        }
        try
        {
            foreach (var document in mfgSettingsDocuments.Where(d => d.Rows.Any(staged.ContainsKey)))
            {
                var candidate = document.Rows.Where(r => Value(r) is not null && Value(r) != r.State?.EffectiveValue)
                    .ToDictionary(r => r.Definition.Key, r => Value(r)!, StringComparer.OrdinalIgnoreCase);
                document.Service.CreateUpdatedBytes(document.Snapshot, candidate);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            ShowComponentDetails("Preview profile", language.Lookup("Profile validation failed. Reload the game settings and review the values.") + "\n\n" + string.Join("\n",lines), owner);
            return;
        }
        if (!ShowThemeConfirmation("Stage profile changes?", string.Join("\n",lines) + "\n\n" +
                language.Lookup("Profiles preview values for the selected game. Use Save settings to write them."), owner) ||
            !CanStageSettings() || staged.Keys.Any(r => !r.CanEdit)) return;
        StageMfgValues(staged);
    }

    private static string HistoryTarget(string exe) =>
        MfgUnlockSettingsService.GetSha256(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(exe).ToUpperInvariant()));

    private bool RecordMfgHistory(string exe, string game, string transaction,
        (MfgSettingsDocument Doc, Dictionary<string, string> Changes)[] edits)
    {
        var changes = edits.SelectMany(e => e.Changes.Select(c =>
        {
            var state = e.Doc.Snapshot.Settings.Single(s => s.Definition.Key == c.Key);
            var label = new MfgProfileValue(e.Doc.Service.ConfigFileName, e.Doc.Service.SectionFor(state.Definition), c.Key, c.Value).Caption;
            return new MfgHistoryChange(label, state.EffectiveValue, c.Value);
        })).ToArray();
        try
        {
            var history = ReadUserData<MfgHistoryEntry>("history");
            MfgUserData.Write(UserDataPath("history"), new[] { new MfgHistoryEntry(game, HistoryTarget(exe), transaction,
                DateTimeOffset.UtcNow, changes) }.Concat(history.Where(h=>h is not null)).Take(25).ToArray());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }

    private async void MfgHistory_Click(object sender, RoutedEventArgs e)
    {
        if (AnyFileOperationBusy || selectedExe is not {} exe || selectedGameRoot is not {} root) return;
        var history = ReadUserData<MfgHistoryEntry>("history").Where(h => h is not null && h.Target == HistoryTarget(exe) &&
            h.Changes is not null && Guid.TryParseExact(h.Transaction,"N",out _)).ToArray();
        var language = LocalizedPresentation.Language;
        var body = new StackPanel();
        body.Children.Add(ToolText(language.Lookup("This restores whole configuration files from the selected save, including settings outside the history list.")));
        if (history.Length == 0) body.Children.Add(ToolText("No saved MFG changes for this executable."));
        var window = CreateInfoDialog("MFG settings history", body, 720, 560);
        foreach (var entry in history)
        {
            var heading = entry.SavedUtc.ToLocalTime().ToString("g",language.Culture) + " · " + entry.Game;
            var details = new StackPanel();
            var rows = ToolText(string.Join("\n", entry.Changes.Where(c=>c is not null).Select(c=>$"{c.Setting} · {c.Before} → {c.After}")));
            LocalizedPresentation.SetPreserveOriginal(rows,true);
            details.Children.Add(rows);
            var restore = new Button { Content = language.Lookup("Preview transaction recovery"), HorizontalAlignment = HorizontalAlignment.Left };
            restore.Click += async (_, _) =>
            {
                if (AnyFileOperationBusy || selectedExe != exe || selectedGameRoot != root) return;
                restore.IsEnabled = false;
                try { await ShowRestoreManagerAsync(root, CreateBackupService(root), window, entry.Transaction); }
                finally { restore.IsEnabled = true; }
            };
            details.Children.Add(restore);
            var item = new Expander { Header = heading, Content = details, Margin = new Thickness(0, 0, 0, 8) };
            LocalizedPresentation.SetPreserveOriginal(item,true);
            body.Children.Add(item);
        }
        LocalizedPresentation.RefreshGeneratedItems(body);
        window.ShowDialog();
        if (selectedExe == exe && !AnyFileOperationBusy) await LoadMfgSettingsAsync();
    }
}

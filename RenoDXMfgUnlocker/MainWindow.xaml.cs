using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace RenoDXMfgUnlocker;

public partial class MainWindow : Window
{
    private const string Repo = "mavismmg/MFGAdaUnlock-RenoDx";
    private static readonly HttpClient Http = CreateHttp();
    private readonly ObservableCollection<Game> games = [];
    private readonly ObservableCollection<Release> allReleases = [];
    private readonly ObservableCollection<Release> visibleReleases = [];
    private bool logExpanded;
    private string? selectedExe;
    private string? selectedGameRoot;
    private List<string> executableCandidates = [];

    public MainWindow()
    {
        InitializeComponent();
        var appVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        AppVersionText.Text = $"v{appVersion}";
        AboutVersionText.Text = $"v{appVersion}";
        GamesCombo.ItemsSource = games;
        GamesCombo.DisplayMemberPath = "Name";
        ReleasesList.ItemsSource = visibleReleases;
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RenoDX-MFG-Unlocker", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ScanSteamGames();
        await LoadReleasesAsync();
    }

    private void ScanSteamGames()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var path = Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam");
            if (Directory.Exists(path)) roots.Add(path);
        }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf)) { var p = Path.Combine(pf, "Steam"); if (Directory.Exists(p)) roots.Add(p); }
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steam && Directory.Exists(steam)) roots.Add(steam.Replace('/', '\\'));
        } catch { }
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        // Older libraryfolders.vdf files store additional libraries under numeric keys.
        foreach (var root in roots)
        {
            var vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) continue;
            var vdfText = File.ReadAllText(vdfPath);
            foreach (Match m in Regex.Matches(vdfText, @"""[0-9]+""\s*""([^""]+)"""))
                libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    var name = Regex.Match(text, "\\\"name\\\"\\s*\\\"([^\\\"]+)\\\"").Groups[1].Value;
                    var folder = Regex.Match(text, "\\\"installdir\\\"\\s*\\\"([^\\\"]+)\\\"").Groups[1].Value;
                    if (name.Length > 0 && folder.Length > 0) games.Add(new Game(name, Path.Combine(apps, "common", folder)));
                } catch { }
            }
        }
        var distinct = games.GroupBy(g => g.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(g => g.Name).ToArray();
        games.Clear(); foreach (var game in distinct) games.Add(game);
        GameStatusText.Text = games.Count == 0 ? "No Steam libraries found · browse to an EXE" : $"Found {games.Count} Steam game{(games.Count == 1 ? "" : "s")}";
        GameStatusDot.Fill = games.Count == 0 ? new SolidColorBrush(Color.FromRgb(100, 97, 111)) : new SolidColorBrush(Color.FromRgb(167, 139, 250));
        Log($"Steam scan complete: {games.Count} installed games found.");
    }

    private async Task LoadReleasesAsync()
    {
        SetBusy(true, "Loading published releases…");
        try
        {
            var found = new List<Release>();
            for (var page = 1; ; page++)
            {
                using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases?per_page=100&page={page}");
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var items = json.RootElement.EnumerateArray().ToArray();
                foreach (var item in items)
                {
                    if (item.GetProperty("draft").GetBoolean()) continue;
                    var assets = item.GetProperty("assets").EnumerateArray()
                        .Where(a => string.Equals(a.GetProperty("name").GetString(), "renodx-mfgunlock.addon64", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (assets.Length == 0) continue;
                    var tag = item.GetProperty("tag_name").GetString() ?? "release";
                    var title = item.GetProperty("name").GetString();
                    var date = item.GetProperty("published_at").GetDateTimeOffset();
                    var asset = assets.OrderBy(a => a.GetProperty("name").GetString()?.Contains("api18", StringComparison.OrdinalIgnoreCase) == true ? 0 : 1).First();
                    found.Add(new Release(title is { Length: > 0 } ? title : tag, tag, $"Published {date:dd MMM yyyy}  ·  {asset.GetProperty("name").GetString()}", asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("name").GetString()!));
                }
                if (items.Length < 100) break;
            }
            allReleases.Clear(); foreach (var release in found) allReleases.Add(release);
            FilterReleases();
            ReleaseSubhead.Text = $"Published addon builds from {Repo}";
            SetStatus($"Loaded {allReleases.Count} published releases");
            Log($"Loaded {allReleases.Count} published release assets from GitHub.");
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't load releases · check your connection and refresh");
            Log("Release loading failed: " + ex.Message);
            ShowThemeDialog("Release loading failed", "Could not load releases from GitHub. Check your connection and try Refresh.\n\n" + ex.Message, isError: true);
        }
        finally { SetBusy(false, null); }
    }

    private void FilterReleases()
    {
        visibleReleases.Clear();
        var query = SearchBox.Text?.Trim() ?? "";
        foreach (var release in allReleases.Where(r => query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Tag.Contains(query, StringComparison.OrdinalIgnoreCase))) visibleReleases.Add(release);
        ReleaseCount.Text = $"{visibleReleases.Count} BUILDS";
    }

    private void GamesCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GamesCombo is null || ExePathBox is null) return;
        selectedGameRoot = null;
        ResetExeSelection();
        if (GamesCombo.SelectedItem is not Game game) return;
        selectedGameRoot = Path.GetFullPath(game.Path);
        SetStatus($"Searching {game.Name} for executables…");
        executableCandidates = FindExecutables(selectedGameRoot);
        if (executableCandidates.Count == 1) { SetExe(executableCandidates[0], showMismatch: false); return; }
        if (executableCandidates.Count > 1)
        {
            var ranked = executableCandidates.Select(p => (Path: p, Score: ScoreExecutable(p, game))).OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (ranked[0].Score >= 100 && (ranked.Length == 1 || ranked[0].Score > ranked[1].Score)) { SetExe(ranked[0].Path, showMismatch: false); return; }
            SetStatus($"{executableCandidates.Count} executables found · choose the game's main EXE");
            var chosen = ChooseExecutable(game.Name, executableCandidates);
            if (chosen is not null) SetExe(chosen, showMismatch: false);
            else SetStatus($"Selected {game.Name} · choose an executable to continue");
            return;
        }
        SetStatus($"Selected {game.Name} · browse to its executable");
        Log($"No executable found under {selectedGameRoot}; choose it manually.");
    }

    private void ResetExeSelection()
    {
        selectedExe = null;
                executableCandidates.Clear();
ExePathBox.Text = "";
        InstallLocationText.Text = "Select an executable to preview the destination";
        UpdateInstallButtonAvailability();
    }

    private bool IsExeForCurrentGame(string path)
    {
        if (selectedGameRoot is null) return GamesCombo.SelectedItem is not Game;
        try
        {
            var root = Path.GetFullPath(selectedGameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private bool HasCurrentExecutable() => selectedExe is not null && File.Exists(selectedExe) && IsExeForCurrentGame(selectedExe);

    private void UpdateInstallButtonAvailability()
    {
        if (InstallButton is not null && ReleasesList is not null)
            InstallButton.IsEnabled = ReleasesList.SelectedItem is not null && HasCurrentExecutable();
    }
    private static List<string> FindExecutables(string folder)
    {
        var found = new List<string>();
        if (!Directory.Exists(folder)) return found;
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(folder));
        var excluded = new[] { "crash", "installer", "setup", "uninstall", "anti-cheat", "anticheat", "gameservicelauncher", "bootstrap", "updater", "repair", "redist", "vcredist", "dxsetup", "bugsplat", "bssndrpt", "crs-", "vconsole", "legacy" };
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
                    if (!excluded.Any(t => Path.GetFileNameWithoutExtension(file).Contains(t, StringComparison.OrdinalIgnoreCase))) found.Add(Path.GetFullPath(file));
                foreach (var child in Directory.EnumerateDirectories(dir))
                    try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); } catch { }
            }
            catch { }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int ScoreExecutable(string path, Game game)
    {
        var gameName = Regex.Replace(game.Name, "[^A-Za-z0-9]", "").ToLowerInvariant();
        var exeName = Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9]", "").ToLowerInvariant();
        var score = exeName == gameName ? 140 : gameName.Length > 3 && exeName.Contains(gameName) ? 110 : exeName.Length >= 8 && gameName.StartsWith(exeName, StringComparison.Ordinal) ? 105 : 0;
        var relative = Path.GetRelativePath(game.Path, path).Replace('\\', '/');
        if (relative.Contains("/Binaries/Win64/", StringComparison.OrdinalIgnoreCase) || relative.Contains("Win64", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (relative.Count(c => c == '/') <= 1) score += 5;
        return score;
    }

    private string? ChooseExecutable(string gameName, IReadOnlyList<string> candidates)
    {
        var dialog = new Window { Title = "Choose game executable", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, Width = 620, Height = 440, ShowInTaskbar = false, Icon = Icon };
        var shell = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 27)), BorderBrush = new SolidColorBrush(Color.FromRgb(57, 52, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(20) };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "Choose the main game executable", Foreground = new SolidColorBrush(Color.FromRgb(244, 242, 250)), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var subtitle = new TextBlock { Text = $"{gameName} · {candidates.Count} candidates found", Foreground = new SolidColorBrush(Color.FromRgb(158, 154, 170)), FontSize = 12, Margin = new Thickness(0, 0, 0, 14) }; Grid.SetRow(subtitle, 1); grid.Children.Add(subtitle);
        var list = new ListBox { ItemsSource = candidates, Background = new SolidColorBrush(Color.FromRgb(15, 17, 22)), Foreground = new SolidColorBrush(Color.FromRgb(229, 226, 238)), BorderBrush = new SolidColorBrush(Color.FromRgb(52, 54, 66)), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding()); factory.SetValue(TextBlock.MarginProperty, new Thickness(8, 7, 8, 7)); factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        list.ItemTemplate = new DataTemplate { VisualTree = factory }; list.SelectedIndex = -1; Grid.SetRow(list, 2); grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 8, 0) };
        var choose = new Button { Content = "Use selected EXE", MinWidth = 130, Padding = new Thickness(16, 9, 16, 9), Background = new SolidColorBrush(Color.FromRgb(112, 75, 208)), BorderBrush = new SolidColorBrush(Color.FromRgb(142, 111, 240)), IsEnabled = false };
        string? result = null; list.SelectionChanged += (_, _) => choose.IsEnabled = list.SelectedItem is not null; cancel.Click += (_, _) => dialog.DialogResult = false; choose.Click += (_, _) => { if (list.SelectedItem is string value) { result = value; dialog.DialogResult = true; } };
        buttons.Children.Add(cancel); buttons.Children.Add(choose); Grid.SetRow(buttons, 3); grid.Children.Add(buttons); shell.Child = grid; dialog.Content = shell;
        list.MouseDoubleClick += (_, _) => choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return dialog.ShowDialog() == true ? result : null;
    }
    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select the game's executable", Filter = "Windows executable (*.exe)|*.exe", CheckFileExists = true };
        if (selectedExe is not null && Directory.Exists(Path.GetDirectoryName(selectedExe))) dialog.InitialDirectory = Path.GetDirectoryName(selectedExe);
        else if (selectedGameRoot is not null && Directory.Exists(selectedGameRoot)) dialog.InitialDirectory = selectedGameRoot;
        if (dialog.ShowDialog(this) == true) SetExe(dialog.FileName);
    }

    private void SetExe(string path, bool showMismatch = true)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (showMismatch) ShowThemeDialog("Invalid executable", "Choose an existing Windows .exe file.", isError: true);
            return;
        }
        var fullPath = Path.GetFullPath(path);
        if (!IsExeForCurrentGame(fullPath))
        {
            if (showMismatch) ShowThemeDialog("Executable does not match", "The selected executable is outside the currently selected game's folder. Choose an executable from that game.", isError: true);
            Log("Rejected an executable outside the currently selected game's folder.");
            return;
        }
        selectedExe = fullPath;
        ExePathBox.Text = fullPath;
        InstallLocationText.Text = Path.Combine(Path.GetDirectoryName(fullPath)!, "renodx-mfgunlock.addon64");
        SetStatus("Game executable selected · ready to install");
        Log("Target executable: " + fullPath);
        UpdateInstallButtonAvailability();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = HasCurrentExecutable() ? Path.GetDirectoryName(selectedExe!) : selectedGameRoot;
        if (folder is null || !Directory.Exists(folder)) { SetStatus("Select a game or executable first"); return; }
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void Home_Click(object sender, RoutedEventArgs e) => SetPage(showAbout: false);
    private void About_Click(object sender, RoutedEventArgs e) => SetPage(showAbout: true);

    private void SetPage(bool showAbout)
    {
        HomePage.Visibility = showAbout ? Visibility.Collapsed : Visibility.Visible;
        AboutPage.Visibility = showAbout ? Visibility.Visible : Visibility.Collapsed;
        HomeNavButton.Background = new SolidColorBrush(showAbout ? Color.FromRgb(23, 25, 32) : Color.FromRgb(41, 35, 58));
        HomeNavButton.BorderBrush = new SolidColorBrush(showAbout ? Color.FromRgb(52, 54, 66) : Color.FromRgb(84, 68, 114));
        AboutNavButton.Background = new SolidColorBrush(showAbout ? Color.FromRgb(41, 35, 58) : Color.FromRgb(23, 25, 32));
        AboutNavButton.BorderBrush = new SolidColorBrush(showAbout ? Color.FromRgb(84, 68, 114) : Color.FromRgb(52, 54, 66));
    }

    private void DeveloperProfile_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/ExiZz1");
    private void OriginalAuthor_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/mavismmg");
    private void SourceRepository_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/mavismmg/MFGAdaUnlock-RenoDx");

    private void OpenExternalLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowThemeDialog("Couldn't open link", "The link could not be opened in your browser. You can copy this address and open it manually:\\n\\n" + url + "\\n\\n" + ex.Message, isError: true); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadReleasesAsync();
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (visibleReleases is not null) FilterReleases(); }
    private void ReleasesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (InstallButton is null) return;
        var release = ReleasesList.SelectedItem as Release;
        SelectedReleaseText.Text = release is null ? "Select a release to continue" : $"{release.Tag}  ·  {release.AssetName}";
        UpdateInstallButtonAvailability();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (ReleasesList.SelectedItem is not Release release) return;
        if (!HasCurrentExecutable())
        {
            ShowThemeDialog("Choose the current game's EXE", "Select an executable from the currently selected game before installing the addon.", isError: true);
            return;
        }
        var targetExe = selectedExe!;
        var target = Path.Combine(Path.GetDirectoryName(targetExe)!, "renodx-mfgunlock.addon64");
        var temp = target + ".download";
        InstallButton.IsEnabled = false;
        try
        {
            SetStatus("Downloading and validating addon…"); Log($"Downloading {release.Tag}…");
            using var response = await Http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)) await input.CopyToAsync(output);
            var info = new FileInfo(temp);
            if (info.Length < 4096 || info.Length > 100 * 1024 * 1024) throw new InvalidDataException("The downloaded addon has an unexpected file size.");
            await using (var fs = File.OpenRead(temp))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') throw new InvalidDataException("The downloaded file is not a valid Windows addon binary.");
            }
            if (!File.Exists(targetExe) || !string.Equals(selectedExe, targetExe, StringComparison.OrdinalIgnoreCase) || !IsExeForCurrentGame(targetExe))
                throw new InvalidOperationException("The game selection changed during download. The active addon was not replaced; select the game and try again.");
            // Only replace the active addon after the staged download passes validation. No backup is created.
            File.Move(temp, target, true);
            SetStatus($"Installed {release.Tag} successfully"); Log($"Installed and validated {release.AssetName} next to the game executable. No backup was created.");
            ShowThemeDialog("Installation complete", $"{release.Name} ({release.Tag}) was installed next to the selected game's executable.\n\nRestart the game to load the addon.");
        }
        catch (Exception ex)
        {
            SetStatus("Installation failed · active addon was left untouched"); Log("Install failed: " + ex.Message);
            ShowThemeDialog("Installation failed", "The addon could not be installed. The active addon was left untouched.\n\n" + ex.Message, isError: true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } UpdateInstallButtonAvailability(); }
    }

    private void ShowThemeDialog(string title, string message, bool isError = false)
    {
        var accent = isError ? Color.FromRgb(201, 120, 148) : Color.FromRgb(167, 139, 250);
        var dialog = new Window
        {
            Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 460,
            ShowInTaskbar = false, Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/RenoDX.ico", UriKind.Absolute))
        };
        var shell = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 27)), BorderBrush = new SolidColorBrush(Color.FromRgb(57, 52, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22) };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 17) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new Image { Width = 34, Height = 34, Source = new BitmapImage(new Uri("pack://application:,,,/Assets/RenoDX.png", UriKind.Absolute)), Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) };
        var heading = new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(244, 242, 250)), FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = "×", Width = 32, Height = 30, Padding = new Thickness(0), FontSize = 17, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(160, 157, 171)), VerticalAlignment = VerticalAlignment.Center };
        close.Click += (_, _) => dialog.Close();
        Grid.SetColumn(heading, 1); Grid.SetColumn(close, 2);
        header.Children.Add(logo); header.Children.Add(heading); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(196, 193, 207)), FontSize = 13, LineHeight = 21, Margin = new Thickness(1, 0, 1, 22) };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new Border { Width = 30, Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var ok = new Button { Content = "Got it", MinWidth = 96, Padding = new Thickness(18, 9, 18, 9), Background = new SolidColorBrush(isError ? Color.FromRgb(78, 46, 63) : Color.FromRgb(112, 75, 208)), BorderBrush = new SolidColorBrush(accent) };
        ok.Click += (_, _) => dialog.Close(); Grid.SetColumn(ok, 1);
        footer.Children.Add(mark); footer.Children.Add(ok); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        shell.Child = layout; dialog.Content = shell;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape || e.Key == Key.Enter) dialog.Close(); };
        dialog.ShowDialog();
    }
    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        logExpanded = !logExpanded; LogPanel.Visibility = logExpanded ? Visibility.Visible : Visibility.Collapsed; LogToggle.Content = logExpanded ? "−  Activity" : "＋  Activity";
    }
    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Text = "";
    private void Log(string message) => LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    private void SetStatus(string message) => StatusText.Text = message;
    private void SetBusy(bool busy, string? message) { if (message is not null) SetStatus(message); Mouse.OverrideCursor = busy ? Cursors.Wait : null; }
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record Game(string Name, string Path);
    private sealed record Release(string Name, string Tag, string Meta, string Url, string AssetName);
}

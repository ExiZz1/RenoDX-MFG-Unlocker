using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace RenoDXMfgUnlocker;

public partial class MainWindow : Window
{
    private readonly ApplicationPreferences applicationPreferences = new();
    private const string Repo = "mavismmg/MFGAdaUnlock-RenoDx";
    private static readonly HttpClient Http = CreateHttp();
    private readonly ObservableCollection<Game> games = [];
    private readonly ObservableCollection<Release> allReleases = [];
    private readonly ObservableCollection<Release> visibleReleases = [];
    private readonly Dictionary<string, string> releaseNotesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly NvidiaComponentScanner nvidiaComponentScanner = new();
    private readonly GameCompatibilityEvaluator gameCompatibilityEvaluator = new(new BuiltInGameCompatibilityProfileCatalog());
    private readonly StreamlineReleaseApiClient streamlineApi = new(Http);
    private readonly StreamlineDownloadService streamlineDownloads = new();
    private readonly StreamlineLocalArchiveValidator streamlineLocalArchiveValidator;
    private readonly StreamlineArchiveStager streamlineStager = new();
    private readonly StreamlineStagingEvidenceService streamlineEvidenceService = new();
    private VerifiedStreamlineStage? verifiedStreamlineStage;
    private StreamlineComponentPlan? currentComponentPlan;
    private readonly HashSet<string> selectedComponentFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<StreamlineAssetRow> streamlineAssets = [];
    private CancellationTokenSource? streamlineOperationCancellation;
    private CancellationTokenSource? installPreviewCancellation;
    private CancellationTokenSource? installOperationCancellation;
    private string? selectedStreamlineDownloadPath;
    private string? selectedStreamlineTag;
    private StreamlineSdkAsset? selectedStreamlineAsset;
    private bool streamlineListLoaded;
    private bool streamlineBusy;
    private bool streamlineShowingPackages;
    private int streamlineSelectionGeneration;
    private string? selectedStreamlineStagingPath;
    private string? explicitComponentTargetDirectory;
    private NvidiaComponentScanResult? currentNvidiaScanResult;
    private readonly System.Text.StringBuilder activityLogSource = new();
    private bool logExpanded;
    private string? selectedExe;
    private string? selectedGameRoot;
    private List<string> executableCandidates = [];
    private readonly MfgUnlockSettingsService mfgUnlockSettings = new(catalog:MfgDynamicSettingsCatalog.BuiltIn);
    private readonly ObservableCollection<MfgUnlockSettingRow> mfgSettingRows = [];
    private MfgUnlockSettingsSnapshot? mfgSettingsSnapshot;
    private readonly MfgUnlockSettingsService xeMfgSettings = new(true,MfgDynamicSettingsCatalog.BuiltIn);
    private readonly ObservableCollection<MfgUnlockSettingRow> xeMfgSettingRows = [];
    private readonly List<MfgSettingsDocument> mfgSettingsDocuments = [];
    private sealed record MfgSettingsDocument(MfgUnlockSettingsService Service,MfgUnlockSettingsSnapshot Snapshot,ObservableCollection<MfgUnlockSettingRow> Rows);
    private bool AnyFileOperationBusy=>reshadeBusy||reshadeSession.BlocksChanges||wizardOperationBusy||mfgSettingsBusy||addonInstallBusy||installOperationCancellation is not null;
    private string? mfgSettingsExePath;
    private string? mfgSettingsAddonHash;
    private VerifiedDlssGProvider? selectedDlssGProvider;
    private bool mfgSettingsBusy;
    private bool addonInstallBusy;

    public MainWindow()
    {
        InitializeComponent();
        DesktopWindowLayout.Attach(this);
        var workArea=SystemParameters.WorkArea;
        MinWidth=Math.Min(MinWidth,Math.Max(480,workArea.Width-24));MinHeight=Math.Min(MinHeight,Math.Max(420,workArea.Height-24));
        var preferred=DesktopWindowLayout.PreferredSize(workArea.Size);Width=preferred.Width;Height=preferred.Height;
        PopulateAboutFeatures();
        InitializeIntelSdkManager();
        InitializeApplicationUpdates();
        InitializeUpstreamReleaseNotice();
        StateChanged+=(_,_)=>{MaximizeRestoreButton.Content=WindowState==WindowState.Maximized?"❐":"□";MaximizeRestoreButton.ToolTip=WindowState==WindowState.Maximized?"Restore window":"Maximize";};
        streamlineLocalArchiveValidator = new StreamlineLocalArchiveValidator(streamlineDownloads.DownloadsDirectory);
        var appVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.4.0";
        AppVersionText.Text = $"v{TechnicalVersionDisplay.Format(appVersion)}";
        AboutVersionText.Text = $"v{TechnicalVersionDisplay.Format(appVersion)}";
        ApplicationReleaseStatus.Apply(HomeReleaseBadge,HomeReleaseStatusText,appVersion);
        GamesCombo.ItemsSource = games;
        GamesCombo.DisplayMemberPath = "Name";
        ReleasesList.ItemsSource = visibleReleases;
        StreamlineAssetsList.ItemsSource = streamlineAssets;
        MfgSettingsItemsControl.ItemsSource = mfgSettingRows;
        XeMfgSettingsItemsControl.ItemsSource = xeMfgSettingRows;
        InitializeMfgCategories();
        InitializeReShade();
        Closing+=(_,e)=>{
            if(AnyFileOperationBusy||intelSdkBusy||streamlineBusy){
                e.Cancel=true;
                intelSdkCancellation?.Cancel();
                catalogCheckCancellation?.Cancel();
                reshadeDownload?.Cancel();
                if(reshadeSession.BlocksChanges) SetReShadeSetupStatus(reshadeSession.CanConfirm
                    ? "The initial installer process exited. Close any remaining setup windows, then confirm and rescan; exit alone is not installation proof."
                    : "Close the official installer normally; do not terminate it during writes.");
            }
        };
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
        if(applicationPreferences.CheckUpdatesAtLaunch) _ = CheckApplicationUpdatesAtLaunchAsync();
        _ = CheckUpstreamReleaseNoticeAsync();
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
                    var appId = Regex.Match(Path.GetFileNameWithoutExtension(manifest), @"^appmanifest_(\d+)$", RegexOptions.IgnoreCase).Groups[1].Value;
                    var buildId = Regex.Match(text, "\\\"buildid\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (folder.Length > 0 && appId.Length > 0) games.Add(new Game(GameExecutableSelection.DisplayName(appId,name), Path.Combine(apps, "common", folder), appId, buildId.Length > 0 ? buildId : null));
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
                    var notes = item.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String ? bodyElement.GetString() : null;
                    var release = new Release(title is { Length: > 0 } ? title : tag, tag, $"Published {date:yyyy-MM-dd}  ·  {asset.GetProperty("name").GetString()}", asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("name").GetString()!, date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture), notes, item.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null, item.TryGetProperty("url", out var apiUrl) ? apiUrl.GetString() : null);
                    found.Add(release);
                    if (notes is not null) releaseNotesCache[tag] = notes;
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
        LocalizedPresentation.RefreshGeneratedItems(ReleasesList);
    }

    private void GamesCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GamesCombo is null || ExePathBox is null) return;
        InvalidateInstallPreview();
        installOperationCancellation?.Cancel();
        explicitComponentTargetDirectory = null;
        selectedComponentFiles.Clear();
        currentComponentPlan = null;
        if (InstallPreviewTargetText is not null) InstallPreviewTargetText.Text = "Not selected · choose a folder inside the selected game's root";
        selectedGameRoot = null;
        ResetExeSelection();
        if (GamesCombo.SelectedItem is not Game game) { UpdateNvidiaComponentContext(); return; }
        selectedGameRoot = Path.GetFullPath(game.Path);
        UpdateNvidiaComponentContext();
        SetStatus($"Searching {game.Name} for executables…");
        executableCandidates = FindExecutables(selectedGameRoot);
        var automatic = GameExecutableSelection.Automatic(selectedGameRoot,game.AppId,executableCandidates,applicationPreferences.ConfirmedExe(selectedGameRoot));
        if (automatic is not null) { SetExe(automatic, showMismatch: false); return; }
        if (executableCandidates.Count > 1)
        {
            var ranked = executableCandidates.Select(p => (Path: p, Score: ScoreExecutable(p, game))).OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            SetStatus($"{executableCandidates.Count} executables found · choose the game's main EXE");
            var chosen = ChooseExecutable(game.Name, ranked.Select(c=>c.Path).ToArray());
            if (chosen is not null) { SetExe(chosen, showMismatch: false); RememberConfirmedExecutable(); }
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
        UpdateNvidiaComponentContext();
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
    private static List<string> FindExecutables(string folder) => ExecutableCandidatePolicy.Discover(folder).ToList();

    private static int ScoreExecutable(string path, Game game)
    {
        var gameName = Regex.Replace(game.Name, "[^A-Za-z0-9]", "").ToLowerInvariant();
        var exeName = Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9]", "").ToLowerInvariant();
        var score = exeName == gameName ? 140 : gameName.Length > 3 && exeName.Contains(gameName) ? 110 : exeName.Length >= 8 && gameName.StartsWith(exeName, StringComparison.Ordinal) ? 105 : 0;
        var relative = Path.GetRelativePath(game.Path, path).Replace('\\', '/');
        if (relative.Contains("/Binaries/Win64/", StringComparison.OrdinalIgnoreCase) || relative.Contains("Win64", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (relative.Count(c => c == '/') <= 1) score += 5;
        return Math.Max(score,ExecutableCandidatePolicy.KnownScore(path,game.AppId));
    }

    private string? ChooseExecutable(string gameName, IReadOnlyList<string> candidates)
    {
        var size=ExecutableSelectionView.DialogSize(DesktopWindowLayout.WorkArea(this).Size);
        var dialog=new Window{Title="Choose game executable",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ResizeMode=ResizeMode.NoResize,Width=size.Width,Height=size.Height,ShowInTaskbar=false,Icon=Icon};
        var root=selectedGameRoot;
        var view=new ExecutableSelectionView(gameName,candidates,()=>root is null?candidates:ExecutableCandidatePolicy.Discover(root,true));
        view.Confirmed+=_=>dialog.DialogResult=true;view.Cancelled+=()=>dialog.DialogResult=false;
        dialog.Content=new Border{Child=view,Background=SetupWizardView.Brush("#13151B"),BorderBrush=SetupWizardView.Brush("#393448"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=new Thickness(20)};
        dialog.Loaded+=(_,_)=>view.Candidates.Focus();
        return dialog.ShowDialog()==true?view.SelectedPath:null;
    }
    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select the game's executable", Filter = "Windows executable (*.exe)|*.exe", CheckFileExists = true };
        if (selectedExe is not null && Directory.Exists(Path.GetDirectoryName(selectedExe))) dialog.InitialDirectory = Path.GetDirectoryName(selectedExe);
        else if (selectedGameRoot is not null && Directory.Exists(selectedGameRoot)) dialog.InitialDirectory = selectedGameRoot;
        if (ShowNativeDialog(dialog,this) == true)
        {
            if(selectedGameRoot is null || !IsPathWithinRoot(selectedGameRoot,dialog.FileName))
            {
                if(!ShowThemeConfirmation("Use manual game folder?", "Use this executable's directory as the allowed game root?\n" + dialog.FileName + "\nYou can only write inside that root; no Steam compatibility is implied.")) return;
                var game = new Game("Manual · " + Path.GetFileNameWithoutExtension(dialog.FileName), Path.GetDirectoryName(dialog.FileName)!, "manual");
                games.Add(game); GamesCombo.SelectedItem=game;
            }
            SetExe(dialog.FileName);
            RememberConfirmedExecutable();
        }
    }

    private void SetExe(string path, bool showMismatch = true)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (showMismatch) ShowThemeDialog("Invalid executable", "Choose an existing Windows .exe file.", isError: true);
            return;
        }
        installOperationCancellation?.Cancel();
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
        UpdateNvidiaComponentContext();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = HasCurrentExecutable() ? Path.GetDirectoryName(selectedExe!) : selectedGameRoot;
        if (folder is null || !Directory.Exists(folder)) { SetStatus("Select a game or executable first"); return; }
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void Home_Click(object sender, RoutedEventArgs e) => SetPage(AppPage.Home);
    private void About_Click(object sender, RoutedEventArgs e) => SetPage(AppPage.About);
    private void NvidiaComponents_Click(object sender, RoutedEventArgs e) => SetPage(AppPage.NvidiaComponents);
    private async void MfgSettings_Click(object sender, RoutedEventArgs e)
    {
        if(AnyFileOperationBusy)return;
        SetPage(AppPage.MfgSettings);
        await LoadMfgSettingsAsync();
    }

    private void SetPage(AppPage page)
    {
        if(AnyFileOperationBusy)return;
        ReShadeView.Visibility=page==AppPage.ReShade?Visibility.Visible:Visibility.Collapsed;SetNavButton(ReShadeNavButton,page==AppPage.ReShade);
        DllInstallationPage.Visibility=page==AppPage.DllInstallation?Visibility.Visible:Visibility.Collapsed;
        AdvancedRuntimePage.Visibility=page==AppPage.AdvancedRuntime?Visibility.Visible:Visibility.Collapsed;
        SetNavButton(DllInstallationNavButton,page==AppPage.DllInstallation);
        HomePage.Visibility = page == AppPage.Home ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == AppPage.About ? Visibility.Visible : Visibility.Collapsed;
        NvidiaComponentsPage.Visibility = page == AppPage.NvidiaComponents ? Visibility.Visible : Visibility.Collapsed;
        MfgSettingsPage.Visibility = page == AppPage.MfgSettings ? Visibility.Visible : Visibility.Collapsed;
        SetNavButton(HomeNavButton, page == AppPage.Home);
        SetNavButton(AboutNavButton, page == AppPage.About);
        SetNavButton(NvidiaComponentsNavButton, page is AppPage.NvidiaComponents or AppPage.AdvancedRuntime);
        SetNavButton(MfgSettingsNavButton, page == AppPage.MfgSettings);
        LocalizedPresentation.RefreshGeneratedItems(page switch {AppPage.Home=>HomePage,AppPage.About=>AboutPage,AppPage.NvidiaComponents=>NvidiaComponentsPage,AppPage.DllInstallation=>DllInstallationPage,AppPage.AdvancedRuntime=>AdvancedRuntimePage,AppPage.ReShade=>ReShadeView,_=>MfgSettingsPage});
    }

    private void NvidiaInventory_Click(object sender, RoutedEventArgs e) => SetNvidiaSubpage(NvidiaSubpage.Components);
    private async void NvidiaPackages_Click(object sender, RoutedEventArgs e)
    {
        SetNvidiaSubpage(NvidiaSubpage.StreamlineSdks);
        if (!streamlineListLoaded) await LoadStreamlineAssetsAsync();
    }

    private void NvidiaInstallPreview_Click(object sender, RoutedEventArgs e)
    {
        SetNvidiaSubpage(NvidiaSubpage.InstallComponents);
        UpdateInstallPreviewContext();
    }

    private void SetNvidiaSubpage(NvidiaSubpage page)
    {
        if(page==NvidiaSubpage.InstallComponents){
            if(AnyFileOperationBusy)return;
            AdvancedRuntimeMode.IsChecked=false;InstallComponentsPanel.Visibility=Visibility.Visible;SetPage(AppPage.AdvancedRuntime);LocalizedPresentation.RefreshGeneratedItems(AdvancedRuntimePage);return;
        }
        SetComponentsProvider(false);
        var components = page == NvidiaSubpage.Components;
        var packages = page == NvidiaSubpage.StreamlineSdks;
        var installPreview = page == NvidiaSubpage.InstallComponents;
        streamlineShowingPackages = packages;
        NvidiaInventoryPanel.Visibility = components ? Visibility.Visible : Visibility.Collapsed;
        StreamlinePackagesPanel.Visibility = packages ? Visibility.Visible : Visibility.Collapsed;
        InstallComponentsPanel.Visibility = installPreview ? Visibility.Visible : Visibility.Collapsed;
        NvidiaScanButton.Visibility = components ? Visibility.Visible : Visibility.Collapsed;
        NvidiaInventoryButton.Background = new SolidColorBrush(components ? Color.FromRgb(41, 35, 58) : Color.FromRgb(23, 25, 32));
        NvidiaInventoryButton.BorderBrush = new SolidColorBrush(components ? Color.FromRgb(84, 68, 114) : Color.FromRgb(52, 54, 66));
        NvidiaPackagesButton.Background = new SolidColorBrush(packages ? Color.FromRgb(41, 35, 58) : Color.FromRgb(23, 25, 32));
        NvidiaPackagesButton.BorderBrush = new SolidColorBrush(packages ? Color.FromRgb(84, 68, 114) : Color.FromRgb(52, 54, 66));
        NvidiaInstallPreviewButton.Background = new SolidColorBrush(installPreview ? Color.FromRgb(41, 35, 58) : Color.FromRgb(23, 25, 32));
        NvidiaInstallPreviewButton.BorderBrush = new SolidColorBrush(installPreview ? Color.FromRgb(84, 68, 114) : Color.FromRgb(52, 54, 66));
        NvidiaPageDisclaimerText.Text = packages
            ? "Downloaded files stay in local storage. A digest does not establish game compatibility or publisher authenticity by itself."
            : installPreview
                ? "Preview is read-only. Apply writes only explicitly reviewed files with backup; versions alone do not establish game compatibility."
                : "Read-only scan. Files found on disk are not proof that the game loaded them.";
        NvidiaOfficialLinkButton.Visibility = components ? Visibility.Visible : Visibility.Collapsed;
        NvidiaScanStatusText.Visibility = components ? Visibility.Visible : Visibility.Collapsed;
        LocalizedPresentation.RefreshGeneratedItems(NvidiaComponentsPage);
    }

    private void UpdateInstallPreviewContext()
    {
        var selectedGame = GamesCombo.SelectedItem as Game;
        InstallPreviewGameText.Text = selectedGame is null
            ? "No Steam game selected · choose a game on Home before selecting a destination."
            : $"{selectedGame.Name} · root: {selectedGameRoot ?? "unavailable"}";
        InstallPreviewChooseTargetButton.IsEnabled = selectedGameRoot is not null && Directory.Exists(selectedGameRoot);
        InstallPreviewSourceText.Text = selectedStreamlineStagingPath ?? "No verified staging folder in this app session. Select an official SDK, download/verify it, then use Stage preview.";
        InstallPreviewTargetText.Text = explicitComponentTargetDirectory ?? "Not selected · choose a folder inside the selected game's root";
        InstallPreviewItemsControl.ItemsSource = null;
        InstallPreviewNewCountText.Text = "New: 0";
        InstallPreviewReplaceCountText.Text = "Replace: 0";
        InstallPreviewReviewCountText.Text = "Review: 0";
        InstallPreviewBackupCountText.Text = "Backup candidates: 0";
        InstallPreviewStatusText.Text = selectedGameRoot is null
            ? "Select an installed game on Home. A target folder must be chosen explicitly."
            : "Choose a target folder and refresh. This preview does not modify files.";
        currentComponentPlan = null;
        InstallPreviewInstallButton.IsEnabled = false;
        InstallPreviewInstallButton.ToolTip = "No verified per-game compatibility profiles are configured.";
    }

    private async void UseSdkProvider_Click(object sender,RoutedEventArgs e)
    {
        if(verifiedStreamlineStage is not {} stage || installOperationCancellation is not null) { ShowThemeDialog("Stage SDK first","Select an official SDK and Stage preview first."); return; }
        ProviderSelectionText.Text="Verifying production SDK DLSS-G and NVIDIA signature…";
        try {
            var store=Path.Combine(streamlineDownloads.StorageRoot,"Providers");
            selectedDlssGProvider=await DlssGProviderImporter.FromSdkAsync(stage,streamlineDownloads.DownloadsDirectory,streamlineDownloads.StagingDirectory,store);
            ProviderSelected();
        } catch(Exception ex) { selectedDlssGProvider=null; ProviderSelectionText.Text="DLSS-G blocked: "+ex.Message; InvalidateInstallPreview(); }
    }
    private async void ImportProvider_Click(object sender,RoutedEventArgs e)
    {
        if(installOperationCancellation is not null) return;
        var dialog=new OpenFileDialog {Title="Import NVIDIA-signed native x64 DLSS-G provider",Filter="DLL files (*.dll)|*.dll"};
        if(ShowNativeDialog(dialog,this)!=true)return;
        ProviderSelectionText.Text="Checking Authenticode, NVIDIA signer, native x64, version and SHA-256…";
        try { selectedDlssGProvider=await Task.Run(()=>new DlssGProviderVerifier().Verify(dialog.FileName)); ProviderSelected(); }
        catch(Exception ex) { selectedDlssGProvider=null; ProviderSelectionText.Text="DLSS-G blocked: "+ex.Message; InvalidateInstallPreview(); }
    }
    private void ProviderSelected()
    {
        InvalidateInstallPreview();
        var p=selectedDlssGProvider!;
        ProviderSelectionText.Text=$"Selected DLSS-G {TechnicalVersionDisplay.Format(p.Version)} · native x64 · trusted {p.Signer}\nSHA-256 {p.Sha256}\nSource: {p.Path}\nDocumented pairs: Streamline 2.14.1 / DLSS-G 310.9.1; Streamline 2.14.16 / DLSS-G 310.9.16. Compatibility remains unconfirmed. Refresh preview.";
    }
    private void ProviderSource_Click(object sender,RoutedEventArgs e) =>
        OpenExternalLink("https://github.com/NVIDIA/DLSS/releases/tag/v310.9.16");
    private AdvancedRuntimeInstaller GetAdvancedInstaller(string? root)
    {
        if(root is null) throw new InvalidDataException("Choose a game first.");
        return new(streamlineDownloads.DownloadsDirectory,streamlineDownloads.StagingDirectory,CreateBackupService(root));
    }
    private void RuntimeMode_Changed(object sender,RoutedEventArgs e)
    {
        if(InstallPreviewInstallButton is null) return;
        InvalidateInstallPreview();
        InstallPreviewStatusText.Text="Mode/API changed. Refresh preview and review all files. Manual is not verified compatibility.";
    }
    private async void ImportRuntimeZip_Click(object sender,RoutedEventArgs e)
    {
        if(streamlineBusy) return;
        var asset=selectedStreamlineAsset;
        if(asset is null || asset.Sha256 is not {Length:64}) { ShowThemeDialog("Select official SDK metadata","On Streamline SDKs, refresh and select the matching official release with a SHA-256 digest first. Unconfirmed local packages are blocked."); return; }
        var dialog=new OpenFileDialog {Title="Select the matching official Streamline SDK ZIP",Filter="ZIP archives (*.zip)|*.zip"};
        if(ShowNativeDialog(dialog,this)!=true) return;
        try {
            var target=await StreamlineLocalArchiveImporter.ImportAsync(dialog.FileName,streamlineDownloads.DownloadsDirectory,asset);
            selectedStreamlineDownloadPath=target;
            ShowThemeDialog("Local SDK verified","The ZIP matches official metadata. Use Streamline SDKs → Stage preview. No game files changed.");
            UpdateStageButton(true);
        } catch(Exception ex) { ShowThemeDialog("Local ZIP blocked",ex.Message,true); }
    }
    private void InstallPreviewChooseTarget_Click(object sender, RoutedEventArgs e)
    {
        if (selectedGameRoot is null || !Directory.Exists(selectedGameRoot))
        {
            InstallPreviewStatusText.Text = "Select an installed game on Home before choosing a target folder.";
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a component folder inside the selected game",
            InitialDirectory = selectedExe is null ? selectedGameRoot : Path.GetDirectoryName(selectedExe),
            Multiselect = false
        };
        if (ShowNativeDialog(dialog,this) != true) return;
        var candidate = Path.GetFullPath(dialog.FolderName);
        if (!IsPathWithinRoot(selectedGameRoot, candidate))
        {
            InvalidateInstallPreview();
            installOperationCancellation?.Cancel();
        explicitComponentTargetDirectory = null;
            InstallPreviewTargetText.Text = "Not selected · folder was outside the selected game's root";
            InstallPreviewStatusText.Text = "Choose a target folder inside the currently selected game's root.";
            return;
        }

        explicitComponentTargetDirectory = candidate;
        InstallPreviewTargetText.Text = candidate;
        InvalidateInstallPreview();
        InstallPreviewStatusText.Text = "Target folder selected explicitly. Refresh to recalculate the read-only preview.";
    }

    private async void InstallPreviewRefresh_Click(object sender, RoutedEventArgs e) => await RefreshInstallComponentsPreviewAsync();

    private async Task RefreshInstallComponentsPreviewAsync()
    {
        installPreviewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        installPreviewCancellation = cancellation;
        var token = cancellation.Token;
        var gameRoot = selectedGameRoot;
        var evidence = verifiedStreamlineStage;
        var game = GamesCombo.SelectedItem as Game;
        var targetPath = explicitComponentTargetDirectory;
        currentComponentPlan = null;
        InstallPreviewInstallButton.IsEnabled = false;
        InstallPreviewRefreshButton.IsEnabled = false;
        InstallPreviewCancelButton.IsEnabled = true;
        InstallPreviewChooseTargetButton.IsEnabled = false;
        InstallPreviewStatusText.Text = "Checking staged candidates and target paths…";
        try
        {
            var installer = GetStreamlineComponentInstaller(gameRoot);
            var result = AdvancedRuntimeMode.IsChecked == true
                ? await GetAdvancedInstaller(gameRoot).PreviewAsync(evidence ?? throw new InvalidDataException("Stage an official verified SDK first."), gameRoot ?? "", selectedExe ?? "", targetPath ?? "", AdvancedD3D12.IsChecked == true, token, selectedDlssGProvider)
                : await installer.CreatePlanAsync(evidence, game?.AppId, gameRoot, targetPath, selectedComponentFiles, token);
            if (!ReferenceEquals(installPreviewCancellation, cancellation)) return;
            currentComponentPlan = result;
            InstallPreviewItemsControl.ItemsSource = result.Entries.Select(x => new ComponentPlanRow(x, AdvancedRuntimeMode.IsChecked != true)).ToArray();
            LocalizedPresentation.RefreshGeneratedItems(InstallPreviewItemsControl);
            InstallPreviewNewCountText.Text = $"New: {result.SelectedNewFiles}";
            InstallPreviewReplaceCountText.Text = $"Replace: {result.SelectedReplacements}";
            InstallPreviewReviewCountText.Text = $"Needs review: {result.SelectedNeedsReview}";
            InstallPreviewBackupCountText.Text = $"Blocked: {result.SelectedBlocked}";
            InstallPreviewInstallButton.IsEnabled = result.CanInstall;
            InstallPreviewInstallButton.ToolTip = result.CanInstall ? "Install the selected reviewed components" : "Installation is blocked until every selected component has verified per-game compatibility.";
            InstallPreviewStatusText.Text = result.Message;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(installPreviewCancellation, cancellation))
                InstallPreviewStatusText.Text = "Preview cancelled. No files were changed.";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(installPreviewCancellation, cancellation))
            {
                InstallPreviewItemsControl.ItemsSource = null;
                currentComponentPlan = null;
                InstallPreviewInstallButton.IsEnabled = false;
                InstallPreviewStatusText.Text = "Could not build preview: " + ExplainRuntimeError(ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(installPreviewCancellation, cancellation))
            {
                installPreviewCancellation = null;
                InstallPreviewRefreshButton.IsEnabled = true;
                InstallPreviewCancelButton.IsEnabled = false;
                InstallPreviewChooseTargetButton.IsEnabled = true;
            }
            cancellation.Dispose();
        }
    }

    private void InstallPreviewCancel_Click(object sender, RoutedEventArgs e)
    {
        installPreviewCancellation?.Cancel();
        installOperationCancellation?.Cancel();
        if (installOperationCancellation is not null) InstallPreviewStatusText.Text = "Cancellation requested. The transaction will roll back completed writes where safe.";
    }

    private StreamlineComponentInstaller GetStreamlineComponentInstaller(string? gameRoot)
    {
        if (gameRoot is null) return new(streamlineDownloads.DownloadsDirectory, streamlineDownloads.StagingDirectory,
            new BackupRestoreService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "Components", "Transactions", "unselected")));
        var rootHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(gameRoot).ToUpperInvariant()))).ToLowerInvariant();
        var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "Components", "Transactions", rootHash);
        return new(streamlineDownloads.DownloadsDirectory, streamlineDownloads.StagingDirectory, new BackupRestoreService(store), new NoVerifiedStreamlineCompatibilityCatalog());
    }

    private async void InstallPreviewInstall_Click(object sender, RoutedEventArgs e)
    {
        if(AnyFileOperationBusy)return;
        if (verifiedStreamlineStage is null || currentComponentPlan is not { CanInstall: true } plan || selectedGameRoot is null || GamesCombo.SelectedItem is not Game game || explicitComponentTargetDirectory is null) return;
        var stage = verifiedStreamlineStage;
        var selectedRoot = selectedGameRoot;
        var selectedTarget = explicitComponentTargetDirectory;
        var selectedFiles = new HashSet<string>(selectedComponentFiles, StringComparer.OrdinalIgnoreCase);
        var displayedPlan = plan;
        var details = string.Join("\n", plan.Entries.Where(x => x.IsSelected).Select(x => $"{x.Action}: {x.FileName}\nSource: {x.SourcePath}\nSHA-256: {x.Sha256}\nTarget: {x.TargetPath}"));
        if (!ShowThemeConfirmation("Confirm component installation", "This will write the selected Streamline files to the explicitly chosen game folder. Existing files will be backed up.\n\n" + details)) return;
        InstallPreviewInstallButton.IsEnabled = false;
        installOperationCancellation?.Cancel();
        installOperationCancellation = new CancellationTokenSource();
        var operationToken = installOperationCancellation.Token;
        InstallPreviewCancelButton.IsEnabled = true;
        try
        {
            var progress = new Progress<int>(value => { InstallPreviewStatusText.Text = $"Installing reviewed components… {value}%"; });
            if (selectedExe is null || IsSelectedGameProcessRunning(selectedExe, out _)) throw new IOException("Close the selected game before installing components.");
            var result = AdvancedRuntimeMode.IsChecked == true
                ? await GetAdvancedInstaller(selectedRoot).InstallAsync(stage, selectedRoot, selectedExe!, selectedTarget, AdvancedD3D12.IsChecked == true, displayedPlan, progress, operationToken, selectedDlssGProvider)
                : await GetStreamlineComponentInstaller(selectedRoot).InstallAsync(stage, game.AppId, selectedRoot, selectedTarget, selectedFiles, displayedPlan, progress, operationToken);
            var completionMessage = $"Transaction {result.Transaction.TransactionId} completed. {result.InstalledFiles.Count} file(s) installed. Runtime loaded: NOT VERIFIED. Dynamic MFG active: NOT VERIFIED. Fully restart, verify loaded paths/versions and Dynamic acceptance in MFG Unlock/ReShade.log. Use Restore to undo.";
            Log("Streamline component transaction completed: " + result.Transaction.TransactionId);
            await RefreshInstallComponentsPreviewAsync();
            InstallPreviewStatusText.Text = completionMessage;
        }
        catch (OperationCanceledException)
        {
            InstallPreviewStatusText.Text = "Installation was cancelled. Completed changes were rolled back where their file hashes still matched the transaction.";
            Log("Streamline component installation cancellation requested.");
        }
        catch (Exception ex)
        {
            InstallPreviewStatusText.Text = "Installation failed or was rolled back: " + ex.Message;
            Log("Streamline component transaction failed: " + ex.Message);
            ShowThemeDialog("Component installation failed", ex.Message, true);
            InstallPreviewInstallButton.IsEnabled = currentComponentPlan?.CanInstall == true;
        }
        finally { installOperationCancellation?.Dispose(); installOperationCancellation = null; InstallPreviewCancelButton.IsEnabled = false; AdvancedRuntimeMode.IsEnabled = true; AdvancedD3D12.IsEnabled = true; }
    }

    private async void InstallPreviewRestore_Click(object sender, RoutedEventArgs e)
    {
        if(AnyFileOperationBusy)return;
        if (selectedGameRoot is null || !Directory.Exists(selectedGameRoot)) { ShowThemeDialog("Select a game", "Select a game before reviewing its component transactions.", true); return; }
        // The same per-game transaction store is used by the installer; construct its service directly for recovery operations.
        var backup = CreateBackupService(selectedGameRoot);
        await ShowRestoreManagerAsync(selectedGameRoot, backup);
        InvalidateInstallPreview(); InstallPreviewStatusText.Text = "Restore review closed. Refresh preview to check the current files.";
    }

    private BackupRestoreService CreateBackupService(string gameRoot)
    {
        var rootHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(gameRoot).ToUpperInvariant()))).ToLowerInvariant();
        return new BackupRestoreService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "Components", "Transactions", rootHash));
    }

    private void ComponentSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string name, IsChecked: true }) selectedComponentFiles.Add(name);
        else if (sender is CheckBox { Tag: string uncheckedName }) selectedComponentFiles.Remove(uncheckedName);
        _ = RefreshInstallComponentsPreviewAsync();
    }

    private bool ShowThemeConfirmation(string title, string message, Window? owner = null)
    {
        var dialog = new Window { Title = title, Owner = owner ?? this, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 500, ShowInTaskbar = false, Icon = Icon };
        var shell = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 27)), BorderBrush = new SolidColorBrush(Color.FromRgb(112, 75, 208)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22) };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(244, 242, 250)), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(196, 193, 207)), FontSize = 12, LineHeight = 19, Margin = new Thickness(0, 0, 0, 20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 92, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = "Confirm", MinWidth = 98, Padding = new Thickness(16, 8, 16, 8), Background = new SolidColorBrush(Color.FromRgb(112, 75, 208)), BorderBrush = new SolidColorBrush(Color.FromRgb(167, 139, 250)) };
        cancel.Click += (_, _) => dialog.DialogResult = false; confirm.Click += (_, _) => dialog.DialogResult = true;
        var bodyScroll = new ScrollViewer { Content = body, MaxHeight = Math.Max(160, Math.Min(SystemParameters.WorkArea.Height, ActualHeight) - 210), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; bodyScroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), FindResource("PremiumScrollBar")); buttons.Children.Add(cancel); buttons.Children.Add(confirm); Grid.SetRow(bodyScroll, 1); Grid.SetRow(buttons, 2); grid.Children.Add(heading); grid.Children.Add(bodyScroll); grid.Children.Add(buttons); shell.Child = grid; dialog.Content = shell;
        return dialog.ShowDialog() == true;
    }

    private async Task ShowRestoreManagerAsync(string gameRoot, BackupRestoreService service, Window? owner = null, string? transactionFilter = null)
    {
        var dialog = new Window { Title = "Component transaction recovery", Owner = owner ?? this, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.CanResize, Width = 760, Height = 560, MinWidth = 600, MinHeight = 420, ShowInTaskbar = false, Icon = Icon };
        dialog.Resources.Add(typeof(ScrollBar), FindResource("PremiumScrollBar"));
        var shell = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 27)), BorderBrush = new SolidColorBrush(Color.FromRgb(57, 52, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18) };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "Component transactions · restore preview", Foreground = new SolidColorBrush(Color.FromRgb(244, 242, 250)), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        var transactions = new ListBox { Background = new SolidColorBrush(Color.FromRgb(18, 20, 26)), Foreground = new SolidColorBrush(Color.FromRgb(226, 223, 236)), BorderBrush = new SolidColorBrush(Color.FromRgb(52, 54, 66)), Margin = new Thickness(0, 0, 0, 10), HorizontalContentAlignment=HorizontalAlignment.Stretch };
        
        transactions.ItemTemplate=RestoreTransactionTemplate();ConfigureRestoreList(transactions,0);ScrollViewer.SetHorizontalScrollBarVisibility(transactions,ScrollBarVisibility.Disabled);
        var transactionStyle=new Style(typeof(ListBoxItem),transactions.ItemContainerStyle);transactionStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));transactionStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,5,8,5)));transactions.ItemContainerStyle=transactionStyle;
        var details = new ContentControl {Margin=new Thickness(0,8,0,0)};
        var status = new TextBlock { Text = "Select a transaction to verify its manifest and preview recovery.", Foreground = new SolidColorBrush(Color.FromRgb(201, 185, 255)), FontSize = 10, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        var restore = new Button { Content = "Restore selected transaction", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false, Background = new SolidColorBrush(Color.FromRgb(112, 75, 208)), BorderBrush = new SolidColorBrush(Color.FromRgb(142, 111, 240)) };
        var close = new Button { Content = "Close", Padding = new Thickness(14, 7, 14, 7) };
        footer.Children.Add(refresh); footer.Children.Add(restore); footer.Children.Add(close);
        Grid.SetRow(transactions, 1); Grid.SetRow(details, 2); Grid.SetRow(status, 3); Grid.SetRow(footer, 4);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(title); grid.Children.Add(transactions); grid.Children.Add(details); grid.Children.Add(status); grid.Children.Add(footer); shell.Child = grid; dialog.Content = shell;
        BackupRestorePreview? selectedPreview = null;
        var previewGeneration = 0;bool restoring=false;dialog.Closing+=(_,e)=>{if(restoring)e.Cancel=true;};
        var historyExe = transactionFilter is null ? null : selectedExe;
        bool MatchesHistory(BackupRestorePreview preview) => transactionFilter is null ||
            historyExe is not null && selectedExe == historyExe && selectedGameRoot == gameRoot &&
            MfgUserData.MatchesHistoryFiles(gameRoot, historyExe, preview.Files);
        async Task Reload()
        {
            transactions.ItemsSource = null; details.Content = null; restore.IsEnabled = false;
            try
            {
                var records = await Task.Run(() => service.ListTransactions(gameRoot)
                    .Where(t => transactionFilter is null || t.TransactionId == transactionFilter).ToList());
                transactions.ItemsSource = records.Select(x => new RestoreRow(x)).ToArray();ConfigureRestoreList(transactions,records.Count);
                LocalizedPresentation.RefreshGeneratedItems(transactions);
                status.Text = (records.Count == 0 ? "No component transactions were found for this game." : $"Found {records.Count} transaction(s). Manifests and backups are verified before restore.")+"\nSaved status is historical; select a transaction to recheck current files without reinstalling.";
            }
            catch (DirectoryNotFoundException) { status.Text = "No component transactions were found for this game."; }
            catch (Exception ex) { status.Text = "Could not list transactions: " + ex.Message; }
        }
        transactions.SelectionChanged += async (_, _) =>
        {
            var generation = ++previewGeneration;
            selectedPreview = null; restore.IsEnabled = false;
            if (transactions.SelectedItem is not RestoreRow row) { details.Content = null; return; }
            try
            {
                var preview = await Task.Run(() => service.PreviewRestore(gameRoot, row.Info.TransactionId));
                if (generation != previewGeneration || transactions.SelectedItem is not RestoreRow current || current.Info.TransactionId != row.Info.TransactionId) return;
                selectedPreview = preview;
                details.Content = RestorePreviewContent(preview,gameRoot,dialog);LocalizedPresentation.RefreshGeneratedItems(details);
                status.Text = RestoreSummary(preview,LocalizedPresentation.Language) + "\n" + LocalizedPresentation.Language.Lookup(preview.ManifestVerified ? "Manifest HMAC verified." : "Manifest is NOT verified.");
                var newer = transactionFilter is not null && preview.CanRestore &&
                    await Task.Run(() => service.HasNewerOverlappingTransaction(gameRoot, preview.TransactionId));
                if (generation != previewGeneration) return;
                restore.IsEnabled = preview.CanRestore && MatchesHistory(preview) && !newer;
                if (newer) status.Text = LocalizedPresentation.Language.Lookup("Restore newer changes to these configuration files first.");
            }
            catch (Exception ex) { details.Content = null; status.Text = "Restore preview failed; no files were changed: " + ex.Message; }
        };
        refresh.Click += async (_, _) => await Reload(); close.Click += (_, _) => dialog.Close();
        restore.Click += async (_, _) =>
        {
            if (restoring||!restore.IsEnabled)return;restore.IsEnabled=false;
            if (transactions.SelectedItem is not RestoreRow row || selectedPreview is not { CanRestore: true } shown || shown.TransactionId != row.Info.TransactionId) return;
            BackupRestorePreview freshPreview;
            try { freshPreview = await Task.Run(() => service.PreviewRestore(gameRoot, row.Info.TransactionId)); }
            catch (Exception ex) { status.Text = "Restore preflight failed; no files were changed: " + ex.Message; return; }
            if (transactions.SelectedItem is not RestoreRow stillSelected || stillSelected.Info.TransactionId != row.Info.TransactionId) return;
            selectedPreview = freshPreview;
            details.Content = RestorePreviewContent(freshPreview,gameRoot,dialog);LocalizedPresentation.RefreshGeneratedItems(details);
            status.Text = RestoreSummary(freshPreview,LocalizedPresentation.Language) + "\n" + LocalizedPresentation.Language.Lookup(freshPreview.ManifestVerified ? "Manifest HMAC verified." : "Manifest is NOT verified.");
            if (!freshPreview.CanRestore || !MatchesHistory(freshPreview)) { restore.IsEnabled = false; return; }
            var warning = transactionFilter is null ? "" :
                LocalizedPresentation.Language.Lookup("This restores whole configuration files from the selected save, including settings outside the history list.") + "\n\n";
            if (!ShowThemeConfirmation("Confirm restore", warning + RestoreConfirmationText(gameRoot,freshPreview), dialog)){restore.IsEnabled=true;return;}
            restore.IsEnabled = false;restoring=true;var wasBusy=wizardOperationBusy;wizardOperationBusy=true;refresh.IsEnabled=false;transactions.IsEnabled=false;close.IsEnabled=false;
            try
            {
                if (selectedExe is null || selectedGameRoot != gameRoot || !MatchesHistory(freshPreview) || IsSelectedGameProcessRunning(selectedExe, out _)) throw new IOException("Select the same game and close it before restoring files.");
                var result = transactionFilter is null ? await service.RestoreAsync(gameRoot, row.Info.TransactionId) :
                    await service.RestoreSettingsHistoryAsync(gameRoot, row.Info.TransactionId);
                status.Text = $"Transaction {result.TransactionId}: {result.Status}. Refresh the list to verify the final state.";
                Log("Restore transaction completed: " + result.TransactionId + " · " + result.Status);
                await Reload();
            }
            catch (Exception ex) { var message = LocalizedPresentation.Language.Translate(ex.Message); status.Text = "Restore stopped safely: " + message; ShowThemeDialog("Restore needs review", message, true); await Reload(); }
            finally{restoring=false;wizardOperationBusy=wasBusy;refresh.IsEnabled=true;transactions.IsEnabled=true;close.IsEnabled=true;restore.IsEnabled=false;}
        };
        await Reload(); dialog.ShowDialog();
    }

    private void InvalidateInstallPreview()
    {
        var active = installPreviewCancellation;
        installPreviewCancellation = null;
        active?.Cancel();
        currentComponentPlan = null;
        if (InstallPreviewInstallButton is not null) InstallPreviewInstallButton.IsEnabled = false;
        if (InstallPreviewRefreshButton is not null) InstallPreviewRefreshButton.IsEnabled = true;
        if (InstallPreviewCancelButton is not null) InstallPreviewCancelButton.IsEnabled = false;
        if (InstallPreviewChooseTargetButton is not null) InstallPreviewChooseTargetButton.IsEnabled = true;
    }

    private static bool IsPathWithinRoot(string root, string path)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(canonicalRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async void StreamlineRefresh_Click(object sender, RoutedEventArgs e) => await LoadStreamlineAssetsAsync();

    private async Task LoadStreamlineAssetsAsync()
    {
        if (streamlineBusy) return;
        var previous = StreamlineAssetsList.SelectedItem as StreamlineAssetRow;
        streamlineBusy = true;
        StreamlineRefreshButton.IsEnabled = false;
        StreamlineStatusText.Text = "Loading official Streamline SDK releases…";
        streamlineAssets.Clear();
        try
        {
            var releases = await streamlineApi.GetReleasesAsync();
            foreach (var release in releases)
            foreach (var asset in release.Assets)
            {
                var date = release.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Publication date unavailable";
                var integrity = StreamlineDigestVerifier.Describe(asset.Sha256);
                streamlineAssets.Add(new(asset, $"{release.Name}  ·  {TechnicalVersionDisplay.Format(release.Tag)}", $"{date}  ·  {asset.Name}", FormatBytes(asset.Size), integrity));
            }
            streamlineListLoaded = true;
            StreamlineStatusText.Text = streamlineAssets.Count == 0 ? "No published SDK ZIP packages matching streamline-sdk-v*.zip were found." : $"Loaded {streamlineAssets.Count} official SDK archive(s). Select one to download.";
            if (previous is not null)
            {
                var matching = streamlineAssets.FirstOrDefault(row =>
                    row.Asset.ReleaseTag.Equals(previous.Asset.ReleaseTag, StringComparison.OrdinalIgnoreCase)
                    && row.Asset.Name.Equals(previous.Asset.Name, StringComparison.OrdinalIgnoreCase));
                if (matching is not null) StreamlineAssetsList.SelectedItem = matching;
            }
        }
        catch (Exception ex)
        {
            streamlineListLoaded = false;
            StreamlineStatusText.Text = "Could not load Streamline releases. Check your connection and use Refresh list to retry. " + ex.Message;
        }
        finally
        {
            streamlineBusy = false;
            StreamlineRefreshButton.IsEnabled = true;
            StreamlineDownloadButton.IsEnabled = StreamlineAssetsList.SelectedItem is StreamlineAssetRow;
            LocalizedPresentation.RefreshGeneratedItems(StreamlineAssetsList);
            if (StreamlineAssetsList.SelectedItem is StreamlineAssetRow) _ = ValidateSelectedLocalArchiveAsync();
            else UpdateStageButton(false);
        }
    }

    private void StreamlineAssets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StreamlineDownloadButton.IsEnabled = StreamlineAssetsList.SelectedItem is StreamlineAssetRow && !streamlineBusy;
        selectedStreamlineDownloadPath = null;
        selectedStreamlineAsset = (StreamlineAssetsList.SelectedItem as StreamlineAssetRow)?.Asset;
        selectedStreamlineTag = selectedStreamlineAsset?.ReleaseTag;
        var generation = ++streamlineSelectionGeneration;
        UpdateStageButton(false);
        if (selectedStreamlineAsset is not null && !streamlineBusy) _ = ValidateSelectedLocalArchiveAsync(generation);
    }

    private async Task ValidateSelectedLocalArchiveAsync(int? expectedGeneration = null)
    {
        var asset = selectedStreamlineAsset;
        if (asset is null || streamlineBusy) return;
        var generation = expectedGeneration ?? ++streamlineSelectionGeneration;
        UpdateStageButton(false);
        StreamlineStatusText.Text = $"Checking local {asset.Name} against the selected release size and SHA-256…";
        try
        {
            var result = await streamlineLocalArchiveValidator.CheckAsync(asset);
            if (generation != streamlineSelectionGeneration || !ReferenceEquals(asset, selectedStreamlineAsset)) return;
            selectedStreamlineDownloadPath = result.IsValid ? result.FilePath : null;
            if(result.IsValid&&result.FilePath is not null&&!NvidiaStageMatchesSelection()){
                try{
                    var recovered=await streamlineEvidenceService.RecoverLatestAsync(streamlineDownloads.DownloadsDirectory,streamlineDownloads.StagingDirectory,result.FilePath,asset);
                    if(generation!=streamlineSelectionGeneration||!ReferenceEquals(asset,selectedStreamlineAsset))return;
                    verifiedStreamlineStage=recovered;selectedStreamlineStagingPath=recovered?.StagingDirectory;
                }catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException){
                    if(generation!=streamlineSelectionGeneration||!ReferenceEquals(asset,selectedStreamlineAsset))return;
                    verifiedStreamlineStage=null;selectedStreamlineStagingPath=null;Log("Cached SDK staging needs review: "+ex.Message);
                }
            }
            UpdateStageButton(result.IsValid);
            StreamlineStatusText.Text = result.IsValid
                ? $"Verified existing archive: {asset.Name} ({FormatBytes(asset.Size)}). {result.Message}"
                : result.Message;
        }
        catch (Exception ex)
        {
            if (generation != streamlineSelectionGeneration || !ReferenceEquals(asset, selectedStreamlineAsset)) return;
            selectedStreamlineDownloadPath = null;
            UpdateStageButton(false);
            StreamlineStatusText.Text = "Could not verify the local archive: " + ex.Message;
        }
    }

    private void UpdateStageButton(bool enabled){StreamlineStageButton.IsEnabled=enabled&&!streamlineBusy;UpdateNvidiaSdkSummary();}

    private async void StreamlineDownload_Click(object sender, RoutedEventArgs e)
    {
        if (StreamlineAssetsList.SelectedItem is not StreamlineAssetRow row || streamlineBusy) return;
        await RunStreamlineOperationAsync(async token =>
        {
            StreamlineStatusText.Text = $"Downloading {row.Asset.Name}…";
            StreamlineProgressBar.Visibility = Visibility.Visible; StreamlineProgressBar.Value = 0;
            var progress = new Progress<StreamlineDownloadProgress>(p =>
            {
                if (p.TotalBytes is > 0) StreamlineProgressBar.Value = Math.Min(100, p.BytesReceived * 100d / p.TotalBytes.Value);
                StreamlineStatusText.Text = $"Downloading {row.Asset.Name}  ·  {FormatBytes(p.BytesReceived)} of {FormatBytes(p.TotalBytes ?? row.Asset.Size)}";
            });
            var result = await streamlineDownloads.DownloadAsync(row.Asset, progress, token);
            selectedStreamlineDownloadPath = result.DigestVerified ? result.FilePath : null; selectedStreamlineTag = row.Asset.ReleaseTag; selectedStreamlineAsset = row.Asset;
            UpdateStageButton(result.DigestVerified);
            StreamlineStatusText.Text = $"Downloaded {row.Asset.Name} ({FormatBytes(new FileInfo(result.FilePath).Length)}). {result.IntegrityMessage}";
            Log("Streamline SDK archive downloaded: " + result.FilePath);
        });
    }

    private async void StreamlineStage_Click(object sender, RoutedEventArgs e)
    {
        var asset = selectedStreamlineAsset;
        var tag = selectedStreamlineTag;
        if (asset is null || tag is null || streamlineBusy) return;
        selectedStreamlineDownloadPath = null;
        selectedStreamlineStagingPath = null;
        verifiedStreamlineStage = null;
        UpdateStageButton(false);
        await RunStreamlineOperationAsync(async token =>
        {
            var check = await streamlineLocalArchiveValidator.CheckAsync(asset, token);
            if (!check.IsValid || check.FilePath is null) throw new InvalidDataException(check.Message);
            selectedStreamlineDownloadPath = check.FilePath;
            var archive = check.FilePath;
            StreamlineStatusText.Text = "Validating archive and preparing a read-only preview staging folder…";
            var result = await streamlineStager.StageAsync(archive, streamlineDownloads.StagingDirectory, tag, token, asset.Sha256);
            verifiedStreamlineStage = await streamlineEvidenceService.CaptureAsync(streamlineDownloads.DownloadsDirectory, streamlineDownloads.StagingDirectory, archive, asset, result.DirectoryPath, token);
            selectedStreamlineStagingPath = result.DirectoryPath;
            StreamlineStatusText.Text = $"Staged {result.FilesExtracted} permitted runtime/license file(s) in {result.DirectoryPath}. {result.FilesSkipped} unrelated SDK file(s) were not extracted. Archive digest and staged runtime inventory verified for this session. Nothing was copied to the game.";
            UpdateInstallPreviewContext();
            Log("Streamline SDK preview staged: " + result.DirectoryPath);
        });
    }

    private async Task RunStreamlineOperationAsync(Func<CancellationToken, Task> operation)
    {
        streamlineBusy = true; StreamlineDownloadButton.IsEnabled = false; StreamlineRefreshButton.IsEnabled = false;
        StreamlineAssetsList.IsEnabled = false;
        StreamlineCancelButton.Visibility = Visibility.Visible; streamlineOperationCancellation = new CancellationTokenSource();
        try { await operation(streamlineOperationCancellation.Token); }
        catch (OperationCanceledException) { StreamlineStatusText.Text = "Operation cancelled. No partial archive or staging folder was kept."; }
        catch (Exception ex) { StreamlineStatusText.Text = "Operation failed: " + ex.Message; Log("Streamline operation failed: " + ex.Message); }
        finally
        {
            streamlineOperationCancellation.Dispose(); streamlineOperationCancellation = null;
            streamlineBusy = false; StreamlineCancelButton.Visibility = Visibility.Collapsed; StreamlineProgressBar.Visibility = Visibility.Collapsed;
            StreamlineAssetsList.IsEnabled = true; StreamlineRefreshButton.IsEnabled = true; StreamlineDownloadButton.IsEnabled = StreamlineAssetsList.SelectedItem is StreamlineAssetRow;
            UpdateStageButton(selectedStreamlineDownloadPath is not null && selectedStreamlineAsset is not null);
        }
    }

    private void StreamlineCancel_Click(object sender, RoutedEventArgs e) => streamlineOperationCancellation?.Cancel();
    private void StreamlineOpenDownloads_Click(object sender, RoutedEventArgs e) => OpenFolderPath(streamlineDownloads.DownloadsDirectory);
    private void StreamlineOpenStaging_Click(object sender, RoutedEventArgs e) => OpenFolderPath(streamlineDownloads.StagingDirectory);
    private void OpenFolderPath(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
        catch (Exception ex) { StreamlineStatusText.Text = "Could not open folder: " + ex.Message; }
    }
    private static string FormatBytes(long bytes) => bytes < 1024 * 1024 ? (bytes/1024d).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+" KB" : (bytes/(1024d*1024)).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+" MB";

    private static void SetNavButton(Button button, bool active)
    {
        button.Background = new SolidColorBrush(active ? Color.FromRgb(41, 35, 58) : Color.FromRgb(23, 25, 32));
        button.BorderBrush = new SolidColorBrush(active ? Color.FromRgb(84, 68, 114) : Color.FromRgb(52, 54, 66));
    }

    private int mfgLoadRevision;
    private sealed record MfgDraft(string AddonHash, string ContentHash, string CatalogIdentity, Dictionary<string, string> Changes);
    private readonly Dictionary<string, MfgDraft[]> mfgDrafts = new(StringComparer.OrdinalIgnoreCase);

    private async Task LoadMfgSettingsAsync()
    {
        var revision = ++mfgLoadRevision;
        if (mfgSettingsExePath is {} previousExe && mfgSettingsAddonHash is { Length: > 0 } previousHash)
        {
            var pending = PendingMfgEdits();
            if (pending.Length == 0) mfgDrafts.Remove(previousExe);
            else mfgDrafts[previousExe] = pending.Select(e => new MfgDraft(previousHash, e.Doc.Snapshot.ContentSha256,
                e.Doc.Service.ConfigFileName + "|" + e.Doc.Snapshot.CatalogIdentity, e.Changes)).ToArray();
        }
        LoadSettingsCatalog();
        RememberSettingsExpansion();
        mfgSettingsSnapshot=null;mfgSettingsExePath=null;mfgSettingsAddonHash=null;mfgSettingsDocuments.Clear();mfgSettingRows.Clear();xeMfgSettingRows.Clear();GenerationModePanel.Children.Clear();
        MfgSettingsSaveButton.IsEnabled=false;MfgSettingsDiscardButton.IsEnabled=false;
        UpdateSettingsCatalogPresentation();
        UpdateMfgSearchPresentation();
        if(!HasCurrentExecutable()||selectedGameRoot is null){SetMfgText(MfgSettingsGameText,"Select a game and its executable on Home first.");SetMfgText(MfgSettingsConfigText,"No configuration loaded · settings remain read-only.");SetMfgText(XeMfgConfigText,"MFGAdaUnlock.ini · not loaded");SetMfgText(MfgSettingsStatusText,"A current executable inside the selected game is required.");return;}
        var exe=selectedExe!;var root=selectedGameRoot;var addonPath=Path.Combine(Path.GetDirectoryName(exe)!,"renodx-mfgunlock.addon64");
        SetMfgText(MfgSettingsGameText,"Saved configuration for: {0}",exe);SetMfgText(MfgSettingsConfigText,"Checking installed addon and ReShade.ini…");
        try{
            var identity=await Task.Run(()=>{
                if(!File.Exists(addonPath))return (Version:"",Hash:"");
                if((File.GetAttributes(addonPath)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("The MFG Unlock addon path is a reparse point; settings editing is blocked.");
                return (Version:FileVersionInfo.GetVersionInfo(addonPath).FileVersion??"",Hash:MfgUnlockSettingsService.GetSha256(File.ReadAllBytes(addonPath)));
            });
            var snapshots=await Task.Run(()=>new[]{mfgUnlockSettings.Load(Path.Combine(Path.GetDirectoryName(exe)!,"ReShade.ini"),root,identity.Version),xeMfgSettings.Load(Path.Combine(Path.GetDirectoryName(exe)!,"MFGAdaUnlock.ini"),root,identity.Version)});
            if(revision != mfgLoadRevision || !string.Equals(exe,selectedExe,StringComparison.OrdinalIgnoreCase)||!HasCurrentExecutable())return;
            mfgSettingsSnapshot=snapshots[0];mfgSettingsExePath=exe;mfgSettingsAddonHash=identity.Hash;
            var closed=!IsSelectedGameProcessRunning(exe,out var processMessage);
            for(var i=0;i<2;i++){
                var snapshot=snapshots[i];var service=i==0?mfgUnlockSettings:xeMfgSettings;var rows=i==0?mfgSettingRows:xeMfgSettingRows;
                var draft = mfgDrafts.GetValueOrDefault(exe)?.FirstOrDefault(d =>
                    d.AddonHash == identity.Hash && d.ContentHash == snapshot.ContentSha256 &&
                    d.CatalogIdentity == service.ConfigFileName + "|" + snapshot.CatalogIdentity);
                foreach(var setting in snapshot.Settings){
                    var saved=setting.IsExplicitlySet?(setting.IsValid?setting.RawValue!:$"Invalid value · {setting.RawValue}"):$"Not set · addon default {setting.EffectiveValue}";
                    var proposed = setting.IsValid ? setting.EffectiveValue : null;
                    if (setting.IsValid && draft?.Changes.TryGetValue(setting.Definition.Key, out var edit) == true &&
                        IsProfileValueValid(setting.Definition, edit))
                        proposed = edit;
                    rows.Add(new MfgUnlockSettingRow(setting.Definition,saved,proposed,snapshot.IsSupportedAddonVersion&&setting.IsValid&&closed,QueueMfgEditUpdate){State=setting,Group=mfgSettingsGroups.For(service.ConfigFileName,setting.Definition),IsExpanded=settingsExpansion.GetValueOrDefault(exe+"|"+setting.Definition.Key),Details=SettingDetails(setting.Definition,service.ConfigFileName,service.SectionFor(setting.Definition)),HelpSections=MainWindow.HelpSections(setting.Definition,service.ConfigFileName,service.SectionFor(setting.Definition))});
                }
                mfgSettingsDocuments.Add(new(service,snapshot,rows));
                foreach(var row in rows) row.IsFavorite = IsMfgFavorite(row);
                
                var isXeDocument=i==1;SetMfgText(isXeDocument?XeMfgConfigText:MfgSettingsConfigText,l=>l.Format("{0} · {1} · configuration SHA-256 {2}",snapshot.ConfigPath,l.Lookup(snapshot.ConfigExists?"saved file found":"file not found; defaults only"),snapshot.ContentSha256)+(isXeDocument&&!snapshot.IsSupportedAddonVersion?"\n"+l.Lookup("This addon version has no reviewed XeMFG settings schema. Known keys remain visible; editing is disabled."):""));
            }
            SetMfgText(MfgSettingsStatusText,l=>!closed?l.Lookup(processMessage)+" "+l.Lookup("Close the game before editing settings."):!snapshots[0].IsSupportedAddonVersion?l.Format("Detected addon version {0}. Editing is disabled for this configuration.",TechnicalVersionDisplay.Format(identity.Version)):l.Format("MFG Unlock {0} · saved configuration values only. Changes apply after restarting the game. Current runtime state cannot be read here.",TechnicalVersionDisplay.Format(identity.Version)));
            LocalizedPresentation.RefreshGeneratedItems(MfgSettingsPage);UpdateMfgSettingsButtons();
        }catch(Exception ex){if(revision!=mfgLoadRevision)return;mfgSettingsSnapshot=null;mfgSettingsDocuments.Clear();mfgSettingRows.Clear();xeMfgSettingRows.Clear();SetMfgText(MfgSettingsStatusText,l=>l.Lookup("Settings are read-only because validation failed:")+" "+MfgSettingsDisplay.Error(ex.Message,l));SetMfgText(MfgSettingsConfigText,"No configuration loaded · settings remain read-only.");SetMfgText(XeMfgConfigText,"No configuration loaded · settings remain read-only.");UpdateMfgSearchPresentation();}
    }

    private void UpdateMfgSettingsButtons()
    {
        UpdateGenerationMode();
        UpdateCatalogEditors();
        UpdateMfgCategoryPresentation();
        bool Dirty(MfgSettingsDocument doc)=>doc.Snapshot.IsSupportedAddonVersion&&doc.Rows.Any(row=>row.ProposedValue is not null&&doc.Snapshot.Settings.First(s=>s.Definition.Key==row.Definition.Key).EffectiveValue!=row.ProposedValue);
        var safeContext=mfgSettingsDocuments.Count>0&&mfgSettingsSnapshot?.IsSupportedAddonVersion==true&&mfgSettingsExePath==selectedExe&&HasCurrentExecutable()&&!AnyFileOperationBusy&&!IsSelectedGameProcessRunning(mfgSettingsExePath!,out _);
        var supported=safeContext&&mfgSettingsDocuments.Where(d=>d.Snapshot.IsSupportedAddonVersion).All(d=>d.Snapshot.Settings.All(s=>s.IsValid)&&d.Rows.All(r=>r.ProposedValue is not null&&r.InputValid));
        SetMfgText(MfgRestartStatusText,mfgSettingsDocuments.Any(Dirty)?"Unsaved changes":"Changes apply after restarting the game.");
        var xmxConflict=PendingNvidiaEditsBlockedByXmx();
        MfgSettingsSaveButton.IsEnabled=supported&&!xmxConflict&&mfgSettingsDocuments.Any(Dirty);MfgSettingsDiscardButton.IsEnabled=safeContext&&mfgSettingsDocuments.Any(Dirty);
        MfgSettingsSaveButton.ToolTip=LocalizedPresentation.Language.Lookup(xmxConflict?PendingXmxConflict:"Only settings for the confirmed installed addon version can be changed.");ToolTipService.SetShowOnDisabled(MfgSettingsSaveButton,true);
        if(xmxConflict)SetMfgText(MfgSettingsStatusText,PendingXmxConflict);
        else if(HasMfgText(MfgSettingsStatusText,PendingXmxConflict))SetMfgText(MfgSettingsStatusText,"Saved values are not live runtime state. Close the game before editing. No performance gain is guaranteed.");
        MfgSettingsItemsControl.IsEnabled=!mfgSettingsBusy;XeMfgSettingsItemsControl.IsEnabled=!mfgSettingsBusy;
    }

    private static bool IsSelectedGameProcessRunning(string exePath, out string message)
    {
        var processName = Path.GetFileNameWithoutExtension(exePath);
        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var runningPath = process.MainModule?.FileName;
                        if (runningPath is null || Path.GetFullPath(runningPath).Equals(Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                        {
                            message = "The selected game appears to be running.";
                            return true;
                        }
                    }
                    catch
                    {
                        message = "A process with the selected game's executable name is running, but its path could not be verified.";
                        return true;
                    }
                }
            }
            message = string.Empty;
            return false;
        }
        catch
        {
            message = "Could not confirm whether the selected game is closed.";
            return true;
        }
    }

    private async void MfgSettingsReload_Click(object sender,RoutedEventArgs e){if(!AnyFileOperationBusy)await LoadMfgSettingsAsync();}
    private void SettingsEditor_PreviewInput(object sender,InputEventArgs e)
    {
        if(AnyFileOperationBusy||mfgSettingsExePath is null||mfgSettingsExePath!=selectedExe||!HasCurrentExecutable()||IsSelectedGameProcessRunning(mfgSettingsExePath,out _)){
            e.Handled=true;SetMfgText(MfgSettingsStatusText,"Close the game and reload the confirmed addon before saving.");UpdateMfgSettingsButtons();
        }
    }
    private void MfgSettingsDiscard_Click(object sender,RoutedEventArgs e)
    {
        if(AnyFileOperationBusy)return;
        foreach(var doc in mfgSettingsDocuments)foreach(var row in doc.Rows){var setting=doc.Snapshot.Settings.First(s=>s.Definition.Key==row.Definition.Key);row.ProposedValue=setting.IsValid?setting.EffectiveValue:null;}
        SetMfgText(MfgSettingsStatusText,"Unsaved changes discarded. Values shown are saved configuration values, not live runtime state.");UpdateMfgSettingsButtons();
    }
    private async void MfgSettingsSave_Click(object sender,RoutedEventArgs e)
    {
        if(PendingNvidiaEditsBlockedByXmx()){SetMfgText(MfgSettingsStatusText,PendingXmxConflict);return;}
        if(AnyFileOperationBusy||mfgSettingsSnapshot is null||mfgSettingsExePath is null||mfgSettingsAddonHash is null||mfgSettingsExePath!=selectedExe||!HasCurrentExecutable())return;
        var exe=mfgSettingsExePath;var addon=Path.Combine(Path.GetDirectoryName(exe)!,"renodx-mfgunlock.addon64");var addonHash=mfgSettingsAddonHash;var root=mfgSettingsSnapshot.AllowedRoot;
        var gameName = (GamesCombo.SelectedItem as Game)?.Name ?? Path.GetFileName(exe);
        bool CanWrite(){try{return !IsSelectedGameProcessRunning(exe,out _)&&File.Exists(addon)&&(File.GetAttributes(addon)&FileAttributes.ReparsePoint)==0&&MfgUnlockSettingsService.GetSha256(File.ReadAllBytes(addon))==addonHash;}catch{return false;}}
        var edits=PendingMfgEdits();
        if(edits.Length==0)return;
        var details=string.Join("\n",edits.SelectMany(e=>e.Changes.Select(c=>e.Doc.Service.ConfigFileName+" · ["+e.Doc.Service.SectionFor(e.Doc.Snapshot.Settings.First(s=>s.Definition.Key==c.Key).Definition)+"] "+c.Key+": "+e.Doc.Snapshot.Settings.First(s=>s.Definition.Key==c.Key).EffectiveValue+" → "+c.Value)));
        if(!CanWrite()){SetMfgText(MfgSettingsStatusText,"Close the game and reload the confirmed addon before saving.");return;}
        if(!ShowThemeConfirmation("Save MFG Unlock settings?",details+"\n\n"+"Original configuration files are backed up together. Restart the game to apply saved values."))return;
        mfgSettingsBusy=true;UpdateMfgSettingsButtons();
        try{
            var files=await Task.Run(()=>edits.Select(e=>e.Doc.Service.PrepareTransaction(e.Doc.Snapshot,e.Changes,CanWrite)).ToArray());
            if(!CanWrite()||selectedExe!=exe||!HasCurrentExecutable())throw new IOException("Close the game and reload the confirmed addon before saving.");
            var result=await CreateBackupService(root).ExecuteAsync(root,files);
            var recorded = RecordMfgHistory(exe, gameName, result.TransactionId, edits);
            await LoadMfgSettingsAsync();SetMfgText(MfgSettingsStatusText,"Settings saved and original data backed up · transaction {0}. Fully restart the game to apply; runtime state was not inspected.",result.TransactionId);
            if (!recorded) ShowThemeDialog("MFG settings history", "Settings were saved. The local history could not be updated.", true);
        }catch(Exception ex){SetMfgText(MfgSettingsStatusText,l=>l.Lookup("Settings were not saved, or the transaction rolled back:")+" "+MfgSettingsDisplay.Error(ex.Message,l));ShowThemeDialog("Could not save MFG Unlock settings",MfgSettingsDisplay.Error(ex.Message,LocalizedPresentation.Language),true);}
        finally{mfgSettingsBusy=false;UpdateMfgSettingsButtons();}
    }

    private async void RecommendedSetup_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCurrentExecutable() || selectedGameRoot is null)
        {
            ShowThemeDialog("Select a game", "Choose the game and its executable on Home before reviewing setup.");
            return;
        }
        var exe = selectedExe!;
        var root = selectedGameRoot;
        var game = GamesCombo.SelectedItem as Game;
        try
        {
            var scan = await Task.Run(() => nvidiaComponentScanner.Scan(exe, root));
            if (selectedExe != exe || selectedGameRoot != root) return;
            var addon = Path.Combine(Path.GetDirectoryName(exe)!, "renodx-mfgunlock.addon64");
            var version = File.Exists(addon) ? FileVersionInfo.GetVersionInfo(addon).FileVersion ?? "" : "";
            var report = new RecommendedSetupService().Assess(scan, version, IsSelectedGameProcessRunning(exe, out _));
            var window = new Window { Title = "Recommended Setup", Owner = this, Width = 800, Height = 620,
                MinWidth = 600, MinHeight = 400, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResizeWithGrip, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), Icon = Icon };
            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = $"Recommended Setup · {report.Status}\n{game?.Name}\n{exe}\n{report.Summary}", TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            var content = new StackPanel();
            foreach (var finding in report.Findings)
                content.Children.Add(new TextBlock { Text = finding, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) });
            var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), FindResource("PremiumScrollBar"));
            Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
            var buttons = new WrapPanel();
            void Add(string label, Action action) { var button = new Button { Content = label, Margin = new Thickness(0, 4, 8, 0), Padding = new Thickness(10, 8, 10, 8) }; button.Click += (_, _) => { window.Close(); action(); }; buttons.Children.Add(button); }
            Add("Components", () => { SetPage(AppPage.NvidiaComponents); SetNvidiaSubpage(NvidiaSubpage.Components); currentNvidiaScanResult = scan; ApplyNvidiaScanResultDisplay(); ApplyCompatibilityAssessment(scan); });
            Add("SDKs", () => NvidiaPackages_Click(this, new RoutedEventArgs()));
            Add("Settings", () => MfgSettings_Click(this, new RoutedEventArgs()));
            Add("Restore", () => InstallPreviewRestore_Click(this, new RoutedEventArgs()));
            var apply = new Button { Content = "Apply Recommended Setup", IsEnabled = false, ToolTip = "No reviewed production operation profile. Review does not authorize writes.", Margin = new Thickness(0,4,0,0) };
            buttons.Children.Add(apply);
            Add("Close", () => { });
            Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
            window.Content = grid; window.ShowDialog();
        }
        catch (Exception ex) { ShowThemeDialog("Setup review failed", ex.Message, true); }
    }
    private enum AppPage { Home, About, NvidiaComponents, MfgSettings, DllInstallation, AdvancedRuntime, ReShade }
    private enum NvidiaSubpage { Components, StreamlineSdks, InstallComponents }

    private void UpdateNvidiaComponentContext()
    {
        UpdateReShadeContext();
        if(manualDllView is not null){SyncManualNvidiaContext();RenderManualNvidia();}
        if (NvidiaScanButton is null) return;
        var validExe = HasCurrentExecutable();
        currentNvidiaScanResult = null;
        ShowBackupCopiesCheckBox.IsChecked = false;
        ShowBackupCopiesCheckBox.IsEnabled = false;
        NvidiaGamePathText.Text = selectedGameRoot ?? (GamesCombo.SelectedItem is Game ? "Game folder is unavailable" : "No Steam game selected · browse to an EXE on Home");
        NvidiaScanRootText.Text = validExe ? "Scan root: " + (selectedGameRoot ?? Path.GetDirectoryName(selectedExe!)) : "Scan root: not selected";
        NvidiaScanButton.IsEnabled = validExe;
        NvidiaScanButton.Content = "Scan";
        NvidiaComponentsItemsControl.ItemsSource = null;
        NvidiaResultsSummaryText.Text = "Select a game and Scan to inspect file versions and locations. Disk presence does not prove runtime activity.";NvidiaInventoryPanel.VerticalAlignment=VerticalAlignment.Top;NvidiaInventoryPanel.MaxHeight=220;
        NvidiaScanStatusText.Text = validExe ? "Ready to scan the selected executable folder and its subfolders." : "Choose a game and executable on Home, then scan.";
        ApplyCompatibilityAssessment(null);
    }

    private async void NvidiaScan_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCurrentExecutable()) { UpdateNvidiaComponentContext(); return; }
        var scannedExe = selectedExe!;
        NvidiaScanButton.IsEnabled = false;
        currentNvidiaScanResult = null;
        ShowBackupCopiesCheckBox.IsChecked = false;
        ShowBackupCopiesCheckBox.IsEnabled = false;
        NvidiaComponentsItemsControl.ItemsSource = null;
        NvidiaResultsSummaryText.Text = "Scanning…";
        NvidiaScanStatusText.Text = "Scanning selected game folder…";
        try
        {
            var scanRoot = selectedGameRoot;
            var result = await Task.Run(() => nvidiaComponentScanner.Scan(scannedExe, scanRoot));
            if (!string.Equals(selectedExe, scannedExe, StringComparison.OrdinalIgnoreCase) || !HasCurrentExecutable())
            {
                UpdateNvidiaComponentContext();
                return;
            }
            if (result.Error is not null)
            {
                NvidiaScanStatusText.Text = result.Error;
                return;
            }
            currentNvidiaScanResult = result;
            ApplyNvidiaScanResultDisplay();
            ApplyCompatibilityAssessment(result);
            NvidiaScanButton.Content = "Rescan";
            var scanSummary = $"Scanned {UiNumber(result.ScannedDirectories)} folders · {UiNumber(result.FoundCopies)} component file(s) found";
            if (result.PotentialConflicts.Count > 0) scanSummary += $" · {UiNumber(result.PotentialConflicts.Count)} possible duplicate/conflict group(s)";
            if (result.Warnings.Count > 0) scanSummary += $" · {UiNumber(result.Warnings.Count)} access/version warning(s)";
            NvidiaScanStatusText.Text = scanSummary;
            if (result.PotentialConflicts.Count > 0) NvidiaScanStatusText.ToolTip = string.Join(Environment.NewLine, result.PotentialConflicts);
            else NvidiaScanStatusText.ToolTip = null;
            Log("NVIDIA component scan completed: " + scanSummary);
        }
        catch (OperationCanceledException)
        {
            NvidiaScanStatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            NvidiaScanStatusText.Text = "Scan failed: " + ex.Message;
            Log("NVIDIA component scan failed: " + ex.Message);
        }
        finally
        {
            NvidiaScanButton.IsEnabled = HasCurrentExecutable();
        }
    }

    private void BackupCopiesToggle_Changed(object sender, RoutedEventArgs e) => ApplyNvidiaScanResultDisplay();

    private void ApplyNvidiaScanResultDisplay()
    {
        if (currentNvidiaScanResult is not { Error: null } result) return;
        var showBackups = ShowBackupCopiesCheckBox.IsChecked == true;
        var display = NvidiaComponentDisplay.Create(result, showBackups);
        NvidiaComponentsItemsControl.ItemsSource = display.VisibleEntries;
        LocalizedPresentation.RefreshGeneratedItems(NvidiaComponentsItemsControl);
        ShowBackupCopiesCheckBox.IsEnabled = display.BackupFileCount > 0;
        NvidiaResultsSummaryText.Text = display.Summary;NvidiaInventoryPanel.VerticalAlignment=VerticalAlignment.Stretch;NvidiaInventoryPanel.MaxHeight=double.PositiveInfinity;
    }

    private void ApplyCompatibilityAssessment(NvidiaComponentScanResult? scan)
    {
        var game = GamesCombo.SelectedItem as Game;
        var executableName = selectedExe is null ? null : Path.GetFileName(selectedExe);
        string? version = null;
        if (selectedExe is not null && File.Exists(selectedExe))
        {
            try { version = FileVersionInfo.GetVersionInfo(selectedExe).ProductVersion ?? FileVersionInfo.GetVersionInfo(selectedExe).FileVersion; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) { }
        }
        string? api = null;
        if (selectedExe is not null && string.Equals(Path.GetFileName(Path.GetDirectoryName(selectedExe)), "x64_dx12", StringComparison.OrdinalIgnoreCase))
            api = "DirectX 12";
        var fingerprint = new GameConfigurationFingerprint(game?.AppId, game?.BuildId, executableName, version, api);
        var report = gameCompatibilityEvaluator.Evaluate(fingerprint, scan);
        var featureStatus = report.MfgSupport.ToString();
        var integrationStatus = report.IntegrationCompatibility.ToString();
        var overrideStatus = report.Profile?.TechnologySupport.GetValueOrDefault("NVIDIA App MFG override for this exact executable", TechnologySupportStatus.Unknown).ToString() ?? "Unknown";
        NvidiaCompatibilityStatusText.Text = $"{report.ProfileLabel} · {FormatCompatibilityLabel(report.EvidenceStatus.ToString())} · {FormatCompatibilityLabel(report.RecommendedAction.ToString())}";
        NvidiaCompatibilityDetailsText.Text = $"{report.ConfigurationLabel}\nGame MFG support: {FormatCompatibilityLabel(featureStatus)} · integration: {FormatCompatibilityLabel(integrationStatus)} · NVIDIA App override for this EXE: {FormatCompatibilityLabel(overrideStatus)}\n{report.Summary}\n{report.Details}";
        if(report.Profile is null) {
            NvidiaCompatibilityStatusText.Text="Automatic / Verified: no reviewed profile. Manual / Advanced uses independent safety checks.";
            NvidiaCompatibilityDetailsText.Text=report.ConfigurationLabel+"\nA missing Automatic profile does not block Manual / Advanced by itself. No game compatibility is certified. Preview and explicit approval are still required.";
        }
        var evidenceTooltip = report.Profile is null ? "" : "\n\nEvidence:\n" + string.Join("\n", report.Profile.Evidence.Select(item => $"[{item.CheckedOn:yyyy-MM-dd}] {item.Claim} · {item.Source}"));
        profileDetails = LocalizedPresentation.SourceText(NvidiaCompatibilityDetailsText,TextBlock.TextProperty) + evidenceTooltip;
        NvidiaCompatibilityStatusText.Text=report.Profile is null?"No reviewed automatic profile":report.ProfileLabel+" · "+string.Join(" · ",new[]{FormatCompatibilityLabel(report.EvidenceStatus.ToString()),FormatCompatibilityLabel(report.RecommendedAction.ToString())}.Distinct(StringComparer.OrdinalIgnoreCase));
        NvidiaCompatibilityDetailsText.Text=report.ConfigurationLabel+"\n"+(report.Profile is null?"Manual mode uses separate checks; a missing profile is not an installation failure.":"Read-only compatibility assessment. Open Profile details for evidence and limitations.");
        NvidiaCompatibilityDetailsText.ToolTip=null;
        NvidiaCompatibilityStatusText.ToolTip="Open Profile details for full compatibility evidence.";
    }

    private static string FormatCompatibilityLabel(string label) => label=="NeedsReview"?"Needs review":Regex.Replace(label, "([a-z])([A-Z])", "$1 $2");

    private void NvidiaStreamlineReleases_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/NVIDIA-RTX/Streamline/releases");

    private void DeveloperProfile_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/ExiZz1");
    private void OriginalAuthor_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/mavismmg");
    private void SourceRepository_Click(object sender, RoutedEventArgs e) => OpenExternalLink("https://github.com/mavismmg/MFGAdaUnlock-RenoDx");

    private void OpenExternalLink(string url)
    {
        if(!EvidenceLinkPolicy.TryGetHttpUri(url,out _)){ShowThemeDialog("Couldn't open link","Only valid HTTP/HTTPS links can be opened.",true);return;}
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowThemeDialog("Couldn't open link", "The link could not be opened in your browser. You can copy this address and open it manually:\n\n" + url + "\n\n" + ex.Message, isError: true); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadReleasesAsync();
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (visibleReleases is not null) FilterReleases(); }
    private void ReleasesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (InstallButton is null) return;
        var release = ReleasesList.SelectedItem as Release;
        SelectedReleaseText.Text = release is null ? "Select a release to continue" : $"{release.DisplayTag}  ·  {release.AssetName}";
        UpdateInstallButtonAvailability();
    }

    private void ReleaseNotesButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent the list item from changing the selected install target when its info affordance is clicked.
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Release release) ShowReleaseNotesDialog(release);
    }

    private void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Release release) ShowReleaseNotesDialog(release);
    }

    private void ShowReleaseNotesDialog(Release release)
    {
        var window = new Window
        {
            Title = $"Release notes · {release.DisplayTag}", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Icon = Icon,
            Width = Math.Min(760, Math.Max(430, ActualWidth * .76)),
            Height = Math.Min(680, Math.Max(390, ActualHeight * .78))
        };
        var shell = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 27)), BorderBrush = new SolidColorBrush(Color.FromRgb(57, 52, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(22) };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headingStack = new StackPanel();
        headingStack.Children.Add(new TextBlock { Text = "RELEASE NOTES", Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250)), FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        headingStack.Children.Add(new TextBlock { Text = release.Name, Foreground = new SolidColorBrush(Color.FromRgb(244, 242, 250)), FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 });
        LocalizedPresentation.SetPreserveOriginal(headingStack.Children[1],true);
        var close = new Button { Content = "×", Width = 32, Height = 30, Padding = new Thickness(0), FontSize = 17, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(160, 157, 171)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Close" };
        close.Click += (_, _) => window.Close();
        header.Children.Add(headingStack); Grid.SetColumn(close, 1); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);

        var metadata = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        metadata.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(41, 35, 58)), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 4, 9, 4), Child = new TextBlock { Text = release.DisplayTag, Foreground = new SolidColorBrush(Color.FromRgb(201, 185, 255)), FontSize = 11, FontWeight = FontWeights.Bold } });
        metadata.Children.Add(new TextBlock { Text = release.PublishedAt is { Length: > 0 } ? $"Published {release.PublishedAt}" : "Publication date unavailable", Foreground = new SolidColorBrush(Color.FromRgb(147, 145, 160)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        Grid.SetRow(metadata, 1); layout.Children.Add(metadata);

        var notesViewer = new FlowDocumentScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsToolBarVisible = false, Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 14), Focusable = true };
        notesViewer.Resources.Add(typeof(ScrollBar), FindResource("PremiumScrollBar"));
        Grid.SetRow(notesViewer, 2); layout.Children.Add(notesViewer);

        var footer = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var retry = new Button { Content = "Retry loading", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        retry.Click += async (_, _) => await RetryReleaseNotesAsync(release, notesViewer, retry);
        var github = new Button { Content = "Open on GitHub ↗", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), Visibility = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releaseUri) && releaseUri.Scheme == Uri.UriSchemeHttps ? Visibility.Visible : Visibility.Collapsed };
        github.Click += (_, _) => { if (release.HtmlUrl is not null) OpenExternalLink(release.HtmlUrl); };
        var done = new Button { Content = "Close", MinWidth = 92, Padding = new Thickness(16, 8, 16, 8), Background = new SolidColorBrush(Color.FromRgb(112, 75, 208)), BorderBrush = new SolidColorBrush(Color.FromRgb(142, 111, 240)) };
        done.Click += (_, _) => window.Close();
        footer.Children.Add(retry); Grid.SetColumn(github, 1); footer.Children.Add(github); Grid.SetColumn(done, 2); footer.Children.Add(done);
        Grid.SetRow(footer, 3); layout.Children.Add(footer);

        shell.Child = layout; window.Content = shell;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        if (releaseNotesCache.TryGetValue(release.Tag, out var cachedNotes)) notesViewer.Document = BuildReleaseNotesDocument(cachedNotes);
        else if (release.Body is not null) { releaseNotesCache[release.Tag] = release.Body; notesViewer.Document = BuildReleaseNotesDocument(release.Body); }
        else
        {
            notesViewer.Document = BuildReleaseNotesDocument(LocalizedPresentation.Language.Translate("Release notes were not included in the release list. Try loading them again."));
            retry.Visibility = Visibility.Visible;
        }
        window.ShowDialog();
    }

    private async Task RetryReleaseNotesAsync(Release release, FlowDocumentScrollViewer viewer, Button retry)
    {
        if (releaseNotesCache.TryGetValue(release.Tag, out var cached)) { viewer.Document = BuildReleaseNotesDocument(cached); retry.Visibility = Visibility.Collapsed; return; }
        if (string.IsNullOrWhiteSpace(release.ApiUrl) || !Uri.TryCreate(release.ApiUrl, UriKind.Absolute, out var apiUri) || apiUri.Scheme != Uri.UriSchemeHttps)
        {
            viewer.Document = BuildReleaseNotesDocument(LocalizedPresentation.Language.Translate("Release notes could not be loaded because the release API address is unavailable. Try Refresh on the main screen."));
            retry.Content = "Retry loading"; retry.IsEnabled = true; return;
        }
        retry.IsEnabled = false; retry.Content = "Loading…";
        viewer.Document = BuildReleaseNotesDocument(LocalizedPresentation.Language.Translate("Loading release notes…"));
        try
        {
            using var response = await Http.GetAsync(apiUri);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var notes = json.RootElement.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
            releaseNotesCache[release.Tag] = notes;
            viewer.Document = BuildReleaseNotesDocument(notes);
            retry.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            viewer.Document = BuildReleaseNotesDocument("Could not load release notes. Check your connection and try again.\n\n" + ex.Message);
            retry.Content = "Retry loading"; retry.IsEnabled = true; retry.Visibility = Visibility.Visible;
        }
    }

    private FlowDocument BuildReleaseNotesDocument(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(196, 193, 207)), LineHeight = 20 };
        if (string.IsNullOrWhiteSpace(markdown))
        {
            document.Blocks.Add(new Paragraph(new Run(LocalizedPresentation.Language.Translate("No release notes were published for this version."))) { Foreground = new SolidColorBrush(Color.FromRgb(147, 145, 160)), FontStyle = FontStyles.Italic, Margin = new Thickness(0, 10, 0, 0) });
            return document;
        }
        var inCodeBlock = false; var codeLines = new List<string>();
        foreach (var originalLine in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = originalLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    var code = new Paragraph(new Run(string.Join("\n", codeLines)) { FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(218, 210, 239)) }) { Background = new SolidColorBrush(Color.FromRgb(14, 16, 21)), Padding = new Thickness(12), Margin = new Thickness(0, 4, 0, 10) };
                    document.Blocks.Add(code); codeLines.Clear(); inCodeBlock = false;
                }
                else inCodeBlock = true;
                continue;
            }
            if (inCodeBlock) { codeLines.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (Regex.IsMatch(line.Trim(), "^(---+|___+|\\*\\*\\*+)$"))
            {
                document.Blocks.Add(new Paragraph(new Run("────────────────────────────────────────")) { Foreground = new SolidColorBrush(Color.FromRgb(63, 57, 76)), Margin = new Thickness(0, 2, 0, 8) });
                continue;
            }
            var heading = Regex.Match(line, "^(#{1,6})\\s+(.+)$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                var paragraph = new Paragraph { Margin = new Thickness(0, level == 1 ? 12 : 8, 0, 5), FontSize = level <= 2 ? 16 : 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(226, 218, 244)) };
                AppendMarkdownInline(paragraph, heading.Groups[2].Value); document.Blocks.Add(paragraph); continue;
            }
            var bullet = Regex.Match(line, "^\\s*[-*+]\\s+(.+)$");
            var numbered = Regex.Match(line, "^\\s*(\\d+)[.)]\\s+(.+)$");
            if (bullet.Success || numbered.Success)
            {
                var paragraph = new Paragraph { Margin = new Thickness(18, 0, 0, 5), TextIndent = -16 };
                paragraph.Inlines.Add(new Run(bullet.Success ? "•  " : numbered.Groups[1].Value + ".  ") { Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250)), FontWeight = FontWeights.Bold });
                AppendMarkdownInline(paragraph, bullet.Success ? bullet.Groups[1].Value : numbered.Groups[2].Value); document.Blocks.Add(paragraph); continue;
            }
            if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
            {
                var paragraph = new Paragraph { Margin = new Thickness(12, 0, 0, 8), FontStyle = FontStyles.Italic };
                paragraph.Inlines.Add(new Run("│  ") { Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250)), FontWeight = FontWeights.Bold });
                var quote=line.TrimStart().TrimStart('>').TrimStart();var admonition=Regex.Match(quote,@"^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]$");
                AppendMarkdownInline(paragraph,admonition.Success?admonition.Groups[1].Value:quote); document.Blocks.Add(paragraph); continue;
            }
            var normal = new Paragraph { Margin = new Thickness(0, 0, 0, 8), TextAlignment = TextAlignment.Left };
            AppendMarkdownInline(normal, line); document.Blocks.Add(normal);
        }
        if (inCodeBlock && codeLines.Count > 0)
            document.Blocks.Add(new Paragraph(new Run(string.Join("\n", codeLines)) { FontFamily = new FontFamily("Consolas") }) { Background = new SolidColorBrush(Color.FromRgb(14, 16, 21)), Padding = new Thickness(12), Margin = new Thickness(0, 4, 0, 10) });
        return document;
    }

    private void AppendMarkdownInline(Paragraph paragraph, string text)
    {
        var pattern = @"(\[(?<label>[^\]]+)\]\((?<url>https?://[^)\s]+)\)|\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|(?<!\*)\*(?<italic>[^*]+)\*(?!\*))";
        var cursor = 0;
        foreach (Match match in Regex.Matches(text, pattern))
        {
            if (match.Index > cursor) paragraph.Inlines.Add(new Run(text[cursor..match.Index]));
            if (match.Groups["label"].Success)
            {
                if (Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    var link = new Hyperlink(new Run(match.Groups["label"].Value)) { NavigateUri = uri, Foreground = new SolidColorBrush(Color.FromRgb(191, 169, 255)), TextDecorations = TextDecorations.Underline };
                    link.RequestNavigate += (_, e) => { OpenExternalLink(e.Uri.AbsoluteUri); e.Handled = true; };
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(new Run(match.Groups["label"].Value));
            }
            else if (match.Groups["bold"].Success) paragraph.Inlines.Add(new Run(match.Groups["bold"].Value) { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(232, 228, 242)) });
            else if (match.Groups["code"].Success) paragraph.Inlines.Add(new Run(match.Groups["code"].Value) { FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(211, 194, 255)), Background = new SolidColorBrush(Color.FromRgb(35, 31, 44)) });
            else if (match.Groups["italic"].Success) paragraph.Inlines.Add(new Run(match.Groups["italic"].Value) { FontStyle = FontStyles.Italic });
            cursor = match.Index + match.Length;
        }
        if (cursor < text.Length) paragraph.Inlines.Add(new Run(text[cursor..]));
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (AnyFileOperationBusy || ReleasesList.SelectedItem is not Release release) return;
        if (!HasCurrentExecutable())
        {
            ShowThemeDialog("Choose the current game's EXE", "Select an executable from the currently selected game before installing the addon.", isError: true);
            return;
        }
        if (IsSelectedGameProcessRunning(selectedExe!, out var processMessage))
        { ShowThemeDialog("Close the game", processMessage, true); return; }
        var targetExe = selectedExe!;
        var targetRoot = selectedGameRoot!;
        var target = Path.Combine(Path.GetDirectoryName(targetExe)!, "renodx-mfgunlock.addon64");
        var originalExists = false;
        string? originalHash = null;
        var temp = Path.Combine(Path.GetTempPath(), "renodx-addon-" + Guid.NewGuid().ToString("N") + ".download");
        addonInstallBusy = true; InstallButton.IsEnabled = false;
        try
        {
            void CheckAddonCopies()
            {
                if(RuntimeDiagnosticsService.FindAddonCopies(targetRoot, targetExe)
                    .Any(name => !name.Equals(Path.GetFileName(target), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Another MFG Unlock addon file exists beside the selected EXE. Installing here could load two copies.");
            }
            CheckAddonCopies();
            using var transfer = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            originalExists = File.Exists(target); originalHash = originalExists ? await StreamlineDigestVerifier.ComputeSha256Async(target) : null;
            SetStatus("Downloading and validating addon…"); Log($"Downloading {release.Tag}…");
            using var response = await Http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, transfer.Token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(transfer.Token))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, transfer.Token)) != 0)
                {
                    if (output.Length + count > 100 * 1024 * 1024) throw new InvalidDataException("The downloaded addon has an unexpected file size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), transfer.Token);
                }
            }
            var info = new FileInfo(temp);
            if (info.Length < 4096 || info.Length > 100 * 1024 * 1024) throw new InvalidDataException("The downloaded addon has an unexpected file size.");
            await using (var fs = File.OpenRead(temp))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') throw new InvalidDataException("The downloaded file is not a valid Windows addon binary.");
            }
            if (!File.Exists(targetExe) || !string.Equals(selectedExe, targetExe, StringComparison.OrdinalIgnoreCase) || !IsExeForCurrentGame(targetExe))
                throw new InvalidOperationException("The game selection changed during download. The active addon was not replaced; select the game and try again.");
            // Install validated bytes through the existing authenticated backup transaction.
            if (IsSelectedGameProcessRunning(targetExe, out var runningMessage)) throw new IOException(runningMessage);
            if (!ShowThemeConfirmation("Install addon?", $"Install {release.Name} ({release.DisplayTag}) to:\n{target}\n\nExisting bytes will be backed up. File placement does not confirm runtime operation.")) return;
            var bytes = await File.ReadAllBytesAsync(temp);
            if (IsSelectedGameProcessRunning(targetExe, out _) || selectedExe != targetExe || selectedGameRoot != targetRoot)
                throw new IOException("Game state changed before installation.");
            CheckAddonCopies();
            var transaction = await CreateBackupService(targetRoot).ExecuteAsync(targetRoot,
                [new BackupTransactionFile(Path.GetRelativePath(targetRoot, target), bytes, originalExists, originalHash)]);
            Log("Addon backup/install transaction: " + transaction.TransactionId);
            SetStatus($"Installed {release.DisplayTag} successfully"); Log($"Installed and validated {release.AssetName} next to the game executable. Original files are protected by a restore transaction.");
            ShowThemeDialog("Installation complete", $"{release.Name} ({release.DisplayTag}) was installed next to the selected game's executable.\n\nRestart the game to load the addon.");
        }
        catch (Exception ex)
        {
            SetStatus("Installation failed · active addon was left untouched"); Log("Install failed: " + ex.Message);
            ShowThemeDialog("Installation failed", "The addon could not be installed. The active addon was left untouched.\n\n" + ex.Message, isError: true);
        }
        finally { addonInstallBusy = false; try { if (File.Exists(temp)) File.Delete(temp); } catch { } UpdateInstallButtonAvailability(); }
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
        var messageScroll = new ScrollViewer {Content=body,MaxHeight=Math.Max(160,SystemParameters.WorkArea.Height-240),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};messageScroll.Resources.Add(typeof(ScrollBar),FindResource("PremiumScrollBar"));Grid.SetRow(messageScroll,1);layout.Children.Add(messageScroll);
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
    private void ClearLog_Click(object sender, RoutedEventArgs e) { activityLogSource.Clear(); LogText.Text = ""; }
    private void Log(string message) { activityLogSource.Append($"[{DateTime.Now:HH:mm:ss}] {message}\n"); LogText.Text = activityLogSource.ToString(); }
    private void SetStatus(string message) => StatusText.Text = message;
    private void SetBusy(bool busy, string? message) { if (message is not null) SetStatus(message); Mouse.OverrideCursor = busy ? Cursors.Wait : null; }
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if(e.ClickCount==2){MaximizeRestore_Click(sender,e);return;}if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class MfgUnlockSettingRow : INotifyPropertyChanged
    {
        private readonly Action onChanged;
        private string? proposedValue;
        public MfgUnlockSettingRow(MfgUnlockSettingDefinition definition, string currentDisplay, string? proposedValue, bool canEdit, Action onChanged)
        {
            Definition = definition;
            this.currentDisplay = currentDisplay;
            DisplayChoices=definition.Choices.Select(c=>new MfgSettingChoiceDisplay(c,LocalizedPresentation.Language)).ToArray();
            this.proposedValue = proposedValue;
            BaseCanEdit=canEdit;CanEdit = canEdit;
            this.onChanged = onChanged;
        }
        public MfgUnlockSettingDefinition Definition { get; }
        private readonly string currentDisplay;
        public MfgUnlockSettingState? State {get;init;}
        public string CurrentDisplay=>State is {} s?MfgSettingsDisplay.Saved(s,LocalizedPresentation.Language):LocalizedPresentation.Language.Lookup(currentDisplay);
        public IReadOnlyList<MfgSettingChoiceDisplay> DisplayChoices {get;}
        private MfgSettingAvailability availability=new(false,"Unavailable: reload the confirmed addon and configuration before editing.");
        public string UnavailableMessage=>availability.Message(LocalizedPresentation.Language);
        public void SetAvailability(MfgSettingAvailability value){availability=value;CanEdit=value.CanEdit;PropertyChanged?.Invoke(this,new(nameof(UnavailableMessage)));}
        public MfgSettingsGroupState Group {get;init;}=null!;
        public string Warning=>LocalizedPresentation.Language.Lookup(MfgSettingsGroupStore.WarningFor(Definition));
        public string DisplayLabel=>MfgSettingsDisplay.Label(Definition,LocalizedPresentation.Language);
        public string DisplayDescription=>MfgSettingsDisplay.Description(Definition,LocalizedPresentation.Language);
        public bool NumericEditor=>Definition.CatalogEntry?.Type is "Integer" or "Float";
        public bool InputValid=>Definition.CatalogEntry is not {} entry||ProposedValue is not null&&MfgDynamicSettingsCatalog.Canonical(entry,ProposedValue)==ProposedValue;
        public string ConstraintText=>Definition.CatalogEntry is {} e?MfgSettingsDisplay.Constraints(e,LocalizedPresentation.Language):"";
        public void RefreshLanguage(){foreach(var name in new[]{nameof(DisplayLabel),nameof(DisplayDescription),nameof(Warning),nameof(CurrentDisplay),nameof(ConstraintText),nameof(UnavailableMessage)})PropertyChanged?.Invoke(this,new(name));foreach(var choice in DisplayChoices)choice.RefreshLanguage();foreach(var help in HelpSections)help.RefreshLanguage();}

        internal bool HasPendingValue => State is { IsValid: true } current && ProposedValue != current.EffectiveValue;
        public bool BaseCanEdit {get;}
        private bool canEdit;
        public bool CanEdit {get=>canEdit;set{if(canEdit==value)return;canEdit=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(CanEdit)));}}
        public string Details {get;init;}="";
        public IReadOnlyList<SettingHelpSection> HelpSections {get;init;}=[];
        public bool IsExpanded {get;set;}
        private bool isFavorite;
        public bool IsFavorite
        {
            get => isFavorite;
            set
            {
                if (isFavorite == value) return;
                isFavorite = value;
                PropertyChanged?.Invoke(this, new(nameof(IsFavorite)));
            }
        }
        public string? SelectedChoice
        {
            get => ProposedValue;
            set
            {
                // WPF can clear the target selection when a filtered container is detached.
                if(value is not null && Definition.Choices.Any(choice => choice.Value == value))
                    ProposedValue = value;
            }
        }
        public string? ProposedValue
        {
            get => proposedValue;
            set
            {
                if (proposedValue == value) return;
                proposedValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProposedValue)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedChoice)));
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(InputValid)));
                onChanged();
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record Game(string Name, string Path, string AppId, string? BuildId = null);
    private sealed record ComponentPlanRow(StreamlineComponentPlanEntry Entry, bool SelectionEnabled = true)
    {
        public string FileName => Entry.FileName;
        public string Action => PlanAction(Entry);
        public bool IsSelected => Entry.IsSelected;
        public bool CanSelect => Entry.CanSelect && SelectionEnabled;
        public string SourceSummary => $"Source: {Entry.SourcePath}\nSDK version: {TechnicalVersionDisplay.Format(Entry.SourceVersion)} · SHA-256: {Entry.Sha256}";
        public string TargetSummary => $"Target: {Entry.TargetPath}";
        public string ExistingSummary => $"Existing: {TechnicalVersionDisplay.Format(Entry.ExistingVersion)} · SHA-256: {Entry.ExistingSha256 ?? "—"}";
        public string BackupNote => Entry.Action == "Replace existing" ? "Original file will be backed up in this transaction." : "";
        public string ReviewNote => Entry.Message;
    }
    private sealed record RestoreRow(BackupTransactionInfo Info)
    {
        public string Created=>Info.CreatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"Unknown";
        public string Status=>RestoreStatus(Info.Status,LocalizedPresentation.Language);
        public string Availability=>LocalizedPresentation.Language.Lookup(Info.CanRestore?"Restore available":Info.Status=="RolledBack"?"Already Restored":"Conflict / review");
        public string FileCount=>LocalizedPresentation.Language.Format("Files: {0}",Info.Files.Count);
        public string Display => $"{Info.CreatedUtc?.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {Info.Status} · {Info.Files.Count} · {(Info.CanRestore ? "restore available" : Info.Status=="RolledBack"?"Already Restored":"Conflict / review")} · {Info.Message}";
    }
    private sealed record Release(string Name, string Tag, string Meta, string Url, string AssetName, string PublishedAt, string? Body, string? HtmlUrl, string? ApiUrl)
    { public string DisplayTag=>TechnicalVersionDisplay.Format(Tag); }
}

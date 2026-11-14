using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Dev14Checks
{
    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }

    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("dev14 ReShade launch, handoff, child exit and rescan guard the session", () => Lifecycle().GetAwaiter().GetResult());
        yield return ("dev14 ReShade cancellation, launch errors and rescan errors support recovery and repeat", () => Recovery().GetAwaiter().GetResult());
        yield return ("dev14 ReShade finish action is outside the scroller and close is guarded", FinishWiring);
        yield return ("dev14 real upstream 1.8.6 and 1.9.0 keep version-scoped discovery and ReShade readers read-only", Upstream);
        yield return ("dev14 MFG search finds translations, INI fields and all filter states", Search);
        yield return ("dev14 new upstream diagnostics are historical and support report redacts raw data", Diagnostics);
        yield return ("dev14 duplicate addon checks only the selected EXE folder", Duplicates);
    }

    private static async Task Lifecycle()
    {
        var session = new ReShadeSetupSession();
        var states = new List<ReShadeSetupPhase>();
        void Changed() => states.Add(session.Phase);
        var parent = new TaskCompletionSource<int?>();
        var launch = session.LaunchAsync(started => { started(); return parent.Task; }, Changed);
        Check(session.BlocksChanges && !session.CanConfirm, "Running parent unlocked changes.");
        Check(!await session.FinishAsync(() => false, () => throw new Exception("Unexpected scan."), Changed), "Confirmed before parent exit.");
        await Reject<InvalidOperationException>(() => session.LaunchAsync(_ => Task.FromResult<int?>(0), Changed));
        parent.SetResult(0);
        Check(await launch == 0 && session.CanConfirm && session.BlocksChanges, "Parent exit unlocked a possible elevated handoff.");
        Check(!await session.FinishAsync(() => true, () => throw new Exception("Unexpected scan."), Changed), "Possible child did not block scan.");
        var scan = new TaskCompletionSource();
        var finish = session.FinishAsync(() => false, () => scan.Task, Changed);
        Check(session.BlocksChanges && !session.CanConfirm, "Scan allowed changes or second confirmation.");
        scan.SetResult();
        Check(await finish && !session.BlocksChanges, "Normal finish did not unlock.");
        Check(states.SequenceEqual(new[] { ReShadeSetupPhase.Preparing, ReShadeSetupPhase.Watching,
            ReShadeSetupPhase.AwaitingConfirmation, ReShadeSetupPhase.Rescanning, ReShadeSetupPhase.Idle }), "Unexpected lifecycle sequence.");

        using var process = Process.GetCurrentProcess();
        Check(OfficialReShadeInstallerProcess.MayStillBeRunning(Path.Combine(Path.GetTempPath(), process.ProcessName + ".exe")), "Real live process missed by child name probe.");
        Check(!OfficialReShadeInstallerProcess.MayStillBeRunning("not-running-" + Guid.NewGuid().ToString("N") + ".exe"), "Absent process blocked.");
    }

    private static async Task Recovery()
    {
        var session = new ReShadeSetupSession();
        static void Changed() { }
        await Reject<OperationCanceledException>(() => session.LaunchAsync(_ => throw new OperationCanceledException(), Changed));
        Check(!session.BlocksChanges, "Cancelled preparation left a busy session.");
        await Reject<IOException>(() => session.LaunchAsync(_ => throw new IOException(), Changed));
        Check(!session.BlocksChanges, "Failed preflight entered confirmation.");
        await Reject<IOException>(() => session.LaunchAsync(started => { started(); throw new IOException(); }, Changed));
        Check(session.CanConfirm && session.BlocksChanges, "Unknown session after launch was unlocked.");
        await Reject<IOException>(() => session.FinishAsync(() => false, () => throw new IOException(), Changed));
        Check(!session.BlocksChanges, "Failed rescan did not unlock.");
        foreach (int? exit in new int?[] { 1602, null })
        {
            Check(await session.LaunchAsync(started => { started(); return Task.FromResult(exit); }, Changed) == exit, "Exit result was lost.");
            Check(await session.FinishAsync(() => false, () => Task.CompletedTask, Changed), "Repeat run could not finish.");
        }
    }

    private static async Task Reject<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void FinishWiring()
    {
        var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../RenoDXMfgUnlocker"));
        var finish = XDocument.Load(Path.Combine(app, "ReShadeManagerView.xaml")).Descendants()
            .Single(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "FinishButton"));
        Check(!finish.Ancestors().Any(e => e.Name.LocalName == "ScrollViewer"), "Finish button can scroll out of view.");
        var window = File.ReadAllText(Path.Combine(app, "MainWindow.ReShade.cs"));
        Check(window.Contains("reshadeSession.LaunchAsync") && window.Contains("reshadeSession.FinishAsync") &&
            window.Contains("OfficialReShadeInstallerProcess.MayStillBeRunning"), "Window bypasses session or process guards.");
        Check(File.ReadAllText(Path.Combine(app, "MainWindow.xaml.cs")).Contains("reshadeSession.BlocksChanges"), "Window close or overlapping edits bypass the ReShade guard.");
    }

    private static void Upstream()
    {
        var old = Dev4UpstreamChecks.Snapshot("1.8.5");
        var jitter = Dev4UpstreamChecks.Snapshot("1.8.6");
        var newest = Dev4UpstreamChecks.Snapshot("1.9.0");
        var baseline = MfgDynamicSettingsCatalog.ReviewedFallback();
        var a = MfgUpstreamSettingsAnalyzer.Analyze(jitter);
        var b = MfgUpstreamSettingsAnalyzer.Analyze(newest);
        Check(a.SchemaRecognized && b.SchemaRecognized, "Verified upstream table is no longer recognized.");
        Check(!MfgUpstreamSettingsAnalyzer.Analyze(old).Editable.Any(e => e.Key == "XmxJitter"), "Future jitter setting leaked to 1.8.5.");
        Check(a.Editable.Any(e => e.Section == "DLSSG" && e.Key == "XmxJitter" && e.Definition?.Default == "1"), "Missing real 1.8.6 jitter declaration.");
        foreach (var key in new[] { "XmxJitter", "PreAdaAdmission", "AutoEarlyLoad" })
            Check(b.Editable.Any(e => e.Section == "DLSSG" && e.Key == key && e.Definition?.Default == "1"), "Missing real 1.9.0 declaration: " + key);
        var reviewed = MfgUpstreamApprovalReview.Compare(b, baseline, jitter);
        Check(reviewed.Single(x => x.Finding.Key == "XmxJitter").Kind == MfgUpstreamApprovalReview.Unchanged, "Unchanged verified jitter declaration did not reconfirm.");
        Check(reviewed.Single(x => x.Finding.Key == "PreAdaAdmission").Kind == MfgUpstreamApprovalReview.New, "New 1.9.0 declaration did not require review.");
        var service = new MfgUnlockSettingsService(true, baseline.WithUpstream(b));
        foreach (var key in new[] { "XmxJitter", "PreAdaAdmission", "AutoEarlyLoad" })
            Check(service.DefinitionsFor("1.9.0").Any(d => d.Key == key && d.IsUpstreamSource)
                && !service.DefinitionsFor("1.8.6").Any(d => d.Key == key), "Write authority escaped the approved release: " + key);
        Check(b.Findings.Any(f => f.Key == "DynamicMaxMultiplier") && !b.Editable.Any(f => f.Key == "DynamicMaxMultiplier"), "ReShade reader disappeared or gained inferred write authority.");
    }

    private static void Search()
    {
        var lang = new UiLocalization();
        var baseCatalog = MfgDynamicSettingsCatalog.ReviewedFallback();
        var baseDefs = new[] { false, true }.SelectMany(x => new MfgUnlockSettingsService(x, baseCatalog).DefinitionsFor("1.9.0")).ToArray();
        var jitter = new MfgUnlockSettingsService(true, baseCatalog.WithUpstream(MfgUpstreamSettingsAnalyzer.Analyze(Dev4UpstreamChecks.Snapshot("1.9.0"))))
            .DefinitionsFor("1.9.0").Single(d => d.Key == "XmxJitter");
        foreach (var culture in UiLocalization.Cultures)
        {
            lang.SetCulture(culture, false);
            foreach (var definition in baseDefs)
            {
                var title = MfgSettingsDisplay.Label(definition, lang);
                Check(MfgSettingsSearch.Matches(MfgSettingFilter.All, title, lang, definition, true, false),
                    $"Unsearchable localized title in {culture} for {definition.Key}.");
            }
            foreach (var filter in Enum.GetValues<MfgSettingFilter>())
                Check(lang.GetCatalog(culture).ContainsKey(MfgSettingsSearch.Label(filter)), "Missing filter translation.");
            Check(lang.GetCatalog(culture).ContainsKey(MfgSettingsSearch.Prompt), "Missing search prompt.");
        }
        var label = MfgSettingsDisplay.Label(jitter, lang);
        bool Find(MfgSettingFilter filter, string query, bool editable = true, bool pending = false) =>
            MfgSettingsSearch.Matches(filter, query, lang, jitter, editable, pending);
        Check(Find(MfgSettingFilter.Upstream, "DLSSG XmxJitter") &&
            Find(MfgSettingFilter.All, label) &&
            Find(MfgSettingFilter.All, jitter.Description), "Missing original key, title, section or description.");
        Check(!Find(MfgSettingFilter.Pending, "jitter") && Find(MfgSettingFilter.Pending, "jitter", pending: true), "Pending not isolated.");
        Check(Find(MfgSettingFilter.Editable, "jitter") && !Find(MfgSettingFilter.Editable, "jitter", editable: false)
            && Find(MfgSettingFilter.ReadOnly, "jitter", editable: false), "Editability filters are incorrect.");
        Check(!Find(MfgSettingFilter.All, "no-such-key") &&
            !MfgSettingsSearch.Includes(MfgSettingFilter.ReadOnly, false, true, false), "Unrelated or editable row leaked.");
    }

    private static void Diagnostics()
    {
        const string exe = @"C:\Users\PrivateName\Game\Fixture.exe";
        const string privateText = @"C:\Users\PrivateName\secret-token-987";
        var now = DateTimeOffset.Parse("2026-11-14T19:02:12Z", System.Globalization.CultureInfo.InvariantCulture);
        var log = new RuntimeLogInput(@"C:\Users\PrivateName\ReShade.log",
            $"Initializing crosire's ReShade version '6.8.0' into '{exe}' (0x1)\nmfgunlock: architecture gate refused {privateText}\nmfgunlock: Auto early-load enabled ADDON.LoadFromDllMain {privateText}\n", now);
        var native = new RuntimeLogInput(@"C:\Users\PrivateName\MFGAdaUnlock-XeMFG.log",
            """
            {"event":"native_session","executable":"Fixture.exe","executable_version":"1.0.0.0","addon":"1.9.0","pid":901,"tick":1,"utc":"2026-11-14T19:02:00Z"}
            {"event":"native_factory_hook_failure","pid":901,"tick":2,"detail":"secret-token-987"}
            """, now);
        var result = RuntimeDiagnosticsService.Analyze(exe, "1.0.0.0", new(null, null, privateText), [log, native], now);
        Check(new[] { RuntimeDiagnosticsService.FactoryHookWarning, RuntimeDiagnosticsService.ProviderGateWarning,
            RuntimeDiagnosticsService.EarlyLoadWarning }.All(result.Warnings.Contains), "An upstream marker was lost.");
        Check(result.Statuses.Any(s => s.StartsWith("NVIDIA Dynamic MFG: Not verified", StringComparison.Ordinal))
            && result.Statuses.Any(s => s.StartsWith("XMX: Not verified", StringComparison.Ordinal)), "Historical diagnostics claimed activity.");
        result = result with { Evidence = [..result.Evidence, "Disk · sl.interposer.dll · 2.14.16.0\n" + privateText,
            "Disk · libxell.dll · " + privateText, "Disk · password.dll · 1.0.0\n" + privateText],
            Statuses = [..result.Statuses.Where(s => !s.StartsWith("NVIDIA Dynamic MFG:", StringComparison.Ordinal)),
                "NVIDIA Dynamic MFG: Enabled / accepted in recorded ReShade session; active Not verified (no unambiguous mode-specific presentation evidence)"],
            Warnings = [..result.Warnings, privateText, "https://example.invalid/api-key"], Logs = [log, native] };
        var lang = new UiLocalization();
        foreach (var culture in UiLocalization.Cultures)
        {
            lang.SetCulture(culture, false);
            var text = RuntimeDiagnosticsExport.Create(result, lang);
            Check(!text.Contains("PrivateName") && !text.Contains("secret-token") && !text.Contains("example.invalid") &&
                !text.Contains("password.dll") && !text.Contains("901"), "Raw data leaked from a diagnostic report.");
            Check(text.Contains("sl.interposer.dll · 2.14.16.0") && text.Contains(lang.Lookup("Unknown version")) &&
                text.Contains(lang.Lookup(RuntimeDiagnosticsService.FactoryHookWarning)) &&
                text.Contains(lang.Lookup("Active state: Not verified")), "Safe evidence or its limits were lost.");
        }
    }

    private static void Duplicates()
    {
        var folder = Path.Combine(Path.GetTempPath(), "renodx-dev14-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(folder, "bin");
        Directory.CreateDirectory(bin);
        try
        {
            var exe = Path.Combine(bin, "Fixture.exe");
            File.WriteAllText(exe, "");
            File.WriteAllText(Path.Combine(bin, "renodx-mfgunlock.addon64"), "");
            File.WriteAllText(Path.Combine(folder, "old-mfgunlock.addon64"), "");
            Check(RuntimeDiagnosticsService.FindAddonCopies(folder, exe).Count == 1, "Reported addon outside selected folder.");
            File.WriteAllText(Path.Combine(bin, "another-mfgunlock.addon64"), "");
            Check(RuntimeDiagnosticsService.FindAddonCopies(folder, exe).Count == 2, "Duplicate in selected folder missed.");
        }
        finally { Directory.Delete(folder, true); }
    }
}

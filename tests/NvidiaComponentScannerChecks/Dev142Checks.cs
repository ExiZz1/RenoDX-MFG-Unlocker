using System.IO;
using System.Xml.Linq;
using RenoDXMfgUnlocker;

internal static class Dev142Checks
{
    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("dev142 ReShade consent allows official manual setup only for the reviewed proxy snapshot", ProxyConsent);
        yield return ("dev142 search groups and filters recognize approved upstream options and preserve category context", Search);
        yield return ("dev142 report shows disk and authenticated backup locations without revealing paths", Diagnostics);
        yield return ("dev142 local profiles reject invalid data and remain separate from validated INI transactions", Profiles);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException) { return; }
        throw new InvalidOperationException("Invalid input was accepted.");
    }

    private static void ProxyConsent()
    {
        var fixture = Environment.GetEnvironmentVariable("RENODX_TEST_RESHADE_INSTALLER");
        using var space = new TestDirectory();
        var exe = Path.Combine(space.Game, "Target.exe");
        var proxy = Path.Combine(space.Game, "d3d12.dll");
        File.Copy(Environment.ProcessPath!, exe);
        File.WriteAllText(proxy, "unidentified proxy");
        var scanner = new ReShadeDetectionService();
        var api = ReShadeGraphicsApi.Direct3D12;
        var scan = scanner.Scan(space.Game, exe, api);
        Check(scan.HasConflicts && !ReShadeManualLaunchPolicy.HasCriticalBlock(scan), "Unknown DLL was not classified as a warning.");
        Reject(() => ReShadeManualLaunchPolicy.Validate(space.Game, exe, api, scan, null));
        var consent = ReShadeManualLaunchPolicy.Approve(space.Game, exe, api, scan);
        ReShadeManualLaunchPolicy.Validate(space.Game, exe, api, scanner.Scan(space.Game, exe, api), consent);
        foreach (var variant in new[] { scan with { CriticalInspectionFailure = true }, scan with { AlternativeAddonPaths = ["custom"] }, scan with { ConfigurationSha256 = "different" } })
            Reject(() => ReShadeManualLaunchPolicy.Validate(space.Game, exe, api, variant, consent));
        Reject(() => ReShadeManualLaunchPolicy.Validate(space.Game, exe, ReShadeGraphicsApi.Direct3D11, scan, consent));
        Reject(() => ReShadeManualLaunchPolicy.Validate(space.Game, Path.Combine(space.Game,"Other.exe"), api, scan, consent));
        File.AppendAllText(proxy, "changed");
        Reject(() => ReShadeManualLaunchPolicy.Validate(space.Game, exe, api, scanner.Scan(space.Game, exe, api), consent));
        foreach (var kind in new[] { ReShadeProxyKind.UnknownThirdParty, ReShadeProxyKind.IdentifiedThirdParty, ReShadeProxyKind.RecognizedReShade, ReShadeProxyKind.IdentityNotVerified })
        {
            var report = scan with { State = ReShadeDetectionState.DetectedUnknownSupport, Files = [scan.Files[0] with { Kind = kind }] };
            Check(ReShadeManualLaunchPolicy.RequiresConsent(report), "Proxy classification bypassed review: " + kind);
        }
        if (fixture is null) throw new SkipCheckException("Genuine reviewed installer fixture is needed to test the complete mocked handoff.");
        var cache = new ReShadePackageService(Path.Combine(space.Root, "cache"));
        var prepared = cache.PrepareAsync(space.Game, null, CancellationToken.None, fixture).GetAwaiter().GetResult();
        var process = new ProbeProcess();
        var runner = new ReShadeGuidedInstaller(cache, scanner, process, _ => false);
        var before = File.ReadAllBytes(proxy);
        var fresh = ReShadeManualLaunchPolicy.Approve(space.Game, exe, api, scanner.Scan(space.Game, exe, api));
        runner.LaunchAsync(space.Game, exe, api, prepared, true, false, () => {}, riskApproval: fresh).GetAwaiter().GetResult();
        Check(process.Starts == 1 && File.ReadAllBytes(proxy).SequenceEqual(before), "Manual handoff changed proxy bytes.");
        Reject(() => runner.LaunchAsync(space.Game, exe, api, prepared, true, false, () => {}, riskApproval: consent).GetAwaiter().GetResult());
        File.AppendAllText(cache.InstallerPath, "tampered");
        Reject(() => runner.LaunchAsync(space.Game, exe, api, prepared, true, false, () => {}, riskApproval: fresh).GetAwaiter().GetResult());
        Check(process.Starts == 1, "Stale consent or unverified package opened setup.");
    }

    private static void Search()
    {
        var view = new UiLocalization();
        var groups = new MfgSettingsGroupStore(view);
        var baseline = MfgDynamicSettingsCatalog.ReviewedFallback();
        foreach (var (version, key) in new[] { ("1.8.5", "GenericGate"), ("1.9.0", "XmxJitter") })
        {
            var source = MfgUpstreamSettingsAnalyzer.Analyze(Dev4UpstreamChecks.Snapshot(version));
            var approved = new MfgUnlockSettingsService(true,baseline.WithUpstream(source)).DefinitionsFor(version).Single(d=>d.Key==key);
            var group = groups.For("MFGAdaUnlock.ini",approved);
            group.IsExpanded = false;
            groups.SetSearchMode(true);
            Check(group.IsExpanded,"A collapsed category hid search matches.");
            foreach (var culture in UiLocalization.Cultures)
            {
                view.SetCulture(culture,false);
                Check(group.DisplayName.StartsWith(view.Lookup(MfgSettingsLayout.CategoryFor(approved).ToString()) + " · "), "Global results lost category context.");
                Check(MfgSettingsSearch.Matches(MfgSettingFilter.Upstream, "DLSSG " + key, view, approved, false,false), "Approved source row is not searchable.");
                Check(MfgSettingsSearch.Matches(MfgSettingFilter.Pending, key, view, approved,true,true) &&
                    !MfgSettingsSearch.Matches(MfgSettingFilter.Pending,key,view,approved,true,false), "Pending filter is not based on row state.");
                Check(MfgSettingsSearch.Matches(MfgSettingFilter.Favorites, key, view, approved,true,false,true), "Favorite is absent.");
                foreach(var name in new[] { "Favorite settings", "Matching settings: {0}", ReShadeManualLaunchPolicy.Consent,
                    "Copy redacted report", "MFG profiles", "MFG settings history" })
                    Check(view.GetCatalog(culture).ContainsKey(name),"Missing translation: "+name);
            }
            groups.SetSearchMode(false);
            Check(!group.IsExpanded,"Normal collapse state was lost.");
        }
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../RenoDXMfgUnlocker/MainWindow.xaml"));
        var doc = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Check((string?)doc.Descendants().Single(e=>(string?)e.Attribute(x+"Name")=="MfgFilterBox").Attribute("Style") == "{StaticResource MfgDarkComboBox}", "Filter uses a default WPF template.");
        Check(doc.Descendants().Any(e=>(string?)e.Attribute(x+"Name")=="MfgSearchClearButton" ), "Missing clear action.");
    }

    private static void Diagnostics()
    {
        using var space = new TestDirectory();
        var game = space.Game;
        var exe = Path.Combine(game,"bin","Target.exe");
        NvidiaComponentEntry Entry(string folder,string version,bool marker=false) =>
            new("sl.common.dll","Found",version,Path.Combine(folder,"sl.common.dll"),"hidden",marker);
        var inventory = RuntimeDiagnosticsService.BuildDiskInventory([
            Entry(Path.Combine(game,"bin"),"1.2"),
            Entry(Path.Combine(game,"other"),"1.3"),
            Entry(Path.Combine(game,"backups"),"1.4",true),
            Entry(space.Root,"private-host\\token")
        ],exe,game);
        File.WriteAllText(Path.Combine(game,"sl.common.dll"),"original");
        var store = new BackupRestoreService(Path.Combine(space.Root,"transactions"));
        var receipt = store.ExecuteAsync(game,[new("sl.common.dll",[1,2,3])]).GetAwaiter().GetResult();
        var backups = RuntimeDiagnosticsService.BackupInventory(store.ListTransactions(game));
        Check(backups.Length==1 && backups[0].Location == RuntimeDiskLocation.ConfirmedBackup, "Verified snapshot missing.");
        var report = new RuntimeDiagnosticReport([],[],[],DiskComponents:[..inventory,..backups]);
        var text = RuntimeDiagnosticsExport.Create(report,new UiLocalization());
        foreach(var label in new[] {"Beside the game EXE","Within the game folder","Other or unknown location","Verified backup copy"})
            Check(text.Contains(label),"Missing location "+label);
        Check(!text.Contains(space.Root) && !text.Contains("private-host"),"Export leaked location or vendor metadata.");
        var snapshot = Directory.EnumerateFiles(receipt.TransactionDirectory,"*.bin",SearchOption.AllDirectories).First(f=>File.ReadAllText(f)=="original");
        File.AppendAllText(snapshot,"tampered");
        Check(RuntimeDiagnosticsService.BackupInventory(store.ListTransactions(game)).Length==0,"Invalid backup was advertised as verified.");
    }

    private static void Profiles()
    {
        using var space = new TestDirectory();
        var folder = Path.Combine(space.Game,"bin","x64");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder,"Target.exe");
        var config = Path.Combine(folder,"ReShade.ini");
        File.WriteAllText(config,"[RenoDX.MFGUnlock]\r\nEnabled=1\r\n");
        var original=File.ReadAllBytes(config);
        var json = Path.Combine(space.Root,"profile.json");
        var profile = MfgUserData.Validate(new(1,"Favorite",[new("ReShade.ini","RenoDX.MFGUnlock","Enabled","0")]));
        MfgUserData.Write(json,profile);
        var loaded = MfgUserData.Validate(MfgUserData.Read<MfgUserProfile>(json));
        Check(loaded.Values.SequenceEqual(profile.Values) && File.ReadAllBytes(config).SequenceEqual(original),"Profile import wrote the game file.");
        Reject(() => MfgUserData.Validate(profile with { Values = [..profile.Values,..profile.Values] }));
        Reject(() => MfgUserData.Validate(profile with { Values = [profile.Values[0] with { File = @"..\ReShade.ini" }] }));
        var service = new MfgUnlockSettingsService();
        var snapshot = service.Load(config,space.Game,"1.9.0");
        Reject(() => service.CreateUpdatedBytes(snapshot,new Dictionary<string,string>{{"Enabled","unreviewed"}}));
        var file = service.PrepareTransaction(snapshot,loaded.Values.ToDictionary(v=>v.Key,v=>v.Value),()=>true);
        var store = new BackupRestoreService(Path.Combine(space.Root,"transactions"));
        var transaction = store.ExecuteAsync(space.Game,[file]).GetAwaiter().GetResult();
        Check(File.ReadAllText(config).Contains("Enabled=0"),"Normal settings Save did not use the proposed value.");
        var recovery = store.PreviewRestore(space.Game,transaction.TransactionId);
        Check(recovery.CanRestore && MfgUserData.MatchesHistoryFiles(space.Game,exe,recovery.Files), "History could not match a nested transaction target.");
        Check(!MfgUserData.MatchesHistoryFiles(space.Game,Path.Combine(space.Game,"Other.exe"),recovery.Files) &&
            !MfgUserData.MatchesHistoryFiles(space.Game,exe,[new("bin/x64/dxgi.dll","Restore original","Matches installed SHA-256","")]),
            "History accepted another executable directory or a DLL.");
        store.RestoreAsync(space.Game,transaction.TransactionId).GetAwaiter().GetResult();
        Check(File.ReadAllBytes(config).SequenceEqual(original),"Normal Restore did not recover the original bytes.");
    }

    private sealed class ProbeProcess : IReShadeInstallerProcess
    {
        internal int Starts { get; private set; }
        public Task<int?> RunAsync(string installer) { Starts++; return Task.FromResult<int?>(0); }
    }

    private sealed class TestDirectory : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),"RenoDXDev142",Guid.NewGuid().ToString("N"));
        internal string Game => Path.Combine(Root,"fixture");
        internal TestDirectory() => Directory.CreateDirectory(Game);
        public void Dispose() => Directory.Delete(Root,true);
    }
}

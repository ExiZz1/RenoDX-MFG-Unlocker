using System.Reflection;
using System.IO;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using RenoDXMfgUnlocker;

internal static class Dev143Checks
{
    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("dev143 actual MFG WPF single selection commits without resetting its live view", Selection);
        yield return ("dev143 STA pending and favorite filters preserve enum and text edits and staged defaults", FiltersAndDrafts);
        yield return ("dev143 ReShade parent exit waits for clean handoff then rescans and unlocks", ReShadeCompletion);
        yield return ("dev143 profile library round-trip and history reject superseded transactions with identical bytes", History);
        yield return ("stable140 recovery conflict and history labels are composed from all eight locale resources", RecoveryLocalization);
        yield return ("stable140 title bar fits all eight locales at minimum width without opening a window", TitleBarLayout);
    }

    private static void Check(bool accepted, string message)
    {
        if (!accepted) throw new InvalidOperationException(message);
    }
    private static void TitleBarLayout() => WpfCheckHost.Run(() =>
    {
        var language = LocalizedPresentation.Language;
        var visit = typeof(LocalizedPresentation).GetMethod("VisitTree",BindingFlags.Static|BindingFlags.NonPublic)!;
        try
        {
            foreach(var culture in UiLocalization.Cultures)
            {
                language.SetCulture(culture,false);
                using var window = new UnshownMainWindow();
                var root = (FrameworkElement)window.Main.Content;
                var appVersion = (TextBlock)window.Main.FindName("AppVersionText");
                var nav = (Button)window.Main.FindName("HomeNavButton");
                var size = new Size(window.Main.MinWidth, window.Main.MinHeight);
                void Layout()
                {
                    root.Measure(size);
                    root.Arrange(new Rect(size));
                    root.UpdateLayout();
                }
                appVersion.Text = "v1.4.0";
                Layout();
                var suffix = Descendants(root).OfType<TextBlock>().First(t=>t.Text=="  /  MFG UNLOCKER");
                var brand = (StackPanel)suffix.Parent;
                var logo = brand.Children.OfType<TextBlock>().First(t=>t.Text=="RENO");
                var navigation = (WrapPanel)nav.Parent;
                Check(suffix.FontSize==logo.FontSize && suffix.FontWeight==FontWeights.SemiBold &&
                    suffix.Foreground.ToString()=="#FF777684" && suffix.VerticalAlignment==VerticalAlignment.Center,
                    "Title typography changed beyond the requested size.");
                visit.Invoke(null,[brand]);
                visit.Invoke(null,[navigation]);
                Layout();
                double X(FrameworkElement item) => item.TranslatePoint(new Point(0,0),root).X;
                Check(suffix.Text==language.Lookup("  /  MFG UNLOCKER"),"Title suffix was not localized for "+culture);
                Check(X(suffix)+suffix.ActualWidth<=X(appVersion) && X(brand)+brand.ActualWidth<=X(navigation)+0.5 &&
                    X(navigation)+navigation.ActualWidth<=root.ActualWidth+0.5, "Title overlaps version or navigation in "+culture);
                foreach(var button in navigation.Children.OfType<Button>())
                    Check(X(button)+button.ActualWidth<=root.ActualWidth+0.5, "Navigation overflows in "+culture);
            }
        }
        finally { language.SetCulture("en-US",false); }
    });
    private sealed class UnshownMainWindow : IDisposable
    {
        internal MainWindow Main { get; } = new();
        public void Dispose() => Main.Close();
    }
    private static void RecoveryLocalization()
    {
        const string cause = "Current SHA-256 matches neither the original nor the transaction-installed file. It will be left untouched.";
        var sample = new BackupRestorePreview("fixture", "Completed", true, false, "Unlocalized placeholder",
            [new("bin/ReShade.ini","Blocked","Changed since transaction",cause)]);
        var language = new UiLocalization();
        foreach (var culture in UiLocalization.Cultures)
        {
            language.SetCulture(culture,false);
            var summary = MainWindow.RestoreSummary(sample,language);
            Check(summary == language.Lookup("Restore is blocked.")+"\nbin/ReShade.ini · "+language.Lookup(cause)+"\n"+language.Lookup("No files have been changed."),
                "Structured restore summary lost its localized reason, path or outcome.");
            if(culture != "en-US") Check(!summary.Contains(cause) && language.Lookup("Preview transaction recovery")!="Preview transaction recovery" &&
                MainWindow.RestoreStatus("RollbackNeedsReview",language)!="Rollback needs review","English remained in recovery text.");
        }
    }

    private static void Selection() => WpfCheckHost.Run(() =>
    {
        using var f = new Rc1SettingsChecks.Fixture();
        var page = (FrameworkElement)f.Window.FindName("MfgSettingsPage");
        page.Visibility = Visibility.Visible;
        var items = (ItemsControl)f.Window.FindName("MfgSettingsItemsControl");
        foreach(var row in f.Rows)
        {
            row.GetType().GetProperty("Group")!.SetValue(row,new MfgSettingsGroupState(MfgSettingsGroup.Addon,LocalizedPresentation.Language));
        }
        Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
        System.Windows.Data.CollectionViewSource.GetDefaultView(items.ItemsSource).Refresh();
        Measure(page);
        var combo = Descendants(items).OfType<ComboBox>().Single(c=>ReferenceEquals(c.DataContext,f.Row("Enabled")));
        // Keep real item containers in the unshown tree instead of materializing a native Popup.
        combo.Template = new ControlTemplate(typeof(ComboBox)) { VisualTree = new FrameworkElementFactory(typeof(ItemsPresenter)) };
        Measure(page);
        Check((string?)combo.SelectedValue == "1","Two-way binding did not select the saved value.");
        // Invoke the same WPF selector entry point that ComboBoxItem mouse-up invokes.
        var click = typeof(ComboBox).GetMethod("NotifyComboBoxItemMouseUp",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var view = (System.ComponentModel.INotifyPropertyChanged)f.Row("Enabled");
        var refreshes = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)items.Items).CollectionChanged += (_, e) =>
        { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) refreshes++; };
        var seenInBinding = -1;
        view.PropertyChanged += (_, e) => { if(e.PropertyName == "ProposedValue") seenInBinding = refreshes; };

        void Select(string value)
        {
            var index = combo.Items.Cast<MfgSettingChoiceDisplay>().ToList().FindIndex(c=>c.Value==value);
            var container = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(index);
            Check(combo.ItemContainerGenerator.ItemFromContainer(container) is MfgSettingChoiceDisplay choice && choice.Value==value, "Mouse entry point was given the wrong container.");
            try { click.Invoke(combo, [container]); }
            catch (TargetInvocationException exception) when(exception.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
            Check(f.Value("Enabled") == value, $"Pending value changed during binding reconciliation. target={combo.SelectedValue}, source={f.Value("Enabled")}, requested={value}");
        }
        Select("0");
        Check(seenInBinding == 0 && refreshes == 0, $"View refreshed inside selection callback ({seenInBinding}, {refreshes}).");
        Select("1");
        Select("0");
        Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.Background);
        Check(f.Value("Enabled")=="0" && f.Edits().SelectMany(d=>d.Changes).Any(p=>p.Key=="Enabled" && p.Value=="0"), "Pending edit was lost.");
        Check(click is not null, "WPF mouse-up entry point missing.");
    });

    private static void FiltersAndDrafts() => WpfCheckHost.Run(() =>
    {
        using var f = new Rc1SettingsChecks.Fixture("1.9.0", MfgDynamicSettingsCatalog.ReviewedFallback());
        var page = (FrameworkElement)f.Window.FindName("MfgSettingsPage");
        var items = (ItemsControl)f.Window.FindName("MfgSettingsItemsControl");
        var filter = (ComboBox)f.Window.FindName("MfgFilterBox");
        var search = (TextBox)f.Window.FindName("MfgSearchBox");
        var group = new MfgSettingsGroupState(MfgSettingsGroup.Addon, LocalizedPresentation.Language);
        foreach(var row in f.Rows) row.GetType().GetProperty("Group")!.SetValue(row, group);
        page.Visibility = Visibility.Visible;
        void Drain() { Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle); Measure(page); }
        void Select(MfgSettingFilter mode, string query)
        { filter.SetCurrentValue(Selector.SelectedValueProperty, mode); search.Text = query; Drain(); }
        var enabled = f.Row("Enabled");
        enabled.GetType().GetProperty("IsFavorite")!.SetValue(enabled,true);
        typeof(MainWindow).GetField("mfgFavorites",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(f.Window,
            new HashSet<string>(["ReShade.ini|RenoDX.MFGUnlock|Enabled"],StringComparer.OrdinalIgnoreCase));
        Select(MfgSettingFilter.Favorites,"Enabled");
        var choice = Descendants(items).OfType<ComboBox>().Single(c=>ReferenceEquals(c.DataContext,enabled));
        choice.SetCurrentValue(Selector.SelectedValueProperty,"0");
        Drain();
        Select(MfgSettingFilter.Pending,"Enabled");
        choice = Descendants(items).OfType<ComboBox>().Single(c=>ReferenceEquals(c.DataContext,enabled));
        Check((string?)choice.SelectedValue=="0","Favorite-to-pending filter lost an edit.");
        var typed = typeof(Dictionary<,>).MakeGenericType(enabled.GetType(),typeof(string));
        var staged = (IDictionary)Activator.CreateInstance(typed)!;
        staged.Add(enabled,"1");
        typeof(MainWindow).GetMethod("StageMfgValues",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(f.Window,[staged]);
        Drain();
        Check(f.Value("Enabled")=="1" && !f.Visible().Contains(enabled), "Staging the documented default did not leave the pending filter.");

        var numeric = f.Rows.First(r=> (bool)r.GetType().GetProperty("NumericEditor")!.GetValue(r)! &&
            (bool)r.GetType().GetProperty("CanEdit")!.GetValue(r)!);
        var number = (MfgUnlockSettingDefinition)numeric.GetType().GetProperty("Definition")!.GetValue(numeric)!;
        Select(MfgSettingFilter.Editable,number.Key);
        var input = Descendants(items).OfType<TextBox>().Single(t=>ReferenceEquals(t.DataContext,numeric));
        const string invalid = "invalid draft";
        input.SetCurrentValue(TextBox.TextProperty, invalid);
        Drain();
        Check(f.Value(number.Key)==invalid && !(bool)numeric.GetType().GetProperty("InputValid")!.GetValue(numeric)!, "Numeric binding lost its draft or accepted malformed input.");
        var legal = number.DefaultValue;
        staged.Clear();
        staged.Add(numeric,legal);
        staged.Add(enabled,"0");
        typeof(MainWindow).GetMethod("StageMfgValues",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(f.Window,[staged]);
        Drain();
        Check(f.Value(number.Key)==legal && f.Value("Enabled")=="0","Batch staging did not update both editor types.");
        try
        {
            LocalizedPresentation.Language.SetCulture("uk-UA",false);
            f.Select(MfgSettingsCategory.Other);
            Drain();
            Check(f.Value("Enabled")=="0" && f.Value(number.Key)==legal,"Language or category change discarded pending edits.");
        }
        finally { LocalizedPresentation.Language.SetCulture("en-US",false); Drain(); }
    });

    private static void ReShadeCompletion()
    {
        var session = new ReShadeSetupSession();
        session.LaunchAsync(started=>{started();return Task.FromResult<int?>(0);},()=>{}).GetAwaiter().GetResult();
        var samples = new Queue<bool>([false,true,true,false,false]);
        var complete = session.WaitForQuietExitAsync(()=>Task.FromResult(samples.Dequeue()),()=>Task.CompletedTask).GetAwaiter().GetResult();
        Check(complete && samples.Count==0 && session.BlocksChanges, "One clear sample unlocked an elevated handoff.");
        Check(!session.FinishAsync(()=>true,()=>throw new Exception("Unexpected rescan"),()=>{}).GetAwaiter().GetResult(), "Final recheck did not block.");
        var rescanned = false;
        session.FinishAsync(()=>false,()=>{rescanned=session.BlocksChanges;return Task.CompletedTask;},()=>{}).GetAwaiter().GetResult();
        Check(rescanned && !session.BlocksChanges,"Completion failed to keep the lock during rescan or release it afterward.");
        var root = Path.Combine(Path.GetTempPath(),"RenoDX-Dev143",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new ReShadeSessionLog(root);
            log.Write("phase AwaitingConfirmation");
            log.Write("phase Idle");
            Check(File.ReadAllLines(Path.Combine(root,"ReShade-lifecycle.log")).Length==2,"Lifecycle records missing.");
        }
        finally { Directory.Delete(root,true); }
    }

    private static void History()
    {
        var root = Path.Combine(Path.GetTempPath(),"RenoDX-Dev143",Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root,"Game");
        Directory.CreateDirectory(game);
        try
        {
            Check(!MfgUserData.IsProfileNameValid("  ") && !MfgUserData.IsProfileNameValid("Bad\nName"),"Invalid name was accepted.");
            var original = new MfgUserProfile(1,"Witcher 3 - TEST",[new("ReShade.ini","RenoDX.MFGUnlock","Enabled","1")]);
            var path = Path.Combine(root,"profiles.json");
            MfgUserData.Write(path,new[]{original});
            Check(MfgUserData.Validate(MfgUserData.Read<MfgUserProfile[]>(path).Single()).Values.SequenceEqual(original.Values),"Profile schema failed after reopen.");
            var ini = Path.Combine(game,"ReShade.ini");
            File.WriteAllBytes(ini,[0]);
            var backup = new BackupRestoreService(Path.Combine(root,"Transactions"));
            string Apply(byte value) => backup.ExecuteAsync(game,[new("ReShade.ini",[value])]).GetAwaiter().GetResult().TransactionId;
            var first = Apply(1);
            var second = Apply(2);
            var third = Apply(1);
            Check(backup.PreviewRestore(game,first).CanRestore && backup.HasNewerOverlappingTransaction(game,first),"Hash recurrence was not detected in history.");
            try { backup.RestoreSettingsHistoryAsync(game,first).GetAwaiter().GetResult(); throw new Exception("Older history was restored."); }
            catch (InvalidOperationException ex) when (ex.Message == BackupRestoreService.NewerHistory) { }
            foreach(var id in new[]{third,second,first}) backup.RestoreSettingsHistoryAsync(game,id).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(ini).SequenceEqual(new byte[]{0}),"Newest-first restore did not recover exact original bytes.");
        }
        finally { Directory.Delete(root,true); }
    }

    private static void Measure(FrameworkElement node)
    {
        node.Measure(new Size(1000,650));
        node.Arrange(new Rect(0,0,1000,650));
        node.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node,i);
            yield return child;
            foreach (var entry in Descendants(child)) yield return entry;
        }
    }
}

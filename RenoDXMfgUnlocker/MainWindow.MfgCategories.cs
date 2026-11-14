using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace RenoDXMfgUnlocker;
public partial class MainWindow
{
    public MfgSettingsStrings MfgStrings {get;}=new(LocalizedPresentation.Language);
    private readonly MfgSettingsGroupStore mfgSettingsGroups=new(LocalizedPresentation.Language);
    private readonly System.Collections.ObjectModel.ObservableCollection<MfgUnlockSettingRow> displayedMfgRows=[];
    private MfgSettingsCategory mfgCategory=MfgSettingsCategory.General;
    private MfgSettingFilter settingsFilter;
    private bool updatingSearchLabels;
    private bool pendingSearchRefresh;
    private bool pendingMfgUpdate;
    private bool SearchingAllCategories => settingsFilter != MfgSettingFilter.All || MfgSearchBox.Text.Trim().Length > 0;
    private sealed record SettingsFilterChoice(MfgSettingFilter Filter, string Label);

    private bool MatchesSettingsSearch(MfgUnlockSettingRow row) =>
        MfgSettingsSearch.Matches(settingsFilter, MfgSearchBox.Text, LocalizedPresentation.Language,
            row.Definition, row.CanEdit, row.HasPendingValue, settingsFilter == MfgSettingFilter.Favorites && IsMfgFavorite(row),
            xeMfgSettingRows.Contains(row) ? "MFGAdaUnlock.ini" : "ReShade.ini");

    private bool VisibleMfgRow(object value) => value is MfgUnlockSettingRow row &&
        !MfgSettingsLayout.IsMethodKey(row.Definition.Key) &&
        (SearchingAllCategories ? MatchesSettingsSearch(row) : MfgSettingsLayout.CategoryFor(row.Definition) == mfgCategory);

    private void QueueMfgSearchRefresh()
    {
        if (pendingSearchRefresh) return;
        pendingSearchRefresh = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            pendingSearchRefresh = false;
            UpdateMfgSearchPresentation();
        }));
    }

    private void QueueMfgEditUpdate()
    {
        if (pendingMfgUpdate) return;
        pendingMfgUpdate = true;
        // A TwoWay SelectedValue update can still be inside WPF Selector.SelectionChanger.End.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            pendingMfgUpdate = false;
            UpdateMfgSettingsButtons();
        }));
    }

    private void UpdateMfgSearchPresentation()
    {
        var searching = SearchingAllCategories;
        var language = LocalizedPresentation.Language;
        mfgSettingsGroups.SetSearchMode(searching);
        var view = CollectionViewSource.GetDefaultView(displayedMfgRows);
        // Refresh only when membership changes. Unchanged numeric editors keep focus while typing.
        if (!view.Cast<MfgUnlockSettingRow>().ToHashSet().SetEquals(displayedMfgRows.Where(VisibleMfgRow)))
            view.Refresh();
        MainSettingsHeading.Text = language.Lookup(searching ? "Settings across all categories" : mfgCategory.ToString());
        MainSettingsHeading.Visibility = searching || !view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        var methods = xeMfgSettingRows.Where(r => MfgSettingsLayout.IsMethodKey(r.Definition.Key)).ToArray();
        var showMethods = !searching ? mfgCategory == MfgSettingsCategory.General : methods.Any(MatchesSettingsSearch);
        GenerationModePanel.Visibility = showMethods && methods.Length == 2 ? Visibility.Visible : Visibility.Collapsed;
        GenerationUnavailableText.Visibility = !searching && showMethods && methods.Length != 2 ? Visibility.Visible : Visibility.Collapsed;
        var unknownSettings = mfgSettingsDocuments.SelectMany(d => d.Snapshot.UnknownSettings
            .Select(i => (File: d.Service.ConfigFileName, Setting: i))).ToArray();
        var unknown = unknownSettings
            .Where(i => MfgSettingsSearch.Contains(MfgSearchBox.Text, language, i.Setting.Key, i.Setting.Section, i.File))
            .GroupBy(i => (i.File, i.Setting.Section, i.Setting.Key))
            .Select(g => g.Key.File + " · [" + g.Key.Section + "] " + g.Key.Key + "=" + string.Join(" | ",g.Select(i=>i.Setting.RawValue).Distinct())).ToArray();
        UnknownSettingsText.Text = unknown.Length == 0 ? language.Lookup("No unknown settings found.") : string.Join("\n", unknown);
        UnknownSettingsPanel.Visibility = !searching || (settingsFilter is MfgSettingFilter.All or MfgSettingFilter.ReadOnly) && unknown.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        var resultCount = view.Cast<MfgUnlockSettingRow>().Count()
            + (showMethods ? methods.Count(r => !searching || MatchesSettingsSearch(r)) : 0)
            + (UnknownSettingsPanel.Visibility == Visibility.Visible ? unknown.Length : 0);
        MfgSearchCount.Text = language.Format("Matching settings: {0}", resultCount);
        MfgSearchCount.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        MfgSearchClearButton.IsEnabled = searching;
        MfgSearchStatus.Text = language.Lookup(displayedMfgRows.Count == 0 && unknownSettings.Length == 0
            ? "No settings loaded. Select a game, then scan its installed addon."
            : searching && resultCount == 0
                ? "No matching loaded settings. Upstream options require source approval for the detected addon version."
                : !displayedMfgRows.Any(r => r.CanEdit)
                    ? "Loaded settings are read-only. Review the addon version, game state and field requirements."
                    : "");
        MfgSearchStatus.Visibility = MfgSearchStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateMfgSearchLabels()
    {
        var language = LocalizedPresentation.Language;
        MfgSearchLabel.Text = language.Lookup(MfgSettingsSearch.Prompt);
        MfgSearchClearButton.Content = language.Lookup("Clear search and filters");
        LocalizedPresentation.SetPreserveOriginal(MfgSearchLabel, true);
        updatingSearchLabels = true;
        try
        {
            MfgFilterBox.ItemsSource = Enum.GetValues<MfgSettingFilter>().Select(f => new SettingsFilterChoice(f, language.Lookup(MfgSettingsSearch.Label(f)))).ToArray();
            MfgFilterBox.SelectedValue = settingsFilter;
        }
        finally { updatingSearchLabels = false; }
    }

    private void InitializeMfgCategories()
    {
        // One presentation view across both INI documents; writers retain their original collections.
        void Synchronize(object? sender,System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            displayedMfgRows.Clear();
            foreach(var row in mfgSettingRows.Concat(xeMfgSettingRows))displayedMfgRows.Add(row);
        }
        mfgSettingRows.CollectionChanged+=Synchronize;xeMfgSettingRows.CollectionChanged+=Synchronize;
        MfgSettingsItemsControl.ItemsSource=displayedMfgRows;
        XeMfgSettingsItemsControl.ItemsSource=null;
        CollectionViewSource.GetDefaultView(displayedMfgRows).Filter=VisibleMfgRow;
        CollectionViewSource.GetDefaultView(displayedMfgRows).GroupDescriptions.Add(new PropertyGroupDescription(nameof(MfgUnlockSettingRow.Group)));
        MfgSearchBox.TextChanged += (_, _) => UpdateMfgSearchPresentation();
        MfgSearchClearButton.Click += (_, _) =>
        {
            MfgSearchBox.Clear();
            MfgFilterBox.SelectedValue = MfgSettingFilter.All;
            QueueMfgSearchRefresh();
        };
        MfgFilterBox.SelectionChanged += (_, _) =>
        {
            if (updatingSearchLabels || MfgFilterBox.SelectedValue is not MfgSettingFilter filter) return;
            settingsFilter = filter;
            QueueMfgSearchRefresh();
        };
        UpdateMfgSearchLabels();
        void RefreshLanguage(){UpdateMfgSearchLabels();UpdateMfgSettingsButtons();}
        EventHandler language=(_,_)=>{if(Dispatcher.CheckAccess())RefreshLanguage();else Dispatcher.BeginInvoke(new Action(RefreshLanguage));};
        LocalizedPresentation.Language.Changed+=language;Closed+=(_,_)=>LocalizedPresentation.Language.Changed-=language;
        UpdateMfgCategoryPresentation();
    }
    private void MfgCategory_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button {Tag:string name}&&Enum.TryParse<MfgSettingsCategory>(name,out var category))SelectMfgCategory(category);
    }
    private void SelectMfgCategory(MfgSettingsCategory category)
    {
        if(!Enum.IsDefined(category)||mfgSettingsBusy)return;
        mfgCategory=category;
        UpdateMfgCategoryPresentation();MfgSettingsScroll.ScrollToTop();
    }
    private void MfgCategory_KeyDown(object sender,KeyEventArgs e)
    {
        var index=e.Key switch {Key.Left=>(int)mfgCategory+2,Key.Right=>(int)mfgCategory+1,Key.Home=>0,Key.End=>2,_=>-1};
        if(index<0||mfgSettingsBusy)return;
        e.Handled=true;SelectMfgCategory((MfgSettingsCategory)(index%3));
        CategoryButtons()[(int)mfgCategory].Focus();
    }
    private Button[] CategoryButtons()=>[MfgGeneralTab,MfgAdvancedTab,MfgOtherTab];
    private void UpdateMfgCategoryPresentation()
    {
        MfgStrings.RefreshLanguage();
        RefreshMfgText();
        MfgSettingsSaveButton.ToolTip=LocalizedPresentation.Language.Lookup(PendingNvidiaEditsBlockedByXmx()?PendingXmxConflict:"Only settings for the confirmed installed addon version can be changed.");
        mfgSettingsGroups.RefreshLanguage();
        UpdateSettingsCatalogPresentation();
        var language=LocalizedPresentation.Language;
        foreach(var (button,index) in CategoryButtons().Select((b,i)=>(b,i))){
            var active=index==(int)mfgCategory;button.Background=SetupWizardView.Brush(active?"#493361":"#20222C");button.BorderBrush=SetupWizardView.Brush(active?"#A78BFA":"#393746");button.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;button.IsEnabled=!mfgSettingsBusy;
            button.Content=language.Lookup(((MfgSettingsCategory)index).ToString());LocalizedPresentation.SetPreserveOriginal(button,true);
        }
        MfgCategoryDescription.Text=language.Lookup(mfgCategory switch {
            MfgSettingsCategory.General=>"Common options and generation method. Changes remain pending until Save.",
            MfgSettingsCategory.Advanced=>"NVIDIA quality, runtime policy and experimental Vulkan options. Version restrictions still apply.",
            _=>"Presentation workarounds and experimental model options. Change only for deliberate troubleshooting."
        });
        XeSettingsHeading.Visibility=Visibility.Collapsed;
        XeMfgSettingsItemsControl.Visibility=Visibility.Collapsed;
        GenerationUnavailableText.Text=language.Lookup("Generation-method editing requires addon 1.8.0 or newer with the required configuration support. Existing settings remain available.");
        foreach(var text in new[]{MfgCategoryDescription,MainSettingsHeading,XeSettingsHeading,GenerationUnavailableText,MfgSearchCount,MfgSearchStatus})LocalizedPresentation.SetPreserveOriginal(text,true);
        QueueMfgSearchRefresh();
        MfgOtherTools.Visibility=mfgCategory==MfgSettingsCategory.Other?Visibility.Visible:Visibility.Collapsed;
        LocalizedPresentation.RefreshGeneratedItems(MfgSettingsPage);
    }
    // Save always reads the full backing collections, never the currently filtered controls.
    private (MfgSettingsDocument Doc,Dictionary<string,string> Changes)[] PendingMfgEdits()=>mfgSettingsDocuments.Where(d=>d.Snapshot.IsSupportedAddonVersion).Select(d=>(Doc:d,Changes:d.Rows.Where(r=>r.ProposedValue is not null&&d.Snapshot.Settings.First(s=>s.Definition.Key==r.Definition.Key).EffectiveValue!=r.ProposedValue).ToDictionary(r=>r.Definition.Key,r=>r.ProposedValue!,StringComparer.OrdinalIgnoreCase))).Where(e=>e.Changes.Count>0).ToArray();
}

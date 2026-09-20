// Author: rstewa · https://github.com/rstewa
// Updated: 03/11/2025

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using Windows.Storage;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using Audibly.App.Helpers;
using Audibly.App.Services;
using Audibly.App.UserControls;
using Audibly.App.ViewModels;
using Audibly.App.Views.ContentDialogs;
using Audibly.Models;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Sentry;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Audibly.App.Views;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LibraryCardPage : Page
{
    #region Filter enums

    private enum LengthFilter { Any, Short, Medium, Long, Custom }

    #endregion

    public const string ImportAudiobookText = "Import an audiobook (.m4b, mp3)";

    public const string ImportAudiobooksFromDirectoryText =
        "Import all audiobooks in a directory (recursively). Single-file audiobooks only (.m4b, mp3)";

    public const string ImportAudiobookWithMultipleFilesText =
        "Import an audiobook made up of multiple files (.m4b, mp3)";

    public const string ImportFromJsonFileText = "Import audiobooks from an Audibly export file (.audibly)";

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private bool _suppressNextTextClear;

    // Length filter state
    private LengthFilter _activeLengthFilter = LengthFilter.Any;
    private bool _updatingLengthToken;
    // Custom length filter bounds in hours (null = no bound on that side)
    private double? _customLengthMinHours;
    private double? _customLengthMaxHours;
    // Sentinel IDs identify length chips so they are excluded from the real tag filter
    private static readonly Guid LengthShortId  = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LengthMediumId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid LengthLongId   = new("00000000-0000-0000-0000-000000000003");
    private static readonly Guid LengthCustomId = new("00000000-0000-0000-0000-000000000004");

    // Tags filter dropdown state
    // _allTagFilterItems is the persistent master row list (one per available tag, checked state
    // always kept in sync with SelectedTags); _tagsFilterDisplayItems is the ListView's bound
    // subset after the search box narrows it — same TagFilterItem instances, so checking a box
    // updates both without any extra sync code.
    private readonly List<TagFilterItem> _allTagFilterItems = new();
    private readonly ObservableCollection<TagFilterItem> _tagsFilterDisplayItems = new();

    /// <summary>
    ///     The ListView's bound item source in the Tags flyout (x:Bind requires a property, not a field).
    /// </summary>
    public ObservableCollection<TagFilterItem> TagsFilterDisplayItems => _tagsFilterDisplayItems;

    public LibraryCardPage()
    {
        InitializeComponent();

        // subscribe to page loaded event
        Loaded += LibraryCardPage_Loaded;
        Unloaded += LibraryCardPage_Unloaded;
        ViewModel.ClearSearchText += ViewModelOnClearSearchText;
        ViewModel.AvailableTagsReloaded += ViewModelOnAvailableTagsReloaded;
        // keep sort UI in sync with ViewModel and persisted settings
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;

        // initialize sort toggles based on saved sort mode
        UpdateSortToggleUI();
        UpdateTagsButtonState();

        LibraryCardScrollView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnScrollViewPointerPressed), true);
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    private async void LibraryCardPage_Loaded(object sender, RoutedEventArgs e)
    {
        // check if data migration already failed
        if (UserSettings.ShowDataMigrationFailedDialog)
        {
            // note: content dialog
            await DialogService.ShowDataMigrationFailedDialogAsync();

            UserSettings.NeedToImportAudiblyExport = false;
            UserSettings.ShowDataMigrationFailedDialog = false;

            return;
        }

        // check if we need to import the user's data from the old database
        if (!UserSettings.NeedToImportAudiblyExport) return;

        // let the user know that we need to migrate their data into the new database
        // todo: probably do not need this try/catch block but leaving it here for now
        try
        {
            await DialogService.ShowDataMigrationRequiredDialogAsync();
        }
                catch (Exception exception)
                {
                    UserSettings.NeedToImportAudiblyExport = false;
                    UserSettings.ShowDataMigrationFailedDialog = false;

                    // log the error
                    ViewModel.LoggingService.LogError(exception, true);

                    // notify user that we failed to import their audiobooks
                    ViewModel.EnqueueNotification(new Notification
                    {
                        Message = "Data Migration Failed",
                        Severity = InfoBarSeverity.Error
                    });
        #if DEBUG
                    throw;
        #endif
                }
            }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.GetAudiobookListAsync();
    }

    /// <summary>
    ///     Resets the audiobook list.
    /// </summary>
    public async Task ResetAudiobookListAsync()
    {
        _activeLengthFilter = LengthFilter.Any;
        SetLengthButtonUI(LengthFilter.Any);

        ViewModel.ClearSelectedTags();
        foreach (var item in _tagsFilterDisplayItems) item.IsChecked = false;
        UpdateTagsButtonState();

        ViewModel.SearchText = string.Empty;
        if (AudiobookSearchBox != null) AudiobookSearchBox.Text = string.Empty;

        await ApplyFiltersAsync();
    }

    /// <summary>
    ///     Single unified filter: applies length, tag (OR), and search-text filters together
    ///     from the master AudiobooksForFilter list, then updates ViewModel.Audiobooks.
    /// </summary>
    private async Task ApplyFiltersAsync()
    {
        var searchText = ViewModel.SearchText;
        // Exclude sentinel length chips from the real tag filter
        var realTags = ViewModel.SelectedTags.Where(t => !IsLengthFilterTag(t)).ToList();
        var hasTagFilter = realTags.Count > 0;
        var hasSearch = !string.IsNullOrEmpty(searchText);

        IEnumerable<AudiobookViewModel> source = ViewModel.AudiobooksForFilter;

        if (hasTagFilter)
        {
            source = source.Where(a =>
                a.Model.Tags.Any(t => realTags.Any(s => s.Id == t.Id)));
        }

        if (_activeLengthFilter != LengthFilter.Any)
        {
            const long shortMax  = 6L  * 3600L;  // 21 600 s
            const long mediumMax = 17L * 3600L;  // 61 200 s
            var customMinSec = (_customLengthMinHours ?? 0) * 3600.0;
            var customMaxSec = (_customLengthMaxHours ?? double.MaxValue) * 3600.0;
            source = _activeLengthFilter switch
            {
                LengthFilter.Short  => source.Where(a => a.Duration > 0 && a.Duration < shortMax),
                LengthFilter.Medium => source.Where(a => a.Duration >= shortMax && a.Duration <= mediumMax),
                LengthFilter.Long   => source.Where(a => a.Duration > mediumMax),
                LengthFilter.Custom => source.Where(a => a.Duration > 0 && a.Duration >= customMinSec && a.Duration <= customMaxSec),
                _                   => source
            };
        }

        List<AudiobookViewModel> results;
        if (hasSearch)
        {
            var terms = searchText.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            var scored = source
                .Select(a => new
                {
                    Audiobook = a,
                    Score = terms.Count(term =>
                        a.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        a.Author.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(a.Description) &&
                         a.Description.Contains(term, StringComparison.OrdinalIgnoreCase)))
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            var exactMatches = scored
                .Where(x =>
                    x.Audiobook.Title.Equals(searchText, StringComparison.OrdinalIgnoreCase) ||
                    x.Audiobook.Author.Equals(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            results = (exactMatches.Count > 0 ? exactMatches : scored).Select(x => x.Audiobook).ToList();
        }
        else
        {
            results = source.ToList();
        }

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            ViewModel.Audiobooks.Clear();
            foreach (var a in results) ViewModel.Audiobooks.Add(a);
        });
    }

    /// <summary>
    ///     Updates the Tags button's border highlight and red dot to reflect whether any
    ///     real (non-length-sentinel) tags are currently selected.
    /// </summary>
    private void UpdateTagsButtonState()
    {
        // Controls are null the first time this is called during page construction.
        if (TagsButton == null) return;

        var isFilterActive = ViewModel.SelectedTags.Any(t => !IsLengthFilterTag(t));

        if (isFilterActive)
        {
            TagsButton.BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            TagsButton.BorderThickness = new Thickness(2);
        }
        else
        {
            TagsButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            TagsButton.BorderThickness = new Thickness(0);
        }

        if (TagsFilterDot != null)
            TagsFilterDot.Visibility = isFilterActive ? Visibility.Visible : Visibility.Collapsed;
    }

    // Fires regardless of what currently has focus (e.g. after toggling the nav pane, which moves
    // focus outside this page's own visual tree) — a plain bubbling KeyDown handler wouldn't.
    private void ClearSelectionKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.Audiobooks.Any(a => a.IsSelected))
        {
            args.Handled = false;
            return;
        }

        ViewModel.ClearSelection();
        args.Handled = true;
    }

    private void OnScrollViewPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.Audiobooks.Any(a => a.IsSelected)) return;

        var current = e.OriginalSource as DependencyObject;
        while (current != null && current != LibraryCardScrollView)
        {
            if (current is AudiobookTile) return;
            current = VisualTreeHelper.GetParent(current);
        }
        ViewModel.ClearSelection();
    }

    private void LibraryCardPage_DragOver(object sender, DragEventArgs e)
    {
        // Accept drops that include storage items (files/folders).
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }

        e.Handled = true;
    }

    private async void LibraryCardPage_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                return;

            var storageItems = await e.DataView.GetStorageItemsAsync();
            if (storageItems == null || storageItems.Count == 0) return;

            // Prefer the first item. If multiple items are dropped, handle the first.
            var first = storageItems[0];

            if (first is StorageFile file)
            {
                // Single file -> import single-file audiobook
                await ViewModel.ImportAudiobookFromFileActivationAsync(file.Path, showImportDialog: true);
            }
            else if (first is StorageFolder folder)
            {
                // Folder -> treat folder as multi-file audiobook (top-level files in folder)
                await ViewModel.ImportAudiobookFromFolderAsync(folder.Path);
            }
        }
                catch (Exception ex)
                {
                    // Log and show notification on failure
                    ViewModel.LoggingService.LogError(ex, true);
                    ViewModel.EnqueueNotification(new Notification
                    {
                        Message = "Failed to import dropped item.",
                        Severity = InfoBarSeverity.Error
                    });
        #if DEBUG
                    throw;
        #endif
                }
            }

    #region multi-select action bar

    private async void MultiSelectDeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Audiobooks.Count(a => a.IsSelected);
        if (await DialogService.ConfirmDeleteAudiobooksAsync(count))
            await ViewModel.DeleteSelectedAudiobooksAsync();
    }

    private async void MultiSelectManageTags_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedAudiobooks = ViewModel.Audiobooks.Where(a => a.IsSelected).ToList();
        await DialogService.ShowManageTagsDialogAsync(selectedAudiobooks);
    }

    #endregion

    #region debug button

    private async void TestContentDialogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangelogContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot
        };
        await dialog.ShowAsync();

        // ViewModel.ProgressDialogPrefix = "Importing";
        // ViewModel.ProgressDialogText = "A Clash of Kings";
        //
        // var dialog = new ProgressContentDialog();
        // dialog.XamlRoot = App.Window.Content.XamlRoot;
        // await dialog.ShowAsync();
        // ViewModel.MessageService.ShowDialog(DialogType.Changelog, "What's New?", Changelog.Text);
        // ViewModel.MessageService.ShowDialog(DialogType.FailedDataMigration, string.Empty, string.Empty);
    }

    private void InfoBar_OnClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        // get the notification object
        if (sender.DataContext is not Notification notification) return;
        ViewModel.OnNotificationClosed(notification);
    }

    private void TestNotificationButton_OnClick(object sender, RoutedEventArgs e)
    {
        // randomly select InfoBarSeverity
        var random = new Random();
        var severity = random.Next(0, 4);

        ViewModel.EnqueueNotification(new Notification
        {
            Message = "This is a test notification",
            Severity = severity switch
            {
                0 => InfoBarSeverity.Informational,
                1 => InfoBarSeverity.Success,
                2 => InfoBarSeverity.Warning,
                3 => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            }
        });
    }

    public void ThrowExceptionButton_OnClick(object sender, RoutedEventArgs e)
    {
        throw new Exception("This is a test exception");
    }

    public void RestartAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.RestartApp();
    }

    public void HideNowPlayingBarButton_OnClick(object sender, RoutedEventArgs e)
    {
        PlayerViewModel.MediaPlayer.Pause();
        if (PlayerViewModel.NowPlaying != null)
            PlayerViewModel.NowPlaying.IsNowPlaying = false;
        PlayerViewModel.NowPlaying = null;
    }

    public void OpenAppStateFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var filePath = ApplicationData.Current.LocalFolder.Path;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{filePath}\"";
        p.Start();
    }

    private void DebugMenuKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ShowDebugMenu = !ViewModel.ShowDebugMenu;
    }

    private void OpenCurrentAudiobooksAppStateFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedAudiobook = PlayerViewModel.NowPlaying;
        if (selectedAudiobook == null) return;
        var dir = Path.GetDirectoryName(selectedAudiobook.CoverImagePath);
        if (dir == null) return;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{dir}\"";
        p.Start();
    }

    private void TestSentryLoggingButton_OnClick(object sender, RoutedEventArgs e)
    {
        SentrySdk.CaptureMessage("Something went wrong");
        ViewModel.EnqueueNotification(new Notification
        {
            Message = "Sentry message sent",
            Severity = InfoBarSeverity.Success
        });
    }

    private void ToggleLoadingProgressBar_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsLoading = !ViewModel.IsLoading;
    }

    #endregion

    // Sort menu handlers
    private void SortAlphabetical_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSortMode = Audibly.App.ViewModels.AudiobookSortMode.Alphabetical;

        SortAlphabeticalItem.IsChecked = true;
        SortByDateImportedItem.IsChecked = false;
        SortByLastPlayedItem.IsChecked = false;
    }

    private void SortByDateImported_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSortMode = Audibly.App.ViewModels.AudiobookSortMode.DateImported;

        SortAlphabeticalItem.IsChecked = false;
        SortByDateImportedItem.IsChecked = true;
        SortByLastPlayedItem.IsChecked = false;
    }

    private void SortByLastPlayed_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSortMode = Audibly.App.ViewModels.AudiobookSortMode.DateLastPlayed;

        SortAlphabeticalItem.IsChecked = false;
        SortByDateImportedItem.IsChecked = false;
        SortByLastPlayedItem.IsChecked = true;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentSortMode))
        {
            // ensure UI update runs on dispatcher
            _dispatcherQueue.TryEnqueue(UpdateSortToggleUI);
        }
    }

    private void UpdateSortToggleUI()
    {
        // controls may not be ready yet
        if (SortAlphabeticalItem == null || SortByDateImportedItem == null || SortByLastPlayedItem == null) return;

        switch (ViewModel.CurrentSortMode)
        {
            case ViewModels.AudiobookSortMode.Alphabetical:
                SortAlphabeticalItem.IsChecked = true;
                SortByDateImportedItem.IsChecked = false;
                SortByLastPlayedItem.IsChecked = false;
                break;
            case ViewModels.AudiobookSortMode.DateImported:
                SortAlphabeticalItem.IsChecked = false;
                SortByDateImportedItem.IsChecked = true;
                SortByLastPlayedItem.IsChecked = false;
                break;
            case ViewModels.AudiobookSortMode.DateLastPlayed:
                SortAlphabeticalItem.IsChecked = false;
                SortByDateImportedItem.IsChecked = false;
                SortByLastPlayedItem.IsChecked = true;
                break;
        }
    }

    private void LibraryCardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSearchText -= ViewModelOnClearSearchText;
        ViewModel.AvailableTagsReloaded -= ViewModelOnAvailableTagsReloaded;
    }

    private async void ViewModelOnAvailableTagsReloaded(object? sender, EventArgs e)
    {
        // SelectedTags may have been pruned (a selected tag was deleted elsewhere).
        UpdateTagsButtonState();
        await ApplyFiltersAsync();
    }

    #region Search / Token functionality

    /// <summary>
    ///     Prevent a typed string from becoming a token unless it exactly matches a tag name.
    ///     Free-text stays in the box as search text; only known tags can be tokens.
    /// </summary>
    private void AudiobookSearchBox_TokenItemAdding(TokenizingTextBox sender, TokenItemAddingEventArgs args)
    {
        if (args.Item is Tag)
        {
            // Programmatic token from ItemsSource — if the control clears the inner text
            // box as a side effect, don't wipe the user's search term.
            _suppressNextTextClear = true;
            return;
        }

        var text = args.TokenText;
        var match = ViewModel.AvailableTags.FirstOrDefault(t =>
            t.Name.Equals(text, StringComparison.OrdinalIgnoreCase));

        if (match != null && !ViewModel.SelectedTags.Any(s => s.Id == match.Id))
        {
            // User typed a tag name — convert to token; the typed text IS the token so let it clear.
            args.Item = match;
        }
        else
        {
            args.Cancel = true;
            var restore = text;
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (AudiobookSearchBox != null && string.IsNullOrEmpty(AudiobookSearchBox.Text))
                    AudiobookSearchBox.Text = restore;
            });
        }
    }

    private async void AudiobookSearchBox_TokenItemAdded(TokenizingTextBox sender, object args)
    {
        _suppressNextTextClear = false;

        // If the control wiped the inner text during a programmatic token add but SearchText
        // still has a value, restore the visual text so the search term remains visible.
        if (!string.IsNullOrEmpty(ViewModel.SearchText) && string.IsNullOrEmpty(sender.Text))
            sender.Text = ViewModel.SearchText;

        await ApplyFiltersAsync();
    }

    private async void AudiobookSearchBox_TokenItemRemoved(TokenizingTextBox sender, object args)
    {
        // Programmatic chip removal (filter state changed via flyout) — already handled there.
        if (_updatingLengthToken) return;

        try
        {
            if (args is Tag removedTag)
            {
                // Length chip dismissed — reset length filter (token already removed by the control)
                if (IsLengthFilterTag(removedTag))
                {
                    await SetLengthFilter(LengthFilter.Any, updateToken: false);
                    return;
                }

                // Real tag chip dismissed — uncheck the matching row in the Tags dropdown
                var match = _allTagFilterItems.FirstOrDefault(i => i.Tag.Id == removedTag.Id);
                if (match != null) match.IsChecked = false;

                UpdateTagsButtonState();
                UpdateApplyTagsButtonText();
            }

            await ApplyFiltersAsync();
        }
        finally
        {
            // The TokenizingTextBox can drop extra chips during successive removals
            // (container recycling resolves the wrong item; selected chips are mass-removed).
            // After the control settles, restore any chips whose filters are still active.
            _dispatcherQueue.TryEnqueue(ReconcileFilterChips);
        }
    }

    /// <summary>
    ///     Re-adds any filter chips that should be present per the current filter state but are
    ///     missing from the search box. Filter state is the source of truth; this heals chips the
    ///     TokenizingTextBox removed spuriously. No-op when everything is consistent.
    /// </summary>
    private void ReconcileFilterChips()
    {
        _updatingLengthToken = true;
        try
        {
            if (_activeLengthFilter == LengthFilter.Any) RemoveLengthFilterTokens();
            else AddLengthFilterToken(_activeLengthFilter);

            // Checked rows are the source of truth for real tags — re-add any missing chips.
            foreach (var item in _allTagFilterItems)
                if (item.IsChecked && !ViewModel.SelectedTags.Any(t => t.Id == item.Tag.Id))
                    ViewModel.SelectedTags.Add(item.Tag);
        }
        finally
        {
            _updatingLengthToken = false;
        }
    }

    /// <summary>
    ///     Updates search text and suggestions as the user types.
    /// </summary>
    private async void AudiobookSearchBox_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        if (string.IsNullOrEmpty(sender.Text) && _suppressNextTextClear)
        {
            // Control cleared the inner text box because a programmatic token was added —
            // don't wipe the user's search term.
            _suppressNextTextClear = false;
            await ApplyFiltersAsync();
            return;
        }

        _suppressNextTextClear = false;
        ViewModel.SearchText = sender.Text;

        if (string.IsNullOrEmpty(sender.Text))
            sender.ItemsSource = null;
        else
            sender.ItemsSource = GetAudiobookTitles(sender.Text).Concat(GetAudiobookAuthors(sender.Text));

        await ApplyFiltersAsync();
    }

    /// <summary>
    ///     Handles Enter / suggestion selection in the search box.
    /// </summary>
    private async void AudiobookSearchBox_QuerySubmitted(AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var text = args.ChosenSuggestion as string ?? args.QueryText;
        ViewModel.SearchText = text;
        await ApplyFiltersAsync();
    }

    private List<string> GetAudiobookTitles(string text)
    {
        var parameters = text.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        return ViewModel.AudiobooksForFilter
            .Select(a => new
            {
                a.Title,
                Score = parameters.Count(p => a.Title.Contains(p, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Title)
            .ToList();
    }

    private List<string> GetAudiobookAuthors(string text)
    {
        var parameters = text.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        return ViewModel.AudiobooksForFilter
            .Select(a => new
            {
                a.Author,
                Score = parameters.Count(p => a.Author.Contains(p, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Author)
            .Distinct()
            .ToList();
    }

    private async void ViewModelOnClearSearchText()
    {
        ViewModel.SearchText = string.Empty;
        if (AudiobookSearchBox != null)
            AudiobookSearchBox.Text = string.Empty;
        await ApplyFiltersAsync();
    }

    #endregion

    #region Tags filter dropdown

    /// <summary>
    ///     Rebuilds the master row list from AvailableTags, checked to match SelectedTags.
    /// </summary>
    private void RebuildAllTagFilterItems()
    {
        _allTagFilterItems.Clear();
        foreach (var tag in ViewModel.AvailableTags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var isChecked = ViewModel.SelectedTags.Any(s => s.Id == tag.Id);
            _allTagFilterItems.Add(new TagFilterItem(tag, isChecked));
        }
    }

    /// <summary>
    ///     Rebuilds the ListView's bound subset from the master list, narrowed by search text.
    /// </summary>
    private void RefreshDisplayedTagItems(string searchText)
    {
        _tagsFilterDisplayItems.Clear();
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _allTagFilterItems
            : _allTagFilterItems.Where(i => i.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        foreach (var item in filtered) _tagsFilterDisplayItems.Add(item);

        if (NoTagsFoundText != null)
            NoTagsFoundText.Visibility = _tagsFilterDisplayItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateApplyTagsButtonText()
    {
        if (ApplyTagsButton == null) return;
        var count = ViewModel.SelectedTags.Count(t => !IsLengthFilterTag(t));
        ApplyTagsButton.Content = count > 0 ? $"Apply ({count})" : "Apply";
    }

    private void TagsFlyout_OnOpened(object sender, object e)
    {
        RebuildAllTagFilterItems();
        if (TagsSearchBox != null) TagsSearchBox.Text = string.Empty;
        RefreshDisplayedTagItems(string.Empty);
        UpdateApplyTagsButtonText();
        TagsSearchBox?.Focus(FocusState.Programmatic);
    }

    private void TagsSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshDisplayedTagItems(TagsSearchBox.Text);
    }

    /// <summary>
    ///     Tags apply live, like Length and (previously) Status — checking a box immediately
    ///     updates the search-bar pills and re-filters the library. Wired to Click rather than
    ///     Checked/Unchecked so it only fires on real user interaction (see the XAML comment).
    /// </summary>
    private async void TagFilterCheckBox_OnToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: TagFilterItem item } checkBox) return;

        // IsChecked binds OneWay (CheckBox.IsChecked is bool?, TagFilterItem.IsChecked is bool),
        // so push the UI state back into the item manually.
        item.IsChecked = checkBox.IsChecked == true;

        if (checkBox.IsChecked == true)
        {
            if (!ViewModel.SelectedTags.Any(t => t.Id == item.Tag.Id))
                ViewModel.SelectedTags.Add(item.Tag);
        }
        else
        {
            var existing = ViewModel.SelectedTags.FirstOrDefault(t => t.Id == item.Tag.Id);
            if (existing != null) ViewModel.SelectedTags.Remove(existing);
        }

        UpdateTagsButtonState();
        UpdateApplyTagsButtonText();
        await ApplyFiltersAsync();
    }

    private async void ClearAllTags_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in _allTagFilterItems) item.IsChecked = false;

        foreach (var t in ViewModel.SelectedTags.Where(t => !IsLengthFilterTag(t)).ToList())
            ViewModel.SelectedTags.Remove(t);

        UpdateTagsButtonState();
        UpdateApplyTagsButtonText();
        await ApplyFiltersAsync();
    }

    private void ApplyTagsButton_OnClick(object sender, RoutedEventArgs e)
    {
        TagsButton.Flyout.Hide();
    }

    #endregion

    #region Length filter

    private bool IsLengthFilterTag(Tag t) =>
        t.Id == LengthShortId || t.Id == LengthMediumId || t.Id == LengthLongId || t.Id == LengthCustomId;

    private void RemoveLengthFilterTokens()
    {
        var toRemove = ViewModel.SelectedTags.Where(IsLengthFilterTag).ToList();
        foreach (var t in toRemove) ViewModel.SelectedTags.Remove(t);
    }

    private static string FormatHours(double hours) => hours.ToString("0.##");

    private string GetCustomLengthLabel()
    {
        if (_customLengthMinHours.HasValue && _customLengthMaxHours.HasValue)
            return $"{FormatHours(_customLengthMinHours.Value)}h - {FormatHours(_customLengthMaxHours.Value)}h";
        if (_customLengthMinHours.HasValue)
            return $"> {FormatHours(_customLengthMinHours.Value)}h";
        if (_customLengthMaxHours.HasValue)
            return $"< {FormatHours(_customLengthMaxHours.Value)}h";
        return string.Empty;
    }

    private void AddLengthFilterToken(LengthFilter filter)
    {
        var (label, id) = filter switch
        {
            LengthFilter.Short  => ("< 6h",     LengthShortId),
            LengthFilter.Medium => ("6h – 17h", LengthMediumId),
            LengthFilter.Long   => ("> 17h",    LengthLongId),
            LengthFilter.Custom => (GetCustomLengthLabel(), LengthCustomId),
            _                   => (string.Empty, Guid.Empty)
        };
        if (id == Guid.Empty || label.Length == 0) return;
        if (!ViewModel.SelectedTags.Any(t => t.Id == id))
            ViewModel.SelectedTags.Add(new Tag { Name = label, Id = id });
    }

    private void SetLengthButtonUI(LengthFilter filter)
    {
        if (LengthAnyItem == null) return;

        LengthAnyItem.IsChecked    = filter == LengthFilter.Any;
        LengthShortItem.IsChecked  = filter == LengthFilter.Short;
        LengthMediumItem.IsChecked = filter == LengthFilter.Medium;
        LengthLongItem.IsChecked   = filter == LengthFilter.Long;
        LengthCustomItem.IsChecked = filter == LengthFilter.Custom;

        if (filter != LengthFilter.Any)
        {
            LengthButton.BorderBrush     = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            LengthButton.BorderThickness = new Thickness(2);
        }
        else
        {
            LengthButton.BorderBrush     = new SolidColorBrush(Colors.Transparent);
            LengthButton.BorderThickness = new Thickness(0);
        }

        if (LengthFilterDot != null)
            LengthFilterDot.Visibility = (filter != LengthFilter.Any) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SetLengthFilter(LengthFilter filter, bool updateToken = true)
    {
        _activeLengthFilter = filter;
        SetLengthButtonUI(filter);

        if (updateToken)
        {
            _updatingLengthToken = true;
            try
            {
                RemoveLengthFilterTokens();
                if (filter != LengthFilter.Any)
                    AddLengthFilterToken(filter);
            }
            finally
            {
                _updatingLengthToken = false;
            }
        }

        await ApplyFiltersAsync();
    }

    private async void LengthAny_OnClick(object sender, RoutedEventArgs e)    => await SetLengthFilter(LengthFilter.Any);
    private async void LengthShort_OnClick(object sender, RoutedEventArgs e)  => await SetLengthFilter(LengthFilter.Short);
    private async void LengthMedium_OnClick(object sender, RoutedEventArgs e) => await SetLengthFilter(LengthFilter.Medium);
    private async void LengthLong_OnClick(object sender, RoutedEventArgs e)   => await SetLengthFilter(LengthFilter.Long);

    private async void LengthCustom_OnClick(object sender, RoutedEventArgs e)
    {
        var minBox = new TextBox
        {
            Width = 80,
            PlaceholderText = "any",
            Text = _customLengthMinHours.HasValue ? FormatHours(_customLengthMinHours.Value) : string.Empty
        };
        var maxBox = new TextBox
        {
            Width = 80,
            PlaceholderText = "any",
            Text = _customLengthMaxHours.HasValue ? FormatHours(_customLengthMaxHours.Value) : string.Empty
        };

        var inputRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        inputRow.Children.Add(new TextBlock { Text = "Min", VerticalAlignment = VerticalAlignment.Center });
        inputRow.Children.Add(minBox);
        inputRow.Children.Add(new TextBlock { Text = "to Max", VerticalAlignment = VerticalAlignment.Center });
        inputRow.Children.Add(maxBox);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Show only audiobooks whose length falls in this range, in hours. " +
                   "Decimals are allowed (e.g. 7.5). Leave a field blank for no limit on that side.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(inputRow);

        var dialog = new ContentDialog
        {
            Title = "Custom Length Filter",
            Content = content,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            // Cancelled — restore the checkmarks the toggle click just changed
            SetLengthButtonUI(_activeLengthFilter);
            return;
        }

        double? min = double.TryParse(minBox.Text, out var minVal) && minVal >= 0 ? minVal : null;
        double? max = double.TryParse(maxBox.Text, out var maxVal) && maxVal >= 0 ? maxVal : null;

        // Nothing usable entered — treat like cancel
        if (min == null && max == null)
        {
            SetLengthButtonUI(_activeLengthFilter);
            return;
        }

        // Swap if the user reversed the bounds
        if (min.HasValue && max.HasValue && min > max)
            (min, max) = (max, min);

        _customLengthMinHours = min;
        _customLengthMaxHours = max;
        await SetLengthFilter(LengthFilter.Custom);
    }

    #endregion
}
// Author: rstewa · https://github.com/rstewa
// Updated: 03/11/2025

using System;
using System.Collections.Generic;
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
using Audibly.App.ViewModels;
using Audibly.App.Views.ContentDialogs;
using CommunityToolkit.WinUI;
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
    #region AudioBookFilter enum

    public enum AudioBookFilter
    {
        InProgress,
        NotStarted,
        Completed
    }

    #endregion

    public const string ImportAudiobookText = "Import an audiobook (.m4b, mp3)";

    public const string ImportAudiobooksFromDirectoryText =
        "Import all audiobooks in a directory (recursively). Single-file audiobooks only (.m4b, mp3)";

    public const string ImportAudiobookWithMultipleFilesText =
        "Import an audiobook made up of multiple files (.m4b, mp3)";

    public const string ImportFromJsonFileText = "Import audiobooks from an Audibly export file (.audibly)";

    private readonly HashSet<AudioBookFilter> _activeFilters = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    public LibraryCardPage()
    {
        InitializeComponent();

        // subscribe to page loaded event
        Loaded += LibraryCardPage_Loaded;
        Unloaded += LibraryCardPage_Unloaded;
        ViewModel.ResetFilters += ViewModelOnResetFilters;
        ViewModel.SelectedTagsChanged += ViewModelOnSelectedTagsChanged;
        // keep sort UI in sync with ViewModel and persisted settings
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;

        // initialize sort toggles based on saved sort mode
        UpdateSortToggleUI();
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    private void ViewModelOnResetFilters()
    {
        SelectAllFiltersCheckBox.IsChecked = false;
    }

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
        _activeFilters.Clear();

        // unchecked all the filter flyout items
        InProgressFilterCheckBox.IsChecked = false;
        NotStartedFilterCheckBox.IsChecked = false;
        CompletedFilterCheckBox.IsChecked = false;

        // Clear selected tags
        ViewModel.SelectedTags.Clear();

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            ViewModel.Audiobooks.Clear();
            foreach (var a in ViewModel.AudiobooksForFilter) ViewModel.Audiobooks.Add(a);
        });
    }

    private HashSet<AudiobookViewModel> GetFilteredAudiobooks()
    {
        // matches audiobooks for each active filter
        var matches = new HashSet<AudiobookViewModel>();
        var hasTagFilter = ViewModel.SelectedTags.Count > 0;
        var hasProgressFilter = _activeFilters.Count > 0;

        foreach (var audiobook in ViewModel.AudiobooksForFilter)
        {
            var matchesProgressFilter = false;
            var matchesTagFilter = false;

            // Check progress filters
            if (hasProgressFilter)
            {
                if (_activeFilters.Contains(AudioBookFilter.InProgress) && audiobook.Progress > 2 && !audiobook.IsCompleted)
                    matchesProgressFilter = true;
                if (_activeFilters.Contains(AudioBookFilter.NotStarted) && audiobook.Progress == 0 && !audiobook.IsCompleted)
                    matchesProgressFilter = true;
                if (_activeFilters.Contains(AudioBookFilter.Completed) && audiobook.IsCompleted)
                    matchesProgressFilter = true;
            }
            else
            {
                matchesProgressFilter = true; // No progress filter active, so all pass
            }

            // Check tag filters
            if (hasTagFilter)
            {
                // Audiobook must have at least one of the selected tags
                matchesTagFilter = audiobook.Model.Tags.Any(tag => 
                    ViewModel.SelectedTags.Any(selectedTag => 
                        selectedTag.Id == tag.Id));
            }
            else
            {
                matchesTagFilter = true; // No tag filter active, so all pass
            }

            // Audiobook must match both filter types (if active)
            if (matchesProgressFilter && matchesTagFilter)
            {
                matches.Add(audiobook);
            }
        }

        return matches;
    }

    /// <summary>
    ///     Filters the audiobook list based on the search text.
    /// </summary>
    private async Task FilterAudiobookList()
    {
        if (_activeFilters.Count == 0 && ViewModel.SelectedTags.Count == 0)
        {
            await ResetAudiobookListAsync();
            return;
        }

        var matches = GetFilteredAudiobooks();

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            ViewModel.Audiobooks.Clear();
            foreach (var match in matches) ViewModel.Audiobooks.Add(match);
        });
    }

    private void SetCheckedState()
    {
        // Controls are null the first time this is called, so we just 
        // need to perform a null check on any one of the controls.
        if (InProgressFilterCheckBox == null) return;

        // check if any of the filters are checked and change the appbar button background color
        if (InProgressFilterCheckBox.IsChecked == true ||
            NotStartedFilterCheckBox.IsChecked == true ||
            CompletedFilterCheckBox.IsChecked == true)
        {
            FilterButton.BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            FilterButton.BorderThickness = new Thickness(2);
        }
        else
        {
            FilterButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            FilterButton.BorderThickness = new Thickness(0);
        }

        if (InProgressFilterCheckBox.IsChecked == true &&
            NotStartedFilterCheckBox.IsChecked == true &&
            CompletedFilterCheckBox.IsChecked == true)
            SelectAllFiltersCheckBox.IsChecked = true;
        else if (InProgressFilterCheckBox.IsChecked == false &&
                 NotStartedFilterCheckBox.IsChecked == false &&
                 CompletedFilterCheckBox.IsChecked == false)
            SelectAllFiltersCheckBox.IsChecked = false;
        else
            // Set third state (indeterminate) by setting IsChecked to null.
            SelectAllFiltersCheckBox.IsChecked = null;
    }

    private async void InProgressFilterCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        SetCheckedState();

        _activeFilters.Add(AudioBookFilter.InProgress);

        await FilterAudiobookList();
    }

    private async void NotStartedFilterCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        SetCheckedState();

        _activeFilters.Add(AudioBookFilter.NotStarted);

        await FilterAudiobookList();
    }

    private async void CompletedFilterCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        SetCheckedState();

        _activeFilters.Add(AudioBookFilter.Completed);

        await FilterAudiobookList();
    }

    private async void InProgressFilterCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        SetCheckedState();

        _activeFilters.Remove(AudioBookFilter.InProgress);

        await FilterAudiobookList();
    }

    private async void NotStartedFilterCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        SetCheckedState();

        _activeFilters.Remove(AudioBookFilter.NotStarted);

        await FilterAudiobookList();
    }

    private async void CompletedFilterCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        SetCheckedState();

        _activeFilters.Remove(AudioBookFilter.Completed);

        await FilterAudiobookList();
    }

    private async void SelectAllFiltersCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        InProgressFilterCheckBox.IsChecked =
            NotStartedFilterCheckBox.IsChecked = CompletedFilterCheckBox.IsChecked = true;
    }

    private async void SelectAllFiltersCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        InProgressFilterCheckBox.IsChecked =
            NotStartedFilterCheckBox.IsChecked = CompletedFilterCheckBox.IsChecked = false;
    }

    private void SelectAllFiltersCheckBox_OnIndeterminate(object sender, RoutedEventArgs e)
    {
        if (InProgressFilterCheckBox.IsChecked == true && NotStartedFilterCheckBox.IsChecked == true &&
            CompletedFilterCheckBox.IsChecked == true)
            SelectAllFiltersCheckBox.IsChecked = false;
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
        }
    }

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
        ViewModel.SelectedTagsChanged -= ViewModelOnSelectedTagsChanged;
    }

    private async void ViewModelOnSelectedTagsChanged(object? sender, EventArgs e)
    {
        await FilterAudiobookList();
    }

    #region Search functionality

    private void AudiobookSearchBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (AudiobookSearchBox == null) return;
        AudiobookSearchBox.QuerySubmitted += AudiobookSearchBox_QuerySubmitted;
        AudiobookSearchBox.TextChanged += AudiobookSearchBox_TextChanged;
    }

    /// <summary>
    ///     Filters or resets the audiobook list based on the search text.
    /// </summary>
    private async void AudiobookSearchBox_QuerySubmitted(AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.QueryText))
            await ViewModel.ResetAudiobookListAsync();
        else
            await FilterAudiobookListBySearch(args.QueryText);
    }

    private List<AudiobookViewModel> GetFilteredAudiobooksBySearch(string text)
    {
        var parameters = text.Split([' '],
            StringSplitOptions.RemoveEmptyEntries);

        var matches = ViewModel.Audiobooks
            .Select(audiobook => new
            {
                Audiobook = audiobook,
                Score = parameters.Count(parameter =>
                    audiobook.Author.Contains(parameter, StringComparison.OrdinalIgnoreCase) ||
                    audiobook.Title.Contains(parameter, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(audiobook.Description) &&
                     audiobook.Description.Contains(parameter, StringComparison.OrdinalIgnoreCase)))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Audiobook)
            .ToList();

        var exactMatches = matches.Where(audiobook =>
            audiobook.Author.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            audiobook.Title.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(audiobook.Description) &&
             audiobook.Description.Equals(text, StringComparison.OrdinalIgnoreCase))).ToList();

        return exactMatches.Count != 0 ? exactMatches : matches;
    }

    /// <summary>
    ///     Filters the audiobook list based on the search text.
    /// </summary>
    private async Task FilterAudiobookListBySearch(string text)
    {
        var matches = GetFilteredAudiobooksBySearch(text);

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            ViewModel.Audiobooks.Clear();
            foreach (var match in matches) ViewModel.Audiobooks.Add(match);
        });
    }

    /// <summary>
    ///     Updates the search box items source when the user changes the search text.
    /// </summary>
    private async void AudiobookSearchBox_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        // We only want to get results when it was a user typing,
        // otherwise we assume the value got filled in by TextMemberPath
        // or the handler for SuggestionChosen.
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            // If no search query is entered, refresh the complete list.
            if (string.IsNullOrEmpty(sender.Text))
            {
                await _dispatcherQueue.EnqueueAsync(async () =>
                    await ViewModel.GetAudiobookListAsync());
                sender.ItemsSource = null;
            }
            else
            {
                sender.ItemsSource = GetAudiobookTitles(sender.Text).Concat(GetAudiobookAuthors(sender.Text));
                await FilterAudiobookListBySearch(sender.Text);
            }
        }
    }

    private List<string> GetAudiobookTitles(string text)
    {
        var parameters = text.Split([' '],
            StringSplitOptions.RemoveEmptyEntries);

        return ViewModel.Audiobooks
            .Select(audiobook => new
            {
                audiobook.Title,
                Score = parameters.Count(parameter =>
                    audiobook.Title.Contains(parameter, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Title)
            .ToList();
    }

    private List<string> GetAudiobookAuthors(string text)
    {
        var parameters = text.Split([' '],
            StringSplitOptions.RemoveEmptyEntries);

        return ViewModel.Audiobooks
            .Select(audiobook => new
            {
                audiobook.Author,
                Score = parameters.Count(parameter =>
                    audiobook.Author.Contains(parameter, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Author)
            .Distinct()
            .ToList();
    }

    #endregion
}
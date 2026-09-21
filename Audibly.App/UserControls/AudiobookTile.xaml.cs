// Author: rstewa · https://github.com/rstewa
// Updated: 06/09/2025

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Audibly.App.Extensions;
using Audibly.App.Helpers;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.Models;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using CommunityToolkit.WinUI.Media;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using ColorHelper = CommunityToolkit.WinUI.Helpers.ColorHelper;

namespace Audibly.App.UserControls;

public sealed partial class AudiobookTile : UserControl
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private ObservableCollection<Audibly.Models.Bookmark> _bookmarks = new();
    private readonly BookmarkService _bookmarkService = new();
    private bool _playButtonHovered;
    private bool _ellipsisButtonHovered;

    public AudiobookTile()
    {
        InitializeComponent();
    }

    private MenuFlyout? GetMenuFlyout() => Resources["MenuFlyout"] as MenuFlyout;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    private static PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    private MainViewModel ViewModel => App.ViewModel;

    public Guid Id
    {
        get => (Guid)GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    public static readonly DependencyProperty IdProperty =
        DependencyProperty.Register(nameof(Id), typeof(Guid), typeof(AudiobookTile), new PropertyMetadata(Guid.Empty));

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(AudiobookTile), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AudiobookTile), new PropertyMetadata(string.Empty));

    public string Author
    {
        get => (string)GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }
    public static readonly DependencyProperty AuthorProperty =
        DependencyProperty.Register(nameof(Author), typeof(string), typeof(AudiobookTile), new PropertyMetadata(string.Empty));

    public bool IsCompleted
    {
        get => (bool)GetValue(IsCompletedProperty);
        set => SetValue(IsCompletedProperty, value);
    }
    public static readonly DependencyProperty IsCompletedProperty =
        DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(AudiobookTile), new PropertyMetadata(false));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(AudiobookTile), new PropertyMetadata(0.0));

    public System.Collections.Generic.List<SourceFile> SourcePaths
    {
        get => (System.Collections.Generic.List<SourceFile>)GetValue(SourcePathsProperty);
        set => SetValue(SourcePathsProperty, value);
    }
    public static readonly DependencyProperty SourcePathsProperty =
        DependencyProperty.Register(nameof(SourcePaths), typeof(System.Collections.Generic.List<SourceFile>), typeof(AudiobookTile), new PropertyMetadata(null));

    public int SourcePathsCount
    {
        get => (int)GetValue(SourcePathsCountProperty);
        set => SetValue(SourcePathsCountProperty, value);
    }
    public static readonly DependencyProperty SourcePathsCountProperty =
        DependencyProperty.Register(nameof(SourcePathsCount), typeof(int), typeof(AudiobookTile), new PropertyMetadata(0));

    public long Duration
    {
        get => (long)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(long), typeof(AudiobookTile), new PropertyMetadata(0L));

    public int ChapterCount
    {
        get => (int)GetValue(ChapterCountProperty);
        set => SetValue(ChapterCountProperty, value);
    }
    public static readonly DependencyProperty ChapterCountProperty =
        DependencyProperty.Register(nameof(ChapterCount), typeof(int), typeof(AudiobookTile), new PropertyMetadata(0));

    public object Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(AudiobookTile), new PropertyMetadata(null));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(AudiobookTile),
            new PropertyMetadata(false, static (d, _) => ((AudiobookTile)d).UpdateSelectionVisual()));

    private void UpdateSelectionVisual()
    {
        if (ButtonTile == null) return;
        if (IsSelected)
        {
            ButtonTile.BorderBrush =
                Application.Current.Resources.TryGetValue("SystemAccentColorBrush", out var b) && b is Brush accent
                ? accent : new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue);
            ButtonTile.BorderThickness = new Thickness(2);
            if (SelectionBadge != null) SelectionBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ButtonTile.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ButtonTile.BorderThickness = new Thickness(0);
            if (SelectionBadge != null) SelectionBadge.Visibility = Visibility.Collapsed;
        }
    }

    private MenuFlyout? GetMultiSelectMenuFlyout() => Resources["MultiSelectFlyout"] as MenuFlyout;

    private void AudiobookTile_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BlackOverlayGrid.Visibility = Visibility.Visible;
        ButtonTile.Background = new SolidColorBrush(ColorHelper.ToColor("#393939"));
        if (!IsSelected)
        {
            ButtonTile.BorderBrush = new SolidColorBrush(ColorHelper.ToColor("#707070"));
            ButtonTile.BorderThickness = new Thickness(2);
        }
        if (Effects.GetShadow(CoverShadowBorder) is AttachedCardShadow shadow)
            shadow.Offset = "6, 6";
    }

    private void AudiobookTile_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Defer so that PlayButton_OnPointerEntered can set _playButtonHovered first when
        // moving from the tile surface into the nested play button.
        _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (_playButtonHovered || _ellipsisButtonHovered) return;
            var flyout = GetMenuFlyout();
            if (flyout?.IsOpen == true || GetMultiSelectMenuFlyout()?.IsOpen == true) return;
            BlackOverlayGrid.Visibility = Visibility.Collapsed;
            ButtonTile.Background = new SolidColorBrush(Colors.Transparent);
            if (!IsSelected)
            {
                ButtonTile.BorderBrush = new SolidColorBrush(Colors.Transparent);
                ButtonTile.BorderThickness = new Thickness(0);
            }
            if (Effects.GetShadow(CoverShadowBorder) is AttachedCardShadow shadow)
                shadow.Offset = "4, 4";
        });
    }

    private void PlayButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _playButtonHovered = true;
        PlayButton.Opacity = 1.0;
        PlayCircle.Opacity = 0.45;
        if (PlayButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.2;
            transform.ScaleY = 1.2;
        }
    }

    private void PlayButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _playButtonHovered = false;
        PlayButton.Opacity = 0.75;
        PlayCircle.Opacity = 0;
        if (PlayButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        }
        BlackOverlayGrid.Visibility = Visibility.Visible;
    }

    private void EllipsisButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ellipsisButtonHovered = true;
        EllipsisButton.Opacity = 1.0;
        EllipsisCircle.Opacity = 0.45;
        if (EllipsisButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.2;
            transform.ScaleY = 1.2;
        }
    }

    private void EllipsisButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _ellipsisButtonHovered = false;
        EllipsisButton.Opacity = 0.75;
        EllipsisCircle.Opacity = 0;
        if (EllipsisButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        }
        BlackOverlayGrid.Visibility = Visibility.Visible;
    }

    private void EllipsisButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSelection();
        var options = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard };
        GetMenuFlyout()?.ShowAt(EllipsisButton, options);
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSelection();
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        try
        {
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                var currentAudiobook = PlayerViewModel.NowPlaying;
                if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
                    await PlayerViewModel.OpenAudiobook(audiobook);
                PlayerViewModel.MediaPlayer.Play();
            });
        }
        catch (Exception ex)
        {
            ViewModel.LoggingService.LogError(ex, true);
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "Failed to open/play audiobook.",
                Severity = InfoBarSeverity.Error
            });
        }
    }

    private void MenuFlyout_Closed(object sender, object e)
    {
        BlackOverlayGrid.Visibility = Visibility.Collapsed;
        ButtonTile.Background = new SolidColorBrush(Colors.Transparent);
        if (!IsSelected)
        {
            ButtonTile.BorderBrush = new SolidColorBrush(Colors.Transparent);
            ButtonTile.BorderThickness = new Thickness(0);
        }
        if (Effects.GetShadow(CoverShadowBorder) is AttachedCardShadow shadow)
            shadow.Offset = "4, 4";
    }

    private void ButtonTile_Click(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
        var isShiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlPressed)
        {
            audiobook.IsSelected = !audiobook.IsSelected;
            ViewModel.SelectionAnchorId = Id;
            return;
        }

        if (isShiftPressed && ViewModel.SelectionAnchorId.HasValue)
        {
            var allBooks = ViewModel.Audiobooks.ToList();
            var anchorIdx = allBooks.FindIndex(a => a.Id == ViewModel.SelectionAnchorId.Value);
            var thisIdx = allBooks.FindIndex(a => a.Id == Id);
            if (anchorIdx >= 0 && thisIdx >= 0)
            {
                var min = Math.Min(anchorIdx, thisIdx);
                var max = Math.Max(anchorIdx, thisIdx);
                for (var i = 0; i < allBooks.Count; i++)
                    allBooks[i].IsSelected = i >= min && i <= max;
            }
            return;
        }

        // Plain click — select this tile (clear any other selection first)
        ViewModel.ClearSelection();
        audiobook.IsSelected = true;
        ViewModel.SelectionAnchorId = Id;
    }

    private void ShowInFileExplorer_OnClick(object sender, RoutedEventArgs e)
    {
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/select, \"{FilePath}\"";
        p.Start();
    }

    private async void DeleteAudiobook_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        ViewModel.SelectedAudiobook = audiobook;

        await ViewModel.DeleteAudiobookAsync();
    }

    private async void RefreshCover_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        await DialogService.RefreshCoverFromFolderAsync(audiobook);
    }

    private void ButtonTile_OnRightTapped(object sender, RightTappedRoutedEventArgs? e)
    {
        if (e is null) return;
        var options = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard };
        if (ViewModel.Audiobooks.Count(a => a.IsSelected) >= 2)
            GetMultiSelectMenuFlyout()?.ShowAt(ButtonTile, options);
        else
            GetMenuFlyout()?.ShowAt(ButtonTile, options);
    }

    private void OpenInAppFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        var dir = System.IO.Path.GetDirectoryName(audiobook.CoverImagePath);
        if (dir == null) return;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{dir}\"";
        p.Start();
    }

    private async void MoreInfo_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        var flyout = GetMenuFlyout();
        flyout?.Hide();

        var editRequested = await DialogService.ShowMoreInfoDialogAsync(audiobook);
        if (editRequested)
            await DialogService.ShowEditAudiobookDialogAsync(audiobook);
    }

    private async void MarkAsCompleted_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        audiobook.IsCompleted = true;
        await audiobook.SaveAsync();
    }

    private async void MarkAsIncomplete_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        audiobook.IsCompleted = false;
        await audiobook.SaveAsync();
    }

    private void ExportMetadataToJson_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        ViewModel.AppDataService.ExportMetadataAsync(audiobook.SourcePaths)
            .ContinueWith(task =>
            {
                if (task.IsFaulted) App.ViewModel.LoggingService.LogError(task.Exception, true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private T? GetFlyoutElement<T>(object sender, string name) where T : FrameworkElement
    {
        if (sender is Flyout flyout && flyout.Content is FrameworkElement root)
            return root.FindName(name) as T;
        return null;
    }

    private static string TagsToCommaSeparatedString(List<Tag> tags)
    {
        if (tags == null || tags.Count == 0)
            return string.Empty;

        return string.Join(", ", tags.Select(t => t.Name));
    }

    private async void DeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Audiobooks.Count(a => a.IsSelected);
        if (count == 0) return;

        var dialog = new ContentDialog
        {
            Title = "Delete selected",
            Content = $"Permanently delete {count} audiobook{(count == 1 ? "" : "s")}? This cannot be undone.",
            PrimaryButtonText = "Delete",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteSelectedAudiobooksAsync();
    }

    private async void ManageTagsSelected_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedAudiobooks = ViewModel.Audiobooks.Where(a => a.IsSelected).ToList();
        await DialogService.ShowManageTagsDialogAsync(selectedAudiobooks);
    }

    private async void EditInfo_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        await DialogService.ShowEditAudiobookDialogAsync(audiobook);
    }

    private async void ButtonTile_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        e.Handled = true;

        try
        {
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                // Load the audiobook if it's not already loaded or if it's a different one
                var currentAudiobook = PlayerViewModel.NowPlaying;
                if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
                {
                    await PlayerViewModel.OpenAudiobook(audiobook);
                }

                // Start playing
                PlayerViewModel.MediaPlayer.Play();
            });
        }
        catch (Exception ex)
        {
            ViewModel.LoggingService.LogError(ex, true);
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "Failed to open/play audiobook.",
                Severity = InfoBarSeverity.Error
            });
        }
    }
    private async void PlayAudiobook_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var currentAudiobook = PlayerViewModel.NowPlaying;

            // Load the audiobook if it's not already loaded or if it's a different one
            if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
            {
                await PlayerViewModel.OpenAudiobook(audiobook);
            }

            // Always start playing
            PlayerViewModel.MediaPlayer.Play();
        });
    }

    private async void OpenInMiniPlayer_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var currentAudiobook = PlayerViewModel.NowPlaying;
            if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
                await PlayerViewModel.OpenAudiobook(audiobook);

            WindowHelper.ShowMiniPlayer();
        });
    }
}
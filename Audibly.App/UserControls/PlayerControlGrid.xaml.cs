// Author: rstewa · https://github.com/rstewa
// Updated: 12/26/2025

using Audibly.App.Extensions;
using Audibly.App.Helpers;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.App.Views;
using Audibly.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.System;
using Constants = Audibly.App.Helpers.Constants;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Audibly.App.UserControls;

public sealed partial class PlayerControlGrid : UserControl
{
    public static readonly DependencyProperty ShowCoverImageProperty =
        DependencyProperty.Register(nameof(ShowCoverImage), typeof(bool), typeof(PlayerControlGrid),
            new PropertyMetadata(true));

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _playbackSpeedFlyoutCts;

    private ObservableCollection<Bookmark> _bookmarks = new();
    private readonly BookmarkService _bookmarkService = new();

    public PlayerControlGrid()
    {
        InitializeComponent();
        AudioPlayer.SetMediaPlayer(PlayerViewModel.MediaPlayer);

        // allow '[' ']' and '\\' to work while the playback-speed flyout is open
        if (PlaybackSpeedSlider != null)
            PlaybackSpeedSlider.KeyDown += PlaybackSpeedSlider_KeyDown;

        // ensure any flyout opened via button still focuses the slider
        if (PlaybackSpeedButton?.Flyout is Flyout f)
            f.Opened += (_, _) => PlaybackSpeedSlider?.Focus(FocusState.Programmatic);

        // Handle global keys for Now Playing view (only enable focus/focus capture when we are on PlayerPage)
        KeyDown += PlayerControlGrid_KeyDown;
        Loaded += (_, _) =>
        {
            // Only try to take focus when in the Now Playing page so we don't steal focus elsewhere
            if (App.RootFrame?.Content is PlayerPage)
            {
                IsTabStop = true;
                Focus(FocusState.Programmatic);
            }
            
            // Sync the volume and playback speed sliders with current ViewModel values
            if (VolumeLevelSlider != null)
                VolumeLevelSlider.Value = PlayerViewModel.VolumeLevel;
            if (PlaybackSpeedSlider != null)
                PlaybackSpeedSlider.Value = PlayerViewModel.PlaybackSpeed;
        };
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    public bool ShowCoverImage
    {
        get => (bool)GetValue(ShowCoverImageProperty);
        set => SetValue(ShowCoverImageProperty, value);
    }

    private async void ChapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var container = sender as ComboBox;
        if (container == null || container.SelectedItem is not ChapterInfo chapter) return;

        // get newly selected chapter
        var newChapter = container.SelectedItem as ChapterInfo;

        // check if the newly selected chapter is the same as the current chapter
        if (PlayerViewModel.NowPlaying?.CurrentChapter != null &&
            PlayerViewModel.NowPlaying.CurrentChapter.Equals(newChapter)) return;

        if (newChapter == null) return;

        // Calculate the absolute position for the start of the new chapter
        long absolutePositionMs = 0;
        for (var i = 0; i < newChapter.ParentSourceFileIndex; i++)
        {
            absolutePositionMs += (long)(PlayerViewModel.NowPlaying.SourcePaths[i].Duration * 1000);
        }
        absolutePositionMs += (long)newChapter.StartTime;
        
        // Always update CurrentTimeMs to the absolute position
        PlayerViewModel.NowPlaying.CurrentTimeMs = (int)absolutePositionMs;

        // Update progress and notify UI since chapter changed
        PlayerViewModel.UpdateAudiobookProgress();
        PlayerViewModel.NowPlaying.RefreshProgress();

        // check if the newly selected chapter is in a different source file than the current chapter
        if (PlayerViewModel.NowPlaying != null &&
            PlayerViewModel.NowPlaying.CurrentSourceFile.Index != newChapter.ParentSourceFileIndex)
        {
            // remember play status so we can resume if it was playing before navigation
            var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

            // set the current source file index to the new source file index
            PlayerViewModel.OpenSourceFile(newChapter.ParentSourceFileIndex, newChapter.Index);

            // If playback was ongoing before navigation, resume it.
            // Calling Play() here is safe: MediaPlayer.Play() will start playback once media is opened/ready.
            if (wasPlaying)
                PlayerViewModel.MediaPlayer.Play();
            
            // SaveAsync is called inside OpenSourceFile, no need to call it again here
        }
        else if (ChapterCombo.SelectedIndex != ChapterCombo.Items.IndexOf(PlayerViewModel.NowPlaying?.CurrentChapter))
        {
            // Same source file - just update the position
            PlayerViewModel.CurrentPosition = TimeSpan.FromMilliseconds(newChapter.StartTime);
            await PlayerViewModel.NowPlaying.SaveAsync();
        }
    }

    private void OpenMiniPlayerButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowHelper.ShowMiniPlayer();
    }

    private void MaximizePlayerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!PlayerViewModel.IsPlayerFullScreen)
        {
            PlayerViewModel.IsPlayerFullScreen = true;
            PlayerViewModel.MaximizeMinimizeGlyph = Constants.MinimizeGlyph;
            PlayerViewModel.MaximizeMinimizeTooltip = Constants.MinimizeTooltip;

            if (App.RootFrame?.Content is not PlayerPage)
                App.RootFrame?.Navigate(typeof(PlayerPage));

            // App.Window.MakeWindowFullScreen();
        }
        else
        {
            PlayerViewModel.IsPlayerFullScreen = false;
            PlayerViewModel.MaximizeMinimizeGlyph = Constants.MaximizeGlyph;
            PlayerViewModel.MaximizeMinimizeTooltip = Constants.MaximizeTooltip;
            if (App.RootFrame?.Content is PlayerPage)
                App.RootFrame?.Navigate(typeof(AppShell));
            // App.Window.RestoreWindow();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null || !IsLoaded) return;

        PlayerViewModel.UpdateVolume(slider.Value);
    }

    private void PlaybackSpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null || !IsLoaded) return;

        PlayerViewModel.UpdatePlaybackSpeed(slider.Value);
    }

    private void TimerButton_OnClick(object sender, RoutedEventArgs e)
    {
        // todo
    }

    private void TimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && int.TryParse(item.Tag?.ToString(), out var seconds))
            PlayerViewModel.SetTimer(seconds);
    }

    private void CustomTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Create the popup
        var timerPopup = new CustomTimerPopup();

        // Find the timer button and position the popup above it

        // get timer button's position using its x:name
        var timerButton = TimerButton;
        if (timerButton == null)
        {
            // Fallback if the button is not found
            timerPopup.Show(XamlRoot);
            return;
        }

        // Show the popup above the button
        timerPopup.ShowAbove(timerButton, XamlRoot);
    }

    private void CancelTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PlayerViewModel.SetTimer(0);
    }

    private void EndOfChapterTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.NowPlaying?.CurrentChapter == null) return;

        var endTime = PlayerViewModel.NowPlaying.CurrentChapter.EndTime;
        var currentPosition = PlayerViewModel.CurrentPosition.TotalMilliseconds;
        var timerDuration = endTime - currentPosition;

        // convert to seconds
        timerDuration = timerDuration.ToSeconds().ToInt();

        if (timerDuration > 0) PlayerViewModel.SetTimer(timerDuration);
    }

    private T? GetFlyoutElement<T>(object sender, string name) where T : FrameworkElement
    {
        if (sender is Flyout flyout && flyout.Content is FrameworkElement root)
            return root.FindName(name) as T;
        return null;
    }

    private async void BookmarksFlyout_Opened(object sender, object e)
    {
        if (PlayerViewModel.NowPlaying == null) return;
        _bookmarks = await _bookmarkService.GetBookmarksAsync(PlayerViewModel.NowPlaying.Id);
        var list = GetFlyoutElement<ListView>(sender, "BookmarksListView");
        if (list != null) list.ItemsSource = _bookmarks;

        // Wire Enter key on the note TextBox so Enter behaves like clicking Add
        var flyout = BookmarksButton?.Flyout as Flyout;
        var root = flyout?.Content as FrameworkElement;
        var noteBox = root?.FindName("BookmarksNoteTextBox") as TextBox;
        if (noteBox != null)
        {
            // Ensure we don't double-subscribe
            noteBox.KeyDown -= BookmarksNoteTextBox_KeyDown;
            noteBox.KeyDown += BookmarksNoteTextBox_KeyDown;
        }

        // Attach a closed handler so we can detach the KeyDown handler when the flyout closes
        if (flyout != null)
        {
            flyout.Closed -= BookmarksFlyout_Closed;
            flyout.Closed += BookmarksFlyout_Closed;
        }
    }

    private async void AddBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.NowPlaying == null) return;
        try
        {
            // Resolve the note TextBox from this control's Flyout content
            var flyout = BookmarksButton?.Flyout as Flyout;
            var root = flyout?.Content as FrameworkElement;
            var noteBox = root?.FindName("BookmarksNoteTextBox") as TextBox;

            var noteText = noteBox?.Text?.Trim() ?? string.Empty;
            var saved = await _bookmarkService.AddBookmarkForCurrentPositionAsync(PlayerViewModel, noteText, _bookmarks);
            if (saved == null) return;

            // Try to find the ListView inside the flyout that displays bookmarks
            var list = GetFlyoutElement<ListView>(flyout, "BookmarksListView");

            if (list != null)
            {
                // Let the UI thread process bindings/layout at least once
                await Task.Yield();

                // Poll for the saved bookmark to appear in the ListView items (timeout ~1000ms)
                const int intervalMs = 50;
                const int maxAttempts = 20; // 20 * 50ms = 1000ms
                var found = false;

                for (var attempt = 0; attempt < maxAttempts; attempt++)
                {
                    // Check items for either reference equality or matching Id
                    if (list.Items.Cast<object>().Any(item =>
                        ReferenceEquals(item, saved) ||
                        (item is Bookmark b && saved is Bookmark s && b.Id == s.Id)))
                    {
                        found = true;
                        break;
                    }

                    await Task.Delay(intervalMs);
                }

                // Small extra delay when found to let visuals stabilize
                if (found)
                    await Task.Delay(1000);
            }

            if (noteBox != null) noteBox.Text = string.Empty;

            // Close the Bookmarks flyout after adding and after the item is observed (or timeout)
            flyout?.Hide();
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Bookmark bookmark)
        {
            // remember play status so we can resume if it was playing before navigation
            var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

            _bookmarkService.NavigateToBookmark(bookmark);

            // If playback was ongoing before navigation, resume it.
            // Calling Play() here is safe: MediaPlayer.Play() will start playback once media is opened/ready.
            if (wasPlaying)
                PlayerViewModel.MediaPlayer.Play();

            // Close the Bookmarks flyout after navigating
            (BookmarksButton?.Flyout as Flyout)?.Hide();
        }
    }

    private async void DeleteBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Bookmark bookmark)
        {
            try
            {
                button.IsEnabled = false; // prevent double-click re-entrancy
                await _bookmarkService.DeleteBookmarkAsync(bookmark, _bookmarks);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // Row was already deleted; update UI and swallow
                _bookmarks.Remove(bookmark);
            }
            catch (Exception ex)
            {
                App.ViewModel.LoggingService?.LogError(ex, true);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    // Public API used by AppShell to adjust speed via '[' and ']'
    public void IncreasePlaybackSpeed()
    {
        ShowPlaybackSpeedFlyout();
        var newValue = PlaybackSpeedSlider.Value + Constants.PlaybackSpeedIncrement;
        if (newValue >= Constants.PlaybackSpeedMaximum) newValue = Constants.PlaybackSpeedMaximum;
        PlaybackSpeedSlider.Value = newValue;
    }

    public void DecreasePlaybackSpeed()
    {
        ShowPlaybackSpeedFlyout();
        var newValue = PlaybackSpeedSlider.Value - Constants.PlaybackSpeedIncrement;
        if (newValue <= Constants.PlaybackSpeedMinimum) newValue = Constants.PlaybackSpeedMinimum;
        PlaybackSpeedSlider.Value = newValue;
    }

    public void ResetPlaybackSpeed()
    {
        ShowPlaybackSpeedFlyout();
        PlaybackSpeedSlider.Value = Constants.PlaybackSpeedDefault;
    }

    private void ShowPlaybackSpeedFlyout()
    {
        if (PlaybackSpeedButton?.Flyout is Flyout flyout)
        {
            flyout.ShowAt(PlaybackSpeedButton);
            // focus slider so keyboard interactions (if any) go there
            PlaybackSpeedSlider.Focus(FocusState.Programmatic);
            ClosePlaybackSpeedFlyout();
        }
    }

    private void ClosePlaybackSpeedFlyout()
    {
        // Cancel any previous close operation
        _playbackSpeedFlyoutCts?.Cancel();
        _playbackSpeedFlyoutCts = new CancellationTokenSource();
        var token = _playbackSpeedFlyoutCts.Token;

        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await Task.Delay(2000, token);
                if (PlaybackSpeedButton?.Flyout is Flyout flyout && flyout.IsOpen)
                    flyout.Hide();
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        });
    }

    // Handle bracket keys while the slider has focus (flyout open)
    private void PlaybackSpeedSlider_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key == (VirtualKey)219) // '['
        {
            DecreasePlaybackSpeed();
            e.Handled = true;
        }
        else if (key == (VirtualKey)221) // ']'
        {
            IncreasePlaybackSpeed();
            e.Handled = true;
        }
        else if (key == (VirtualKey)0xDC) // backslash '\\'
        {
            ResetPlaybackSpeed();
            e.Handled = true;
        }
    }

    // Capture Up/Down and bracket/backslash in the Now Playing view and show the same slider as mini-player
    private void PlayerControlGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key == (VirtualKey)219) // Open bracket '['
        {
            DecreasePlaybackSpeed();
            ClosePlaybackSpeedFlyout();
            e.Handled = true;
        }
        else if (key == (VirtualKey)221) // Close bracket ']'
        {
            IncreasePlaybackSpeed();
            ClosePlaybackSpeedFlyout();
            e.Handled = true;
        }
        else if (key == (VirtualKey)0xDC) // Backslash '\\'
        {
            ResetPlaybackSpeed();
            ClosePlaybackSpeedFlyout();
            e.Handled = true;
        }
        else if (key == VirtualKey.Up) // Increase speed with Up arrow
        {
            IncreasePlaybackSpeed();
            ClosePlaybackSpeedFlyout();
            e.Handled = true;
        }
        else if (key == VirtualKey.Down) // Decrease speed with Down arrow
        {
            DecreasePlaybackSpeed();
            ClosePlaybackSpeedFlyout();
            e.Handled = true;
        }
        else if (key == VirtualKey.Space)
        {
            // Suppress play/pause when typing in the Bookmarks flyout (e.g., the note TextBox)
            if (IsInBookmarksFlyoutTextEditing())
            {
                // Let the TextBox receive the space character
                e.Handled = false;
                return;
            }

            // Toggle play/pause
            var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            if (wasPlaying)
                PlayerViewModel.MediaPlayer.Pause();
            else
                PlayerViewModel.MediaPlayer.Play();

            e.Handled = true;
        }
    }

    // Checks whether focus is currently inside the Bookmarks flyout and on a text-edit control
    private bool IsInBookmarksFlyoutTextEditing()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (focused == null) return false;

        // If focus is on a TextBox (or within its visual tree) inside the Bookmarks flyout, return true
        // Find the flyout root
        var flyout = BookmarksButton?.Flyout as Flyout;
        var root = flyout?.Content as FrameworkElement;
        if (root == null) return false;

        // Walk up the visual tree from the focused element to see if it's within the flyout content
        var current = focused;
        while (current != null)
        {
            if (current == root)
                break;

            current = VisualTreeHelper.GetParent(current);
        }

        // If not within the flyout, ignore
        if (current != root) return false;

        // If the focused element is a text input (TextBox or RichEditBox), suppress space handling
        var isTextInput =
            focused is TextBox ||
            focused is RichEditBox;

        return isTextInput;
    }

    // KeyDown handler for the Bookmarks note TextBox - Enter adds the bookmark
    private void BookmarksNoteTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // Prevent newline and further propagation
            e.Handled = true;

            // Invoke add bookmark (same behavior as the Add button)
            try
            {
                AddBookmark_Click(this, null);
            }
            catch (Exception ex)
            {
                App.ViewModel.LoggingService?.LogError(ex, true);
            }
        }
    }

    // Clean up subscriptions when the flyout closes
    private void BookmarksFlyout_Closed(object sender, object e)
    {
        var flyout = sender as Flyout ?? BookmarksButton?.Flyout as Flyout;
        var root = flyout?.Content as FrameworkElement;
        var noteBox = root?.FindName("BookmarksNoteTextBox") as TextBox;
        if (noteBox != null)
        {
            noteBox.KeyDown -= BookmarksNoteTextBox_KeyDown;
        }

        if (flyout != null)
        {
            flyout.Closed -= BookmarksFlyout_Closed;
        }
    }
}
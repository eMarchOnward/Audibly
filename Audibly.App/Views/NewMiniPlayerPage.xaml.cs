// Author: rstewa · https://github.com/rstewa
// Updated: 12/26/2025
// Updated: 01/01/2026 - add global hotkey hook while mini-player pinned

using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.System;
using Audibly.App.Extensions;
using Audibly.App.Helpers;
using Audibly.App.UserControls;
using Audibly.App.ViewModels;
using Audibly.Models;
using Audibly.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using CommunityToolkit.WinUI;

using AppConstants = Audibly.App.Helpers.Constants;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics; // for Trace

namespace Audibly.App.Views;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class NewMiniPlayerPage : Page
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _playbackSpeedFlyoutCts;
    private GlobalKeyboardHook? _globalKeyboardHook; // <-- added

    // todo: fix the bug where this is getting triggered even when the user hasn't clicked the slider
    private bool _isUserDragging = false;
    private bool _wasPlayingBeforeDrag = false;

    private ObservableCollection<Bookmark> _bookmarks = new();
    private readonly BookmarkService _bookmarkService = new();

    public NewMiniPlayerPage()
    {
        InitializeComponent();
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Loaded += NewMiniPlayerPage_Loaded;
        Unloaded += NewMiniPlayerPage_Unloaded;
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    private void NewMiniPlayerPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Attach pointer events to the slider itself with handledEventsToo=true
        // This is more reliable than trying to find the thumb
        PositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Slider_OnPointerPressed), true);
        PositionSlider.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Slider_OnPointerMoved), true);
        PositionSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Slider_OnPointerReleased), true);
        PositionSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Slider_OnPointerCaptureLost), true);

        // Wire keyboard handling so the page receives KeyDown events.
        // Ensures the page has focus and listens for key presses (Up/Down, [, ], backslash).
        this.KeyDown += NewMiniPlayerPage_KeyDown;
        // Request focus so that KeyDown will fire when the page is visible.
        // If another control must keep focus, consider using KeyboardAccelerators instead.
        _ = this.Focus(FocusState.Programmatic);
        
        // Sync the volume and playback speed sliders with current ViewModel values
        if (VolumeLevelSlider != null)
            VolumeLevelSlider.Value = PlayerViewModel.VolumeLevel;
        if (PlaybackSpeedSlider != null)
            PlaybackSpeedSlider.Value = PlayerViewModel.PlaybackSpeed;
    }

    private void NewMiniPlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Clean up handlers attached at Loaded time.
        this.KeyDown -= NewMiniPlayerPage_KeyDown;
    }

    private void NewMiniPlayerPage_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        var key = args.Key;
        if (key == (VirtualKey)219) // Open bracket '['
        {
            HandleSpeedDecrease();
            ClosePlaybackSpeedFlyout();
            args.Handled = true;
        }
        else if (key == (VirtualKey)221) // Close bracket ']'
        {
            HandleSpeedIncrease();
            ClosePlaybackSpeedFlyout();
            args.Handled = true;
        }
        else if (key == (VirtualKey)0xDC) // Backslash '\\'
        {
            ResetPlaybackSpeed();
            ClosePlaybackSpeedFlyout();
            args.Handled = true;
        }
        else if (key == VirtualKey.Up) // Increase speed with Up arrow
        {
            HandleSpeedIncrease();
            ClosePlaybackSpeedFlyout();
            args.Handled = true;
        }
        else if (key == VirtualKey.Down) // Decrease speed with Down arrow
        {
            HandleSpeedDecrease();
            ClosePlaybackSpeedFlyout();
            args.Handled = true;
        }
        else if (key == VirtualKey.Space)
        {
            // Suppress play/pause when typing in the Bookmarks flyout (e.g., the note TextBox)
            if (IsInBookmarksFlyoutTextEditing())
            {
                // Let the TextBox receive the space character
                args.Handled = false;
                return;
            }

            // Toggle play/pause
            var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            if (wasPlaying)
                PlayerViewModel.MediaPlayer.Pause();
            else
                PlayerViewModel.MediaPlayer.Play();

            args.Handled = true;
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
                if (PlaybackSpeedSliderFlyout.IsOpen)
                    PlaybackSpeedSliderFlyout.Hide();
            }
            catch (TaskCanceledException)
            {
                // Ignore cancellation
            }
        });
    }

    private void ResetPlaybackSpeed()
    {
        PlaybackSpeedSliderFlyout.ShowAt(PlaybackSpeedButton);
        PlaybackSpeedSlider.Value = AppConstants.PlaybackSpeedDefault;
    }

    private void HandleSpeedIncrease()
    {
        PlaybackSpeedSliderFlyout.ShowAt(PlaybackSpeedButton);
        var newValue = PlaybackSpeedSlider.Value + AppConstants.PlaybackSpeedIncrement;
        if (newValue >= AppConstants.PlaybackSpeedMaximum) newValue = AppConstants.PlaybackSpeedMaximum;
        PlaybackSpeedSlider.Value = newValue;
    }

    private void HandleSpeedDecrease()
    {
        PlaybackSpeedSliderFlyout.ShowAt(PlaybackSpeedButton);
        var newValue = PlaybackSpeedSlider.Value - AppConstants.PlaybackSpeedIncrement;
        if (newValue <= AppConstants.PlaybackSpeedMinimum) newValue = AppConstants.PlaybackSpeedMinimum;
        PlaybackSpeedSlider.Value = newValue;
    }

    private void Slider_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Only consider it a drag start if the pointer is pressed over the slider
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse &&
            e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            _isUserDragging = true;

            // Check if currently playing and pause if so
            _wasPlayingBeforeDrag = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            if (_wasPlayingBeforeDrag)
            {
                PlayerViewModel.MediaPlayer.Pause();
            }
        }
    }

    private void Slider_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // Confirm we're in a drag operation if we're moving with button pressed
        if (!_isUserDragging &&
            e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            _isUserDragging = true;

            // Check if currently playing and pause if so
            _wasPlayingBeforeDrag = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            if (_wasPlayingBeforeDrag)
            {
                PlayerViewModel.MediaPlayer.Pause();
            }
        }
    }

    private async void Slider_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isUserDragging) return;

        _isUserDragging = false;

        if (PlayerViewModel.NowPlaying?.CurrentChapter == null) return;

        // Update the playback position
        PlayerViewModel.CurrentPosition =
            TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime + PositionSlider.Value);

        await PlayerViewModel.NowPlaying.SaveAsync();

        // Resume playback if it was playing before drag
        if (_wasPlayingBeforeDrag)
        {
            PlayerViewModel.MediaPlayer.Play();
        }
    }

    private async void Slider_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isUserDragging) return;

        _isUserDragging = false;

        if (PlayerViewModel.NowPlaying?.CurrentChapter != null)
        {
            PlayerViewModel.CurrentPosition =
                TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime + PositionSlider.Value);

            await PlayerViewModel.NowPlaying.SaveAsync();
        }

        // Resume playback if it was playing before drag
        if (_wasPlayingBeforeDrag)
        {
            PlayerViewModel.MediaPlayer.Play();
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

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowHelper.GetMiniPlayerWindow();
        if (window == null) return;

        PinButton.Visibility = Visibility.Collapsed;
        UnpinButton.Visibility = Visibility.Visible;

        window.SetWindowDraggable(false);
        window.SetWindowAlwaysOnTop(true);

        // Register global hotkeys (Ctrl+Up, Ctrl+Down, Ctrl+Left, Ctrl+Right, Ctrl+Space)
        try
        {
            // Avoid double-installing
            _globalKeyboardHook?.Dispose();
            _globalKeyboardHook = new GlobalKeyboardHook
            {
                OnCtrlUp = () =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try { HandleSpeedIncrease(); ClosePlaybackSpeedFlyout();  }
                        catch (Exception ex) { App.ViewModel.LoggingService?.LogError(ex, true); }
                    });
                },
                OnCtrlDown = () =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try { HandleSpeedDecrease(); ClosePlaybackSpeedFlyout();  }
                        catch (Exception ex) { App.ViewModel.LoggingService?.LogError(ex, true); }
                    });
                },
                OnCtrlLeft = () =>
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            // Prefer using the control's public SkipForwardAsync if available.
                            if (PlaySkipButtonsStack.SkipBackAsync != null)
                            {
                                await PlaySkipButtonsStack.SkipBackAsync();
                            }
                            else
                            {
                                //await PerformSkipForwardAsync();
                            }
                        }
                        catch (Exception ex) { App.ViewModel.LoggingService?.LogError(ex, true); }
                    });
                },
                OnCtrlRight = () =>
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            // Prefer using the control's public SkipForwardAsync if available.
                            if (PlaySkipButtonsStack.SkipForwardAsync != null)
                            {
                                await PlaySkipButtonsStack.SkipForwardAsync();
                            }
                            else
                            {
                                //await PerformSkipForwardAsync();
                            }
                        }
                        catch (Exception ex) { App.ViewModel.LoggingService?.LogError(ex, true); }
                    });
                },
                OnCtrlSpace = () =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
                            if (wasPlaying)
                                PlayerViewModel.MediaPlayer.Pause();
                            else
                                PlayerViewModel.MediaPlayer.Play();
                        }
                        catch (Exception ex) { App.ViewModel.LoggingService?.LogError(ex, true); }
                    });
                }
            };
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }

    private void UnpinButton_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowHelper.GetMiniPlayerWindow();
        if (window == null) return;

        UnpinButton.Visibility = Visibility.Collapsed;
        PinButton.Visibility = Visibility.Visible;

        window.SetWindowDraggable(true);
        window.SetWindowAlwaysOnTop(false);

        // Unregister global hotkeys
        try
        {
            _globalKeyboardHook?.Dispose();
            _globalKeyboardHook = null;
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }

    // Helper: skip back ~10s (mirrors PlaySkipButtonsStack behavior)
    private async Task PerformSkipBackAsync()
    {
        try
        {
            var skipAmount = TimeSpan.FromSeconds(10);

            if (PlayerViewModel.NowPlaying == null || PlayerViewModel.NowPlaying.CurrentChapter == null)
            {
                PlayerViewModel.CurrentPosition = PlayerViewModel.CurrentPosition - skipAmount > TimeSpan.Zero
                    ? PlayerViewModel.CurrentPosition - skipAmount
                    : TimeSpan.Zero;

                if (PlayerViewModel.NowPlaying != null)
                    await PlayerViewModel.NowPlaying.SaveAsync();

                return;
            }

            var currentPos = PlayerViewModel.CurrentPosition;
            var candidate = currentPos - skipAmount;
            var chapterStart = TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime);

            // If candidate would go before chapter start, clamp to chapter start
            if (candidate < chapterStart)
                PlayerViewModel.CurrentPosition = chapterStart;
            else
                PlayerViewModel.CurrentPosition = candidate;

            await PlayerViewModel.NowPlaying.SaveAsync();
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }

    // Helper: skip forward ~30s (mirrors PlaySkipButtonsStack behavior)
    private async Task PerformSkipForwardAsync()
    {
        try
        {
            var skipAmount = TimeSpan.FromSeconds(30);

            var naturalDuration = PlayerViewModel.MediaPlayer.PlaybackSession.NaturalDuration;
            var maxDuration = naturalDuration; // naturalDuration is already a TimeSpan

            var newPos = PlayerViewModel.CurrentPosition + skipAmount <= maxDuration
                ? PlayerViewModel.CurrentPosition + skipAmount
                : maxDuration;

            PlayerViewModel.CurrentPosition = newPos;

            if (PlayerViewModel.NowPlaying != null)
                await PlayerViewModel.NowPlaying.SaveAsync();
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }

    private void BackToLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        WindowHelper.RestoreMainWindow();
        WindowHelper.HideMiniPlayer();
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
            // Resolve the note TextBox from this page's Flyout content
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

            // Close the Bookmarks flyout after adding
            flyout?.Hide();
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }
    private async void DeleteBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Audibly.Models.Bookmark bookmark)
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

    private void NowPlayingBar_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        Slider_OnPointerCaptureLost(sender, e);
    }

    private void EndOfChapterTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.NowPlaying?.CurrentChapter == null) return;
        var endTime = PlayerViewModel.NowPlaying.CurrentChapter.EndTime;
        var currentPosition = PlayerViewModel.CurrentPosition.TotalMilliseconds;
        var timerDuration = endTime - currentPosition;
        timerDuration = timerDuration.ToSeconds().ToInt();
        if (timerDuration > 0) PlayerViewModel.SetTimer(timerDuration);
    }

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        // remember play status so we can resume if it was playing before navigation
        var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        if (sender is Button button && button.Tag is Audibly.Models.Bookmark bookmark)
            _bookmarkService.NavigateToBookmark(bookmark);

        // If playback was ongoing before navigation, resume it.
        // Calling Play() here is safe: MediaPlayer.Play() will start playback once media is opened/ready.
        if (wasPlaying)
            PlayerViewModel.MediaPlayer.Play();

        // Close the Bookmarks flyout after navigating
        (BookmarksButton?.Flyout as Flyout)?.Hide();
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
// Author: rstewa · https://github.com/rstewa
// Updated: 01/26/2025

using System;
using Audibly.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
using Windows.Media.Playback;
using CommunityToolkit.WinUI;

namespace Audibly.App.UserControls;

public sealed partial class NowPlayingBar : UserControl
{
    private bool _isUserDragging = false;
    private bool _wasPlayingBeforeDrag = false;

    public NowPlayingBar()
    {
        InitializeComponent();
        Loaded += NowPlayingBar_Loaded;
    }

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    private void NowPlayingBar_Loaded(object sender, RoutedEventArgs e)
    {
        // Attach pointer events to the slider itself with handledEventsToo=true
        // This is more reliable than trying to find the thumb
        PositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Slider_OnPointerPressed), true);
        PositionSlider.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Slider_OnPointerMoved), true);
        PositionSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Slider_OnPointerReleased), true);
        PositionSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Slider_OnPointerCaptureLost), true);
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

    /// <summary>
    /// Gets whether the user is currently dragging the slider
    /// </summary>
    public bool IsUserDragging => _isUserDragging;
}
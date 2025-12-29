// Author: rstewa · https://github.com/rstewa
// Updated: 02/14/2025

using System;
using Audibly.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Audibly.App.UserControls;

public sealed partial class PlaySkipButtonsStack : UserControl
{
    private static readonly TimeSpan _skipBackButtonAmount = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _skipForwardButtonAmount = TimeSpan.FromSeconds(30);

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(PlaySkipButtonsStack), new PropertyMetadata(0.0));

    public static readonly DependencyProperty PlayButtonSizeProperty = DependencyProperty.Register(
        nameof(PlayButtonSize), typeof(double), typeof(PlaySkipButtonsStack), new PropertyMetadata(32.0));

    public PlaySkipButtonsStack()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double PlayButtonSize
    {
        get => (double)GetValue(PlayButtonSizeProperty);
        set => SetValue(PlayButtonSizeProperty, value);
    }

    private void PlayPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.PlayPauseIcon == Symbol.Play)
            PlayerViewModel.MediaPlayer.Play();
        else
            PlayerViewModel.MediaPlayer.Pause();
    }

    private async void PreviousChapterButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.NowPlaying is null || PlayerViewModel.NowPlaying.CurrentChapter is null)
            return;

        var currentPos = PlayerViewModel.CurrentPosition;
        var chapterStart = TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime);
        
        // If we're not at the start of the current chapter (with a small tolerance), go to chapter start first
        var tolerance = TimeSpan.FromSeconds(2); // 2-second tolerance to account for small variations
        if (currentPos > chapterStart + tolerance)
        {
            PlayerViewModel.CurrentPosition = chapterStart;
            await PlayerViewModel.NowPlaying.SaveAsync();
            return;
        }

        // If we're at/near the start of the current chapter, proceed to previous chapter
        if (PlayerViewModel.NowPlaying?.CurrentChapterIndex - 1 > 0 &&
            PlayerViewModel.NowPlaying?.Chapters[(int)(PlayerViewModel.NowPlaying?.CurrentChapterIndex - 1)]
                .ParentSourceFileIndex != PlayerViewModel.NowPlaying?.CurrentSourceFileIndex)
        {
            var newChapterIdx = (int)PlayerViewModel.NowPlaying.CurrentChapterIndex - 1;
            PlayerViewModel.OpenSourceFile(PlayerViewModel.NowPlaying.CurrentSourceFileIndex - 1, newChapterIdx);
            PlayerViewModel.CurrentPosition =
                TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.Chapters[newChapterIdx].StartTime);
            await PlayerViewModel.NowPlaying.SaveAsync();

            return;
        }

        var newChapterIndex = PlayerViewModel.NowPlaying.CurrentChapterIndex - 1 >= 0
            ? PlayerViewModel.NowPlaying.CurrentChapterIndex - 1
            : PlayerViewModel.NowPlaying.CurrentChapterIndex;

        if (newChapterIndex == null) return;

        // PlayerViewModel.NowPlaying.CurrentChapter = PlayerViewModel.NowPlaying.Chapters[(int)newChapterIndex];
        PlayerViewModel.NowPlaying.CurrentChapterIndex = newChapterIndex;
        PlayerViewModel.NowPlaying.CurrentChapterTitle =
            PlayerViewModel.NowPlaying.Chapters[(int)newChapterIndex].Title;
        PlayerViewModel.ChapterComboSelectedIndex = (int)newChapterIndex;
        PlayerViewModel.ChapterDurationMs =
            (int)(PlayerViewModel.NowPlaying.CurrentChapter.EndTime -
                  PlayerViewModel.NowPlaying.CurrentChapter.StartTime);
        PlayerViewModel.CurrentPosition =
            TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime);

        await PlayerViewModel.NowPlaying.SaveAsync();
    }

    private async void NextChapterButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.NowPlaying is not null &&
            PlayerViewModel.NowPlaying?.CurrentChapterIndex + 1 < PlayerViewModel.NowPlaying?.Chapters.Count &&
            PlayerViewModel.NowPlaying?.Chapters[(int)(PlayerViewModel.NowPlaying?.CurrentChapterIndex + 1)]
                .ParentSourceFileIndex != PlayerViewModel.NowPlaying?.CurrentSourceFileIndex)
        {
            var newChapterIdx = (int)PlayerViewModel.NowPlaying.CurrentChapterIndex + 1;
            PlayerViewModel.OpenSourceFile(PlayerViewModel.NowPlaying.CurrentSourceFileIndex + 1, newChapterIdx);
            PlayerViewModel.CurrentPosition =
                TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.Chapters[newChapterIdx].StartTime);
            await PlayerViewModel.NowPlaying.SaveAsync();

            return;
        }

        var newChapterIndex =
            PlayerViewModel.NowPlaying.CurrentChapterIndex + 1 < PlayerViewModel.NowPlaying.Chapters.Count
                ? PlayerViewModel.NowPlaying.CurrentChapterIndex + 1
                : PlayerViewModel.NowPlaying.CurrentChapterIndex;

        if (newChapterIndex == null) return;

        // PlayerViewModel.NowPlaying.CurrentChapter = PlayerViewModel.NowPlaying.Chapters[(int)newChapterIndex];
        PlayerViewModel.NowPlaying.CurrentChapterIndex = newChapterIndex;
        PlayerViewModel.NowPlaying.CurrentChapterTitle =
            PlayerViewModel.NowPlaying.Chapters[(int)newChapterIndex].Title;
        PlayerViewModel.ChapterComboSelectedIndex = (int)newChapterIndex;
        PlayerViewModel.ChapterDurationMs =
            (int)(PlayerViewModel.NowPlaying.CurrentChapter.EndTime -
                  PlayerViewModel.NowPlaying.CurrentChapter.StartTime);
        PlayerViewModel.CurrentPosition =
            TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime);

        await PlayerViewModel.NowPlaying.SaveAsync();
    }

    private async void SkipBackButton_OnClick(object sender, RoutedEventArgs e)
    {
        // If we don't have NowPlaying or a CurrentChapter, fall back to previous behavior.
        if (PlayerViewModel.NowPlaying == null || PlayerViewModel.NowPlaying.CurrentChapter == null)
        {
            PlayerViewModel.CurrentPosition = PlayerViewModel.CurrentPosition - _skipBackButtonAmount > TimeSpan.Zero
                ? PlayerViewModel.CurrentPosition - _skipBackButtonAmount
                : TimeSpan.Zero;

            if (PlayerViewModel.NowPlaying != null)
                await PlayerViewModel.NowPlaying.SaveAsync();

            return;
        }

        var currentPos = PlayerViewModel.CurrentPosition;
        var candidate = currentPos - _skipBackButtonAmount;

        // Start of the current chapter (ms -> TimeSpan)
        var chapterStart = TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime);
        
        // Define tolerance for "at the start" - if we're within 3 seconds of chapter start or file start
        var tolerance = TimeSpan.FromSeconds(3);

        // Check if we're at the start of the chapter or file (considering tolerance)
        bool isAtChapterStart = currentPos <= chapterStart + tolerance;
        bool isAtFileStart = currentPos <= tolerance; // Very beginning of the file

        // If we're at the start of chapter/file, pause and go to previous chapter/file
        if (isAtChapterStart || isAtFileStart)
        {
            // Pause playback first
            bool wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;
            if (wasPlaying)
            {
                PlayerViewModel.MediaPlayer.Pause();
            }

            // Try to go to previous chapter using the existing logic from PreviousChapterButton
            if (PlayerViewModel.NowPlaying?.CurrentChapterIndex - 1 >= 0)
            {
                // Check if previous chapter is in a different source file
                if (PlayerViewModel.NowPlaying?.CurrentChapterIndex - 1 >= 0 &&
                    PlayerViewModel.NowPlaying?.Chapters[(int)(PlayerViewModel.NowPlaying?.CurrentChapterIndex - 1)]
                        .ParentSourceFileIndex != PlayerViewModel.NowPlaying?.CurrentSourceFileIndex)
                {
                    var newChapterIdx = (int)PlayerViewModel.NowPlaying.CurrentChapterIndex - 1;
                    var previousChapter = PlayerViewModel.NowPlaying.Chapters[newChapterIdx];
                    
                    // Calculate the target position (10 seconds before end of previous chapter)
                    var chapterEnd = TimeSpan.FromMilliseconds(previousChapter.EndTime);
                    var positionBeforeEnd = chapterEnd - TimeSpan.FromSeconds(10);
                    var chapterStartTime = TimeSpan.FromMilliseconds(previousChapter.StartTime);
                    var targetPosition = positionBeforeEnd > chapterStartTime ? positionBeforeEnd : chapterStartTime;
                    
                    // Update the target position in the audiobook model before opening the file
                    PlayerViewModel.NowPlaying.CurrentTimeMs = (int)targetPosition.TotalMilliseconds;
                    
                    // Set pending auto play if we were playing before
                    if (wasPlaying)
                    {
                        PlayerViewModel.SetPendingAutoPlay(true);
                    }
                    
                    PlayerViewModel.OpenSourceFile(PlayerViewModel.NowPlaying.CurrentSourceFileIndex - 1, newChapterIdx);
                    
                    await PlayerViewModel.NowPlaying.SaveAsync();
                    return;
                }

                // Previous chapter is in the same source file
                var newChapterIndex = PlayerViewModel.NowPlaying.CurrentChapterIndex - 1;
                if (newChapterIndex >= 0)
                {
                    var previousChapter = PlayerViewModel.NowPlaying.Chapters[(int)newChapterIndex];
                    
                    PlayerViewModel.NowPlaying.CurrentChapterIndex = newChapterIndex;
                    PlayerViewModel.NowPlaying.CurrentChapterTitle = previousChapter.Title;
                    PlayerViewModel.ChapterComboSelectedIndex = (int)newChapterIndex;
                    PlayerViewModel.ChapterDurationMs =
                        (int)(previousChapter.EndTime - previousChapter.StartTime);
                    
                    // Position 10 seconds before the end of the previous chapter
                    var chapterEnd = TimeSpan.FromMilliseconds(previousChapter.EndTime);
                    var positionBeforeEnd = chapterEnd - TimeSpan.FromSeconds(10);
                    var chapterStartTime = TimeSpan.FromMilliseconds(previousChapter.StartTime);
                    
                    // Make sure we don't go before the start of the chapter
                    PlayerViewModel.CurrentPosition = positionBeforeEnd > chapterStartTime ? positionBeforeEnd : chapterStartTime;

                    await PlayerViewModel.NowPlaying.SaveAsync();
                    
                    // Resume playback if it was playing before
                    if (wasPlaying)
                    {
                        PlayerViewModel.MediaPlayer.Play();
                    }
                    return;
                }
            }
            
            // If we can't go to previous chapter, just stay at current position and remain paused
            return;
        }

        // Normal skip back behavior when not at the start
        // If candidate would go before the start of the current source file, clamp to source file start (0).
        if (candidate < TimeSpan.Zero)
        {
            PlayerViewModel.CurrentPosition = TimeSpan.Zero;
        }
        // If candidate would cross before the current chapter, clamp to chapter start.
        else if (candidate < chapterStart)
        {
            PlayerViewModel.CurrentPosition = chapterStart;
        }
        else
        {
            PlayerViewModel.CurrentPosition = candidate;
        }

        await PlayerViewModel.NowPlaying.SaveAsync();
    }

    private async void SkipForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        // todo: might need to switch this to using the duration from the audiobook record
        PlayerViewModel.CurrentPosition = PlayerViewModel.CurrentPosition + _skipForwardButtonAmount <=
                                          PlayerViewModel.MediaPlayer.PlaybackSession.NaturalDuration
            ? PlayerViewModel.CurrentPosition + _skipForwardButtonAmount
            : PlayerViewModel.MediaPlayer.PlaybackSession.NaturalDuration;

        await PlayerViewModel.NowPlaying.SaveAsync();
    }
}
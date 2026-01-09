// Author: rstewa · https://github.com/rstewa
// Updated: 02/14/2025

using Audibly.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Playback;

namespace Audibly.App.UserControls;

public sealed partial class PlaySkipButtonsStack : UserControl
{
    private static readonly TimeSpan _skipBackButtonAmount = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _skipForwardButtonAmount = TimeSpan.FromSeconds(30);

    // Track last previous chapter button click time
    private DateTime _lastPreviousChapterClick = DateTime.MinValue;
    private static readonly TimeSpan _doubleClickThreshold = TimeSpan.FromSeconds(3);

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

        var now = DateTime.UtcNow;
        var timeSinceLastClick = now - _lastPreviousChapterClick;
        _lastPreviousChapterClick = now;

        var currentPos = PlayerViewModel.CurrentPosition;
        var chapterStart = TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.CurrentChapter.StartTime);
        var tolerance = TimeSpan.FromSeconds(2);

        // Check if this is a "double click" (within 3 seconds of last click)
        var isDoubleClick = timeSinceLastClick <= _doubleClickThreshold;

        // If double-click OR we're within 2 seconds of chapter start, go to previous chapter
        if (isDoubleClick || currentPos <= chapterStart + tolerance)
        {
            // Go to previous chapter
            var currentChapterIndex = PlayerViewModel.NowPlaying.CurrentChapterIndex ?? 0;
            
            // Can't go back if we're at the first chapter
            if (currentChapterIndex <= 0)
                return;

            var newChapterIndex = currentChapterIndex - 1;
            var previousChapter = PlayerViewModel.NowPlaying.Chapters[newChapterIndex];
            
            // Check if previous chapter is in a different source file
            if (previousChapter.ParentSourceFileIndex != PlayerViewModel.NowPlaying.CurrentSourceFileIndex)
            {
                // remember play status so we can resume if it was playing before navigation
                var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

                // Calculate absolute position at the start of the previous chapter
                long absolutePositionMs = 0;
                for (var i = 0; i < previousChapter.ParentSourceFileIndex; i++)
                {
                    absolutePositionMs += (long)(PlayerViewModel.NowPlaying.SourcePaths[i].Duration * 1000);
                }
                absolutePositionMs += (long)previousChapter.StartTime;

                // OpenSourceFile will handle setting CurrentTimeMs and position via AudioPlayer_MediaOpened
                PlayerViewModel.OpenSourceFile(previousChapter.ParentSourceFileIndex, newChapterIndex, (int)absolutePositionMs);
                
                // Update progress since chapter changed
                PlayerViewModel.UpdateAudiobookProgress();
                PlayerViewModel.NowPlaying.RefreshProgress();
                
                await PlayerViewModel.NowPlaying.SaveAsync();

                // If playback was ongoing before navigation, resume it.
                if (wasPlaying)
                    PlayerViewModel.SetPendingAutoPlay(true);

                return;
            }

            // Previous chapter is in same file - just update position
            PlayerViewModel.NowPlaying.CurrentChapterIndex = newChapterIndex;
            PlayerViewModel.NowPlaying.CurrentChapterTitle = previousChapter.Title;
            PlayerViewModel.ChapterComboSelectedIndex = newChapterIndex;
            PlayerViewModel.ChapterDurationMs = (int)(previousChapter.EndTime - previousChapter.StartTime);
            PlayerViewModel.CurrentPosition = TimeSpan.FromMilliseconds(previousChapter.StartTime);

            // Update progress since chapter changed
            PlayerViewModel.UpdateAudiobookProgress();
            PlayerViewModel.NowPlaying.RefreshProgress();

            await PlayerViewModel.NowPlaying.SaveAsync();
        }
        else
        {
            // Single click and we're more than 2 seconds into chapter - go to chapter start
            PlayerViewModel.CurrentPosition = chapterStart;
            
            // Update progress since we moved position
            PlayerViewModel.UpdateAudiobookProgress();
            PlayerViewModel.NowPlaying.RefreshProgress();
            
            await PlayerViewModel.NowPlaying.SaveAsync();
        }
    }

    private async void NextChapterButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerViewModel.NowPlaying is not null &&
            PlayerViewModel.NowPlaying?.CurrentChapterIndex + 1 < PlayerViewModel.NowPlaying?.Chapters.Count &&
            PlayerViewModel.NowPlaying?.Chapters[(int)(PlayerViewModel.NowPlaying?.CurrentChapterIndex + 1)]
                .ParentSourceFileIndex != PlayerViewModel.NowPlaying?.CurrentSourceFileIndex)
        {
            // remember play status so we can resume if it was playing before navigation
            var wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

            var newChapterIdx = (int)PlayerViewModel.NowPlaying.CurrentChapterIndex + 1;
            PlayerViewModel.OpenSourceFile(PlayerViewModel.NowPlaying.CurrentSourceFileIndex + 1, newChapterIdx);
            PlayerViewModel.CurrentPosition =
                TimeSpan.FromMilliseconds(PlayerViewModel.NowPlaying.Chapters[newChapterIdx].StartTime);
            
            // Update progress since chapter changed
            PlayerViewModel.UpdateAudiobookProgress();
            PlayerViewModel.NowPlaying.RefreshProgress();
            
            await PlayerViewModel.NowPlaying.SaveAsync();

            // If playback was ongoing before navigation, resume it.
            // Calling Play() here is safe: MediaPlayer.Play() will start playback once media is opened/ready.
            if (wasPlaying)
                PlayerViewModel.MediaPlayer.Play();

            return;
        }

        var newChapterIndex =
            PlayerViewModel.NowPlaying.CurrentChapterIndex + 1 < PlayerViewModel.NowPlaying.Chapters.Count
                ? PlayerViewModel.NowPlaying.CurrentChapterIndex + 1
                : PlayerViewModel.NowPlaying.CurrentChapterIndex;

        if (newChapterIndex == null) return;

        // If there is no next chapter, do nothing.
        if (PlayerViewModel.NowPlaying.CurrentChapterIndex == newChapterIndex) return;

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

        // Update progress since chapter changed
        PlayerViewModel.UpdateAudiobookProgress();
        PlayerViewModel.NowPlaying.RefreshProgress();

        await PlayerViewModel.NowPlaying.SaveAsync();
    }

    public static async Task SkipBackAsync()
    {
        var PlayerViewModel = App.PlayerViewModel;

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
                    var targetPositionInFile = positionBeforeEnd > chapterStartTime ? positionBeforeEnd : chapterStartTime;
                    
                    // Calculate absolute position across all source files
                    long absolutePositionMs = 0;
                    var targetSourceFileIndex = PlayerViewModel.NowPlaying.CurrentSourceFileIndex - 1;
                    for (var i = 0; i < targetSourceFileIndex; i++)
                    {
                        absolutePositionMs += (long)(PlayerViewModel.NowPlaying.SourcePaths[i].Duration * 1000);
                    }
                    absolutePositionMs += (long)targetPositionInFile.TotalMilliseconds;
                    
                    // Update the absolute position in the audiobook model before opening the file
                    PlayerViewModel.NowPlaying.CurrentTimeMs = (int)absolutePositionMs;
                    
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

    public static async Task SkipForwardAsync()
    {
        var PlayerViewModel = App.PlayerViewModel;

        // If we don't have NowPlaying, fall back to simple behavior
        if (PlayerViewModel.NowPlaying == null)
        {
            var naturalDuration = PlayerViewModel.MediaPlayer.PlaybackSession.NaturalDuration;
            var newPos = PlayerViewModel.CurrentPosition + _skipForwardButtonAmount <= naturalDuration
                ? PlayerViewModel.CurrentPosition + _skipForwardButtonAmount
                : naturalDuration;

            PlayerViewModel.CurrentPosition = newPos;
            return;
        }

        // Calculate the new absolute position
        var currentAbsolutePositionMs = PlayerViewModel.NowPlaying.CurrentTimeMs;
        var newAbsolutePositionMs = currentAbsolutePositionMs + (long)_skipForwardButtonAmount.TotalMilliseconds;
        
        // Calculate total audiobook duration in milliseconds
        var totalDurationMs = (long)(PlayerViewModel.NowPlaying.Duration * 1000);
        
        // Clamp to total duration
        if (newAbsolutePositionMs > totalDurationMs)
        {
            newAbsolutePositionMs = totalDurationMs;
        }

        // Find which source file this position falls into
        long cumulativeMs = 0;
        var currentSourceFileIndex = PlayerViewModel.NowPlaying.CurrentSourceFileIndex;
        var targetSourceFileIndex = currentSourceFileIndex;
        var positionWithinFileMs = 0L;
        
        for (var i = 0; i < PlayerViewModel.NowPlaying.SourcePaths.Count; i++)
        {
            var fileDurationMs = (long)(PlayerViewModel.NowPlaying.SourcePaths[i].Duration * 1000);
            
            if (newAbsolutePositionMs < cumulativeMs + fileDurationMs)
            {
                targetSourceFileIndex = i;
                positionWithinFileMs = newAbsolutePositionMs - cumulativeMs;
                break;
            }
            
            cumulativeMs += fileDurationMs;
        }

        // Check if we need to switch source files
        if (targetSourceFileIndex != currentSourceFileIndex)
        {
            // Crossing file boundary - start at the beginning of the next file
            // Calculate absolute position at the start of the target file
            long absolutePositionAtFileStart = 0;
            for (var i = 0; i < targetSourceFileIndex; i++)
            {
                absolutePositionAtFileStart += (long)(PlayerViewModel.NowPlaying.SourcePaths[i].Duration * 1000);
            }
            
            PlayerViewModel.NowPlaying.CurrentTimeMs = (int)absolutePositionAtFileStart;
            
            // Find the first chapter in the target source file
            var targetChapter = PlayerViewModel.NowPlaying.Chapters
                .FirstOrDefault(c => c.ParentSourceFileIndex == targetSourceFileIndex);
            
            var targetChapterIndex = targetChapter?.Index ?? 
                PlayerViewModel.NowPlaying.CurrentChapterIndex ?? 0;

            // Remember if we were playing
            bool wasPlaying = PlayerViewModel.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            
            if (wasPlaying)
            {
                PlayerViewModel.SetPendingAutoPlay(true);
            }

            PlayerViewModel.OpenSourceFile(targetSourceFileIndex, targetChapterIndex);
        }
        else
        {
            // Same file, just update position
            PlayerViewModel.NowPlaying.CurrentTimeMs = (int)newAbsolutePositionMs;
            PlayerViewModel.CurrentPosition = TimeSpan.FromMilliseconds(positionWithinFileMs);
        }

        await PlayerViewModel.NowPlaying.SaveAsync();
    }

    private async void SkipBackButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SkipBackAsync();
    }

    private async void SkipForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SkipForwardAsync();
    }
}
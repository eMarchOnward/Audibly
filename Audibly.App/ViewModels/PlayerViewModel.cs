// Author: rstewa · https://github.com/rstewa
// Updated: 08/02/2025

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Windows.Media.Core;
using Windows.Media.Playback;
using Audibly.App.Extensions;
using Audibly.App.Helpers;
using Audibly.App.Services;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace Audibly.App.ViewModels;

public class PlayerViewModel : BindableBase, IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    ///     Gets the app-wide MediaPlayer instance.
    /// </summary>
    public readonly MediaPlayer MediaPlayer = new();

    private int _chapterComboSelectedIndex;

    private long _chapterDurationMs;

    private string _chapterDurationText = "0:00:00";

    private long _chapterPositionMs;

    private string _chapterPositionText = "0:00:00";

    private bool _isPlayerFullScreen;
    private bool _isTimerActive;

    private string _maximizeMinimizeGlyph = Constants.MaximizeGlyph;

    private string _maximizeMinimizeTooltip = Constants.MaximizeTooltip;

    private AudiobookViewModel? _nowPlaying;

    private bool _pendingAutoPlay;

    private double _playbackSpeed = 1.0;

    private Symbol _playPauseIcon = Symbol.Play;

    private Timer? _sleepTimer;
    private DateTime _timerEndTime;

    private double _timerValue;

    private double _volumeLevel;

    private string _volumeLevelGlyph = Constants.VolumeGlyph3;

    private bool _mediaEventsInitialized;

    private int _debugMediaEndedCount;
    
    private bool _isSwitchingSourceFiles;

    public PlayerViewModel()
    {
        InitializeAudioPlayer();
    }

    /// <summary>
    ///     Gets or sets the currently playing audiobook.
    /// </summary>
    public AudiobookViewModel? NowPlaying
    {
        get => _nowPlaying;
        set => Set(ref _nowPlaying, value);
    }

    /// <summary>
    ///     Gets or sets the timer value for the player.
    /// </summary>
    public double TimerValue
    {
        get => _timerValue;
        set => Set(ref _timerValue, value);
    }

    /// <summary>
    ///     Gets or sets the glyph for the volume level.
    /// </summary>
    public string VolumeLevelGlyph
    {
        get => _volumeLevelGlyph;
        set => Set(ref _volumeLevelGlyph, value);
    }

    /// <summary>
    ///     Gets or sets the volume level.
    /// </summary>
    public double VolumeLevel
    {
        get => _volumeLevel;
        set => Set(ref _volumeLevel, value);
    }

    /// <summary>
    ///     Gets or sets the playback speed.
    /// </summary>
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set => Set(ref _playbackSpeed, value);
    }

    /// <summary>
    ///     Gets or sets the chapter duration text.
    /// </summary>
    public string ChapterDurationText
    {
        get => _chapterDurationText;
        set => Set(ref _chapterDurationText, value);
    }

    /// <summary>
    ///     Gets or sets the chapter position text.
    /// </summary>
    public string ChapterPositionText
    {
        get => _chapterPositionText;
        set => Set(ref _chapterPositionText, value);
    }

    /// <summary>
    ///     Gets or sets the chapter position in milliseconds.
    /// </summary>
    public int ChapterPositionMs
    {
        get => (int)_chapterPositionMs;
        set
        {
            Set(ref _chapterPositionMs, value);
            ChapterPositionText = _chapterPositionMs.ToStr_ms();
        }
    }

    /// <summary>
    ///     Gets or sets the chapter duration in milliseconds.
    /// </summary>
    public int ChapterDurationMs
    {
        get => (int)_chapterDurationMs;
        set
        {
            Set(ref _chapterDurationMs, value);
            ChapterDurationText = _chapterDurationMs.ToStr_ms();
        }
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the player is in full screen mode.
    /// </summary>
    public bool IsPlayerFullScreen
    {
        get => _isPlayerFullScreen;
        set => Set(ref _isPlayerFullScreen, value);
    }

    /// <summary>
    ///     Gets or sets the glyph for the maximize/minimize button.
    /// </summary>
    public string MaximizeMinimizeGlyph
    {
        get => _maximizeMinimizeGlyph;
        set => Set(ref _maximizeMinimizeGlyph, value);
    }

    /// <summary>
    ///     Gets or sets the tooltip for the maximize/minimize button.
    /// </summary>
    public string MaximizeMinimizeTooltip
    {
        get => _maximizeMinimizeTooltip;
        set => Set(ref _maximizeMinimizeTooltip, value);
    }

    /// <summary>
    ///     Gets or sets the play/pause icon.
    /// </summary>
    public Symbol PlayPauseIcon
    {
        get => _playPauseIcon;
        set => Set(ref _playPauseIcon, value);
    }

    /// <summary>
    ///     Gets or sets the selected index of the chapter combo box.
    /// </summary>
    public int ChapterComboSelectedIndex
    {
        get => _chapterComboSelectedIndex;
        set => Set(ref _chapterComboSelectedIndex, value);
    }

    /// <summary>
    ///     Gets or sets the current position of the media player.
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get => MediaPlayer.PlaybackSession.Position;
        set => MediaPlayer.PlaybackSession.Position = value > TimeSpan.Zero ? value : TimeSpan.Zero;
    }

    /// <summary>
    /// </summary>
    public bool IsTimerActive
    {
        get => _isTimerActive;
        private set => Set(ref _isTimerActive, value);
    }

    /// <summary>
    /// </summary>
    public string TimerRemainingText
    {
        get => _isTimerActive ? FormatTimeRemaining() : "Timer Off";
        private set => OnPropertyChanged();
    }

    #region methods

    private void InitializeAudioPlayer()
    {
        if (_mediaEventsInitialized)
        {
            Debug.WriteLine("InitializeAudioPlayer called again; skipping event subscriptions.");
            return;
        }

        MediaPlayer.AutoPlay = false;
        MediaPlayer.AudioCategory = MediaPlayerAudioCategory.Media;
        MediaPlayer.AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia;
        MediaPlayer.CommandManager.IsEnabled = true; // todo: what is this?
        MediaPlayer.MediaOpened += AudioPlayer_MediaOpened;
        MediaPlayer.MediaEnded += AudioPlayer_MediaEnded;
        MediaPlayer.MediaFailed += AudioPlayer_MediaFailed;
        MediaPlayer.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
        MediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;

        _mediaEventsInitialized = true;
        // set volume level from settings
        // UpdateVolume(UserSettings.Volume);
        // UpdatePlaybackSpeed(UserSettings.PlaybackSpeed);
    }

    public void SetTimer(double seconds)
    {
        // Cancel existing timer if active
        if (_sleepTimer != null)
        {
            _sleepTimer.Stop();
            _sleepTimer.Dispose();
            _sleepTimer = null;
        }

        // Disable timer if seconds is 0
        if (seconds <= 0)
        {
            TimerValue = 0;
            IsTimerActive = false;
            OnPropertyChanged(nameof(TimerRemainingText));
            return;
        }

        // Create and start new timer
        _timerEndTime = DateTime.Now.AddSeconds(seconds);
        TimerValue = seconds;
        IsTimerActive = true;

        _sleepTimer = new Timer(1000); // Update every second
        _sleepTimer.Elapsed += SleepTimer_Elapsed;
        _sleepTimer.Start();

        OnPropertyChanged(nameof(TimerRemainingText));
    }

    private void SleepTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        var timeRemaining = _timerEndTime - DateTime.Now;

        if (timeRemaining <= TimeSpan.Zero)
            // Timer expired - pause playback
            _dispatcherQueue.TryEnqueue(() =>
            {
                MediaPlayer.Pause();
                IsTimerActive = false;
                _sleepTimer?.Stop();
                _sleepTimer?.Dispose();
                _sleepTimer = null;
                OnPropertyChanged(nameof(TimerRemainingText));
            });
        else
            // Update remaining time display
            _dispatcherQueue.TryEnqueue(() => { OnPropertyChanged(nameof(TimerRemainingText)); });
    }

    private string FormatTimeRemaining()
    {
        var timeRemaining = _timerEndTime - DateTime.Now;
        if (timeRemaining <= TimeSpan.Zero)
            return "Timer Off";

        return timeRemaining.TotalHours >= 1
            ? $"{(int)timeRemaining.TotalHours}:{timeRemaining:mm\\:ss}"
            : $"{timeRemaining:mm\\:ss}";
    }

    public void Dispose()
    {
        _sleepTimer?.Stop();
        _sleepTimer?.Dispose();
    }

    public async void UpdateVolume(double volume)
    {
        VolumeLevel = volume;
        MediaPlayer.Volume = volume / 100;
        VolumeLevelGlyph = volume switch
        {
            > 66 => Constants.VolumeGlyph3,
            > 33 => Constants.VolumeGlyph2,
            > 0 => Constants.VolumeGlyph1,
            _ => Constants.VolumeGlyph0
        };

        // save volume level for audiobook
        if (NowPlaying == null) return;

        NowPlaying.Volume = volume;
        NowPlaying.IsModified = true;
        await NowPlaying.SaveAsync();
    }

    public async void UpdatePlaybackSpeed(double speed)
    {
        PlaybackSpeed = speed;
        MediaPlayer.PlaybackRate = speed;

        // save playback speed for audiobook
        if (NowPlaying == null) return;

        NowPlaying.PlaybackSpeed = speed;
        NowPlaying.IsModified = true;
        await NowPlaying.SaveAsync();
    }

    public void SetPendingAutoPlay(bool value)
    {
        _pendingAutoPlay = value;
    }

    public async Task OpenAudiobook(AudiobookViewModel audiobook)
    {
        if (NowPlaying != null && NowPlaying.Equals(audiobook))
            return;

        // Store the current audiobook to ensure we properly save its state
        var previousAudiobook = NowPlaying;

        // Pause playback first to stop position updates
        MediaPlayer.Pause();

        // If we have a previous audiobook, ensure it's properly saved before switching
        if (previousAudiobook != null)
        {
            previousAudiobook.IsNowPlaying = false;
            // Wait for any pending saves to complete
            await previousAudiobook.SaveAsync();
        }

        App.ViewModel.SelectedAudiobook = audiobook;

        // verify that the file exists
        // if there are multiple source files, check them all

        if (audiobook.SourcePaths.Any(sourceFile => !File.Exists(sourceFile.FilePath)))
        {
            // note: content dialog
            await DialogService.ShowErrorDialogAsync("Error",
                $"Unable to play audiobook: {audiobook.Title}. One or more of its source files were deleted or moved.");

            return;
        }

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            // Now it's safe to switch to the new audiobook
            NowPlaying = audiobook;

            if (NowPlaying.DateLastPlayed == null)
            {
                // use the global playback speed and volume level if they are set
                // and this is the first time the audiobook is being played
                UpdatePlaybackSpeed(UserSettings.PlaybackSpeed);
                UpdateVolume(UserSettings.Volume);
            }
            else
            {
                // use the audiobook's playback speed and volume level
                UpdatePlaybackSpeed(NowPlaying.PlaybackSpeed);
                UpdateVolume(NowPlaying.Volume);
            }

            NowPlaying.IsNowPlaying = true;
            NowPlaying.DateLastPlayed = DateTime.Now;

            ChapterComboSelectedIndex = NowPlaying.CurrentChapterIndex ?? 0;
            NowPlaying.CurrentChapterTitle = NowPlaying.Chapters[ChapterComboSelectedIndex].Title;

            await NowPlaying.SaveAsync();
        });

        // If the UI is sorted by last played, re-apply sort so the just-played book moves to the front
        try
        {
            if (App.ViewModel.CurrentSortMode == AudiobookSortMode.DateLastPlayed)
            {
                // Small delay so the player UI can initialize before the library view refreshes
                await Task.Delay(400);
                App.ViewModel.ApplySort();
            }
        }
        catch (Exception ex)
        {
            // log but do not crash the player
            App.ViewModel.LoggingService.LogError(ex, true);
        }

        MediaPlayer.Source = MediaSource.CreateFromUri(audiobook.CurrentSourceFile.FilePath.AsUri());
    }

    public void JumpToPosition(long positionMs)
    {
        if (NowPlaying == null) return;

        // 1) Find the correct source file by walking durations (seconds) and comparing against absolute ms position
        var sourceFiles = NowPlaying.SourcePaths;
        if (sourceFiles == null || sourceFiles.Count == 0) return;

        var cumulativeMs = 0L; // cumulative duration across source files in milliseconds
        var targetSourceIndex = 0;
        for (var i = 0; i < sourceFiles.Count; i++)
        {
            var fileDurationMs = sourceFiles[i].Duration * 1000; // Duration is in seconds; convert to ms
            if (positionMs < cumulativeMs + fileDurationMs)
            {
                targetSourceIndex = i;
                break;
            }
            cumulativeMs += fileDurationMs;
        }

        // 2) Compute the position relative to the found source file
        var positionWithinSourceMs = positionMs - cumulativeMs;
        if (positionWithinSourceMs < 0) positionWithinSourceMs = 0;

        // 3) Find the chapter within the target source file by relative position
        var chapter = NowPlaying.Chapters
            .FirstOrDefault(c => c.ParentSourceFileIndex == targetSourceIndex &&
                                 positionWithinSourceMs >= c.StartTime &&
                                 positionWithinSourceMs < c.EndTime);

        // 4) Update current time and navigate
        // Set absolute CurrentTimeMs to the bookmark position
        NowPlaying.CurrentTimeMs = (int)positionMs;

        if (NowPlaying.CurrentSourceFileIndex != targetSourceIndex)
        {
            // Switch source file and set chapter index accordingly
            var targetChapterIndex = chapter?.Index ?? NowPlaying.CurrentChapterIndex ?? 0;
            OpenSourceFile(targetSourceIndex, targetChapterIndex);
            // Let MediaOpened handler seek using NowPlaying.CurrentTimeMs (already absolute)
            CurrentPosition = TimeSpan.FromMilliseconds(positionWithinSourceMs);
        }
        else
        {
            // Same source file: set direct playback position
            CurrentPosition = TimeSpan.FromMilliseconds(positionWithinSourceMs);
        }
    }

    public async void OpenSourceFile(int index, int chapterIndex)
    {
        if (NowPlaying == null || NowPlaying.CurrentSourceFileIndex == index)
            return;

        _isSwitchingSourceFiles = true;

        // Do not reset CurrentTimeMs; it may be set by JumpToPosition
        NowPlaying.CurrentSourceFileIndex = index;
        NowPlaying.CurrentChapterIndex = chapterIndex;

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            NowPlaying.CurrentChapterTitle = NowPlaying.Chapters[chapterIndex].Title;
        });

        await NowPlaying.SaveAsync();

        MediaPlayer.Source = MediaSource.CreateFromUri(NowPlaying.CurrentSourceFile.FilePath.AsUri());
    }

    # endregion

    #region event handlers

    private void AudioPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        if (NowPlaying == null) return;
        _dispatcherQueue.EnqueueAsync(async () =>
        {
            if (NowPlaying.Chapters.Count == 0)
            {
                NowPlaying = null;

                // note: content dialog
                await DialogService.ShowErrorDialogAsync("Error",
                    "An error occurred while trying to open the selected audiobook. " +
                    "The chapters could not be loaded. Please try importing the audiobook again.");

                return;
            }

            ChapterComboSelectedIndex = NowPlaying.CurrentChapterIndex ?? 0;

            ChapterDurationMs = (int)(NowPlaying.CurrentChapter.EndTime - NowPlaying.CurrentChapter.StartTime);

            // Calculate the position relative to the current source file from the absolute CurrentTimeMs
            long cumulativeMs = 0;
            for (var i = 0; i < NowPlaying.CurrentSourceFileIndex; i++)
            {
                cumulativeMs += (long)(NowPlaying.SourcePaths[i].Duration * 1000);
            }
            
            var positionWithinSourceMs = NowPlaying.CurrentTimeMs - cumulativeMs;
            if (positionWithinSourceMs < 0) positionWithinSourceMs = 0;

            ChapterPositionMs =
                positionWithinSourceMs > NowPlaying.CurrentChapter.StartTime
                    ? (int)(positionWithinSourceMs - NowPlaying.CurrentChapter.StartTime)
                    : 0;

            // Set the MediaPlayer position to the calculated position within the current source file
            CurrentPosition = TimeSpan.FromMilliseconds(positionWithinSourceMs);

            _isSwitchingSourceFiles = false;

            if (_pendingAutoPlay)
            {
                _pendingAutoPlay = false;
                MediaPlayer.Play();
            }
        });
    }

    private void AudioPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        if (NowPlaying == null) return;

        var currentSourceIndex = NowPlaying.CurrentSourceFileIndex;
        if (currentSourceIndex >= NowPlaying.SourcePaths.Count - 1) return;

        var nextSourceIndex = currentSourceIndex + 1;

        var nextChapter = NowPlaying.Chapters
            .FirstOrDefault(c => c.ParentSourceFileIndex == nextSourceIndex);

        if (nextChapter == null) return;

        // Set flag BEFORE modifying CurrentTimeMs to prevent race condition with PlaybackSession_PositionChanged
        _isSwitchingSourceFiles = true;

        // Calculate the absolute position at the start of the next source file
        long absolutePositionMs = 0;
        for (var i = 0; i < nextSourceIndex; i++)
        {
            absolutePositionMs += (long)(NowPlaying.SourcePaths[i].Duration * 1000);
        }
        
        // Set the absolute position to the start of the next file
        NowPlaying.CurrentTimeMs = (int)absolutePositionMs;

        _pendingAutoPlay = true;
        OpenSourceFile(nextSourceIndex, nextChapter.Index);
    }


    private void AudioPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() => NowPlaying = null);

        // note: content dialog
        App.ViewModel.EnqueueNotification(new Notification
        {
            Message =
                "Unable to open the audiobook: media failed. Please verify that the file is not corrupted and try again.",
            Severity = InfoBarSeverity.Error
        });
    }

    private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        switch (sender.PlaybackState)
        {
            case MediaPlaybackState.Playing:
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (PlayPauseIcon == Symbol.Pause) return;
                    PlayPauseIcon = Symbol.Pause;
                });

                break;

            case MediaPlaybackState.Paused:
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (PlayPauseIcon == Symbol.Play) return;
                    PlayPauseIcon = Symbol.Play;
                });

                break;
        }
    }

    private async void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
    {
        if (NowPlaying == null) return;

        // Don't update position while switching source files to prevent race conditions
        if (_isSwitchingSourceFiles) return;

        // Additional safety check: ensure we're not processing position updates for a stale audiobook
        // This can happen during audiobook switching when events are queued
        var currentNowPlaying = NowPlaying; // Capture current reference to avoid race conditions

        if (!currentNowPlaying.CurrentChapter.InRange(CurrentPosition.TotalMilliseconds))
        {
            var newChapter = currentNowPlaying.Chapters.FirstOrDefault(c =>
                c.ParentSourceFileIndex == currentNowPlaying.CurrentSourceFileIndex &&
                c.InRange(CurrentPosition.TotalMilliseconds));

            if (newChapter != null)
                _ = _dispatcherQueue.EnqueueAsync(() =>
                {
                    // Double-check that NowPlaying hasn't changed since we started processing this event
                    if (NowPlaying == currentNowPlaying)
                    {
                        currentNowPlaying.CurrentChapterIndex = ChapterComboSelectedIndex = newChapter.Index;
                        currentNowPlaying.CurrentChapterTitle = newChapter.Title;
                        ChapterDurationMs = (int)(currentNowPlaying.CurrentChapter.EndTime - currentNowPlaying.CurrentChapter.StartTime);
                    }
                });
        }

        _ = _dispatcherQueue.EnqueueAsync(async () =>
        {
            // Double-check that NowPlaying hasn't changed since we started processing this event
            if (NowPlaying != currentNowPlaying) return;

            ChapterPositionMs = (int)(CurrentPosition.TotalMilliseconds > currentNowPlaying.CurrentChapter.StartTime
                ? CurrentPosition.TotalMilliseconds - currentNowPlaying.CurrentChapter.StartTime
                : 0);
            
            // Calculate absolute position: sum of all previous files + current position in this file
            long absolutePositionMs = 0;
            for (var i = 0; i < currentNowPlaying.CurrentSourceFileIndex; i++)
            {
                absolutePositionMs += (long)(currentNowPlaying.SourcePaths[i].Duration * 1000);
            }
            absolutePositionMs += (long)CurrentPosition.TotalMilliseconds;
            
            currentNowPlaying.CurrentTimeMs = (int)absolutePositionMs;

            // Calculate progress using the absolute CurrentTimeMs position
            currentNowPlaying.Progress = Math.Ceiling((double)currentNowPlaying.CurrentTimeMs / (currentNowPlaying.Duration * 1000) * 100);
            currentNowPlaying.IsCompleted = currentNowPlaying.Progress >= 99.9;

            await currentNowPlaying.SaveAsync();
        });
    }

    #endregion
}
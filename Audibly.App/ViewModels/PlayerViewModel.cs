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

    // Throttling fields for position updates
    private DateTime _lastPositionUiUpdate = DateTime.MinValue;
    private readonly TimeSpan _positionUpdateInterval = TimeSpan.FromMilliseconds(500);

    // Dirty flag fields for persistence
    private bool _positionDirty;
    private DateTime _lastPositionPersistTime = DateTime.MinValue;
    private readonly TimeSpan _positionPersistInterval = TimeSpan.FromSeconds(10);

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

        var previousAudiobook = NowPlaying;

        MediaPlayer.Pause();

        if (previousAudiobook != null)
        {
            previousAudiobook.IsNowPlaying = false;

            // Save previous audiobook in the background so we do not block UI when switching
            _ = Task.Run(async () =>
            {
                try
                {
                    await previousAudiobook.SaveAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error saving previous audiobook: {ex}");
                }
            });
        }

        App.ViewModel.SelectedAudiobook = audiobook;

        // Move file existence checks off the UI thread for large multi-file audiobooks
        var missingFile = await Task.Run(() =>
        {
            return audiobook.SourcePaths
                .FirstOrDefault(sourceFile => !File.Exists(sourceFile.FilePath));
        });

        if (missingFile != null)
        {
            await DialogService.ShowErrorDialogAsync("Error",
                $"Unable to play audiobook: {audiobook.Title}. One or more of its source files were deleted or moved.");

            return;
        }

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            NowPlaying = audiobook;

            if (NowPlaying.DateLastPlayed == null)
            {
                UpdatePlaybackSpeed(UserSettings.PlaybackSpeed);
                UpdateVolume(UserSettings.Volume);
            }
            else
            {
                UpdatePlaybackSpeed(NowPlaying.PlaybackSpeed);
                UpdateVolume(NowPlaying.Volume);
            }

            NowPlaying.IsNowPlaying = true;
            NowPlaying.DateLastPlayed = DateTime.Now;

            ChapterComboSelectedIndex = NowPlaying.CurrentChapterIndex ?? 0;
            NowPlaying.CurrentChapterTitle = NowPlaying.Chapters[ChapterComboSelectedIndex].Title;

            // Reset persistence tracking for the new audiobook
            _positionDirty = false;
            _lastPositionPersistTime = DateTime.UtcNow;
            
            // Update progress once when opening
            UpdateAudiobookProgress();
            NowPlaying.RefreshProgress();
            await NowPlaying.SaveAsync();
        });

        MediaPlayer.Source = MediaSource.CreateFromUri(audiobook.CurrentSourceFile.FilePath.AsUri());

        // If the UI is sorted by last played, re-apply sort in the background without blocking playback
        if (App.ViewModel.CurrentSortMode == AudiobookSortMode.DateLastPlayed)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Small delay to ensure DateLastPlayed has been saved
                    await Task.Delay(380);
                    App.ViewModel.ApplySort();
                }
                catch (Exception ex)
                {
                    // log but do not crash the player
                    App.ViewModel.LoggingService.LogError(ex, true);
                }
            });
        }
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

        if (NowPlaying.CurrentSourceFileIndex != targetSourceIndex)
        {
            var targetChapterIndex = chapter?.Index ?? NowPlaying.CurrentChapterIndex ?? 0;
            OpenSourceFile(targetSourceIndex, targetChapterIndex, (int)positionMs);
        }
        else
        {
            NowPlaying.CurrentTimeMs = (int)positionMs;
            CurrentPosition = TimeSpan.FromMilliseconds(positionWithinSourceMs);

            // Mark dirty and persist immediately on explicit seek
            MarkPositionDirty();
            _ = TryPersistPositionAsync(NowPlaying);
        }
    }

    public async void OpenSourceFile(int index, int chapterIndex, int? targetPositionMs = null)
    {
        if (NowPlaying == null || NowPlaying.CurrentSourceFileIndex == index)
            return;

        Debug.WriteLine($"[OpenSourceFile] Opening file {index}, chapter {chapterIndex}, targetPosition: {targetPositionMs}");

        _isSwitchingSourceFiles = true;

        if (targetPositionMs.HasValue)
        {
            NowPlaying.CurrentTimeMs = targetPositionMs.Value;
        }

        NowPlaying.CurrentSourceFileIndex = index;
        NowPlaying.CurrentChapterIndex = chapterIndex;

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            NowPlaying.CurrentChapterTitle = NowPlaying.Chapters[chapterIndex].Title;
        });

        // Mark dirty and persist immediately on explicit chapter/file jump
        MarkPositionDirty();
        await TryPersistPositionAsync(NowPlaying);

        Debug.WriteLine($"[OpenSourceFile] Loading media source: {NowPlaying.CurrentSourceFile.FilePath}");
        MediaPlayer.Source = MediaSource.CreateFromUri(NowPlaying.CurrentSourceFile.FilePath.AsUri());
    }
    #endregion
    #region event handlers

    private void AudioPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        if (NowPlaying == null) return;
        _dispatcherQueue.EnqueueAsync(async () =>
        {
            Debug.WriteLine($"[MediaOpened] File opened: Index={NowPlaying.CurrentSourceFileIndex}, Path={NowPlaying.CurrentSourceFile.FilePath}");
            
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

            Debug.WriteLine($"[MediaOpened] AbsoluteCurrentTimeMs={NowPlaying.CurrentTimeMs}ms, CumulativeMs={cumulativeMs}ms, PositionWithinSource={positionWithinSourceMs}ms");

            // Set the MediaPlayer position to the calculated position within the current source file
            CurrentPosition = TimeSpan.FromMilliseconds(positionWithinSourceMs);

            Debug.WriteLine($"[MediaOpened] Clearing _isSwitchingSourceFiles flag");
            _isSwitchingSourceFiles = false;

            if (_pendingAutoPlay)
            {
                Debug.WriteLine($"[MediaOpened] Auto-playing");
                _pendingAutoPlay = false;
                MediaPlayer.Play();
            }

            // Restore playback speed - MediaPlayer resets to 1.0 when a new source is loaded
            MediaPlayer.PlaybackRate = PlaybackSpeed;
            Debug.WriteLine($"[MediaOpened] Restored playback speed to {PlaybackSpeed}x");

            // Update progress when a new file opens (chapter changed across files)
            UpdateAudiobookProgress();
            NowPlaying.RefreshProgress();
        });
    }

    private void AudioPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        if (NowPlaying == null) return;

        // Prevent processing MediaEnded multiple times during file switching
        if (_isSwitchingSourceFiles)
        {
            Debug.WriteLine($"[MediaEnded] BLOCKED - Already switching files");
            return;
        }

        var currentSourceIndex = NowPlaying.CurrentSourceFileIndex;
        Debug.WriteLine($"[MediaEnded] CurrentSourceIndex: {currentSourceIndex}, TotalFiles: {NowPlaying.SourcePaths.Count}");
        
        if (currentSourceIndex >= NowPlaying.SourcePaths.Count - 1) return;

        var nextSourceIndex = currentSourceIndex + 1;

        var nextChapter = NowPlaying.Chapters
            .FirstOrDefault(c => c.ParentSourceFileIndex == nextSourceIndex);

        if (nextChapter == null)
        {
            Debug.WriteLine($"[MediaEnded] No chapter found for file index {nextSourceIndex}");
            return;
        }

        Debug.WriteLine($"[MediaEnded] Transitioning from file {currentSourceIndex} to {nextSourceIndex}, chapter: {nextChapter.Title}");

        // Set flag BEFORE modifying CurrentTimeMs to prevent race condition with PlaybackSession_PositionChanged
        _isSwitchingSourceFiles = true;

        // Calculate the absolute position at the start of the next source file
        long absolutePositionMs = 0;
        for (var i = 0; i < nextSourceIndex; i++)
        {
            absolutePositionMs += (long)(NowPlaying.SourcePaths[i].Duration * 1000);
        }
        
        Debug.WriteLine($"[MediaEnded] Setting CurrentTimeMs to {absolutePositionMs}ms (start of file {nextSourceIndex})");
        
        _pendingAutoPlay = true;
        // Pass the target position to OpenSourceFile so it can set it AFTER the flag is set
        OpenSourceFile(nextSourceIndex, nextChapter.Index, (int)absolutePositionMs);
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
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    if (PlayPauseIcon != Symbol.Play)
                    {
                        PlayPauseIcon = Symbol.Play;
                    }

                    // On pause, update progress and flush any pending position changes
                    if (NowPlaying != null)
                    {
                        UpdateAudiobookProgress();
                        await TryPersistPositionAsync(NowPlaying);
                    }
                });

                break;
        }
    }

    private async void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
    {
        if (NowPlaying == null || _isSwitchingSourceFiles) return;

        var currentNowPlaying = NowPlaying;
        var currentPositionMs = CurrentPosition.TotalMilliseconds; // Capture early on the correct thread

        var now = DateTime.UtcNow;
        if (now - _lastPositionUiUpdate < _positionUpdateInterval)
        {
            return;
        }

        _lastPositionUiUpdate = now;

        // Only update chapter if we detect a change
        if (!currentNowPlaying.CurrentChapter.InRange(currentPositionMs))
        {
            var newChapter = currentNowPlaying.Chapters.FirstOrDefault(c =>
                c.ParentSourceFileIndex == currentNowPlaying.CurrentSourceFileIndex &&
                c.InRange(currentPositionMs));

            if (newChapter != null)
                _ = _dispatcherQueue.EnqueueAsync(() =>
                {
                    if (NowPlaying == currentNowPlaying)
                    {
                        currentNowPlaying.CurrentChapterIndex = ChapterComboSelectedIndex = newChapter.Index;
                        currentNowPlaying.CurrentChapterTitle = newChapter.Title;
                        ChapterDurationMs = (int)(currentNowPlaying.CurrentChapter.EndTime - currentNowPlaying.CurrentChapter.StartTime);

                        // Update progress now that chapter changed and notify the UI for the tile
                        UpdateAudiobookProgress();
                        currentNowPlaying.RefreshProgress();
                    }
                });
        }

        // Lightweight UI update - chapter position only (must be on UI thread)
        _ = _dispatcherQueue.EnqueueAsync(() =>
        {
            if (NowPlaying != currentNowPlaying) return;
            
            ChapterPositionMs = (int)(currentPositionMs > currentNowPlaying.CurrentChapter.StartTime
                ? currentPositionMs - currentNowPlaying.CurrentChapter.StartTime
                : 0);
        });

        // Calculate and persist position in background without blocking
        _ = Task.Run(async () =>
        {
            if (NowPlaying != currentNowPlaying) return;

            long absolutePositionMs = 0;
            for (var i = 0; i < currentNowPlaying.CurrentSourceFileIndex; i++)
            {
                absolutePositionMs += (long)(currentNowPlaying.SourcePaths[i].Duration * 1000);
            }
            absolutePositionMs += (long)currentPositionMs;

            currentNowPlaying.CurrentTimeMs = (int)absolutePositionMs;

            MarkPositionDirty();
            await TryPersistPositionAsync(currentNowPlaying);
        });
    }



    // Dirty flag helpers
    private void MarkPositionDirty()
    {
        _positionDirty = true;
    }

    private async Task TryPersistPositionAsync(AudiobookViewModel audiobook)
    {
        if (!_positionDirty) return;

        var now = DateTime.UtcNow;
        if (now - _lastPositionPersistTime < _positionPersistInterval)
        {
            return;
        }

        _positionDirty = false;
        _lastPositionPersistTime = now;

        try
        {
            await audiobook.SaveAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving playback position: {ex}");
            _positionDirty = true; // keep dirty so we try again later
        }
    }

    public void UpdateAudiobookProgress()
    {
        if (NowPlaying == null) return;

        NowPlaying.Progress = Math.Ceiling((double)NowPlaying.CurrentTimeMs / (NowPlaying.Duration * 1000) * 100);
        NowPlaying.IsCompleted = NowPlaying.Progress >= 99.9;
    }
    #endregion
}
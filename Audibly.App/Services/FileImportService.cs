// Author: rstewa · https://github.com/rstewa
// Created: 04/15/2024
// Updated: 12/28/2025

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using ATL;
using Audibly.App.Extensions;
using Audibly.App.Services.Interfaces;
using Audibly.App.ViewModels;
using Audibly.Models;
using AutoMapper;
using Microsoft.UI.Xaml.Controls;
using Sharpener.Extensions;
using ChapterInfo = Audibly.Models.ChapterInfo;

namespace Audibly.App.Services;

public class FileImportService : IImportFiles
{
    private static IMapper _mapper;

    public FileImportService()
    {
        _mapper = new MapperConfiguration(cfg => { cfg.CreateMap<ATL.ChapterInfo, ChapterInfo>(); }).CreateMapper();
    }

    #region IImportFiles Members

    public event IImportFiles.ImportCompletedHandler? ImportCompleted;

    // TODO: need a better way of checking if a file is one we have already imported
    public async Task ImportDirectoryAsync(string path, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback)
    {
        var didFail = false;

        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".m4b", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) // <-- added .m4a
            .ToList();
        var numberOfFiles = files.Count;

        var filesList = files.AsList();

        foreach (var file in files)
        {
            // Check if cancellation was requested
            cancellationToken.ThrowIfCancellationRequested();

            var audiobook = await CreateAudiobook(file);

            if (audiobook == null) didFail = true;

            if (audiobook != null)
            {
                // insert the audiobook into the database
                var result = await App.Repository.Audiobooks.UpsertAsync(audiobook);
                if (result == null) didFail = true;
            }

            var title = audiobook?.Title ?? Path.GetFileNameWithoutExtension(file);

            // report progress
            await progressCallback(filesList.IndexOf(file), numberOfFiles, title, didFail);

            didFail = false;
        }

        ImportCompleted?.Invoke();
    }

    public async Task ImportFromJsonAsync(StorageFile file, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback)
    {
        // read the json string from the file
        var json = FileIO.ReadTextAsync(file).AsTask().Result;

        if (string.IsNullOrEmpty(json))
        {
            // log the error
            App.ViewModel.LoggingService.LogError(new Exception("Failed to read the json file"), true);
            ImportCompleted?.Invoke();
            return;
        }

        // deserialize the json string to a list of audiobooks
        var importedAudiobooks = JsonSerializer.Deserialize<List<ImportedAudiobook>>(json);

        if (importedAudiobooks == null)
        {
            // log the error
            App.ViewModel.LoggingService.LogError(new Exception("Failed to deserialize the json file"), true);
            return;
        }

        var didFail = false;
        var numberOfFiles = importedAudiobooks.Count;

        foreach (var importedAudiobook in importedAudiobooks)
        {
            // Check if cancellation was requested
            cancellationToken.ThrowIfCancellationRequested();

            // verify that the audiobook file exists
            if (!File.Exists(importedAudiobook.FilePath))
            {
                // log the error
                App.ViewModel.LoggingService.LogError(new Exception("Audiobook file does not exist"));
                App.ViewModel.EnqueueNotification(new Notification
                {
                    Message = $"Audiobook file was moved or deleted: {importedAudiobook.FilePath}",
                    Severity = InfoBarSeverity.Warning
                });

                didFail = true;
                continue;
            }

            var audiobook = await CreateAudiobook(importedAudiobook.FilePath, importedAudiobook);

            if (audiobook == null)
            {
                didFail = true;
            }
            else
            {
                // insert the audiobook into the database
                var result = await App.Repository.Audiobooks.UpsertAsync(audiobook);
                if (result == null) didFail = true;
            }

            var title = audiobook?.Title ?? Path.GetFileNameWithoutExtension(importedAudiobook.FilePath);

            // report progress
            await progressCallback(importedAudiobooks.IndexOf(importedAudiobook), numberOfFiles, title, didFail);

            didFail = false;
        }

        ImportCompleted?.Invoke();
    }

    public async Task ImportFromMultipleFilesAsync(string[] paths, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback)
    {
        var didFail = false;

        // todo: need to see if we can call progressCallback from the CreateAudiobook function
        var numberOfFiles = 1; // paths.Length;

        // Check if cancellation was requested
        cancellationToken.ThrowIfCancellationRequested();

        var audiobook = await CreateAudiobookFromMultipleFiles(paths);

        if (audiobook == null) didFail = true;

        if (audiobook != null)
        {
            var existingAudioBook = await App.Repository.Audiobooks.GetByTitleAuthorComposerAsync(audiobook.Title,
                audiobook.Author,
                audiobook.Composer);
            if (existingAudioBook != null)
            {
                // log the error
                App.ViewModel.LoggingService.LogError(new Exception("Audiobook already exists in the database"));
                App.ViewModel.EnqueueNotification(new Notification
                {
                    Message = $"Audiobook is already in the library: {existingAudioBook.Title}",
                    Severity = InfoBarSeverity.Warning
                });

                didFail = true;

                await progressCallback(numberOfFiles, numberOfFiles, audiobook.Title, didFail);

                ImportCompleted?.Invoke();

                return;
            }

            // insert the audiobook into the database
            var result = await App.Repository.Audiobooks.UpsertAsync(audiobook);
            if (result == null) didFail = true;
        }

        var title = audiobook?.Title ?? Path.GetFileNameWithoutExtension(paths.First());

        // report progress
        await progressCallback(numberOfFiles, numberOfFiles, title, didFail);

        ImportCompleted?.Invoke();
    }

    public async Task ImportFileAsync(string path, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback)
    {
        // Check if cancellation was requested
        cancellationToken.ThrowIfCancellationRequested();

        var didFail = false;
        var audiobook = await CreateAudiobook(path);

        if (audiobook == null) didFail = true;

        // insert the audiobook into the database
        if (audiobook != null)
        {
            var result = await App.Repository.Audiobooks.UpsertAsync(audiobook);
            if (result == null) didFail = true;
        }

        var title = audiobook?.Title ?? Path.GetFileNameWithoutExtension(path);

        // report progress
        // NOTE: keeping this bc this function will be used in the future to import 1-to-many files
        await progressCallback(1, 1, title, didFail);

        ImportCompleted?.Invoke();
    }

    #endregion

    private static async Task<Audiobook?> CreateAudiobookFromMultipleFiles(string[] paths)
    {
        try
        {
            var audiobook = new Audiobook
            {
                CurrentSourceFileIndex = 0,
                SourcePaths = [],
                PlaybackSpeed = 1.0,
                Volume = 1.0,
                IsCompleted = false,
                DateImported = DateTime.UtcNow
            };

            var sourceFileIndex = 0;
            var chapterIndex = 0;
            foreach (var path in paths)
            {
                var track = new Track(path);

                // check if this is the 1st file
                if (audiobook.SourcePaths.Count == 0)
                {
                    // Prefer Album tag for audiobook title; fall back to Title when Album is empty.
                    audiobook.Title = track.Album.IsNullOrEmpty() ? track.Title : track.Album;
                    audiobook.Composer = track.Composer;
                    audiobook.Author = track.Artist;
                    audiobook.Description =
                        track.Description.IsNullOrEmpty()
                            ? track.Comment.IsNullOrEmpty()
                                ? track.AdditionalFields.TryGetValue("\u00A9des", out var value) ? value : track.Comment
                                : track.Comment
                            : track.Description;
                    audiobook.ReleaseDate = track.Date;
                }

                var sourceFile = new SourceFile
                {
                    Index = sourceFileIndex++,
                    FilePath = path,
                    Duration = track.Duration
                };

                audiobook.SourcePaths.Add(sourceFile);

                // Don't process chapters for mp3 files (ATL seems to be picking up junk)
                if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    // read in the chapters
                    foreach (var ch in track.Chapters)
                    {
                        var tmp = _mapper.Map<ChapterInfo>(ch);
                        tmp.Index = chapterIndex++;
                        tmp.ParentSourceFileIndex = sourceFile.Index;
                        audiobook.Chapters.Add(tmp);
                    }
                }

                if (track.Chapters.Count == 0 || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    // create a single chapter for the entire book
                    audiobook.Chapters.Add(new ChapterInfo
                    {
                        StartTime = 0,
                        EndTime = Convert.ToUInt32(audiobook.SourcePaths[sourceFileIndex - 1].Duration * 1000),
                        StartOffset = 0,
                        EndOffset = 0,
                        UseOffset = false,
                        Title = string.IsNullOrWhiteSpace(track.Title) ? 
                            (chapterIndex + 1).ToString() : (chapterIndex + 1).ToString() + " " + track.Title,
                        Index = chapterIndex++,
                        ParentSourceFileIndex = sourceFile.Index
                    });
            }

            // get duration of the entire audiobook
            audiobook.Duration = audiobook.SourcePaths.Sum(x => x.Duration);

            // save the cover image somewhere
            var firstPath = paths.First();
            var trackForImage = new Track(firstPath);
            var imageBytes = trackForImage.EmbeddedPictures.FirstOrDefault()?.PictureData;

            if (imageBytes == null)
            {
                imageBytes = TryGetFolderCoverBytes(Path.GetDirectoryName(firstPath));
            }

            // generate hash from title, author, and composer
            var hash = $"{audiobook.Title}{audiobook.Author}{audiobook.Composer}".GetSha256Hash();

            (audiobook.CoverImagePath, audiobook.ThumbnailPath) =
                await App.ViewModel.AppDataService.WriteCoverImageAsync(hash, imageBytes);

            audiobook.CurrentChapterIndex = 0;

            return audiobook;
        }
        catch (Exception e)
        {
            // log the error
            App.ViewModel.LoggingService.LogError(e, true);
            return null;
        }
    }

    private static async Task<Audiobook?> CreateAudiobook(string path, ImportedAudiobook? importedAudiobook = null)
    {
        try
        {
            var track = new Track(path);

            var existingAudioBook =
                await App.Repository.Audiobooks.GetByTitleAuthorComposerAsync(track.Title, track.Artist,
                    track.Composer);
            if (existingAudioBook != null)
            {
                // log the error
                App.ViewModel.LoggingService.LogError(new Exception("Audiobook already exists in the database"));
                App.ViewModel.EnqueueNotification(new Notification
                {
                    Message = "Audiobook is already in the library.",
                    Severity = InfoBarSeverity.Warning
                });
                return null;
            }

            var sourceFile = new SourceFile
            {
                Index = 0,
                FilePath = path,
                Duration = track.Duration
            };

            var audiobook = new Audiobook
            {
                CurrentSourceFileIndex = 0,
                // Prefer Album tag for audiobook title; fall back to Title when Album is empty.
                Title = track.Album.IsNullOrEmpty() ? track.Title : track.Album,
                Composer = track.Composer,
                CurrentChapterIndex = importedAudiobook?.CurrentChapterIndex ?? 0,
                Duration = track.Duration,
                Author = track.Artist,
                Description =
                    track.Description.IsNullOrEmpty()
                        ? track.Comment.IsNullOrEmpty()
                            ? track.AdditionalFields.TryGetValue("\u00A9des", out var value) ? value : track.Comment
                            : track.Comment
                        : track.Description,
                PlaybackSpeed = 1.0,
                Progress = importedAudiobook?.Progress ?? 0,
                ReleaseDate = track.Date,
                Volume = 1.0,
                IsCompleted = importedAudiobook?.IsCompleted ?? false,
                IsNowPlaying = importedAudiobook?.IsNowPlaying ?? false,
                DateImported = DateTime.UtcNow,
                SourcePaths =
                [
                    sourceFile
                ]
            };

            // save the cover image somewhere
            var imageBytes = track.EmbeddedPictures.FirstOrDefault()?.PictureData;

            if (imageBytes == null)
            {
                var directory = Path.GetDirectoryName(path);
                imageBytes = TryGetFolderCoverBytes(directory);
            }

            // generate hash from title, author, and composer
            var hash = $"{audiobook.Title}{audiobook.Author}{audiobook.Composer}".GetSha256Hash();

            (audiobook.CoverImagePath, audiobook.ThumbnailPath) =
                await App.ViewModel.AppDataService.WriteCoverImageAsync(hash, imageBytes);

            // read in the chapters
            var chapterIndex = 0;
            foreach (var ch in track.Chapters)
            {
                var tmp = _mapper.Map<ChapterInfo>(ch);
                tmp.Index = chapterIndex++;
                tmp.ParentSourceFileIndex = sourceFile.Index;
                audiobook.Chapters.Add(tmp);
            }

            if (audiobook.Chapters.Count == 0)
                // create a single chapter for the entire book
                audiobook.Chapters.Add(new ChapterInfo
                {
                    StartTime = 0,
                    EndTime = Convert.ToUInt32(audiobook.SourcePaths.First().Duration * 1000),
                    StartOffset = 0,
                    EndOffset = 0,
                    UseOffset = false,
                    Title = audiobook.Title,
                    Index = 0,
                    ParentSourceFileIndex = sourceFile.Index
                });

            return audiobook;
        }
        catch (Exception e)
        {
            // log the error
            App.ViewModel.LoggingService.LogError(e, true);
            return null;
        }
    }

    /// <summary>
    ///     Attempts to find a suitable cover image in the given directory.
    ///     Preference order:
    ///     1. Files named &quot;cover.*&quot; or &quot;folder.*&quot; (case-insensitive).
    ///     2. Otherwise, the largest image file by size.
    ///     Returns null if no image files are found or directory is invalid.
    /// </summary>
    private static byte[]? TryGetFolderCoverBytes(string? directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            var dirInfo = new DirectoryInfo(directory);

            // Restrict to common image extensions
            var imageFiles = dirInfo.GetFiles()
                .Where(f =>
                    f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (imageFiles.Count == 0)
            {
                return null;
            }

            // 1. Prefer &quot;cover.*&quot; or &quot;folder.*&quot; (base name only, case-insensitive)
            var preferred = imageFiles.FirstOrDefault(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f.Name);
                return name.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("folder", StringComparison.OrdinalIgnoreCase);
            });

            var selected = preferred ?? imageFiles.OrderByDescending(f => f.Length).First();

            return File.ReadAllBytes(selected.FullName);
        }
        catch (Exception e)
        {
            App.ViewModel.LoggingService.LogError(e, true);
            return null;
        }
    }
}
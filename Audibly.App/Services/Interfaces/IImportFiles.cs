// Author: rstewa · https://github.com/rstewa
// Created: 04/15/2024
// Updated: 10/03/2024

using System;
using System.Threading;
using System.Threading.Tasks;
using Audibly.Models;
using Windows.Storage;

namespace Audibly.App.Services.Interfaces;

public interface IImportFiles
{
    public delegate void ImportCompletedHandler();

    public event ImportCompletedHandler ImportCompleted;

    Task ImportDirectoryAsync(string path, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback);

    Task ImportFileAsync(string path, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback);

    Task ImportFromMultipleFilesAsync(string[] paths, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback);

    Task ImportFromJsonAsync(StorageFile file, CancellationToken cancellationToken,
        Func<int, int, string, bool, Task> progressCallback);

    /// <summary>
    ///     Scrapes metadata/chapters/cover art for a single audio file into an in-memory Audiobook,
    ///     without writing it to the database. Used by the "review before import" flow.
    /// </summary>
    Task<Audiobook?> ScrapeAudiobookAsync(string path);

    /// <summary>
    ///     Scrapes metadata/chapters/cover art for a single audiobook made up of multiple source files
    ///     into an in-memory Audiobook, without writing it to the database. Used by the
    ///     "review before import" flow.
    /// </summary>
    Task<Audiobook?> ScrapeAudiobookFromMultipleFilesAsync(string[] paths);
}
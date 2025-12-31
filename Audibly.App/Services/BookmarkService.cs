// Author: rstewa · https://github.com/rstewa
// Created: 12/31/2025

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Audibly.App.ViewModels;
using Audibly.Models;
using Microsoft.EntityFrameworkCore;

namespace Audibly.App.Services;

public class BookmarkService
{
    /// <summary>
    ///     Load bookmarks for an audiobook, ordered by position ascending.
    /// </summary>
    public async Task<ObservableCollection<Bookmark>> GetBookmarksAsync(Guid audiobookId)
    {
        var items = await App.Repository.Bookmarks.GetByAudiobookAsync(audiobookId);
        return new ObservableCollection<Bookmark>(items.OrderBy(b => b.PositionMs));
    }

    /// <summary>
    ///     Add a bookmark for the current playback position. Computes absolute position across all source files.
    /// </summary>
    /// <param name="player">The shared PlayerViewModel.</param>
    /// <param name="noteText">Optional user-entered note. If null/empty, a timestamp will be used.</param>
    /// <param name="listToUpdate">Optional collection to update (inserted in sorted order).</param>
    public async Task<Bookmark?> AddBookmarkForCurrentPositionAsync(PlayerViewModel player, string? noteText = null, ObservableCollection<Bookmark>? listToUpdate = null)
    {
        var nowPlaying = player.NowPlaying;
        if (nowPlaying == null) return null;

        // Compute absolute position (ms) across audiobook
        long absolutePositionMs = (long)player.CurrentPosition.TotalMilliseconds;
        if (nowPlaying.CurrentSourceFileIndex > 0)
        {
            for (var i = 0; i < nowPlaying.CurrentSourceFileIndex; i++)
            {
                // SourceFile.Duration is in seconds; convert to ms
                absolutePositionMs += nowPlaying.SourcePaths[i].Duration * 1000;
            }
        }

        var note = string.IsNullOrWhiteSpace(noteText)
            ? DateTime.Now.ToString("MM/dd/yyyy HH:mm")
            : noteText.Trim();

        var bookmark = new Bookmark
        {
            AudiobookId = nowPlaying.Id,
            Note = note,
            PositionMs = absolutePositionMs,
            CreatedAtUtc = DateTime.UtcNow
        };

        var saved = await App.Repository.Bookmarks.UpsertAsync(bookmark);
        if (saved != null && listToUpdate != null)
        {
            InsertSorted(listToUpdate, saved);
        }

        return saved;
    }

    /// <summary>
    ///     Delete a bookmark and update the optional list.
    /// </summary>
    public async Task<bool> DeleteBookmarkAsync(Bookmark bookmark, ObservableCollection<Bookmark>? listToUpdate = null)
    {
        try
        {
            await App.Repository.Bookmarks.DeleteAsync(bookmark.Id);
            listToUpdate?.Remove(bookmark);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already deleted; ensure UI is updated
            listToUpdate?.Remove(bookmark);
            return true;
        }
    }

    /// <summary>
    ///     Navigate to a specific bookmark position.
    /// </summary>
    public void NavigateToBookmark(Bookmark bookmark)
    {
        App.PlayerViewModel.JumpToPosition(bookmark.PositionMs);
    }

    public static void InsertSorted(ObservableCollection<Bookmark> list, Bookmark item)
    {
        var index = list.TakeWhile(b => b.PositionMs < item.PositionMs).Count();
        list.Insert(index, item);
    }
}

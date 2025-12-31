// Author: rstewa · https://github.com/rstewa
// Created: 12/30/2025
// Updated: 12/30/2025

using Audibly.Models;
using Audibly.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Audibly.Repository.Sql;

public class SqlBookmarkRepository(AudiblyContext db) : IBookmarkRepository
{
    public async Task<IEnumerable<Bookmark>> GetByAudiobookAsync(Guid audiobookId)
    {
        return await db.Bookmarks
            .Where(b => b.AudiobookId == audiobookId)
            .OrderBy(b => b.PositionMs)
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<Bookmark?> GetAsync(Guid id)
    {
        return db.Bookmarks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Bookmark?> UpsertAsync(Bookmark bookmark)
    {
        var existing = await db.Bookmarks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookmark.Id);
        if (existing == null)
            db.Bookmarks.Add(bookmark);
        else
            db.Bookmarks.Update(bookmark);

        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            return null;
        }
        return bookmark;
    }

    public async Task DeleteAsync(Guid id)
    {
        var bookmark = await db.Bookmarks.FirstOrDefaultAsync(b => b.Id == id);
        if (bookmark != null)
        {
            db.Bookmarks.Remove(bookmark);
            await db.SaveChangesAsync();
        }
    }
}
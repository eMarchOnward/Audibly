// Author: rstewa · https://github.com/rstewa
// Created: 10/22/2024
// Updated: 10/24/2024

using Audibly.Models;
using Audibly.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Audibly.Repository.Sql;

public class SqlAudiobookRepository(AudiblyContext db) : IAudiobookRepository
{
    #region IAudiobookRepository Members

    public async Task<IEnumerable<Audiobook>> GetAsync()
    {
        return await db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .OrderBy(audiobook => audiobook.Title)
            // .AsNoTracking()  // todo: testing this out
            .ToListAsync();
    }

    public Task<Audiobook?> GetNowPlayingAsync()
    {
        // return now playing audiobook
        return db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .AsNoTracking()
            .FirstOrDefaultAsync(audiobook => audiobook.IsNowPlaying);
    }

    public Task<Audiobook?> GetByTitleAuthorComposerAsync(string title, string author, string composer)
    {
        return db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .AsNoTracking()
            .FirstOrDefaultAsync(audiobook =>
                audiobook.Title == title &&
                audiobook.Author == author &&
                audiobook.Composer == composer);
    }

    // get async using filepath
    public async Task<Audiobook?> GetByFilePathAsync(string filePath)
    {
        return await db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .AsNoTracking()
            .FirstOrDefaultAsync(audiobook =>
                audiobook.SourcePaths.Any(source => source.FilePath == filePath));
    }

    public async Task<Audiobook?> GetAsync(Guid id)
    {
        return await db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .AsNoTracking()
            .FirstOrDefaultAsync(audiobook => audiobook.Id == id);
    }

    public async Task<IEnumerable<Audiobook>> GetAsync(string search)
    {
        var parameters = search.Split(' ');
        return await db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .Where(audiobook =>
                parameters.Any(parameter =>
                    audiobook.Author.StartsWith(parameter) ||
                    audiobook.Title.StartsWith(parameter) ||
                    audiobook.Description.StartsWith(parameter)))
            .OrderByDescending(audiobook =>
                parameters.Count(parameter =>
                    audiobook.Author.StartsWith(parameter) ||
                    audiobook.Title.StartsWith(parameter) ||
                    audiobook.Description.StartsWith(parameter)))
            .AsNoTracking()
            .ToListAsync();
    }

    // C#
    public async Task<Audiobook?> UpsertAsync(Audiobook audiobook)
    {
        // load tracked entity (look up by Id first, fall back to Title+Author)
        var existing = await db.Audiobooks
            .Include(a => a.SourcePaths)
            .Include(a => a.Chapters)
            .Include(a => a.Bookmarks)
            .FirstOrDefaultAsync(a => a.Id == audiobook.Id
                || (a.Title == audiobook.Title && a.Author == audiobook.Author));

        if (existing == null)
        {
            db.Audiobooks.Add(audiobook);
        }
        else
        {
            // if found by Title/Author but different Id -> handle conflict explicitly
            if (existing.Id != audiobook.Id)
                return null; // or merge, or signal conflict

            // copy scalar properties
            db.Entry(existing).CurrentValues.SetValues(audiobook);

            // reconcile SourcePaths: add/update/remove by Id
            var incomingById = audiobook.SourcePaths.ToDictionary(s => s.Id);
            foreach (var src in existing.SourcePaths.ToList())
            {
                if (!incomingById.TryGetValue(src.Id, out var incoming))
                {
                    // removed
                    db.Remove(src);
                }
                else
                {
                    // update scalar props
                    db.Entry(src).CurrentValues.SetValues(incoming);
                    incomingById.Remove(src.Id);
                }
            }
            // remaining incoming are new
            foreach (var newSrc in incomingById.Values)
                existing.SourcePaths.Add(newSrc);

            // repeat reconciliation for Chapters and Bookmarks
        }

        await db.SaveChangesAsync();
        return audiobook;
    }

    public async Task DeleteAsync(Guid audiobookId)
    {
        var audiobook = await db.Audiobooks
            .Include(x => x.SourcePaths)
            .Include(x => x.Chapters)
            .Include(x => x.Bookmarks)
            .FirstOrDefaultAsync(a => a.Id == audiobookId);

        if (audiobook != null) db.Remove(audiobook);

        try
        {
            await db.SaveChangesAsync();
        }
        // this is for a really annoying edge case I was running into for some reason
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
                if (entry.Entity is Audiobook || entry.Entity is SourceFile || entry.Entity is ChapterInfo || entry.Entity is Bookmark)
                {
                    var proposedValues = entry.CurrentValues;
                    var databaseValues = entry.GetDatabaseValues();

                    if (databaseValues == null)
                    {
                        // The entity was deleted by another process
                        db.Entry(entry.Entity).State = EntityState.Detached;
                    }
                    else
                    {
                        foreach (var property in proposedValues.Properties)
                        {
                            var proposedValue = proposedValues[property];
                            var databaseValue = databaseValues[property];

                            // Decide which value should be written to database
                            proposedValues[property] = proposedValue;
                        }

                        // Refresh original values to bypass next concurrency check
                        entry.OriginalValues.SetValues(databaseValues);
                    }
                }
                else
                {
                    throw new NotSupportedException(
                        "Don't know how to handle concurrency conflicts for "
                        + entry.Metadata.Name);
                }

            // Retry the save operation
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAllAsync(Func<int, int, string, string, Task> progressCallback)
    {
        var audiobooks = db.Audiobooks
            .Include(x => x.SourcePaths)
            .Include(x => x.Chapters)
            .Include(x => x.Bookmarks)
            .ToList();
        
        for (var i = 0; i < audiobooks.Count; i++) 
        {
            db.Remove(audiobooks[i]);
            await progressCallback(i, audiobooks.Count, audiobooks[i].Title, audiobooks[i].CoverImagePath);
        }

        await db.SaveChangesAsync();
    }

    #endregion
}
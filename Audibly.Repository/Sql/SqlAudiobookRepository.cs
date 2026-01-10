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
            .Include(x => x.Tags)
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
            .Include(x => x.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(audiobook => audiobook.IsNowPlaying);
    }

    public Task<Audiobook?> GetByTitleAuthorComposerAsync(string title, string author, string composer)
    {
        return db.Audiobooks
            .Include(x => x.SourcePaths.OrderBy(source => source.Index))
            .Include(x => x.Chapters.OrderBy(chapter => chapter.Index))
            .Include(x => x.Bookmarks)
            .Include(x => x.Tags)
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
            .Include(x => x.Tags)
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
            .Include(x => x.Tags)
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
            .Include(x => x.Tags)
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
        try
        {
            // load tracked entity (look up by Id first, fall back to Title+Author)
            var existing = await db.Audiobooks
                .Include(a => a.SourcePaths)
                .Include(a => a.Chapters)
                .Include(a => a.Bookmarks)
                .Include(a => a.Tags)
                .FirstOrDefaultAsync(a => a.Id == audiobook.Id
                    || (a.Title == audiobook.Title && a.Author == audiobook.Author));

            if (existing == null)
            {
                // For new audiobook, resolve tags first
                await ResolveTags(audiobook);
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
                var incomingById = audiobook.SourcePaths?.ToDictionary(s => s.Id) ?? new Dictionary<Guid, SourceFile>();
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

                // Handle Tags: completely replace the collection
                await UpdateTags(existing, audiobook.Tags ?? new List<Tag>());

                // repeat reconciliation for Chapters and Bookmarks
            }

            await db.SaveChangesAsync();
            return audiobook;
        }
        catch (Exception ex)
        {
            // Log the actual exception details
            Console.WriteLine($"Error in UpsertAsync: {ex.Message}");
            Console.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
            throw;
        }
    }

    private async Task UpdateTags(Audiobook existingAudiobook, List<Tag> newTags)
    {
        try
        {
            // Remove all existing tag relationships
            existingAudiobook.Tags.Clear();

            // If no new tags, we're done
            if (newTags == null || newTags.Count == 0)
                return;

            // Process each new tag
            foreach (var incomingTag in newTags)
            {
                System.Diagnostics.Debug.WriteLine($"Processing tag: {incomingTag.Name} (Normalized: {incomingTag.NormalizedName}, Id: {incomingTag.Id})");
                
                // Find or create the tag in the database
                var dbTag = await db.Tags
                    .Where(t => t.NormalizedName == incomingTag.NormalizedName)
                    .FirstOrDefaultAsync();

                if (dbTag != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Found existing tag in DB: {dbTag.Name} (Id: {dbTag.Id})");
                    
                    // Tag exists - attach it if not already tracked
                    var entry = db.Entry(dbTag);
                    System.Diagnostics.Debug.WriteLine($"Tag entry state: {entry.State}");
                    
                    if (entry.State == EntityState.Detached)
                    {
                        db.Tags.Attach(dbTag);
                        System.Diagnostics.Debug.WriteLine("Attached tag to context");
                    }
                    existingAudiobook.Tags.Add(dbTag);
                    System.Diagnostics.Debug.WriteLine("Added tag to audiobook");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Creating new tag");
                    
                    // Tag doesn't exist - create a new one
                    var newTag = new Tag
                    {
                        Id = Guid.NewGuid(),
                        Name = incomingTag.Name,
                        NormalizedName = incomingTag.NormalizedName
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"New tag created with Id: {newTag.Id}");
                    db.Tags.Add(newTag);
                    System.Diagnostics.Debug.WriteLine("Added new tag to context");
                    existingAudiobook.Tags.Add(newTag);
                    System.Diagnostics.Debug.WriteLine("Added new tag to audiobook");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in UpdateTags: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    private async Task ResolveTags(Audiobook audiobook)
    {
        if (audiobook.Tags == null || audiobook.Tags.Count == 0)
            return;

        var originalTags = audiobook.Tags.ToList();
        audiobook.Tags.Clear();

        foreach (var tag in originalTags)
        {
            // Check if tag already exists in database by normalized name
            var existingTag = await db.Tags
                .Where(t => t.NormalizedName == tag.NormalizedName)
                .FirstOrDefaultAsync();

            if (existingTag != null)
            {
                // Use existing tag - attach if needed
                var entry = db.Entry(existingTag);
                if (entry.State == EntityState.Detached)
                {
                    db.Tags.Attach(existingTag);
                }
                audiobook.Tags.Add(existingTag);
            }
            else
            {
                // Create new tag with explicit ID
                var newTag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = tag.Name,
                    NormalizedName = tag.NormalizedName
                };
                audiobook.Tags.Add(newTag);
            }
        }
    }

    public async Task DeleteAsync(Guid audiobookId)
    {
        var audiobook = await db.Audiobooks
            .Include(x => x.SourcePaths)
            .Include(x => x.Chapters)
            .Include(x => x.Bookmarks)
            .Include(x => x.Tags)
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
            .Include(x => x.Tags)
            .ToList();
        
        for (var i = 0; i < audiobooks.Count; i++) 
        {
            db.Remove(audiobooks[i]);
            await progressCallback(i, audiobooks.Count, audiobooks[i].Title, audiobooks[i].CoverImagePath);
        }

        await db.SaveChangesAsync();
    }

    public async Task<IEnumerable<Tag>> GetAllTagsAsync()
    {
        return await db.Tags
            .OrderBy(tag => tag.Name)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task DeleteOrphanedTagsAsync()
    {
        var orphanedTags = await db.Tags
            .Include(t => t.Audiobooks)
            .Where(t => t.Audiobooks.Count == 0)
            .ToListAsync();

        if (orphanedTags.Count > 0)
        {
            db.Tags.RemoveRange(orphanedTags);
            await db.SaveChangesAsync();
        }
    }

    #endregion
}
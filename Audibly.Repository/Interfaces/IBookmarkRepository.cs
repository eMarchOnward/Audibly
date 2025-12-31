// Author: rstewa · https://github.com/rstewa
// Created: 12/30/2025
// Updated: 12/30/2025

using Audibly.Models;

namespace Audibly.Repository.Interfaces;

public interface IBookmarkRepository
{
    Task<IEnumerable<Bookmark>> GetByAudiobookAsync(Guid audiobookId);
    Task<Bookmark?> GetAsync(Guid id);
    Task<Bookmark?> UpsertAsync(Bookmark bookmark);
    Task DeleteAsync(Guid id);
}
// Author: rstewa · https://github.com/rstewa
// Created: 12/30/2025
// Updated: 12/30/2025

namespace Audibly.Models;

/// <summary>
///     Represents a user-created bookmark within an audiobook.
/// </summary>
public class Bookmark : DbObject
{
    /// <summary>
    ///     Text note entered by the user.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    ///     When the bookmark was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Absolute position within the audiobook in milliseconds.
    /// </summary>
    public long PositionMs { get; set; }

    public Guid AudiobookId { get; set; }
    public Audiobook Audiobook { get; set; }
}
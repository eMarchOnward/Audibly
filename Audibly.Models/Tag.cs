// Author: rstewa · https://github.com/rstewa
// Created: 01/07/2025
// Updated: 01/07/2025

namespace Audibly.Models;

/// <summary>
///     Represents a tag that can be associated with audiobooks.
/// </summary>
public class Tag : DbObject, IEquatable<Tag>
{
    /// <summary>
    ///     The display name of the tag.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Normalized name for uniqueness (lowercase, trimmed).
    /// </summary>
    public string NormalizedName { get; set; }

    /// <summary>
    ///     Audiobooks associated with this tag.
    /// </summary>
    public List<Audiobook> Audiobooks { get; set; } = [];

    public bool Equals(Tag? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(NormalizedName, other.NormalizedName, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((Tag)obj);
    }

    public override int GetHashCode()
    {
        return NormalizedName?.ToLowerInvariant().GetHashCode() ?? 0;
    }
}

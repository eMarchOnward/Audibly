using System;
using System.Collections.Generic;
using System.Linq;
using Audibly.Models;

namespace Audibly.App.Helpers;

/// <summary>
///     Shared text-sanitization helpers for audiobook metadata edited by the user
///     (used by both the Edit Info dialog and the tag-management flyouts).
/// </summary>
public static class AudiobookEditHelpers
{
    /// <summary>
    ///     Sorts tag names with symbols/punctuation before letters (e.g. "_foobar" sorts before
    ///     "Fiction"), case-insensitively among letters. Everywhere tags are listed in the UI should
    ///     sort with this comparer so ordering is consistent across the app. Sorting happens here in
    ///     the app layer rather than in the database query, since this ordering can't be translated to
    ///     SQL — a plain SQL/culture/ordinal sort would place "_foobar" differently in each place.
    /// </summary>
    public static readonly IComparer<string> TagNameComparer = Comparer<string>.Create(CompareTagNames);

    private static int CompareTagNames(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;

        var len = Math.Min(x.Length, y.Length);
        for (var i = 0; i < len; i++)
        {
            var cx = x[i];
            var cy = y[i];

            // Non-letters (digits, punctuation, symbols, whitespace) sort before letters.
            var rankX = char.IsLetter(cx) ? 1 : 0;
            var rankY = char.IsLetter(cy) ? 1 : 0;
            if (rankX != rankY) return rankX - rankY;

            var nx = char.ToUpperInvariant(cx);
            var ny = char.ToUpperInvariant(cy);
            if (nx != ny) return nx - ny;
        }

        return x.Length - y.Length;
    }

    public static string SanitizeForSqlite(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Trim, remove non-printable control characters except CR/LF/TAB, and escape single quotes
        var trimmed = value.Trim();
        var sanitizedChars = trimmed.Where(c => c == '\n' || c == '\r' || c == '\t' || !char.IsControl(c));
        var sanitized = new string(sanitizedChars.ToArray());
        // Normalize newlines to \n
        sanitized = sanitized.Replace("\r\n", "\n").Replace("\r", "\n");
        // Escape single quotes for SQLite text safety (although EF parameterizes, this is defensive)
        sanitized = sanitized.Replace("'", "''");
        return sanitized;
    }

    public static string NormalizeTagName(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return string.Empty;

        // Trim leading/trailing spaces
        var normalized = tagName.Trim();

        // Remove any non-alphanumeric characters except spaces and hyphens
        normalized = new string(normalized.Where(c => char.IsLetterOrDigit(c) ||
            c == ' ' || c == '-' || c == '|' || c == '/' || c == '_').ToArray());

        // Convert to lowercase for case-insensitive comparison
        return normalized.ToLowerInvariant();
    }

    public static List<Tag> ParseTagsFromText(string tagsText)
    {
        if (string.IsNullOrWhiteSpace(tagsText))
            return new List<Tag>();

        var tagNames = tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = new List<Tag>();

        foreach (var tagName in tagNames)
        {
            var displayName = tagName.Trim(); // Keep spaces in display name
            var normalizedName = NormalizeTagName(tagName);

            if (string.IsNullOrEmpty(normalizedName))
                continue;

            // Check if we already have this tag (avoid duplicates)
            if (tags.Any(t => t.NormalizedName == normalizedName))
                continue;

            tags.Add(new Tag
            {
                Name = displayName,
                NormalizedName = normalizedName
            });
        }

        return tags;
    }
}

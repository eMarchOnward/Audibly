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

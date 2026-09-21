// Author: rstewa · https://github.com/rstewa
// Created: 3/21/2024
// Updated: 3/22/2024

using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;

namespace Audibly.App.ViewModels;

/// <summary>
///     Provides static methods for use in x:Bind function binding to convert bound values to the required value.
/// </summary>
public static class Converters
{
    /// <summary>
    ///     Returns the reverse of the provided value.
    /// </summary>
    public static bool Not(bool value)
    {
        return !value;
    }

    /// <summary>
    ///     Returns true if the specified value is not null; otherwise, returns false.
    /// </summary>
    public static bool IsNotNull(object value)
    {
        return value != null;
    }

    /// <summary>
    ///     Returns Visibility.Collapsed if the specified value is true; otherwise, returns Visibility.Visible.
    /// </summary>
    public static Visibility CollapsedIf(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Visibility CollapsedIfNot(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    ///     Returns Visibility.Visible only when both values are true; otherwise, returns Visibility.Collapsed.
    /// </summary>
    public static Visibility CollapsedIfNot(bool a, bool b)
    {
        return a && b ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    ///     Returns Visibility.Collapsed if the specified value is null; otherwise, returns Visibility.Visible.
    /// </summary>
    public static Visibility CollapsedIfNull(object? value)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    ///     Returns Visibility.Collapsed if the specified string is null or empty; otherwise, returns Visibility.Visible.
    /// </summary>
    public static Visibility CollapsedIfNullOrEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    ///     Returns Visibility.Collapsed if the specified string is null or empty; otherwise, returns Visibility.Visible.
    /// </summary>
    public static Visibility CollapsedIfNullOrEmpty<TEntity>(ObservableCollection<TEntity> value)
    {
        return value == null || value.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Visibility VisibleIfNullOrEmpty<TEntity>(ObservableCollection<TEntity> value)
    {
        return value == null || value.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    ///     Returns Visibility.Visible if the specified count is zero; otherwise, returns Visibility.Collapsed.
    ///     Non-generic on purpose — x:Bind function binding crashes the XAML compiler on a generic method
    ///     (e.g. the &lt;TEntity&gt; overloads above), so bind to a collection's .Count instead of the
    ///     collection itself when you need this from XAML.
    /// </summary>
    public static Visibility VisibleIfZero(int count)
    {
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    ///     Side length of the square cover area inside an AudiobookTile of the given overall tile width,
    ///     after subtracting the tile's own Button.Padding (11 on each side = 22 total). The cover Border's
    ///     MaxHeight can't know about that padding on its own, so binding it to the raw tile width made the
    ///     cover area 22px taller than it was wide — a self-referencing ActualWidth binding was tried as a
    ///     fix but made covers disappear (ActualWidth starts at 0 before layout runs and got stuck there).
    ///     This plain top-down calculation avoids that.
    /// </summary>
    public static double TileCoverSize(double tileWidth)
    {
        return Math.Max(0, tileWidth - 22);
    }
}
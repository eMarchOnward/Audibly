using System.ComponentModel;
using Audibly.Models;

namespace Audibly.App.ViewModels;

/// <summary>
///     A single row in the Tags filter dropdown: pairs a Tag with whether it's currently checked.
///     Kept separate from the Tag model so the checkbox can be bound directly and toggled
///     programmatically (e.g. when a search-bar pill is removed) without touching the DB model.
/// </summary>
public sealed class TagFilterItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public TagFilterItem(Tag tag, bool isChecked)
    {
        Tag = tag;
        _isChecked = isChecked;
    }

    public Tag Tag { get; }

    public string Name => Tag.Name;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Audibly.App.Extensions;
using Audibly.Models;
using CommunityToolkit.Mvvm.Input;

namespace Audibly.App.ViewModels;

public partial class BookmarksViewModel : BindableBase
{
    private string _newBookmarkNote = string.Empty;

    public BookmarksViewModel()
    {
        AddBookmarkCommand = new AsyncRelayCommand(AddBookmarkAsync);
        DeleteBookmarkCommand = new AsyncRelayCommand<Bookmark>(DeleteBookmarkAsync);
        JumpToBookmarkCommand = new RelayCommand<Bookmark>(JumpToBookmark);
    }

    public ObservableCollection<Bookmark> Bookmarks { get; } = new();

    public string NewBookmarkNote
    {
        get => _newBookmarkNote;
        set => Set(ref _newBookmarkNote, value);
    }

    public IAsyncRelayCommand AddBookmarkCommand { get; }
    public IAsyncRelayCommand<Bookmark> DeleteBookmarkCommand { get; }
    public IRelayCommand<Bookmark> JumpToBookmarkCommand { get; }

    public async Task LoadBookmarksAsync(Guid audiobookId)
    {
        var bookmarks = await App.Repository.Bookmarks.GetByAudiobookAsync(audiobookId);
        Bookmarks.Clear();
        foreach (var bookmark in bookmarks.OrderBy(b => b.PositionMs))
        {
            Bookmarks.Add(bookmark);
        }
    }

    private async Task AddBookmarkAsync()
    {
        if (App.PlayerViewModel.NowPlaying == null) return;

        var note = string.IsNullOrWhiteSpace(NewBookmarkNote)
            ? DateTime.Now.ToString("MM/dd/yyyy hh:ss")
            : NewBookmarkNote;

        var bookmark = new Bookmark
        {
            AudiobookId = App.PlayerViewModel.NowPlaying.Id,
            Note = note,
            PositionMs = (long)App.PlayerViewModel.CurrentPosition.TotalMilliseconds,
            CreatedAtUtc = DateTime.UtcNow
        };

        var saved = await App.Repository.Bookmarks.UpsertAsync(bookmark);
        if (saved == null) return;
        
        // Insert sorted by position
        var index = Bookmarks.TakeWhile(b => b.PositionMs < saved.PositionMs).Count();
        Bookmarks.Insert(index, saved);

        NewBookmarkNote = string.Empty;
    }

    private async Task DeleteBookmarkAsync(Bookmark? bookmark)
    {
        if (bookmark == null) return;

        await App.Repository.Bookmarks.DeleteAsync(bookmark.Id);
        Bookmarks.Remove(bookmark);
    }

    private void JumpToBookmark(Bookmark? bookmark)
    {
        if (bookmark == null || App.PlayerViewModel.NowPlaying == null) return;
        App.PlayerViewModel.CurrentPosition = TimeSpan.FromMilliseconds(bookmark.PositionMs);
    }
}
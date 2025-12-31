// Author: rstewa · https://github.com/rstewa
// Updated: 06/09/2025

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.Models;
using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using ColorHelper = CommunityToolkit.WinUI.Helpers.ColorHelper;

namespace Audibly.App.UserControls;

public sealed partial class AudiobookTile : UserControl
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private ObservableCollection<Audibly.Models.Bookmark> _bookmarks = new();

    public AudiobookTile()
    {
        InitializeComponent();
    }

    private MenuFlyout? GetMenuFlyout() => Resources["MenuFlyout"] as MenuFlyout;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    private static PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    private MainViewModel ViewModel => App.ViewModel;

    public Guid Id
    {
        get => (Guid)GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    public static readonly DependencyProperty IdProperty =
        DependencyProperty.Register(nameof(Id), typeof(Guid), typeof(AudiobookTile), new PropertyMetadata(Guid.Empty));

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(AudiobookTile), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AudiobookTile), new PropertyMetadata(string.Empty));

    public string Author
    {
        get => (string)GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }
    public static readonly DependencyProperty AuthorProperty =
        DependencyProperty.Register(nameof(Author), typeof(string), typeof(AudiobookTile), new PropertyMetadata(string.Empty));

    public bool IsCompleted
    {
        get => (bool)GetValue(IsCompletedProperty);
        set => SetValue(IsCompletedProperty, value);
    }
    public static readonly DependencyProperty IsCompletedProperty =
        DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(AudiobookTile), new PropertyMetadata(false));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(AudiobookTile), new PropertyMetadata(0.0));

    public System.Collections.Generic.List<SourceFile> SourcePaths
    {
        get => (System.Collections.Generic.List<SourceFile>)GetValue(SourcePathsProperty);
        set => SetValue(SourcePathsProperty, value);
    }
    public static readonly DependencyProperty SourcePathsProperty =
        DependencyProperty.Register(nameof(SourcePaths), typeof(System.Collections.Generic.List<SourceFile>), typeof(AudiobookTile), new PropertyMetadata(null));

    public int SourcePathsCount
    {
        get => (int)GetValue(SourcePathsCountProperty);
        set => SetValue(SourcePathsCountProperty, value);
    }
    public static readonly DependencyProperty SourcePathsCountProperty =
        DependencyProperty.Register(nameof(SourcePathsCount), typeof(int), typeof(AudiobookTile), new PropertyMetadata(0));

    public object Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(AudiobookTile), new PropertyMetadata(null));

    private void AudiobookTile_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BlackOverlayGrid.Visibility = Visibility.Visible;
        ButtonTile.Background =
            new SolidColorBrush(ColorHelper.ToColor("#393939")); // Change background to indicate hover
    }

    private void AudiobookTile_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var flyout = GetMenuFlyout();
        if (flyout?.IsOpen == true) return;
        BlackOverlayGrid.Visibility = Visibility.Collapsed;
        ButtonTile.Background = new SolidColorBrush(Colors.Transparent); // Revert background to original
    }

    private void MenuFlyout_Closed(object sender, object e)
    {
        BlackOverlayGrid.Visibility = Visibility.Collapsed;
        ButtonTile.Background = new SolidColorBrush(Colors.Transparent); // Revert background to original
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            await PlayerViewModel.OpenAudiobook(audiobook);

            // todo: this really breaks shit
            // PlayerViewModel.MediaPlayer.Play();
        });
    }

    private void ShowInFileExplorer_OnClick(object sender, RoutedEventArgs e)
    {
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/select, \"{FilePath}\"";
        p.Start();
    }

    private async void DeleteAudiobook_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        ViewModel.SelectedAudiobook = audiobook;

        await ViewModel.DeleteAudiobookAsync();
    }

    private async void ChangeCover_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        // Show file picker for supported image formats
        var supportedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };
        var selectedFile = ViewModel.FileDialogService.OpenFileDialog(supportedImageTypes, PickerLocationId.PicturesLibrary);
        
        if (selectedFile == null) return; // User cancelled
        
        try
        {
            // Read the selected image file
            var imageBytes = await FileIO.ReadBufferAsync(selectedFile);
            var imageBytesArray = new byte[imageBytes.Length];
            using (var reader = DataReader.FromBuffer(imageBytes))
            {
                reader.ReadBytes(imageBytesArray);
            }
            
            // Generate the folder path for this audiobook's data
            var folderName = $"{audiobook.Title}_{audiobook.Id}".Replace(" ", "_");
            
            // Delete old cover images first
            if (!string.IsNullOrEmpty(audiobook.Model.CoverImagePath))
            {
                await ViewModel.AppDataService.DeleteCoverImageAsync(audiobook.Model.CoverImagePath);
            }
            
            // Create new cover image and thumbnail using the same process as import
            var (coverImagePath, thumbnailPath) = await ViewModel.AppDataService.WriteCoverImageAsync(folderName, imageBytesArray);
            
            if (!string.IsNullOrEmpty(coverImagePath))
            {
                // Update the audiobook model with new cover paths
                audiobook.Model.CoverImagePath = coverImagePath;
                audiobook.Model.ThumbnailPath = thumbnailPath;
                
                // Save the updated audiobook to database first
                await audiobook.SaveAsync();
                
                // Force refresh of cover image properties in the UI with a small delay
                // This ensures the new image files are fully written before UI tries to load them
                await Task.Delay(100);
                audiobook.RefreshCoverImage();
                
                // Show success notification
                ViewModel.EnqueueNotification(new Notification
                {
                    Message = "Cover image updated successfully!",
                    Severity = InfoBarSeverity.Success
                });
            }
            else
            {
                // Show error notification
                ViewModel.EnqueueNotification(new Notification
                {
                    Message = "Failed to update cover image.",
                    Severity = InfoBarSeverity.Error
                });
            }
        }
        catch (Exception ex)
        {
            ViewModel.LoggingService.LogError(ex, true);
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "An error occurred while updating the cover image.",
                Severity = InfoBarSeverity.Error
            });
        }
    }

    private void ButtonTile_OnRightTapped(object sender, RightTappedRoutedEventArgs? e)
    {
        if (e is null) return;
        var myOption = new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Transient
        };
        var flyout = GetMenuFlyout();
        flyout?.ShowAt(ButtonTile, myOption);
    }

    private void OpenInAppFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        var dir = Path.GetDirectoryName(audiobook.CoverImagePath);
        if (dir == null) return;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{dir}\"";
        p.Start();
    }

    private async void MoreInfo_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        var flyout = GetMenuFlyout();
        flyout?.Hide();

        // note: content dialog
        await DialogService.ShowMoreInfoDialogAsync(audiobook);
    }

    private async void MarkAsCompleted_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        audiobook.IsCompleted = true;
        await audiobook.SaveAsync();
    }

    private async void MarkAsIncomplete_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        audiobook.IsCompleted = false;
        await audiobook.SaveAsync();
    }

    private void ExportMetadataToJson_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        ViewModel.AppDataService.ExportMetadataAsync(audiobook.SourcePaths)
            .ContinueWith(task =>
            {
                if (task.IsFaulted) App.ViewModel.LoggingService.LogError(task.Exception, true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private T? GetFlyoutElement<T>(object sender, string name) where T : FrameworkElement
    {
        if (sender is Flyout flyout && flyout.Content is FrameworkElement root)
            return root.FindName(name) as T;
        return null;
    }

    private async void BookmarksFlyout_Opened(object sender, object e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        var items = await App.Repository.Bookmarks.GetByAudiobookAsync(audiobook.Id);
        _bookmarks = new System.Collections.ObjectModel.ObservableCollection<Audibly.Models.Bookmark>(items.OrderBy(b => b.PositionMs));
        var list = GetFlyoutElement<ListView>(sender, "BookmarksListView");
        if (list != null) list.ItemsSource = _bookmarks;
    }

    private async void AddBookmark_Click(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        try
        {
            // Resolve the note TextBox from this control's Flyout content
            var flyout = BookmarksButton?.Flyout as Flyout;
            var root = flyout?.Content as FrameworkElement;
            var noteBox = root?.FindName("BookmarksNoteTextBox") as TextBox;

            var noteText = noteBox?.Text?.Trim() ?? string.Empty;
            var note = noteText.Length > 0 ? noteText : DateTime.Now.ToString("MM/dd/yyyy HH:mm");

            var bookmark = new Audibly.Models.Bookmark
            {
                AudiobookId = audiobook.Id,
                Note = note,
                PositionMs = (long)App.PlayerViewModel.CurrentPosition.TotalMilliseconds,
                CreatedAtUtc = DateTime.UtcNow
            };
            var saved = await App.Repository.Bookmarks.UpsertAsync(bookmark);
            if (saved == null) return;
            var index = _bookmarks.TakeWhile(b => b.PositionMs < saved.PositionMs).Count();
            _bookmarks.Insert(index, saved);
            if (noteBox != null) noteBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            App.ViewModel.LoggingService?.LogError(ex, true);
        }
    }

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Audibly.Models.Bookmark bookmark)
            App.PlayerViewModel.JumpToPosition(bookmark.PositionMs);
    }

    private async void DeleteBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Audibly.Models.Bookmark bookmark)
        {
            try
            {
                button.IsEnabled = false; // prevent double-click re-entrancy
                await App.Repository.Bookmarks.DeleteAsync(bookmark.Id);
                _bookmarks.Remove(bookmark);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // Row was already deleted; update UI and swallow
                _bookmarks.Remove(bookmark);
            }
            catch (Exception ex)
            {
                App.ViewModel.LoggingService?.LogError(ex, true);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }
}
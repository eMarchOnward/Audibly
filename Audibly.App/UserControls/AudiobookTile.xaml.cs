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
    private readonly BookmarkService _bookmarkService = new();

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
        var dir = System.IO.Path.GetDirectoryName(audiobook.CoverImagePath);
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

    private static string SanitizeForSqlite(string? value)
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

    private async void EditInfo_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        ViewModel.SelectedAudiobook = audiobook;

        // Build dialog content inline
        var thumbnail = new Image
        {
            Width = 96,
            Height = 96,
            Stretch = Stretch.UniformToFill
        };
        thumbnail.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath("SelectedAudiobook.ThumbnailPath")
        });

        var titleBox = new TextBox { Header = "Title" };
        titleBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath("SelectedAudiobook.Model.Title"),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = Microsoft.UI.Xaml.Data.UpdateSourceTrigger.PropertyChanged
        });

        var authorBox = new TextBox { Header = "Author" };
        authorBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath("SelectedAudiobook.Model.Author"),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = Microsoft.UI.Xaml.Data.UpdateSourceTrigger.PropertyChanged
        });

        var narratorBox = new TextBox { Header = "Narrator" };
        narratorBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath("SelectedAudiobook.Model.Composer"),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = Microsoft.UI.Xaml.Data.UpdateSourceTrigger.PropertyChanged
        });

        var descBox = new TextBox { Header = "Description", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120 };
        descBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath("SelectedAudiobook.Model.Description"),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = Microsoft.UI.Xaml.Data.UpdateSourceTrigger.PropertyChanged
        });

        var fieldsPanel = new StackPanel { Spacing = 8 };
        fieldsPanel.Children.Add(titleBox);
        fieldsPanel.Children.Add(authorBox);
        fieldsPanel.Children.Add(narratorBox);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(thumbnail, 0);
        grid.Children.Add(thumbnail);
        Grid.SetColumn(fieldsPanel, 1);
        grid.Children.Add(fieldsPanel);

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(12) };
        panel.Children.Add(grid);
        panel.Children.Add(descBox);
        panel.DataContext = ViewModel;

        var dialog = new ContentDialog
        {
            Title = "Edit Info",
            Content = panel,
            PrimaryButtonText = "OK",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            MinWidth = 420
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Read values directly from textboxes and sanitize
            var newTitle = SanitizeForSqlite(titleBox.Text);
            var newAuthor = SanitizeForSqlite(authorBox.Text);
            var newNarrator = SanitizeForSqlite(narratorBox.Text);
            var newDescription = SanitizeForSqlite(descBox.Text);

            // Apply to model
            audiobook.Model.Title = newTitle;
            audiobook.Model.Author = newAuthor;
            audiobook.Model.Composer = newNarrator;
            audiobook.Model.Description = newDescription;

            // Persist
            await audiobook.SaveAsync();
            audiobook.RefreshCoverImage();

            // Refresh Library view so updated metadata appears
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                await ViewModel.GetAudiobookListAsync();
            });

            // If the edited audiobook is currently playing, update NowPlaying metadata
            var now = PlayerViewModel.NowPlaying;
            if (now != null && now.Id == audiobook.Id)
            {
                now.Model.Title = audiobook.Model.Title;
                now.Model.Author = audiobook.Model.Author;
            }
        }
    }
}
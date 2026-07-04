// Author: rstewa · https://github.com/rstewa
// Updated: 06/09/2025

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Audibly.App.Extensions;
using Audibly.App.Helpers;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.Models;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
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
    private bool _playButtonHovered;
    private bool _ellipsisButtonHovered;

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

    public long Duration
    {
        get => (long)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(long), typeof(AudiobookTile), new PropertyMetadata(0L));

    public int ChapterCount
    {
        get => (int)GetValue(ChapterCountProperty);
        set => SetValue(ChapterCountProperty, value);
    }
    public static readonly DependencyProperty ChapterCountProperty =
        DependencyProperty.Register(nameof(ChapterCount), typeof(int), typeof(AudiobookTile), new PropertyMetadata(0));

    public object Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(AudiobookTile), new PropertyMetadata(null));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(AudiobookTile),
            new PropertyMetadata(false, static (d, _) => ((AudiobookTile)d).UpdateSelectionVisual()));

    private void UpdateSelectionVisual()
    {
        if (ButtonTile == null) return;
        if (IsSelected)
        {
            ButtonTile.BorderBrush =
                Application.Current.Resources.TryGetValue("SystemAccentColorBrush", out var b) && b is Brush accent
                ? accent : new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue);
            ButtonTile.BorderThickness = new Thickness(2);
            if (SelectionBadge != null) SelectionBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ButtonTile.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ButtonTile.BorderThickness = new Thickness(0);
            if (SelectionBadge != null) SelectionBadge.Visibility = Visibility.Collapsed;
        }
    }

    private MenuFlyout? GetMultiSelectMenuFlyout() => Resources["MultiSelectFlyout"] as MenuFlyout;

    private void AudiobookTile_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BlackOverlayGrid.Visibility = Visibility.Visible;
        ButtonTile.Background =
            new SolidColorBrush(ColorHelper.ToColor("#393939")); // Change background to indicate hover
    }

    private void AudiobookTile_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Defer so that PlayButton_OnPointerEntered can set _playButtonHovered first when
        // moving from the tile surface into the nested play button.
        _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (_playButtonHovered || _ellipsisButtonHovered) return;
            var flyout = GetMenuFlyout();
            if (flyout?.IsOpen == true || GetMultiSelectMenuFlyout()?.IsOpen == true) return;
            BlackOverlayGrid.Visibility = Visibility.Collapsed;
            ButtonTile.Background = new SolidColorBrush(Colors.Transparent);
        });
    }

    private void PlayButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _playButtonHovered = true;
        PlayButton.Opacity = 1.0;
        PlayCircle.Opacity = 0.45;
        if (PlayButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.2;
            transform.ScaleY = 1.2;
        }
    }

    private void PlayButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _playButtonHovered = false;
        PlayButton.Opacity = 0.75;
        PlayCircle.Opacity = 0;
        if (PlayButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        }
        BlackOverlayGrid.Visibility = Visibility.Visible;
    }

    private void EllipsisButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _ellipsisButtonHovered = true;
        EllipsisButton.Opacity = 1.0;
        EllipsisCircle.Opacity = 0.45;
        if (EllipsisButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.2;
            transform.ScaleY = 1.2;
        }
    }

    private void EllipsisButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _ellipsisButtonHovered = false;
        EllipsisButton.Opacity = 0.75;
        EllipsisCircle.Opacity = 0;
        if (EllipsisButton.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        }
        BlackOverlayGrid.Visibility = Visibility.Visible;
    }

    private void EllipsisButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSelection();
        var options = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard };
        GetMenuFlyout()?.ShowAt(EllipsisButton, options);
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSelection();
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        try
        {
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                var currentAudiobook = PlayerViewModel.NowPlaying;
                if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
                    await PlayerViewModel.OpenAudiobook(audiobook);
                PlayerViewModel.MediaPlayer.Play();
            });
        }
        catch (Exception ex)
        {
            ViewModel.LoggingService.LogError(ex, true);
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "Failed to open/play audiobook.",
                Severity = InfoBarSeverity.Error
            });
        }
    }

    private void MenuFlyout_Closed(object sender, object e)
    {
        BlackOverlayGrid.Visibility = Visibility.Collapsed;
        ButtonTile.Background = new SolidColorBrush(Colors.Transparent);
    }

    private void ButtonTile_Click(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
        var isShiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlPressed)
        {
            audiobook.IsSelected = !audiobook.IsSelected;
            ViewModel.SelectionAnchorId = Id;
            return;
        }

        if (isShiftPressed && ViewModel.SelectionAnchorId.HasValue)
        {
            var allBooks = ViewModel.Audiobooks.ToList();
            var anchorIdx = allBooks.FindIndex(a => a.Id == ViewModel.SelectionAnchorId.Value);
            var thisIdx = allBooks.FindIndex(a => a.Id == Id);
            if (anchorIdx >= 0 && thisIdx >= 0)
            {
                var min = Math.Min(anchorIdx, thisIdx);
                var max = Math.Max(anchorIdx, thisIdx);
                for (var i = 0; i < allBooks.Count; i++)
                    allBooks[i].IsSelected = i >= min && i <= max;
            }
            return;
        }

        // Plain click — select this tile (clear any other selection first)
        ViewModel.ClearSelection();
        audiobook.IsSelected = true;
        ViewModel.SelectionAnchorId = Id;
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

            // Generate the folder hash using the same method as FileImportService
            // This ensures we're updating the correct folder location
            var hash = $"{audiobook.Model.Title}{audiobook.Model.Author}{audiobook.Model.Composer}".GetSha256Hash();

            // Delete old cover images first
            if (!string.IsNullOrEmpty(audiobook.Model.CoverImagePath))
            {
                await ViewModel.AppDataService.DeleteCoverImageAsync(audiobook.Model.CoverImagePath);
            }

            // Create new cover image and thumbnail using WriteCoverImageAsync
            // This handles 1:1 cropping automatically
            var (coverImagePath, thumbnailPath) = await ViewModel.AppDataService.WriteCoverImageAsync(hash, imageBytesArray);

            if (!string.IsNullOrEmpty(coverImagePath))
            {
                // Update the audiobook model with new cover paths
                audiobook.Model.CoverImagePath = coverImagePath;
                audiobook.Model.ThumbnailPath = thumbnailPath;

                // Mark the audiobook as modified so SaveAsync will actually save
                audiobook.IsModified = true;

                // Save the updated audiobook to database
                await audiobook.SaveAsync();

                // Force refresh of cover image properties in the UI
                audiobook.RefreshCoverImage();

                // If the edited audiobook is currently playing, update NowPlaying cover
                var now = PlayerViewModel.NowPlaying;
                if (now != null && now.Id == audiobook.Id)
                {
                    now.Model.CoverImagePath = audiobook.Model.CoverImagePath;
                    now.Model.ThumbnailPath = audiobook.Model.ThumbnailPath;
                    now.RefreshCoverImage();
                }

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
        var options = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard };
        if (ViewModel.Audiobooks.Count(a => a.IsSelected) >= 2)
            GetMultiSelectMenuFlyout()?.ShowAt(ButtonTile, options);
        else
            GetMenuFlyout()?.ShowAt(ButtonTile, options);
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

    private static string NormalizeTagName(string tagName)
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

    private static List<Tag> ParseTagsFromText(string tagsText)
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

    private static string TagsToCommaSeparatedString(List<Tag> tags)
    {
        if (tags == null || tags.Count == 0)
            return string.Empty;

        return string.Join(", ", tags.Select(t => t.Name));
    }

    private async void DeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Audiobooks.Count(a => a.IsSelected);
        if (count == 0) return;

        var dialog = new ContentDialog
        {
            Title = "Delete selected",
            Content = $"Permanently delete {count} audiobook{(count == 1 ? "" : "s")}? This cannot be undone.",
            PrimaryButtonText = "Delete",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteSelectedAudiobooksAsync();
    }

    private async void ManageTagsSelected_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedAudiobooks = ViewModel.Audiobooks.Where(a => a.IsSelected).ToList();
        if (selectedAudiobooks.Count == 0) return;

        var allTags = (await App.Repository.Audiobooks.GetAllTagsAsync()).OrderBy(t => t.Name).ToList();

        var pendingAddTags = new ObservableCollection<Tag>();
        var pendingRemoveTags = new ObservableCollection<Tag>();

        var sectionLabelStyle = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style;
        var captionStyle = Application.Current.Resources["CaptionTextBlockStyle"] as Style;

        // --- TokenizingTextBoxes ---
        var addTagsBox = new TokenizingTextBox
        {
            PlaceholderText = "Type a tag and press Enter, or separate with commas",
            TokenDelimiter = ",",
            ItemsSource = pendingAddTags,
            TextMemberPath = "Name"
        };
        addTagsBox.TokenItemAdding += (_, args) =>
        {
            if (args.Item is Tag) return;
            var text = args.TokenText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) { args.Cancel = true; return; }
            var norm = NormalizeTagName(text);
            if (string.IsNullOrEmpty(norm)) { args.Cancel = true; return; }
            if (pendingAddTags.Any(t => t.NormalizedName == norm)) { args.Cancel = true; return; }
            args.Item = allTags.FirstOrDefault(t => t.NormalizedName == norm)
                        ?? new Tag { Name = text, NormalizedName = norm };
        };

        var removeTagsBox = new TokenizingTextBox
        {
            PlaceholderText = "Type a tag and press Enter, or separate with commas",
            TokenDelimiter = ",",
            ItemsSource = pendingRemoveTags,
            TextMemberPath = "Name"
        };
        removeTagsBox.TokenItemAdding += (_, args) =>
        {
            if (args.Item is Tag) return;
            var text = args.TokenText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) { args.Cancel = true; return; }
            var norm = NormalizeTagName(text);
            if (string.IsNullOrEmpty(norm)) { args.Cancel = true; return; }
            if (pendingRemoveTags.Any(t => t.NormalizedName == norm)) { args.Cancel = true; return; }
            var existing = allTags.FirstOrDefault(t => t.NormalizedName == norm);
            if (existing == null) { args.Cancel = true; return; }
            args.Item = existing;
        };

        // --- Tag strips (one per section) ---
        var addTagButtons = new Dictionary<string, Button>();
        var removeTagButtons = new Dictionary<string, Button>();
        StackPanel? addTagStrip = null;
        StackPanel? removeTagStrip = null;

        if (allTags.Count > 0)
        {
            addTagStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            removeTagStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            foreach (var tag in allTags)
            {
                var capturedTag = tag;

                var addBtn = new Button { Content = "+ " + tag.Name, Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
                addBtn.Click += (_, _) =>
                {
                    if (!pendingAddTags.Any(t => t.NormalizedName == capturedTag.NormalizedName))
                        pendingAddTags.Add(capturedTag);
                };
                addTagButtons[tag.NormalizedName] = addBtn;
                addTagStrip.Children.Add(addBtn);

                var removeBtn = new Button { Content = "- " + tag.Name, Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
                removeBtn.Click += (_, _) =>
                {
                    if (!pendingRemoveTags.Any(t => t.NormalizedName == capturedTag.NormalizedName))
                        pendingRemoveTags.Add(capturedTag);
                };
                removeTagButtons[tag.NormalizedName] = removeBtn;
                removeTagStrip.Children.Add(removeBtn);
            }

            pendingAddTags.CollectionChanged += (_, args) =>
            {
                if (args.NewItems != null)
                    foreach (Tag t in args.NewItems)
                        if (addTagButtons.TryGetValue(t.NormalizedName, out var btn)) btn.Visibility = Visibility.Collapsed;
                if (args.OldItems != null)
                    foreach (Tag t in args.OldItems)
                        if (addTagButtons.TryGetValue(t.NormalizedName, out var btn)) btn.Visibility = Visibility.Visible;
            };

            pendingRemoveTags.CollectionChanged += (_, args) =>
            {
                if (args.NewItems != null)
                    foreach (Tag t in args.NewItems)
                        if (removeTagButtons.TryGetValue(t.NormalizedName, out var btn)) btn.Visibility = Visibility.Collapsed;
                if (args.OldItems != null)
                    foreach (Tag t in args.OldItems)
                        if (removeTagButtons.TryGetValue(t.NormalizedName, out var btn)) btn.Visibility = Visibility.Visible;
            };
        }

        // --- Layout helpers ---
        StackPanel BuildSection(string title, StackPanel? strip, TokenizingTextBox tagsBox)
        {
            var section = new StackPanel { Spacing = 8 };
            section.Children.Add(new TextBlock { Text = title, Style = sectionLabelStyle });

            if (strip != null)
            {
                section.Children.Add(new TextBlock { Text = "Available Tags", Style = captionStyle, Opacity = 0.7 });
                section.Children.Add(new ScrollViewer
                {
                    Content = strip,
                    HorizontalScrollMode = ScrollMode.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollMode = ScrollMode.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
                });
            }

            section.Children.Add(tagsBox);
            return section;
        }

        var content = new StackPanel { Spacing = 16, MinWidth = 440, Padding = new Thickness(4) };
        content.Children.Add(BuildSection("Add Tags", addTagStrip, addTagsBox));
        content.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Margin = new Thickness(0, 4, 0, 4)
        });
        content.Children.Add(BuildSection("Remove Tags", removeTagStrip, removeTagsBox));

        var count = selectedAudiobooks.Count;
        var dialog = new ContentDialog
        {
            Title = $"Manage Tags — {count} audiobook{(count == 1 ? "" : "s")}",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520,
                VerticalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "OK",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            MinWidth = 500
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // Commit any free text still in the boxes
        foreach (var t in ParseTagsFromText(addTagsBox.Text ?? string.Empty))
            if (!pendingAddTags.Any(x => x.NormalizedName == t.NormalizedName))
                pendingAddTags.Add(allTags.FirstOrDefault(a => a.NormalizedName == t.NormalizedName) ?? t);

        foreach (var t in ParseTagsFromText(removeTagsBox.Text ?? string.Empty))
        {
            var existing = allTags.FirstOrDefault(a => a.NormalizedName == t.NormalizedName);
            if (existing != null && !pendingRemoveTags.Any(x => x.NormalizedName == t.NormalizedName))
                pendingRemoveTags.Add(existing);
        }

        if (pendingAddTags.Count == 0 && pendingRemoveTags.Count == 0) return;

        // Apply to each selected audiobook
        foreach (var audiobook in selectedAudiobooks)
        {
            var modified = false;

            foreach (var addTag in pendingAddTags)
            {
                if (!audiobook.Model.Tags.Any(t => t.NormalizedName == addTag.NormalizedName))
                {
                    audiobook.Model.Tags.Add(allTags.FirstOrDefault(t => t.NormalizedName == addTag.NormalizedName) ?? addTag);
                    modified = true;
                }
            }

            foreach (var removeTag in pendingRemoveTags)
            {
                var match = audiobook.Model.Tags.FirstOrDefault(t => t.NormalizedName == removeTag.NormalizedName);
                if (match != null)
                {
                    audiobook.Model.Tags.Remove(match);
                    modified = true;
                }
            }

            if (modified)
            {
                audiobook.IsModified = true;
                await audiobook.SaveAsync();
            }
        }

        await App.Repository.Audiobooks.DeleteOrphanedTagsAsync();
        await ViewModel.RefreshTagsForAudiobooksAsync(selectedAudiobooks);
    }

    private async void EditInfo_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        ViewModel.SelectedAudiobook = audiobook;

        var allTags = await App.Repository.Audiobooks.GetAllTagsAsync();

        // Working copy – fresh Tag instances so EF tracking on the original list is unaffected
        var pendingTags = new ObservableCollection<Tag>(audiobook.Model.Tags.Select(t => new Tag
        {
            Name = t.Name,
            NormalizedName = t.NormalizedName
        }));

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

        // Tags – TokenizingTextBox bound to pendingTags so chips appear immediately
        var tagsBox = new TokenizingTextBox
        {
            PlaceholderText = "Type a tag and press Enter, or use commas",
            TokenDelimiter = ",",
            ItemsSource = pendingTags,
            TextMemberPath = "Name"
        };

        tagsBox.TokenItemAdding += (_, args) =>
        {
            if (args.Item is Tag) return; // programmatic add via ObservableCollection

            var text = args.TokenText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) { args.Cancel = true; return; }

            var normalizedName = NormalizeTagName(text);
            if (string.IsNullOrEmpty(normalizedName)) { args.Cancel = true; return; }

            if (pendingTags.Any(t => t.NormalizedName == normalizedName)) { args.Cancel = true; return; }

            // Reuse the existing DB tag if it matches, otherwise create a new one
            var existing = allTags.FirstOrDefault(t => t.NormalizedName == normalizedName);
            args.Item = existing ?? new Tag { Name = text, NormalizedName = normalizedName };
        };

        // Available-tags picker: one button per existing tag not already on this audiobook
        var sectionLabelStyle = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style;
        var tagsSection = new StackPanel { Spacing = 4 };

        if (allTags.Any())
        {
            tagsSection.Children.Add(new TextBlock
            {
                Text = "Available Tags",
                Margin = new Thickness(0, 0, 0, 2),
                Style = sectionLabelStyle
            });

            // Map NormalizedName → Button so CollectionChanged can toggle visibility
            var tagButtons = new Dictionary<string, Button>();
            var tagStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var tag in allTags.OrderBy(t => t.Name))
            {
                var capturedTag = tag;
                var alreadyAdded = pendingTags.Any(t => t.NormalizedName == tag.NormalizedName);
                var addBtn = new Button
                {
                    Content = "+ " + tag.Name,
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 12,
                    Visibility = alreadyAdded ? Visibility.Collapsed : Visibility.Visible
                };
                addBtn.Click += (btnSender, btnArgs) =>
                {
                    if (!pendingTags.Any(t => t.NormalizedName == capturedTag.NormalizedName))
                        pendingTags.Add(capturedTag);
                };
                tagButtons[tag.NormalizedName] = addBtn;
                tagStrip.Children.Add(addBtn);
            }

            // Keep Available Tags and token box in sync as the user adds/removes tags
            pendingTags.CollectionChanged += (_, args) =>
            {
                if (args.NewItems != null)
                    foreach (Tag added in args.NewItems)
                        if (tagButtons.TryGetValue(added.NormalizedName, out var btn))
                            btn.Visibility = Visibility.Collapsed;

                if (args.OldItems != null)
                    foreach (Tag removed in args.OldItems)
                        if (tagButtons.TryGetValue(removed.NormalizedName, out var btn))
                            btn.Visibility = Visibility.Visible;
            };

            tagsSection.Children.Add(new ScrollViewer
            {
                Content = tagStrip,
                HorizontalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
            });

            tagsSection.Children.Add(new TextBlock
            {
                Text = "Tags",
                Margin = new Thickness(0, 4, 0, 2),
                Style = sectionLabelStyle
            });
        }

        tagsSection.Children.Add(tagsBox);

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

        var panel = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(12),
            MinWidth = 400
        };
        panel.Children.Add(grid);
        panel.Children.Add(tagsSection);
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
            audiobook.Model.Title = SanitizeForSqlite(audiobook.Model.Title);
            audiobook.Model.Author = SanitizeForSqlite(audiobook.Model.Author);
            audiobook.Model.Composer = SanitizeForSqlite(audiobook.Model.Composer);
            audiobook.Model.Description = SanitizeForSqlite(audiobook.Model.Description);

            // Parse any uncommitted free text still in the tags box (comma-separated)
            var remainingText = tagsBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(remainingText))
            {
                foreach (var t in ParseTagsFromText(remainingText))
                {
                    if (!pendingTags.Any(x => x.NormalizedName == t.NormalizedName))
                        pendingTags.Add(t);
                }
            }

            audiobook.Model.Tags = pendingTags.ToList();
            audiobook.IsModified = true;

            await audiobook.SaveAsync();
            await App.Repository.Audiobooks.DeleteOrphanedTagsAsync();
            audiobook.RefreshCoverImage();

            await ViewModel.RefreshTagsForAudiobooksAsync(new[] { audiobook });

            var now = PlayerViewModel.NowPlaying;
            if (now != null && now.Id == audiobook.Id)
            {
                now.Model.Title = audiobook.Model.Title;
                now.Model.Author = audiobook.Model.Author;
            }
        }
    }

    private async void ButtonTile_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        e.Handled = true;

        try
        {
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                // Load the audiobook if it's not already loaded or if it's a different one
                var currentAudiobook = PlayerViewModel.NowPlaying;
                if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
                {
                    await PlayerViewModel.OpenAudiobook(audiobook);
                }

                // Start playing
                PlayerViewModel.MediaPlayer.Play();
            });
        }
        catch (Exception ex)
        {
            ViewModel.LoggingService.LogError(ex, true);
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "Failed to open/play audiobook.",
                Severity = InfoBarSeverity.Error
            });
        }
    }
    private async void PlayAudiobook_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var currentAudiobook = PlayerViewModel.NowPlaying;

            // Load the audiobook if it's not already loaded or if it's a different one
            if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
            {
                await PlayerViewModel.OpenAudiobook(audiobook);
            }

            // Always start playing
            PlayerViewModel.MediaPlayer.Play();
        });
    }

    private async void OpenInMiniPlayer_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var currentAudiobook = PlayerViewModel.NowPlaying;
            if (currentAudiobook == null || currentAudiobook.Id != audiobook.Id)
                await PlayerViewModel.OpenAudiobook(audiobook);

            WindowHelper.ShowMiniPlayer();
        });
    }
}
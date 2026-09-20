// Author: rstewa · https://github.com/rstewa
// Updated: 07/29/2025

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Audibly.App.Extensions;
using Audibly.App.Helpers;
using Audibly.App.ViewModels;
using Audibly.App.Views.ContentDialogs;
using Audibly.App.Views.ControlPages;
using Audibly.Models;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ColorHelper = CommunityToolkit.WinUI.Helpers.ColorHelper;

namespace Audibly.App.Services;

public static class DialogService
{
    private static readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    private static ContentDialog? _progressDialog;

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public static MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    private static PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    private static XamlRoot? GetXamlRoot()
    {
        try
        {
            return App.Window?.Content?.XamlRoot;
        }
        catch (Exception e)
        {
            App.ViewModel.LoggingService.LogError(e, true);
            return null;
        }
    }

    // confirmation dialog with custom title and content
    internal static async Task<ContentDialogResult> ShowConfirmationDialogAsync(string title, string content,
        string primaryButtonText = "Yes", string closeButtonText = "No")
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return ContentDialogResult.None;

        var result = ContentDialogResult.None;
        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var confirmationDialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
                XamlRoot = App.Window.Content.XamlRoot,
                RequestedTheme = ThemeHelper.ActualTheme
            };

            result = await confirmationDialog.ShowOneAtATimeAsync();
        });

        return result;
    }

    internal static async Task ShowErrorDialogAsync(string title, string content)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var errorDialog = new ContentDialog
            {
                Title = title,
                Content = content,
                XamlRoot = App.Window.Content.XamlRoot,
                RequestedTheme = ThemeHelper.ActualTheme
            }.SetPrimaryButton("OK");
            await errorDialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task ShowOkDialogAsync(string title, string content)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                RequestedTheme = ThemeHelper.ActualTheme,
                XamlRoot = App.Window.Content.XamlRoot
            }.SetPrimaryButton("OK");
            await dialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task ShowOnboardingDialogAsync()
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            // show onboarding dialog
            var dialog = new ContentDialog
            {
                Title = "Welcome to Audibly!",
                Content = "We're glad you're here. Let's get started by adding your first audiobook.",
                CloseButtonText = "Ok",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ThemeHelper.ActualTheme,
                XamlRoot = App.Window.Content.XamlRoot
            };
            await dialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task ShowChangelogDialogAsync()
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            // show changelog dialog
            var dialog = new ChangelogContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                RequestedTheme = ThemeHelper.ActualTheme
            };

            await dialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task<ContentDialogResult> ShowSelectFilesDialogAsync()
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return ContentDialogResult.None;

        var result = ContentDialogResult.None;
        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var selectFilesDialog = new SelectFilesDialog();

            var contentDialog = new ContentDialog
            {
                Title = "Put Selected Files in Order (Drag and Drop)",
                Content = selectFilesDialog,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = App.Window.Content.XamlRoot,
                MinWidth = selectFilesDialog.ActualWidth,
                RequestedTheme = ThemeHelper.ActualTheme
            };

            result = await contentDialog.ShowOneAtATimeAsync();
        });

        return result;
    }

    internal static async Task ShowDataMigrationRequiredDialogAsync()
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "Data Migration Required",
                Content =
                    "To ensure compatibility with the latest update, we need to migrate your data to the new database " +
                    "format. This process may take a few minutes depending on the size of your library. Do not close the app " +
                    "during this process.",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonText = "Migrate Data",
                XamlRoot = App.Window.Content.XamlRoot,
                RequestedTheme = ThemeHelper.ActualTheme
            }.SetPrimaryButton("Migrate Data", async (_, _) => await ViewModel.MigrateDatabase());
            await dialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task ShowDataMigrationFailedDialogAsync()
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "Data Migration Failed",
                Content =
                    "We were unable to migrate your data from the previous version of Audibly. Please contact support for assistance.",
                CloseButtonText = "Ok",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ThemeHelper.ActualTheme,
                XamlRoot = App.Window.Content.XamlRoot
            };
            await dialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task<bool> ShowMoreInfoDialogAsync(AudiobookViewModel audiobookViewModel)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return false;

        var editRequested = false;
        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            var moreInfoDialog = new MoreInfoDialogContent(audiobookViewModel);

            var contentDialog = new ContentDialog
            {
                Title = "More Info",
                Content = moreInfoDialog,
                CloseButtonText = "Close",
                XamlRoot = App.Window.Content.XamlRoot,
                RequestedTheme = ThemeHelper.ActualTheme,
                MinWidth = moreInfoDialog.ActualWidth
            };

            moreInfoDialog.SetParentDialog(contentDialog);

            await contentDialog.ShowOneAtATimeAsync();
            editRequested = moreInfoDialog.EditDetailsRequested;
        });

        return editRequested;
    }

    /// <summary>
    ///     Shows the audiobook Edit Info dialog for the given audiobook and, if confirmed, saves the changes.
    ///     Used both to edit an audiobook already in the library and — with <paramref name="isNewImport" /> set —
    ///     to let the user review/adjust freshly-scraped metadata before it is written to the database for the
    ///     first time (AudiobookViewModel.SaveAsync upserts, so this works unchanged for a not-yet-persisted book).
    /// </summary>
    /// <returns>True if the user confirmed (and the audiobook was saved); false if cancelled.</returns>
    internal static async Task<bool> ShowEditAudiobookDialogAsync(AudiobookViewModel audiobook, bool isNewImport = false)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return false;

        ViewModel.SelectedAudiobook = audiobook;

        var allTags = await App.Repository.Audiobooks.GetAllTagsAsync();

        // Working copy – fresh Tag instances so EF tracking on the original list is unaffected
        var pendingTags = new ObservableCollection<Tag>(audiobook.Model.Tags.Select(t => new Tag
        {
            Name = t.Name,
            NormalizedName = t.NormalizedName
        }));

        var coverImg = new Image { Width = 96, Height = 96, Stretch = Stretch.UniformToFill };
        coverImg.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath("SelectedAudiobook.ThumbnailPath")
        });

        var hoverOverlay = new Grid
        {
            Width = 96,
            Height = 96,
            Background = new SolidColorBrush(ColorHelper.ToColor("#8C000000")),
            IsHitTestVisible = false,
            Opacity = 0
        };
        hoverOverlay.Children.Add(new FontIcon
        {
            Glyph = "",
            FontSize = 22,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var coverGrid = new Grid { Width = 96, Height = 96 };
        coverGrid.Children.Add(coverImg);
        coverGrid.Children.Add(hoverOverlay);

        var coverButton = new Button
        {
            Content = coverGrid,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        ToolTipService.SetToolTip(coverButton, "Click to change cover");
        coverButton.PointerEntered += (_, _) => hoverOverlay.Opacity = 1;
        coverButton.PointerExited += (_, _) => hoverOverlay.Opacity = 0;
        coverButton.Click += async (_, _) =>
        {
            var supportedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };
            // Start the picker in the audiobook's source folder — the most likely home for new cover art
            var sourceFolder = Path.GetDirectoryName(audiobook.SourcePaths?.FirstOrDefault()?.FilePath);
            var selectedPath = ViewModel.FileDialogService.OpenFileDialogInFolder(supportedImageTypes, sourceFolder, "Images");
            if (selectedPath == null) return;
            try
            {
                var bytes = await File.ReadAllBytesAsync(selectedPath);
                await ApplyCoverImageAsync(audiobook, bytes);
            }
            catch (Exception ex)
            {
                ViewModel.LoggingService.LogError(ex, true);
                ViewModel.EnqueueNotification(new Notification { Message = "Failed to update cover image.", Severity = InfoBarSeverity.Error });
            }
        };

        var refreshLink = new HyperlinkButton
        {
            Content = "Refresh from folder",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 2, 0, 0),
            FontSize = 12
        };
        refreshLink.Click += async (_, _) => await RefreshCoverFromFolderAsync(audiobook);

        var coverPanel = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        coverPanel.Children.Add(coverButton);
        coverPanel.Children.Add(refreshLink);

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

            var normalizedName = AudiobookEditHelpers.NormalizeTagName(text);
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
            var tagStrip = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 4 };
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

            tagsSection.Children.Add(tagStrip);

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
        Grid.SetColumn(coverPanel, 0);
        grid.Children.Add(coverPanel);
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
            Title = isNewImport ? "Review New Audiobook" : "Edit Info",
            Content = panel,
            PrimaryButtonText = isNewImport ? "Add to Library" : "OK",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.Window.Content.XamlRoot,
            RequestedTheme = ThemeHelper.ActualTheme,
            MinWidth = 420
        };

        var result = await dialog.ShowOneAtATimeAsync();
        if (result != ContentDialogResult.Primary) return false;

        audiobook.Model.Title = AudiobookEditHelpers.SanitizeForSqlite(audiobook.Model.Title);
        audiobook.Model.Author = AudiobookEditHelpers.SanitizeForSqlite(audiobook.Model.Author);
        audiobook.Model.Composer = AudiobookEditHelpers.SanitizeForSqlite(audiobook.Model.Composer);
        audiobook.Model.Description = AudiobookEditHelpers.SanitizeForSqlite(audiobook.Model.Description);

        // Parse any uncommitted free text still in the tags box (comma-separated)
        var remainingText = tagsBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(remainingText))
        {
            foreach (var t in AudiobookEditHelpers.ParseTagsFromText(remainingText))
            {
                if (!pendingTags.Any(x => x.NormalizedName == t.NormalizedName))
                    pendingTags.Add(t);
            }
        }

        audiobook.Model.Tags = pendingTags.ToList();
        audiobook.IsModified = true;

        var saved = await audiobook.SaveAsync();
        if (!saved)
        {
            await ShowErrorDialogAsync("Unable to Save",
                $"Another audiobook with the title \"{audiobook.Model.Title}\" by {audiobook.Model.Author} already exists. " +
                "Change the title or author and try again.");
            return false;
        }

        await App.Repository.Audiobooks.DeleteOrphanedTagsAsync();
        audiobook.RefreshCoverImage();

        await ViewModel.RefreshTagsForAudiobooksAsync(new[] { audiobook });

        var now = PlayerViewModel.NowPlaying;
        if (now != null && now.Id == audiobook.Id)
        {
            now.Model.Title = audiobook.Model.Title;
            now.Model.Author = audiobook.Model.Author;
        }

        return true;
    }

    internal static async Task ApplyCoverImageAsync(AudiobookViewModel audiobook, byte[] imageBytesArray, string? sourceFileName = null)
    {
        // Salt the hash with ticks so the new cover gets a unique path — the WinUI Image
        // control caches bitmaps by URI and will not reload if the path is unchanged.
        var hash = $"{audiobook.Model.Title}{audiobook.Model.Author}{audiobook.Model.Composer}{DateTime.UtcNow.Ticks}".GetSha256Hash();

        if (!string.IsNullOrEmpty(audiobook.Model.CoverImagePath))
            await ViewModel.AppDataService.DeleteCoverImageAsync(audiobook.Model.CoverImagePath);

        var (coverImagePath, thumbnailPath) = await ViewModel.AppDataService.WriteCoverImageAsync(hash, imageBytesArray);

        if (!string.IsNullOrEmpty(coverImagePath))
        {
            audiobook.Model.CoverImagePath = coverImagePath;
            audiobook.Model.ThumbnailPath = thumbnailPath;
            audiobook.IsModified = true;
            await audiobook.SaveAsync();
            audiobook.RefreshCoverImage();

            var now = PlayerViewModel.NowPlaying;
            if (now != null && now.Id == audiobook.Id)
            {
                now.Model.CoverImagePath = coverImagePath;
                now.Model.ThumbnailPath = thumbnailPath;
                now.RefreshCoverImage();
            }

            var message = sourceFileName != null
                ? $"Found file {sourceFileName}."
                : "Cover image updated successfully!";
            ViewModel.EnqueueNotification(new Notification
            {
                Message = message,
                Severity = InfoBarSeverity.Success
            });
        }
        else
        {
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "Failed to update cover image.",
                Severity = InfoBarSeverity.Error
            });
        }
    }

    internal static async Task RefreshCoverFromFolderAsync(AudiobookViewModel audiobook)
    {
        var firstPath = audiobook.SourcePaths?.FirstOrDefault()?.FilePath;
        var folder = firstPath != null ? Path.GetDirectoryName(firstPath) : null;

        var (imageBytes, fileName) = FileImportService.TryGetFolderCoverBytes(folder);
        if (imageBytes == null)
        {
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "No image files found in the audiobook folder.",
                Severity = InfoBarSeverity.Warning
            });
            return;
        }

        try
        {
            await ApplyCoverImageAsync(audiobook, imageBytes, fileName);
        }
        catch (Exception ex)
        {
            ViewModel.LoggingService.LogError(ex, true);
            ViewModel.EnqueueNotification(new Notification
            {
                Message = "An error occurred while refreshing the cover image.",
                Severity = InfoBarSeverity.Error
            });
        }
    }

    internal static async Task ShowProgressDialogAsync(string title, CancellationTokenSource? cts,
        bool showCancelButton = true)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null) return;

        // yes, I'm intentionally not awaiting this
        _dispatcherQueue.EnqueueAsync(async () =>
        {
            _progressDialog = new ProgressContentDialog(cts)
            {
                Title = title,
                RequestedTheme = ThemeHelper.ActualTheme,
                XamlRoot = App.Window.Content.XamlRoot
            };

            if (showCancelButton)
            {
                _progressDialog.DefaultButton = ContentDialogButton.Close;
                _progressDialog.SetCloseButton("Cancel");
            }

            // todo: should i pass cts to ShowOneAtATimeAsync?
            await _progressDialog.ShowOneAtATimeAsync();
        });
    }

    internal static async Task CloseProgressDialogAsync()
    {
        await _dispatcherQueue.EnqueueAsync(() =>
        {
            if (_progressDialog == null) return;
            _progressDialog.Hide();
            _progressDialog = null;
        });
    }
}
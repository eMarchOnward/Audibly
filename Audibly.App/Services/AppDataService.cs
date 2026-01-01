// Author: rstewa · https://github.com/rstewa
// Updated: 06/09/2025

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using ATL;
using Audibly.App.Helpers;
using Audibly.App.Services.Interfaces;
using Audibly.App.ViewModels;
using Audibly.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace Audibly.App.Services;

public class AppDataService : IAppDataService
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private static StorageFolder StorageFolder => ApplicationData.Current.LocalFolder;

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    #region IAppDataService Members

    // Plan (pseudocode):
    // 1. Create or open the per-book appdata folder.
    // 2. If imageBytes is null -> copy packaged default asset as before.
    // 3. If imageBytes is provided:
    //    a. Load the image from a MemoryStream using ImageSharp.
    //    b. Compute square side = min(width, height).
    //    c. Compute center crop rectangle: cropX = (width - side)/2, cropY = (height - side)/2.
    //    d. Mutate the image by cropping with the computed rectangle.
    //    e. Save the cropped image into a MemoryStream as PNG (to preserve transparency).
    //    f. Write resulting bytes to the created `CoverImage.png` StorageFile.
    // 4. Continue to create thumbnail using existing ShrinkAndSaveAsync.
    // 5. Let the existing try/catch handle fallbacks if anything fails.
    public async Task<Tuple<string, string>> WriteCoverImageAsync(string path, byte[]? imageBytes)
    {
        try
        {
            string coverImagePath;
            var bookAppdataDir = await StorageFolder.CreateFolderAsync(path, CreationCollisionOption.OpenIfExists);

            if (imageBytes == null)
            {
                var defaultCover = await AssetHelper.GetAssetFileAsync("DefaultCoverImage.png");
                // Copy default image into the per-book app-data folder
                await defaultCover.CopyAsync(bookAppdataDir, "CoverImage.png", NameCollisionOption.ReplaceExisting);
                // Use the copied file path in app-data, not the asset's path
                coverImagePath = Path.Combine(bookAppdataDir.Path, "CoverImage.png");
            }
            else
            {
                // Create the destination file first
                var coverImage = await bookAppdataDir.CreateFileAsync("CoverImage.png", CreationCollisionOption.ReplaceExisting);

                // Load image from the provided bytes, center-crop to 1:1, then save to the storage file.
                try
                {
                    using var inputStream = new MemoryStream(imageBytes);
                    using var image = await Image.LoadAsync(inputStream);

                    // Determine square side (center crop)
                    var side = Math.Min(image.Width, image.Height);
                    if (image.Width != image.Height)
                    {
                        var cropX = (image.Width - side) / 2;
                        var cropY = (image.Height - side) / 2;
                        var cropRect = new SixLabors.ImageSharp.Rectangle(cropX, cropY, side, side);
                        image.Mutate(ctx => ctx.Crop(cropRect));
                    }

                    // Save cropped image to a MemoryStream as PNG (preserves transparency if present)
                    await using var outStream = new MemoryStream();
                    await image.SaveAsPngAsync(outStream);
                    var outBytes = outStream.ToArray();

                    // Write bytes to StorageFile
                    await FileIO.WriteBytesAsync(coverImage, outBytes);
                    coverImagePath = coverImage.Path;
                }
                catch
                {
                    // If in-memory processing fails for any reason, fall back to writing the original bytes
                    await FileIO.WriteBytesAsync(coverImage, imageBytes);
                    coverImagePath = coverImage.Path;
                }
            }

            // Create 400x400 thumbnail next to the cover
            var thumbnailPath = Path.Combine(bookAppdataDir.Path, "Thumbnail.jpeg");
            var result = await ShrinkAndSaveAsync(coverImagePath, thumbnailPath, 400, 400);
            if (!result)
            {
                // If resizing fails, just reuse the original cover path
                thumbnailPath = coverImagePath;
            }

            return new Tuple<string, string>(coverImagePath, thumbnailPath);
        }
        catch (Exception e)
        {
            App.ViewModel.LoggingService.LogError(e, true);

            // Second-chance fallback: still try to place a local default cover so UI gets a valid file path.
            try
            {
                var bookAppdataDir = await StorageFolder.CreateFolderAsync(path, CreationCollisionOption.OpenIfExists);
                var defaultCover = await AssetHelper.GetAssetFileAsync("DefaultCoverImage.png");
                await defaultCover.CopyAsync(bookAppdataDir, "CoverImage.png", NameCollisionOption.ReplaceExisting);
                var localCover = Path.Combine(bookAppdataDir.Path, "CoverImage.png");
                var localThumb = Path.Combine(bookAppdataDir.Path, "Thumbnail.jpeg");

                // Best-effort thumbnail; ignore failure
                var _ = await ShrinkAndSaveAsync(localCover, localThumb, 400, 400);
                if (!File.Exists(localThumb)) localThumb = localCover;

                return new Tuple<string, string>(localCover, localThumb);
            }
            catch
            {
                // Final fallback: packaged asset (UI should still show this via converter)
                return new Tuple<string, string>(
                    "ms-appx:///Assets/DefaultCoverImage.png",
                    "ms-appx:///Assets/DefaultCoverImage.png"
                );
            }
        }
    }

    public async Task DeleteCoverImageAsync(string path)
    {
        // note: the following code is only needed if I re-enable the .ico creation
        // var dir = Path.GetDirectoryName(path);
        // FolderIcon.ResetFolderAttributes(dir);
        // FolderIcon.DeleteIcon(dir);

        try
        {
            var dirName = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dirName)) return;

            var folder = await StorageFolder.GetFolderFromPathAsync(dirName);

            // Try to delete all files in the folder first
            var files = await folder.GetFilesAsync();
            foreach (var file in files)
                try
                {
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch (Exception fileEx)
                {
                    App.ViewModel.LoggingService.LogError(fileEx);
                }

            // Now try to delete the empty folder
            await folder.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch (Exception e)
        {
            App.ViewModel.LoggingService.LogError(e, true);
        }
    }

    public async Task DeleteCoverImagesAsync(List<string> paths, Func<int, int, string, Task> progressCallback)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            await DeleteCoverImageAsync(paths[i]);
            await progressCallback(i, paths.Count, Path.GetFileName(paths[i]));
        }
    }

    public async Task WriteMetadataAsync(string path, Track track)
    {
        var bookAppdataDir = await StorageFolder.CreateFolderAsync(path,
            CreationCollisionOption.OpenIfExists);
        var json = JsonSerializer.Serialize(track, new JsonSerializerOptions { WriteIndented = true });
        await FileIO.WriteTextAsync(
            await bookAppdataDir.CreateFileAsync("Metadata.json", CreationCollisionOption.ReplaceExisting), json);
    }

    public async Task ExportMetadataAsync(List<SourceFile> sourceFiles)
    {
        try
        {
            // Create a list to hold all track metadata
            var tracks = new List<Track>();

            // Process each source file
            foreach (var sourceFile in sourceFiles)
            {
                var track = new Track(sourceFile.FilePath);
                tracks.Add(track);
            }

            // Serialize the entire collection
            var json = JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true });

            var file = ViewModel.FileDialogService.SaveFileDialog("Metadata.json",
                new List<string> { ".json" }, PickerLocationId.DocumentsLibrary);
            if (file == null) return; // User cancelled
            // Write the JSON to the file
            await FileIO.WriteTextAsync(file, json);

            // show notification
            App.ViewModel.EnqueueNotification(new Notification
            {
                Message = $"Metadata exported to {file.Name}", Severity = InfoBarSeverity.Success
            });


            // Uncomment the following lines if you want to use a dispatcher queue for UI updates

            // await _dispatcherQueue.EnqueueAsync(async () =>
            // {
            //     // Let the user choose where to save the combined metadata file
            //     var savePicker = new FileSavePicker
            //     {
            //         SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            //         SuggestedFileName = "Combined Metadata.json",
            //         FileTypeChoices = { { "JSON", new List<string> { ".json" } } }
            //     };
            //     var file = await savePicker.PickSaveFileAsync();
            //     if (file == null) return; // User cancelled
            //
            //     await FileIO.WriteTextAsync(file, json);
            // });
        }
        catch (Exception e)
        {
            App.ViewModel.LoggingService.LogError(e, true);
        }
    }

    #endregion

    // from: https://stackoverflow.com/questions/26486671/how-to-resize-an-image-maintaining-the-aspect-ratio-in-c-sharp
    private async Task<bool> ShrinkAndSaveAsync(string path, string savePath, int maxHeight, int maxWidth)
    {
        try
        {
            // Ensure destination directory exists
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.OpenRead(path);
            using var image = await Image.LoadAsync(fs);

            // Determine the square crop side (center crop to 1:1 aspect ratio)
            var side = Math.Min(image.Width, image.Height);
            if (image.Width != image.Height)
            {
                var cropX = (image.Width - side) / 2;
                var cropY = (image.Height - side) / 2;
                var cropRect = new SixLabors.ImageSharp.Rectangle(cropX, cropY, side, side);

                image.Mutate(ctx => ctx.Crop(cropRect));
            }

            // After crop, decide if resize is needed to fit within maxWidth/maxHeight
            // Because we enforce 1:1, only one side is relevant.
            var finalSide = side;
            var maxSide = Math.Min(maxWidth, maxHeight);

            if (finalSide > maxSide)
            {
                // Resize down to the constrained square size
                image.Mutate(ctx => ctx.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(maxSide, maxSide),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
                }));
            }

            await image.SaveAsync(savePath);
            return true;
        }
        catch (Exception e)
        {
            App.ViewModel.LoggingService.LogError(e, true);
            return false;
        }
    }

    private bool ResizeNeeded(int height, int width, int maxHeight, int maxWidth, out int newHeight, out int newWidth)
    {
        // first use existing dimensions
        newHeight = height;
        newWidth = width;

        // if below max on both then do nothing
        if (height <= maxHeight && width <= maxWidth) return false;

        // naively check height first
        if (height > maxHeight)
        {
            // set down to max height
            newHeight = maxHeight;

            // calculate what new width would be
            var heightReductionRatio = maxHeight / height; // ratio of maxHeight:image.Height
            newWidth = width * heightReductionRatio; // apply ratio to image.Width
        }

        // does width need to be reduced? 
        // (this will also re-check width after shrinking by height dimension)
        if (newWidth > maxWidth)
        {
            // if so, re-calculate height to fit for maxWidth
            var widthReductionRatio =
                maxWidth / newWidth; // ratio of maxWidth:newWidth (height reduction ratio may have been applied)
            newHeight = maxHeight * widthReductionRatio; // apply new ratio to maxHeight to get final height
            newWidth = maxWidth;
        }

        // if we got here, resize needed and out vars have been set
        return true;
    }
}
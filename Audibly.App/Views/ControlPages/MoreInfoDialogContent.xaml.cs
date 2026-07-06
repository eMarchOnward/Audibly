// Author: rstewa · https://github.com/rstewa
// Created: 10/16/2024
// Updated: 07/05/2026

using System;
using System.IO;
using System.Linq;
using Audibly.App.Extensions;
using Audibly.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Audibly.App.Views.ControlPages;

public sealed partial class MoreInfoDialogContent : Page
{
    public AudiobookViewModel AudiobookViewModel { get; set; }
    public string Description { get; set; }
    public string TagsString { get; set; }
    public Visibility TagsVisibility { get; set; }
    public string DateImportedStr { get; set; }
    public string DateLastPlayedStr { get; set; }
    public string FolderPath { get; set; }
    public Visibility FolderPathVisibility { get; set; }
    public bool EditDetailsRequested { get; private set; }

    private ContentDialog? _parentDialog;

    public void SetParentDialog(ContentDialog dialog) => _parentDialog = dialog;

    public MoreInfoDialogContent(AudiobookViewModel audiobookViewModel)
    {
        AudiobookViewModel = audiobookViewModel;
        Description = audiobookViewModel.Description.FormatText();

        if (audiobookViewModel.Model.Tags != null && audiobookViewModel.Model.Tags.Count > 0)
        {
            TagsString = string.Join(", ", audiobookViewModel.Model.Tags.Select(t => t.Name));
            TagsVisibility = Visibility.Visible;
        }
        else
        {
            TagsString = string.Empty;
            TagsVisibility = Visibility.Collapsed;
        }

        DateImportedStr = audiobookViewModel.Model.DateImported?.ToString("MMM d, yyyy") ?? "Unknown";
        DateLastPlayedStr = audiobookViewModel.Model.DateLastPlayed?.ToString("MMM d, yyyy") ?? "Never";

        var firstSourcePath = audiobookViewModel.SourcePaths?.FirstOrDefault()?.FilePath;
        if (!string.IsNullOrEmpty(firstSourcePath))
        {
            FolderPath = Path.GetDirectoryName(firstSourcePath) ?? string.Empty;
            FolderPathVisibility = string.IsNullOrEmpty(FolderPath) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            FolderPath = string.Empty;
            FolderPathVisibility = Visibility.Collapsed;
        }

        InitializeComponent();
    }

    private void EditDetails_OnClick(object sender, RoutedEventArgs e)
    {
        EditDetailsRequested = true;
        _parentDialog?.Hide();
    }
}

// Author: rstewa · https://github.com/rstewa
// Created: 10/16/2024
// Updated: 10/17/2024

using System;
using System.Linq;
using Audibly.App.Extensions;
using Audibly.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Audibly.App.Views.ControlPages;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MoreInfoDialogContent : Page
{
    public AudiobookViewModel AudiobookViewModel { get; set; }
    public string Description { get; set; }
    public string TagsString { get; set; }
    public Visibility TagsVisibility { get; set; }

    public MoreInfoDialogContent(AudiobookViewModel audiobookViewModel)
    {
        AudiobookViewModel = audiobookViewModel;
        Description = audiobookViewModel.Description.FormatText();
        
        // Format tags as comma-separated string
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
        
        InitializeComponent();
    }
}
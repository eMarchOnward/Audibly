// Author: rstewa · https://github.com/rstewa
// Updated: 07/14/2025

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Audibly.App.Helpers;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.App.Views;
using Audibly.Models;
using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Constants = Audibly.App.Helpers.Constants;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Audibly.App;

/// <summary>
///     The "chrome" layer of the app that provides top-level navigation with
///     proper keyboarding navigation.
/// </summary>
public sealed partial class AppShell : Page
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    public readonly string LibraryLabel = "Library";
    public readonly string NowPlayingLabel = "Now Playing";
    public readonly string TagsLabel = "Tags";

    /// <summary>
    ///     Initializes a new instance of the AppShell, sets the static 'Current' reference,
    ///     adds callbacks for Back requests and changes in the SplitView's DisplayMode, and
    ///     provide the nav menu list with the data to display.
    /// </summary>
    public AppShell()
    {
        InitializeComponent();

        // set the title bar
        var window = WindowHelper.GetMainWindow();
        if (window != null) window.SetTitleBar(AppTitleBar);
        //window.SizeChanged += Window_SizeChanged; // Subscribe to the SizeChanged event
        AppShellFrame.Navigate(typeof(LibraryCardPage));

        Loaded += (_, _) =>
        {
            NavView.SelectedItem = LibraryCardMenuItem;
            NavView.IsPaneOpen = !UserSettings.IsSidebarCollapsed;
        };
        PointerWheelChanged += (_, e) =>
        {
            // wait 1 second before resetting the zoom buttons
            // todo: check if library is the current page
            if (e.KeyModifiers == VirtualKeyModifiers.Control)
            {
                if (e.GetCurrentPoint(this).Properties.MouseWheelDelta > 0)
                    ViewModel.IncreaseAudiobookTileSize();
                else
                    ViewModel.DecreaseAudiobookTileSize();
            }
        };

        NavView.PaneClosed += (_, _) => { UserSettings.IsSidebarCollapsed = true; };
        NavView.PaneOpened += (_, _) => { UserSettings.IsSidebarCollapsed = false; };

        // Subscribe to clear tag selection event
        ViewModel.ClearTagSelection += ViewModelOnClearTagSelection;
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    /// <summary>
    ///     Gets the navigation frame instance.
    /// </summary>
    public Frame AppAppShellFrame => AppShellFrame;

    private async void AppShell_OnLoaded(object sender, RoutedEventArgs e)
    {
        // Check to see if this is the first time the app is being launched
        var hasCompletedOnboarding =
            ApplicationData.Current.LocalSettings.Values.FirstOrDefault(x => x.Key == "HasCompletedOnboarding");
        if (hasCompletedOnboarding.Value == null)
        {
            ApplicationData.Current.LocalSettings.Values["HasCompletedOnboarding"] = true;

            // show onboarding dialog
            // note: content dialog
            await DialogService.ShowOnboardingDialogAsync();

            UserSettings.Version = Constants.Version;
        }
        else
        {
            // check for current version key
            var userCurrentVersion = UserSettings.Version;
            if (userCurrentVersion == null || userCurrentVersion != Constants.Version)
            {
                UserSettings.Version = Constants.Version;

                // show changelog dialog
                // note: content dialog
                await DialogService.ShowChangelogDialogAsync();
            }
        }

        // check for file activation error
        if (ViewModel.FileActivationError != string.Empty)
        {
            // note: content dialog
            await DialogService.ShowErrorDialogAsync("File Activation Error", ViewModel.FileActivationError);
            ViewModel.FileActivationError = string.Empty;
        }
    }

    private void InfoBar_OnClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        // get the notification object
        if (sender.DataContext is not Notification notification) return;
        ViewModel.OnNotificationClosed(notification);
    }

    /// <summary>
    ///     Navigates to the page corresponding to the tapped item.
    /// </summary>
    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem item) return;

        // check if the item is already the current page
        // if (item == (NavigationViewItem)NavView.SelectedItem) return;

        if (item == LibraryCardMenuItem)
        {
            if (AppAppShellFrame.Content is LibraryCardPage) return;
            AppAppShellFrame.Navigate(typeof(LibraryCardPage));
        }
        else if (item == NowPlayingMenuItem)
        {
            // Clear tag filters when navigating to Now Playing view
            ViewModel.SelectedTags.Clear();
            TagsListView?.SelectedItems.Clear();
            
            App.RootFrame?.Navigate(typeof(PlayerPage));
            PlayerViewModel.IsPlayerFullScreen = true;
            PlayerViewModel.MaximizeMinimizeGlyph = Constants.MinimizeGlyph;
        }
        else if (item == (NavigationViewItem)NavView.SettingsItem)
        {
            if (AppAppShellFrame.Content is SettingsPage) return;
            AppAppShellFrame.Navigate(typeof(SettingsPage));
        }
    }

    /// <summary>
    ///     Ensures the nav menu reflects reality when navigation is triggered outside
    ///     the nav menu buttons.
    /// </summary>
    private void OnNavigatingToPage(object sender, NavigatingCancelEventArgs e)
    {
        if (e.NavigationMode == NavigationMode.Back)
        {
            // if (e.SourcePageType == typeof(LibraryPage)) NavView.SelectedItem = AudiobookListMenuItem;
            if (e.SourcePageType == typeof(LibraryCardPage)) NavView.SelectedItem = LibraryCardMenuItem;
            else if (e.SourcePageType == typeof(PlayerPage)) NavView.SelectedItem = NowPlayingMenuItem;
            else if (e.SourcePageType == typeof(SettingsPage)) NavView.SelectedItem = NavView.SettingsItem;
        }
    }

    /// <summary>
    ///     Invoked when the View Code button is clicked. Launches the repo on GitHub.
    /// </summary>
    private async void ViewCodeNavPaneButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://github.com/rstewa/audibly"));
    }

    /// <summary>
    ///     Navigates the frame to the previous page.
    /// </summary>
    private void NavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (AppAppShellFrame.CanGoBack) AppAppShellFrame.GoBack();
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        if (UserSettings.IsSidebarCollapsed || args.DisplayMode == NavigationViewDisplayMode.Minimal)
            VisualStateManager.GoToState(this, "Compact", true);
        else
            VisualStateManager.GoToState(this, "Default", true);
    }

    /// <summary>
    ///     Handles tag selection changes and filters the audiobook list.
    /// </summary>
    private void TagsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView listView) return;

        // Update the SelectedTags collection
        ViewModel.SelectedTags.Clear();
        foreach (Tag tag in listView.SelectedItems)
        {
            ViewModel.SelectedTags.Add(tag);
        }

        // Update visual indicators for selected items
        UpdateTagSelectionIndicators(listView);

        // Notify that selected tags have changed
        ViewModel.NotifySelectedTagsChanged();
    }

    /// <summary>
    ///     Updates the visual selection indicators for all tag items.
    /// </summary>
    private void UpdateTagSelectionIndicators(ListView listView)
    {
        // Iterate through all containers in the ListView
        for (int i = 0; i < listView.Items.Count; i++)
        {
            var container = listView.ContainerFromIndex(i) as ListViewItem;
            if (container == null) continue;

            // Find the SelectionIndicator Border in the DataTemplate
            var grid = FindChild<Grid>(container);
            var indicator = grid?.FindName("SelectionIndicator") as Border;
            
            if (indicator != null)
            {
                // Show indicator if this item is selected
                indicator.Visibility = listView.SelectedItems.Contains(listView.Items[i]) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    ///     Helper method to find a child element of a specific type in the visual tree.
    /// </summary>
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    private async void ClearTagsLink_Tapped(object sender, RoutedEventArgs e)
    {
        // Clear selected tags in the view model
        ViewModel.SelectedTags.Clear();

        // Clear visual selection in the ListView (if it's instantiated)
        TagsListView?.SelectedItems.Clear();

        // Notify listeners that search text should be cleared
        ViewModel.NotifyClearSearchText();

        // Notify listeners (LibraryCardPage listens for this and will refilter)
        ViewModel.NotifySelectedTagsChanged();

        // Optional: explicitly reload the full audiobook list (ensures AvailableTags/data are fresh)
        await ViewModel.GetAudiobookListAsync();
    }

    private void ViewModelOnClearTagSelection()
    {
        // Clear visual selection in the ListView and update indicators
        if (TagsListView != null)
        {
            TagsListView.SelectedItems.Clear();
            UpdateTagSelectionIndicators(TagsListView);
        }
    }
}
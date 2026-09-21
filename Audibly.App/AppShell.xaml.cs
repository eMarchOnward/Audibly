// Author: rstewa · https://github.com/rstewa
// Updated: 07/14/2025

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.UI.Xaml.Controls.Primitives;
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
    public readonly string AppBrandingLabel = $"Audibly eHead {Constants.Version}";

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

        // Collapse the description back down whenever the selected book changes.
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SingleSelectedAudiobook))
                ResetDescriptionExpand();
        };

        // Keep the details panel's scrollable area filling the space between "Now Playing" and the
        // pane footer branding as the window is resized. LayoutUpdated covers every case that can
        // change that space (window resize, pane open/close, selection changing) without needing to
        // enumerate them individually.
        NavView.SizeChanged += (_, _) => UpdateDetailsScrollViewerHeight();
        LayoutUpdated += (_, _) => UpdateDetailsScrollViewerHeight();

        // Keep the details panel's scrollbar hidden until the pointer is actually over the
        // panel, instead of WinUI's default idle-thin-line behavior. Loaded fires even while
        // the ScrollViewer is Visibility="Collapsed", so this wiring happens once, early.
        DetailsScrollViewer.Loaded += (_, _) =>
        {
            DetailsScrollViewer.ApplyTemplate();
            var verticalScrollBar = DetailsScrollViewer.FindDescendant("VerticalScrollBar") as ScrollBar;
            if (verticalScrollBar == null) return;

            verticalScrollBar.Opacity = 0;
            DetailsScrollViewer.PointerEntered += (_, _) => verticalScrollBar.Opacity = 1;
            DetailsScrollViewer.PointerExited += (_, _) => verticalScrollBar.Opacity = 0;
        };
    }

    private double _lastDetailsMaxHeight = -1;

    /// <summary>
    ///     Measures the gap between the details ScrollViewer and the pane footer branding item (both
    ///     relative to NavView) and sets MaxHeight to fill it. Deliberately measures against OTHER
    ///     elements rather than the ScrollViewer's own ActualHeight/ActualWidth — a self-referencing
    ///     size binding on this same element previously made the library's cover art vanish entirely,
    ///     because the bound value can get stuck at 0 before the first layout pass resolves it.
    /// </summary>
    private void UpdateDetailsScrollViewerHeight()
    {
        try
        {
            var scrollViewerTop = DetailsScrollViewer.TransformToVisual(NavView)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var footerTop = PaneFooterRoot.TransformToVisual(NavView)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;

            var available = Math.Max(120, footerTop - scrollViewerTop - 12);

            if (Math.Abs(available - _lastDetailsMaxHeight) > 0.5)
            {
                _lastDetailsMaxHeight = available;
                DetailsScrollViewer.MaxHeight = available;
            }
        }
        catch
        {
            // Layout hasn't settled yet (e.g. the panel is still Collapsed) — keep the current MaxHeight.
        }
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
            ViewModel.ClearSelectedTags();
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

    #region Details panel

    private async void DetailsEditBookButton_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.SingleSelectedAudiobook;
        if (audiobook == null) return;
        await DialogService.ShowEditAudiobookDialogAsync(audiobook);
    }

    private void DetailsShowInFileExplorerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.SingleSelectedAudiobook;
        if (audiobook == null) return;

        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/select, \"{audiobook.CurrentSourceFile.FilePath}\"";
        p.Start();
    }

    private async void DetailsManageTagsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedAudiobooks = ViewModel.Audiobooks.Where(a => a.IsSelected).ToList();
        await DialogService.ShowManageTagsDialogAsync(selectedAudiobooks);
    }

    private async void DetailsDeleteSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Audiobooks.Count(a => a.IsSelected);
        if (await DialogService.ConfirmDeleteAudiobooksAsync(count))
            await ViewModel.DeleteSelectedAudiobooksAsync();
    }

    private bool _isDescriptionExpanded;

    private void DescriptionToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isDescriptionExpanded = !_isDescriptionExpanded;
        DescriptionTextBlock.MaxLines = _isDescriptionExpanded ? 0 : 4;
        DescriptionToggleText.Text = _isDescriptionExpanded ? "Show less" : "Show more";
    }

    private void ResetDescriptionExpand()
    {
        _isDescriptionExpanded = false;
        DescriptionTextBlock.MaxLines = 4;
        DescriptionToggleText.Text = "Show more";
    }

    #endregion
}
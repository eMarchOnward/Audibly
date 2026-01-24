// Author: rstewa · https://github.com/rstewa
// Updated: 05/08/2025

using System;
using Windows.Storage;
using Sentry;

namespace Audibly.App.Helpers;

public static class UserSettings
{
    public static bool ShowDataMigrationFailedDialog
    {
        get
        {
            try
            {
                var dataMigrationFailed = ApplicationData.Current.LocalSettings.Values["DataMigrationFailed"];
                if (dataMigrationFailed != null)
                    if (bool.TryParse(dataMigrationFailed.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["DataMigrationFailed"] = false;
                return false;
            }
            catch (Exception e)
            {
                // log to sentry
                SentrySdk.CaptureException(e);
                return false;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["DataMigrationFailed"] = value;
    }

    public static bool NeedToImportAudiblyExport
    {
        get
        {
            try
            {
                var needToImport = ApplicationData.Current.LocalSettings.Values["NeedToImportAudiblyExportFile"];
                if (needToImport != null)
                    if (bool.TryParse(needToImport.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["NeedToImportAudiblyExportFile"] = false;
                return false;
            }
            catch (Exception e)
            {
                // log to sentry
                SentrySdk.CaptureException(e);
                return false;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["NeedToImportAudiblyExportFile"] = value;
    }

    public static string? Version
    {
        get
        {
            var version = ApplicationData.Current.LocalSettings.Values["CurrentVersion"];
            return version?.ToString();
        }
        set => ApplicationData.Current.LocalSettings.Values["CurrentVersion"] = value;
    }

    public static string? PreviousVersion
    {
        get
        {
            var version = ApplicationData.Current.LocalSettings.Values["PreviousVersion"];
            return version?.ToString();
        }
        set => ApplicationData.Current.LocalSettings.Values["PreviousVersion"] = value;
    }

    public static double ZoomLevel
    {
        get
        {
            try
            {
                var zoomLevel = ApplicationData.Current.LocalSettings.Values["ZoomLevel"];
                if (zoomLevel != null)
                    if (double.TryParse(zoomLevel.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["ZoomLevel"] = 100;
                return 100;
            }
            catch (Exception e)
            {
                // log to sentry
                SentrySdk.CaptureException(e);
                return 100;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["ZoomLevel"] = value;
    }

    public static double Volume
    {
        get
        {
            try
            {
                var volume = ApplicationData.Current.LocalSettings.Values["Volume"];
                if (volume != null)
                    if (double.TryParse(volume.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["Volume"] = 100;
                return 100;
            }
            catch (Exception e)
            {
                // log to sentry
                SentrySdk.CaptureException(e);
                return 100;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["Volume"] = value;
    }

    public static double PlaybackSpeed
    {
        get
        {
            try
            {
                var playbackSpeed = ApplicationData.Current.LocalSettings.Values["PlaybackSpeed"];
                if (playbackSpeed != null)
                    if (double.TryParse(playbackSpeed.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["PlaybackSpeed"] = 1;
                return 1;
            }
            catch (Exception e)
            {
                // log to sentry
                SentrySdk.CaptureException(e);
                return 1;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["PlaybackSpeed"] = value;
    }

    public static bool IsSidebarCollapsed
    {
        get
        {
            try
            {
                var isCollapsed = ApplicationData.Current.LocalSettings.Values["IsSidebarCollapsed"];
                if (isCollapsed != null)
                    if (bool.TryParse(isCollapsed.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["IsSidebarCollapsed"] = false;
                return false;
            }
            catch (Exception e)
            {
                // log to sentry
                SentrySdk.CaptureException(e);
                return false;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["IsSidebarCollapsed"] = value;
    }

    // remembers user's preferred sort mode for the audiobook list
    public static int SortMode
    {
        get
        {
            try
            {
                var sortMode = ApplicationData.Current.LocalSettings.Values["SortMode"];
                if (sortMode != null)
                    if (int.TryParse(sortMode.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["SortMode"] = 0; // default: Alphabetical
                return 0;
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return 0;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["SortMode"] = value;
    }

    public static int WindowWidth
    {
        get
        {
            try
            {
                var windowWidth = ApplicationData.Current.LocalSettings.Values["WindowWidth"];
                if (windowWidth != null)
                    if (int.TryParse(windowWidth.ToString(), out var result))
                        return result;

                return 0; // 0 indicates no saved size, use default
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return 0;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["WindowWidth"] = value;
    }

    public static int WindowHeight
    {
        get
        {
            try
            {
                var windowHeight = ApplicationData.Current.LocalSettings.Values["WindowHeight"];
                if (windowHeight != null)
                    if (int.TryParse(windowHeight.ToString(), out var result))
                        return result;

                return 0; // 0 indicates no saved size, use default
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return 0;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["WindowHeight"] = value;
    }

    public static int WindowX
    {
        get
        {
            try
            {
                var windowX = ApplicationData.Current.LocalSettings.Values["WindowX"];
                if (windowX != null)
                    if (int.TryParse(windowX.ToString(), out var result))
                        return result;

                return int.MinValue; // MinValue indicates no saved position, use default
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return int.MinValue;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["WindowX"] = value;
    }

    public static int WindowY
    {
        get
        {
            try
            {
                var windowY = ApplicationData.Current.LocalSettings.Values["WindowY"];
                if (windowY != null)
                    if (int.TryParse(windowY.ToString(), out var result))
                        return result;

                return int.MinValue; // MinValue indicates no saved position, use default
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return int.MinValue;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["WindowY"] = value;
    }

    public static int SkipBackSeconds
    {
        get
        {
            try
            {
                var skipBackSeconds = ApplicationData.Current.LocalSettings.Values["SkipBackSeconds"];
                if (skipBackSeconds != null)
                    if (int.TryParse(skipBackSeconds.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["SkipBackSeconds"] = 10;
                return 10;
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return 10;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["SkipBackSeconds"] = value;
    }

    public static int SkipForwardSeconds
    {
        get
        {
            try
            {
                var skipForwardSeconds = ApplicationData.Current.LocalSettings.Values["SkipForwardSeconds"];
                if (skipForwardSeconds != null)
                    if (int.TryParse(skipForwardSeconds.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["SkipForwardSeconds"] = 30;
                return 30;
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return 30;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["SkipForwardSeconds"] = value;
    }

    public static bool ScaleSkipWithPlaybackSpeed
    {
        get
        {
            try
            {
                var scaleSkip = ApplicationData.Current.LocalSettings.Values["ScaleSkipWithPlaybackSpeed"];
                if (scaleSkip != null)
                    if (bool.TryParse(scaleSkip.ToString(), out var result))
                        return result;

                ApplicationData.Current.LocalSettings.Values["ScaleSkipWithPlaybackSpeed"] = false;
                return false;
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                return false;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values["ScaleSkipWithPlaybackSpeed"] = value;
    }
}

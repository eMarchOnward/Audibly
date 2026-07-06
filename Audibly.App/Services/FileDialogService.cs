// Author: rstewa · https://github.com/rstewa
// Updated: 06/09/2025

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using Audibly.App.Services.Interfaces;
using WinRT.Interop;
using StorageFile = Windows.Storage.StorageFile;

namespace Audibly.App.Services;

public class FileDialogService : IFileDialogService
{
    #region IFileDialogService Members

    public StorageFile OpenFileDialog(List<string> fileTypes,
        PickerLocationId locationId = PickerLocationId.Desktop)
    {
        var openPicker = new FileOpenPicker();
        var window = App.Window;
        var hWnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(openPicker, hWnd);
        openPicker.SuggestedStartLocation = locationId;
        openPicker.ViewMode = PickerViewMode.Thumbnail;

        foreach (var fileType in fileTypes) openPicker.FileTypeFilter.Add(fileType);

        return openPicker.PickSingleFileAsync().AsTask().GetAwaiter().GetResult();
    }

    public StorageFile SaveFileDialog(string defaultFileName, List<string> fileTypes,
        PickerLocationId locationId = PickerLocationId.Desktop)
    {
        var savePicker = new FileSavePicker();
        var window = App.Window;
        var hWnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(savePicker, hWnd);
        savePicker.SuggestedStartLocation = locationId;
        savePicker.SuggestedFileName = defaultFileName;

        foreach (var fileType in fileTypes) savePicker.FileTypeChoices.Add(fileType, new List<string> { fileType });

        return savePicker.PickSaveFileAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Opens a file-picker dialog starting in a specific directory. The WinRT FileOpenPicker
    ///     only supports library locations, so this uses the Win32 IFileOpenDialog directly.
    ///     Returns the selected file path, or null if the user cancelled.
    /// </summary>
    public string? OpenFileDialogInFolder(List<string> fileTypes, string? initialDirectory,
        string filterName = "Supported files")
    {
        var hWnd = WindowNative.GetWindowHandle(App.Window);

        var dialog = (IFileDialog)new FileOpenDialogRCW();

        var spec = string.Join(";", fileTypes.Select(t => "*" + t));
        dialog.SetFileTypes(1, new[] { new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = spec } });

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iid, out var folderItem) == 0)
                dialog.SetFolder(folderItem);
        }

        if (dialog.Show(hWnd) != 0) return null; // cancelled (or error)

        dialog.GetResult(out var item);
        item.GetDisplayName(SIGDN_FILESYSPATH, out var pszPath);
        var path = Marshal.PtrToStringUni(pszPath);
        Marshal.FreeCoTaskMem(pszPath);
        return path;
    }

    #endregion

    #region Win32 IFileOpenDialog interop

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        out IShellItem ppv);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW
    {
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr hwndOwner);

        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint fos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    #endregion
}

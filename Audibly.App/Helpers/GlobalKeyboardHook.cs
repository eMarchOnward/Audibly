using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

namespace Audibly.App.Helpers;

/// <summary>
/// Lightweight global keyboard hook that invokes callbacks when Ctrl+{Up,Down,Left,Right,Space} are pressed.
/// Install by creating an instance and wire the public actions. Dispose to uninstall.
/// Uses a low-level keyboard hook (WH_KEYBOARD_LL).
/// </summary>
internal sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    // Virtual key codes
    private const int VK_UP = 0x26;
    private const int VK_DOWN = 0x28;
    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;
    private const int VK_SPACE = 0x20;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_CONTROL = 0x11;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    // Callbacks the consumer can set
    public Action? OnCtrlUp { get; set; }
    public Action? OnCtrlDown { get; set; }
    public Action? OnCtrlLeft { get; set; }
    public Action? OnCtrlRight { get; set; }
    public Action? OnCtrlSpace { get; set; }

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
        _hookId = SetHook(_proc);
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        var curProcess = Process.GetCurrentProcess();
        var curModule = curProcess.MainModule;
        var moduleName = curModule?.ModuleName ?? string.Empty;
        var moduleHandle = GetModuleHandle(moduleName);
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vk = kb.vkCode;

                var ctrlDown = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0
                               || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0
                               || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                if (ctrlDown)
                {
                    bool handled = false;

                    switch (vk)
                    {
                        case VK_UP:
                            OnCtrlUp?.Invoke();
                            handled = OnCtrlUp != null;
                            break;
                        case VK_DOWN:
                            OnCtrlDown?.Invoke();
                            handled = OnCtrlDown != null;
                            break;
                        case VK_LEFT:
                            OnCtrlLeft?.Invoke();
                            handled = OnCtrlLeft != null;
                            break;
                        case VK_RIGHT:
                            OnCtrlRight?.Invoke();
                            handled = OnCtrlRight != null;
                            break;
                        case VK_SPACE:
                            OnCtrlSpace?.Invoke();
                            handled = OnCtrlSpace != null;
                            break;
                    }

                    if (handled)
                    {
                        // Return a non-zero value to prevent propagation to other apps/windows.
                        return (IntPtr)1;
                    }
                }
            }
        }
        catch
        {
            // Swallow exceptions to avoid breaking the hook chain.
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    #region Native

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    #endregion
}
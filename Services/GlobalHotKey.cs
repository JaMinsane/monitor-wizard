using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace MonitorWizard.Services;

public sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 1200;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkF12 = 0x7B;
    private const uint WmHotKey = 0x0312;

    private readonly Window _window;
    private readonly Action _pressed;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private IntPtr _windowHandle;
    private bool _isRegistered;

    public GlobalHotKey(Window window, Action pressed)
    {
        _window = window;
        _pressed = pressed;
        _subclassProc = WndProc;
    }

    public bool Register()
    {
        _windowHandle = WindowNative.GetWindowHandle(_window);

        if (_windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!NativeMethods.SetWindowSubclass(_windowHandle, _subclassProc, UIntPtr.Zero, UIntPtr.Zero))
        {
            return false;
        }

        _isRegistered = NativeMethods.RegisterHotKey(_windowHandle, HotKeyId, ModControl | ModAlt, VkF12);

        if (!_isRegistered)
        {
            NativeMethods.RemoveWindowSubclass(_windowHandle, _subclassProc, UIntPtr.Zero);
        }

        return _isRegistered;
    }

    public void Dispose()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (_isRegistered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotKeyId);
        }

        NativeMethods.RemoveWindowSubclass(_windowHandle, _subclassProc, UIntPtr.Zero);
        _isRegistered = false;
        _windowHandle = IntPtr.Zero;
    }

    private IntPtr WndProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        if (message == WmHotKey && wParam == (UIntPtr)HotKeyId)
        {
            _pressed();
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private static partial class NativeMethods
    {
        internal delegate IntPtr SubclassProc(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr refData);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowSubclass(
            IntPtr windowHandle,
            SubclassProc subclassProc,
            UIntPtr subclassId,
            UIntPtr refData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveWindowSubclass(
            IntPtr windowHandle,
            SubclassProc subclassProc,
            UIntPtr subclassId);

        [DllImport("comctl32.dll")]
        internal static extern IntPtr DefSubclassProc(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);
    }
}

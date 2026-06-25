using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Text_Grab
{
    public class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int HOTKEY_ID_START = 9000;
        private const int HOTKEY_ID_STOP = 9001;
        private const int HOTKEY_ID_COPY = 9002;

        private const int WM_HOTKEY = 0x0312;

        private HwndSource? hwndSource;
        private readonly IntPtr windowHandle;

        public event Action? OnStartGrab;
        public event Action? OnFinalizePipeline;
        public event Action? OnCopySelected;

        public HotkeyManager(IntPtr windowHandle)
        {
            this.windowHandle = windowHandle;
        }

        public void RegisterHotkeys(AppSettings settings)
        {
            // Register Start Grab (e.g. Win + S)
            bool startRegistered = RegisterHotKey(windowHandle, HOTKEY_ID_START, settings.StartGrabModifiers | MOD_NOREPEAT, settings.StartGrabKey);
            if (!startRegistered)
            {
                int errorCode = Marshal.GetLastWin32Error();
                System.Windows.MessageBox.Show(
                    $"Failed to register Start Grab hotkey (Error Code: {errorCode}). This hotkey may be in use by another application.",
                    "Hotkey Registration Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            // Register Finalize Pipeline (e.g. Win + Shift + S)
            bool stopRegistered = RegisterHotKey(windowHandle, HOTKEY_ID_STOP, settings.ProcessBatchModifiers | MOD_NOREPEAT, settings.ProcessBatchKey);
            if (!stopRegistered)
            {
                int errorCode = Marshal.GetLastWin32Error();
                System.Windows.MessageBox.Show(
                    $"Failed to register Finalize Pipeline hotkey (Error Code: {errorCode}). This hotkey may be in use by another application.",
                    "Hotkey Registration Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            // Register Copy Selected (e.g. Win + D)
            bool copyRegistered = RegisterHotKey(windowHandle, HOTKEY_ID_COPY, settings.CopySelectedModifiers | MOD_NOREPEAT, settings.CopySelectedKey);
            if (!copyRegistered)
            {
                int errorCode = Marshal.GetLastWin32Error();
                System.Windows.MessageBox.Show(
                    $"Failed to register Copy Selected hotkey (Error Code: {errorCode}). This shortcut is likely reserved by the system shell (Show Desktop).\n\nIf it does not trigger, please check if your system permits overriding this key.",
                    "Hotkey Registration Warning",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            if (hwndSource == null)
            {
                hwndSource = HwndSource.FromHwnd(windowHandle);
                hwndSource?.AddHook(HwndHook);
            }
        }

        public void UpdateHotkeys(AppSettings settings)
        {
            UnregisterAll();
            RegisterHotkeys(settings);
        }

        private void UnregisterAll()
        {
            UnregisterHotKey(windowHandle, HOTKEY_ID_START);
            UnregisterHotKey(windowHandle, HOTKEY_ID_STOP);
            UnregisterHotKey(windowHandle, HOTKEY_ID_COPY);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_START)
                {
                    OnStartGrab?.Invoke();
                    handled = true;
                }
                else if (id == HOTKEY_ID_STOP)
                {
                    OnFinalizePipeline?.Invoke();
                    handled = true;
                }
                else if (id == HOTKEY_ID_COPY)
                {
                    OnCopySelected?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(HwndHook);
                hwndSource = null;
            }

            UnregisterAll();
            GC.SuppressFinalize(this);
        }
    }
}

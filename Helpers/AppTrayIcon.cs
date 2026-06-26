using System;
using System.Drawing;
using System.Windows.Forms;

namespace SnapAtom
{
    public class AppTrayIcon : IDisposable
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private readonly NotifyIcon notifyIcon;
        private Icon? customIcon;
        private IntPtr hIcon = IntPtr.Zero;

        public event Action? OnCaptureRegion;
        public event Action? OnCopySelected;
        public event Action? OnProcessBatch;
        public event Action? OnOpenClippingsFolder;
        public event Action? OnOpenSettings;
        public event Action? OnExit;

        public AppTrayIcon()
        {
            notifyIcon = new NotifyIcon();

            // Try to load tray icon from custom ICO
            try
            {
                string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    customIcon = new Icon(icoPath);
                    notifyIcon.Icon = customIcon;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load tray icon from ICO: {ex.Message}");
            }

            // Fallback to custom PNG if ICO failed or doesn't exist
            if (notifyIcon.Icon == null)
            {
                try
                {
                    string pngPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.png");
                    if (System.IO.File.Exists(pngPath))
                    {
                        using (Bitmap bmp = new Bitmap(pngPath))
                        {
                            hIcon = bmp.GetHicon();
                            notifyIcon.Icon = Icon.FromHandle(hIcon);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load tray icon from PNG: {ex.Message}");
                }
            }

            // Fallback if loading custom icon files failed
            if (notifyIcon.Icon == null)
            {
                try
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                    {
                        notifyIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            if (notifyIcon.Icon == null)
            {
                notifyIcon.Icon = SystemIcons.Application;
            }

            notifyIcon.Text = "SnapAtom OCR Pipeline";
            notifyIcon.Visible = true;

            // Double click tray icon opens settings
            notifyIcon.DoubleClick += (s, e) => OnOpenSettings?.Invoke();

            CreateContextMenu();
        }

        private void CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            var captureItem = new ToolStripMenuItem("Capture Region");
            captureItem.Click += (s, e) => OnCaptureRegion?.Invoke();
            menu.Items.Add(captureItem);

            var copyItem = new ToolStripMenuItem("Copy Selected Text");
            copyItem.Click += (s, e) => OnCopySelected?.Invoke();
            menu.Items.Add(copyItem);

            var processItem = new ToolStripMenuItem("Process Clippings Batch");
            processItem.Click += (s, e) => OnProcessBatch?.Invoke();
            menu.Items.Add(processItem);

            var openFolderItem = new ToolStripMenuItem("Open Clippings Folder");
            openFolderItem.Click += (s, e) => OnOpenClippingsFolder?.Invoke();
            menu.Items.Add(openFolderItem);

            menu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => OnOpenSettings?.Invoke();
            menu.Items.Add(settingsItem);

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => OnExit?.Invoke();
            menu.Items.Add(exitItem);

            notifyIcon.ContextMenuStrip = menu;
        }

        public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3000)
        {
            try
            {
                notifyIcon.ShowBalloonTip(timeoutMs, title, text, icon);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show balloon tip: {ex.Message}");
            }
        }

        public void UpdateTooltip(string text)
        {
            notifyIcon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text;
        }

        public void Dispose()
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            if (customIcon != null)
            {
                customIcon.Dispose();
                customIcon = null;
            }

            if (hIcon != IntPtr.Zero)
            {
                DestroyIcon(hIcon);
                hIcon = IntPtr.Zero;
            }
        }
    }
}

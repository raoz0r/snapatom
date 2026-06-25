using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace Text_Grab
{
    public partial class MainWindow : Window
    {
        private HotkeyManager? hotkeyManager;
        private AppSettings settings = null!;
        private AppTrayIcon? trayIcon;
        private bool isProcessing = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // Ensure window is invisible on creation
            this.Width = 0;
            this.Height = 0;
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Collapsed;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Load Settings
            settings = AppSettings.Load();

            // Initialize Tray Icon
            trayIcon = new AppTrayIcon();
            trayIcon.OnCaptureRegion += HandleStartGrab;
            trayIcon.OnCopySelected += HandleCopySelected;
            trayIcon.OnProcessBatch += HandleFinalizePipeline;
            trayIcon.OnOpenClippingsFolder += HandleOpenClippingsFolder;
            trayIcon.OnOpenSettings += HandleOpenSettings;
            trayIcon.OnExit += HandleExit;

            // Now that the window source is initialized, we have a valid handle
            IntPtr handle = new WindowInteropHelper(this).Handle;
            
            hotkeyManager = new HotkeyManager(handle);
            hotkeyManager.OnStartGrab += HandleStartGrab;
            hotkeyManager.OnFinalizePipeline += HandleFinalizePipeline;
            hotkeyManager.OnCopySelected += HandleCopySelected;
            
            hotkeyManager.RegisterHotkeys(settings);
            
            System.Diagnostics.Debug.WriteLine("OCR Pipeline background service started.");
        }

        private void HandleStartGrab()
        {
            System.Diagnostics.Debug.WriteLine("Hotkey Win + S (or custom) pressed. Launching selection overlay...");
            
            // Create and show selection overlay
            SelectionWindow selectionWindow = new SelectionWindow();
            selectionWindow.Show();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_C = 0x43;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private async void HandleCopySelected()
        {
            System.Diagnostics.Debug.WriteLine("Hotkey Win + D (or custom) pressed. Copying selected text...");

            // Wait for user to release the Windows modifier keys so they don't interfere with Ctrl+C
            while ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0)
            {
                await System.Threading.Tasks.Task.Delay(50);
            }

            // 1. Backup existing clipboard content
            IDataObject? clipboardBackup = null;
            try
            {
                clipboardBackup = Clipboard.GetDataObject();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to backup clipboard: {ex.Message}");
            }

            // 2. Clear clipboard to ensure fresh read
            try
            {
                Clipboard.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear clipboard: {ex.Message}");
            }

            // 3. Simulate Ctrl+C
            keybd_event(VK_CONTROL, 0, 0, 0); // Ctrl Down
            keybd_event(VK_C, 0, 0, 0);       // C Down
            System.Threading.Thread.Sleep(10); // Small pause
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, 0); // C Up
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0); // Ctrl Up

            // 4. Poll clipboard for text (up to 200ms)
            string selectedText = string.Empty;
            for (int i = 0; i < 10; i++)
            {
                await System.Threading.Tasks.Task.Delay(20);
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        selectedText = Clipboard.GetText();
                        break;
                    }
                }
                catch
                {
                    // Clipboard might be locked by another process temporarily
                }
            }

            // 5. Restore clipboard backup
            if (clipboardBackup != null)
            {
                try
                {
                    Clipboard.SetDataObject(clipboardBackup);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to restore clipboard backup: {ex.Message}");
                }
            }

            // 6. Write text to temp.json
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                TempJsonWriter.AppendEntry(selectedText, "table");
                System.Diagnostics.Debug.WriteLine($"Successfully appended selected text to JSON: {selectedText}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No selected text detected (or copy failed).");
            }
        }

        private async void HandleFinalizePipeline()
        {
            if (isProcessing)
            {
                trayIcon?.ShowNotification("Pipeline Busy", "Clippings processing batch is already running.", System.Windows.Forms.ToolTipIcon.Warning);
                return;
            }

            string filePath = TempJsonWriter.GetFilePath();
            if (!File.Exists(filePath))
            {
                trayIcon?.ShowNotification("Batch Empty", "No captures recorded in the current batch (temp.json does not exist).", System.Windows.Forms.ToolTipIcon.Warning);
                return;
            }

            isProcessing = true;
            trayIcon?.ShowNotification("Processing Started", "Gemini OCR clippings batch processing started in background...", System.Windows.Forms.ToolTipIcon.Info);

            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string typeArg = "";
                if (args.Length > 1)
                {
                    typeArg = args[1];
                }

                await System.Threading.Tasks.Task.Run(async () =>
                {
                    await PipelineProcessor.ProcessBatchAsync(settings, typeArg);
                });

                trayIcon?.ShowNotification("Processing Finished", "OCR clippings batch processed successfully!", System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                trayIcon?.ShowNotification("Processing Failed", $"Error processing clippings: {ex.Message}", System.Windows.Forms.ToolTipIcon.Error);
                MessageBox.Show($"Error running C# pipeline: {ex.Message}", "Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isProcessing = false;
            }
        }

        private void HandleOpenClippingsFolder()
        {
            try
            {
                if (Directory.Exists(settings.ClippingsSavePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = settings.ClippingsSavePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    trayIcon?.ShowNotification("Folder Not Found", $"Output directory does not exist yet: {settings.ClippingsSavePath}", System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open clippings folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleOpenSettings()
        {
            // Open settings window as dialog
            SettingsWindow settingsWindow = new SettingsWindow(settings);
            settingsWindow.OnSettingsSaved += (updatedSettings) =>
            {
                this.settings = updatedSettings;
                if (hotkeyManager != null)
                {
                    hotkeyManager.UpdateHotkeys(this.settings);
                }
                trayIcon?.ShowNotification("Settings Saved", "Application settings updated and hotkeys re-registered.", System.Windows.Forms.ToolTipIcon.Info);
            };
            settingsWindow.ShowDialog();
        }

        private void HandleExit()
        {
            this.Close();
            Application.Current.Shutdown();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (hotkeyManager != null)
            {
                hotkeyManager.Dispose();
                hotkeyManager = null;
            }

            if (trayIcon != null)
            {
                trayIcon.Dispose();
                trayIcon = null;
            }
        }
    }
}
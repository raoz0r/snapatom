using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapAtom
{
    public partial class SettingsWindow : Window
    {
        private AppSettings settings;
        private ObservableCollection<MetadataItem> customMetadataItems = null!;
        public event Action<AppSettings>? OnSettingsSaved;

        // Modifiers flags
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        // Virtual Key Codes Mapping
        private static readonly Dictionary<string, uint> KeyMap = new Dictionary<string, uint>
        {
            { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
            { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
            { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
            { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
            { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
            { "Z", 0x5A },
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
            { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
            { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 }, { "F5", 0x74 },
            { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 }, { "F9", 0x78 }, { "F10", 0x79 },
            { "F11", 0x7A }, { "F12", 0x7B }, { "Space", 0x20 }
        };

        private static readonly string[] Providers = new[]
        {
            "Google AI Studio",
            "OpenAI",
            "Anthropic",
            "Custom OpenAI-Compatible"
        };

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            this.settings = settings;

            PopulateCombos();
            LoadSettingsIntoUi();
            LoadLastSyncLogs();
        }

        private void PopulateCombos()
        {
            var keyNames = KeyMap.Keys.ToList();
            GrabKeyCombo.ItemsSource = keyNames;
            CopyKeyCombo.ItemsSource = keyNames;
            BatchKeyCombo.ItemsSource = keyNames;

            ProviderCombo.ItemsSource = Providers;
        }

        private void LoadSettingsIntoUi()
        {
            SavePathTextBox.Text = settings.ClippingsSavePath;

            // Load custom metadata items
            customMetadataItems = new ObservableCollection<MetadataItem>();
            if (settings.CustomMetadata != null)
            {
                foreach (var item in settings.CustomMetadata)
                {
                    customMetadataItems.Add(new MetadataItem { Key = item.Key, Value = item.Value });
                }
            }
            MetadataItemsControl.ItemsSource = customMetadataItems;

            // Load AI configuration
            ProviderCombo.SelectedItem = settings.AiProvider;
            ApiKeyTextBox.Text = settings.AiApiKey;
            ModelTextBox.Text = settings.AiModelName;
            CustomEndpointTextBox.Text = settings.CustomEndpoint;

            // Show/Hide custom endpoint depending on selection
            UpdateCustomEndpointVisibility();

            // Load Start Grab hotkey
            GrabWinCb.IsChecked = (settings.StartGrabModifiers & MOD_WIN) != 0;
            GrabCtrlCb.IsChecked = (settings.StartGrabModifiers & MOD_CONTROL) != 0;
            GrabShiftCb.IsChecked = (settings.StartGrabModifiers & MOD_SHIFT) != 0;
            GrabAltCb.IsChecked = (settings.StartGrabModifiers & MOD_ALT) != 0;
            GrabKeyCombo.SelectedItem = GetKeyName(settings.StartGrabKey);

            // Load Copy Selected hotkey
            CopyWinCb.IsChecked = (settings.CopySelectedModifiers & MOD_WIN) != 0;
            CopyCtrlCb.IsChecked = (settings.CopySelectedModifiers & MOD_CONTROL) != 0;
            CopyShiftCb.IsChecked = (settings.CopySelectedModifiers & MOD_SHIFT) != 0;
            CopyAltCb.IsChecked = (settings.CopySelectedModifiers & MOD_ALT) != 0;
            CopyKeyCombo.SelectedItem = GetKeyName(settings.CopySelectedKey);

            // Load Process Batch hotkey
            BatchWinCb.IsChecked = (settings.ProcessBatchModifiers & MOD_WIN) != 0;
            BatchCtrlCb.IsChecked = (settings.ProcessBatchModifiers & MOD_CONTROL) != 0;
            BatchShiftCb.IsChecked = (settings.ProcessBatchModifiers & MOD_SHIFT) != 0;
            BatchAltCb.IsChecked = (settings.ProcessBatchModifiers & MOD_ALT) != 0;
            BatchKeyCombo.SelectedItem = GetKeyName(settings.ProcessBatchKey);

            // Load system prompt from prompt.md
            try
            {
                string promptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt.md");
                if (File.Exists(promptPath))
                {
                    PromptTextBox.Text = File.ReadAllText(promptPath);
                }
                else
                {
                    PromptTextBox.Text = "";
                }
            }
            catch (Exception ex)
            {
                PromptTextBox.Text = $"Error loading prompt.md: {ex.Message}";
            }
        }

        private string GetKeyName(uint vkCode)
        {
            foreach (var kvp in KeyMap)
            {
                if (kvp.Value == vkCode)
                    return kvp.Key;
            }
            return "S"; // Fallback
        }

        private uint GetVkCode(string? keyName, uint defaultVal)
        {
            if (keyName != null && KeyMap.TryGetValue(keyName, out uint vk))
            {
                return vk;
            }
            return defaultVal;
        }

        private uint GetModifierValue(CheckBox win, CheckBox ctrl, CheckBox shift, CheckBox alt)
        {
            uint modifiers = 0;
            if (win.IsChecked == true) modifiers |= MOD_WIN;
            if (ctrl.IsChecked == true) modifiers |= MOD_CONTROL;
            if (shift.IsChecked == true) modifiers |= MOD_SHIFT;
            if (alt.IsChecked == true) modifiers |= MOD_ALT;
            return modifiers;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select Clippings Output Folder";
                dialog.UseDescriptionForTitle = true;
                
                string currentPath = SavePathTextBox.Text;
                if (Directory.Exists(currentPath))
                {
                    dialog.InitialDirectory = currentPath;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SavePathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void AddMetadataButton_Click(object sender, RoutedEventArgs e)
        {
            customMetadataItems.Add(new MetadataItem { Key = "", Value = "" });
        }

        private void DeleteMetadataButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MetadataItem item)
            {
                customMetadataItems.Remove(item);
            }
        }

        private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCustomEndpointVisibility();

            // Populate default model if empty or matches standard placeholders of other providers
            string? selectedProvider = ProviderCombo.SelectedItem as string;
            if (ModelTextBox != null && selectedProvider != null)
            {
                string currentModel = ModelTextBox.Text.Trim();
                if (selectedProvider == "Google AI Studio" && (string.IsNullOrEmpty(currentModel) || currentModel == "gpt-4o" || currentModel == "claude-3-5-sonnet-latest"))
                {
                    ModelTextBox.Text = "gemini-2.5-flash";
                }
                else if (selectedProvider == "OpenAI" && (string.IsNullOrEmpty(currentModel) || currentModel == "gemini-2.5-flash" || currentModel == "claude-3-5-sonnet-latest"))
                {
                    ModelTextBox.Text = "gpt-4o";
                }
                else if (selectedProvider == "Anthropic" && (string.IsNullOrEmpty(currentModel) || currentModel == "gemini-2.5-flash" || currentModel == "gpt-4o"))
                {
                    ModelTextBox.Text = "claude-3-5-sonnet-latest";
                }
            }
        }

        private void UpdateCustomEndpointVisibility()
        {
            if (CustomEndpointPanel == null) return;

            string? selectedProvider = ProviderCombo.SelectedItem as string;
            if (selectedProvider == "Custom OpenAI-Compatible")
            {
                CustomEndpointPanel.Visibility = Visibility.Visible;
            }
            else
            {
                CustomEndpointPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
        {
            string provider = (ProviderCombo.SelectedItem as string) ?? "Google AI Studio";
            string key = ApiKeyTextBox.Text.Trim();
            string model = ModelTextBox.Text.Trim();
            string endpoint = CustomEndpointTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(key) && provider != "Custom OpenAI-Compatible")
            {
                TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                TestStatusText.Text = "API key cannot be empty.";
                return;
            }

            TestConnectionBtn.IsEnabled = false;
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)); // zinc
            TestStatusText.Text = $"Testing connection to {provider}...";

            try
            {
                bool success = await AiClient.TestConnectionAsync(provider, key, model, endpoint);
                if (success)
                {
                    TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // green
                    TestStatusText.Text = "Connection successful! Configuration is valid.";
                }
                else
                {
                    TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                    TestStatusText.Text = "Connection failed. Please verify API key, model name, and endpoint.";
                }
            }
            catch (Exception ex)
            {
                TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                TestStatusText.Text = $"Connection error: {ex.Message}";
            }
            finally
            {
                TestConnectionBtn.IsEnabled = true;
            }
        }

        private void LoadLastSyncLogs()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "logs");
                string logPath = Path.Combine(logDir, "process_clippings.log");

                if (!File.Exists(logPath))
                {
                    LogsTextBox.Text = "No sync logs found yet.";
                    return;
                }

                // Read all lines
                string[] lines = File.ReadAllLines(logPath);
                int lastSyncIndex = -1;

                // Find the start of the last sync run
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].Contains("Starting clippings processing pipeline"))
                    {
                        lastSyncIndex = i;
                        break;
                    }
                }

                if (lastSyncIndex >= 0)
                {
                    var lastSyncLines = lines.Skip(lastSyncIndex);
                    LogsTextBox.Text = string.Join(Environment.NewLine, lastSyncLines);
                }
                else
                {
                    // Fallback to showing the last 100 lines
                    var lastLines = lines.Skip(Math.Max(0, lines.Length - 100));
                    LogsTextBox.Text = string.Join(Environment.NewLine, lastLines);
                }

                LogsTextBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                LogsTextBox.Text = $"Error reading log file: {ex.Message}";
            }
        }

        private void RefreshLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadLastSyncLogs();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Simple validation
            if (string.IsNullOrWhiteSpace(SavePathTextBox.Text))
            {
                MessageBox.Show("Please select or enter a valid clippings save path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save text prompt
            try
            {
                string promptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt.md");
                File.WriteAllText(promptPath, PromptTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save prompt.md: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save settings
            settings.ClippingsSavePath = SavePathTextBox.Text.Trim();
            settings.AiProvider = (ProviderCombo.SelectedItem as string) ?? "Google AI Studio";
            settings.AiApiKey = ApiKeyTextBox.Text.Trim();
            settings.AiModelName = ModelTextBox.Text.Trim();
            settings.CustomEndpoint = CustomEndpointTextBox.Text.Trim();

            // Save custom metadata
            settings.CustomMetadata = customMetadataItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .ToList();

            // Save Start Grab hotkey
            settings.StartGrabModifiers = GetModifierValue(GrabWinCb, GrabCtrlCb, GrabShiftCb, GrabAltCb);
            settings.StartGrabKey = GetVkCode(GrabKeyCombo.SelectedItem as string, 0x53);

            // Save Copy Selected hotkey
            settings.CopySelectedModifiers = GetModifierValue(CopyWinCb, CopyCtrlCb, CopyShiftCb, CopyAltCb);
            settings.CopySelectedKey = GetVkCode(CopyKeyCombo.SelectedItem as string, 0x44);

            // Save Process Batch hotkey
            settings.ProcessBatchModifiers = GetModifierValue(BatchWinCb, BatchCtrlCb, BatchShiftCb, BatchAltCb);
            settings.ProcessBatchKey = GetVkCode(BatchKeyCombo.SelectedItem as string, 0x53);

            // Save to file
            settings.Save();

            // Notify main window
            OnSettingsSaved?.Invoke(settings);

            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

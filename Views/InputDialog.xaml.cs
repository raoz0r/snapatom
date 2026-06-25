using System.Windows;

namespace Text_Grab
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;
        public bool IsCancelled { get; private set; } = true;

        public InputDialog(string defaultText = "")
        {
            InitializeComponent();
            InputTextBox.Text = defaultText;
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void Sync_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text.Trim();
            IsCancelled = false;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsCancelled = true;
            this.Close();
        }
    }
}

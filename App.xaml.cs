using System;
using System.Windows;
using System.Windows.Interop;

namespace Text_Grab
{
    public partial class App : Application
    {
        private MainWindow? mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Instantiate MainWindow but do NOT show it
            mainWindow = new MainWindow();

            // Force handle creation so SourceInitialized and Hook registration occur
            WindowInteropHelper helper = new WindowInteropHelper(mainWindow);
            helper.EnsureHandle();
        }
    }
}


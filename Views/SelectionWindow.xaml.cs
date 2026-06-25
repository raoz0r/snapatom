using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace SnapAtom
{
    public partial class SelectionWindow : Window
    {
        private Point startPoint;
        private bool isDragging;

        public SelectionWindow()
        {
            InitializeComponent();
            
            // Cover the entire virtual screen (multi-monitor setup)
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
            // Force focus to the canvas so key events like Escape are registered immediately
            SelectionCanvas.Focus();
        }

        private void SelectionCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                startPoint = e.GetPosition(SelectionCanvas);
                isDragging = true;
                SelectionBorder.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionBorder, startPoint.X);
                Canvas.SetTop(SelectionBorder, startPoint.Y);
                SelectionBorder.Width = 0;
                SelectionBorder.Height = 0;
            }
        }

        private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPoint = e.GetPosition(SelectionCanvas);
                double x = Math.Min(startPoint.X, currentPoint.X);
                double y = Math.Min(startPoint.Y, currentPoint.Y);
                double width = Math.Abs(startPoint.X - currentPoint.X);
                double height = Math.Abs(startPoint.Y - currentPoint.Y);

                Canvas.SetLeft(SelectionBorder, x);
                Canvas.SetTop(SelectionBorder, y);
                SelectionBorder.Width = width;
                SelectionBorder.Height = height;
            }
        }

        private async void SelectionCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                SelectionBorder.Visibility = Visibility.Collapsed;

                Point endPoint = e.GetPosition(SelectionCanvas);
                
                double wpfX = Math.Min(startPoint.X, endPoint.X);
                double wpfY = Math.Min(startPoint.Y, endPoint.Y);
                double wpfWidth = Math.Abs(startPoint.X - endPoint.X);
                double wpfHeight = Math.Abs(startPoint.Y - endPoint.Y);

                // Ignore tiny clicks/selections
                if (wpfWidth > 4 && wpfHeight > 4)
                {
                    // Hide immediately so selection overlay is not captured in the screenshot
                    this.Hide();

                    try
                    {
                        // 1. Calculate DPI scaled coordinates
                        DpiScale dpi = VisualTreeHelper.GetDpi(this);
                        
                        // We must offset the coordinates by the VirtualScreenLeft/Top
                        // because SystemParameters.VirtualScreenLeft can be negative (e.g. left monitor is secondary)
                        int screenX = (int)((wpfX + SystemParameters.VirtualScreenLeft) * dpi.DpiScaleX);
                        int screenY = (int)((wpfY + SystemParameters.VirtualScreenTop) * dpi.DpiScaleY);
                        int width = (int)(wpfWidth * dpi.DpiScaleX);
                        int height = (int)(wpfHeight * dpi.DpiScaleY);

                        // 2. Capture the region
                        using (Bitmap bitmap = CaptureRegion(screenX, screenY, width, height))
                        {
                            // 3. Extract text
                            string extractedText = await OcrEngineHelper.ExtractTextFromBitmapAsync(bitmap);
                            
                            // 4. Save to temp.json if text was found
                            if (!string.IsNullOrWhiteSpace(extractedText))
                            {
                                TempJsonWriter.AppendEntry(extractedText, "text");
                                System.Diagnostics.Debug.WriteLine($"Successfully appended to JSON: {extractedText}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("No text detected in selected region.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error during OCR processing: {ex.Message}", "OCR Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                
                this.Close();
            }
        }

        private void SelectionCanvas_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private Bitmap CaptureRegion(int x, int y, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
    }
}

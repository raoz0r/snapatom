using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Text_Grab
{
    public static class OcrEngineHelper
    {
        /// <summary>
        /// Extracts text from a System.Drawing.Bitmap using the Windows OCR API.
        /// </summary>
        /// <param name="bitmap">The image to OCR.</param>
        /// <param name="languageTag">Language tag to use (e.g. "en-US").</param>
        /// <returns>The recognized text as a string.</returns>
        public static async Task<string> ExtractTextFromBitmapAsync(Bitmap bitmap, string languageTag = "en-US")
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            // 1. Save Bitmap to stream and convert to SoftwareBitmap
            using MemoryStream memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Bmp);
            memoryStream.Position = 0;

            var randomAccessStream = memoryStream.AsRandomAccessStream();
            BitmapDecoder bmpDecoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            using SoftwareBitmap softwareBitmap = await bmpDecoder.GetSoftwareBitmapAsync();

            // 2. Resolve OCR Engine Language
            Windows.Globalization.Language language = new Windows.Globalization.Language(languageTag);
            OcrEngine? ocrEngine = OcrEngine.TryCreateFromLanguage(language);
            
            if (ocrEngine == null)
            {
                // Fallback to user languages or default system language
                ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            if (ocrEngine == null)
            {
                throw new InvalidOperationException("Could not initialize native Windows OCR Engine. Please check if OCR language features are installed.");
            }

            // 3. Recognize and build text
            OcrResult ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
            
            StringBuilder sb = new StringBuilder();
            foreach (OcrLine line in ocrResult.Lines)
            {
                sb.AppendLine(line.Text);
            }

            return sb.ToString().TrimEnd();
        }
    }
}

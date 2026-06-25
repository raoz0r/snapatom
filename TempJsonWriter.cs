using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Text_Grab
{
    public class OcrEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public static class TempJsonWriter
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Appends a new OCR text entry with a timestamp and type tag to temp.json.
        /// </summary>
        /// <param name="text">The OCR text to store.</param>
        /// <param name="type">The type tag (e.g. "text" or "table").</param>
        public static void AppendEntry(string text, string type)
        {
            List<OcrEntry> entries;

            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    entries = JsonSerializer.Deserialize<List<OcrEntry>>(json) ?? new List<OcrEntry>();
                }
                else
                {
                    entries = new List<OcrEntry>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading temp.json: {ex.Message}. Starting fresh.");
                entries = new List<OcrEntry>();
            }

            entries.Add(new OcrEntry
            {
                Timestamp = DateTimeOffset.UtcNow.ToString("o"), // ISO 8601 format
                Type = type,
                Text = text
            });

            try
            {
                string updatedJson = JsonSerializer.Serialize(entries, JsonOptions);
                File.WriteAllText(FilePath, updatedJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing to temp.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the temp.json file if it exists.
        /// </summary>
        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing temp.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the path to temp.json.
        /// </summary>
        public static string GetFilePath() => FilePath;
    }
}

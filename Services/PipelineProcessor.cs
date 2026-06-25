using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Text_Grab
{
    public static class PipelineLogger
    {
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "logs");
        private static readonly string LogPath = Path.Combine(LogDir, "process_clippings.log");

        public static void LogInfo(string message) => Log(message, "INFO");
        public static void LogWarning(string message) => Log(message, "WARNING");
        public static void LogError(string message) => Log(message, "ERROR");

        private static void Log(string message, string level)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {level} - {message}";
                File.AppendAllText(LogPath, logLine + Environment.NewLine, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine(logLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

        public static string GetLogPath() => LogPath;
    }

    public static class PipelineProcessor
    {
        private class GeminiOutput
        {
            public string title { get; set; } = string.Empty;
            public string description { get; set; } = string.Empty;
            public string content { get; set; } = string.Empty;
            public string[] internal_links { get; set; } = Array.Empty<string>();
            public string[] external_links { get; set; } = Array.Empty<string>();
        }

        private static string CleanMetadataValue(string val)
        {
            if (string.IsNullOrEmpty(val))
                return "";
            // Replace colons with dashes to avoid frontmatter parsing issues
            return val.Replace(":", "-").Trim();
        }

        private static string SanitizeFilename(string name)
        {
            // Remove characters invalid for Windows filenames
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string sanitized = Regex.Replace(name, "[" + invalidChars + "<>:\"/\\\\|?*]", "");
            return sanitized.Trim();
        }

        private static string CleanJsonString(string text)
        {
            text = text.Trim();
            
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            if (text.StartsWith("```json"))
            {
                text = text.Substring(7);
            }
            else if (text.StartsWith("```"))
            {
                text = text.Substring(3);
            }

            if (text.EndsWith("```"))
            {
                text = text.Substring(0, text.Length - 3);
            }

            return text.Trim();
        }

        public static async Task ProcessBatchAsync(AppSettings settings, string typeArg = "")
        {
            PipelineLogger.LogInfo($"Starting clippings processing pipeline in C# with provider '{settings.AiProvider}'...");

            if (string.IsNullOrWhiteSpace(settings.AiApiKey) && settings.AiProvider != "Custom OpenAI-Compatible")
            {
                PipelineLogger.LogError($"{settings.AiProvider} API key is not configured. Aborting batch process.");
                throw new InvalidOperationException($"{settings.AiProvider} API key is not configured.");
            }

            string tempJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp.json");
            if (!File.Exists(tempJsonPath))
            {
                PipelineLogger.LogWarning($"temp.json not found at {tempJsonPath}. Nothing to process.");
                return;
            }

            // Read temp.json
            string rawJson;
            try
            {
                rawJson = await File.ReadAllTextAsync(tempJsonPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                PipelineLogger.LogError($"Failed to read temp.json: {ex.Message}");
                throw;
            }

            var entries = JsonSerializer.Deserialize<OcrEntry[]>(rawJson);
            if (entries == null || entries.Length == 0)
            {
                PipelineLogger.LogWarning("temp.json is empty or not in the expected array format. Exiting.");
                return;
            }

            PipelineLogger.LogInfo($"Found {entries.Length} entries to process in temp.json.");

            // Read prompt.md
            string promptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompt.md");
            if (!File.Exists(promptPath))
            {
                PipelineLogger.LogError($"System prompt file not found at {promptPath}. Aborting.");
                throw new FileNotFoundException("System prompt file not found.", promptPath);
            }

            string systemInstruction;
            try
            {
                systemInstruction = await File.ReadAllTextAsync(promptPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                PipelineLogger.LogError($"Failed to read prompt.md: {ex.Message}");
                throw;
            }

            // Initialize index.md path (temporary session index)
            string indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.md");
            try
            {
                await File.WriteAllTextAsync(indexPath, "", Encoding.UTF8);
                PipelineLogger.LogInfo($"Initialized temporary index.md at {indexPath}.");
            }
            catch (Exception ex)
            {
                PipelineLogger.LogError($"Failed to create temporary index.md: {ex.Message}");
                throw;
            }

            // Ensure destination clippings directory exists
            try
            {
                Directory.CreateDirectory(settings.ClippingsSavePath);
            }
            catch (Exception ex)
            {
                PipelineLogger.LogError($"Failed to create clippings output directory {settings.ClippingsSavePath}: {ex.Message}");
                throw;
            }

            int successCount = 0;
            int totalEntries = entries.Length;

            for (int i = 0; i < totalEntries; i++)
            {
                var entry = entries[i];
                PipelineLogger.LogInfo($"[{i + 1}/{totalEntries}] Processing entry...");

                string text = entry.Text ?? "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    PipelineLogger.LogInfo($"[{i + 1}/{totalEntries}] Entry has empty text. Skipping.");
                    continue;
                }

                string entryType = entry.Type ?? "text";
                string timestamp = entry.Timestamp ?? "";

                // Read current index.md
                string indexContent = "";
                try
                {
                    if (File.Exists(indexPath))
                    {
                        indexContent = await File.ReadAllTextAsync(indexPath, Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    PipelineLogger.LogError($"Failed to read index.md: {ex.Message}");
                }

                // Construct user prompt
                string userPrompt = $@"Here is the current running index of files created so far in this session (available for internal links):
---
{indexContent}
---

Here is the raw OCR data you need to process:
---
Timestamp: {timestamp}
Type: {entryType}
Text:
{text}
---";

                // Query AI
                GeminiOutput? aiData = null;
                try
                {
                    PipelineLogger.LogInfo($"  -> Sending request to {settings.AiProvider} using model '{settings.AiModelName}'...");
                    string rawResponseText = await AiClient.GenerateContentAsync(
                        settings.AiProvider,
                        settings.AiApiKey,
                        settings.AiModelName,
                        settings.CustomEndpoint,
                        systemInstruction,
                        userPrompt
                    );

                    string cleanedJson = CleanJsonString(rawResponseText);
                    aiData = JsonSerializer.Deserialize<GeminiOutput>(cleanedJson);
                }
                catch (Exception ex)
                {
                    PipelineLogger.LogError($"  -> ERROR processing entry: {ex.Message}");
                    continue;
                }

                if (aiData == null)
                {
                    PipelineLogger.LogError($"  -> Failed to deserialize JSON output from Gemini response.");
                    continue;
                }

                string title = string.IsNullOrWhiteSpace(aiData.title) ? $"Captured Note {i + 1}" : aiData.title;
                string description = aiData.description ?? "";
                string content = aiData.content ?? "";
                string[] internalLinks = aiData.internal_links ?? Array.Empty<string>();
                string[] externalLinks = aiData.external_links ?? Array.Empty<string>();

                // Clean metadata values to remove any colons
                string typeClean = CleanMetadataValue(typeArg == "" ? entryType : typeArg);
                string descriptionClean = CleanMetadataValue(description);

                // Format links lists
                var cleanedInternalLinksList = new StringBuilder();
                if (internalLinks != null)
                {
                    foreach (var link in internalLinks)
                    {
                        cleanedInternalLinksList.AppendLine($"- {link.Replace(":", "")}");
                    }
                }
                string internalLinksStr = cleanedInternalLinksList.ToString().TrimEnd();

                var externalLinksList = new StringBuilder();
                if (externalLinks != null)
                {
                    foreach (var link in externalLinks)
                    {
                        externalLinksList.AppendLine($"- {link}");
                    }
                }
                string externalLinksStr = externalLinksList.ToString().TrimEnd();

                // Construct custom metadata lines
                var customMetaStr = new StringBuilder();
                if (settings.CustomMetadata != null)
                {
                    foreach (var meta in settings.CustomMetadata)
                    {
                        if (!string.IsNullOrWhiteSpace(meta.Key))
                        {
                            customMetaStr.AppendLine($"{CleanMetadataValue(meta.Key)}: {CleanMetadataValue(meta.Value)}");
                        }
                    }
                }

                // Construct markdown file output
                string mdContent = $@"---
categories: Clippings
type: {typeClean}
description: {descriptionClean}
{customMetaStr.ToString().TrimEnd()}
---

## Concept
{content}

## Links
{internalLinksStr}

## References
{externalLinksStr}
";

                // Save note file
                string safeTitle = SanitizeFilename(title);
                string noteFilename = $"{safeTitle}.md";
                string noteFilepath = Path.Combine(settings.ClippingsSavePath, noteFilename);

                try
                {
                    await File.WriteAllTextAsync(noteFilepath, mdContent, Encoding.UTF8);
                    PipelineLogger.LogInfo($"  -> Saved note: {noteFilename}");

                    // Update running index
                    await File.AppendAllTextAsync(indexPath, $"- [[{title}]] - {descriptionClean}\n", Encoding.UTF8);

                    successCount++;
                }
                catch (Exception ex)
                {
                    PipelineLogger.LogError($"  -> Failed to write note {noteFilename} or update index: {ex.Message}");
                }

                // Sleep briefly between calls to be a good API citizen (like the 10s python sleep)
                if (i < totalEntries - 1)
                {
                    await Task.Delay(10000);
                }
            }

            // Save permanent index file to clippings folder before deleting temporary index.md
            if (File.Exists(indexPath))
            {
                try
                {
                    string safeType = string.IsNullOrWhiteSpace(typeArg) ? "General" : typeArg;
                    string indexFilename = $"_index_{SanitizeFilename(safeType)}.md";
                    string permanentIndexPath = Path.Combine(settings.ClippingsSavePath, indexFilename);
                    
                    string runningIndexContent = await File.ReadAllTextAsync(indexPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(runningIndexContent))
                    {
                        if (File.Exists(permanentIndexPath))
                        {
                            string existing = await File.ReadAllTextAsync(permanentIndexPath, Encoding.UTF8);
                            if (!existing.EndsWith("\n") && !existing.EndsWith("\r"))
                            {
                                existing += "\n";
                            }
                            await File.WriteAllTextAsync(permanentIndexPath, existing + runningIndexContent, Encoding.UTF8);
                        }
                        else
                        {
                            string header = $"# {safeType} Index\n\n";
                            await File.WriteAllTextAsync(permanentIndexPath, header + runningIndexContent, Encoding.UTF8);
                        }
                        PipelineLogger.LogInfo($"Saved permanent index to: {permanentIndexPath}");
                    }
                }
                catch (Exception ex)
                {
                    PipelineLogger.LogError($"Failed to save permanent index file: {ex.Message}");
                }
            }

            // Cleanup temporary index.md
            if (File.Exists(indexPath))
            {
                try
                {
                    File.Delete(indexPath);
                    PipelineLogger.LogInfo("Cleaned up temporary index.md.");
                }
                catch (Exception ex)
                {
                    PipelineLogger.LogWarning($"Failed to remove temporary index.md: {ex.Message}");
                }
            }

            // Relocate temp.json to backup folder only if all entries were successfully processed
            if (successCount == totalEntries)
            {
                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scrapped_backup");
                try
                {
                    Directory.CreateDirectory(backupDir);
                    string timestampStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupPath = Path.Combine(backupDir, $"temp_{timestampStr}.json");
                    File.Move(tempJsonPath, backupPath);
                    PipelineLogger.LogInfo($"Moved temp.json to backup: {backupPath}");
                }
                catch (Exception ex)
                {
                    PipelineLogger.LogError($"Failed to backup temp.json: {ex.Message}");
                }
            }
            else
            {
                PipelineLogger.LogInfo($"Some or all entries failed to process ({successCount}/{totalEntries} succeeded). Keeping temp.json in place.");
            }

            PipelineLogger.LogInfo($"Completed processing batch: {successCount}/{totalEntries} entries successfully processed.");
        }
    }
}

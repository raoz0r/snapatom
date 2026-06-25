using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SnapAtom
{
    public static class AiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        /// <summary>
        /// Sends a generation prompt to the configured AI provider and returns the text response.
        /// </summary>
        public static async Task<string> GenerateContentAsync(
            string provider,
            string apiKey,
            string modelName,
            string customEndpoint,
            string systemInstructionText,
            string promptText)
        {
            if (string.IsNullOrWhiteSpace(apiKey) && provider != "Custom OpenAI-Compatible")
                throw new ArgumentException("API Key is not set.", nameof(apiKey));

            string url;
            string jsonPayload;
            var request = new HttpRequestMessage(HttpMethod.Post, "");

            if (provider == "Google AI Studio")
            {
                url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = promptText } } }
                    },
                    systemInstruction = new
                    {
                        parts = new[] { new { text = systemInstructionText } }
                    },
                    generationConfig = new
                    {
                        responseMimeType = "application/json"
                    }
                };
                jsonPayload = JsonSerializer.Serialize(requestBody);
                request.RequestUri = new Uri(url);
            }
            else if (provider == "OpenAI")
            {
                url = "https://api.openai.com/v1/chat/completions";
                var requestBody = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemInstructionText },
                        new { role = "user", content = promptText }
                    },
                    response_format = new { type = "json_object" }
                };
                jsonPayload = JsonSerializer.Serialize(requestBody);
                request.RequestUri = new Uri(url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            else if (provider == "Anthropic")
            {
                url = "https://api.anthropic.com/v1/messages";
                var requestBody = new
                {
                    model = modelName,
                    system = systemInstructionText,
                    messages = new[]
                    {
                        new { role = "user", content = promptText }
                    },
                    max_tokens = 4000
                };
                jsonPayload = JsonSerializer.Serialize(requestBody);
                request.RequestUri = new Uri(url);
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (provider == "Custom OpenAI-Compatible")
            {
                url = string.IsNullOrWhiteSpace(customEndpoint) ? "http://localhost:11434/v1/chat/completions" : customEndpoint;
                var requestBody = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemInstructionText },
                        new { role = "user", content = promptText }
                    },
                    response_format = new { type = "json_object" }
                };
                jsonPayload = JsonSerializer.Serialize(requestBody);
                request.RequestUri = new Uri(url);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }
            }
            else
            {
                throw new NotSupportedException($"AI Provider '{provider}' is not supported.");
            }

            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await HttpClient.SendAsync(request);
                string rawResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"{provider} API error (Status {response.StatusCode}): {rawResponse}");
                }

                using var doc = JsonDocument.Parse(rawResponse);

                if (provider == "Google AI Studio")
                {
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) && 
                        candidates.ValueKind == JsonValueKind.Array && 
                        candidates.GetArrayLength() > 0)
                    {
                        var content = candidates[0].GetProperty("content");
                        if (content.TryGetProperty("parts", out var parts) && 
                            parts.ValueKind == JsonValueKind.Array && 
                            parts.GetArrayLength() > 0)
                        {
                            return parts[0].GetProperty("text").GetString() ?? string.Empty;
                        }
                    }
                }
                else if (provider == "OpenAI" || provider == "Custom OpenAI-Compatible")
                {
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && 
                        choices.ValueKind == JsonValueKind.Array && 
                        choices.GetArrayLength() > 0)
                    {
                        var message = choices[0].GetProperty("message");
                        if (message.TryGetProperty("content", out var content))
                        {
                            return content.GetString() ?? string.Empty;
                        }
                    }
                }
                else if (provider == "Anthropic")
                {
                    if (doc.RootElement.TryGetProperty("content", out var content) && 
                        content.ValueKind == JsonValueKind.Array && 
                        content.GetArrayLength() > 0)
                    {
                        if (content[0].TryGetProperty("text", out var text))
                        {
                            return text.GetString() ?? string.Empty;
                        }
                    }
                }

                throw new Exception("Unexpected response format from AI API.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling {provider} API: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Simple check to verify if the API Key and model connect correctly.
        /// </summary>
        public static async Task<bool> TestConnectionAsync(string provider, string apiKey, string modelName, string customEndpoint)
        {
            try
            {
                string text = await GenerateContentAsync(
                    provider,
                    apiKey,
                    modelName,
                    customEndpoint,
                    "You are a test assistant.",
                    "Respond with JSON containing only a single boolean true value.");
                return !string.IsNullOrWhiteSpace(text);
            }
            catch
            {
                return false;
            }
        }
    }
}

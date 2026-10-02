using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace StardewPresence.Framework.Services
{
    public class CloudImageUploader
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly IMonitor monitor;
        private string? lastImageHash;
        private string? cachedUploadedUrl;

        public CloudImageUploader(IMonitor monitor)
        {
            this.monitor = monitor;
        }

        public async Task<string?> UploadImageAsync(byte[] pngBytes, string apiUrl, string? authBearerToken = null)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                this.monitor.Log("[ImageUploader] Image byte array is empty.", LogLevel.Warn);
                return null;
            }

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                ModLogger.LogTrace(this.monitor, "[ImageUploader] Upload URL not configured.");
                return null;
            }

            string currentHash = ComputeHash(pngBytes);
            if (currentHash == this.lastImageHash && !string.IsNullOrEmpty(this.cachedUploadedUrl))
            {
                return this.cachedUploadedUrl;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);

                if (!string.IsNullOrWhiteSpace(authBearerToken))
                {
                    if (authBearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", authBearerToken);
                    }
                    else
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authBearerToken);
                    }
                }

                request.Headers.UserAgent.ParseAdd("StardewPresence/0.2.0");

                using var content = new ByteArrayContent(pngBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                request.Content = content;

                using HttpResponseMessage response = await HttpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMessage = TryExtractErrorMessage(responseBody) ?? response.ReasonPhrase ?? "Unknown error";
                    ModLogger.LogWarn(this.monitor, $"[ImageUploader] Upload failed (HTTP {(int)response.StatusCode}): {errorMessage}");
                    return null;
                }

                string trimmedBody = responseBody.Trim();
                if (trimmedBody.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmedBody.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    this.lastImageHash = currentHash;
                    this.cachedUploadedUrl = trimmedBody;
                    ModLogger.LogInfo(this.monitor, $"[ImageUploader] Image uploaded: {trimmedBody}");
                    return trimmedBody;
                }

                using JsonDocument doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("url", out JsonElement urlElement))
                {
                    string? uploadedUrl = urlElement.GetString();
                    if (!string.IsNullOrEmpty(uploadedUrl))
                    {
                        this.lastImageHash = currentHash;
                        this.cachedUploadedUrl = uploadedUrl;
                        ModLogger.LogInfo(this.monitor, $"[ImageUploader] Image uploaded: {uploadedUrl}");
                        return uploadedUrl;
                    }
                }
                else if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.TryGetProperty("url", out JsonElement dataUrlElement))
                {
                    string? uploadedUrl = dataUrlElement.GetString();
                    if (!string.IsNullOrEmpty(uploadedUrl))
                    {
                        this.lastImageHash = currentHash;
                        this.cachedUploadedUrl = uploadedUrl;
                        ModLogger.LogInfo(this.monitor, $"[ImageUploader] Image uploaded: {uploadedUrl}");
                        return uploadedUrl;
                    }
                }

                ModLogger.LogWarn(this.monitor, $"[ImageUploader] Response missing url: {responseBody}");
                return null;
            }
            catch (TaskCanceledException)
            {
                ModLogger.LogWarn(this.monitor, "[ImageUploader] Request timed out connecting to upload API.");
                return null;
            }
            catch (HttpRequestException ex)
            {
                // Offline or network unreachable - fallback to default logo
                ModLogger.LogWarn(this.monitor, $"[ImageUploader] Offline or network unreachable: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.LogError(this.monitor, $"[ImageUploader] Upload exception: {ex.Message}");
                return null;
            }
        }

        public void InvalidateCache()
        {
            this.lastImageHash = null;
            this.cachedUploadedUrl = null;
        }

        private static string ComputeHash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static string? TryExtractErrorMessage(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                {
                    return errorElement.GetString();
                }
                if (doc.RootElement.TryGetProperty("message", out JsonElement msgElement))
                {
                    return msgElement.GetString();
                }
            }
            catch
            {
            }
            return null;
        }
    }
}

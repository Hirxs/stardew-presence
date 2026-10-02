using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewPresence.Framework.Models;
using StardewPresence.Framework.Rendering;
using StardewModdingAPI;
using StardewValley;

namespace StardewPresence.Framework.Services
{
    public class FarmerImageGenerator : IDisposable
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly ModConfig config;
        private readonly ImageHostingService imageHostingService;
        private readonly Dictionary<string, Texture2D?> backgroundCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> remoteFrameCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient FrameHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        private string? loadingFrameName;
        private Task<byte[]?>? frameDownloadTask;

        private string? lastAppearanceKey;
        private string? cachedUploadedUrl;
        private bool isUploading;

        public FarmerImageGenerator(IModHelper helper, IMonitor monitor, ModConfig config)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.config = config;
            this.imageHostingService = new ImageHostingService(monitor);
        }

        public string? GetCachedImageUrl() => cachedUploadedUrl;

        public void ResetCache()
        {
            cachedUploadedUrl = null;
            lastAppearanceKey = null;
            ClearBackgroundCache();
        }

        public void InvalidateCache()
        {
            lastAppearanceKey = null;
            ClearBackgroundCache();
        }

        public void ClearBackgroundCache()
        {
            foreach (var kvp in backgroundCache)
            {
                try { kvp.Value?.Dispose(); } catch { }
            }
            backgroundCache.Clear();
        }

        public string GetAppearanceKey(Farmer farmer)
        {
            string season = !string.IsNullOrWhiteSpace(config.ForcedSeason) && !config.ForcedSeason.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? config.ForcedSeason.ToLowerInvariant()
                : (Game1.currentSeason ?? "spring").ToLowerInvariant();

            string hairColor = farmer.hairstyleColor.Value.ToString();
            string pantsColor = farmer.pantsColor.Value.ToString();
            string spouseKey = farmer.spouse ?? "single";

            string liveSuffix = config.FarmerFrame < 0
                ? $"_live_{farmer.FarmerSprite?.CurrentFrame}_{farmer.FacingDirection}"
                : "";

            return $"{season}_{config.ImageFrame}_{config.ImageFrameUrl}_{config.ForcedSeason}_{config.UseCustomMapBackground}_{config.CustomBackgroundMode}_{config.LastBackgroundCaptureTimestamp}_{farmer.UniqueMultiplayerID}_{farmer.IsMale}_{farmer.skin.Value}_{farmer.hair.Value}_{hairColor}_{farmer.shirt.Value}_{farmer.pants.Value}_{pantsColor}_{farmer.accessory.Value}_{farmer.hat.Value}_{farmer.boots.Value}_{spouseKey}_{config.CompanionType}_{config.ShowFarmer}_{config.ShowCompanion}_{config.ShowSpouse}_{config.ShowPet}_{config.FarmerOffsetX}_{config.FarmerOffsetY}_{config.FarmerScale}_{config.FarmerFrame}_{config.FarmerFlip}_{config.FarmerFacingDirection}_{config.FarmerEmote}_{config.SpouseOffsetX}_{config.SpouseOffsetY}_{config.SpouseScale}_{config.SpouseFrame}_{config.SpouseLayerFront}_{config.SpouseFlip}_{config.SpouseFacingDirection}_{config.SpouseEmote}_{config.PetOffsetX}_{config.PetOffsetY}_{config.PetScale}_{config.PetFrame}_{config.PetLayerFront}_{config.PetFlip}_{config.PetEmote}{liveSuffix}";
        }

        public void CheckAndUpdate(Farmer farmer, Action<string> onUrlUpdated)
        {
            if (!InternalSettings.EnableDynamicFarmerImage || farmer == null) return;
            if (isUploading) return;

            string currentKey = GetAppearanceKey(farmer);
            if (currentKey == lastAppearanceKey)
            {
                return;
            }

            try
            {
                byte[]? pngBytes = RenderFarmerCard(farmer);
                if (pngBytes == null || pngBytes.Length == 0)
                {
                    monitor.Log("[StardewPresence] Failed to render farmer portrait canvas.", LogLevel.Trace);
                    return;
                }

                lastAppearanceKey = currentKey;

                isUploading = true;

                Task.Run(async () =>
                {
                    try
                    {
                        string? url = await imageHostingService.UploadAsync(pngBytes, config);
                        if (!string.IsNullOrEmpty(url))
                        {
                            cachedUploadedUrl = url;
                            ModLogger.LogTrace(monitor, $"[StardewPresence] Uploaded dynamic farmer card to: {url}");
                            onUrlUpdated?.Invoke(url);
                        }
                    }
                    catch (Exception ex)
                    {
                        monitor.Log($"[StardewPresence] Error uploading dynamic image: {ex.Message}", LogLevel.Warn);
                    }
                    finally
                    {
                        isUploading = false;
                    }
                });
            }
            catch (Exception ex)
            {
                ModLogger.LogWarn(monitor, $"[StardewPresence] Render error: {ex.Message}");
            }
        }

        public Texture2D? GetSeasonalBackgroundTexture(string season)
        {
            season = (season ?? "spring").ToLowerInvariant();
            string cacheKey = config.UseCustomMapBackground
                ? $"custom_{config.CustomBackgroundMode}_{season}_{config.LastBackgroundCaptureTimestamp}"
                : $"seasonal_{season}";

            if (backgroundCache.TryGetValue(cacheKey, out var cached) && cached != null && !cached.IsDisposed)
            {
                return cached;
            }

            if (config.UseCustomMapBackground)
            {
                string customFileName = config.CustomBackgroundMode.Equals("Seasonal", StringComparison.OrdinalIgnoreCase)
                    ? $"custom_bg_{season}.png"
                    : "custom_bg.png";

                string customPath = Path.Combine(helper.DirectoryPath, "assets", "backgrounds", customFileName);
                if (!File.Exists(customPath) && config.CustomBackgroundMode.Equals("Seasonal", StringComparison.OrdinalIgnoreCase))
                {
                    customPath = Path.Combine(helper.DirectoryPath, "assets", "backgrounds", "custom_bg.png");
                }

                if (File.Exists(customPath))
                {
                    try
                    {
                        using var fileStream = File.OpenRead(customPath);
                        var customTex = Texture2D.FromStream(Game1.graphics.GraphicsDevice, fileStream);
                        backgroundCache[cacheKey] = customTex;
                        return customTex;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.LogTrace(monitor, $"[StardewPresence] Error loading custom background '{customPath}': {ex.Message}");
                    }
                }
            }

            string staticBgFile = Path.Combine(helper.DirectoryPath, "assets", "backgrounds", $"{season}_bg.png");
            if (File.Exists(staticBgFile))
            {
                try
                {
                    var tex = helper.ModContent.Load<Texture2D>($"assets/backgrounds/{season}_bg.png");
                    backgroundCache[cacheKey] = tex;
                    return tex;
                }
                catch (Exception ex)
                {
                    ModLogger.LogTrace(monitor, $"[StardewPresence] Could not load background '{staticBgFile}': {ex.Message}");
                }
            }

            backgroundCache[cacheKey] = null;
            return null;
        }

        public byte[]? RenderFarmerCard(Farmer farmer)
        {
            if (farmer == null) return null;

            int width = 256;
            int height = 256;
            var graphicsDevice = Game1.graphics.GraphicsDevice;

            string season = !string.IsNullOrWhiteSpace(config.ForcedSeason) && !config.ForcedSeason.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? config.ForcedSeason.ToLowerInvariant()
                : (Game1.currentSeason ?? "spring").ToLowerInvariant();

            Texture2D? bgTexture = GetSeasonalBackgroundTexture(season);
            string frameSelection = config.ImageFrame.Equals("url", StringComparison.OrdinalIgnoreCase)
                ? config.ImageFrameUrl
                : config.ImageFrame;
            Texture2D? frameTexture = GetFrameTexture(frameSelection);
            bool remoteFrameRequested = config.ImageFrame.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(config.ImageFrameUrl, UriKind.Absolute, out Uri? frameUri) &&
                (frameUri.Scheme == Uri.UriSchemeHttp || frameUri.Scheme == Uri.UriSchemeHttps);

            if (remoteFrameRequested && frameTexture == null)
            {
                return null;
            }

            var (spouseNpc, spouseFarmer, petNpc) = CompanionResolver.GetCompanions(farmer, config);
            var layout = UILayout.Load(helper.DirectoryPath);

            using var renderTarget = new RenderTarget2D(
                graphicsDevice,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents
            );

            FarmerSceneRenderer.RenderSceneToTarget(
                graphicsDevice,
                renderTarget,
                farmer,
                spouseFarmer,
                spouseNpc,
                petNpc,
                bgTexture,
                frameTexture,
                config,
                layout,
                width,
                height
            );

            using MemoryStream ms = new MemoryStream();
            renderTarget.SaveAsPng(ms, width, height);
            return ms.ToArray();
        }

        public Texture2D? GetFrameTexture(string? frameName = null)
        {
            string selectedFrame = frameName ?? config.ImageFrame;
            if (string.IsNullOrWhiteSpace(selectedFrame) || selectedFrame.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (Uri.TryCreate(selectedFrame, UriKind.Absolute, out Uri? frameUri) &&
                (frameUri.Scheme == Uri.UriSchemeHttp || frameUri.Scheme == Uri.UriSchemeHttps))
            {
                if (remoteFrameCache.TryGetValue(selectedFrame, out Texture2D? cachedFrame))
                {
                    return cachedFrame;
                }

                if (frameDownloadTask == null || !string.Equals(loadingFrameName, selectedFrame, StringComparison.Ordinal))
                {
                    loadingFrameName = selectedFrame;
                    frameDownloadTask = DownloadFrameAsync(frameUri);
                    return null;
                }

                if (!frameDownloadTask.IsCompleted)
                {
                    return null;
                }

                byte[]? frameBytes = frameDownloadTask.Status == TaskStatus.RanToCompletion
                    ? frameDownloadTask.Result
                    : null;
                frameDownloadTask = null;
                loadingFrameName = null;

                if (frameBytes == null || frameBytes.Length == 0)
                {
                    return null;
                }

                try
                {
                    using var stream = new MemoryStream(frameBytes);
                    Texture2D remoteFrame = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                    remoteFrameCache[selectedFrame] = remoteFrame;
                    return remoteFrame;
                }
                catch (Exception ex)
                {
                    ModLogger.LogTrace(monitor, $"[StardewPresence] Could not decode image frame URL: {ex.Message}");
                    return null;
                }
            }

            string assetName = selectedFrame.Equals("wooden", StringComparison.OrdinalIgnoreCase)
                ? "player_frame_wooden"
                : $"player_frame_{selectedFrame}";

            try
            {
                return helper.ModContent.Load<Texture2D>($"assets/frames/{assetName}.png");
            }
            catch (Exception ex)
            {
                ModLogger.LogTrace(monitor, $"[StardewPresence] Could not load image frame '{selectedFrame}': {ex.Message}");
                return null;
            }
        }

        private static async Task<byte[]?> DownloadFrameAsync(Uri frameUri)
        {
            try
            {
                return await FrameHttpClient.GetByteArrayAsync(frameUri);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            ClearBackgroundCache();
            foreach (Texture2D texture in remoteFrameCache.Values)
            {
                try { texture.Dispose(); } catch { }
            }
            remoteFrameCache.Clear();
            imageHostingService.Dispose();
        }
    }
}

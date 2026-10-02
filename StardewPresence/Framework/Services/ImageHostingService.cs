using System;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace StardewPresence.Framework.Services
{
    public class ImageHostingService : IDisposable
    {
        private readonly IMonitor monitor;
        private readonly CloudImageUploader cloudUploader;

        public ImageHostingService(IMonitor monitor)
        {
            this.monitor = monitor;
            this.cloudUploader = new CloudImageUploader(monitor);
        }

        public async Task<string?> UploadAsync(byte[] pngBytes, ModConfig config)
        {
            if (pngBytes == null || pngBytes.Length == 0) return null;

            string uploadUrl = config.CustomUploadUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(uploadUrl))
            {
                ModLogger.LogTrace(monitor, "[StardewPresence] CustomUploadUrl is empty. Dynamic portrait upload skipped.");
                return null;
            }

            return await cloudUploader.UploadImageAsync(pngBytes, uploadUrl, config.CustomUploadAuthHeader);
        }

        public void InvalidateCache()
        {
            cloudUploader.InvalidateCache();
        }

        public void Dispose()
        {
        }
    }
}

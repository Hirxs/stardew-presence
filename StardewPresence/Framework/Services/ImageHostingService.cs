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

            string uploadUrl = !string.IsNullOrWhiteSpace(config.CustomUploadUrl)
                ? config.CustomUploadUrl
                : InternalSettings.CustomUploadUrl;

            string authHeader = !string.IsNullOrWhiteSpace(config.CustomUploadAuthHeader)
                ? config.CustomUploadAuthHeader
                : InternalSettings.CustomUploadAuthHeader;

            return await cloudUploader.UploadImageAsync(pngBytes, uploadUrl, authHeader);
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

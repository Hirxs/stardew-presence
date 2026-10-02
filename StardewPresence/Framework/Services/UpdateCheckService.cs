using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;

namespace StardewPresence.Framework.Services
{
    public class UpdateCheckService
    {
        private const string DefaultNexusUrl = "https://www.nexusmods.com/stardewvalley/mods/51515";

        private readonly IMonitor monitor;
        private readonly IModHelper helper;
        private readonly ModConfig config;

        public bool IsUpdateAvailable { get; private set; }
        public string? NewVersion { get; private set; }
        public string? UpdateUrl { get; private set; }
        public bool HasNotifiedThisSession { get; private set; }

        public UpdateCheckService(IMonitor monitor, IModHelper helper, ModConfig config)
        {
            this.monitor = monitor;
            this.helper = helper;
            this.config = config;
        }

        /// <summary>
        /// Scans the SMAPI log file in the background to detect updates flagged by SMAPI for Nexus Mods.
        /// </summary>
        public void CheckForUpdatesAsync()
        {
            if (!InternalSettings.CheckForUpdates) return;

            Task.Run(async () =>
            {
                try
                {
                    // Scan immediately and retry a couple of times in case SMAPI is still completing its startup checks
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (CheckSmapiLogForUpdate())
                        {
                            return;
                        }
                        await Task.Delay(3000);
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LogTrace(this.monitor, $"[UpdateCheck] Error checking for updates: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Reads the SMAPI log file to see if SMAPI flagged an update for Stardew Presence from Nexus Mods.
        /// </summary>
        private bool CheckSmapiLogForUpdate()
        {
            try
            {
                string logPath = Path.Combine(Constants.DataPath, "ErrorLogs", "SMAPI-latest.txt");
                if (!File.Exists(logPath))
                {
                    logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
                }

                if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
                {
                    return false;
                }

                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Look for SMAPI update alert lines:
                    // e.g. "[ALERT SMAPI]    Stardew Presence (BETA) 0.2.1: https://www.nexusmods.com/stardewvalley/mods/51515 (you have 0.2.0)"
                    if (line.Contains("[ALERT SMAPI]", StringComparison.OrdinalIgnoreCase) &&
                        (line.Contains("Stardew Presence", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Hyris.StardewPresence", StringComparison.OrdinalIgnoreCase)))
                    {
                        var match = Regex.Match(line, @"Stardew Presence.*?\s+([0-9]+\.[0-9]+\.[0-9]+.*?):\s+(https?://\S+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            this.NewVersion = match.Groups[1].Value.Trim();
                            this.UpdateUrl = match.Groups[2].Value.Trim();
                        }
                        else
                        {
                            this.NewVersion = "nueva versión";
                            this.UpdateUrl = DefaultNexusUrl;
                        }

                        this.IsUpdateAvailable = true;
                        ModLogger.LogInfo(this.monitor, $"[StardewPresence] SMAPI Nexus update alert detected: {this.NewVersion} ({this.UpdateUrl})");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LogTrace(this.monitor, $"[UpdateCheck] Could not read SMAPI log: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Displays the in-game HUD notification if an update is pending and hasn't been shown yet.
        /// </summary>
        public void ShowNotificationIfPending()
        {
            if (!InternalSettings.CheckForUpdates || !this.IsUpdateAvailable || this.HasNotifiedThisSession)
            {
                return;
            }

            if (!Context.IsWorldReady || !Context.IsPlayerFree)
            {
                return;
            }

            this.HasNotifiedThisSession = true;

            string versionText = !string.IsNullOrWhiteSpace(this.NewVersion) ? this.NewVersion : "";
            string notificationText = this.helper.Translation.Get("notification.update", new { version = versionText })
                .Default(string.IsNullOrWhiteSpace(versionText)
                    ? "Stardew Presence: ¡Nueva actualización disponible!"
                    : $"Stardew Presence: ¡Nueva actualización disponible! (v{versionText})")
                .ToString();

            Game1.addHUDMessage(new HUDMessage(notificationText, HUDMessage.newQuest_type));
            this.monitor.Log($"[StardewPresence] {notificationText}. Descárgala en Nexus Mods: {this.UpdateUrl ?? DefaultNexusUrl}", LogLevel.Alert);
        }
    }
}

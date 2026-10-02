using System;
using System.Linq;
using StardewPresence.Framework.Clients;
using StardewPresence.Framework.Integrations;
using StardewPresence.Framework.Menus;
using StardewPresence.Framework.Models;
using StardewPresence.Framework.Presence;
using StardewPresence.Framework.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewPresence
{
    public class ModEntry : Mod
    {
        private ModConfig config = null!;
        private DiscordRpcClient? discordClient;
        private PresenceManager? presenceManager;
        private FarmerImageGenerator? farmerImageGenerator;
        private UpdateCheckService? updateCheckService;
        private int ticksSinceLastUpdate;
        private bool shouldShowWelcomeNotification;
        private string? lastKnownPlayerName;
        private string? pendingDynamicImageUrl;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.Player.Warped += OnWarped;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;

        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            int totalMods = Helper.ModRegistry.GetAll().Count();
            ModLogger.LogTrace(Monitor, $"[StardewPresence] Detected {totalMods} active mods.");

            presenceManager = new PresenceManager(Helper, Monitor, config);
            presenceManager.SetModCount(totalMods);
            farmerImageGenerator = new FarmerImageGenerator(Helper, Monitor, config);

            updateCheckService = new UpdateCheckService(Monitor, Helper, config);
            updateCheckService.CheckForUpdatesAsync();

            InitDiscordClient();
            RegisterGenericModConfigMenu();
            ConfigFileFormatter.RestoreComments(Helper.DirectoryPath);

            UpdatePresence();
        }

        private void InitDiscordClient()
        {
            try
            {
                discordClient?.Dispose();
                discordClient = new DiscordRpcClient(config.AppId, (msg, isInfo) =>
                {
                    ModLogger.Log(Monitor, msg, isInfo ? LogLevel.Info : LogLevel.Trace);
                });

                discordClient.OnJoin += OnDiscordJoin;
                discordClient.OnJoinRequest += OnDiscordJoinRequest;
                discordClient.OnReady += user =>
                {
                    ModLogger.LogTrace(Monitor, $"[StardewPresence] Ready event received from Discord client.");
                };
            }
            catch (Exception ex)
            {
                Monitor.Log($"[StardewPresence] Failed to initialize Discord RPC: {ex}", LogLevel.Error);
            }
        }

        private void OnDiscordJoin(string secret)
        {
            ModLogger.LogInfo(Monitor, $"[StardewPresence] Discord Join invitation received with secret/code: {secret}");

            try
            {
                DesktopClipboard.SetText(secret);
                Game1.addHUDMessage(new HUDMessage($"[Discord Invite] Código de invitación '{secret}' copiado al portapapeles.", HUDMessage.achievement_type));
            }
            catch (Exception ex)
            {
                ModLogger.LogTrace(Monitor, $"[StardewPresence] Could not process Discord join secret: {ex.Message}");
            }
        }

        private void OnDiscordJoinRequest(DiscordUser user)
        {
            ModLogger.LogInfo(Monitor, $"[StardewPresence] Player '{user.Username}' requested to join your farm via Discord.");
            try
            {
                Game1.addHUDMessage(new HUDMessage($"[Discord] {user.Username} solicitó unirse a tu granja.", HUDMessage.newQuest_type));
            }
            catch
            {
            }
        }

        private void UpdatePresence()
        {
            if (discordClient == null || presenceManager == null) return;

            try
            {
                if (!config.EnablePresence)
                {
                    discordClient.ClearActivity();
                    return;
                }

                var activity = presenceManager.BuildActivity();
                discordClient.SetActivity(activity);
            }
            catch (Exception ex)
            {
                ModLogger.LogTrace(Monitor, $"[StardewPresence] Error building presence update: {ex}");
            }
        }

        private void UpdateFarmerImage()
        {
            if (Context.IsWorldReady && Game1.player != null && farmerImageGenerator != null)
            {
                farmerImageGenerator.CheckAndUpdate(Game1.player, newUrl =>
                {
                    pendingDynamicImageUrl = newUrl;
                });
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (config.UseCustomMapBackground && config.AutoUpdateCustomBackground && !string.IsNullOrWhiteSpace(config.CustomMapLocation) && farmerImageGenerator != null)
            {
                MapBackgroundCaptureHelper.TryRefreshConfiguredSpot(this.Helper, this.Monitor, this.config, this.farmerImageGenerator, notify: false);
            }

            lastKnownPlayerName = Game1.player.Name;
            UpdateFarmerImage();
            UpdatePresence();
            updateCheckService?.CheckForUpdatesAsync();
            shouldShowWelcomeNotification = true;
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            presenceManager?.SetDynamicImageUrl(null);
            farmerImageGenerator?.InvalidateCache();
            shouldShowWelcomeNotification = false;
            UpdatePresence();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (config.UseCustomMapBackground && config.AutoUpdateCustomBackground && !string.IsNullOrWhiteSpace(config.CustomMapLocation) && farmerImageGenerator != null)
            {
                MapBackgroundCaptureHelper.TryRefreshConfiguredSpot(this.Helper, this.Monitor, this.config, this.farmerImageGenerator, notify: false);
            }

            UpdateFarmerImage();
            UpdatePresence();
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            UpdatePresence();
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (e.IsLocalPlayer)
            {
                UpdatePresence();
            }
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(pendingDynamicImageUrl))
            {
                string newImageUrl = pendingDynamicImageUrl;
                pendingDynamicImageUrl = null;
                presenceManager?.SetDynamicImageUrl(newImageUrl);
                UpdatePresence();
            }

            if (shouldShowWelcomeNotification && Context.IsWorldReady && Context.IsPlayerFree)
            {
                shouldShowWelcomeNotification = false;
                if (config.ShowEditorKeyNotification)
                {
                    string keyName = config.EditorKey.ToString();
                    string text = Helper.Translation.Get("notification.welcome", new { key = keyName })
                        .Default($"Stardew Presence: Presiona [{keyName}] para personalizar tu Discord RPC.")
                        .ToString();
                    Game1.addHUDMessage(new HUDMessage(text, HUDMessage.newQuest_type));
                }

                updateCheckService?.ShowNotificationIfPending();
            }

            ticksSinceLastUpdate++;
            if (ticksSinceLastUpdate >= 10)
            {
                ticksSinceLastUpdate = 0;
                UpdateFarmerImage();
                UpdatePresence();
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (e.Button == config.EditorKey && Context.IsWorldReady && Game1.activeClickableMenu == null)
            {
                OpenEditorMenu();
            }
        }

        private void OpenEditorMenu()
        {
            if (!Context.IsWorldReady || farmerImageGenerator == null)
            {
                Monitor.Log("[StardewPresence] You must load a save file before opening the Portrait Editor.", LogLevel.Warn);
                return;
            }

            Game1.activeClickableMenu = new PortraitEditorMenu(Helper, Monitor, ModManifest, config, farmerImageGenerator, () =>
            {
                UpdateFarmerImage();
                UpdatePresence();
            });
        }

        private void OpenMapBackgroundSelector()
        {
            if (!Context.IsWorldReady || farmerImageGenerator == null)
            {
                Monitor.Log("[StardewPresence] You must load a save file before opening the Map Background Selector.", LogLevel.Warn);
                return;
            }

            Game1.activeClickableMenu = new MapBackgroundSelectorMenu(Helper, Monitor, config, farmerImageGenerator, () =>
            {
                UpdateFarmerImage();
                UpdatePresence();
            });
        }

        private void RefreshCustomBackground()
        {
            if (!Context.IsWorldReady)
            {
                Monitor.Log("[StardewPresence] You must load a save file before refreshing the map background.", LogLevel.Warn);
                return;
            }

            if (farmerImageGenerator != null)
            {
                bool ok = MapBackgroundCaptureHelper.TryRefreshConfiguredSpot(this.Helper, this.Monitor, this.config, this.farmerImageGenerator, notify: true);
                if (ok)
                {
                    ModLogger.LogInfo(Monitor, $"[StardewPresence] Successfully refreshed map background spot at '{config.CustomMapLocation}' ({config.CustomMapTileX}, {config.CustomMapTileY}) for season '{Game1.currentSeason}'.");
                    UpdateFarmerImage();
                    UpdatePresence();
                }
                else
                {
                    Monitor.Log("[StardewPresence] Could not refresh map background. Ensure you have captured a spot first.", LogLevel.Warn);
                }
            }
        }

        private void RegisterGenericModConfigMenu()
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null) return;

            gmcm.Register(
                mod: ModManifest,
                reset: () =>
                {
                    config = new ModConfig();
                    Helper.WriteConfig(config);
                    ConfigFileFormatter.RestoreComments(Helper.DirectoryPath);
                    UpdateFarmerImage();
                    UpdatePresence();
                },
                save: () =>
                {
                    Helper.WriteConfig(config);
                    ConfigFileFormatter.RestoreComments(Helper.DirectoryPath);
                    UpdateFarmerImage();
                    UpdatePresence();
                }
            );

            // General
            gmcm.AddSectionTitle(ModManifest, () => "General Settings");
            gmcm.AddBoolOption(ModManifest, () => config.EnablePresence, val => config.EnablePresence = val, () => "Enable Rich Presence", () => "Enable or disable Discord Rich Presence");
            gmcm.AddTextOption(ModManifest, () => config.AppId, val => { config.AppId = val; InitDiscordClient(); }, () => "Discord Application ID", () => "The Client ID from Discord Developer Portal");
            gmcm.AddTextOption(ModManifest, () => config.GameNameMode, val => config.GameNameMode = val, () => "Game Name", () => "Choose the displayed game name in Discord: default, smapi, modded, custom", new[] { "default", "smapi", "modded", "custom" });
            gmcm.AddTextOption(ModManifest, () => config.CustomGameName, val => config.CustomGameName = val, () => "Custom Game Name", () => "Used only when Game Name is set to custom.");
            gmcm.AddTextOption(ModManifest, () => config.ImageFrame, val => { config.ImageFrame = val; UpdateFarmerImage(); UpdatePresence(); }, () => "Image Frame", () => "Frame around the generated farmer image.", new[] { "none", "wooden", "wooden_black", "wooden_blue", "wooden_gray", "wooden_green", "wooden_mythic_purple", "wooden_pink", "wooden_purple", "wooden_red", "wooden_yellow" });

            // Visual Portrait Editor
            gmcm.AddSectionTitle(ModManifest, () => "Visual Portrait & Spouse Editor");
            gmcm.AddKeybind(ModManifest, () => config.EditorKey, val => config.EditorKey = val, () => "Open Editor Key", () => "Press this key in-game to open the interactive visual editor menu (default F8).");
            gmcm.AddBoolOption(ModManifest, () => config.UseCustomMapBackground, val => { config.UseCustomMapBackground = val; UpdateFarmerImage(); UpdatePresence(); }, () => "Use Custom Map Background", () => "Use custom captured map background instead of default seasonal art");
            gmcm.AddBoolOption(ModManifest, () => config.AutoUpdateCustomBackground, val => config.AutoUpdateCustomBackground = val, () => "Auto-Update Map Background", () => "Automatically updates the custom map background on new days and seasonal changes.");

            // Main Menu
            gmcm.AddSectionTitle(ModManifest, () => "Main Menu Presence");
            gmcm.AddTextOption(ModManifest, () => config.TitleScreenLogo, val => config.TitleScreenLogo = val, () => "Title Screen Logo", () => "Choose between: auto, smapi, vanilla, expanded", new[] { "auto", "smapi", "vanilla", "expanded" });
            gmcm.AddTextOption(ModManifest, () => config.TitleMenuDetails, val => config.TitleMenuDetails = val, () => "Menu Details Text", () => "Text shown on the Details line when in main menu");
            gmcm.AddTextOption(ModManifest, () => config.TitleMenuState, val => config.TitleMenuState = val, () => "Menu State Text", () => "Text shown on the State line when in main menu");
            gmcm.AddTextOption(ModManifest, () => config.TitleLargeImageKey, val => config.TitleLargeImageKey = val, () => "Title Large Image Key", () => "Asset key for the main menu large icon");
            gmcm.AddTextOption(ModManifest, () => config.TitleSmallImageKey, val => config.TitleSmallImageKey = val, () => "Title Small Image Key", () => "Asset key for the main menu small icon");

            // Custom Presence Lines
            gmcm.AddSectionTitle(ModManifest, () => "Presence Text Lines");
            gmcm.AddTextOption(ModManifest, () => config.CustomLine1Format, val => { config.CustomLine1Format = val; UpdatePresence(); }, () => "Line 1 (Details) Format", () => "Use tokens: {Position}, {farmname}, {Player}, {Money}, {Date}, {Time}, {Season}, {Weather}, {Spouse}, {Pet}, {Health}, {Energy}, {Mods}");
            gmcm.AddTextOption(ModManifest, () => config.CustomLine2Format, val => { config.CustomLine2Format = val; UpdatePresence(); }, () => "Line 2 (State) Format", () => "Leave empty for auto (Date & Money), or use tokens");

            // Discord RPC Buttons
            gmcm.AddSectionTitle(ModManifest, () => "Discord RPC Buttons");
            gmcm.AddBoolOption(ModManifest, () => config.EnableButton1, val => { config.EnableButton1 = val; UpdatePresence(); }, () => "Enable Button 1", () => "Show clickable button 1 on your Discord profile");
            gmcm.AddTextOption(ModManifest, () => config.Button1Label, val => { config.Button1Label = val; UpdatePresence(); }, () => "Button 1 Label", () => "Button 1 text (max 32 chars)");
            gmcm.AddTextOption(ModManifest, () => config.Button1Url, val => { config.Button1Url = val; UpdatePresence(); }, () => "Button 1 URL", () => "Web address button 1 opens");
            gmcm.AddBoolOption(ModManifest, () => config.EnableButton2, val => { config.EnableButton2 = val; UpdatePresence(); }, () => "Enable Button 2", () => "Show clickable button 2 on your Discord profile");
            gmcm.AddTextOption(ModManifest, () => config.Button2Label, val => { config.Button2Label = val; UpdatePresence(); }, () => "Button 2 Label", () => "Button 2 text (max 32 chars)");
            gmcm.AddTextOption(ModManifest, () => config.Button2Url, val => { config.Button2Url = val; UpdatePresence(); }, () => "Button 2 URL", () => "Web address button 2 opens");

            // In-Game Details
            gmcm.AddSectionTitle(ModManifest, () => "In-Game Toggles & Privacy");
            gmcm.AddBoolOption(ModManifest, () => config.ShowLocation, val => config.ShowLocation = val, () => "Show Location / Activity", () => "Show current location, mining floor, fishing, etc.");
            gmcm.AddBoolOption(ModManifest, () => config.HideNpcHouses, val => config.HideNpcHouses = val, () => "Privacy: Hide NPC Houses", () => "Show generic town/forest instead of specific NPC houses");
            gmcm.AddBoolOption(ModManifest, () => config.ShowEventDetails, val => config.ShowEventDetails = val, () => "Show Heart Event Details", () => "Show NPC name and heart level during cutscenes");
            gmcm.AddBoolOption(ModManifest, () => config.ShowFarmName, val => config.ShowFarmName = val, () => "Show Farm Name on Line 2", () => "Show farm name on presence state line");
            gmcm.AddBoolOption(ModManifest, () => config.ShowMoney, val => config.ShowMoney = val, () => "Show Money in Tooltip", () => "Show farmer gold count");
            gmcm.AddBoolOption(ModManifest, () => config.ShowQiCoins, val => config.ShowQiCoins = val, () => "Show Qi Coins", () => "Show Qi club coins next to money when unlocked");
            gmcm.AddBoolOption(ModManifest, () => config.ShowHealthAndEnergy, val => config.ShowHealthAndEnergy = val, () => "Show Health & Energy", () => "Show HP and Stamina in weather icon tooltip");
            gmcm.AddBoolOption(ModManifest, () => config.ShowSpouse, val => config.ShowSpouse = val, () => "Show Spouse / Roommate", () => "Show spouse or roommate in tooltip");
            gmcm.AddBoolOption(ModManifest, () => config.ShowMultiplayer, val => config.ShowMultiplayer = val, () => "Show Multiplayer Status", () => "Show party size in multiplayer sessions");
            gmcm.AddBoolOption(ModManifest, () => config.ShowModCount, val => config.ShowModCount = val, () => "Show Mod Count", () => "Display active mod count in tooltips");
            gmcm.AddBoolOption(ModManifest, () => config.ShowElapsedTime, val => config.ShowElapsedTime = val, () => "Show Elapsed Time", () => "Display time elapsed since game launch");

            // Notifications & Updates
            gmcm.AddSectionTitle(ModManifest, () => "Notifications & Updates");
            gmcm.AddBoolOption(
                ModManifest,
                () => config.ShowEditorKeyNotification,
                val => config.ShowEditorKeyNotification = val,
                () => $"Show [{config.EditorKey}] Startup Notification",
                () => $"Show the HUD notification on save load reminding you to press [{config.EditorKey}]. When disabled, this notification is hidden, but update notifications remain active."
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                discordClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

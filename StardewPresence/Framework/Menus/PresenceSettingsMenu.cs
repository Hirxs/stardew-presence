using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewPresence.Framework.Models;
using StardewPresence.Framework.Presence;
using StardewPresence.Framework.Services;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace StardewPresence.Framework.Menus
{
    public class PresenceSettingsMenu : IClickableMenu
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly ModConfig liveConfig;
        private readonly ModConfig originalConfig;
        private readonly Action onConfigSaved;
        private readonly Action? onReturnToParentMenu;

        private static readonly Rectangle YellowButtonSourceRect = new Rectangle(432, 439, 9, 9);
        private static readonly Rectangle CheckboxUncheckedRect = new Rectangle(227, 425, 9, 9);
        private static readonly Rectangle CheckboxCheckedRect   = new Rectangle(236, 425, 9, 9);

        // Tabs
        private int selectedTab = 0; // 0 = General, 1 = Lines, 2 = Buttons
        private ClickableComponent tabGeneral = null!;
        private ClickableComponent tabLines = null!;
        private ClickableComponent tabButtons = null!;

        // Bottom action buttons
        private ClickableComponent btnCancel = null!;
        private ClickableComponent btnDefault = null!;
        private ClickableComponent btnSave = null!;
        private ClickableComponent btnSaveAndClose = null!;
        private ClickableTextureComponent closeButton = null!;
        private ClickableTextureComponent btnScrollUp = null!;
        private ClickableTextureComponent btnScrollDown = null!;

        // Tab 0: General Settings items
        private class SettingItem
        {
            public string Label { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsCheckbox { get; set; }
            public Func<bool>? GetBoolValue { get; set; }
            public Action<bool>? SetBoolValue { get; set; }
            public Func<string>? GetOptionValue { get; set; }
            public Action? NextOption { get; set; }
            public Rectangle Bounds { get; set; }
            public Rectangle ControlBounds { get; set; }
        }

        private readonly List<SettingItem> settingsList = new();
        private int currentScroll;
        public UILayout Layout { get; private set; }

        private FileSystemWatcher? layoutWatcher;
        private FileSystemWatcher? devLayoutWatcher;
        private DateTime lastReloadTime = DateTime.MinValue;

        // Tab 1: Lines Customizer UI
        private TextBox line1Box = null!;
        private TextBox line2Box = null!;
        private int activeLineBox = 1;
        private readonly string[] availableTokens = new[]
        {
            "[Position]", "[farmname]", "[Player]", "[Money]", "[Date]", "[Time]",
            "[Season]", "[Day]", "[Year]", "[Weather]", "[Spouse]", "[Pet]",
            "[Health]", "[Energy]", "[Mods]", "[QiCoins]", "[QiGems]"
        };
        private readonly List<ClickableComponent> tokenChips = new();

        // Tab 2: Buttons Customizer UI
        private ClickableComponent btn1Toggle = null!;
        private TextBox btn1LabelBox = null!;
        private TextBox btn1UrlBox = null!;
        private ClickableComponent btn2Toggle = null!;
        private TextBox btn2LabelBox = null!;
        private TextBox btn2UrlBox = null!;

        // Custom Game Name input
        private TextBox customGameNameBox = null!;
        private string? lastRealtimeConfigKey;

        public PresenceSettingsMenu(
            IModHelper helper,
            IMonitor monitor,
            ModConfig config,
            Action onConfigSaved,
            Action? onReturnToParentMenu = null)
            : base(0, 0, 880, 640, false)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.liveConfig = config;
            this.originalConfig = config.Clone();
            this.onConfigSaved = onConfigSaved;
            this.onReturnToParentMenu = onReturnToParentMenu;

            this.Layout = UILayout.Load(helper.DirectoryPath);
            DoLayout();
            InitSettings();
            InitHotReloadWatcher();
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            DoLayout();
        }

        private void DoLayout()
        {
            width = Layout.SettingsMenuWidth > 0 ? Layout.SettingsMenuWidth : 880;
            height = Layout.SettingsMenuHeight > 0 ? Layout.SettingsMenuHeight : 640;

            width = Math.Min(width, Game1.uiViewport.Width - 32);
            height = Math.Min(height, Game1.uiViewport.Height - 32);

            Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(width, height);
            xPositionOnScreen = (int)center.X;
            yPositionOnScreen = (int)center.Y;

            InitButtons();

            // Tabs inside dialogue box near top
            int tabW = 150;
            int tabH = 34;
            int tabGap = 12;
            int totalTabsW = (tabW * 3) + (tabGap * 2);
            int tabStartX = xPositionOnScreen + (width - totalTabsW) / 2;
            int tabY = yPositionOnScreen + 38;

            tabGeneral = new ClickableComponent(new Rectangle(tabStartX, tabY, tabW, tabH), "tab_general");
            tabLines   = new ClickableComponent(new Rectangle(tabStartX + tabW + tabGap, tabY, tabW, tabH), "tab_lines");
            tabButtons = new ClickableComponent(new Rectangle(tabStartX + (tabW + tabGap) * 2, tabY, tabW, tabH), "tab_buttons");

            // GMCM Layout: Labels on the left, inputs on the right
            var boxTex = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            int padLeft = Layout.SettingsMenuListPadLeft > 0 ? Layout.SettingsMenuListPadLeft : 60;
            int padRight = Layout.SettingsMenuListPadRight > 0 ? Layout.SettingsMenuListPadRight : 60;
            int leftX = xPositionOnScreen + padLeft;
            int rightEdge = xPositionOnScreen + width - padRight;
            int controlW = Layout.SettingsMenuControlWidth > 0 ? Layout.SettingsMenuControlWidth : 320;
            int rightX = rightEdge - controlW;
            int contentTopY = yPositionOnScreen + (Layout.SettingsMenuListPadTop > 0 ? Layout.SettingsMenuListPadTop : 88);

            if (line1Box == null)
            {
                line1Box = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 160,
                    Text = !string.IsNullOrWhiteSpace(liveConfig.CustomLine1Format) ? liveConfig.CustomLine1Format : "[Position] [farmname]"
                };
                line2Box = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 160,
                    Text = liveConfig.CustomLine2Format ?? ""
                };
                btn1LabelBox = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 64,
                    Text = liveConfig.Button1Label ?? "Stardew Presence"
                };
                btn1UrlBox = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 250,
                    Text = liveConfig.Button1Url ?? "https://www.nexusmods.com/stardewvalley/mods/51515"
                };
                btn2LabelBox = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 64,
                    Text = liveConfig.Button2Label ?? ""
                };
                btn2UrlBox = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 250,
                    Text = liveConfig.Button2Url ?? ""
                };
                customGameNameBox = new TextBox(boxTex, null, Game1.smallFont, Game1.textColor)
                {
                    limitWidth = false,
                    textLimit = 64,
                    Text = liveConfig.CustomGameName ?? "Stardew Valley"
                };
            }

            // Tab 1 (Lines) layout
            tokenChips.Clear();
            int prefixTableY = yPositionOnScreen + 110;
            int prefixTableX = leftX;
            int chipH = 28;
            int chipSpacing = 8;
            int chipX = prefixTableX;
            int chipRowY = prefixTableY + 10;

            foreach (string token in availableTokens)
            {
                int chipW = (int)Game1.smallFont.MeasureString(token).X + 18;
                if (chipX + chipW > rightEdge)
                {
                    chipX = prefixTableX;
                    chipRowY += chipH + 6;
                }
                tokenChips.Add(new ClickableComponent(new Rectangle(chipX, chipRowY, chipW, chipH), token));
                chipX += chipW + chipSpacing;
            }

            int l1Y = chipRowY + chipH + 30;
            line1Box.X = rightX;
            line1Box.Y = l1Y;
            line1Box.Width = controlW;

            int l2Y = l1Y + 76;
            line2Box.X = rightX;
            line2Box.Y = l2Y;
            line2Box.Width = controlW;

            // Tab 2 (Buttons) layout
            int b1ToggleY = contentTopY + 34;
            btn1Toggle = new ClickableComponent(new Rectangle(leftX, b1ToggleY, width - padLeft - padRight, 36), "btn1_toggle");

            int b1LabelY = b1ToggleY + 38;
            btn1LabelBox.X = rightX;
            btn1LabelBox.Y = b1LabelY;
            btn1LabelBox.Width = controlW;

            int b1UrlY = b1LabelY + 38;
            btn1UrlBox.X = rightX;
            btn1UrlBox.Y = b1UrlY;
            btn1UrlBox.Width = controlW;

            int b2ToggleY = b1UrlY + 44;
            btn2Toggle = new ClickableComponent(new Rectangle(leftX, b2ToggleY, width - padLeft - padRight, 36), "btn2_toggle");

            int b2LabelY = b2ToggleY + 38;
            btn2LabelBox.X = rightX;
            btn2LabelBox.Y = b2LabelY;
            btn2LabelBox.Width = controlW;

            int b2UrlY = b2LabelY + 38;
            btn2UrlBox.X = rightX;
            btn2UrlBox.Y = b2UrlY;
            btn2UrlBox.Width = controlW;

            customGameNameBox.X = rightX;
            customGameNameBox.Y = contentTopY + 220;
            customGameNameBox.Width = controlW;
        }

        private void InitHotReloadWatcher()
        {
            try
            {
                string watchDir = helper.DirectoryPath;
                if (Directory.Exists(watchDir))
                {
                    layoutWatcher = new FileSystemWatcher(watchDir, "ui_layout.json")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };
                    layoutWatcher.Changed += OnLayoutFileChanged;
                    layoutWatcher.Created += OnLayoutFileChanged;
                }

                string devDir = @"C:\Users\ale_y\Documents\Stardew-Dev\StardewPresence\StardewPresence";
                if (!Directory.Exists(devDir))
                {
                    devDir = @"C:\Users\ale_y\Documents\Stardew-Dev\StardewPresence";
                }
                if (Directory.Exists(devDir) && !devDir.Equals(watchDir, StringComparison.OrdinalIgnoreCase))
                {
                    devLayoutWatcher = new FileSystemWatcher(devDir, "ui_layout.json")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };
                    devLayoutWatcher.Changed += OnLayoutFileChanged;
                    devLayoutWatcher.Created += OnLayoutFileChanged;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LogTrace(monitor, $"[Hot-Reload] Could not start FileSystemWatcher in SettingsMenu: {ex.Message}");
            }
        }

        public void ReloadLayoutManual()
        {
            try
            {
                Layout = UILayout.Load(helper.DirectoryPath);
                width = Layout.SettingsMenuWidth > 0 ? Layout.SettingsMenuWidth : 880;
                height = Layout.SettingsMenuHeight > 0 ? Layout.SettingsMenuHeight : 640;
                DoLayout();
                Game1.playSound("coin");
                ModLogger.LogInfo(monitor, "[Hot-Reload] ui_layout.json reloaded in PresenceSettingsMenu via F5.");
            }
            catch (Exception ex)
            {
                monitor.Log($"[Hot-Reload] Error reloading settings layout: {ex.Message}", LogLevel.Warn);
            }
        }

        private void OnLayoutFileChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - lastReloadTime).TotalMilliseconds < 250) return;
            lastReloadTime = DateTime.Now;

            try
            {
                System.Threading.Thread.Sleep(50);
                Layout = UILayout.Load(helper.DirectoryPath);
                width = Layout.SettingsMenuWidth > 0 ? Layout.SettingsMenuWidth : 880;
                height = Layout.SettingsMenuHeight > 0 ? Layout.SettingsMenuHeight : 640;
                DoLayout();
                Game1.playSound("drumkit6");
                ModLogger.LogInfo(monitor, "[Hot-Reload] ui_layout.json reloaded in SettingsMenu.");
            }
            catch (Exception ex)
            {
                monitor.Log($"[Hot-Reload] Error reloading in SettingsMenu: {ex.Message}", LogLevel.Warn);
            }
        }

        private void InitSettings()
        {
            settingsList.Clear();

            // 1. Title Screen Logo
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.logo").ToString(),
                IsCheckbox = false,
                GetOptionValue = () =>
                {
                    return liveConfig.TitleScreenLogo switch
                    {
                        "smapi" => helper.Translation.Get("settings.logo_smapi").ToString(),
                        "vanilla" => helper.Translation.Get("settings.logo_vanilla").ToString(),
                        "expanded" => helper.Translation.Get("settings.logo_expanded").ToString(),
                        _ => helper.Translation.Get("settings.logo_auto").ToString()
                    };
                },
                NextOption = () =>
                {
                    liveConfig.TitleScreenLogo = liveConfig.TitleScreenLogo switch
                    {
                        "auto" => "smapi",
                        "smapi" => "vanilla",
                        "vanilla" => "expanded",
                        _ => "auto"
                    };
                }
            });

            settingsList.Add(new SettingItem
            {
                Label = "Game Name",
                IsCheckbox = false,
                GetOptionValue = () =>
                {
                    return liveConfig.GameNameMode switch
                    {
                        "smapi" => "SMAPI",
                        "modded" => "Stardew Modded",
                        "custom" => string.IsNullOrWhiteSpace(liveConfig.CustomGameName) ? "Custom" : liveConfig.CustomGameName,
                        _ => "Stardew Valley"
                    };
                },
                NextOption = () =>
                {
                    liveConfig.GameNameMode = liveConfig.GameNameMode switch
                    {
                        "default" => "smapi",
                        "smapi" => "modded",
                        "modded" => "custom",
                        _ => "default"
                    };

                    if (liveConfig.GameNameMode == "custom" && string.IsNullOrWhiteSpace(liveConfig.CustomGameName))
                    {
                        liveConfig.CustomGameName = "Stardew Valley";
                    }
                }
            });

            // 2. Hide NPC Houses
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.hide_npc_houses").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.HideNpcHouses,
                SetBoolValue = (val) => liveConfig.HideNpcHouses = val
            });

            // 3. Show Event Details
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_event_details").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowEventDetails,
                SetBoolValue = (val) => liveConfig.ShowEventDetails = val
            });

            // 4. Show Farm Name
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_farm_name").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowFarmName,
                SetBoolValue = (val) => liveConfig.ShowFarmName = val
            });

            // 5. Show Money
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_money").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowMoney,
                SetBoolValue = (val) => liveConfig.ShowMoney = val
            });

            // 6. Show Elapsed Time
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_elapsed_time").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowElapsedTime,
                SetBoolValue = (val) => liveConfig.ShowElapsedTime = val
            });

            // 7. Show Mod Count
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_mod_count").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowModCount,
                SetBoolValue = (val) => liveConfig.ShowModCount = val
            });

            // 8. Show Multiplayer
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_multiplayer").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowMultiplayer,
                SetBoolValue = (val) => liveConfig.ShowMultiplayer = val
            });

            // 9. Show Companion
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_spouse").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowCompanion,
                SetBoolValue = (val) => liveConfig.ShowCompanion = val
            });

            // 10. Show [F8] Notification
            settingsList.Add(new SettingItem
            {
                Label = helper.Translation.Get("settings.show_editor_notification", new { key = liveConfig.EditorKey.ToString() })
                    .Default($"Show [{liveConfig.EditorKey}] Notification").ToString(),
                Description = helper.Translation.Get("settings.show_editor_notification_desc", new { key = liveConfig.EditorKey.ToString() })
                    .Default($"Show the reminder when loading a save to press [{liveConfig.EditorKey}]. (Update notifications will still be shown).").ToString(),
                IsCheckbox = true,
                GetBoolValue = () => liveConfig.ShowEditorKeyNotification,
                SetBoolValue = (val) => liveConfig.ShowEditorKeyNotification = val
            });
        }

        private void InitButtons()
        {
            closeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 36, yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f);

            int btnW = Layout.SettingsMenuButtonWidth > 0 ? Layout.SettingsMenuButtonWidth : 160;
            int btnH = Layout.SettingsMenuButtonHeight > 0 ? Layout.SettingsMenuButtonHeight : 38;
            int gap = Layout.SettingsMenuButtonGap > 0 ? Layout.SettingsMenuButtonGap : 12;
            int btnY = yPositionOnScreen + height - 24 - btnH;
            int totalW = (btnW * 4) + (gap * 3);
            int startX = xPositionOnScreen + (width - totalW) / 2;

            btnCancel = new ClickableComponent(new Rectangle(startX, btnY, btnW, btnH), "cancel");
            btnDefault = new ClickableComponent(new Rectangle(startX + (btnW + gap), btnY, btnW, btnH), "default");
            btnSave = new ClickableComponent(new Rectangle(startX + (btnW + gap) * 2, btnY, btnW, btnH), "save");
            btnSaveAndClose = new ClickableComponent(new Rectangle(startX + (btnW + gap) * 3, btnY, btnW, btnH), "save_close");

            int arrowX = xPositionOnScreen + width - 42;
            int contentTopY = yPositionOnScreen + (Layout.SettingsMenuListPadTop > 0 ? Layout.SettingsMenuListPadTop : 88);
            int maxVisible = Layout.SettingsMenuMaxVisibleItems > 0 ? Layout.SettingsMenuMaxVisibleItems : 8;
            int itemH = Layout.SettingsMenuItemHeight > 0 ? Layout.SettingsMenuItemHeight : 46;

            btnScrollUp = new ClickableTextureComponent(
                new Rectangle(arrowX, contentTopY + 38, 24, 24),
                Game1.mouseCursors,
                new Rectangle(421, 459, 12, 12),
                2f);
            btnScrollDown = new ClickableTextureComponent(
                new Rectangle(arrowX, contentTopY + 38 + (maxVisible * itemH) - 28, 24, 24),
                Game1.mouseCursors,
                new Rectangle(421, 472, 12, 12),
                2f);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (closeButton.containsPoint(x, y))
            {
                Game1.playSound("bigDeSelect");
                ExitMenu();
                return;
            }

            // Tab navigation
            if (tabGeneral.containsPoint(x, y))
            {
                selectedTab = 0;
                DeselectAllTextBoxes();
                Game1.playSound("smallSelect");
                return;
            }
            if (tabLines.containsPoint(x, y))
            {
                selectedTab = 1;
                DeselectAllTextBoxes();
                line1Box?.SelectMe();
                activeLineBox = 1;
                Game1.playSound("smallSelect");
                return;
            }
            if (tabButtons.containsPoint(x, y))
            {
                selectedTab = 2;
                DeselectAllTextBoxes();
                btn1LabelBox?.SelectMe();
                Game1.playSound("smallSelect");
                return;
            }

            // Action Buttons
            if (btnCancel.containsPoint(x, y))
            {
                liveConfig.CopyFrom(originalConfig);
                if (line1Box != null) line1Box.Text = originalConfig.CustomLine1Format ?? "[Position] [farmname]";
                if (line2Box != null) line2Box.Text = originalConfig.CustomLine2Format ?? "";
                if (btn1LabelBox != null) btn1LabelBox.Text = originalConfig.Button1Label ?? "Stardew Presence";
                if (btn1UrlBox != null) btn1UrlBox.Text = originalConfig.Button1Url ?? "https://www.nexusmods.com/stardewvalley/mods/51515";
                if (btn2LabelBox != null) btn2LabelBox.Text = originalConfig.Button2Label ?? "";
                if (btn2UrlBox != null) btn2UrlBox.Text = originalConfig.Button2Url ?? "";
                Game1.playSound("bigDeSelect");
                ExitMenu();
                return;
            }

            if (btnDefault.containsPoint(x, y))
            {
                var def = new ModConfig();
                if (selectedTab == 0)
                {
                    liveConfig.TitleScreenLogo = def.TitleScreenLogo;
                    liveConfig.HideNpcHouses = def.HideNpcHouses;
                    liveConfig.ShowFarmName = def.ShowFarmName;
                    liveConfig.ShowMoney = def.ShowMoney;
                    liveConfig.ShowElapsedTime = def.ShowElapsedTime;
                    liveConfig.ShowModCount = def.ShowModCount;
                    liveConfig.ShowEventDetails = def.ShowEventDetails;
                    liveConfig.ShowMultiplayer = def.ShowMultiplayer;
                    liveConfig.ShowCompanion = def.ShowCompanion;
                    liveConfig.ShowEditorKeyNotification = def.ShowEditorKeyNotification;
                }
                else if (selectedTab == 1)
                {
                    liveConfig.CustomLine1Format = def.CustomLine1Format;
                    liveConfig.CustomLine2Format = def.CustomLine2Format;
                    if (line1Box != null) line1Box.Text = liveConfig.CustomLine1Format;
                    if (line2Box != null) line2Box.Text = liveConfig.CustomLine2Format;
                }
                else if (selectedTab == 2)
                {
                    liveConfig.EnableButton1 = def.EnableButton1;
                    liveConfig.Button1Label = def.Button1Label;
                    liveConfig.Button1Url = def.Button1Url;
                    liveConfig.EnableButton2 = def.EnableButton2;
                    liveConfig.Button2Label = def.Button2Label;
                    liveConfig.Button2Url = def.Button2Url;
                    if (btn1LabelBox != null) btn1LabelBox.Text = liveConfig.Button1Label;
                    if (btn1UrlBox != null) btn1UrlBox.Text = liveConfig.Button1Url;
                    if (btn2LabelBox != null) btn2LabelBox.Text = liveConfig.Button2Label;
                    if (btn2UrlBox != null) btn2UrlBox.Text = liveConfig.Button2Url;
                }
                DeselectAllTextBoxes();
                onConfigSaved?.Invoke();
                lastRealtimeConfigKey = BuildRealtimeConfigKey();
                Game1.playSound("drumkit6");
                return;
            }

            if (btnSave.containsPoint(x, y) || btnSaveAndClose.containsPoint(x, y))
            {
                if (line1Box != null) liveConfig.CustomLine1Format = line1Box.Text;
                if (line2Box != null) liveConfig.CustomLine2Format = line2Box.Text;
                if (btn1LabelBox != null) liveConfig.Button1Label = btn1LabelBox.Text;
                if (btn1UrlBox != null) liveConfig.Button1Url = btn1UrlBox.Text;
                if (btn2LabelBox != null) liveConfig.Button2Label = btn2LabelBox.Text;
                if (btn2UrlBox != null) liveConfig.Button2Url = btn2UrlBox.Text;
                if (customGameNameBox != null) liveConfig.CustomGameName = customGameNameBox.Text;

                helper.WriteConfig(liveConfig);
                ConfigFileFormatter.RestoreComments(helper.DirectoryPath);
                onConfigSaved?.Invoke();

                if (btnSaveAndClose.containsPoint(x, y))
                {
                    Game1.playSound("money");
                    ExitMenu();
                }
                else
                {
                    Game1.playSound("coin");
                }
                return;
            }

            // Tab 0: General Settings items
            if (selectedTab == 0)
            {
                if (new Rectangle(customGameNameBox.X, customGameNameBox.Y, customGameNameBox.Width, 44).Contains(x, y) && liveConfig.GameNameMode == "custom")
                {
                    DeselectAllTextBoxes();
                    customGameNameBox.SelectMe();
                    Game1.playSound("smallSelect");
                    return;
                }

                int itemHeight = Layout.SettingsMenuItemHeight > 0 ? Layout.SettingsMenuItemHeight : 46;
                int maxVisible = Layout.SettingsMenuMaxVisibleItems > 0 ? Layout.SettingsMenuMaxVisibleItems : 8;
                int padLeft = Layout.SettingsMenuListPadLeft > 0 ? Layout.SettingsMenuListPadLeft : 60;
                int padRight = Layout.SettingsMenuListPadRight > 0 ? Layout.SettingsMenuListPadRight : 60;
                int contentTopY = yPositionOnScreen + (Layout.SettingsMenuListPadTop > 0 ? Layout.SettingsMenuListPadTop : 88);
                int listY = contentTopY + 38;

                if (settingsList.Count > maxVisible)
                {
                    if (btnScrollUp.containsPoint(x, y) && currentScroll > 0)
                    {
                        currentScroll--;
                        Game1.playSound("shiny4");
                        return;
                    }
                    if (btnScrollDown.containsPoint(x, y) && currentScroll + maxVisible < settingsList.Count)
                    {
                        currentScroll++;
                        Game1.playSound("shiny4");
                        return;
                    }
                }

                int startIdx = currentScroll;
                int count = Math.Min(maxVisible, settingsList.Count - startIdx);

                for (int i = 0; i < count; i++)
                {
                    var item = settingsList[startIdx + i];
                    int itemY = listY + (i * itemHeight);
                    var rowRect = new Rectangle(xPositionOnScreen + padLeft, itemY, width - (padLeft + padRight), itemHeight);

                    if (rowRect.Contains(x, y))
                    {
                        if (item.IsCheckbox)
                        {
                            bool cur = item.GetBoolValue?.Invoke() ?? false;
                            item.SetBoolValue?.Invoke(!cur);
                            Game1.playSound("drumkit6");
                        }
                        else
                        {
                            item.NextOption?.Invoke();
                            Game1.playSound("smallSelect");
                        }
                        return;
                    }
                }
                return;
            }

            // Tab 1: Lines Customizer
            if (selectedTab == 1)
            {
                if (new Rectangle(line1Box.X, line1Box.Y, line1Box.Width, 44).Contains(x, y))
                {
                    DeselectAllTextBoxes();
                    line1Box.SelectMe();
                    activeLineBox = 1;
                    Game1.playSound("smallSelect");
                    return;
                }

                if (new Rectangle(line2Box.X, line2Box.Y, line2Box.Width, 44).Contains(x, y))
                {
                    DeselectAllTextBoxes();
                    line2Box.SelectMe();
                    activeLineBox = 2;
                    Game1.playSound("smallSelect");
                    return;
                }

                foreach (var chip in tokenChips)
                {
                    if (chip.containsPoint(x, y))
                    {
                        var targetBox = activeLineBox == 2 ? line2Box : line1Box;
                        if (!string.IsNullOrEmpty(targetBox.Text) && !targetBox.Text.EndsWith(" "))
                        {
                            targetBox.Text += " ";
                        }
                        targetBox.Text += chip.name;
                        targetBox.SelectMe();
                        Game1.playSound("coin");
                        return;
                    }
                }

                DeselectAllTextBoxes();
                return;
            }

            // Tab 2: Buttons Customizer
            if (selectedTab == 2)
            {
                if (btn1Toggle.containsPoint(x, y))
                {
                    liveConfig.EnableButton1 = !liveConfig.EnableButton1;
                    Game1.playSound("drumkit6");
                    return;
                }

                if (btn2Toggle.containsPoint(x, y))
                {
                    liveConfig.EnableButton2 = !liveConfig.EnableButton2;
                    Game1.playSound("drumkit6");
                    return;
                }

                if (new Rectangle(btn1LabelBox.X, btn1LabelBox.Y, btn1LabelBox.Width, 44).Contains(x, y))
                {
                    DeselectAllTextBoxes();
                    btn1LabelBox.SelectMe();
                    Game1.playSound("smallSelect");
                    return;
                }

                if (new Rectangle(btn1UrlBox.X, btn1UrlBox.Y, btn1UrlBox.Width, 44).Contains(x, y))
                {
                    DeselectAllTextBoxes();
                    btn1UrlBox.SelectMe();
                    Game1.playSound("smallSelect");
                    return;
                }

                if (new Rectangle(btn2LabelBox.X, btn2LabelBox.Y, btn2LabelBox.Width, 44).Contains(x, y))
                {
                    DeselectAllTextBoxes();
                    btn2LabelBox.SelectMe();
                    Game1.playSound("smallSelect");
                    return;
                }

                if (new Rectangle(btn2UrlBox.X, btn2UrlBox.Y, btn2UrlBox.Width, 44).Contains(x, y))
                {
                    DeselectAllTextBoxes();
                    btn2UrlBox.SelectMe();
                    Game1.playSound("smallSelect");
                    return;
                }

                DeselectAllTextBoxes();
                return;
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (selectedTab == 0)
            {
                int maxVisible = Layout.SettingsMenuMaxVisibleItems > 0 ? Layout.SettingsMenuMaxVisibleItems : 7;
                if (direction > 0 && currentScroll > 0)
                {
                    currentScroll--;
                    Game1.playSound("shiny4");
                }
                else if (direction < 0 && currentScroll + maxVisible < settingsList.Count)
                {
                    currentScroll++;
                    Game1.playSound("shiny4");
                }
            }
        }

        public override void update(GameTime time)
        {
            base.update(time);
            line1Box?.Update();
            line2Box?.Update();
            btn1LabelBox?.Update();
            btn1UrlBox?.Update();
            btn2LabelBox?.Update();
            btn2UrlBox?.Update();

            if (line1Box != null) liveConfig.CustomLine1Format = line1Box.Text;
            if (line2Box != null) liveConfig.CustomLine2Format = line2Box.Text;
            if (btn1LabelBox != null) liveConfig.Button1Label = btn1LabelBox.Text;
            if (btn1UrlBox != null) liveConfig.Button1Url = btn1UrlBox.Text;
            if (btn2LabelBox != null) liveConfig.Button2Label = btn2LabelBox.Text;
            if (btn2UrlBox != null) liveConfig.Button2Url = btn2UrlBox.Text;
            if (customGameNameBox != null) liveConfig.CustomGameName = customGameNameBox.Text;

            string realtimeConfigKey = BuildRealtimeConfigKey();

            if (!string.Equals(lastRealtimeConfigKey, realtimeConfigKey, StringComparison.Ordinal))
            {
                lastRealtimeConfigKey = realtimeConfigKey;
                onConfigSaved?.Invoke();
            }
        }

        private string BuildRealtimeConfigKey()
        {
            return string.Join("\u001f",
                liveConfig.CustomLine1Format,
                liveConfig.CustomLine2Format,
                liveConfig.Button1Label,
                liveConfig.Button1Url,
                liveConfig.Button2Label,
                liveConfig.Button2Url,
                liveConfig.CustomGameName,
                liveConfig.GameNameMode,
                liveConfig.ImageFrame,
                liveConfig.ImageFrameUrl
            );
        }

        public override void receiveKeyPress(Keys key)
        {
            if (selectedTab == 1 || selectedTab == 2)
            {
                TextBox? activeBox = GetActiveTextBox();
                if (activeBox != null)
                {
                    if (key == Keys.Escape)
                    {
                        activeBox.Selected = false;
                        Game1.playSound("bigDeSelect");
                        return;
                    }
                    if (key == Keys.Tab)
                    {
                        CycleNextTextBox();
                        Game1.playSound("smallSelect");
                        return;
                    }
                    if (key == Keys.Enter)
                    {
                        activeBox.Selected = false;
                        Game1.playSound("smallSelect");
                        return;
                    }
                    return;
                }
            }

            if (key == Keys.F5)
            {
                ReloadLayoutManual();
                return;
            }

            if (key == Keys.Escape)
            {
                ExitMenu();
                return;
            }
            base.receiveKeyPress(key);
        }

        private TextBox? GetActiveTextBox()
        {
            if (line1Box?.Selected == true) return line1Box;
            if (line2Box?.Selected == true) return line2Box;
            if (btn1LabelBox?.Selected == true) return btn1LabelBox;
            if (btn1UrlBox?.Selected == true) return btn1UrlBox;
            if (btn2LabelBox?.Selected == true) return btn2LabelBox;
            if (btn2UrlBox?.Selected == true) return btn2UrlBox;
            if (customGameNameBox?.Selected == true) return customGameNameBox;
            return null;
        }

        private void CycleNextTextBox()
        {
            if (selectedTab == 1)
            {
                if (line1Box?.Selected == true)
                {
                    line1Box.Selected = false;
                    line2Box?.SelectMe();
                    activeLineBox = 2;
                }
                else
                {
                    line2Box?.Selected = false;
                    line1Box?.SelectMe();
                    activeLineBox = 1;
                }
            }
            else if (selectedTab == 2)
            {
                if (btn1LabelBox?.Selected == true)
                {
                    btn1LabelBox.Selected = false;
                    btn1UrlBox?.SelectMe();
                }
                else if (btn1UrlBox?.Selected == true)
                {
                    btn1UrlBox.Selected = false;
                    btn2LabelBox?.SelectMe();
                }
                else if (btn2LabelBox?.Selected == true)
                {
                    btn2LabelBox.Selected = false;
                    btn2UrlBox?.SelectMe();
                }
                else
                {
                    DeselectAllTextBoxes();
                    btn1LabelBox?.SelectMe();
                }
            }
        }

        private void DeselectAllTextBoxes()
        {
            if (line1Box != null) line1Box.Selected = false;
            if (line2Box != null) line2Box.Selected = false;
            if (btn1LabelBox != null) btn1LabelBox.Selected = false;
            if (btn1UrlBox != null) btn1UrlBox.Selected = false;
            if (btn2LabelBox != null) btn2LabelBox.Selected = false;
            if (btn2UrlBox != null) btn2UrlBox.Selected = false;
            if (customGameNameBox != null) customGameNameBox.Selected = false;
            if (Game1.keyboardDispatcher?.Subscriber is TextBox)
            {
                Game1.keyboardDispatcher.Subscriber = null;
            }
        }

        private void ExitMenu()
        {
            DeselectAllTextBoxes();
            layoutWatcher?.Dispose();
            devLayoutWatcher?.Dispose();

            if (onReturnToParentMenu != null)
            {
                onReturnToParentMenu.Invoke();
            }
            else
            {
                Game1.exitActiveMenu();
            }
        }

        protected override void cleanupBeforeExit()
        {
            base.cleanupBeforeExit();
            DeselectAllTextBoxes();
            layoutWatcher?.Dispose();
            devLayoutWatcher?.Dispose();
        }

        public override void draw(SpriteBatch b)
        {
            Vector2 centered = Utility.getTopLeftPositionForCenteringOnScreen(width, height);
            if (xPositionOnScreen != (int)centered.X || yPositionOnScreen != (int)centered.Y)
            {
                xPositionOnScreen = (int)centered.X;
                yPositionOnScreen = (int)centered.Y;
                DoLayout();
            }

            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

            string title = !string.IsNullOrWhiteSpace(Layout.SettingsMenuTitleText)
                ? Layout.SettingsMenuTitleText
                : helper.Translation.Get("settings.title").Default("Presence Settings").ToString();
            SpriteText.drawStringWithScrollCenteredAt(b, title, xPositionOnScreen + width / 2, yPositionOnScreen - 18);

            int mouseX = Game1.getOldMouseX();
            int mouseY = Game1.getOldMouseY();

            // Draw Tabs
            string tGen = helper.Translation.Get("settings.tab_general").Default("General");
            string tLin = helper.Translation.Get("settings.tab_lines").Default("Lines");
            string tBtn = helper.Translation.Get("settings.tab_buttons").Default("Buttons");

            DrawTabButton(b, tabGeneral.bounds, tGen, selectedTab == 0, tabGeneral.containsPoint(mouseX, mouseY));
            DrawTabButton(b, tabLines.bounds, tLin, selectedTab == 1, tabLines.containsPoint(mouseX, mouseY));
            DrawTabButton(b, tabButtons.bounds, tBtn, selectedTab == 2, tabButtons.containsPoint(mouseX, mouseY));

            int padLeft = Layout.SettingsMenuListPadLeft > 0 ? Layout.SettingsMenuListPadLeft : 60;
            int padRight = Layout.SettingsMenuListPadRight > 0 ? Layout.SettingsMenuListPadRight : 60;
            int leftX = xPositionOnScreen + padLeft;
            int rightEdge = xPositionOnScreen + width - padRight;
            int controlW = Layout.SettingsMenuControlWidth > 0 ? Layout.SettingsMenuControlWidth : 320;
            int rightX = rightEdge - controlW;
            int contentTopY = yPositionOnScreen + (Layout.SettingsMenuListPadTop > 0 ? Layout.SettingsMenuListPadTop : 88);

            // Tab 0: General (GMCM Image 3 style: Section title, left label 1.0f dialogueFont, right controls)
            if (selectedTab == 0)
            {
                SpriteText.drawString(b, "General Settings", leftX, contentTopY);

                int itemHeight = Layout.SettingsMenuItemHeight > 0 ? Layout.SettingsMenuItemHeight : 46;
                int maxVisible = Layout.SettingsMenuMaxVisibleItems > 0 ? Layout.SettingsMenuMaxVisibleItems : 8;
                int listY = contentTopY + 38;

                int startIdx = currentScroll;
                int count = Math.Min(maxVisible, settingsList.Count - startIdx);

                for (int i = 0; i < count; i++)
                {
                    var item = settingsList[startIdx + i];
                    int itemY = listY + (i * itemHeight);
                    int rowW = width - (padLeft + padRight);

                    bool isHovered = new Rectangle(leftX, itemY, rowW, itemHeight).Contains(mouseX, mouseY);
                    if (isHovered)
                    {
                        b.Draw(Game1.staminaRect, new Rectangle(leftX - 6, itemY + 2, rowW + 12, itemHeight - 4), Color.Wheat * 0.22f);
                    }

                    // Pure 1.0f dialogueFont - 100% sharp pixel font matching vanilla Stardew
                    Utility.drawTextWithShadow(b, item.Label, Game1.dialogueFont, new Vector2(leftX, itemY + 4), Game1.textColor);

                    if (item.IsCheckbox)
                    {
                        bool isChecked = item.GetBoolValue?.Invoke() ?? false;
                        Rectangle srcRect = isChecked ? CheckboxCheckedRect : CheckboxUncheckedRect;
                        b.Draw(Game1.mouseCursors, new Vector2(rightX, itemY + 4), srcRect, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);
                    }
                    else
                    {
                        string optText = item.GetOptionValue?.Invoke() ?? string.Empty;
                        DrawGmcmDropdown(b, new Rectangle(rightX, itemY + 2, controlW, 38), optText, isHovered);
                    }
                }

                if (settingsList.Count > maxVisible)
                {
                    if (currentScroll > 0) btnScrollUp.draw(b);
                    if (currentScroll + maxVisible < settingsList.Count) btnScrollDown.draw(b);
                }

                if (liveConfig.GameNameMode == "custom")
                {
                    Utility.drawTextWithShadow(b, "Custom Game Name", Game1.dialogueFont, new Vector2(leftX, customGameNameBox.Y - 26), Game1.textColor);
                    customGameNameBox.Draw(b);
                }
            }
            // Tab 1: Lines Customizer (GMCM layout)
            else if (selectedTab == 1)
            {
                SpriteText.drawString(b, "Presence Text Lines", leftX, contentTopY);

                int modCount = helper.ModRegistry.GetAll().Count();
                string defPosition = LocationResolver.GetFriendlyLocationName(Game1.currentLocation, helper, liveConfig);

                // Line 1
                int row1Y = contentTopY + 36;
                string l1Label = helper.Translation.Get("editor.line1_label").Default("Line 1 (Details):");
                Utility.drawTextWithShadow(b, l1Label, Game1.dialogueFont, new Vector2(leftX, row1Y + 4), activeLineBox == 1 ? Game1.textColor : Game1.textColor * 0.85f);
                line1Box.Draw(b);

                string p1 = PlayerStatusFormatter.FormatCustomLine(line1Box.Text, Game1.player, liveConfig, helper, modCount, defPosition);
                Utility.drawTextWithShadow(b, "Preview:  " + (string.IsNullOrWhiteSpace(p1) ? "(Empty)" : p1), Game1.smallFont, new Vector2(leftX, row1Y + 42), Color.DarkSlateBlue);

                // Line 2
                int row2Y = row1Y + 76;
                string l2Label = helper.Translation.Get("editor.line2_label").Default("Line 2 (State):");
                Utility.drawTextWithShadow(b, l2Label, Game1.dialogueFont, new Vector2(leftX, row2Y + 4), activeLineBox == 2 ? Game1.textColor : Game1.textColor * 0.85f);
                line2Box.Draw(b);

                string p2 = !string.IsNullOrWhiteSpace(line2Box.Text)
                    ? PlayerStatusFormatter.FormatCustomLine(line2Box.Text, Game1.player, liveConfig, helper, modCount)
                    : PlayerStatusFormatter.GetPlayerInfoState(Game1.player, liveConfig, helper);
                Utility.drawTextWithShadow(b, "Preview:  " + (string.IsNullOrWhiteSpace(p2) ? "(Empty)" : p2), Game1.smallFont, new Vector2(leftX, row2Y + 42), Color.DarkSlateBlue);

                // Available prefixes / tokens above the format boxes
                int tokensHeaderY = yPositionOnScreen + 92;
                SpriteText.drawString(b, "Available prefixes:", leftX, tokensHeaderY);
                Utility.drawTextWithShadow(
                    b,
                    "Hover any token to insert it",
                    Game1.smallFont,
                    new Vector2(leftX + 260, tokensHeaderY + 2),
                    Color.DarkSlateBlue
                );

                int chipX = leftX;
                int chipY = yPositionOnScreen + 118;
                int chipH = 30;
                int chipSpacing = 8;
                int maxWidth = width - (padLeft + padRight) - 12;

                foreach (string token in availableTokens)
                {
                    int chipW = (int)Game1.smallFont.MeasureString(token).X + 18;
                    if (chipX + chipW > leftX + maxWidth)
                    {
                        chipX = leftX;
                        chipY += chipH + 6;
                    }

                    Rectangle chipRect = new Rectangle(chipX, chipY, chipW, chipH);
                    bool hovered = chipRect.Contains(mouseX, mouseY);
                    Color chipColor = hovered ? Color.LightGoldenrodYellow : Color.White;
                    DrawTextureBox(b, Game1.mouseCursors, YellowButtonSourceRect, chipRect.X, chipRect.Y, chipRect.Width, chipRect.Height, chipColor, 3f, false);
                    Vector2 textSize = Game1.smallFont.MeasureString(token);
                    Vector2 textPos = new Vector2(
                        chipRect.X + (chipRect.Width - textSize.X) / 2,
                        chipRect.Y + (chipRect.Height - textSize.Y) / 2
                    );
                    Utility.drawTextWithShadow(b, token, Game1.smallFont, textPos, hovered ? Color.DarkSlateBlue : Game1.textColor);
                    chipX += chipW + chipSpacing;
                }

                if (line1Box.Y < chipY + chipH + 18)
                {
                    line1Box.Y = chipY + chipH + 18;
                    line2Box.Y = line1Box.Y + 76;
                }
            }
            // Tab 2: Buttons Customizer (GMCM layout + Live Discord card)
            else if (selectedTab == 2)
            {
                SpriteText.drawString(b, "Discord RPC Buttons", leftX, contentTopY);

                // Button 1
                int b1ToggleY = contentTopY + 34;
                Utility.drawTextWithShadow(b, "Enable Discord Button 1", Game1.dialogueFont, new Vector2(leftX, b1ToggleY + 4), liveConfig.EnableButton1 ? Game1.textColor : Color.Gray);
                Rectangle src1 = liveConfig.EnableButton1 ? CheckboxCheckedRect : CheckboxUncheckedRect;
                b.Draw(Game1.mouseCursors, new Vector2(rightX, b1ToggleY + 4), src1, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);

                int b1LabelY = b1ToggleY + 38;
                Utility.drawTextWithShadow(b, "Button 1 Text", Game1.dialogueFont, new Vector2(leftX, b1LabelY + 4), liveConfig.EnableButton1 ? Game1.textColor : Color.Gray);
                btn1LabelBox.Draw(b);

                int b1UrlY = b1LabelY + 38;
                Utility.drawTextWithShadow(b, "Button 1 URL", Game1.dialogueFont, new Vector2(leftX, b1UrlY + 4), liveConfig.EnableButton1 ? Game1.textColor : Color.Gray);
                btn1UrlBox.Draw(b);

                // Button 2
                int b2ToggleY = b1UrlY + 44;
                Utility.drawTextWithShadow(b, "Enable Discord Button 2", Game1.dialogueFont, new Vector2(leftX, b2ToggleY + 4), liveConfig.EnableButton2 ? Game1.textColor : Color.Gray);
                Rectangle src2 = liveConfig.EnableButton2 ? CheckboxCheckedRect : CheckboxUncheckedRect;
                b.Draw(Game1.mouseCursors, new Vector2(rightX, b2ToggleY + 4), src2, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);

                int b2LabelY = b2ToggleY + 38;
                Utility.drawTextWithShadow(b, "Button 2 Text", Game1.dialogueFont, new Vector2(leftX, b2LabelY + 4), liveConfig.EnableButton2 ? Game1.textColor : Color.Gray);
                btn2LabelBox.Draw(b);

                int b2UrlY = b2LabelY + 38;
                Utility.drawTextWithShadow(b, "Button 2 URL", Game1.dialogueFont, new Vector2(leftX, b2UrlY + 4), liveConfig.EnableButton2 ? Game1.textColor : Color.Gray);
                btn2UrlBox.Draw(b);

                // Live Discord Mockup Card
                int cardY = b2UrlY + 44;
                int cardW = width - (padLeft + padRight);
                Rectangle cardRect = new Rectangle(leftX, cardY, cardW, 84);
                b.Draw(Game1.staminaRect, cardRect, new Color(43, 45, 49));

                Utility.drawTextWithShadow(b, "PLAYING A GAME", Game1.smallFont, new Vector2(cardRect.X + 14, cardRect.Y + 8), Color.LightGray * 0.8f);
                Utility.drawTextWithShadow(b, "Stardew Valley", Game1.dialogueFont, new Vector2(cardRect.X + 14, cardRect.Y + 22), Color.White);

                int modCount = helper.ModRegistry.GetAll().Count();
                string defPosition = LocationResolver.GetFriendlyLocationName(Game1.currentLocation, helper, liveConfig);
                string p1 = PlayerStatusFormatter.FormatCustomLine(line1Box.Text, Game1.player, liveConfig, helper, modCount, defPosition);
                Utility.drawTextWithShadow(b, !string.IsNullOrWhiteSpace(p1) ? p1 : "Playing Stardew Valley", Game1.smallFont, new Vector2(cardRect.X + 14, cardRect.Y + 48), Color.White);

                string p2 = !string.IsNullOrWhiteSpace(line2Box.Text)
                    ? PlayerStatusFormatter.FormatCustomLine(line2Box.Text, Game1.player, liveConfig, helper, modCount)
                    : PlayerStatusFormatter.GetPlayerInfoState(Game1.player, liveConfig, helper);
                if (!string.IsNullOrWhiteSpace(p2))
                {
                    Utility.drawTextWithShadow(b, p2, Game1.smallFont, new Vector2(cardRect.X + 14, cardRect.Y + 64), Color.LightGray * 0.85f);
                }

                // Discord Buttons on Card (right side)
                int pillW = 190;
                int pillH = 26;
                Rectangle b1Pill = new Rectangle(cardRect.Right - pillW - 14, cardRect.Y + 14, pillW, pillH);
                b.Draw(Game1.staminaRect, b1Pill, liveConfig.EnableButton1 ? new Color(78, 80, 88) : new Color(50, 52, 58));
                string t1 = !string.IsNullOrWhiteSpace(btn1LabelBox.Text) ? $"↗ {btn1LabelBox.Text}" : "↗ Stardew Presence";
                Vector2 s1 = Game1.smallFont.MeasureString(t1);
                Utility.drawTextWithShadow(b, t1, Game1.smallFont, new Vector2(b1Pill.X + (b1Pill.Width - s1.X) / 2, b1Pill.Y + 4), liveConfig.EnableButton1 ? Color.White : Color.Gray);

                Rectangle b2Pill = new Rectangle(cardRect.Right - pillW - 14, cardRect.Y + 46, pillW, pillH);
                b.Draw(Game1.staminaRect, b2Pill, liveConfig.EnableButton2 ? new Color(78, 80, 88) : new Color(50, 52, 58));
                string t2 = liveConfig.EnableButton2 ? (!string.IsNullOrWhiteSpace(btn2LabelBox.Text) ? $"↗ {btn2LabelBox.Text}" : "↗ Button 2") : "( Disabled )";
                Vector2 s2 = Game1.smallFont.MeasureString(t2);
                Utility.drawTextWithShadow(b, t2, Game1.smallFont, new Vector2(b2Pill.X + (b2Pill.Width - s2.X) / 2, b2Pill.Y + 4), liveConfig.EnableButton2 ? Color.White : Color.Gray);
            }

            // Bottom action buttons
            DrawYellowButton(b, btnCancel.bounds, helper.Translation.Get("settings.cancel").Default("Cancel"), btnCancel.containsPoint(mouseX, mouseY));
            DrawYellowButton(b, btnDefault.bounds, helper.Translation.Get("settings.default").Default("Default"), btnDefault.containsPoint(mouseX, mouseY));
            DrawYellowButton(b, btnSave.bounds, helper.Translation.Get("settings.save").Default("Save"), btnSave.containsPoint(mouseX, mouseY));
            DrawYellowButton(b, btnSaveAndClose.bounds, helper.Translation.Get("settings.save_close").Default("Save & Close"), btnSaveAndClose.containsPoint(mouseX, mouseY));

            closeButton.draw(b);
            drawMouse(b);
        }

        private void DrawCheckbox(SpriteBatch b, Rectangle bounds, string text, bool isChecked)
        {
            Rectangle src = isChecked ? CheckboxCheckedRect : CheckboxUncheckedRect;
            int boxY = bounds.Y + (bounds.Height - 36) / 2;
            b.Draw(
                Game1.mouseCursors,
                new Vector2(bounds.X, boxY),
                src,
                Color.White,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                0.9f
            );

            Vector2 textPos = new Vector2(bounds.X + 44, boxY + 4);
            Utility.drawTextWithShadow(b, text, Game1.dialogueFont, textPos, isChecked ? Game1.textColor : Color.Gray);
        }

        private void DrawGmcmDropdown(SpriteBatch b, Rectangle bounds, string text, bool isHovered)
        {
            Color borderColor = new Color(112, 53, 16);
            Color bgColor = isHovered ? new Color(245, 180, 115) : new Color(228, 154, 85);

            b.Draw(Game1.staminaRect, bounds, borderColor);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), bgColor);

            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Vector2 textPos = new Vector2(bounds.X + 12, bounds.Y + (bounds.Height - textSize.Y) / 2f);
            Utility.drawTextWithShadow(b, text, Game1.smallFont, textPos, Game1.textColor);

            int arrowSize = 24;
            int arrowX = bounds.Right - arrowSize - 8;
            int arrowY = bounds.Y + (bounds.Height - arrowSize) / 2;
            b.Draw(Game1.mouseCursors, new Vector2(arrowX, arrowY), new Rectangle(421, 472, 12, 12), Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0.9f);
        }

        private void DrawYellowButton(SpriteBatch b, Rectangle bounds, string text, bool isHovered)
        {
            Color tint = isHovered ? Color.Wheat : Color.White;
            DrawTextureBox(b, Game1.mouseCursors, YellowButtonSourceRect, bounds.X, bounds.Y, bounds.Width, bounds.Height, tint, 4f, false);

            Vector2 sz = Game1.dialogueFont.MeasureString(text);
            float scale = 1f;
            if (sz.X > bounds.Width - 16)
            {
                scale = (bounds.Width - 16) / sz.X;
            }

            float tx = bounds.X + (bounds.Width - sz.X * scale) / 2f;
            float ty = bounds.Y + (bounds.Height - sz.Y * scale) / 2f;

            Utility.drawTextWithShadow(b, text, Game1.dialogueFont, new Vector2(tx, ty), Game1.textColor, scale);
        }

        private void DrawTabButton(SpriteBatch b, Rectangle bounds, string text, bool isSelected, bool isHovered)
        {
            Color tint = isSelected ? Color.White : (isHovered ? Color.Wheat : new Color(175, 175, 175));
            DrawTextureBox(b, Game1.mouseCursors, YellowButtonSourceRect, bounds.X, bounds.Y, bounds.Width, bounds.Height, tint, 4f, false);

            Vector2 sz = Game1.smallFont.MeasureString(text);
            float tx = bounds.X + (bounds.Width - sz.X) / 2f;
            float ty = bounds.Y + (bounds.Height - sz.Y) / 2f;

            Color textColor = isSelected ? Game1.textColor : (isHovered ? Game1.textColor : (Game1.textColor * 0.7f));
            Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(tx, ty), textColor);
        }

        private void DrawTextureBox(SpriteBatch b, Texture2D texture, Rectangle sourceRect, int x, int y, int width, int height, Color color, float scale, bool drawShadow)
        {
            int cornerSize = 3;
            int scaledCorner = (int)(cornerSize * scale);

            b.Draw(texture, new Rectangle(x, y, scaledCorner, scaledCorner), new Rectangle(sourceRect.X, sourceRect.Y, cornerSize, cornerSize), color);
            b.Draw(texture, new Rectangle(x + width - scaledCorner, y, scaledCorner, scaledCorner), new Rectangle(sourceRect.X + 6, sourceRect.Y, cornerSize, cornerSize), color);
            b.Draw(texture, new Rectangle(x, y + height - scaledCorner, scaledCorner, scaledCorner), new Rectangle(sourceRect.X, sourceRect.Y + 6, cornerSize, cornerSize), color);
            b.Draw(texture, new Rectangle(x + width - scaledCorner, y + height - scaledCorner, scaledCorner, scaledCorner), new Rectangle(sourceRect.X + 6, sourceRect.Y + 6, cornerSize, cornerSize), color);

            b.Draw(texture, new Rectangle(x + scaledCorner, y, width - scaledCorner * 2, scaledCorner), new Rectangle(sourceRect.X + 3, sourceRect.Y, 3, cornerSize), color);
            b.Draw(texture, new Rectangle(x + scaledCorner, y + height - scaledCorner, width - scaledCorner * 2, scaledCorner), new Rectangle(sourceRect.X + 3, sourceRect.Y + 6, 3, cornerSize), color);
            b.Draw(texture, new Rectangle(x, y + scaledCorner, scaledCorner, height - scaledCorner * 2), new Rectangle(sourceRect.X, sourceRect.Y + 3, cornerSize, 3), color);
            b.Draw(texture, new Rectangle(x + width - scaledCorner, y + scaledCorner, scaledCorner, height - scaledCorner * 2), new Rectangle(sourceRect.X + 6, sourceRect.Y + 3, cornerSize, 3), color);

            b.Draw(texture, new Rectangle(x + scaledCorner, y + scaledCorner, width - scaledCorner * 2, height - scaledCorner * 2), new Rectangle(sourceRect.X + 3, sourceRect.Y + 3, 3, 3), color);
        }
    }
}

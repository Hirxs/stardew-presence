![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788303872-1707155287.png)

![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788303856-1825108638.png)

# Real Time - Discord RPC Mod & Cloudflare Image Host

Welcome to Stardew Presence... A mod that displays your Stardew Valley save on Discord in real time, not just limiting itself to statistics, but also showing your character and extra content: custom avatars, pet, spouse, stats, cinematics, current location, weather, and more! Plus, you can customize all of this to your liking.

No external dependencies required! Includes Discord's "Invite to party" integration for seamless co-op invites.

### Socials & Support
[My Twitter/X](https://x.com/HyrxsMC) | [My Discord Server](https://discord.gg/eSZmgX8usS)

![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788307580-37793147.png)

## Features

- Dynamic portraits of your farmer with your exact clothes, hats, and seasonal backgrounds
- Display your spouse, pet, and horse together in real-time
- In-game visual editor (F8) to customize positions, frames, scales, and emotes
- Live tracking for locations, mine floors, fishing, cutscenes, and festivals
- Shows your farm name, gold, Qi gems, weather, and in-game date
- Fully compatible with multiplayer and co-op
- Bandwidth friendly with SHA-256 image caching
- GMCM support and translated into English, Spanish, Portuguese, and Chinese
- Compatible with custom content

![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788309792-1990975222.png)
![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788309794-1819641835.png)
![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788309818-1569739481.png)

## How to use

- Press **F8** anywhere in-game to open the Visual Editor
- Switch between Player, Spouse, and Pet tabs
- Use the D-Pad arrows to adjust character positions (hold Shift to move faster in ±5px steps)
- Adjust Scale, cycle through animation Frames, and toggle Flip or Layer
- Pick floating Emotes for each character (*Hearts, Music, Stars, etc.*)
- Use the Season selector to preview different backgrounds or lock your favorite one
- Click **Save and Apply** to update your Discord presence immediately
- Click Settings (or use GMCM) to customize what info you want to display on Discord

![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788309127-7983673.png)
![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788309135-2113405394.png)
![](https://staticdelivery.nexusmods.com/mods/1303/images/51515/51515-1788309170-512814675.png)

## Installation
- Install the latest version of SMAPI
- Download the mod from [Releases](https://github.com/Hirxs/Stardew-Presence/releases) or [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/51515)
- Unzip/copy the mod folder into `Stardew Valley/Mods`. Run the game using SMAPI

---

## Cloudflare Image Hosting & Dynamic Portraits

This repository includes a Cloudflare Pages site and API (`web/` and `functions/`) for hosting your dynamic farmer portraits on Cloudflare R2 with zero bandwidth fees.

### Deploying to Cloudflare Pages via GitHub
1. Push this repository to GitHub.
2. In the Cloudflare Dashboard, go to **Workers & Pages** &rarr; **Create application** &rarr; **Pages** &rarr; **Connect to Git**.
3. Select this repository:
   - **Framework preset**: `None`
   - **Build output directory**: `web`
4. Under project **Settings** &rarr; **Functions** &rarr; **R2 bucket bindings**, add:
   - Variable name: `BUCKET`
   - R2 bucket: Select your bucket (`stardew-presence`)
5. In Stardew Valley `config.json` (or GMCM in-game), configure:
   ```json
   {
     "EnableDynamicFarmerImage": true,
     "CustomUploadUrl": "https://your-site.pages.dev/api/upload"
   }
   ```

### Privacy & Transparency
- **Zero Telemetry**: No tracking cookies, user identifiers, or telemetry are ever sent or stored.
- **Image Data Only**: The API strictly receives rendered character sprites (192x192 PNG) for Discord Rich Presence display.
- **Offline Resilient**: When offline or if no upload URL is set, the mod automatically uses the standard title screen logo without errors or delays.

---

## Versions
- **0.1.0**: First release
- **0.2.0**:
  - Added dynamic in-game map background capture with free-camera selector (F8)
  - Added in-game welcome notification on save load to customize Discord RPC
  - Fixed clothing and accessories not matching character facing directions and animation frames
  - Improved companion and pet rendering, scaling, and floating emotes
  - Optimized image generation and SHA-256 caching to reduce bandwidth
  - Removed obsolete static background assets
- **0.2.5**:
  - Option to disable the F8 startup notification
  - Graceful offline fallback to default title screen logo
  - Cloudflare Pages and R2 image hosting integration

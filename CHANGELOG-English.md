# Version history (English)

Release notes in English, shown on the What's new page when Lotus is in English. The full history (in Portuguese) is in [CHANGELOG.md](CHANGELOG.md).

## 2.0.4-beta "Lotus 1.0.4 Beta" (11/22/2026)

### Improved
- **Real FPS on the Lotus Neon overlay.** The big FPS number now shows how many frames **appear on screen** (RivaTuner's own PresentMon), not how many the game sends. With frame generation, frames are sent in bursts and the old number and graph could mislead. The Framepacer graph and the other numbers are still measured frame by frame to stay fluid; with frame generation the graph may still turn into a solid block at times, which is how RivaTuner measures, while the picture on screen stays smooth.
- **Hint when picking the overlay.** In My Games › Overlay, the Lotus Neon tile says it is the best choice if you play with frame generation.

## 2.0.3-beta "Lotus 1.0.3 Beta" (11/22/2026)

### New
- **Frame generation without G-Sync/FreeSync.** Tick "I use frame generation in this game" and save with Automatic mode. On an NVIDIA card and a monitor without G-Sync/FreeSync, Lotus turns on the **NVIDIA driver's V-Sync for that game only** and **RivaTuner's Reflex limiter just under the monitor's real rate**. With frame generation on, games lock their own V-Sync, and without it frames arrived in pairs with tearing (it looked like half the FPS). Lotus saves how things were first; Restore or Turn off puts RivaTuner and the driver's V-Sync back exactly as before.
- Measured in Gothic 1 Remake (RTX 5060, 75 Hz monitor without G-Sync, walking and turning the camera): **no tearing, a flat frametime line (worst 1% at 13.6 ms) and 59 ms PC latency**. In the same test, the old mode (Reflex without V-Sync) showed paired frames with tearing; scanline sync (tear line off screen) was smooth but at 105 ms; 60 Hz with frame generation had the same stutters and 67 ms.

### Improved
- **Ticking frame generation** on the game's presentation card switches pacing back to Automatic (the only one that works with frame generation on NVIDIA) instead of failing on Save.
## 2.0.2-beta "Lotus 1.0.2 Beta" (11/21/2026)

### New
- **Free memory when a game opens** (Game profile, on by default, switch in "Adjust the Game profile"). Once, when the game opens: idle programs give their RAM back to Windows (the game, the foreground window, Windows, drivers, anti-cheat, Steam/launchers, Discord and OBS are left alone), Wallpaper Engine pauses and only the programs **you** put in "Close before playing" close normally, like clicking X. When the game closes, Wallpaper Engine plays again and those programs reopen minimized. No administrator and no Windows cache purge. The memory freed is actually measured and shown in a notice inside Lotus itself when you come back to it ("In your Stellar Blade session, Lotus freed 2.4 GB of RAM for the game"), and kept in the Tools history.
- **Never shrink.** In Tools › Free memory for gaming, the shield next to each program keeps it out of the memory freeing: it is not trimmed, closed or paused (for example, the music app you listen to while playing).
- **Memory tip in games (16 GB or less).** In "Optimize this game", PCs with 16 GB of RAM or less get a tip to free memory before playing, saying whether the Game profile already does it. It moves up the list when the game recommends more RAM than the PC has. In a Stellar Blade test on 16 GB of RAM, stutters followed free RAM dropping close to zero.
- **Lighter Lotus in the tray.** When it goes to the tray, Lotus gives the memory it is not using back to Windows. Measured on the test PC: from 337 MB to 27 MB in RAM right after (about 80 MB after 30 s). Everything comes back when the window opens.

### Improved
- **Steadier Console Mode.** Without FreeSync/G-Sync, RivaTuner is back just under the real 60 Hz (59.99). With a limit exactly equal to the monitor rate, frames queued behind V-Sync and the framepacer line wobbled. In a Stellar Blade test, 59.99 kept the line flat (1% low 60, 0.1% low 55).
- **One limiter only.** When RivaTuner already paces an Unreal game, applying Lotus settings now turns the game's own FPS limit off (unlimited) instead of setting the same value in both.

## 2.0.1-beta "Lotus 1.0.1 Beta" (11/21/2026)

### Improved
- **Console Mode.** The "60" button on the FPS limit card is now called Console Mode (with the controller icon). On, it sets 60 FPS in the game **and in RivaTuner** (RivaTuner used to stay at 59.99) and brings pacing back to Fluid, like a console: every frame stays on screen for the same time. Without FreeSync/G-Sync, the monitor still switches to 60 Hz only while the game is in front. Turning it off brings back what was there before. If you use frame generation in the game, Lotus keeps the mode that works with it.

## 2.0.0-beta "Lotus 1.0 Beta" (11/20/2026)

### Important announcement
- **Lotus is out of alpha: this is Lotus 1.0 Beta.** Everything built during the alphas (reversible optimization, My Games, per-game graphics settings, Lotus Score, Tools, Save backup and the diary) is now ready for everyday use. Thank you to everyone who tested it!
- **Updates will be further apart.** From now on, new versions come out with more time between them: each one more complete and better tested. Important fixes still come out as soon as they are needed.
- **Your Lotus updates to the beta on its own**, just like before: your games, diary, achievements and settings stay as they are.

### New
- **Animated opening.** A lotus flower drawn in the app itself opens petal by petal, with a golden glow and the logo. Full (about 3 s) the first time and after each update, short (about 1.4 s) the other times. Click or Esc skips it; Settings › "Opening animation" turns it off.
- **Meet Lotus.** A guide with one page for each part of the app (Optimize, My Games, Lotus Score, Tools, Save backup and My diary), each with a short animation, plain text and a button to try it right away.
- **First launch in one place.** In the same guide you pick the language (Português or English), connect your Steam account or choose a name and photo, pick a theme and color (with a live preview) and, if you want, connect Google Drive for save backups.
- **What's new.** After each update, Lotus shows what changed in that version (no internet needed). You can see it again in Settings › "Meet Lotus".

### Fixed
- The yellow warning about switching Hz no longer appears for no reason: not when an administrator window (Task Manager, installers) is in front, and not when the game is already at the right Hz.

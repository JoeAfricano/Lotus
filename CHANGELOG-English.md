# Version history (English)

Release notes in English, shown on the What's new page when Lotus is in English. The full history (in Portuguese) is in [CHANGELOG.md](CHANGELOG.md).

## 2.0.8-beta "Lotus 1.0.8 Beta" (11/28/2026)

### Improved
- **"Turn off SCSKiller" now lives in Optimize this game,** right under the mode cards, with the rest of what helps the game run smoothly (it left the game page).
- **FrameGen explained on the card:** ticking it does not turn the generator on in the game; the text says what Lotus changes and, when a mode is already in use, reminds you to press "Apply again".
- **The card in use shows the limiter really saved** for the game (a game set up earlier may stay on front edge).

## 2.0.7-beta "Lotus 1.0.7 Beta" (11/28/2026)

Fixes and additions from the smoothness study of 11/25–11/28/2026 (Stellar Blade, Wukong and Gothic 1 Remake).

### Improved
- **Default RivaTuner limiter.** With an NVIDIA card the default is now RivaTuner's Reflex mode (the best in Wukong and Gothic in the study). With other cards, front edge. Scanline sync is no longer picked by itself, only by hand. Games already set up keep their limiter, and the per-game measurement can still change it.
- **More hooks found in the game folder.** The card that shows files such as SCSKiller's, ReShade's and mods' now also looks for `d3d9.dll` and `winmm.dll`. Each time you turn one off or back on, it is written to the Tools history with the file and the result. Lotus still never turns anything off by itself.

### New
- **"Optimize this game" screen with mode cards.** The graphics recommendations for your PC sit on top. Right below, in "Choose how you play", there are three large cards: **PC mode** (the screen's highest rate, with RivaTuner just under it), **Console mode** (60 Hz with 59.99) and **Custom** (opens the detailed settings: rate, limiter, straight line or lowest latency and the highest latency). Each card says exactly what it will change on your monitor, and the one in use is marked. "I want to use FrameGen in this game" redraws the cards for frame generation. Choosing a mode asks for confirmation, and nothing changes before that. The cards have a gradient background, Lotus's own illustration (monitor, controller and sliders) and follow the colors of the theme you use, light, dark or custom.
- **Notice when the game's limit is below the screen's rate.** "Why is it stuttering?" now notices when the game is held at an FPS limit below the monitor's rate (like Wukong at 60 on a 75 Hz screen, which repeated about 2,000 frames in 2 minutes) and shows the two measured ways out: remove the game's limit and keep RivaTuner just under the screen's rate, or use Modo Console at 60 Hz. When the card's time per frame was measured, Lotus names one: 75 Hz only when each frame takes under about 13 ms. When it was not, it shows both. The processor's time per frame is not in RivaTuner measurements, so the choice looks at the card only.
- **Limiter chosen per game, by measurement.** No limiter suits every game: RivaTuner's Reflex was the worst in Stellar Blade and the best in Wukong and Gothic. With Modo Console on, Lotus measures the limiter in use once, quietly, during play (RivaTuner's counters, no administrator prompt, nothing changes). When the game closes, it offers to try the other one next time and, after both, shows the result and offers the one with fewer frames off the beat and, on a tie, lower latency. Switching only happens when you press the button.
- **"Straight line" or "Lowest latency" in Modo Console.** In each game you can choose the straightest line (default: RivaTuner just under the screen's rate) or the lowest latency (the game's own limiter, with RivaTuner without a limit). In Gothic, on this PC, lowest latency gave 36 ms instead of 46, with a few more doubled frames. The measured latency of each choice shows next to it once measured.
- **Latency per frame in Measure FPS and "Why is it stuttering?".** In games with Reflex, the result now shows the typical latency (median) and the worst moments (p95). PresentMon recordings now read each frame's latency. You can set the highest latency you accept (on the Modo Console card): Lotus says when a measurement goes above it and never suggests limiters or presets above it.
- **Latency on the Neon overlay (optional).** In My Games › Overlay, "Show latency on the Neon style" adds a "LAT 46 ms" line to the Neon card, right under the frame time, in the same style as the rest. The value comes from the game's Reflex as RivaTuner itself reads it, at no cost to the PC. It measures from reading the controller until the card finishes the frame, without the wait for the screen, so it reads a few ms below PresentMon. A game without Reflex shows "LAT —", never a made-up number. It ships off, and off the Neon stays exactly as it was. The line shows the next time RivaTuner opens.
- **Cause of each hitch from the frame itself.** In PresentMon recordings, each frame above 50 ms is now matched with the time the processor and the card spent on it, plus memory and disk in that second. The split between "processor/game" and "graphics card" is more accurate.

### Fixed
- **Game still "open" after a crash.** When a game crashed and left only its error window (like Gothic 1 Remake's `CrashReportClient`), Lotus thought the game was still running: it showed "Playing now", kept the Game profile and the card tuning on, and saved a diary session hours too long. Lotus now recognizes the game by its real executable and ignores the helper programs in its folder (error window, built-in browser, anti-cheat and the small launcher that only starts the game). When only the error window is left, the session ends when the game closed, everything goes back to normal and Lotus's icon by the clock shows that the game crashed. The error window's time does not count in the diary.
- **Modo Console that sometimes did not kick in.** In 2 launches of Gothic the screen stayed at 74 Hz instead of 60 and the game repeated 1 frame in 4, with nothing in the log. Every rate change is now written to Lotus's log (requested, obtained or undone, with the reason and the exact Hz). After the game appears, Lotus checks the real rate for 10 seconds and asks again if it did not stick or went back, at most 3 times. If another program keeps changing it, Lotus stops (no fight) and says so. The rate only goes back when the game closes: Alt+Tab or an error window in front no longer touch it. When Modo Console should be on and is not, Lotus's icon by the clock shows why, with no balloon over the game.
- **RivaTuner closed after a PC restart.** After a restart, MSI Afterburner may open without RivaTuner, and then the FPS limit and Modo Console do not work. When Lotus sees a game open (even from Steam or a shortcut) that depends on RivaTuner, it now opens RivaTuner, checks that it opened and asks you to close and reopen the game (RivaTuner only works in games opened after it). If Windows does not allow it, Lotus says so and does not ask again for 30 minutes.
- **Notices over the game.** The "Session saved to the diary" notice of one session could stay on screen during the next one. With a game open, Lotus now shows no Windows balloon: notices wait until the game closes and come together in one balloon. When a game opens, a balloon still on screen goes away.
- **Frame recording that stopped without a word.** Recordings stuck in Windows (from a Lotus or a measurement closed midway) used up Windows' recording slots and the next ones recorded nothing. Now, when the last recording got no frames or lost events, or a recording was left open, Lotus first closes the stuck recordings that carry its name (only its own; RivaTuner's and other programs' stay). Only in those cases Windows asks for one more administrator permission. The "no frame in 10 seconds" notice stays. The clock of recordings made with PresentMon 2.6's `--date_time`, which came with the time zone reversed, is now corrected when read (Lotus's own recordings do not use that clock).
- **Frame generation assumed on.** In Gothic, turning the generator on through the config file does not really turn it on, and Lotus still trusted only the "I use frame generation in this game" option. Each measurement (Measure FPS and "Why is it stuttering?") now compares the frames shown on screen with the frames the card really drew (from the game's Reflex). When the option and the measurement disagree, the result and the Modo Console screen say so and what to do. When the game has no Reflex, Lotus does not guess.

## 2.0.6-beta "Lotus 1.0.6 Beta" (11/27/2026)

An update focused on smoother games, with fewer hitches and fewer surprises when you sit down to play.

### New
- **"Turn off SCSKiller" button on the game page.** SCSKiller helps prevent stutter by preparing shaders ahead of time, but it leaves its own file in the game folder that, in some games, can cause small hitches. Now you can turn that file off only in the game you want, with one press, and let the game talk straight to the graphics card. It is **optional**: the card explains when it is worth it, nothing changes until you press it, nothing is deleted, and pressing again restores it. It also works with ReShade, OptiScaler and mods that use the same kind of file.
- **Game pictures in large size.** On the game page (in My Games and the Game list), click a picture to see it large, in high definition, filling the window. Use the arrows for the others, and Esc or a click outside to close.
- **Memory compression in Tools › Windows up to date.** Shows whether Windows memory compression and the SysMain service are on (some "optimizers" turn both off) and offers to turn them on, with your confirmation. With them on, Windows makes better use of RAM and has to fetch less from the disk mid-game, which helps most on PCs with 16 GB or less. Undo restores it exactly.
- **The cause of each hitch in "Why is it stuttering?".** Lotus now shows what most likely caused each hitch: full memory, disk, graphics card at its limit, or processor. You see right away where the problem is and get the right tip to fix it.
- **NVIDIA shader cache in Tools › Drivers.** Shows the driver's shader cache size and warns when it is off or small. A good cache keeps the game from preparing the same shaders again, which cuts stutter. Read only: Lotus changes nothing.

### Improved
- **RivaTuner ready before the game.** Lotus now opens RivaTuner as soon as Lotus itself starts, if you use the overlay, Console Mode or the FPS limit. These features then work even when you open the game from Steam or anywhere else. And if a game opens before RivaTuner, Lotus tells you what to do.
- **More reliable frame measurement.** FPS measurement is steadier, and if something blocks reading the frames, Lotus tells you why instead of showing an empty result.

## 2.0.5-beta "Lotus 1.0.5 Beta" (11/23/2026)

### New
- **Overlay shortcut in Lotus.** In My Games › Overlay, pick the key that shows and hides the overlay in game (Scroll Lock, Pause, Ctrl + F10, Ctrl + F11, Ctrl + Alt + F10, Ctrl + Alt + F11 or Ctrl + Shift + O) without opening RivaTuner or MSI Afterburner. The shortcut is kept by RivaTuner itself, so it works even with Lotus closed. If MSI Afterburner already uses a key for this, Lotus shows which one and does not allow the same key, since one program would undo the other. "None" gives back what RivaTuner had before.

### Fixed
- **Overlay on a PC with a freshly installed RivaTuner.** On a computer where RivaTuner was installed but never opened, "Use" on any overlay style showed "The style could not be changed". RivaTuner only creates its profiles folder and plugin list the first time it opens, and Lotus did not expect that. Lotus now creates what is missing and the style works the first time. Tested on a freshly installed, never-opened RivaTuner 7.3.7.

### Improved
- **One click leaves everything ready.** "Use" on any style (not only the premium ones) turns on RivaTuner's overlay editor, turns the overlay display on, sets the style and opens RivaTuner (or reopens it if it was open). All with **a single Windows prompt**; before, a regular PC could show two (one from Lotus and one from RivaTuner). Undo gives back the settings you had.
- **Clearer messages.** If the Windows permission is declined (when using or undoing a style), Lotus says so and explains what to do, including when the account needs an administrator password. If RivaTuner is old and lacks the overlay editor, Lotus asks you to update it, without asking for permission for nothing.

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

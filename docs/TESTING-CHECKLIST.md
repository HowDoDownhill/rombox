# Release Testing Checklist

Run this before tagging a version. It is deliberately manual — almost everything that has broken in
this project broke at a boundary a unit test would not have crossed: a real emulator process, a real
filesystem, a real controller, a real OS difference.

**Rules**

- Run the whole list on the platform you developed on, and at minimum sections 0, 4, 5, 9 and 11 on
  the other one. Most defects here have been Windows-only or Linux-only.
- Test the **exported build**, not just the editor. `ApplicationRootDirectory` resolves differently
  in each (`ConfigManager.DetermineApplicationRootDirectory`), and `install_scripts/` and `tools/`
  are copied next to the executable only by `build.sh`.
- When a feature is added, add its row to the relevant section in the same commit. A feature with no
  row here is untested by definition.
- A failure means: record the emulator, the system slug, the platform, and the console output. Godot
  prints emulator resolution and installer errors via `GD.PrintErr` — read them before filing.

---

## 0. Build gates

- [ ] `dotnet build` succeeds with 0 warnings, 0 errors.
- [ ] `./build.sh` (or `build.bat`) exports **both** Windows and Linux without errors.
- [ ] `build/<platform>/` contains the binary, the `.pck`, the `data_rombox_*` runtime folder,
      `install_scripts/` and `tools/7zip/`, and nothing else under `tools/` (the developer tools hold
      private test-machine details).
- [ ] Run `build.sh` a second time without deleting `build/` — no nested `install_scripts/install_scripts`.
- [ ] With Godot unavailable (unset `GODOT_BIN`, not on PATH, or a wrong path), `build.bat` and
      `build.sh` stop with a message before deleting anything, and `build-release` makes no zips.
- [ ] Both release zips contain `data_rombox_*/GodotSharp.dll` (a good Windows zip is ~90 MB).
- [ ] `build.sh`/`build.bat` also write `bundled/install_scripts.zip` and
      `build/macos/rombox-macos.zip` (~140 MB). The zip holds one `RomBox.app` with
      `Contents/Resources/data_rombox_macos_arm64/` and `..._x86_64/`, and the `.pck`
      inside it contains `bundled/install_scripts.zip`.
- [ ] Exported binary launches and reaches the login screen.

## 1. First run on a clean profile

Delete (or move aside) `config.cfg`, `games.cache`, `systems.cache`, `emulators/EmulatorMap.json`.

- [ ] Login screen needs host, username, password and API key. An empty field, a wrong password, a
      wrong key and a key from another RomM user each show their own message. A key pasted with a
      stray space or invisible character still signs in; a key with other odd characters shows a
      message saying to copy it again.
- [ ] Loading screen fetches systems, games and firmware without an unhandled exception.
- [ ] Required directories are created (`roms/`, `bios/`, `emulators/`, `saves/`, `downloads/`,
      `assets/*`) and each non-project directory gets a `.gdignore`.
- [ ] `config.cfg` is written and reloads correctly on second launch.
- [ ] Upgrade path: with an existing `EmulatorMap.json`, newly added default platforms appear and
      existing mappings are **not** reordered (`MergeMissingDefaultMappings`).

### Setup wizard

`-- --setup-capture=<png> --setup-step=<Welcome|Look|Source|RomM|Local|Steam|Done>` saves one step as
an image. `-- --setup-keys=a,b,up,down,left,right,stickup,stickdown` sends those as joypad events and
logs what had focus.
The editor skips the Steam step unless `ROMBOX_STEAM_ROOT` points at a Steam folder.

- [ ] A clean profile opens the wizard. A profile already signed in to RomM goes to the library.
- [ ] Controller only: every step can be finished, and down from the last control reaches Next.
- [ ] Login screen, after a failed sign-in: a controller can move between the fields and press Login.
- [ ] Look: Style and Theme change the screen at once.
- [ ] RomM: a wrong address or password shows an error and stays on the step.
- [ ] Local: an ES-DE or EmuDeck folder is found by itself or through Browse, and the count is right.
      Multi-disc games list once. `.bin` Genesis ROMs and `.md` files are listed.
- [ ] Local, Create Console Folders: a new path is created with a folder for each console, Next opens
      an empty library, and a second press adds nothing. A folder already named `megadrive` gets no
      `genesis` beside it. A path ending in a console name, such as `roms/snes`, is refused.
- [ ] Steam, with Steam running: finishing closes Steam, adds the shortcut and restarts through it.
- [ ] Not Now: no Steam or controller prompt appears afterwards.
- [ ] Run Setup Again: Cancel returns to the library. Switching source shows the other library, and
      switching back loads RomM from its cache.
- [ ] Local library: Delete, the installed filter, favorites and netplay are hidden, and a game plays
      from its own folder. A missing folder shows a message and opens an empty library.
- [ ] Local library art: covers, logos and screenshots appear for No-Intro and Redump file names, and
      for names with no region tag. A game with no match keeps its title card. A second start lists
      nothing new. Settings > Game List Settings > Download Box Art off stops new requests.
- [ ] Local library info: a known dump shows its publisher, year and summary with the AI note after
      the selection rests. Scrolling quickly hashes nothing. A second start uses the saved answer.

## 2. Library and browsing

- [ ] System carousel populates; details panel populates; covers load.
- [ ] Cover art, marquees and screenshots download in the background and appear without a restart.
- [ ] "Hide games without box art" and "Show all systems" both take effect.
- [ ] Fuzzy search filters; an empty buffer does not filter.
- [ ] Game View (Carousel, Grid, List) switches immediately, keeps the selected game, and persists
      across a restart. In each view: d-pad and mouse wheel move the selection, clicking selects,
      double-clicking plays or downloads, and switching systems and the Installed filter rebind it.
      In the carousel and grid every card in a system is the same size, including N64 and SNES
      where box shapes are mixed; odd-shaped covers sit inside the frame.
- [ ] Cache rebuild (delete cache files, re-fetch from the loading screen) works.
- [ ] Startup from a warm cache: the `[Startup]` log line shows `auth done` under a second after
      `auth start` on both OSes (5 s there means the IPv6 lookup stall is back), and
      `startup_to_library_ms` stays in line with DESIGN-NOTES "Startup".
- [ ] An old indented `games.cache` is compacted once on load (`Compacted the game cache ...`), and
      the library is unchanged afterwards.

## 3. Downloading

- [ ] Download a ROM: progress bar advances, the downloads page lists it, cancel works.
- [ ] Downloaded ROM lands in `roms/<system_slug>/` and the installed icon appears.
- [ ] A multi-file / archived ROM extracts to the right place.
- [ ] Delete removes the local file and the button returns to "Download".

## 4. Emulator install

Test at least one **archive** emulator (RetroArch) and one **single-file** emulator (any AppImage or
`.exe` with `extract: false`) per platform.

- [ ] Action button reports the right step at each stage: `Install <emulator>` →
      `Installing <emulator>...` → `Download` / `Install Core` → `Play`.
- [ ] The release picker lists releases and installing one succeeds.
- [ ] **After install completes, the button leaves the install state.** If it re-opens the release
      picker, `IsEmulatorInstalled` is failing — check `executable_name` / `executable_regex` in
      `meta.json` against what actually landed in `emulators/<dir>/`.
- [ ] Installed files match the meta: the executable resolves, `emulator_dir_name` is respected.
- [ ] Reinstall over an existing install preserves `preserve_on_reinstall` paths and save data.
- [ ] Install is refused while that emulator is running.
- [ ] Uninstall removes the install and the button returns to `Install <emulator>`.
- [ ] RetroArch only: the `RetroArch_cores.7z` bundle for the same version installs alongside
      RetroArch, so the selected core exists at first launch with no further download. Switching
      core in settings takes effect on the next launch.

## 5. Launch and close

This is the section with the worst platform divergence. Do not skip it on either OS.

- [ ] Launch with a game: the emulator starts, loads the ROM, and BIOS is copied in where required.
- [ ] Launch without a game (start menu) works.
- [ ] Launch args are correct for the system — check `{system}`, `{settings}` and `{core_path}`
      substitution, and that the ROM path stays last where the emulator requires it.
- [ ] **Close hotkey**: hold the configured buttons for the configured hold time (default `Back` for
      2s) while the emulator has focus. The emulator exits. A quick press does nothing. Test with
      the emulator **fullscreen**: a covered frontend runs at about 1 fps on Linux, which is where
      a frame-based hold timer broke.
- [ ] While a game runs the frontend stops drawing: the close-hold log line reports about 60 frames
      for a 2 s hold, and the frontend redraws normally after the emulator exits.
- [ ] Linux: the frontend runs on native Wayland (`run.log` window size equals the panel resolution,
      not a 2x-scaled XWayland size) and falls back to X11 where Wayland is unavailable.
- [ ] The frontend does **not** freeze while closing. Any wait for process exit must be off the main
      thread.
- [ ] The emulator exits *gracefully* — not killed. Confirm by checking the game's save survived (see
      section 6). A hard kill loses in-memory SRAM.
- [ ] No orphaned emulator process is left behind. On Linux verify with
      `pgrep -af <emulator>` after closing; an AppImage runs the real binary in its own session, so
      the process the frontend started is only a wrapper.
- [ ] Changing the close buttons and hold time in settings works, persists to `config.cfg`, and the
      new hold closes the emulator.
- [ ] The frontend regains focus / input after the emulator exits.

## 6. Saves and sync

- [ ] The emulator's save directory is a link into `saves/<emulator>/...` after install and launch.
- [ ] Play, save in-game, exit via the close hotkey — the save file appears in the central store.
- [ ] Save sync to RomM runs after exit and uploads only real save data, never preserved config.
- [ ] The play session appears under `GET /api/play-sessions` with the right ROM and duration.
- [ ] Clear `DeviceId` in `config.cfg` and relaunch: RomM returns the **same** device for this
      machine (`GET /api/devices` gains no new row), and the device has a hostname, on both OSes.
- [ ] Switching a system to a different emulator warns that saves are not converted.
- [ ] A game with both an untagged and an emulator-tagged save on RomM downloads only the tagged
      one (`Skipping untagged save ...` in the log); a game with only untagged saves still gets them.
- [ ] With the RomM server unreachable at launch, exiting a RetroArch game uploads only that game's
      changed save. Other games' saves in the same folder stay out of the upload.
- [ ] Reinstalling the emulator does not destroy saves or save states.
- [ ] Save states land inside the install directory, not in a per-user location outside it.

## 7. Controller

- [ ] Controller is detected at startup and after hot-plug.
- [ ] Navigation, select, back, settings, downloads page, delete, cancel all respond.
- [ ] Controller icons render for the connected pad.
- [ ] Button prompts are positional for every controller: face buttons show the filled position
      (Play is bottom, Back is right, Delete is left, Downloads is top), Options shows the Xbox,
      PlayStation and Nintendo Start symbols side by side, and the bar still fits at every interface
      size. Using the keyboard hides the prompts.
- [ ] Emulator controller config is written only when a controller is actually detected, and an
      existing device line is never overwritten.
- [ ] Face buttons map positionally (South/East/West/North), not by Xbox letter.

## 8. Settings and UI panels

- [ ] Every settings section opens, renders its fields, and persists changes to `config.cfg`.
- [ ] Per-emulator settings fields write `user_settings.json` and change the launch arguments.
- [ ] Linux, RetroArch: the log shows `Applied retroarch setting video_driver = vulkan` at launch,
      a GB/GBA game holds full speed on a hybrid laptop, and switching Video Driver to OpenGL
      takes effect on the next launch. The setting does not appear on Windows.
- [ ] RetroArch: holding Start for 2 s opens RetroArch's menu (the "Open RetroArch Menu With"
      default), changing the setting takes effect next launch, and Quit RetroArch from the menu
      ends the session: `run.log` shows `[Emulator] session ended for ...` and the play session
      reaches RomM.
- [ ] Preferred emulator and preferred core per system persist and take effect.
- [ ] BIOS/firmware selection per system persists.
- [ ] Panel open/close, section transitions, and the panel stack behave with both mouse and controller.
- [ ] Theme and background changes apply. Mesh, Silk and Horizon animate smoothly at the
      display's native resolution (4K on the laptop) with no banding in the dark areas.
- [ ] Style: switching Glass and Simple applies at once, and Background Style shows only under
      Glass. `-- --ui-redraw-probe --ui-capture-style=Simple` logs `0 frames drawn` (Glass logs
      dozens).
- [ ] Simple: the selected game has an accent ring in the carousel, grid and list. In the grid and
      list it glides to the next game when the selection moves.
- [ ] Light theme under both styles: text, system logos, controller glyphs, switches and the
      selection highlight are readable in the game list, settings, start menu, downloads and the
      system-jump popup.
- [ ] Game Details, Large Image and Box Art: switching applies at once and keeps the selected game.
      Screenshots rotate every 5 seconds. A game with no art still lays out cleanly on both.
- [ ] Start > Open ROMs Folder opens the file manager on the local games folder, or on `roms` with
      RomM. A missing folder shows a message. The button is hidden on Android.
- [ ] Interface Sounds: moving, Select, Back and changing system each make a sound; startup and a
      library reload make none; turning the setting off silences them at once.
- [ ] Interface Sounds stay silent when pressing buttons while a game is running or another program is in front.
- [ ] Text follows the four styles: settings, descriptions, start menu and card titles are the same
      Body size; no new `font_size` override in scenes or code (`grep -rn "theme_override_font_sizes\|AddThemeFontSizeOverride"`).
- [ ] Interface Size: each value applies immediately and persists; at 130% and 150% on a 1280×800
      window the button bar, settings sidebar and start menu still fit. Auto logs its choice as
      `[UI] interface size` and picks 130% on a Steam Deck or other screen 900 px or less tall.
- [ ] The selection highlight glides between items with keyboard, controller and mouse in settings
      (both columns), the start menu, the downloads page and the system-jump popup, and fades out
      when a list loses focus. System-jump tiles leave clear space around the logo and name.
- [ ] The UI fills the screen with no black bars at 16:9, 16:10, 21:9 and 4:3. Capture each with
      `-- --ui-capture=<png> --ui-capture-size=WxH` (add `--ui-capture-view=settings|downloads|start`
      for the other screens): carousel cards keep their size, the details banner fits its row, and
      the summary is not clipped.
- [ ] `-- --ui-bench=<report>` finishes with every phase's end state showing the input landed, no
      frame over 33 ms, and no more than a couple over 16.7 ms in any phase. Compare against the
      table in DESIGN-NOTES "Measure with `--ui-bench`".
- [ ] Bottom-bar and changelog buttons show controller glyphs (A, B, X, Y, Start) left of their
      labels after any controller input, and hide them again after moving the mouse.

## 9. Cross-platform traps

These are the failure modes this project has actually shipped. Check them whenever you touch paths,
`meta.json`, or process handling.

- [ ] **Path casing.** Linux is case-sensitive. Every `res://` path must match the real name exactly.
      Sweep with a script that resolves each `res://` reference against the filesystem — a lowercase
      `scripts/autoloads/` silently killed four autoloads and emptied the carousel and details panel.
- [ ] **Executable resolution.** The same emulator can ship a directory tree on one OS and a single
      AppImage on the other. If a recipe extracts *and* declares a literal `executable_name`, verify
      the file it names actually exists after install.
- [ ] **Bundled tools over PATH tools.** Archive extraction must use `tools/7zip/<platform>/`, never
      a system `7z` — it will not be installed on a clean machine.
- [ ] **Executable bits.** Anything shipped or extracted that must run needs `chmod +x` on Unix; zip
      does not preserve the bit.
- [ ] **Windows-only .NET APIs.** `Process.CloseMainWindow()` returns `false` and does nothing on
      Unix. `Process.Kill()` does not reach reparented children. Registry, drive letters, `cmd.exe`
      and directory junctions are all Windows-only branches.
- [ ] **Editor vs exported paths.** Anything writing to `ApplicationRootDirectory` writes into the
      repository when run from the editor. Guard destructive startup work with `OS.HasFeature("editor")`.
- [ ] **Line endings and file modes** in git are clean after a full run of the app.

## 10. Netplay

Needs two machines. `--netplay-host` and `--netplay-join=<address>` (after a bare `--`) drive the
lobby from the command line, so one side can be the Linux test box (`docs/LINUX-TESTING.md`).

- [ ] Host is offered only when the play button would launch the game. A game that still needs a
      download or an emulator install says so instead.
- [ ] LAN: the joiner discovers the lobby, the roster shows both players, and readiness updates when
      the other player's ROM finishes downloading.
- [ ] Join code: copying it works, and the joiner connects with it.
- [ ] Internet: UPnP maps **both** the lobby port and the emulator's netplay port, and the code
      advertises the public address only when both mappings succeeded.
- [ ] Start launches both emulators connected. Test RetroArch and Flycast (GGPO) at minimum.
- [ ] Flycast: player two's inputs, including the analog stick, reach the host.
- [ ] After the session, a plain launch of any game carries no `--host` / `--connect` arguments.
- [ ] The joiner's save is **not** uploaded after a session. The host's is.
- [ ] Closing or cancelling the lobby returns to the previous browse view, with controller focus.

## 11. Input layer

- [ ] Linux: startup logs no `InputLayer` exception.
- [ ] Windows first launch asks once for a Controller Setup method (Steam Input, Manual), with Steam
      Input recommended. Outside Steam the Steam Input choice reads `Add to Steam and Use Steam
      Input`. The answer persists and shows in **Settings > Input Settings > Controller Setup**.
      Linux never asks.
- [ ] A config saved with `ControllerMappingConsent="accepted"` (the retired ViGEm method) shows the
      first-launch prompt again.
- [ ] Started outside Steam with Steam installed (Windows and Linux), the startup offer **Play
      RomBox through Steam?** appears before the Controller Setup prompt, with the full text visible
      without scrolling. Not Now and Back go on to the Controller Setup prompt and the offer returns
      next launch; Don't Ask Again stops it for good
      (`OfferSteamLaunch=false` under `[Steam]` in `config.cfg`).
- [ ] Add to Steam and Restart with Steam closed: `shortcuts.vdf` gains a `RomBox` entry pointing
      at the running executable; `grid/` gains `<appid>p.png`, `<appid>.png`, `<appid>_hero.png`,
      `<appid>_logo.png` and `<appid>_icon.png`; `localconfig.vdf` has
      `"UseSteamControllerConfig" "2"` under `apps/<appid>`; both files have a `.rombox-backup`. Steam
      starts, RomBox opens from it with artwork in the library, and the first copy quits.
- [ ] The same with Steam running: Restart Steam closes Steam, then the steps above run. Not Now
      leaves Steam and its files untouched.
- [ ] In the Steam-started copy, **Properties > Controller** shows Steam Input enabled, the offer
      does not appear, and the Steam Library row reads `Running Through Steam`.
- [ ] Already in the library and started outside Steam: the offer reads **Start RomBox through
      Steam?** and Restart Through Steam relaunches without restarting Steam.
- [ ] Move the RomBox folder: the offer reads **Fix RomBox's Steam shortcut?**, and fixing it
      rewrites the same entry (same appid, play time kept) to the new path.
- [ ] Updating from 1.2.2 through `romm-frontend-windows.zip`: the app comes back as `rombox.exe`
      and the `romm-frontend.*` files and `data_romm-frontend_*` folder are gone a few seconds
      later. With a Steam shortcut to `romm-frontend.exe`, they stay until the shortcut is fixed.
- [ ] A download in progress: Add to Steam explains that downloads have to finish first.
- [ ] With no Steam install, no offer appears, the Steam Library row reads `Steam Not Found`, and
      Add to Steam explains what to do.
- [ ] Steam Input chosen, started outside Steam: the status reads `Start the App From Steam to Use
      It`, **Start > Assign Controllers** is hidden, and a launch logs `not starting a session: the
      app was not launched through Steam`.
- [ ] Steam Input, started from Steam: the launch log's session line ends `, Steam Input`, and
      **Start > Assign Controllers** is hidden.
- [ ] Steam Input, four pads in Dolphin: each pad drives its own port. Turning one pad off between
      launches moves the later players up.
- [ ] Dolphin ports read `XInput/<slot>/Gamepad`, and the face buttons in each port read
      `Button A/B/X/Y`.
- [ ] Linux: Dolphin launches with `-u .../emulators/dolphin/User`, its ports read
      `SDL/<n>/<pad name>`, and a Switch Pro plus an Xbox pad each drive their own port.
- [ ] Linux: after Assign Controllers, turning one assigned pad off keeps the others in order (the
      log shows `player order is now [...]`).
- [ ] Linux, launched from a Steam shortcut on a Wayland desktop with Steam Input on: the log shows
      `Relaunching on display driver x11`, the relaunched app has no Wayland warnings, and a
      Switch Pro drives the menus. Without Steam, the app stays on Wayland.
- [ ] Linux, Steam shortcut with Steam Input on and two pads: **Start > Assign Controllers** is
      hidden, the launch log's session line ends `, Steam Input`, and in Dolphin and gopher64 each
      pad drives its own port in connection order. With Steam Input off for the shortcut, the button
      shows and the session line has no `Steam Input`.
- [ ] Linux, RetroArch with `XDG_CONFIG_HOME` exported by the session (Omarchy does): menu icons
      show, and `--verbose` reports the pad as configured from the AppImage home's autoconfig.
- [ ] gopher64 (Windows under Steam, and Linux): starting from a config with no assignments, the log
      lists the pads (`gopher64 lists N controller(s)`), each player gets a port, and only player
      ports are enabled in `portable_data/config/config.json`. Each pad drives its own player.
- [ ] Manual: launching a game writes no `[InputLayer]` binding lines and leaves emulator
      controller configs unchanged.
- [ ] **Start > Assign Controllers** keeps the start menu its usual width while it shows
      `Player N: Press a Button`, at every interface size.
- [ ] Under Steam Input, each emulator gets every player in player order. Check at least RetroArch,
      Dolphin, PCSX2, Flycast, ares and mGBA.
- [ ] `--controller-report` writes a report whose checks all read OK under Steam.
      The full grid lives in `CONTROLLER-TEST-MATRIX.md`.
- [ ] Dolphin ports 2 to 4 receive port 1's bindings and are switched on in `Dolphin.ini` when a
      player is assigned.
- [ ] RetroArch under Steam Input: `retroarch.cfg` has `input_joypad_driver = sdl2`, and each pad
      drives its own player.
- [ ] Two pads give two players with no double input (PCSX2 player 2 is the known trap).
- [ ] The frontend UI ignores the controller while an emulator is running.
- [ ] Unplugging and re-plugging a pad mid-game does not reorder players.

## 12. Android

- [ ] Fresh install: the login screen appears, logging in loads the library, and the second launch
      reaches the library from the cache in a few seconds (`files/games.cache` exists).
- [ ] Grid is the default view on a fresh install, on every platform.
- [ ] Swiping up and down moves through games in all three views; a flick carries further; a tap
      selects; a double tap on the selected game plays it.
- [ ] Settings: the Back button and the system back gesture both return to the game list; the back
      gesture on the game list leaves the app open.
- [ ] No controller-mapping offer, and no Automatic Controller Mapping or Prefer Discrete GPU rows.
- [ ] Games without cover art show a dark card with a readable title.
- [ ] Installing an Android-ready emulator (Flycast) downloads its APK and opens the system
      installer; the first time, Android asks to allow installs from RomBox. After installing
      and returning to the app, the button changes from "Install Flycast" to "Download".
- [ ] Downloading a Dreamcast game saves it to `Documents/RomM/roms/dc`; the button becomes Play.
      The first Play explains folder access and opens the picker; choosing `Documents/RomM` starts
      the game in Flycast, and later games start without asking.
- [ ] Installing an emulator with an APK recipe downloads the APK and opens the system installer,
      even with Google Play on the phone; returning after the install shows Download or Play.
- [ ] DuckStation and Snes9x EX+ (no APK) open their Play listings, and Update on an emulator that
      was installed from Play opens Play.
- [ ] Defaults on a fresh install: Dreamcast Flycast, N64 gopher64, DS melonDS, PSP PPSSPP, 3DS
      Azahar, GameCube Dolphin, PS2 ARMSX2, PS1 RetroArch with PCSX ReARMed, SNES RetroArch.
- [ ] A PS1 game starts in RetroArch with PCSX ReARMed and no BIOS in RetroArch's system folder.
- [ ] GameCube (Dolphin), PS1 (DuckStation), DS (melonDS), PSP (PPSSPP) and 3DS (Azahar) games
      start in their standalone apps.
- [ ] N64 (gopher64), SNES (Snes9x EX+) and PS2 (ARMSX2, after choosing a BIOS in ARMSX2) games
      start in their standalone apps, and the buttons name ARMSX2 and Snes9x EX+.
- [ ] A system's settings on Android show no emulator settings fields (no Start in Fullscreen for
      RetroArch, no gopher64 or PCSX2 options).
- [ ] The first GBA, SNES or N64 Play explains which RetroArch core to install; Open RetroArch opens
      it and the prompt returns until I've Installed It, which starts the game with that core.
- [ ] A system's settings show only emulators with an Android build, with the one games launch in
      selected (N64 shows RetroArch, not gopher64).
- [ ] For a RetroArch system, **Installed in RetroArch** under Core matches the prompt's record;
      turning it off makes the next Play show the core prompt again, and changing the core updates it.
- [ ] With RetroArch Plus from Google Play installed, the button reads Replace RetroArch and opens the
      system uninstaller; Install RetroArch then installs the libretro build, which asks for All files
      access on first launch.
- [ ] Downloads from IPv6-capable hosts (the libretro buildbot) succeed on a network without IPv6.

## 13. macOS

Run on an Apple Silicon Mac and an Intel Mac. Unzip `rombox-macos.zip` from the build, move
`RomBox.app` to /Applications, and keep Console.app or `log stream` open for the app's
output. The data folder is `~/Library/Application Support/RomBox`.

- [ ] The first open is blocked as unverified; **Open Anyway** in System Settings > Privacy &
      Security opens it, and later opens start straight away.
- [ ] Activity Monitor shows the app as Apple on Apple Silicon (no Rosetta) and Intel on Intel.
- [ ] The data folder holds `config.cfg`, `install_scripts/` (12 emulators), `emulators/`, `roms/`,
      `saves/`, `bios/`, `downloads/` and `assets/`. Nothing is written inside the `.app`.
- [ ] Login, library load and the second start from the cache work as on Windows.
- [ ] Settings shows no Controller Setup, Steam Library or Prefer Discrete GPU rows, and there is no
      first-run controller prompt.
- [ ] A downloaded multi-file game unpacks into `roms/<system>/`.
- [ ] Each emulator installs, starts with a game, and closes back to the app. After closing, its
      saves are under `saves/<emulator>/` and its settings under `emulators/<emulator>/`; check
      that nothing new appeared in `~/Library/Application Support/<Emulator>` or `~/Documents`:
      - [ ] RetroArch (disk image). The first SNES and N64 games download their cores into
            `emulators/retroarch/cores/` (`.dylib`); the menu shows its icons.
      - [ ] Dolphin (disk image). Video Backend defaults to Metal; Vulkan and OpenGL also start.
      - [ ] mGBA (disk image). `emulators/mGBA/config.ini` is the one in use.
      - [ ] DuckStation. Settings and memory cards are under
            `emulators/duckstation/home/Library/Application Support/DuckStation/`, and Internal
            Resolution in the app's settings changes the game.
      - [ ] PCSX2 (`.tar.xz`, versioned bundle name). `inis/PCSX2.ini` and `memcards/` are in
            `emulators/pcsx2/`.
      - [ ] PPSSPP. The memory stick is `emulators/ppsspp/home/.config/ppsspp/`.
      - [ ] Flycast. `emu.cfg` and VMUs are under `emulators/flycast/home/Library/Application Support/Flycast/`.
      - [ ] melonDS. `emulators/melonDS/portable/melonDS.toml` is the one in use.
      - [ ] Azahar. `emulators/azahar/user/` is the one in use.
      - [ ] ares. `emulators/ares/settings.bml` is the one in use and saves land in `saves/`.
      - [ ] gopher64 (Apple Silicon only). Config and saves are under
            `emulators/gopher64/home/Library/Application Support/gopher64/`.
- [ ] On Intel, N64 offers RetroArch only (no gopher64), and SNES on either Mac offers RetroArch
      only (no snes9x).
- [ ] Reinstalling or updating an emulator replaces its `.app` (one bundle left, PCSX2 included)
      and keeps its settings and saves.
- [ ] A save made on the Mac uploads to RomM, and a save from another machine downloads into the
      emulator.
- [ ] Netplay hosts and joins with a Windows or Linux machine.
- [ ] The in-app update replaces `RomBox.app` and reopens it; with the copy made to fail
      (for example the app folder made read-only), the old app stays and still opens.

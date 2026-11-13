RomBox - Version 1.3.91 Changelog (test build)

🔧 Changes

    Nintendo 64 Buttons (gopher64): The B button on your controller is now B in the game. It was on X, and B did nothing. RomBox adds its own button layout to gopher64 the next time you start or host an N64 game. If you made your own layout in gopher64, RomBox leaves it as it is.
    Sharper Picture When Hosting: The picture now gets the full 10 Mbit/s. A fault had it running at about a quarter of that.
    Smoother Over Uneven Connections: A guest now holds picture and sound for a few extra milliseconds when the connection delivers them in clumps, and stops drawing the library behind the game.

---

RomBox - Version 1.3.90 Changelog (test build)

This is a test build of play-together by streaming. Expect rough edges, and please say what you find.

✨ New Features

    Play Together by Streaming: Host Session now streams the host's game to everyone who joins. Guests need only RomBox: no game files, no emulator, and it works for every console. The host picks a game and presses Start Game, and each guest sees and hears the game and plays with their own controller.
    Guests' Controllers: The host's computer plugs in a controller for each guest before the game starts, so the game sees every player. A guest's row in the lobby shows how many controllers they have.
    Picture and Sound That Adapt: The picture is sent at 60 frames a second. On an uneven connection the guest holds picture and sound for a few extra milliseconds so motion stays smooth.

🔧 Changes

    Sessions Stream Now: Host Session and Join Session no longer use the emulators' own netplay. Hosting and joining also work with games in your own folders.
    Hosting on Windows Needs a Driver: The first time you host, RomBox explains the ViGEmBus driver and asks before installing it. Without it guests can watch and listen but can't play. Guests don't need it.
    Wide Screens: A game shown on an ultrawide monitor is streamed as its middle 16:9, so guests don't get bars on every side.

⚠️ Known Limits

    Hosting is tested on Windows. Hosting on Linux streams the whole screen and has not been tested. macOS can't host or join yet.
    Joining on Linux needs FFmpeg installed (the ffmpeg package).
    Playing across the internet needs the host's router to open ports 55440 and 55450. RomBox asks the router to do it; if the router refuses, forward both ports (UDP) to the host.
    A lost connection can freeze or smear the picture for up to a second.
    The host's bitrate is 10 Mbit/s. To change it, set BitrateKbps under [Streaming] in config.cfg.

---

RomBox - Version 1.3.1 Changelog

✨ New Features

    Get Test Builds Early: A new switch in Settings > General Settings. Turn it on and RomBox also offers test builds, which have new features before they are finished and may have problems. It is off unless you turn it on. Turn it off while you have a test build and RomBox offers to put back the latest finished release.

---

RomBox - Version 1.3.0 Changelog

✨ New Features

    New Name: RomM Frontend is now RomBox. Updating renames the app's files for you and keeps your games, saves and settings. If you start it from a Steam shortcut, RomBox offers to fix the shortcut.
    Setup Guide: A new install now walks you through the look of the app, where your games are, and Steam. It works with a controller. To go through it again, choose Settings > General Settings > Run Setup Again.
    Games in Your Own Folders: RomBox can now show games already on your computer or a network share, with no RomM server. It reads the folder layouts from ES-DE, EmuDeck and RetroArch. Starting from nothing? Setup can create a folder for each console. Save sync, collections and netplay need RomM for now.
    Art and Game Info for Local Games: Box art, logos and screenshots download from libretro's thumbnail collection. The publisher and year come from Hasheous, using a code made from the game you select. Both can be turned off in Game List Settings.
    Two Game Details Layouts: Large Image shows the game's screenshots across the panel, changing every 5 seconds. Box Art shows the cover and a list of facts beside a screenshot. Choose one in Settings > Game List Settings > Game Details. With a RomM library, choose Start > Refresh All Games once to show each game's year and company.
    Open ROMs Folder: A new Start menu option shows your games folder in your file manager.
    Simple Style: Flat colours that redraw the screen only when something changes, which is easier on laptops and handhelds. Choose it in Settings > General Settings > Style.
    Light and Dark Themes: Two new themes, and Dark is the default on new installs.
    Interface Sounds: Short sounds when you move, select, go back and change system. Turn them off in General Settings.
    Add to Steam: On Windows and Linux, RomBox can add itself to your Steam library with artwork and Steam Input turned on, then restart from Steam. That brings controller support in every emulator, Game Mode, the overlay and Remote Play. RomBox offers this at startup, and the same button is in Settings > Input Settings.

🔧 Changes

    Controller Setup (Windows): The ViGEm option is gone. If you were using it, RomBox asks you to choose again at startup.
    Signing In: RomM sign-in now needs your username, password and an API key. If you signed in with only a password, RomBox shows the login screen after updating. Make a key in RomM under your profile, then Client API Tokens.

🐛 Bug Fixes

    Login Screen With a Controller: A controller can now move between the fields and press Login.
    Text Boxes: The highlight on a selected text box now goes all the way around it.
    Pasted API Keys: A key copied with a stray space or invisible character failed to sign in with "Request headers must contain only ASCII characters". It now signs in.

---

RomM Frontend - Version 1.2.2 Changelog

🐛 Bug Fixes

    RetroArch on Omarchy and Similar Setups: RetroArch showed no menu icons and didn't recognise controllers when the desktop session sets its own config folder (XDG_CONFIG_HOME). It now always uses the files installed with it.

---

RomM Frontend - Version 1.2.1 Changelog

🐛 Bug Fixes

    Dolphin Controllers on Linux: Dolphin ignored its settings on Linux and used keyboard and mouse for every player. It now uses the app's settings and saves folder, and the app points each player's port at their controller, so any controller works.
    Steam Input on Linux: Controllers gave no input when the app was started from Steam on a Wayland desktop such as Omarchy or GNOME. The app now runs through X11 there, which Steam needs to send it controller input.
    gopher64 Controllers: gopher64 used the keyboard unless a controller was assigned by hand. The app now assigns each player's controller to a port on every launch, on Windows and Linux.

---

RomM Frontend - Version 1.2.0 Changelog

✨ New Features

    Android (in development): A phone build with touch controls. It installs emulators from Google Play where possible, downloads games to Documents/RomM, and launches them in Flycast, Dolphin, DuckStation, melonDS, PPSSPP, Azahar or RetroArch.
    Controller Setup Choice (Windows): First startup now asks how to set up controllers: Steam Input (when you start the app from Steam), ViGEm (when you start it on its own) or Manual (set up each emulator yourself). Change it in Settings > Input Settings. With Steam Input, Steam sets player order. A new Controller Setup guide covers each method.
    Universal Button Prompts: Prompts now show button positions (top, right, bottom, left) for every controller, and Start shows the Xbox, PlayStation and Nintendo symbols together, so they're right whatever pad you hold.
    Controller Report: Starting the app with --controller-report writes a file showing what every emulator will see from each controller, for diagnosing mapping problems.

🐛 Bug Fixes

    Four Players With Xbox Controllers (Windows): Xbox controllers now go straight to the emulator, and only other pads (Switch Pro, PlayStation) use a virtual controller. Three or more Xbox pads used to run out of Windows' four controller slots, leaving players with dead controllers.
    Controllers in Dolphin Beyond Player One: Ports 2 to 4 now get working bindings and are switched on, so multiplayer GameCube games see every player.
    Mixed Controllers in PCSX2 and DuckStation: Player one could be pointed at the wrong device when a non-Xbox pad was connected alongside Xbox pads.
    RetroArch Controller Mapping: RetroArch now reads controllers through its SDL driver, so each player gets their own pad when Xbox and other controllers are mixed. Before, players could get no controller or someone else's.
    Player Order When a Controller Drops Off: An assigned order no longer resets when one pad disconnects. The players after it move up, and the pad rejoins at the end when it reconnects.
    Dolphin Player Order: Dolphin now finds each player's pad by its Xbox controller slot, so player order holds when several pads share a name, and Dolphin launches about two seconds sooner.
    Assign Controllers Button: The button's prompt no longer stretches the start menu while waiting for each player's button press.
    Controllers Waking Mid-Game: An Xbox controller that went to sleep and woke during a game stopped working in the app's menus afterwards.
    Save Upload When RomM Is Unreachable: If the server was down when a game launched, exiting it could upload every save in the emulator's folder as that game's save.
    Downloads on Networks Without IPv6: Emulator and asset downloads from servers with an IPv6 address failed with "Network is unreachable" on networks with no IPv6 route, instead of falling back to IPv4.

---

RomM Frontend - Version 1.1.0 Changelog

This release covers everything since 1.0.13, including the netplay and Linux work that shipped in preview form as 1.0.14 to 1.0.16.

✨ New Features

    Three Game Views: Choose Carousel, Grid or List in Settings > Game List Settings > Game View. Grid, now the default, shows a wall of covers; List shows one game per line and loads fastest.
    Netplay: Host a session from the start menu and share its code, or join one. The lobby picks the game, checks everyone has the ROM and the right emulator, and launches everyone together. Works across the internet through UPnP on RetroArch systems and Flycast (Dreamcast, with GGPO rollback).
    Controller Layer (Windows): Every emulator now sees the same virtual controller, whatever pad is in your hands, so bindings stop breaking when you swap controllers. Set player order by pressing a button on each pad. The app offers to install the driver it needs, once.
    New Looks: Three new backgrounds (Mesh, Silk, Horizon) and seven new themes (Midnight, Obsidian, Amethyst, Glacier, Bordeaux, Jade, Graphite). Every background now has fine grain that removes colour banding.
    Interface Size: Scale all text and controls from 85% to 150%. Auto picks a larger size on small screens such as the Steam Deck.
    RetroArch Controls: Choose which controller button opens the RetroArch menu, and which video driver it uses (Vulkan by default on Linux).

💄 Quality of Life Improvements

    Visual Refresh: A new type system with consistent sizes and weights, frosted panels, drawn navigation arrows and rounded progress bars. Selections share one highlight everywhere, and it glides between items instead of jumping.
    Even Game Cards: Every card in a system is now the same size, with covers fitted inside.
    Any Screen Shape: The interface fills ultrawide, 16:10 and handheld screens instead of letterboxing, and runs borderless fullscreen.
    Faster: Startup on Linux dropped from 9.5 s to 1.8 s, and scrolling and system switching no longer stutter on large libraries.
    Linux: Runs natively on Wayland, and stops drawing behind a running emulator to free up the GPU.
    Mouse Support: Click a game to select it, double-click to play it, and drag to scroll in any view.
    Tidier Details: Emulators show their proper names, game summaries no longer show HTML codes, and the API key field is masked.

🐛 Bug Fixes

    Wrong Card Sizes After Switching Systems: Game cards could stay at the wrong size after switching systems until you moved to another game.
    Settings Buttons: The Back and Select buttons at the bottom of the settings screen did nothing when clicked.
    Unreadable Titles Without Cover Art: Games without cover art showed a white card that hid their title.
    Missing Button Prompts: The bottom bar and update dialog never showed which controller button does what. They now show A, B, X, Y and Start icons whenever a controller is in use.
    Duplicate Devices in RomM: Linux installs registered a new device with RomM on every launch.
    Emulator Close Hotkey on Linux: Holding the close combination did nothing on Linux.
    Broken Downloads: Cover art and screenshots could download twice at once or end up truncated.
    Stale Saves Returning: An old save without an emulator tag could be downloaded again next to the current one.
    Lost Firmware Path: A moved or deleted BIOS file was still treated as present.
    Launching Twice: Pressing play repeatedly could start a game more than once.
    Raw Markup in Prompts: Some on-screen prompts showed formatting tags instead of formatted text.
    Release Packages: Release zips now contain only the app's own files.

---

RomM Frontend - Version 1.0.13 Changelog

✨ New Features

    Central Save Store: Every emulator's saves now live together in one `saves` folder instead of being scattered inside each emulator's install directory. Each emulator's save folder becomes a link into that store, so the emulator writes exactly where it always did. Reinstalling or uninstalling an emulator no longer risks your saves, and one folder is now the whole backup.
    RetroArch Support: RetroArch is now an installable emulator covering 41 systems, 27 of which had no emulator at all before, including Saturn, Arcade, Neo Geo, Atari 2600/5200/7800/Lynx/Jaguar, PC Engine, MSX, Commodore 64, Amiga, WonderSwan, 3DO, Vectrex, Intellivision, ColecoVision, Game Gear and SG-1000.
    Per-System Core Selection: Systems running under RetroArch gain a Core dropdown in their settings, so you can pick Mesen or Nestopia for NES, snes9x or bsnes for SNES, and so on, independently for each system.
    On-Demand Cores: Cores download the first time you launch a system rather than all at once, so installing RetroArch stays small and changing core never needs a reinstall.

💄 Quality of Life Improvements

    RetroArch by Default for Older Systems: Generations 1-5 and all cartridge systems now default to RetroArch, matching what RomM's in-browser player supports. Existing setups keep whichever emulator they already use.
    Save Warning When Switching Emulator: Each system's settings warn you when another emulator already holds saves for it, since saves stay with the emulator that wrote them.
    Clearer Install Prompts: Buttons now name the emulator ("Install RetroArch") and show "Install Core" when only a core is missing. The start menu no longer offers to launch an emulator that is not installed, and offers to install it instead.
    Newest Emulator Versions First: Emulator version lists that are scraped from a website are now sorted newest first rather than in page order.

🐛 Bug Fixes

    Unreachable Settings for Some Platforms: Genesis and Master System had no working settings page at all, because their names contain a slash and the panel could never be found again after being built.
    Saves Uploaded in Bulk: Playing one Genesis game uploaded every Mega Drive save you owned attached to that one game. Saves are now tracked per game.
    BIOS Uploaded as Save Data: Flycast was uploading 6.6 MB of Dreamcast and Naomi BIOS, a shader cache and cached boxart to the server on every sync. Only VMU and NVRAM files sync now.
    3DS System Files Uploaded: Azahar was uploading the entire emulated 3DS NAND and SD card under a single game. 3DS saves now sync per title.
    Saves Mixed Between Emulators: With two emulators available for one system, sync could pull one emulator's save into the other's folder. Saves are now tagged with the emulator that wrote them.
    Installing Over a Running Emulator: Reinstalling while an emulator was open deleted part of the install and then failed on files still in use. Installing is now blocked while an emulator is running.
    Editor Load Errors: The project no longer tries to import installed emulator files, which was producing thousands of shader and asset errors.
    Missing Source File: A source file was excluded by an over-broad ignore rule, so a fresh clone of the project did not compile.

---

RomM Frontend - Version 1.0.5 Changelog

🐛 Bug Fixes

    Portable Directory Support: Updated the AppUpdater to download files directly into the portable `downloads` folder next to the executable instead of the default Godot user directory.
    Linux Permissions: Added a startup check on Linux to recursively grant read, write, and execute permissions to the entire application directory, permanently resolving issues with emulators failing to launch downloaded games.
    Linux Updater Script: Fixed an issue where the `update.sh` script contained hidden carriage returns, preventing it from executing correctly on Linux.

---

RomM Frontend - Version 1.0.4 Changelog

✨ New Features

    Fuzzy Game Search: You can now type directly while focused on the game list to quickly jump to any game matching your search in the currently selected system.
    Random Game Selector: Added a new "Random Game" option in the start menu that instantly takes you to a randomly selected game from your active game list.

💄 Quality of Life Improvements

    Updater Interaction: The update changelog popup now cleanly hooks into standard footer buttons (A: Select, B: Close) for a smoother navigation experience.

🐛 Bug Fixes

    Linux Downloads: Addressed a permissions issue on Linux where downloaded games wouldn't be readable by emulators, explicitly granting read and write access post-download.
    Update Dialog Text Rendering: Removed raw formatting strings from the GitHub changelog payload that caused strange visual rendering artifacts.

---

RomM Frontend - Version 1.0.3 Changelog

💄 Quality of Life Improvements

    Carousel Focus Border: Added a clear focus border around the currently selected game in the carousel to easily identify your active selection.
    Cleaner Header Interface: The platform header text is now exclusively displayed on the Downloads and Settings pages, providing a cleaner, unobstructed view while browsing your games list.

🐛 Bug Fixes

    Carousel Centering: Fixed an issue where the game list would not properly center the active game upon switching systems until the carousel was manually moved.
    Update Popup Logic: Resolved a bug where the update changelog popup would display on startup even when you were already on the latest version.

---

RomM Frontend - Version 1.0.2 Changelog

✨ New Features

    Integrated In-App Popups: Replaced the standard OS dialog windows with a custom, sleek UI overlay for handling app updates and viewing changelogs natively.

💄 Quality of Life Improvements

    Refresh Games Progress Tracker: The "Refresh Current System" button now displays a dedicated progress overlay, letting you know exactly how many games have been discovered from RomM.
    Graceful Updater Restarts: The updater will now hold on the "Download complete" status for a few seconds before restarting, ensuring you aren't abruptly kicked out of the app without knowing why.

🐛 Bug Fixes

    Improved Logging Privacy: Removed the printing of sensitive configuration details (like your host, username, password, and API key) from the developer console during login.
    Cleaner Console Output: Silenced the 404 Not Found console errors that would unnecessarily print whenever a game lacked artwork on your RomM instance.

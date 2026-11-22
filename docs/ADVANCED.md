# Advanced configuration

For players who want to edit files by hand, and for anyone adding an emulator. Everything
here can be left alone in normal use.

- [config.cfg](#configcfg)
- [Choosing emulators per system](#choosing-emulators-per-system)
- [Adding an emulator](#adding-an-emulator)
- [Emulator settings](#emulator-settings)
- [Controller bindings](#controller-bindings)
- [Custom themes](#custom-themes)

## config.cfg

Created in the app folder on first run (on a Mac, `~/Library/Application Support/RomBox`).
Close the app before editing it.

### `[Paths]`

| Key | Default | Contents |
|---|---|---|
| `RomsPath` | `roms/` | Downloaded games |
| `BiosPath` | `bios/` | BIOS and firmware |
| `EmulatorsPath` | `emulators/` | Installed emulators |
| `SavesPath` | `saves/` | All saves, one folder per emulator |

These four can point anywhere, for example another drive. Downloads, install scripts, tools and
cached art always stay in the app folder.

Each emulator's own save folder is a link into `SavesPath`, so the emulator writes where it
always does and the data lands in the store.

### `[RomM]`

`Host`, `Username`, `Password`, `ApiKey`, and `ValidLoginLastUsed`.

> [!WARNING]
> `Password` is stored in plain text.

### `[UI]`

| Key | Values |
|---|---|
| `GameListView` | `Carousel`, `Grid`, `List` |
| `AppTheme` | Any theme listed in the app, `Match System`, or one from `themes.json`. New installs start on `Dark` |
| `AppStyle` | `Glass` (animated background, frosted panels) or `Simple` (flat colours, redraws only on change) |
| `AppBackground` | `Flow`, `Mesh`, `Silk`, `Horizon`, `Blobs`, `Waves`, `Aurora`, `Bokeh`, `Grid`, `Gradient`, `Solid`. Glass only |
| `DetailsLayout` | `Hero` (large image) or `Box` (box art and a list of facts) |
| `UiSounds` | `true` plays interface sounds |
| `InterfaceSize` | `Auto`, `85%`, `100%`, `115%`, `130%`, `150%` |
| `HideGamesWithoutBoxArt` | `true` or `false` |
| `ShowAllSystems` | `true` shows systems with no games |

### `[Library]` and `[Setup]`

| Key | Contents |
| --- | --- |
| `Library/Source` | `RomM` or `Local` |
| `Library/LocalPath` | The root of a local library, holding one folder per system |
| `Library/ScrapeArtwork` | `true` downloads art for a local library from libretro's thumbnail server |
| `Library/ScrapeGameInfo` | `true` sends the selected local game's file hash to Hasheous to identify it |
| `Setup/Complete` | `false` shows the setup wizard at the next start |

### `[Input]`

| Key | Meaning |
|---|---|
| `EmulatorCloseHotkeys` | Godot `JoyButton` numbers held together to close an emulator. Default `[4]`, the View button. |
| `EmulatorCloseHotkeyCount` | How many of them must be held |
| `EmulatorCloseHoldSeconds` | How long to hold them. Default `2.0`. |

## Choosing emulators per system

The easiest way is each system's page in **Settings**. That choice is stored under
`[PreferredEmulators]` in `config.cfg`.

`emulators/EmulatorMap.json` lists which emulators can run each system, first entry as the
default. It is created on first run:

| Systems | Default | Alternative |
|---|---|---|
| NES, SNES, Game Boy, GBC, GBA, DS, PS1, Genesis, Master System, Sega CD, 32X | RetroArch | the standalone emulator |
| Saturn, Arcade, Neo Geo, Atari, PC Engine, MSX, C64, Amiga, WonderSwan, 3DO and more | RetroArch | |
| GameCube, Wii | Dolphin | |
| N64 | gopher64 | RetroArch |
| 3DS | Azahar | |
| PS2 / PS3 / PS4 | PCSX2 / RPCS3 / shadPS4 | |
| PSP | PPSSPP | RetroArch |
| Dreamcast | Flycast | RetroArch |

Keys are RomM platform slugs and values are emulator folder names under `install_scripts/`:

```json
{
  "snes": ["retroarch", "snes9x"],
  "ps2": ["pcsx2"]
}
```

## Adding an emulator

Each emulator is a folder under `install_scripts/` with a `meta.json`, and optionally a
`default_config/` folder copied into the install on setup.

```json
{
  "name": "My Emulator",
  "executable_name": {
    "windows": "my_emulator.exe",
    "linux": "my_emulator-x86_64.AppImage"
  },
  "emulator_dir_name": { "windows": "my_emulator_win", "linux": "my_emulator_linux" },
  "emulator_bios_path": { "windows": "bios", "linux": "bios" },
  "relative_save_path": { "default": "saves", "ps2": "memcards" },
  "preserve_on_reinstall": ["config.ini"],
  "launch_args_with_game": "-fullscreen \"{rom_path}\"",
  "launch_args_without_game": "",
  "install_recipe": {
    "windows": {
      "type": "github_release",
      "repo": "developer/my_emulator",
      "asset_regex": ".*windows-x64\\.zip$",
      "extract": true
    },
    "linux": {
      "type": "direct_url",
      "url": "https://example.com/my_emulator.AppImage",
      "extract": false
    }
  }
}
```

### Fields

| Field | Meaning |
|---|---|
| `name` | Name shown in the app |
| `executable_name` | Executable per OS. Use `executable_regex` when the name contains a version. |
| `android_name` | Name shown on Android when the Android app is called something else, such as ARMSX2 for PCSX2 |
| `android_package` | The emulator's Android package name, such as `com.flycast.emulator`. On Android the app is installed from an APK and detected by this name; give it an `android` entry in `emulator_dir_name` and `install_recipe` too. |
| `android_play_store` | The emulator is on Google Play. Play is used when the recipe has no `android` APK entry, or to update a copy that came from Play. |
| `android_alternate_packages` | Other package names for the same emulator, such as a GitHub build that differs from the Play one. |
| `android_launch` | How a game is opened: `activity`, optional `action`, `game` (`document`, `document_extra:<name>` or `extra:<name>`), `extras` and `restart_task`. Without it, games for this emulator cannot be launched on Android. |
| `android_system_cores` | RetroArch only: a system's core list on Android, in place of `system_cores`, such as a core that runs without a BIOS |
| `emulator_dir_name` | Install folder per OS, under `emulators/` |
| `emulator_bios_path` | Where the chosen BIOS is copied inside the install |
| `relative_save_path` | Save folder inside the install, per system slug or `default`. A string or an array; `{system_slug}` is replaced. Either can also be an object keyed by OS with a `default` entry, for an emulator that keeps saves somewhere else on one OS. |
| `preserve_on_reinstall` | Files kept when the emulator is reinstalled or updated |
| `launch_args_with_game` | Arguments with `{rom_path}`, `{bios_path}`, `{system}` and `{settings}` |
| `launch_args_without_game` | Arguments when opened from the start menu without a game |
| `launch_env` | Environment variables per OS; `{emulator_dir}` is replaced |
| `system_flags` | Text substituted for `{system}` per system slug, so one emulator can pick a core or machine per system. A missing slug removes `{system}`. |

### Install recipes

`install_recipe` is keyed by OS (`windows`, `linux`, `macos`, `android`). A key such as
`macos-arm64` or `macos-x86_64` serves only that processor and wins over the plain OS key, and
`{arch}` in a URL or `asset_regex` becomes `arm64` or `x86_64`. An emulator with no recipe for the
running OS and processor is not offered. Every type accepts `extract` and an optional
`extract_folder_regex` naming a folder inside the archive to take.

On macOS, `executable_name` is the path to the binary inside the bundle, such as
`Dolphin.app/Contents/MacOS/Dolphin`, and `executable_regex` matches that whole path. Zips, disk
images (`.dmg`) and tarballs all unpack. The app's data folder on a Mac is
`~/Library/Application Support/RomBox`, so keep the emulator's data inside its install with
a launch argument, a portable marker in `default_config/`, or `launch_env` setting `HOME` to
`{emulator_dir}/home`. A `default_config/macos/` folder is copied over the shared one on a Mac, as
`default_config/linux/` is on Linux.

| Type | Fields | Use for |
|---|---|---|
| `github_release` | `repo`, `asset_regex` | Projects attaching builds to GitHub releases |
| `github_tags` | `repo`, `tag_regex`, `url_template` with `{version}` | Projects that tag releases without assets |
| `web_scrape` | `list_url`, then `version_regex` with `url_template`, or `link_regex` | Projects hosting builds on their own site |
| `direct_url` | `url` | A fixed link |

## Emulator settings

`settings_fields` in `meta.json` become controls on the system's settings page. Each change is
applied as a launch argument or written into the emulator's config file.

```json
{
  "id": "internal_res",
  "label": "Internal Resolution",
  "type": "dropdown",
  "default_value_string": "1",
  "config_file_relative_path": "config.ini",
  "config_section": "Graphics",
  "config_key": "ResolutionScale",
  "options": { "Native (1x)": "1", "2x": "2", "4x": "4" }
}
```

| Field | Meaning |
|---|---|
| `id`, `label` | Identifier and on-screen name |
| `category` | Heading the setting sits under on the settings page, such as `Display`, `System` or `Controllers`. Settings with the same category are shown together. |
| `type` | `boolean`, `dropdown`, `hidden`, or `usb_bluetooth_adapter` for a list of USB Bluetooth adapters whose choice fills `{vid}` and `{pid}` in `launch_arg_format` (both `-1` for Automatic) |
| `default_value_bool`, `default_value_string` | Defaults; strings accept macros such as `{game_id}` |
| `launch_arg_true`, `launch_arg_false` | Arguments for a boolean |
| `launch_arg_format` | Argument for a dropdown, such as `-res {value}` |
| `config_file_relative_path` | Config file inside the install; may be keyed by OS, with a `default` entry for the rest |
| `config_section`, `config_key` | Where to write; nest sections with `/` or `.` for JSON and BML |
| `operating_systems` | Only show the setting on these OSes. Without it a setting shows everywhere except Android, which needs `android` listed. |
| `apply_on_launch` | Rewrite the value before every launch, in case the emulator changed it |

Supported config formats: INI, CFG and TOML, JSON, and BML.

## Controller bindings

Most emulators ship a controller config in `install_scripts/<emulator>/default_config/`. With
Controller Setup set to Steam Input and the app started from Steam (Windows), the app also points each emulator at every
player's controller on every launch; see [Controller setup](CONTROLLERS.md). Dolphin identifies pads by name: if yours shows as disconnected,
select it once in Dolphin's own controller settings.

### Multitap

DuckStation and PCSX2 have a **Multitap** setting on the system's settings page: Off, In Port 1 or
In Port 2. Pick the port the game expects its multitap in. With a multitap in port 1, players 1 to
4 sit on its four slots. With one in port 2, player 1 stays in port 1 and players 2 to 4 sit on the
multitap. RomBox plays up to four controllers.

In `meta.json` this is `port_layouts` inside `controller_config`. `setting_id` names the dropdown
in `settings_fields`, and each of its values has a layout: `ports` lists the port number each
player gets, in player order, and `config_values` are `Section/Key` entries written to the
controller config file before every launch.

### Wii Bluetooth adapter (Dolphin)

**Use a Wii Bluetooth Adapter for Wii Remotes** hands a USB Bluetooth adapter to Dolphin, such as
a Wii or Wii U Bluetooth module on a USB adapter board. Wii Remotes then connect as they do to a
console, with their speaker and add-ons working. It is offered on Windows and Linux.

**Wii Bluetooth Adapter to Use** picks which adapter Dolphin takes. Automatic lets Dolphin take the
first one it can open. The list shows the Bluetooth adapters RomBox finds when the settings page
opens, so plug the adapter in before starting RomBox. On Windows the list also shows adapters that
were set up before and are unplugged now.

Set the adapter up once before turning it on:

- Windows: install the WinUSB driver for the adapter with [Zadig](https://zadig.akeo.ie/). Windows
  stops using that adapter for its own Bluetooth.
- Linux: add a udev rule that gives your user access to the adapter.

Dolphin's [Bluetooth Passthrough guide](https://wiki.dolphin-emu.org/index.php?title=Bluetooth_Passthrough)
has the steps for both. To pair a remote, open Dolphin with **Start > Launch Current Emulator**,
open **Controllers**, press **Sync**, then press the red button on the remote.

## Recomps

A recomp is a console game rebuilt as its own PC program. RomBox works with
[RecompLauncher](https://github.com/elripalda/RecompLauncher), which finds, installs, sets up and
updates them.

- **Start > Install RecompLauncher** downloads the newest RecompLauncher into RomBox's
  `recomplauncher` folder, on Windows and Linux. On Windows that copy keeps its data, its browser
  data and the recomps it installs inside that folder. On Linux the program is in that folder and
  its data stays in your user data folder.
- **Start > Move RecompLauncher Into RomBox** appears on Windows when RecompLauncher is installed
  the usual way. RomBox copies the program and what it remembers into its `recomplauncher` folder
  and uses that copy from then on. Recomps you already installed stay where they are and keep
  working. The copy you had is left in place for you to uninstall.

- Recomps installed in RecompLauncher appear in the game list of the console they came from, with
  a **Recomp** badge. A recomp for a console your library has no system for is not listed.
- A game in your library that has a recomp you have not installed carries a **Recomp Available**
  badge. Select it and choose **Start > Get the Recomp of This Game** for the list of its recomps.
  Picking one opens its page in RecompLauncher, where you install it.
- **Play** starts the recomp through RecompLauncher. No emulator is started.
- **Delete** removes an installed recomp and its folder. Close RecompLauncher first.
- **Start > Find and Install Recomps** opens RecompLauncher's full-screen view.
- New recomps show in RomBox when you come back to it.

When it starts, RomBox tells RecompLauncher which games are in its ROMs folder and your local
library folder, so RecompLauncher can offer your own copy during setup. It writes this into
RecompLauncher's `library.json` while RecompLauncher is closed, and keeps the previous file
beside it as `library.json.rombox-backup`. A copy of RecompLauncher inside RomBox's
`recomplauncher` folder with a `portable.txt` beside it keeps its data and installed recomps in
that folder.

Holding the close hotkey ends RomBox's session. It may leave the recomp running when
RecompLauncher was already open before you pressed Play.

## Custom themes

Add themes to `themes.json` in the app folder. Each one needs four colours: the base, two
accents, and the panel tint with its transparency:

```json
{
  "My Theme": { "bg": "#05070d", "primary": "#13215a", "secondary": "#0a4a6e", "panel": "#080c1a8c" }
}
```

A theme whose base colour is light gets dark text, as the built-in Light theme does.

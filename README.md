# RomBox

A couch-friendly client for [RomM](https://github.com/rommapp/romm). Browse your RomM library
with a controller, download a game, and play it. The emulator is installed and set up for you,
and your saves sync back to RomM.

RomBox is an independent project. It is not affiliated with or endorsed by the RomM project.
Until version 1.2.2 it was called RomM Frontend.

![Carousel view](docs/images/carousel-view.jpg)

| Grid view | List view |
|---|---|
| ![Grid view](docs/images/grid-view.jpg) | ![List view](docs/images/list-view.jpg) |

## Contents

- [Install](#install)
- [First launch](#first-launch)
- [Controls](#controls)
- [Playing games](#playing-games)
- [Saves and BIOS](#saves-and-bios)
- [Netplay](#netplay)
- [Controllers](#controllers)
- [Steam](#steam)
- [Settings](#settings)
- [Files](#files)
- [Troubleshooting](#troubleshooting)

## Install

You need a RomM server and a 64-bit PC running Windows 10/11 or Linux, or a Mac (Apple Silicon or
Intel) running macOS 12 or later.

1. Download `rombox-windows.zip` or `rombox-linux.zip` from
   [Releases](https://github.com/HowDoDownhill/rombox/releases/latest).
2. Extract it to any folder you can write to. The app is portable: everything it downloads
   stays in that folder.
3. Run `rombox.exe` on Windows or `rombox.sh` on Linux.

On a Mac:

1. Download `rombox-macos.zip`, open it, and move `RomBox.app` to Applications.
2. Open it. The app is not notarized by Apple, so macOS blocks the first open: go to
   **System Settings > Privacy & Security** and choose **Open Anyway**.
3. Everything it downloads goes to `~/Library/Application Support/RomBox`.

Nothing else needs installing. Updates are offered in the app when a new release is out.

## First launch

Enter your server details:

- **Server Address**: the full URL, such as `https://romm.example.com` or `http://192.168.1.50:8080`.
- **Username** and **Password**: your RomM login.
- **API Key**: create one in RomM under your profile, then **Client API Tokens**.

They are saved for next time. Your library, cover art and BIOS files then download in the
background.

## Controls

Button names follow an Xbox controller. The bar at the bottom of the screen always shows what
each button does right now.

| Action | Controller | Keyboard |
|---|---|---|
| Move | D-pad or left stick | Arrow keys |
| Play, download or confirm | A | Enter |
| Back | B | Escape (in menus) |
| Previous or next system | LB or RB | Page Up or Page Down |
| Pick a system from a list | Hold LB or RB | |
| Jump to the next letter | Left or Right (carousel and list views) | Left or Right |
| Show installed games only | B | |
| Downloads | Y | Backspace |
| Delete a downloaded game | X | Delete |
| Start menu | Start | Escape (from the game list) |
| Switch between systems and collections | View | Tab |
| Search | Type while the game list is selected | |
| Close a running emulator | Hold View for 2 seconds | |

The mouse works too: click a game to select it and double-click to play it.

## Playing games

Select a game and press **A**. The first press does whatever is needed next: install the
emulator, download the game, then play it. Progress shows on the Downloads page (**Y**).

To leave a game, hold **View** for two seconds. You can change this combination in
**Settings > Input Settings**.

The **Start** menu holds everything else for the selected game: favorites, the system's
emulator and BIOS, netplay, controller assignment, a random game, and settings.

## Saves and BIOS

**Saves** sync with RomM automatically. Before a game starts, any newer save on the server is
downloaded; when the emulator closes, changed saves are uploaded. All saves live in the `saves`
folder, so reinstalling an emulator never touches them and one folder is your whole backup.

**BIOS files** are downloaded from RomM's firmware section. If a system has more than one,
choose it from **Start > Select Bios For Emulator**.

## Netplay

Play a game together with someone running RomBox on another PC.

1. The host selects a game and chooses **Start > Host Session**. A join code is copied to the
   clipboard; send it to the other players.
2. Each player copies the code and chooses **Start > Join Session**. Players on the same network
   are found automatically without a code.
3. The lobby checks that everyone has the game and the right emulator, then the host starts
   the game for everyone.

Netplay works on systems run by RetroArch and on Dreamcast (Flycast). Over the internet, the
host's router needs UPnP turned on.

## Controllers

Any controller your PC recognises works in the app. For games:

- **Windows**: start the app from Steam with Steam Input on, and the app points each emulator at
  every player's controller. At first startup the app asks how to set up controllers:
  - **Steam Input** (recommended). Outside Steam, this choice adds RomBox to your Steam library
    and restarts it from there.
  - **Manual**, to set up each emulator yourself.

  **Settings > Input Settings > Add to Steam** adds RomBox to Steam at any time. Steam sets player
  order. [Controller setup](docs/CONTROLLERS.md) has a guide for each method.
- **Linux**: emulators use your controller directly, and the app points Dolphin and gopher64 at
  each player's controller when a game starts.
- **Mac**: emulators use your controller directly. Set up each one from its own menu
  (**Start > Launch Current Emulator**); [Controller setup](docs/CONTROLLERS.md#manual) lists where.
- **Wii** games use real Wii Remotes over Bluetooth. Press 1+2 on the remote while the game runs.

## Steam

RomBox is not sold on Steam. It adds itself to your Steam library as a non-Steam game. On Windows
and Linux, when RomBox starts outside Steam and finds Steam installed, it offers to do this and
restart from Steam. That gets you:

- **Steam Input**: every controller works, and each emulator gets the right one for every player.
- **Steam Deck Game Mode and Big Picture**, with full controller navigation.
- **The Steam overlay, Remote Play and Steam Link.**

If you skip it, games work the same. You set up controllers in each emulator yourself, and RomBox
stays out of Game Mode, the overlay and streaming. **Don't Ask Again** stops the offer;
**Settings > Input Settings > Steam Library** has the same button.

Steam only reads new library entries when it starts, so RomBox closes Steam if it is open, adds
its shortcut with library artwork and Steam Input turned on, and starts itself again through
Steam. If you move the RomBox folder, the offer comes back to fix the shortcut.

## Settings

Open **Start > Settings**.

- **Game List Settings**: Carousel, Grid or List view, hiding games without cover art, and
  showing empty systems.
- **General Settings**: app theme, background style, interface size (useful on a TV or a
  handheld), and on Windows a preference for the dedicated GPU.
- **Input Settings**: the combination that closes an emulator and how long to hold it, and on
  Windows the Controller Setup method.
- **Each system**: which emulator it uses and that emulator's options, such as resolution or,
  for RetroArch, the core.

## Files

Everything lives in the app folder. On a Mac it lives in
`~/Library/Application Support/RomBox`.

| Folder | Contents |
|---|---|
| `roms` | Downloaded games |
| `saves` | All saves, one folder per emulator |
| `bios` | BIOS and firmware |
| `emulators` | Installed emulators |
| `config.cfg` | Your settings and login. It holds your password in plain text, so keep it private. |

To move games, saves, BIOS or emulators to another drive, see
[Advanced configuration](docs/ADVANCED.md).

## Troubleshooting

- **Can't log in**: check the server address includes `http://` or `https://` and the port,
  and that the API key is still valid in RomM.
- **A game won't start**: open the system's settings and try reinstalling its emulator, or pick
  a different one. Some systems (PS1, PS2) need a BIOS file in RomM's firmware section.
- **Wrong buttons or no controller in a game**: on Windows, check **Controller Setup** in
  **Settings > Input Settings** matches how you started the app. See
  [Controller setup](docs/CONTROLLERS.md#when-a-controller-does-not-work).
- **Report a problem**: open an
  [issue](https://github.com/HowDoDownhill/rombox/issues) and include
  `rombox.console.exe` output on Windows, or the terminal output of `rombox.sh`
  on Linux.

## For emulator authors and tinkerers

[Advanced configuration](docs/ADVANCED.md) covers `config.cfg`, choosing emulators per system,
adding a new emulator, and its settings.

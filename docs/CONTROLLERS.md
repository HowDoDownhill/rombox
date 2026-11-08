# Controller setup

RomM sets up controllers through Steam Input. Start the app from Steam with Steam Input on, and
RomM points each emulator at the right controller for every player, so Player 1 stays Player 1.

On Windows the app asks once, at first startup, how to set up controllers. Change the answer at
any time in **Settings > Input Settings > Controller Setup**.

| Method | Pick it when | What RomM does |
|---|---|---|
| **Steam Input** (recommended) | You start the app from Steam (a non-Steam shortcut, Big Picture, or Steam Deck desktop) with Steam Input turned on | Points each emulator at Steam's controller for every player. |
| **Manual** | You want to set up each emulator yourself, or you don't use Steam | Leaves every emulator's controller bindings and device settings alone. |

When the app is running outside Steam, choosing Steam Input also adds RomM to your Steam library
(see [Adding RomM to Steam](#adding-romm-to-steam)). With Steam Input chosen and the app started
outside Steam, RomM leaves emulator controller settings alone, as it does for Manual.

On Linux there is no prompt. Emulators read your controllers directly, and for emulators that
need to be told which pad to use (Dolphin and gopher64), the app points each player at their
controller on every launch, in connection order or the order set with **Start > Assign
Controllers**. When you start the app from Steam with Steam Input on, the app notices Steam's
controllers and follows the Steam Input rules below: Steam's connection order sets players, and
**Start > Assign Controllers** is hidden.

On a Mac there is no prompt either, and emulators keep their own controller settings, as with
Manual.

The app's own menus work with any controller under both methods. Button prompts are the same for
every controller: face buttons show their position (top, right, bottom, left), and Start shows
the Xbox, PlayStation and Nintendo symbols side by side.

## Adding RomM to Steam

**Settings > Input Settings > Steam Library > Add to Steam** adds RomM to your Steam library as a
non-Steam game, on Windows and Linux. The row's status says whether RomM is already there.

Steam only reads new library entries when it starts, so if Steam is running the app offers to
restart it: it closes Steam, adds the shortcut, and opens Steam again. A copy of the previous
shortcut list is kept next to it as `shortcuts.vdf.romm-backup`.

To add it by hand instead: in Steam, choose **Games > Add a Non-Steam Game to My Library**, browse
to the app folder, and add `romm-frontend.exe` (Linux: `romm-frontend.x86_64`).

## Steam Input

Steam translates every controller you hold into a Steam virtual Xbox pad before an emulator sees
it. RomM passes those pads through and writes each emulator's device setting so player 1 is
Steam's first pad, player 2 its second, and so on.

1. Add RomM to Steam (above).
2. In Steam, open **RomM Frontend > Properties > Controller** and set it to **Enable Steam Input**.
   For Xbox controllers, Steam's own setting under **Settings > Controller** decides whether Steam
   Input covers them too.
3. Start RomM from your Steam library. On Windows, choose **Steam Input** at the first-startup
   prompt.

Steam decides player order. It numbers its pads separately for each program it starts, so the app
cannot set the order for a game, and **Start > Assign Controllers** is hidden with this method.
Players follow Steam's connection order inside each game; when a pad drops off, the players after
it move up.

Steam keeps the controllers' layout and any per-game remapping you set in Steam. Emulators started
by the app inherit Steam's hooks, so the Steam overlay works in-game.

## Manual

RomM writes nothing to emulator controller bindings or device settings. Configure each emulator once from its own
menu (**Start > Launch Current Emulator** opens it without a game); settings live in
`emulators/<name>/` and survive updates.

| Emulator | Where to set controllers |
|---|---|
| RetroArch | **Settings > Input > Port 1 Controls** (and Port 2 to 4), then **Main Menu > Configuration File > Save Current Configuration** |
| Dolphin | **Controllers**: set each port to Standard Controller, then **Configure** and pick the Device |
| PCSX2 | **Settings > Controllers**: pick a controller per port and use **Automatic Mapping** |
| DuckStation | **Settings > Controllers**: pick a controller per port and use **Automatic Mapping** |
| Flycast | **Settings > Controls**: assign each physical device to a port |
| melonDS | **Config > Input and hotkeys**: pick the joystick and bind buttons |
| PPSSPP | **Settings > Controls > Control mapping** |
| Azahar | **Emulation > Configure > Controls** |
| ares | **Settings > Input** |
| snes9x | **Input > Input Configuration** |
| mGBA | **Tools > Settings > Controllers** |
| gopher64 | The controller settings in gopher64's own window |

Switching from Manual to Steam Input later rewrites the device setting for each player on every
launch from Steam. Button bindings you set stay, with two exceptions: RetroArch is switched to its
`sdl2` controller driver, and Dolphin copies port 1's bindings into ports 2 to 4 when those are
empty.

## Upgrading from ViGEm

Earlier versions offered a third method, ViGEm, which gave non-Xbox controllers a virtual Xbox 360
pad through the ViGEmBus driver. It has been removed, and Steam Input now covers those controllers. If you chose it, the
app asks again at the next startup. The ViGEmBus driver stays installed; RomM no longer uses it,
and you can uninstall it from **Settings > Apps** like any other program.

## When a controller does not work

- Check **Settings > Input Settings**: the status next to Controller Setup says whether the method
  can work right now (for example "Start the App From Steam to Use It").
- Start the app with `--controller-report` (on the command line, or in the Steam shortcut's Launch
  Options). About a minute later it writes `controller-report-<time>.md` under
  `%APPDATA%\Godot\app_userdata\romm-frontend\logs\`, showing which device each emulator will get
  for every player. Attach it to an [issue](https://github.com/HowDoDownhill/romm-frontend/issues).

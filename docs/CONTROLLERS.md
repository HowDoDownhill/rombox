# Controller setup

On Windows the app asks once, at first startup, how to set up controllers for your emulators.
Change the answer at any time in **Settings > Input Settings > Controller Setup**.

| Method | Pick it when | What RomM does |
|---|---|---|
| **Steam Input** | You start the app from Steam (a non-Steam shortcut, Big Picture, or Steam Deck desktop) with Steam Input turned on | Points each emulator at Steam's controller for every player. No driver is installed. |
| **ViGEm** | You start the app on its own | Passes Xbox controllers straight to the emulator, gives every other controller a virtual Xbox 360 pad, and points each emulator at the right pad for every player. Installs the ViGEmBus driver once. |
| **Manual** | You want to set up each emulator yourself | Leaves every emulator's controller bindings and device settings alone. |

The app recommends Steam Input when Steam started it and ViGEm otherwise.

On Linux there is no prompt. Emulators read your controllers directly, and for emulators that
need to be told which pad to use (Dolphin and gopher64), the app points each player at their
controller on every launch, in connection order or the order set with **Start > Assign
Controllers**. Steam Input works as it does for any game.

The app's own menus work with any controller under all three methods. Button prompts are the same
for every controller: face buttons show their position (top, right, bottom, left), and Start shows
the Xbox, PlayStation and Nintendo symbols side by side.

## Steam Input

Steam translates every controller you hold into a Steam virtual Xbox pad before an emulator sees
it. RomM passes those pads through and writes each emulator's device setting so player 1 is
Steam's first pad, player 2 its second, and so on.

1. In Steam, choose **Games > Add a Non-Steam Game to My Library**, browse to the app folder, and
   add `romm-frontend.exe`.
2. Open the shortcut's **Properties > Controller** and set it to **Enable Steam Input**. For Xbox
   controllers, Steam's own setting under **Settings > Controller** decides whether Steam Input
   covers them too.
3. Start the app from Steam. At the first-startup prompt choose **Steam Input**.

Steam decides player order. It numbers its pads separately for each program it starts, so the app
cannot set the order for a game, and **Start > Assign Controllers** is hidden with this method.
Players follow Steam's connection order inside each game; when a pad drops off, the players after
it move up.

Steam keeps the controllers' layout and any per-game remapping you set in Steam. Emulators started
by the app inherit Steam's hooks, so the Steam overlay works in-game.

To pick player order yourself while starting from Steam, turn Steam Input off for the RomM
shortcut and choose ViGEm.

If you later start the app outside Steam with this method still selected, Xbox controllers keep
working. Other controllers may not reach every emulator: PCSX2 and DuckStation number pads by Xbox
controller slot and will not see them. Switch to ViGEm for standalone use.

## ViGEm

[ViGEmBus](https://github.com/nefarius/ViGEmBus) is a Windows driver that creates virtual Xbox 360
controllers. Choosing ViGEm installs it with one Windows permission prompt; nothing else is needed.
Uninstall it from **Settings > Apps** like any other program.

On each launch:

- Xbox controllers (anything Windows lists as an XInput device, wired, Bluetooth or on the Xbox
  Wireless Adapter) go straight to the emulator.
- Every other controller (PlayStation, Switch Pro, 8BitDo in Switch or DirectInput mode) gets a
  virtual Xbox 360 pad for the length of the game, so emulators see the layout their default
  bindings expect. Per-system remaps in the app apply to these pads.
- Each emulator's device setting is rewritten so player 1 is your first controller. **Start >
  Assign Controllers** sets the order; without it, players follow connection order. When an
  assigned pad disconnects, the players after it move up, and a pad that reconnects joins at the
  end.

Windows has four Xbox controller slots, so four players fit in any mix of controllers. ViGEm also
works when Steam started the app with Steam Input off for the shortcut.

If the driver is missing or fails to install, Xbox controllers still work and the others are left
out, with a line in the log saying why.

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

Switching from Manual to Steam Input or ViGEm later rewrites the device setting for each player
on every launch. Button bindings you set stay, with two exceptions: RetroArch is switched to its
`sdl2` controller driver, and Dolphin copies port 1's bindings into ports 2 to 4 when those are
empty.

## When a controller does not work

- Check **Settings > Input Settings**: the status next to Controller Setup says whether the method
  can work right now (for example "Not Launched Through Steam").
- Start the app with `--controller-report` (on the command line, or in the Steam shortcut's Launch
  Options). About a minute later it writes `controller-report-<time>.md` under
  `%APPDATA%\Godot\app_userdata\romm-frontend\logs\`, showing which device each emulator will get
  for every player. Attach it to an [issue](https://github.com/HowDoDownhill/romm-frontend/issues).

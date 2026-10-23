# PPLua: sandboxed Lua mods for People Playground

[![Sandbox tests](https://github.com/heisenberg1234567891000-ship-it/PPLua/actions/workflows/tests.yml/badge.svg)](https://github.com/heisenberg1234567891000-ship-it/PPLua/actions/workflows/tests.yml)
[![Release](https://img.shields.io/github/v/release/heisenberg1234567891000-ship-it/PPLua)](https://github.com/heisenberg1234567891000-ship-it/PPLua/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**English** · [Русский](README.ru.md)

PPLua is an **unofficial** mod loader that brings code mods back to People Playground, as Lua that runs in a
sandbox. It has been tested on People Playground 1.27.18 and is not affiliated with Studio Minus.

C# mods were taken out of the game after the Workshop malware outbreak in September 2026. A C# mod is a
.NET program with full access to your PC. A PPLua mod is a Lua script that can only reach the game world:
it can add items and categories, drive physics, people and limbs, set off explosions, play sounds, swap
sprites, and add hotkeys and context-menu buttons. It **cannot touch files, the network, other programs or
.NET**. Each mod runs in its own interpreter with an instruction budget, so a broken or hostile mod is
stopped instead of freezing the game.

![Homelander, the bundled example mod, firing heat vision](docs/images/homelander.png)

- [Install](#install) · [Security model](#security-model) · [Writing a mod](#writing-a-mod) ·
  [API reference](docs/API.md) · [Building](#building-from-source) · [Changelog](CHANGELOG.md)

## Features

- **Items and categories.** Clone any catalog item, give it a sprite, a thumbnail and Lua behaviour, and file
  it under your own category.
- **Objects.** Position, velocity, mass, gravity, temperature, charge, fire, materials, softness, bounciness,
  welds, hinges, lights, decorations, context-menu buttons, and hooks for use, update, collision, stabs and
  gunshots.
- **People and limbs.** Health, pain, shock, consciousness, poses and walking, bone breaks, dismemberment,
  muscle strength, toughness, regeneration, immunity, a damage buffer, and skin painting that keeps the game's
  own shading.
- **Body blueprint.** Record how a body is put together, then reattach severed limbs or regrow crushed ones on
  their sockets.
- **World.** Spawning, explosions, impulse pulses, EMP, particles, raycasts, beams, sprite-only effects and
  lightweight physics objects built from your own sprites.
- **Sound.** One-shot sounds, controllable looping audio sources, and the game's own material sounds.
- **Input, timers and storage.** Keys and mouse, game-time timers, and up to 256 KB of saved data per mod.
- **The F8 overlay.** Lists every mod with its state, shows recent errors and log lines, and turns mods on or
  off.
- **MoonSharp fixes.** PPLua patches two MoonSharp 2.0 bugs that silently corrupt variables (see the
  [changelog](CHANGELOG.md)), so ordinary Lua behaves like ordinary Lua.

## Install

1. Download **BepInEx 5.4.23.5 (x64)** from its official release page:
   <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5>. The file is `BepInEx_win_x64_5.4.23.5.zip`,
   and its SHA-256 is `82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4`.
2. Open the game folder (Steam → People Playground → Manage → Browse local files) and extract BepInEx there.
   `winhttp.dll`, `doorstop_config.ini` and a `BepInEx` folder should now sit next to `People Playground.exe`.
3. Download `PPLua-<version>.zip` from the [Releases](../../releases) page and extract it into the same folder.
   You get `BepInEx/plugins/PPLua/` and `LuaMods/`.
4. Start the game. When a map loads, a notification reads "PPLua: N Lua mod(s) running". Press **F8** for the
   mod list.

Each release lists SHA-256 hashes in `SHA256SUMS.txt`. To check a download in PowerShell:

```powershell
Get-FileHash .\PPLua-0.8.0.zip -Algorithm SHA256
```

**Linux and Steam Deck (Proton):** set the game's launch options in Steam to
`WINEDLLOVERRIDES="winhttp=n,b" %command%`.

### Installing a Lua mod

Put the mod's folder, the one that contains `mod.json`, into `People Playground/LuaMods/` and restart the
game. To turn a mod off, untick it in the F8 window; the change applies after a restart.

### Uninstalling

Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx/` and `LuaMods/` from the game
folder. Then run Steam's "Verify integrity of game files" to be sure the game is back to stock.

## Security model

Lua is not safe on its own. What makes PPLua safe is what a mod is given. Every mod gets its own
[MoonSharp](https://www.moonsharp.org/) interpreter (a Lua 5.2 interpreter written in C#), and the loader
decides what that interpreter can see.

| A mod cannot | How it is enforced |
|---|---|
| Read, write or delete files | `io`, `load`, `loadfile`, `dofile`, `debug`, `string.dump`, and `os.execute/remove/rename/exit/getenv/tmpname` are removed. `require` only loads `.lua` files from the mod's own folder |
| Load assets from outside its folder | Every path is checked: `..`, absolute paths, drive letters and NTFS streams (`file:stream`) are refused. Only `.png/.jpg` (up to 16 MB) and `.wav/.mp3` (up to 32 MB) load |
| Use the network or start programs | The API has no such functions |
| Reach .NET, Unity or reflection | Lua never receives a .NET object. Game objects reach Lua as handles: empty tables with a locked metatable, while the real object stays on the C# side |
| Freeze the game with an endless loop | Each call into Lua may run 20 million instructions (about 0.2 s). A mod that goes over is stopped for good, and `pcall` cannot catch the stop |
| Interfere with other mods | Each mod has its own interpreter, globals and storage |

**What a mod can do:** anything inside the game world. It can spawn and delete objects, kill and heal people,
cause explosions and change physics. It can read keys while the game has focus, and keep up to 256 KB of its
own data (`ppg.save`) in `BepInEx/config/PPLua/storage/<mod>.json`.

**Known limits:**

- A mod can still run out of memory, for example by doubling a string in a loop, and crash the game. No files
  are harmed when that happens.
- The loader (`PPLua.dll`) and BepInEx are ordinary .NET code with full rights, so you have to trust them.
  That is why the source is open and every release publishes SHA-256 hashes. Get BepInEx only from its official
  GitHub page.
- The sandbox is covered by automated tests (`tests/SandboxTests`) and in-game tests (`tests/PPLuaProbe`,
  `tests/BadLoopMod`). That is not an audit. If you find a way out of the sandbox, please report it privately
  as described in [SECURITY.md](SECURITY.md).

## Writing a mod

A minimal mod is a folder `LuaMods/MyMod/` with two files.

`mod.json`:

```json
{ "name": "My Mod", "author": "me", "description": "What it does", "version": "1.0", "main": "main.lua" }
```

`main.lua`:

```lua
local spr = ppg.sprite("sprites/ball.png")   -- paths are relative to the mod folder

ppg.register_item{
  original = "Brick",                 -- the catalog item to clone
  name = "Lua Ball",
  description = "Jumps when activated",
  category = "Misc.",
  thumbnail = spr,
  after_spawn = function(obj)
    obj:set_sprite(spr)
    obj:on_use(function(self) self:impulse(0, 10) end)
  end,
}

ppg.on("map_loaded", function() ppg.notify("Hello from Lua!") end)
```

`main.lua` runs once when the game starts. Register items, categories and events there; do anything that
changes the world from an event such as `map_loaded`. Errors go to `BepInEx/LogOutput.log` and to the F8 window,
and a mod that raises more than 20 errors in 5 seconds is stopped.

The full reference is in [docs/API.md](docs/API.md) ([Russian](docs/API.ru.md)).

### Bundled example: Homelander

[LuaMods/Homelander](LuaMods/Homelander/main.lua) is a fan-made Homelander built only from PPLua calls, and
it shows most of the API working together: a suit painted over the game's own skin, a cape, heat vision that
burns and slices, flight, a speed punch, a thunder clap, invulnerability and regeneration. Find it in the
**Supes** category (the "+" menu of extra categories).

| Key | Action |
|---|---|
| **H** (hold) | Heat vision at the mouse cursor |
| **J** | Take off or land |
| **R** (hold) | Fly toward the cursor |
| **N** | Speed punch toward the cursor |
| **B** | Thunder clap toward the cursor |

The mod ships a synthesized heat-vision sound. To use your own, put it at
`LuaMods/Homelander/sounds/laser.mp3`. You can also replace the look with your own `custom/skin.png` and
`custom/cape.png`. See the mod's [README.txt](LuaMods/Homelander/README.txt) for details.

## Configuration

BepInEx creates `BepInEx/config/pplua.loader.cfg` on the first start.

| Section | Key | Default | Meaning |
|---|---|---|---|
| `Mods` | `Disabled` | (empty) | Comma-separated folder names of mods that should not run. The F8 checkboxes edit this |
| `UI` | `OverlayKey` | `F8` | Key that opens the mod list |
| `Sandbox` | `InstructionBudget` | `20000000` | Lua instructions one callback may run before its mod is stopped |

## Troubleshooting

| Symptom | What to check |
|---|---|
| No "PPLua" notification on the map | `winhttp.dll` must sit next to `People Playground.exe`, and `BepInEx/LogOutput.log` must exist after a start. On Proton, set the launch option above |
| A mod is missing from the F8 list | The folder needs a `mod.json` directly inside it, not inside another subfolder |
| A mod shows "failed to start" | `main.lua` raised an error while loading. Select the mod in F8 to see it |
| A mod shows "stopped" | Select it in F8 to see the last error. A mod is stopped after more than 20 errors in 5 seconds, or when one call runs past the instruction budget |
| "no longer exists" errors | The object was deleted. Check `obj:exists()` before using a handle you kept |

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer. Building the plugin also needs the
game with BepInEx 5.4.23.5 installed, because it compiles against the game's own DLLs. Those DLLs are only
read at build time; nothing from the game is copied into the repository or the release.

```bash
dotnet build src/PPLua.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\People Playground"
dotnet run --project tests/SandboxTests -c Release
python tools/package.py
```

The plugin is built to `src/bin/Release/PPLua.dll`. `tools/package.py` writes `dist/PPLua-<version>.zip` and
`dist/SHA256SUMS.txt`.

The sandbox tests do **not** need the game. Without it they take HarmonyX from NuGet, which is how GitHub
Actions runs them on every push. [tests/README.md](tests/README.md) explains the in-game tests.

### Project layout

| Path | Contents |
|---|---|
| `src/` | The BepInEx plugin: sandbox, Lua API, handles, the body blueprint and MoonSharp fixes |
| `LuaMods/Homelander/` | The bundled example mod |
| `docs/` | API reference in English and Russian |
| `tests/SandboxTests/` | Sandbox escape and denial-of-service tests, runnable without the game |
| `tests/PPLuaProbe/`, `tests/BadLoopMod/`, `tests/HomelanderLab/` | In-game test mods |
| `tools/` | Release packaging and the script that draws Homelander's art and sounds |

## Contributing

Bug reports, API ideas and pull requests are welcome; start with [CONTRIBUTING.md](CONTRIBUTING.md). Report
sandbox escapes privately, as described in [SECURITY.md](SECURITY.md).

## License and credits

PPLua is released under the [MIT license](LICENSE). MoonSharp is BSD-3-Clause and is bundled; BepInEx is
LGPL-2.1 and is downloaded separately. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The loader and the Homelander example were written with AI assistance (Claude by Anthropic). Homelander's art
and sounds are drawn and synthesized by [tools/make_homelander_assets.py](tools/make_homelander_assets.py).

People Playground is a game by Studio Minus. PPLua ships no game files and is not affiliated with or endorsed by
Studio Minus. The Homelander mod is unofficial fan work, not affiliated with The Boys, Amazon or Sony Pictures
Television.

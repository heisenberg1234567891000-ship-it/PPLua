# Security policy

PPLua exists so that People Playground mods can run without full access to the player's PC. A way around the
sandbox is the most serious bug this project can have.

## Supported versions

| Version | Supported |
|---|---|
| 0.8.x | Yes |
| Older | No (internal test builds, never published) |

## What counts as a vulnerability

Anything that lets a Lua mod, or a file a Lua mod ships, do one of these:

- read, write, create or delete a file outside the mod's own storage (`BepInEx/config/PPLua/storage/<mod>.json`);
- load an asset or Lua module from outside the mod's own folder;
- open a network connection or start a process;
- obtain a .NET, Unity or reflection object, or call a .NET method that the API does not expose;
- keep running after the instruction budget stops it, or hang the game in a way the budget does not catch;
- read or change another mod's globals or storage;
- make the loader execute anything other than Lua inside the sandbox.

Also in scope: a crafted `mod.json`, image, sound or Lua file that crashes the loader in a way that could lead to
any of the above.

## Not a vulnerability

- A mod that uses a lot of memory and crashes the game. This is a known limit (see the README), although ideas
  for capping memory are welcome as a feature request.
- A mod doing destructive things inside the game world (deleting objects, killing people). That is what mods are for.
- Problems in BepInEx, MoonSharp or the game itself that PPLua does not make worse. Please report those upstream.

## How to report

**Do not open a public issue.** Use GitHub's private vulnerability reporting: open the repository's
**Security** tab and choose **Report a vulnerability**.

Please include:

- the PPLua and People Playground versions;
- a minimal mod (`mod.json` and `main.lua`) that shows the problem;
- what it achieves (for example, "reads `C:\Windows\win.ini`").

We aim to answer within 7 days. Once a fix is released, the advisory is published with credit to the reporter,
unless they prefer otherwise.

## How the sandbox is built

The README's [security model](README.md#security-model) describes the design. In the code:

- `src/Sandbox.cs` builds each interpreter: the allowed standard libraries, `require` restricted to the mod folder,
  path checks (`ResolveFile`), the capped `string.rep`, and the instruction budget that `pcall` cannot catch.
- `src/Handles.cs` turns game objects into handles: empty tables with a locked metatable.
- `src/LuaMod.cs` runs every call into Lua, contains errors and stops a mod that misbehaves.
- `tests/SandboxTests` checks escapes and denial of service outside the game; `tests/BadLoopMod` checks the same
  inside it.

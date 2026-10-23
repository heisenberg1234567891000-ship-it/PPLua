# Tests

## Sandbox tests (no game needed)

`SandboxTests` is a console program that builds the same sandbox the loader uses and tries to break out of it:
dangerous globals, `require` and asset paths outside the mod folder, infinite loops (including inside `pcall` and
coroutines), runaway recursion, huge strings, metatable tampering, a budget shared across nested calls, and a
budget that survives a host-side `catch`. It also checks the two MoonSharp fixes and compiles every Lua file in
`LuaMods/`.

```bash
dotnet run --project tests/SandboxTests -c Release
```

It prints `PASS` or `FAIL` for each check and ends with `ALL PASSED`; the exit code is the number of failures.
With BepInEx installed in the game it uses the game's HarmonyX, and without it HarmonyX 2.9.0 comes from NuGet.
GitHub Actions runs it the second way on every push and pull request.

## In-game tests

These are ordinary Lua mods. Copy a folder into `People Playground/LuaMods/`, start the game, load a map, and read
`BepInEx/LogOutput.log`. Remove the folder again afterwards.

| Mod | What it checks | What to look for |
|---|---|---|
| `PPLuaProbe` | The API end to end: sandbox refusals, registering and spawning an item, events, timers, object fields, people, kill and heal, deleted handles | `PROBE PASS` / `PROBE FAIL` lines, then `PROBE DONE passed=N failed=0` |
| `BadLoopMod` | A hostile mod: it tries to escape the sandbox, then hangs the game with an endless loop wrapped in `pcall` | The game keeps running, the escape attempts print `nil` or `false`, and F8 shows the mod as stopped |
| `HomelanderLab` | Homelander in a scene with a victim and a brick. **F9** sets off an explosion next to him and **F10** empties a gun into him | `LAB` lines every 2 seconds. After F9 and F10 he should be alive with `hurt_limbs=0` |

`HomelanderLab` needs the Homelander mod in `LuaMods/` as well. Use H, J, R, N and B there to try his powers by hand.

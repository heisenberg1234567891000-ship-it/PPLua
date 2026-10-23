# Contributing to PPLua

Thanks for helping. Bug reports, API ideas, documentation fixes and pull requests are all welcome, in English or
Russian.

## Reporting a bug

Open an issue with the bug report template and include:

- the PPLua, People Playground and BepInEx versions (the F8 window title shows the PPLua version);
- `BepInEx/LogOutput.log` from the session where it happened;
- the smallest Lua mod that reproduces it, if the bug is in the API.

**Sandbox escapes are the exception.** If a Lua mod can read or write files, reach the network, start a program,
touch .NET objects or get past the instruction budget, do not open a public issue. Follow [SECURITY.md](SECURITY.md).

## Proposing an API

Open a feature request that describes what your mod needs to do in the game, not only the function you have in
mind. Every new function widens what a mod can reach, so each one is checked against the
[security model](README.md#security-model): mods get the game world and nothing outside it.

## Pull requests

1. Fork the repository and branch from `main`.
2. Build and test (see [Building from source](README.md#building-from-source)):
   ```bash
   dotnet build src/PPLua.csproj -c Release -p:GameDir="<your People Playground folder>"
   dotnet run --project tests/SandboxTests -c Release
   ```
3. If you changed the loader, run the in-game tests described in [tests/README.md](tests/README.md) and say in the
   pull request what you checked.
4. If you changed the API, update **both** `docs/API.md` and `docs/API.ru.md`, and add a line to `CHANGELOG.md`
   under an `Unreleased` heading.
5. Keep the pull request to one change.

### Ground rules

- **Never commit game files.** That includes the game's DLLs, decompiled game code and extracted assets. The
  project compiles against the player's own install, and nothing from the game is redistributed.
- **The sandbox never hands Lua a .NET object.** New API surface returns plain Lua values or handles created in
  `src/Handles.cs`.
- **Every path from Lua goes through the sandbox's path check** (`Sandbox.ResolveFile`), with an extension
  allow-list and a size cap.
- **Every call into Lua goes through `LuaMod.Call`**, so errors are contained and the instruction budget applies.
- Art and sounds in example mods must be your own or properly licensed, with the source credited.
- The plugin targets .NET Framework 4.7.2 and runs under Unity's Mono, so use only APIs that exist there. Match the
  style of the surrounding code, with short comments that explain why rather than what.

## Writing a mod for PPLua

You don't need to contribute here to share a mod. Publish its folder anywhere you like; players drop it into
`LuaMods/`. The [API reference](docs/API.md) covers everything a mod can call.

## License

By contributing you agree that your contribution is released under the project's [MIT license](LICENSE).

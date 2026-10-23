## What this changes

## How it was tested

- [ ] `dotnet run --project tests/SandboxTests -c Release` passes
- [ ] Checked in game (say which test mods or steps, see tests/README.md)

## Checklist

- [ ] No game files, decompiled game code or extracted game assets
- [ ] Lua still gets no .NET objects, and new paths go through `Sandbox.ResolveFile`
- [ ] API changes are documented in both `docs/API.md` and `docs/API.ru.md`
- [ ] `CHANGELOG.md` has a line under `Unreleased`

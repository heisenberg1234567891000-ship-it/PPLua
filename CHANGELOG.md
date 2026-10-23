# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). Until 1.0 the API may still change between minor versions.

Versions before 0.8.0 were internal test builds and were never published.

## [0.8.0] - 2026-10-23

The first public release.

### Added
- `ppg.version` and `ppg.game_version` are now documented.
- English API reference (`docs/API.md`) and English README; the Russian versions moved to `docs/API.ru.md` and
  `README.ru.md`.
- Sandbox tests run without the game installed (HarmonyX comes from NuGet), and GitHub Actions runs them on every
  push and pull request.
- A sandbox test that compiles every Lua file shipped in `LuaMods/`.
- Releases come with `SHA256SUMS.txt`.

### Changed
- Homelander is the only bundled example. The other example mods used during development are no longer part of the
  project.
- Homelander no longer ships a third-party heat-vision sample. It uses its own synthesized sound, and players can
  drop their own `sounds/laser.mp3` into the mod folder.
- The release zip puts the documentation under `BepInEx/plugins/PPLua/docs/`.

## [0.7.1] - 2026-10-22

### Added
- `obj.softness`: how soft a material is. At `0`, blades and needles no longer stick. The material is copied, so
  sounds and other properties stay the same.

## [0.7.0] - 2026-10-22

### Added
- `ppg.raycast(..., ignore, solid)`: with `solid = true` the ray stops at the map's walls and floor, and returns
  `false` instead of an object.

### Fixed
- **MoonSharp:** `local a, b` without `=` picked up leftover values from the stack instead of `nil` (inside a loop,
  a table or the iteration number). It is now `nil`, as in standard Lua. This affected any mod that declared
  variables that way.
- The loader's Harmony patches are applied before mods load, so fixes that act at compile time cover `main.lua`.

## [0.6.1] - 2026-10-21

### Fixed
- Homelander touched its own body every frame, so deleting him or one of his parts (the Delete tool, "Clear
  living") hit the error limit: the mod was stopped and its items vanished from the catalog. It now skips deleted
  parts.

## [0.6.0] - 2026-10-21

### Added
- Body blueprint API: `person:blueprint()`, `limb:parent()`, `limb:rest_pose()`, `limb:reattach()`,
  `limb:restore()`, `limb:detach()`, `limb:clear_damage()`, and the fields `limb.disintegrated` and
  `limb.joint_gap`. An arm or leg lost before `blueprint()` is modelled on its pair.

### Fixed
- A reattached limb carries everything that hangs from it (a knee used to end up 20 cm off).
- A restored limb gets a fresh joint (a hand hanging from it used to lie on the floor while counted as attached).

## [0.5.0] - 2026-10-21

### Added
- `ppg.show{...}`: a sprite without physics, for flashes, spheres and backdrops behind every object.

## [0.4.0] - 2026-10-21

### Added
- `obj:on_stab` (injections and stuck blades), `obj:on_shot`, and the contact normal in `on_collide`.
- `person.time_scale` (personal time that also works in slow motion), `person.seizure`, `person:held()`,
  `person:rot_color()`.
- Damage buffer: `limb.buffer`, `limb.buffered`, `limb:take_buffer()`.
- `limb.frozen`, `acid`, `rot`, `zombie`, `connected`.
- `obj.frozen`, `bounciness`, `friction`, `rendered`, `is_static`; `obj:hinge()`, `obj:ignore_collision()`,
  `obj:bind_to()`.
- `ppg.create{...}` (physics objects from mod sprites), `ppg.emp{...}`, `beam:path{...}`.

## [0.3.0] - 2026-10-20

### Added
- `obj:audio(sound, {...})`: a controllable sound source (play from a position, stop, time, volume, pitch, loop)
  that goes through the game's mixer.
- `obj:material_sound(...)`, `obj:sizzle()`, `obj:impact(...)`, `ppg.exists(path)`, `sound.length`.

## [0.2.0] - 2026-10-20

### Added
- The Homelander example mod.
- `person:paint{...}` (paint over the game's own skin), `obj:decorate(...)`, `ppg.beam{...}`.
- `person.pose`, `person.walk`, `person:pose_names()`.
- Limb stats: `strength`, `toughness`, `regeneration`, `shot_damage`, `skin_size()`.
- `obj.visible`, `local_angle`, `bulletproof`, `disintegratable`; `obj:light()` returns the light object.
- `ppg.raycast(..., ignore)` and a pivot point in `ppg.sprite`.

### Fixed
- `limb.immune` also blocks damage the game applies while bypassing its own flag (explosions, pulses).
- **MoonSharp:** `a.x, a.y = 1, 2` through `__newindex` lost the last value.
- `obj.flipped` uses the world scale, so it is correct for the limbs of a person who faces the other way.

[0.8.0]: ../../releases/tag/v0.8.0

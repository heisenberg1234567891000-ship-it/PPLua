# PPLua Lua API reference (v0.8.0)

**English** · [Русский](API.ru.md)

Lua 5.2 (MoonSharp). The standard `string`, `table`, `math`, `bit32`, `coroutine`, `os.time/clock/date/difftime`
and `json` libraries are available, plus the globals `ppg`, `mod`, `log` (same as `print`) and `require`.

## Lifecycle

- `main.lua` runs **once, when the game starts**. Register items, categories and events there, and load sprites
  and sounds.
- Items and categories are added to the catalog **on every map**.
- Anything that changes the world (`ppg.spawn`, `ppg.explode`, `ppg.notify`...) works only on a map. Call it from
  an event such as `ppg.on("map_loaded", ...)`.
- Timers (`ppg.after/every`) are cleared when a new map loads.
- `after_spawn` runs again when a save is loaded, when objects are pasted and when a contraption is spawned. That
  is why per-object hooks (`on_use`, `on_update`, `button`...) belong in `after_spawn`.

## `mod`
`mod.id` (the folder name), `mod.name`, `mod.author`, `mod.version`.
Loader and game versions: `ppg.version` (for example `"0.8.0"`) and `ppg.game_version` (for example `"1.27.18"`).

## `require(name)`
`require("lib.util")` loads `lib/util.lua` from the mod's folder (and only from there). The result is cached.

## Registration

```lua
ppg.register_item{
  original = "Brick",           -- required: an existing item to clone (see ppg.spawnables())
  name = "My Item",             -- required: a unique name
  description = "...",
  category = "Misc.",           -- category name; defaults to the original's category
  thumbnail = "thumb.png",      -- a path or ppg.sprite(...)
  order = "zz",                 -- sort key in the catalog
  before_spawn = function(obj) end,
  after_spawn  = function(obj) end,
}

ppg.register_category{ name = "My Stuff", description = "...", icon = "icon.png" }
```
Built-in categories: `Entities, Melee, Firearms, Explosives, Vehicles, Machinery, Chemistry, Misc.`.
Your own categories appear in the "+" drop-down.

## Events: `ppg.on(event, fn)`

| Event | Arguments |
|---|---|
| `map_loaded` | none |
| `update` | `dt` (every frame) |
| `fixed_update` | `dt` (every physics step) |
| `spawn` | `obj, spawnable_name`: any item was spawned (by the player, a mod or a save) |
| `removed` | `obj` |
| `activate` | `obj`: an item was activated |
| `select` / `deselect` | `obj` |
| `gunshot` | `obj` (the weapon) |
| `death` | `person` |

## Timers
`id = ppg.after(sec, fn)`, `id = ppg.every(sec, fn)`, `ppg.cancel(id)`. Times are in game time, so slow motion
slows them down too.

## Assets (from the mod folder only)
- `ppg.sprite(path, [scale=1], [pixelated=true], [pivot_x=0.5], [pivot_y=0.5])`: `.png/.jpg`, 35 pixels = 1 metre × scale.
  The pivot is given as fractions: `0.5, 1` is the middle of the top edge (handy for a cape).
- `ppg.texture(path, [pixelated=true])`: for `person:skin`
- `ppg.sound(path)`: `.wav/.mp3`; has a `length` field (seconds)
- `ppg.exists(path)`: whether the file exists in the mod folder (for optional assets)

Fields: sprites have `width/height`, textures have `width/height`, sounds have `length`.

## World
| Function | Description |
|---|---|
| `ppg.in_map()` | whether a map is loaded |
| `ppg.notify(text)` | a notification in the corner of the screen |
| `ppg.spawn(name, [x, y], [flipped])` → `obj` | spawn a catalog item, modded ones included. Without coordinates it spawns under the mouse |
| `ppg.objects()` | every spawned object |
| `ppg.people()` | every person (`person`) |
| `ppg.find(x, y, [radius=1])` | objects inside a circle |
| `ppg.raycast(x, y, dx, dy, [dist=100], [ignore], [solid=false])` → `obj, hx, hy` | the first object on a ray. `ignore` is a person, limb or object whose parts are skipped (so you don't hit yourself). With `solid = true` the ray also stops at the map's walls and floor, and then returns `false` instead of an object |
| `ppg.beam{r, g, b, a, width=0.05, light=false, light_radius=2}` → `beam` | a beam (a line drawn over everything), see below |
| `ppg.create{sprite, x, y, angle=0, material, flipped=false, exact_collider=true}` → `obj` | your own physics object from a mod sprite (debris, prosthetics, droplets). Not saved |
| `ppg.show{sprite, x, y, scale=1, angle=0, behind=false, alpha=1}` → `obj` | a sprite without physics: flashes, spheres, backdrops (`behind=true` puts it behind every object). Move it with `x/y/angle`, `resize`, `color`, `destroy` |
| `ppg.emp{x, y, range=5, chance=1}` → `count` | an EMP like the game's own device: electronics in range break |
| `ppg.explode{x, y, force=10, range, fragments=24, dismember=0.025, shrapnel=0, effects=true, big}` | an explosion |
| `ppg.pulse{x, y, force=10, range=5, effects=true, break_objects=true}` | an impulse blast |
| `ppg.particle(name, x, y)` | a particle effect |
| `ppg.play_sound(sound, [volume=1])` | play a sound |
| `ppg.spawnables([category])`, `ppg.categories()`, `ppg.particles()`, `ppg.materials()` | lists of names |

**Particles:** `BigExplosion, BigZap, BlasterImpact, BlasterImpactHole, BloodExplosion,
BrokenElectronicsSpark, Disintegration, EnergyVesselLightning, EnormousExplosion, Explosion, Flash, FuseBlown,
HugeZap, IonExplosion, MetalHit poolable, PinkExplosion, RedBarrelExplosion, Ricochet, Spark, Vapor,
WoodHit poolable`.

**Materials:** `AndroidArmour, Bottle, Bowling ball, Bowling pin, Brick, Bulletproof, Ceramic, Concrete,
Electronics, Flammable metal, Glass, Gore, Gorse, Hollow metal, Human, Ice, Incredible, Insulator, Metal,
Plastic, Rock, Rubber, Snow, Soap, Soft, Tempered Glass, Weapon, Wood`.

## Input and time
`ppg.mouse()` → `x, y` (world coordinates), `ppg.key(name)` (held), `ppg.key_down(name)` (pressed this frame),
`ppg.key_up(name)`, `ppg.mouse_button(0|1|2)`, `ppg.time()`.
Key names are Unity KeyCode names, case-insensitive: `"e"`, `"space"`, `"leftshift"`, `"f7"`, `"1"`, `"mouse0"`.
Keep in mind that the game uses some keys itself (for example, **E** and **Q** spawn an item).

## Storage
`ppg.save(table)` / `ppg.load()` → `table|nil`. JSON up to 256 KB, separate storage for each mod, written to disk
lazily.

## Object (`obj`)

Fields (✎ = writable):

| Field | |
|---|---|
| `name` ✎, `id`, `spawnable` | name, unique id, catalog item name |
| `x` ✎, `y` ✎, `angle` ✎ | position (m) and angle (degrees) |
| `vx` ✎, `vy` ✎, `spin` ✎ | velocity and angular velocity |
| `mass` ✎, `gravity` ✎ | mass and gravity multiplier |
| `temperature` ✎, `charge` ✎, `burn` ✎ (0..1) | temperature in °C, electric charge, how burnt it is |
| `visible` ✎, `local_angle` ✎ | whether the object is shown; angle relative to its parent (for decorations) |
| `rendered` ✎ | whether the sprite is drawn (physics stays): invisibility |
| `frozen` ✎ | pinned in place (a kinematic body: it neither falls nor gets pushed) |
| `bounciness` ✎, `friction` ✎ | bounciness and friction (the object gets its own physics material) |
| `is_static` | a fixed part of the map (walls, floor) |
| `bulletproof` ✎, `disintegratable` ✎ | bullets don't go through; whether it can turn to ash |
| `softness` ✎ (0..1) | how soft the material is: blades and needles only stick when it is above 0.01. At `0` nothing sticks (the material is copied, so sounds and everything else stay the same) |
| `on_fire`, `wet`, `flipped`, `width`, `height`, `is_person` | read-only (`flipped`: whether it faces the other way in the world) |

Methods:

| Method | |
|---|---|
| `obj:exists()` | whether the object is still alive (never raises an error) |
| `obj:destroy()` | delete it |
| `obj:force(fx, fy, [px, py])`, `obj:impulse(fx, fy, [px, py])`, `obj:torque(t)` | forces |
| `obj:ignite([ignore_flammability])`, `obj:extinguish()` | fire |
| `obj:use()` | activate |
| `obj:disintegrate()`, `obj:weightless([bool=true])` | |
| `obj:material(name)` | the physics material (`ppg.materials()`) |
| `obj:set_sprite(sprite, [fix_collider=true])`, `obj:color(r, g, b, [a])` (0..1), `obj:resize(sx, [sy])` | appearance |
| `obj:light(r, g, b, [radius=5], [brightness=1.5])` → `obj` | a glow; returns the light object (`.visible`, `:destroy()`) |
| `obj:decorate(sprite, {x, y, angle, scale, behind})` → `obj` | a decoration sprite that moves with the object (a cape, a helmet). No collision and not saved, so add it in `after_spawn` |
| `obj:play(sound, [volume])` | a sound from the object (once) |
| `obj:audio(sound, {volume=1, pitch=1, loop=false})` → `audio` | a controllable sound source on the object, see below |
| `obj:material_sound("sizzle"\|"soft"\|"hard"\|"stab", [volume])` → `bool` | the material's own game sound: sizzling and flesh sounds for people, metal for metal. `false` if the material has no such sound |
| `obj:sizzle([sound=true])` | smoke and sizzle of a hot material |
| `obj:impact(x, y, [dx, dy], [size=1])` | the material's hit effect (blood for flesh, sparks for metal, splinters for wood) |
| `obj:weld(other)` | rigidly fix it to another object |
| `obj:button(label, fn, [description])` | a context-menu button (right click), `fn(obj)` |
| `obj:on_use(fn)` | `fn(obj)` on activation |
| `obj:on_update(fn)` | `fn(obj, dt)` every frame |
| `obj:on_collide(fn)` | `fn(obj, other, speed, nx, ny)` on a collision; `nx, ny` is the contact normal (from the other body toward this one) |
| `obj:on_stab(fn)` | `fn(obj, victim)` when a sharp object (a needle, a blade) gets stuck in `victim`; this is how injections work |
| `obj:on_shot(fn)` | `fn(obj, damage, x, y)` when a bullet hits |
| `obj:hinge(other, [x, y], [min, max])` | a hinge with `other` at a world point (the object's centre by default), with angle limits |
| `obj:ignore_collision(obj_or_person)` | stop colliding with an object or a whole person |
| `obj:bind_to(other)` | delete this object together with `other` |
| `obj:person()`, `obj:limb()`, `obj:parts()` | the person, the limb, every physical part |

Pass `nil` to an `on_*` method to remove the hook.

## Person (`person`)
Fields: `alive`, `consciousness` ✎ (0..1), `pain` ✎, `shock` ✎, `adrenaline` ✎, `oxygen` ✎ (0..1), `braindead` ✎,
`walk` ✎ (-1..1, the direction the person wants to walk; the game resets it, so set it every frame), `pose` ✎ (a
pose name or `nil`), `time_scale` ✎ (personal time: 5 means five times faster than the world, even in slow
motion), `seizure` ✎ (seconds of seizures).
Poses: `Rest, Fetus, Flailing, Stumbling, Swimming, In pain, Walking, Sitting, Flat, Decerebrate, Decorticate`
(`person:pose_names()`).

Methods: `person:object()`, `person:limbs()`, `person:limb("Head")`, `person:kill()`, `person:heal()`,
`person:blood_color(r, g, b)` (0..255), `person:skin(skin_tex, [flesh_tex], [bone_tex], [scale])`,
`person:paint{ [limb name] = texture, ["*"] = texture }`, `person:rot_color(r, g, b)`, `person:held()` → the
items in their hands, `person:blueprint()` records how the body is assembled (see "Body blueprint" below).

**`person:paint`** paints over the person's own skin from the game. Each texture is stretched over its limb, the
game's outline and shading stay, and transparent pixels leave the original visible. That way a mod ships only its
own artwork, not the game's files. Limb sizes of a human (width × height, facing right):
Head 11×11, UpperBody 9×9, MiddleBody 9×9, LowerBody 9×12, UpperArm(Front) 5×13, LowerArm(Front) 5×16,
UpperLeg(Front) 5×16, LowerLeg(Front) 5×14, Foot(Front) 9×3 (`limb:skin_size()`).

## Limb (`limb`)
Fields: `name`, `health` ✎, `max_health`, `broken`, `dismembered`, `has_brain`, `numbness` ✎, `immune` ✎, `zombie`,
`strength` ✎ (muscle strength, usually 8–14; the game's strength serum tops out at 15), `toughness` ✎ (the
threshold for fractures and dismemberment; a large number makes the limb indestructible), `regeneration` ✎,
`shot_damage` ✎ (bullet damage multiplier), `frozen` ✎ (frostbite: the joints stiffen),
`acid` ✎ and `rot` ✎ (0..1, acid burns and rot on the skin), `connected` (whether it is attached to the body),
`buffer` ✎ / `buffered` / `limb:take_buffer()`: store damage instead of taking it (bullets, explosions, cuts and
fractures are counted but not applied) and collect what was stored.
`immune = true` also protects against damage the game applies while bypassing its own flag (explosions).
`disintegrated`: the limb was crushed or turned to ash. `joint_gap`: how many metres apart the two halves of
its joint have drifted (`nil` without a joint; more than a few centimetres means the joint no longer holds it).

Methods: `limb:object()`, `limb:person()`, `limb:break_bone()`, `limb:heal_bone()`, `limb:damage(x)`,
`limb:slice()` (cut it off), `limb:crush()`, `limb:heal()`, `limb:skin_size()` → `w, h`,
`limb:clear_damage()` (wipe wounds and marks off the skin).

### Body blueprint
`person:blueprint()` records how the body parts are connected right now: joints, links to the body, blood flow.
An arm or a leg that was already lost before the call is modelled on its pair (the left one on the right one).
After that:

| Method | |
|---|---|
| `limb:parent()` | the limb this one hangs from (`nil` for the middle of the torso) |
| `limb:rest_pose()` → `x, y, angle, sx, sy` | where the limb would sit on its parent right now, and the joint's socket point on the parent (`sx, sy`) |
| `limb:reattach()` → `bool` | put a severed limb back: it snaps into its socket and gets a new joint, blood flow and a link to the body. Everything hanging from it (the shin and foot under a thigh) moves with it |
| `limb:restore()` → `bool` | bring back a crushed or disintegrated limb and put it on its parent. Whatever hung from it comes back **severed**; put it back with `reattach()` |
| `limb:detach()` → `bool` | take the limb off its parent, as in dismemberment, but without blood or pain |

```lua
local p = ppg.spawn("Human", 0, 1):person()
p:blueprint()
-- ... later: everything severed comes back, everything crushed is rebuilt
for _, l in ipairs(p:limbs()) do
  if l.disintegrated then l:restore() end
end
for _, l in ipairs(p:limbs()) do
  if l.dismembered then l:reattach() end      -- order doesn't matter: whatever hangs from a limb moves with it
end
p:heal()
```
For a smooth return instead of an instant one, first steer the severed part toward `limb:rest_pose()` (with its
object's `vx/vy`, or `x/y` together with `frozen = true`) and call `reattach()` once it arrives.

## Sound (`audio`)
`local s = obj:audio(ppg.sound("sounds/laser.wav"), { volume = 0.9 })`. Methods `s:play([from_seconds])`,
`s:stop()`, `s:destroy()`; fields `playing`, `length`, `time` ✎ (the playback position, handy for your own
loop), `volume` ✎, `pitch` ✎, `loop` ✎. Sound goes through the game's mixer and follows its volume settings.

## Beam (`beam`)
`local b = ppg.beam{ r = 1, g = 0.1, b = 0.1, width = 0.06, light = true }`, then every frame
`b:set(x1, y1, x2, y2)`, or a polyline `b:path{x1, y1, x2, y2, ...}` (lightning). Fields `visible` ✎, `width` ✎;
methods `b:color(r, g, b, [a])`, `b:destroy()`.
Beams are not saved and disappear when the map changes.

## Errors and limits
- An error in a handler goes to the log and to the F8 window. After more than 20 errors in 5 seconds the mod is
  stopped.
- More than 20 million instructions in one call stops the mod (the `InstructionBudget` setting in
  `BepInEx/config/pplua.loader.cfg`).
- Using a deleted object raises a "no longer exists" error. Check `obj:exists()`.
- Object handles can be compared with `==`: one object always gives the same table.
- `obj.vx, obj.vy = 0, 0` works. In MoonSharp 2.0, multiple assignment through `__newindex` lost the last value;
  PPLua fixes that (for your own metatables too).
- `local a, b` without `=` gives `nil`, as in standard Lua. In MoonSharp 2.0 such variables picked up leftover
  values from the stack (inside a loop, for example, a table or the iteration number); PPLua fixes that.
- `main.lua` and every module loaded with `require` may be at most 1 MB. `string.rep` will not build a string
  longer than 16 MB.

## A larger example
[LuaMods/Homelander/main.lua](../LuaMods/Homelander/main.lua): skin painting with `person:paint`, a cape with
`obj:decorate`, heat vision on `ppg.beam` and `ppg.raycast`, a looping sound through `obj:audio`, flight, a speed
punch, a thunder clap, invulnerability and regeneration, hotkeys and context-menu buttons.

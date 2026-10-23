# PPLua: справочник Lua-API (v0.8.0)

[English](API.md) · **Русский**

Lua 5.2 (MoonSharp). Доступны стандартные `string`, `table`, `math`, `bit32`, `coroutine`,
`os.time/clock/date/difftime` и `json`, плюс глобальные `ppg`, `mod`, `log` (= `print`) и `require`.

## Жизненный цикл

- `main.lua` выполняется **один раз при запуске игры**. Здесь регистрируются предметы, категории,
  события и загружаются спрайты и звуки.
- Предметы и категории добавляются в каталог **на каждой карте**.
- Всё, что меняет мир (`ppg.spawn`, `ppg.explode`, `ppg.notify`...), работает только на карте. Вызывайте
  такое из событий, например `ppg.on("map_loaded", ...)`.
- Таймеры (`ppg.after/every`) сбрасываются при загрузке новой карты.
- `after_spawn` вызывается заново при загрузке сохранения, вставке и спавне постройки. Поэтому хуки
  объекта (`on_use`, `on_update`, `button`...) ставьте именно в `after_spawn`.

## `mod`
`mod.id` (имя папки), `mod.name`, `mod.author`, `mod.version`.
Версии загрузчика и игры: `ppg.version` (например, `"0.8.0"`) и `ppg.game_version` (например, `"1.27.18"`).

## `require(name)`
`require("lib.util")` загрузит `lib/util.lua` из папки мода (только из неё). Результат кешируется.

## Регистрация

```lua
ppg.register_item{
  original = "Brick",           -- обязательно: существующий предмет-основа (ppg.spawnables())
  name = "My Item",             -- обязательно: уникальное имя
  description = "...",
  category = "Misc.",           -- имя категории; по умолчанию категория основы
  thumbnail = "thumb.png",      -- путь или ppg.sprite(...)
  order = "zz",                 -- ключ сортировки в каталоге
  before_spawn = function(obj) end,
  after_spawn  = function(obj) end,
}

ppg.register_category{ name = "My Stuff", description = "...", icon = "icon.png" }
```
Встроенные категории: `Entities, Melee, Firearms, Explosives, Vehicles, Machinery, Chemistry, Misc.`.
Свои категории появляются в выпадающем списке «+».

## События: `ppg.on(event, fn)`

| Событие | Аргументы |
|---|---|
| `map_loaded` | — |
| `update` | `dt` (каждый кадр) |
| `fixed_update` | `dt` (шаг физики) |
| `spawn` | `obj, spawnable_name`: любой предмет заспавнен (игроком, модом, из сохранения) |
| `removed` | `obj` |
| `activate` | `obj`: предмет активирован |
| `select` / `deselect` | `obj` |
| `gunshot` | `obj` (оружие) |
| `death` | `person` |

## Таймеры
`id = ppg.after(sec, fn)`, `id = ppg.every(sec, fn)`, `ppg.cancel(id)`. Время игровое, замедление его тоже замедляет.

## Ассеты (только из папки мода)
- `ppg.sprite(path, [scale=1], [pixelated=true], [pivot_x=0.5], [pivot_y=0.5])`: `.png/.jpg`, 35 пикселей = 1 метр × scale.
  Опорная точка задаётся долями: `0.5, 1` — середина верхнего края (удобно для плаща).
- `ppg.texture(path, [pixelated=true])`: для `person:skin`
- `ppg.sound(path)`: `.wav/.mp3`; поле `length` (секунды)
- `ppg.exists(path)`: есть ли такой файл в папке мода (для необязательных ассетов)

Поля: у спрайта `width/height`, у текстуры `width/height`, у звука `length`.

## Мир
| Функция | Описание |
|---|---|
| `ppg.in_map()` | загружена ли карта |
| `ppg.notify(text)` | уведомление в углу экрана |
| `ppg.spawn(name, [x, y], [flipped])` → `obj` | заспавнить предмет каталога, в том числе из модов. Без координат спавнит под мышью |
| `ppg.objects()` | все заспавненные предметы |
| `ppg.people()` | все люди (`person`) |
| `ppg.find(x, y, [radius=1])` | предметы в круге |
| `ppg.raycast(x, y, dx, dy, [dist=100], [ignore], [solid=false])` → `obj, hx, hy` | первый предмет на луче. `ignore`: человек, конечность или объект, чьи части пропускаются (чтобы не попасть в себя). С `solid = true` луч упирается и в стены и пол карты; тогда вместо объекта приходит `false` |
| `ppg.beam{r, g, b, a, width=0.05, light=false, light_radius=2}` → `beam` | луч (линия поверх всего), см. ниже |
| `ppg.create{sprite, x, y, angle=0, material, flipped=false, exact_collider=true}` → `obj` | свой физический объект из спрайта мода (осколки, протезы, капли). Не сохраняется |
| `ppg.show{sprite, x, y, scale=1, angle=0, behind=false, alpha=1}` → `obj` | спрайт без физики: вспышки, сферы, фоны (`behind=true` — позади всех объектов). Двигается через `x/y/angle`, `resize`, `color`, `destroy` |
| `ppg.emp{x, y, range=5, chance=1}` → `count` | ЭМИ как у устройства игры: электроника в радиусе выходит из строя |
| `ppg.explode{x, y, force=10, range, fragments=24, dismember=0.025, shrapnel=0, effects=true, big}` | взрыв |
| `ppg.pulse{x, y, force=10, range=5, effects=true, break_objects=true}` | импульсный взрыв |
| `ppg.particle(name, x, y)` | эффект частиц |
| `ppg.play_sound(sound, [volume=1])` | проиграть звук |
| `ppg.spawnables([category])`, `ppg.categories()`, `ppg.particles()`, `ppg.materials()` | списки имён |

**Частицы:** `BigExplosion, BigZap, BlasterImpact, BlasterImpactHole, BloodExplosion,
BrokenElectronicsSpark, Disintegration, EnergyVesselLightning, EnormousExplosion, Explosion, Flash, FuseBlown,
HugeZap, IonExplosion, MetalHit poolable, PinkExplosion, RedBarrelExplosion, Ricochet, Spark, Vapor,
WoodHit poolable`.

**Материалы:** `AndroidArmour, Bottle, Bowling ball, Bowling pin, Brick, Bulletproof, Ceramic, Concrete,
Electronics, Flammable metal, Glass, Gore, Gorse, Hollow metal, Human, Ice, Incredible, Insulator, Metal,
Plastic, Rock, Rubber, Snow, Soap, Soft, Tempered Glass, Weapon, Wood`.

## Ввод и время
`ppg.mouse()` → `x, y` (мировые координаты), `ppg.key(name)` (зажата), `ppg.key_down(name)` (нажата в этом
кадре), `ppg.key_up(name)`, `ppg.mouse_button(0|1|2)`, `ppg.time()`.
Имена клавиш берутся из Unity KeyCode без учёта регистра: `"e"`, `"space"`, `"leftshift"`, `"f7"`, `"1"`, `"mouse0"`.
Учтите, что игра сама использует некоторые клавиши (например, **E** и **Q** спавнят предмет).

## Хранилище
`ppg.save(table)` / `ppg.load()` → `table|nil`. JSON до 256 КБ, своё хранилище у каждого мода, запись на диск отложенная.

## Объект (`obj`)

Поля (✎ можно менять):

| Поле | |
|---|---|
| `name` ✎, `id`, `spawnable` | имя, уникальный id, имя предмета каталога |
| `x` ✎, `y` ✎, `angle` ✎ | позиция (м) и угол (градусы) |
| `vx` ✎, `vy` ✎, `spin` ✎ | скорость и угловая скорость |
| `mass` ✎, `gravity` ✎ | масса и множитель гравитации |
| `temperature` ✎, `charge` ✎, `burn` ✎ (0..1) | температура °C, электрический заряд, обгорание |
| `visible` ✎, `local_angle` ✎ | показан ли объект; угол относительно родителя (для украшений) |
| `rendered` ✎ | рисуется ли спрайт (физика остаётся) — невидимость |
| `frozen` ✎ | заморожен на месте (кинематическое тело: не падает и не сдвигается) |
| `bounciness` ✎, `friction` ✎ | упругость и трение (свой физматериал у объекта) |
| `is_static` | неподвижная часть карты (стены, пол) |
| `bulletproof` ✎, `disintegratable` ✎ | пули не пробивают; может ли распадаться в пепел |
| `softness` ✎ (0..1) | мягкость материала: клинки и иглы застревают, только если она больше 0,01. `0` — ничто не втыкается (материал копируется, звуки и прочее остаются) |
| `on_fire`, `wet`, `flipped`, `width`, `height`, `is_person` | только чтение (`flipped` — развёрнут ли в мире) |

Методы:

| Метод | |
|---|---|
| `obj:exists()` | жив ли объект (никогда не выдаёт ошибку) |
| `obj:destroy()` | удалить |
| `obj:force(fx, fy, [px, py])`, `obj:impulse(fx, fy, [px, py])`, `obj:torque(t)` | силы |
| `obj:ignite([ignore_flammability])`, `obj:extinguish()` | огонь |
| `obj:use()` | активировать |
| `obj:disintegrate()`, `obj:weightless([bool=true])` | |
| `obj:material(name)` | физический материал (`ppg.materials()`) |
| `obj:set_sprite(sprite, [fix_collider=true])`, `obj:color(r, g, b, [a])` (0..1), `obj:resize(sx, [sy])` | внешний вид |
| `obj:light(r, g, b, [radius=5], [brightness=1.5])` → `obj` | свечение; возвращает объект света (`.visible`, `:destroy()`) |
| `obj:decorate(sprite, {x, y, angle, scale, behind})` → `obj` | спрайт-украшение, едет вместе с объектом (плащ, шлем). Без коллизии, не сохраняется: добавляйте в `after_spawn` |
| `obj:play(sound, [volume])` | звук от объекта (один раз) |
| `obj:audio(sound, {volume=1, pitch=1, loop=false})` → `audio` | управляемый источник звука на объекте, см. ниже |
| `obj:material_sound("sizzle"\|"soft"\|"hard"\|"stab", [volume])` → `bool` | звук материала из игры: у людей шипение и звук плоти, у металла металл. `false`, если у материала такого звука нет |
| `obj:sizzle([sound=true])` | дым и шипение раскалённого материала |
| `obj:impact(x, y, [dx, dy], [size=1])` | эффект попадания материала (кровь у плоти, искры у металла, щепки у дерева) |
| `obj:weld(other)` | жёстко скрепить с другим объектом |
| `obj:button(label, fn, [description])` | кнопка в контекстном меню (правый клик), `fn(obj)` |
| `obj:on_use(fn)` | `fn(obj)` при активации |
| `obj:on_update(fn)` | `fn(obj, dt)` каждый кадр |
| `obj:on_collide(fn)` | `fn(obj, other, speed, nx, ny)` при столкновении; `nx, ny` — нормаль контакта (от другого тела к этому) |
| `obj:on_stab(fn)` | `fn(obj, victim)` когда острый предмет (игла, клинок) застревает в `victim` — так работают уколы |
| `obj:on_shot(fn)` | `fn(obj, damage, x, y)` при попадании пули |
| `obj:hinge(other, [x, y], [min, max])` | шарнир с `other` в мировой точке (по умолчанию центр объекта), с пределами угла |
| `obj:ignore_collision(obj_or_person)` | не сталкиваться с объектом или со всем человеком |
| `obj:bind_to(other)` | удалить этот объект вместе с `other` |
| `obj:person()`, `obj:limb()`, `obj:parts()` | человек, конечность, все физические части |

Передайте `nil` в `on_*`, чтобы снять хук.

## Человек (`person`)
Поля: `alive`, `consciousness` ✎ (0..1), `pain` ✎, `shock` ✎, `adrenaline` ✎, `oxygen` ✎ (0..1), `braindead` ✎,
`walk` ✎ (-1..1, желаемое направление ходьбы; игра гасит его, ставьте каждый кадр), `pose` ✎ (имя позы или `nil`),
`time_scale` ✎ (личное время: 5 = в пять раз быстрее мира, даже в замедлении), `seizure` ✎ (секунды судорог).
Позы: `Rest, Fetus, Flailing, Stumbling, Swimming, In pain, Walking, Sitting, Flat, Decerebrate, Decorticate`
(`person:pose_names()`).

Методы: `person:object()`, `person:limbs()`, `person:limb("Head")`, `person:kill()`, `person:heal()`,
`person:blood_color(r, g, b)` (0..255), `person:skin(skin_tex, [flesh_tex], [bone_tex], [scale])`,
`person:paint{ [имя конечности] = texture, ["*"] = texture }`, `person:rot_color(r, g, b)`, `person:held()` →
предметы в руках, `person:blueprint()` — запомнить, как собрано тело (см. «Сборка тела» ниже).

**`person:paint`** перекрашивает человека поверх его собственного скина из игры: каждая текстура растягивается
на свою конечность, контур и тени игры сохраняются, прозрачные пиксели оставляют оригинал. Поэтому мод несёт
только свой рисунок, а не файлы игры. Размеры конечностей человека (ширина × высота, лицо смотрит вправо):
Head 11×11, UpperBody 9×9, MiddleBody 9×9, LowerBody 9×12, UpperArm(Front) 5×13, LowerArm(Front) 5×16,
UpperLeg(Front) 5×16, LowerLeg(Front) 5×14, Foot(Front) 9×3 (`limb:skin_size()`).

## Конечность (`limb`)
Поля: `name`, `health` ✎, `max_health`, `broken`, `dismembered`, `has_brain`, `numbness` ✎, `immune` ✎, `zombie`,
`strength` ✎ (сила мышц, обычно 8–14, максимум сыворотки 15), `toughness` ✎ (порог переломов и отрыва,
большое число = неразрушимая), `regeneration` ✎, `shot_damage` ✎ (множитель урона от пуль), `frozen` ✎ (обледенение: суставы деревенеют),
`acid` ✎ и `rot` ✎ (0..1, кислотные ожоги и гниение на коже), `connected` (связана ли с телом),
`buffer` ✎ / `buffered` / `limb:take_buffer()` — копить урон вместо получения (пули, взрывы, порезы,
переломы считаются, но не наносятся) и забрать накопленное.
`immune = true` защищает и от урона, который игра наносит в обход своего флага (взрывы).
`disintegrated` — конечность раздавлена или распалась в пепел, `joint_gap` — на сколько метров разошлись
две половины её сустава (`nil` без сустава; больше нескольких сантиметров значит, что сустав её не держит).

Методы: `limb:object()`, `limb:person()`, `limb:break_bone()`, `limb:heal_bone()`, `limb:damage(x)`,
`limb:slice()` (отсечь), `limb:crush()`, `limb:heal()`, `limb:skin_size()` → `w, h`,
`limb:clear_damage()` (стереть раны и следы с кожи).

### Сборка тела
`person:blueprint()` запоминает, как сейчас соединены части тела: суставы, связи с телом, кровоток. Рука или
нога, потерянная ещё до этого вызова, берётся по образцу парной (левая по правой). После этого:

| Метод | |
|---|---|
| `limb:parent()` | конечность, на которой эта держится (у середины туловища — `nil`) |
| `limb:rest_pose()` → `x, y, angle, sx, sy` | где конечность стояла бы на родителе прямо сейчас, и точка сустава на родителе (`sx, sy`) |
| `limb:reattach()` → `bool` | вернуть оторванную конечность на место: она встаёт в гнездо, получает новый сустав, кровоток и связь с телом. Всё, что на ней висит (голень и стопа под бедром), переносится вместе с ней |
| `limb:restore()` → `bool` | вернуть раздавленную или распавшуюся конечность и поставить её на родителя. То, что висело на ней, возвращается **оторванным**, его ставят на место через `reattach()` |
| `limb:detach()` → `bool` | снять конечность с родителя, как при отрыве, но без крови и боли |

```lua
local p = ppg.spawn("Human", 0, 1):person()
p:blueprint()
-- ... позже: всё оторванное летит обратно, раздавленное собирается заново
for _, l in ipairs(p:limbs()) do
  if l.disintegrated then l:restore() end
end
for _, l in ipairs(p:limbs()) do
  if l.dismembered then l:reattach() end      -- порядок не важен: висящее на конечности переезжает вместе с ней
end
p:heal()
```
Чтобы возврат был плавным, а не мгновенным, сначала ведите оторванную часть к `limb:rest_pose()` (через `vx/vy`
её объекта или `x/y` с `frozen = true`) и вызывайте `reattach()`, когда она долетит.

## Звук (`audio`)
`local s = obj:audio(ppg.sound("sounds/laser.wav"), { volume = 0.9 })`. Методы `s:play([с_секунды])`,
`s:stop()`, `s:destroy()`; поля `playing`, `length`, `time` ✎ (позиция воспроизведения, удобно для своего
цикла), `volume` ✎, `pitch` ✎, `loop` ✎. Звук идёт через микшер игры и учитывает её громкость.

## Луч (`beam`)
`local b = ppg.beam{ r = 1, g = 0.1, b = 0.1, width = 0.06, light = true }`, затем каждый кадр
`b:set(x1, y1, x2, y2)` или ломаная `b:path{x1, y1, x2, y2, ...}` (молнии). Поля `visible` ✎, `width` ✎;
методы `b:color(r, g, b, [a])`, `b:destroy()`.
Лучи не сохраняются и пропадают при смене карты.

## Ошибки и ограничения
- Ошибка в обработчике пишется в лог и в окно F8. После более чем 20 ошибок за 5 секунд мод
  останавливается.
- Больше 20 млн инструкций за один вызов означает остановку мода (настройка `InstructionBudget` в
  `BepInEx/config/pplua.loader.cfg`).
- Обращение к удалённому объекту даёт ошибку «no longer exists». Проверяйте `obj:exists()`.
- Ручки объектов можно сравнивать через `==`: один объект всегда даёт одну и ту же таблицу.
- `obj.vx, obj.vy = 0, 0` работает. В MoonSharp 2.0 множественное присваивание через `__newindex` теряло
  последнее значение; PPLua это исправляет (и для ваших собственных метатаблиц тоже).
- `local a, b` без `=` даёт `nil`, как в обычном Lua. В MoonSharp 2.0 такие переменные получали случайные
  значения со стека (например, в цикле — таблицу или номер итерации); PPLua это исправляет.
- `main.lua` и модули для `require` — не больше 1 МБ каждый. `string.rep` не создаёт строки длиннее 16 МБ.

## Пример посложнее
[LuaMods/Homelander/main.lua](../LuaMods/Homelander/main.lua): перекраска скина через `person:paint`, плащ через
`obj:decorate`, лазеры из глаз на `ppg.beam` и `ppg.raycast`, зацикленный звук через `obj:audio`, полёт, рывок,
ударная волна, неуязвимость и регенерация, горячие клавиши и кнопки в контекстном меню.

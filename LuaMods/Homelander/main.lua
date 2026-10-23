-- Homelander: an unofficial fan mod for PPLua.
--
-- Keys (they control every Homelander on the map):
--   H (hold)  heat vision at the mouse cursor: burns, cuts limbs off, melts small objects
--   J         take off / land
--   R (hold)  fly toward the cursor (takes off by itself)
--   N         speed punch: rockets toward the cursor and smashes whatever he hits
--   B         thunder clap: a shockwave toward the cursor
-- Always on: bulletproof, fireproof, cannot be cut, crushed or dismembered, heals himself.

-- ---- assets ----

local suit = {}
for _, part in ipairs{ "Head", "UpperBody", "MiddleBody", "LowerBody", "UpperArm", "LowerArm", "UpperLeg", "LowerLeg", "Foot" } do
  suit[part] = ppg.texture("suit/" .. part .. ".png")
end
local paint = {
  Head = suit.Head, UpperBody = suit.UpperBody, MiddleBody = suit.MiddleBody, LowerBody = suit.LowerBody,
  UpperArm = suit.UpperArm, UpperArmFront = suit.UpperArm, LowerArm = suit.LowerArm, LowerArmFront = suit.LowerArm,
  UpperLeg = suit.UpperLeg, UpperLegFront = suit.UpperLeg, LowerLeg = suit.LowerLeg, LowerLegFront = suit.LowerLeg,
  Foot = suit.Foot, FootFront = suit.Foot,
}
-- Optional look-alike assets: drop your own files into custom/ (only ones you have the right to use).
--   custom/skin.png   a full People Playground human skin texture, replaces the painted suit
--   custom/cape.png   the cape sprite (hangs from its top edge)
local custom_skin = ppg.exists("custom/skin.png") and ppg.texture("custom/skin.png") or nil
local cape_sprite = ppg.sprite(ppg.exists("custom/cape.png") and "custom/cape.png" or "sprites/cape.png", 1, true, 0.5, 1)

local sounds = {
  -- a player's own laser.mp3 if present (buildup, burst at 1.5 s, steady beam 2.0-3.3 s, final crack at 4.0 s),
  -- else the bundled synth loop
  laser = ppg.sound(ppg.exists("sounds/laser.mp3") and "sounds/laser.mp3" or "sounds/laser.wav"),
  boom = ppg.sound("sounds/boom.wav"),
  whoosh = ppg.sound("sounds/whoosh.wav"),
}
local LONG_LASER = sounds.laser.length > 2
local SND_START, SND_LOOP_FROM, SND_LOOP_TO, SND_END = 1.35, 2.0, 3.3, 3.95

local EYE_FORWARD, EYE_UP = 0.07, 0.06   -- eye position from the head centre (metres, face side)
local EYE_GAP = 0.03                     -- the far eye sits a little further back in side view

local FLY_SPEED = 18     -- m/s
local DASH_SPEED = 45
local DASH_TIME = 0.35
local LASER_RANGE = 60
local CUT_TIME = 0.45    -- seconds of heat vision on a limb before it comes off
local MELT_TIME = 1.5    -- seconds on an object before it disintegrates

local heroes = {}        -- person handle -> state
local told = false

ppg.register_category{ name = "Supes", description = "Superheroes made with Lua", icon = "icon.png" }

-- ---- helpers ----

local function normalize(dx, dy)
  local d = math.sqrt(dx * dx + dy * dy)
  if d < 1e-4 then return 0, 0, 0 end
  return dx / d, dy / d, d
end

local function wrap_angle(a) return (a + 180) % 360 - 180 end

local function each_part(st, fn)
  for _, limb in ipairs(st.limbs) do
    if limb:exists() then              -- a part deleted from the world (the Delete tool) is skipped
      local o = limb:object()
      if o:exists() then fn(o, limb) end
    end
  end
end

-- His upper body; nil once it has been deleted.
local function torso(st)
  local chest = st.person:limb("UpperBody")
  return chest and chest:object()
end

local function aim(st)
  local mx, my = ppg.mouse()
  local t = torso(st)
  return normalize(mx - t.x, my - t.y)
end

local function set_flying(st, on)
  if st.flying == on then return end
  st.flying = on
  each_part(st, function(o) o.gravity = on and 0 or 1 end)
  st.person.pose = on and "Rest" or nil
  if on then torso(st):play(sounds.whoosh, 0.5) end
end

local function laser_sound(st, on)
  local s = st.laser_audio
  if on and not st.firing then
    s:play(LONG_LASER and SND_START or 0)
  elseif on and LONG_LASER and s.time > SND_LOOP_TO then
    s.time = SND_LOOP_FROM                      -- keep the steady part going while H is held
  elseif not on and st.firing then
    if LONG_LASER and s.playing and s.time < SND_END then s.time = SND_END else s:stop() end
  end
  st.firing = on
end

local function hide_laser(st)
  for _, b in ipairs(st.beams) do b.visible = false end
  st.glow.visible = false
  laser_sound(st, false)
end

local function forget(person, st)
  for _, b in ipairs(st.beams) do
    if b:exists() then b:destroy() end
  end
  heroes[person] = nil
end

local function eye(head, back)
  local a = math.rad(head.angle)
  local lx = (head.flipped and -1 or 1) * (EYE_FORWARD - back)
  return head.x + lx * math.cos(a) - EYE_UP * math.sin(a), head.y + lx * math.sin(a) + EYE_UP * math.cos(a)
end

-- Searing flesh on people, the material's own hit sound and debris on everything else.
local function burn_effects(st, hit, x, y, dx, dy, dt)
  st.fx = st.fx - dt
  st.snd = st.snd - dt
  if st.fx <= 0 then
    hit:impact(x, y, -dx, -dy, 0.6)
    hit:sizzle(false)
    st.fx = 0.1
  end
  if st.snd <= 0 then
    if hit:limb() then
      hit:material_sound(math.random() < 0.5 and "sizzle" or "stab", 0.8)
    elseif not hit:material_sound("sizzle", 0.6) then
      hit:material_sound("hard", 0.4)
    end
    st.snd = 0.22
  end
end

-- ---- abilities ----

local function heat_vision(st, dt)
  local head_limb = st.person:limb("Head")
  if not head_limb then return hide_laser(st) end    -- no eyes left to fire from
  local head = head_limb:object()
  local mx, my = ppg.mouse()
  local x1, y1 = eye(head, 0)
  local x2, y2 = eye(head, EYE_GAP)
  local cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
  local dx, dy = normalize(mx - cx, my - cy)
  if dx == 0 and dy == 0 then return hide_laser(st) end

  local hit, hx, hy = ppg.raycast(cx, cy, dx, dy, LASER_RANGE, st.person)
  local ex, ey = hx or (cx + dx * LASER_RANGE), hy or (cy + dy * LASER_RANGE)

  -- two beams converging on the target, each with a hot core, inside one soft glow
  local flicker = math.random() * 0.02
  st.beams[1]:set(x1, y1, ex, ey); st.beams[1].width = 0.04 + flicker
  st.beams[2]:set(x2, y2, ex, ey); st.beams[2].width = 0.04 + flicker
  st.beams[3]:set(x1, y1, ex, ey)
  st.beams[4]:set(x2, y2, ex, ey)
  st.beams[5]:set(cx, cy, ex, ey); st.beams[5].width = 0.15 + flicker * 3
  for _, b in ipairs(st.beams) do b.visible = true end
  st.glow.visible = true
  laser_sound(st, true)

  if not hit then return end
  burn_effects(st, hit, ex, ey, dx, dy, dt)

  local id = hit.id
  st.heat[id] = (st.heat[id] or 0) + dt
  local heat = st.heat[id]
  hit.temperature = hit.temperature + 900 * dt
  if heat > 0.15 and not hit.on_fire then hit:ignite(true) end

  local limb = hit:limb()
  if limb then
    if not limb.immune and not limb.dismembered then
      limb:damage(40 * dt)
      if heat > CUT_TIME then
        limb:slice()
        st.heat[id] = 0
      end
    end
  elseif heat > MELT_TIME and hit.disintegratable then
    hit:disintegrate()
    st.heat[id] = nil
  end
end

local function speed_punch(st)
  local dx, dy, d = aim(st)
  if dx == 0 and dy == 0 then return end
  -- long enough to reach the cursor and a little beyond, never a cross-map flight
  st.dash = math.max(0.08, math.min(DASH_TIME, (d + 1) / DASH_SPEED))
  st.dash_x, st.dash_y = dx, dy
  each_part(st, function(o) o.gravity = 0 end)
  torso(st):play(sounds.whoosh, 0.9)
end

local function thunder_clap(st)
  local dx, dy = aim(st)
  local t = torso(st)
  local cx, cy = t.x + dx * 1.6, t.y + dy * 1.6
  ppg.pulse{ x = cx, y = cy, force = 30, range = 7 }
  ppg.particle("Flash", cx, cy)
  t:play(sounds.boom, 1)
  st.brace = 0.25   -- he plants himself instead of being thrown by his own shockwave
end

-- What a speeding Homelander does to whatever he slams into.
local function smash(st, other, speed)
  if st.dash <= 0 or speed < 8 or not other:exists() then return end
  local victim = other:person()
  if victim == st.person then return end
  local limb = other:limb()
  if limb and not limb.immune then
    limb:crush()
  elseif not limb then
    pcall(function() other:impulse(st.dash_x * 40 * other.mass, st.dash_y * 40 * other.mass) end)
  end
end

-- Runs twice a second whatever happens to him: a stopped heart, freezing or electrocution
-- are undone before they can kill him, and he gets back up even if something did.
local function regenerate(st)
  each_part(st, function(o)
    if o.on_fire then o:extinguish() end
    o.burn = 0
    o.temperature = 37
    o.charge = 0
  end)
  st.person:heal()
end

-- ---- the item ----

local function make_supe(obj)
  local person = obj:person()
  if not person then return end
  if custom_skin then person:skin(custom_skin) else person:paint(paint) end

  local st = {
    person = person, limbs = person:limbs(), flying = false, firing = false,
    dash = 0, dash_x = 0, dash_y = 0, brace = 0, heat = {}, fx = 0, snd = 0, regen = 0,
  }
  for _, limb in ipairs(st.limbs) do
    limb.immune = true
    limb.toughness = 1e6
    limb.strength = 15
    limb.regeneration = 50
    limb.shot_damage = 0
    local o = limb:object()
    o.bulletproof = true
    o.disintegratable = false
    o:on_collide(function(_, other, speed) smash(st, other, speed) end)
  end

  local back = person:limb("UpperBody"):object()
  st.cape = back:decorate(cape_sprite, { x = -0.11, y = 0.12, behind = true })
  local head = person:limb("Head"):object()
  st.glow = head:light(1, 0.15, 0.1, 1.2, 2)
  st.glow.visible = false
  st.laser_audio = head:audio(sounds.laser, { volume = 0.9, loop = not LONG_LASER })
  st.beams = {
    ppg.beam{ r = 1, g = 0.1, b = 0.06, width = 0.04 },                                 -- near eye
    ppg.beam{ r = 1, g = 0.1, b = 0.06, width = 0.04 },                                 -- far eye
    ppg.beam{ r = 1, g = 0.85, b = 0.7, width = 0.014 },                                -- hot cores
    ppg.beam{ r = 1, g = 0.85, b = 0.7, width = 0.014 },
    ppg.beam{ r = 1, g = 0.2, b = 0.1, a = 0.25, width = 0.15, light = true, light_radius = 1.6 }, -- glow
  }
  hide_laser(st)

  back:button("Fly / land", function() set_flying(st, not st.flying) end, "Homelander: take off or land (J)")
  back:button("Thunder clap", function() thunder_clap(st) end, "Homelander: shockwave toward the cursor (B)")

  heroes[person] = st
  if not told then
    told = true
    ppg.notify("Homelander: H heat vision, J fly/land, R fly to cursor, N speed punch, B thunder clap")
  end
end

ppg.register_item{
  original = "Human",
  name = "Homelander",
  description = "Fan-made supe. H: heat vision, J: fly/land, R: fly to cursor, N: speed punch, B: thunder clap. Bulletproof and nearly indestructible.",
  category = "Supes",
  thumbnail = "thumb.png",
  after_spawn = make_supe,
}

-- ---- per-frame control ----

ppg.on("map_loaded", function()
  heroes = {}
  told = false
end)

ppg.on("update", function(dt)
  local fire = ppg.key("h")
  local toggle = ppg.key_down("j")
  local punch = ppg.key_down("n")
  local clap = ppg.key_down("b")
  local t = ppg.time()

  for person, st in pairs(heroes) do
    if not person:exists() or not torso(st) then
      forget(person, st)
    else
      st.regen = st.regen - dt
      if st.regen <= 0 then
        regenerate(st)
        st.regen = 0.5
      end
    end

    if not heroes[person] then
      -- forgotten above
    elseif not person.alive then
      hide_laser(st)
    else
      if toggle then set_flying(st, not st.flying) end
      if ppg.key("r") and not st.flying then set_flying(st, true) end
      if punch then speed_punch(st) end
      if clap then thunder_clap(st) end
      if fire then heat_vision(st, dt) else hide_laser(st) end

      -- the cape streams away from the direction of travel
      local body = torso(st)
      local facing = body.flipped and -1 or 1
      local speed = math.sqrt(body.vx * body.vx + body.vy * body.vy)
      local trail = math.max(-80, math.min(80, -body.vx * facing * 5))
      if st.flying and speed > 2 then trail = trail + math.sin(t * 22) * 6 end
      if st.cape:exists() then st.cape.local_angle = st.cape.local_angle + (wrap_angle(trail - st.cape.local_angle)) * math.min(1, dt * 8) end
    end
  end
end)

ppg.on("fixed_update", function(dt)
  local mx, my = ppg.mouse()
  local steer = ppg.key("r")
  for person, st in pairs(heroes) do
    if person:exists() and person.alive and torso(st) then
      local tvx, tvy, rate = nil, nil, 8
      if st.brace > 0 then
        st.brace = st.brace - dt
        tvx, tvy, rate = 0, 0, 30
      elseif st.dash > 0 then
        st.dash = st.dash - dt
        tvx, tvy, rate = st.dash_x * DASH_SPEED, st.dash_y * DASH_SPEED, 30
        if st.dash <= 0 and not st.flying then each_part(st, function(o) o.gravity = 1 end) end
      elseif st.flying then
        tvx, tvy = 0, 0
        if steer then
          local body = torso(st)
          local dx, dy, d = normalize(mx - body.x, my - body.y)
          local speed = math.min(FLY_SPEED, d * 4)
          tvx, tvy = dx * speed, dy * speed
        end
      end

      if tvx then
        local k = math.min(1, dt * rate)
        each_part(st, function(o)
          o.vx = o.vx + (tvx - o.vx) * k
          o.vy = o.vy + (tvy - o.vy) * k
        end)
        -- stay upright, leaning into the direction of travel
        local lean = math.max(-35, math.min(35, -tvx * 1.5))
        for _, name in ipairs{ "UpperBody", "MiddleBody", "LowerBody" } do
          local part = person:limb(name):object()
          local err = wrap_angle(part.angle - lean)
          part.spin = part.spin + (-err * 6 - part.spin) * math.min(1, dt * 10)
        end
      end
    end
  end
end)

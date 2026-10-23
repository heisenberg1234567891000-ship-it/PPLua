-- Test scene for the Homelander mod: spawns him, a victim and a brick, then reports what happened to them.
local hero, victim, brick

local function pos(o) return string.format("(%.2f, %.2f)", o.x, o.y) end

local function report(tag)
  if not (hero and hero:exists()) then print("LAB " .. tag .. " hero missing") return end
  local p = hero:person()
  local torso = p:limb("UpperBody"):object()
  local cut, broken = 0, 0
  for _, l in ipairs(victim:person():limbs()) do
    if l.dismembered then cut = cut + 1 end
    if l.broken then broken = broken + 1 end
  end
  local hurt = 0
  for _, l in ipairs(p:limbs()) do if l.dismembered or l.broken or not l.immune then hurt = hurt + 1 end end
  print(string.format("LAB %s hero alive=%s torso=%s gravity=%.1f pose=%s hurt_limbs=%d | victim alive=%s cut=%d broken=%d | brick=%s",
    tag, tostring(p.alive), pos(torso), torso.gravity, tostring(p.pose), hurt,
    tostring(victim:person().alive), cut, broken, brick:exists() and pos(brick) or "gone"))
end

ppg.on("map_loaded", function()
  hero = ppg.spawn("Homelander", -2, 3)
  victim = ppg.spawn("Human", 3, 3)
  brick = ppg.spawn("Brick", 6.5, 3)
  local mx, my = ppg.mouse()
  print(string.format("LAB spawned; victim torso at %s, mouse at (%.2f, %.2f)", pos(victim:person():limb("UpperBody"):object()), mx, my))
  ppg.every(2, function() report("t=" .. string.format("%.0f", ppg.time())) end)
  -- audio API mechanics the laser sound relies on (copy Homelander's sounds/laser.mp3 here to run it)
  if ppg.exists("sounds/laser.mp3") then
  local snd = ppg.sound("sounds/laser.mp3")
  local src = brick:audio(snd, { volume = 0 })
  print(string.format("LAB audio length=%.2f", snd.length))
  src:play(1.35)
  ppg.after(0.5, function()
    print(string.format("LAB audio after 0.5s playing=%s time=%.2f (expect ~1.85)", tostring(src.playing), src.time))
    src.time = 3.95
    ppg.after(1.3, function() print("LAB audio after end jump playing=" .. tostring(src.playing) .. " (expect false)") end)
  end)
  end
  -- material sounds: flesh must have sizzle and stab clips
  local limb_obj = victim:person():limb("UpperBody"):object()
  print("LAB flesh sounds sizzle=" .. tostring(limb_obj:material_sound("sizzle", 0)) .. " stab=" .. tostring(limb_obj:material_sound("stab", 0))
    .. " soft=" .. tostring(limb_obj:material_sound("soft", 0)) .. " | brick hard=" .. tostring(brick:material_sound("hard", 0)) .. " sizzle=" .. tostring(brick:material_sound("sizzle", 0)))
  ppg.after(1.5, function()
    local t = hero:person():limb("UpperBody"):object()
    print("LAB floor check: hero torso " .. pos(t) .. ", victim head " .. pos(victim:person():limb("Head"):object()))
  end)
end)

-- F9: an explosion right next to Homelander. F10: a gun emptied into him. He should shrug off both.
ppg.on("update", function()
  if not (hero and hero:exists()) then return end
  local t = hero:person():limb("UpperBody"):object()
  if ppg.key_down("f9") then
    ppg.explode{ x = t.x + 0.5, y = t.y, force = 25, range = 6 }
    ppg.after(1.5, function() report("after-explosion") end)
  end
  if ppg.key_down("f10") then
    local guns = ppg.spawnables("Firearms")
    local gun = ppg.spawn(guns[1], t.x - 2.5, t.y)
    print("LAB firing " .. guns[1] .. " at " .. pos(t))
    gun.gravity = 0
    for i = 1, 8 do
      ppg.after(0.3 * i, function()
        if gun:exists() then
          gun.x, gun.y, gun.angle, gun.vx, gun.vy, gun.spin = t.x - 2.5, t.y, 0, 0, 0, 0
          gun:use()
        end
      end)
    end
    ppg.after(3.5, function() report("after-gunfire") end)
  end
end)

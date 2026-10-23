-- Misbehaving mod used to check the watchdog: tries to escape the sandbox, then to freeze the game.
print("escape attempts: io=" .. tostring(io) .. " load=" .. tostring(load) .. " os.execute=" .. tostring(os.execute))
print("require outside: " .. tostring(pcall(require, "../../BepInEx/config/BepInEx")))
print("sprite outside: " .. tostring(pcall(ppg.sprite, "../../People Playground_Data/Resources/unity_builtin_extra")))
ppg.on("map_loaded", function()
  print("about to hang")
  while true do pcall(function() while true do end end) end
end)

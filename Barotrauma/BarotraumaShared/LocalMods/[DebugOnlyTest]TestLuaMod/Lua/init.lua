print("Hello!")

Hook.Add("character.created", "test", function(character) 
    print("character.created: ", character)
end)

Hook.Add("character.death", "test", function(character) 
    print("character.death: ", character)
end)

Hook.Add("character.giveJobItems", "test", function(character) 
    print("character.giveJobItems: ", character)
end)

Hook.Add("roundStart", "test", function()
    print("roundStart")
end)

Hook.Add("roundEnd", "test", function()
    print("roundEnd")
end)

Hook.Add("missionsEnded", "test", function()
    print("missionsEnded")
end)

-- cfg tests
local str = "CLIENT: "

if SERVER then
  str = "SERVER: "
end

function OnChanged(cfg)
  print(str, "cfg value for ", cfg.InternalName, " changed to ", cfg.Value)
end

local failed, package = trygetpackage("[DebugOnlyTest]TestLuaMod")

print("packageFailed=", failed)
print("package", package.Name)

local success, config = ConfigService.TryGetConfig(SettingBase.Int32, package, "TestSynchroServer")
local success2, config2 = ConfigService.TryGetConfig(SettingBase.Int32, package, "TestSynchroClient")

if not success or not success2 then
  print("Failed to get configs.")
  return
end

config.OnValueChanged.add(OnChanged)
config2.OnValueChanged.add(OnChanged)

print(str, " testsynchroclient=", config2.Value)
print(str, " testsynchroserver=", config.Value)

-- The server should keep updating the value and it should show up on the client.
-- The client should try updating and it should fail.

local lastTime = Timer.Time + 30 -- give time to join

Hook.Add("think", "printconfig", function()
  if lastTime > Timer.Time then return end
  lastTime = Timer.Time + 10

  if SERVER then
    local succ = config.TrySetValue(config.Value + 1)
    print("Success of setting value on server for '", config.InternalName,"': ", succ)
  end
  if CLIENT then
    local succ = config.TrySetValue(config.Value + 1)
    print("Success of setting value on client for '", config.InternalName,"': ", succ, " | This should fail if permissions are not set for client.")
  end
  
end)

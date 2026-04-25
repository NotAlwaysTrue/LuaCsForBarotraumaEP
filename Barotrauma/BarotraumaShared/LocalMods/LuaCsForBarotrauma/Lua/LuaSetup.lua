LuaSetup = {}

local path = ...

local function AddTableToGlobal(tbl)
    for k, v in pairs(tbl) do
        _G[k] = v
    end
end

if SERVER then
    AddTableToGlobal(dofile(path .. "/Lua/DefaultLib/LibServer.lua"))
else
    AddTableToGlobal(dofile(path .. "/Lua/DefaultLib/LibClient.lua"))
end

AddTableToGlobal(dofile(path .. "/Lua/DefaultLib/LibShared.lua"))

AddTableToGlobal(dofile(path .. "/Lua/CompatibilityLib.lua"))

dofile(path .. "/Lua/DefaultHook.lua")

Descriptors = LuaUserData

dofile(path .. "/Lua/DefaultLib/Utils/Math.lua")
dofile(path .. "/Lua/DefaultLib/Utils/String.lua")
dofile(path .. "/Lua/DefaultLib/Utils/Util.lua")
dofile(path .. "/Lua/DefaultLib/Utils/SteamApi.lua")

if not CSActive then
    for k, v in pairs(debug) do
        if k ~= "getmetatable" and k ~= "setmetatable" and k ~= "traceback" then
            debug[k] = nil
        end
    end
end

LuaSetup = nil
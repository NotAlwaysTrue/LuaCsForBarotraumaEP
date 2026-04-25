using Barotrauma.LuaCs.Compatibility;
using System;
using System.Reflection;
using static Barotrauma.LuaCs.Compatibility.ILuaCsHook;
using LuaCsCompatPatchFunc = Barotrauma.LuaCsPatch;

namespace Barotrauma.LuaCs;

public interface ILuaPatcher : IReusableService
{
    string Patch(string identifier, string className, string methodName, string[] parameterTypes, LuaCsPatchFunc patch, HookMethodType hookType = HookMethodType.Before);
    string Patch(string identifier, string className, string methodName, LuaCsPatchFunc patch, HookMethodType hookType = HookMethodType.Before);
    string Patch(string className, string methodName, string[] parameterTypes, LuaCsPatchFunc patch, HookMethodType hookType = HookMethodType.Before);
    string Patch(string className, string methodName, LuaCsPatchFunc patch, HookMethodType hookType = HookMethodType.Before);
    bool RemovePatch(string identifier, string className, string methodName, string[] parameterTypes, HookMethodType hookType);
    bool RemovePatch(string identifier, string className, string methodName, HookMethodType hookType);

    void HookMethod(string identifier, MethodBase method, LuaCsCompatPatchFunc patch, HookMethodType hookType = HookMethodType.Before, IAssemblyPlugin owner = null);
    public void HookMethod(string identifier, string className, string methodName, string[] parameterNames, LuaCsCompatPatchFunc patch, ILuaCsHook.HookMethodType hookMethodType = ILuaCsHook.HookMethodType.Before);
    public void HookMethod(string identifier, string className, string methodName, LuaCsCompatPatchFunc patch, ILuaCsHook.HookMethodType hookMethodType = ILuaCsHook.HookMethodType.Before);
    public void HookMethod(string className, string methodName, LuaCsCompatPatchFunc patch, ILuaCsHook.HookMethodType hookMethodType = ILuaCsHook.HookMethodType.Before);
    public void HookMethod(string className, string methodName, string[] parameterNames, LuaCsCompatPatchFunc patch, ILuaCsHook.HookMethodType hookMethodType = ILuaCsHook.HookMethodType.Before);
}

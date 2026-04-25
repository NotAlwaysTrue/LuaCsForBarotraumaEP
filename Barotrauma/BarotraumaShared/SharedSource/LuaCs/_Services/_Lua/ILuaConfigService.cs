using System.Collections.Generic;
using Barotrauma.LuaCs.Data;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs;

public interface ILuaConfigService : ILuaService
{
    FluentResults.Result LoadSavedValueForConfig(ISettingBase setting);
    bool TryGetConfig<T>(ContentPackage package, string internalName, out T instance) where T : ISettingBase;
    FluentResults.Result SaveConfigValue(ISettingBase setting);
}

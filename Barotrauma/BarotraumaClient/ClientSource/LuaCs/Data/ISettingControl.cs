using System;

namespace Barotrauma.LuaCs.Data;

public interface ISettingControl : ISettingBase
{
    KeyOrMouse Value { get; }
    bool TrySetValue(KeyOrMouse value);
    bool IsDown();
    bool IsHit();
}

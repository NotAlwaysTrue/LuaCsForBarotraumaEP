using System;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs.Data;

public interface IDisplayable
{
    public void AddDisplayComponent(GUILayoutGroup layoutGroup, Vector2 relativeSize, Action<string> onSerializedValue);
}

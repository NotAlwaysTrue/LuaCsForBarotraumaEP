using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.LuaCs;

public interface IConsoleCommandsService : IService
{
    void RegisterCommand(string name, string help, Action<string[]> onExecute, Func<string[][]> getValidArgs = null, bool isCheat = false);
    void AssignOnExecute(string names, Action<string[]> onExecute);
#if SERVER
    internal void AssignOnClientRequestExecute(string names, Action<Client, Vector2, string[]> onClientRequestExecute);
#endif
    void RemoveCommand(string name);
    void RemoveRegisteredCommands();
}

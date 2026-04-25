using Barotrauma.LuaCs.Events;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.LuaCs;

internal class ConsoleCommandsService : IConsoleCommandsService
{
    private readonly List<DebugConsole.Command> _registeredCommands = new();
    
    public void Dispose()
    {
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }

        foreach (var cmd in _registeredCommands.ToImmutableArray())
        {
            DebugConsole.Commands.Remove(cmd);
        }

        _registeredCommands.Clear();
    }

    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }

    public void RegisterCommand(string name, string help, Action<string[]> onExecute, Func<string[][]> getValidArgs = null, bool isCheat = false)
    {
        IService.CheckDisposed(this);
     
        if (DebugConsole.Commands.Any(cmd => cmd.Names.Contains(name)))
        {
            LuaCsSetup.Instance.Logger.LogWarning($"Registering console command {name} more than once!");
        }
        
        var cmd = new DebugConsole.Command(name, help, onExecute, getValidArgs, isCheat);
        _registeredCommands.Add(cmd);
        DebugConsole.Commands.Add(cmd);
    }

    public void AssignOnExecute(string names, Action<string[]> onExecute)
    {
        var matchingCommand = DebugConsole.Commands.Find(c => c.Names.Intersect(names.Split('|').ToIdentifiers()).Any());
        if (matchingCommand == null)
        {
            throw new Exception("AssignOnExecute failed. Command matching the name(s) \"" + names + "\" not found.");
        }
        else
        {
            matchingCommand.OnExecute = onExecute;
        }
    }

#if SERVER
    public void AssignOnClientRequestExecute(string names, Action<Client, Vector2, string[]> onClientRequestExecute)
    {
        var matchingCommand = DebugConsole.Commands.Find(c => c.Names.Intersect(names.Split('|').ToIdentifiers()).Any());
        if (matchingCommand == null)
        {
            throw new Exception("AssignOnClientRequestExecute failed. Command matching the name(s) \"" + names + "\" not found.");
        }
        else
        {
            matchingCommand.OnClientRequestExecute = onClientRequestExecute;
        }
    }
#endif

    public void RemoveCommand(string name)
    {
        IService.CheckDisposed(this);

        _registeredCommands.RemoveAll(cmd => cmd.Names.Contains(name));
        DebugConsole.Commands.RemoveAll(cmd => cmd.Names.Contains(name));
    }

    public void RemoveRegisteredCommands()
    {
        IService.CheckDisposed(this);
        foreach (var cmd in _registeredCommands.ToImmutableArray())
        {
            DebugConsole.Commands.Remove(cmd);
        }
        _registeredCommands.Clear();
    }
}

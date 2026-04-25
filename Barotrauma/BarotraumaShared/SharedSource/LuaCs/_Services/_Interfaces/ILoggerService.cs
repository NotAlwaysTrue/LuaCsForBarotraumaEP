using System;
using Barotrauma.Networking;
using FluentResults;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs;

public readonly record struct PendingLog(string Message, Color? Color, ServerLog.MessageType MessageType);

public interface ILoggerSubscriber
{
    void OnLog(PendingLog pendingLog);
}

/// <summary>
/// Provides console and debug logging services
/// </summary>
public interface ILoggerService : IReusableService
{
    void Subscribe(ILoggerSubscriber subscriber);
    void Unsubscribe(ILoggerSubscriber subscriber);
    void ProcessLogs();
    void HandleException(Exception exception, string prefix = null);
    void LogError(string message);
    void LogWarning(string message);
    void LogMessage(string message, Color? serverColor = null, Color? clientColor = null);
    void Log(string message, Color? color = null, ServerLog.MessageType messageType = ServerLog.MessageType.ServerMessage);
    void LogResults(FluentResults.Result result);
    
    #region DebugBuilds

    void LogDebug(string message, Color? color = null);
    void LogDebugWarning(string message);
    void LogDebugError(string message);

    #endregion

    #region LegacyCompat_LuaCsLogger

    public void HandleException(Exception ex, LuaCsMessageOrigin origin)
    {
        HandleException(ex, origin.ToString());
    }

    public void LogError(string message, LuaCsMessageOrigin origin)
    {
        LogError(message);
    }
    
    #endregion
}

public enum LuaCsMessageOrigin
{
    LuaCs,
    Unknown,
    LuaMod,
    CSharpMod,
}

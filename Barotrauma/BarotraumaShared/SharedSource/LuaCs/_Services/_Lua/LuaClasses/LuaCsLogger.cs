using System;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using MoonSharp.Interpreter;
using Barotrauma.LuaCs;

namespace Barotrauma
{

    public partial class LuaCsLogger
    {
        public static void HandleException(Exception ex, LuaCsMessageOrigin origin)
        {
            LuaCsSetup.Instance.Logger.HandleException(ex);
        }

        public static void LogError(string message, LuaCsMessageOrigin origin)
        {
            LuaCsSetup.Instance.Logger.LogError(message);
        }

        public static void LogError(string message)
        {
            LuaCsSetup.Instance.Logger.LogError(message);
        }

        public static void LogMessage(string message, Color? serverColor = null, Color? clientColor = null)
        {
            LuaCsSetup.Instance.Logger.LogMessage(message, serverColor, clientColor);
        }

        public static void Log(string message, Color? color = null, ServerLog.MessageType messageType = ServerLog.MessageType.ServerMessage)
        {
            LuaCsSetup.Instance.Logger.Log(message, color, messageType);
        }
    }

    partial class LuaCsSetup
    {
        // Compatibility with cs mods that use this method.
        public static void PrintLuaError(object message) => LuaCsSetup.Instance.Logger.LogError($"{message}");
        public static void PrintCsError(object message) => LuaCsSetup.Instance.Logger.LogError($"{message}");
        public static void PrintGenericError(object message) => LuaCsSetup.Instance.Logger.LogError($"{message}");

        internal void PrintMessage(object message) => LuaCsSetup.Instance.Logger.LogMessage($"{message}");

        public static void PrintCsMessage(object message) => LuaCsSetup.Instance.Logger.LogMessage($"{message}");

        internal void HandleException(Exception ex, LuaCsMessageOrigin origin) => LuaCsSetup.Instance.Logger.HandleException(ex);
    }
}

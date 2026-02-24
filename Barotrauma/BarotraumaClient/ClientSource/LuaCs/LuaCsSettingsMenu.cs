using Microsoft.Xna.Framework;

namespace Barotrauma
{
    static class LuaCsSettingsMenu
    {
        private static GUIFrame frame;

        public static void Open(RectTransform rectTransform)
        {
            Close();

            frame = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.6f), rectTransform, Anchor.Center));

            GUIListBox list = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.95f), frame.RectTransform, Anchor.Center), false);

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), list.Content.RectTransform), "LuaCs Settings", textAlignment: Alignment.Center);


            new GUITickBox(new RectTransform(new Vector2(0.8f, 0.1f), list.Content.RectTransform), "Enable CSharp Scripting")
            {
                Selected = GameMain.LuaCs.Config.EnableCsScripting,
                ToolTip = "This enables CSharp Scripting for mods to use, WARNING: CSharp is NOT sandboxed, be careful with what mods you download.",
                OnSelected = (GUITickBox tick) =>
                {
                    GameMain.LuaCs.Config.EnableCsScripting = tick.Selected;
                    GameMain.LuaCs.WriteSettings();

                    return true;
                }
            };

            new GUITickBox(new RectTransform(new Vector2(0.8f, 0.1f), list.Content.RectTransform), "Treat Forced Mods As Normal")
            {
                Selected = GameMain.LuaCs.Config.TreatForcedModsAsNormal,
                ToolTip = "This makes mods that were setup to run even when disabled to only run when enabled.",
                OnSelected = (GUITickBox tick) =>
                {
                    GameMain.LuaCs.Config.TreatForcedModsAsNormal = tick.Selected;
                    GameMain.LuaCs.WriteSettings();

                    return true;
                }
            };

            new GUITickBox(new RectTransform(new Vector2(0.8f, 0.1f), list.Content.RectTransform), "Prefer To Use Workshop Lua Setup")
            {
                Selected = GameMain.LuaCs.Config.PreferToUseWorkshopLuaSetup,
                ToolTip = "This makes Lua look first for the Lua/LuaSetup.lua located in the Workshop package instead of the one located locally.",
                OnSelected = (GUITickBox tick) =>
                {
                    GameMain.LuaCs.Config.PreferToUseWorkshopLuaSetup = tick.Selected;
                    GameMain.LuaCs.WriteSettings();

                    return true;
                }
            };

            new GUITickBox(new RectTransform(new Vector2(0.8f, 0.1f), list.Content.RectTransform), "Disable Error GUI Overlay")
            {
                Selected = GameMain.LuaCs.Config.DisableErrorGUIOverlay,
                ToolTip = "",
                OnSelected = (GUITickBox tick) =>
                {
                    GameMain.LuaCs.Config.DisableErrorGUIOverlay = tick.Selected;
                    GameMain.LuaCs.WriteSettings();

                    return true;
                }
            };

            new GUITickBox(new RectTransform(new Vector2(0.8f, 0.1f), list.Content.RectTransform), "Hide usernames In Error Logs")
            {
                Selected = GameMain.LuaCs.Config.HideUserNames,
                ToolTip = "Hides the operating system username when displaying error logs (eg your username on windows).",
                OnSelected = (GUITickBox tick) =>
                {
                    GameMain.LuaCs.Config.HideUserNames = tick.Selected;
                    GameMain.LuaCs.WriteSettings();

                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1f, 0.1f), list.Content.RectTransform), $"Remove Client-Side ProjectEP", style: "GUIButtonSmall")
            {
                ToolTip = "Remove Client-Side ProjectEP.",
                OnClicked = (tb, userdata) =>
                {
                    LuaCsInstaller.Uninstall();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.8f, 0.01f), frame.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0f, 0.05f)
            }, "Close")
            {
                OnClicked = (GUIButton button, object obj) =>
                {
                    Close();

                    return true;
                }
            };
        }

        public static void Close()
        {
            frame?.Parent.RemoveChild(frame);
            frame = null;
        }
    }
}

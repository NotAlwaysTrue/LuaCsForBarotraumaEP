using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.LuaCs.Events;
using FluentResults;
using HarmonyLib;
using Microsoft.Xna.Framework;

[HarmonyPatch]
internal class MainMenuPatch : ISystem, IEventScreenSelected
{
    public bool IsDisposed { get; private set; }

    private readonly IEventService _eventService;

    private bool mainMenuUIAdded = false;

    public MainMenuPatch(IEventService eventService)
    {
        _eventService = eventService;

        RegisterEvents();

#if CLIENT
        if (Screen.Selected is MainMenuScreen mainMenuScreen)
        {
            AddToMainMenu(mainMenuScreen);
        }
#endif
    }

    public void OnScreenSelected(Screen screen)
    {
#if CLIENT
        if (screen is MainMenuScreen mainMenuScreen)
        {
            AddToMainMenu(mainMenuScreen);
        }
#endif
    }

#if CLIENT
    private void AddToMainMenu(MainMenuScreen screen)
    {
        if (mainMenuUIAdded) { return; }

        var textBlock = new GUITextBlock(new RectTransform(new Point(300, 30), screen.Frame.RectTransform, Anchor.TopLeft) { AbsoluteOffset = new Point(10, 10) }, "", Color.Red)
        {
            IgnoreLayoutGroups = false
        };

        textBlock.OnAddedToGUIUpdateList = (GUIComponent component) =>
        {
            string mode = LuaCsSetup.Instance.CsRunPolicyValue;

            if (mode is "Prompt")
            {
                string sessionState = LuaCsSetup.Instance.IsCsEnabledForSession ? "yes" : "no";
                mode = $"enabled (prompt mode, allowed for this session: {sessionState})";
            }
            else if (mode is "Enabled")
            {
                mode = "always enabled";
            }
            else
            {
                mode = "disabled";
            }

            textBlock.Text = $"LuaCsForBarotrauma active (revision {AssemblyInfo.GitRevision}), C# is currently {mode}\nNew settings available in the game settings menu.";
        };

        mainMenuUIAdded = true;
    }
#endif

    private void RegisterEvents()
    {
        _eventService.Subscribe<IEventScreenSelected>(this);
    }

    public void Dispose()
    {
        _eventService.Unsubscribe<IEventScreenSelected>(this);

        IsDisposed = true;
    }

    public FluentResults.Result Reset()
    {
        RegisterEvents();

        return FluentResults.Result.Ok();
    }
}

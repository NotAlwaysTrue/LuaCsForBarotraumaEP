using System;
using System.Linq;
using Barotrauma.Extensions;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs;

public class SettingsMenuSystem : ISettingsMenuSystem
{
    
    private ModsControlsSettingsMenu _controlsMenuInstance;
    private ModsGameplaySettingsMenu _gameplayMenuInstance;
    private GUIFrame _gameplayContentFrame;
    private GUIFrame _controlsContentFrame;
    private SettingsMenu _settingsMenuInstance;
    
    private readonly Harmony _harmony;
    private readonly IPackageManagementService _packageManagementService;
    private readonly IConfigService _configService;
    private readonly ILoggerService _loggerService;
    private static SettingsMenuSystem SystemInstance;
    
    public SettingsMenuSystem(IPackageManagementService packageManagementService, IConfigService configService, ILoggerService loggerService)
    {
        _packageManagementService = packageManagementService;
        _configService = configService;
        _loggerService = loggerService;
        SystemInstance = this;
        _harmony = Harmony.CreateAndPatchAll(typeof(SettingsMenuSystem));
    }

    [HarmonyPatch(typeof(SettingsMenu), "CreateModsTab"), HarmonyPostfix]
    private static void SettingsMenu_CreateModsTab_Post(SettingsMenu __instance)
    {
        SystemInstance._settingsMenuInstance = __instance;
        SystemInstance.CreateSettingsMenu(__instance);
    }

    private void CreateSettingsMenu(SettingsMenu __instance)
    {
        DisposeMenuFrames();
        
        var tabCount = Enum.GetValues<SettingsMenu.Tab>().Length;
        var tabGameplayIndex = (SettingsMenu.Tab)tabCount;
        var tabControlsIndex = (SettingsMenu.Tab)tabCount+1;

        _gameplayContentFrame = CreateNewContentTab(tabGameplayIndex, __instance,
            GUIStyle.ComponentStyles.ContainsKey("SettingsMenuTab.LuaCsSettings") ? "SettingsMenuTab.LuaCsSettings" : "SettingsMenuTab.Mods",
            $"{LuaCsSetup.PackageName}.SettingsMenu.ModGameplayButton");
        /*_controlsContentFrame = CreateNewContentTab(tabControlsIndex, __instance, 
            "SettingsMenuTab.Controls", $"{LuaCsSetup.PackageName}.SettingsMenu.ModControlsButton");
            */

        _gameplayMenuInstance = new ModsGameplaySettingsMenu(_gameplayContentFrame, _packageManagementService, _configService, _loggerService, __instance);
        //_controlsMenuInstance = new ModsControlsSettingsMenu(_controlsContentFrame, _packageManagementService, _configService, __instance);
    }
    
    private GUIFrame CreateNewContentTab(SettingsMenu.Tab tab, SettingsMenu settingsMenuInstance, string settingsMenuTabName, string settingMenuHoverTextIdent)
    {
        if (settingsMenuInstance.tabContents.TryGetValue(tab, out (GUIButton Button, GUIFrame Content) tabContent))
        {
            return tabContent.Content;
        }

        var contentFr = new GUIFrame(new RectTransform(Vector2.One * 0.95f, settingsMenuInstance.contentFrame.RectTransform, Anchor.Center, Pivot.Center), style: null);
            
        var button = new GUIButton(new RectTransform(Vector2.One, settingsMenuInstance.tabber.RectTransform, 
            Anchor.TopLeft, Pivot.TopLeft, scaleBasis: ScaleBasis.Smallest), "", style: settingsMenuTabName)
        {
            ToolTip = TextManager.Get(settingMenuHoverTextIdent),
            OnClicked = (b, _) =>
            {
                settingsMenuInstance.SelectTab(tab);
                return false;
            }
        };
        button.RectTransform.MaxSize = RectTransform.MaxPoint;
        button.Children.ForEach(c => c.RectTransform.MaxSize = RectTransform.MaxPoint);
            
        settingsMenuInstance.tabContents.Add(tab, (button, contentFr));

        return contentFr;
    }


    [HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.ApplyInstalledModChanges)), HarmonyPostfix]
    private static void SettingsMenu_ApplyInstalledModChanges_Post()
    {
        SystemInstance._gameplayMenuInstance?.ApplyInstalledModChanges();
        SystemInstance._controlsMenuInstance?.ApplyInstalledModChanges();
    }

    private void DisposeMenuFrames()
    {
        _controlsMenuInstance?.Dispose();
        _gameplayMenuInstance?.Dispose();
        _controlsMenuInstance = null;
        _gameplayMenuInstance = null;
    }
    
    #region DISPOSAL

    public void Dispose()
    {
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }
        DisposeMenuFrames();
        GC.SuppressFinalize(this);
    }
    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }
    public FluentResults.Result Reset()
    {
        throw new NotImplementedException();
    }

    #endregion
}

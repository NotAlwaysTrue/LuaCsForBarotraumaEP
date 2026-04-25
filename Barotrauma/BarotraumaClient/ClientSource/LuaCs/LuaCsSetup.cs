using Barotrauma.CharacterEditor;
using Barotrauma.Extensions;
using Barotrauma.LuaCs;
using Barotrauma.LuaCs.Data;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using static System.Collections.Specialized.BitVector32;

// ReSharper disable ObjectCreationAsStatement

namespace Barotrauma
{
    partial class LuaCsSetup
    {        
        public void PromptCSharpMods(Action<bool> onSelection, bool joiningServer)
        {
            ImmutableArray<ContentPackage> contentPackages = PackageManagementService.GetLoadedUnrestrictedPackages()
                .Where(p => p.Name != PackageName)
                .ToImmutableArray();

            if (_csRunPolicy?.Value is "Enabled")
            {
                IsCsEnabledForSession = true;
                onSelection(true);
                return;
            }
            else if (_csRunPolicy?.Value is "Disabled")
            {
                IsCsEnabledForSession = false;
                onSelection(false);
                return;
            }

            if (contentPackages.None())
            {
                onSelection(true);
                return;
            }

            GUIMessageBox messageBox = new GUIMessageBox(
                TextManager.Get("warning"),
                relativeSize: new Vector2(0.3f, 0.55f),
                minSize: new Point(400, 500),
                text: string.Empty,
                buttons: []);

            GUILayoutGroup msgBoxLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.75f), messageBox.Content.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBoxLayout.RectTransform), "The following mods contain CSharp code OR Unsandboxed Lua Code",
                font: GUIStyle.SubHeadingFont, wrap: true, textAlignment: Alignment.Center);

            GUIListBox packageListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), msgBoxLayout.RectTransform))
            {
                CurrentSelectMode = GUIListBox.SelectMode.None
            };

            foreach (ContentPackage package in contentPackages)
            {
                GUIFrame packageFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), packageListBox.Content.RectTransform), style: "ListBoxElement");
                GUILayoutGroup packageLayout = new GUILayoutGroup(new RectTransform(Vector2.One, packageFrame.RectTransform), true, Anchor.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(0.7f, 1f), packageLayout.RectTransform), package.Name);
                new GUIButton(new RectTransform(new Vector2(0.3f, 1f), packageLayout.RectTransform, Anchor.CenterRight), "Open Folder", style: "GUIButtonSmall")
                {
                    OnClicked = (GUIButton button, object obj) =>
                    {
                        string directory = package.Dir;
                        if (string.IsNullOrEmpty(directory)) { return false; }

                        ToolBox.OpenFileWithShell(directory);
                        return true;
                    }
                };
            }

            string bodyText =
                joiningServer ?
                "You are joining a server that includes mods with C# code OR unrestricted Lua code. These mods are not sandboxed and may access your computer without restrictions. If you trust these mods, select 'Enable C# for this session'. Otherwise, select 'Cancel' to run only Lua mods."
                : "You have enabled mods that include C# code. These mods are not sandboxed and may access your computer without restrictions. If you trust these mods, select 'Enable C# for this session'. Otherwise, select 'Cancel' to run only Sandboxed Lua mods.";

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0f), msgBoxLayout.RectTransform), bodyText, wrap: true)
            {
                Wrap = true
            };

            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), messageBox.Content.RectTransform, Anchor.BottomCenter), isHorizontal: false, childAnchor: Anchor.TopCenter);

            new GUIButton(new RectTransform(new Vector2(0.8f, 0.0f), buttonLayout.RectTransform), "Enable C# for this session")
            {
                TextBlock = { AutoScaleHorizontal = true },
                OnClicked = (btn, userdata) =>
                {
                    IsCsEnabledForSession = true;
                    onSelection(true);
                    messageBox.Close();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.8f, 0.0f), buttonLayout.RectTransform), "Cancel")
            {
                OnClicked = (btn, userdata) =>
                {
                    IsCsEnabledForSession = false;
                    onSelection(false);
                    messageBox.Close();
                    return true;
                }
            };
        }

        private void SetupServicesProviderClient(IServicesProvider serviceProvider)
        {
            serviceProvider.RegisterServiceType<IUIStylesService, UIStylesService>(ServiceLifetime.Singleton);
            // supplied via factory
            //serviceProvider.RegisterServiceType<IUIStylesCollection, UIStylesCollection>(ServiceLifetime.Transient);
            serviceProvider.RegisterServiceType<IParserServiceAsync<ResourceParserInfo, IStylesResourceInfo>, ModConfigFileParserService>(ServiceLifetime.Transient);
            serviceProvider.RegisterServiceType<IUIStylesCollection.IFactory, UIStylesCollection.Factory>(ServiceLifetime.Transient);
            serviceProvider.RegisterServiceType<ISettingsMenuSystem, SettingsMenuSystem>(ServiceLifetime.Singleton);
        }

        /// <summary>
        /// Handles changes in game states tracked by screen changes.
        /// </summary>
        /// <param name="screen">The new game screen.</param>
        public partial void OnScreenSelected(Screen screen)
        {
            /*Note: This logic needs to be run after the triggering event so that recursion scenarios (ie. resetting the EventService)
             do not occur, so we delay it by one game tick.*/
            CoroutineManager.Invoke(() =>
            {
                switch (screen)
                {
                    // menus and navigation states
                    case MainMenuScreen:
                    case ModDownloadScreen:
                    case ServerListScreen:
                        SetRunState(RunState.Unloaded);
                        SetRunState(RunState.LoadedNoExec);
                        break;
                    // running lobby or editor states
                    case CampaignEndScreen:
                    case CharacterEditorScreen:
                    case EventEditorScreen:
                    case GameScreen:
                    case LevelEditorScreen:
                    case NetLobbyScreen:
                    case ParticleEditorScreen:
                    case RoundSummaryScreen:
                    case SpriteEditorScreen:
                    case SubEditorScreen:
                    case TestScreen: // notes: TestScreen is a Linux edge case editor screen and is deprecated.

                        if (screen is NetLobbyScreen && CurrentRunState != RunState.Running && GameMain.Client?.ClientPeer is not P2POwnerPeer)
                        {
                            PromptCSharpMods(selection =>
                            {
                                SetRunState(RunState.Running);
                            }, joiningServer: true);
                        }
                        else
                        {
                            SetRunState(RunState.Running);
                        }
                        break;
                    default:
                        Logger.LogError(
                            $"{nameof(LuaCsSetup)}: Received an unknown screen {screen?.GetType().Name ?? "'null screen'"}. Retarding load state to 'unloaded'.");
                        SetRunState(RunState.Unloaded);
                        break;
                }
            }, delay: 0f); // min is one tick delay.
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Numerics;
using Barotrauma.LuaCs.Data;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Vector4 = Microsoft.Xna.Framework.Vector4;

// ReSharper disable ObjectCreationAsStatement

namespace Barotrauma.LuaCs;

internal sealed class ModsGameplaySettingsMenu : ModsSettingsMenuBase
{
    private ImmutableArray<ISettingBase> _settingsInstancesGameplay;
    // menu vars
    private GUILayoutGroup _modCategoryDisplayGroup, _settingsDisplayGroup;
    private string _selectedSearchQuery = string.Empty;
    private ContentPackage _selectedContentPackage;
    private string _selectedCategory = string.Empty;
    private ImmutableArray<ISettingBase> _currentlyDisplayedSettings;
    private ILoggerService _loggerService;
    
    private bool _promptOpen = false;
    
    
    // Note: "static" instead of "const" for Hot Reload and to allow changing at runtime.
    // ReSharper disable FieldCanBeMadeReadOnly.Local
    
    // --- UI controls ---
    private static float MenuTitleHeight = 0.06f; // (ContentDisplayAreaHeightContainer + MenuTitleHeight) < 1f 
    private static float ContentDisplayAreaHeightContainer = 0.93f;
    private static float ContentDisplayAreaHeightInnerCategories = 0.99f;
    private static float ContentDisplayAreaHeightInnerSettings = 0.97f;
    private static float ContentLeftRightSplitPosition = 0.3f;
    
    // Search Bar
    private static float SearchBarLayoutHeight = 0.06f;
    private static float SearchBarLabelWidth = 0.1f;
    private static float SearchBarLabelBoxSpacing = 0.05f;
    
    private static float SearchBarTextBoxWidth = 1f - SearchBarLabelWidth - SearchBarLabelBoxSpacing;
    
    // Categories, Packages Display Area
    private static float CategoriesDisplayListHeight = 0.945f;
    private static float CategoryButtonHeightRelative = 0.122f;
    private static float PackageSelectionButtonHeight = 0.07f;

    private static Color CategoryButtonHoverSelectColor = new Color(50, 50, 50, 255);
    private static Color CategoryButtonTextColor = Color.PeachPuff;
    private static Color CategoryButtonTextColorSelected = Color.White;
    private static Color CategoryButtonColorPressed = Color.TransparentBlack;
    
    // Settings Display Area
    private static float SettingLabelWidth = 0.6f;
    private static float SettingControlWidth = 0.4f;
    private static float SettingHeight = 0.05625f/ContentDisplayAreaHeightContainer/ContentDisplayAreaHeightInnerSettings;
    private static Color SettingEntryLabelTextColor = Color.PeachPuff;
    private static string SettingGUIFrameStyle = "";
    private static Color? SettingGUIFrameColor = null;
    
    // settings reset
    private static Vector2 SettingsResetButtonTopSpacer = new Vector2(0f, 0.02f);
    private static Vector2 SettingsResetButtonDimensions = new Vector2(0.3f, 0.05f);
    private static string SettingsResetButtonStyle = "GUIButtonSmall";
    private static Color SettingsResetButtonColor = Color.DarkOliveGreen;
    private static Color SettingsResetButtonHoverColor = Color.Olive;
    private static Color SettingsResetButtonTextColor = Color.PeachPuff;
    private static Color SettingsResetButtonTextColorSelected = Color.White;

    private static Vector2 ResetConfirmationPromptDimensions = new Vector2(0.15f, 0.2f);
    
    
    // ReSharper restore FieldCanBeMadeReadOnly.Local
    private const string SettingsResetButtonText = $"{LuaCsSetup.PackageName}.SettingsMenu.ResetVisibleSettings";
    private const string SettingsResetPromptTitle = $"{LuaCsSetup.PackageName}.SettingsMenu.ResetPrompt.Title";
    private const string SettingsResetPromptContents = $"{LuaCsSetup.PackageName}.SettingsMenu.ResetPrompt.Message";
    private const string SettingsResetPromptYesText = $"{LuaCsSetup.PackageName}.SettingsMenu.ResetPrompt.Yes";
    private const string SettingsResetPromptNoText = $"{LuaCsSetup.PackageName}.SettingsMenu.ResetPrompt.No";
    
    
    private event Action OnApplyInstalledModsChanges;
    
    public ModsGameplaySettingsMenu(GUIFrame contentFrame, 
        IPackageManagementService packageManagementService, 
        IConfigService configService, 
        ILoggerService loggerService,
        SettingsMenu settingsMenuInstance) : base(contentFrame, packageManagementService, configService, settingsMenuInstance)
    {
        _settingsInstancesGameplay = configService.GetDisplayableConfigs()
            .ToImmutableArray();

        _loggerService = loggerService;
        
        var mainLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), contentFrame.RectTransform, Anchor.Center), false, Anchor.TopLeft);
        // page title
        var menuTitleLayoutGroup = new GUILayoutGroup(
                new RectTransform(new Vector2(1f, MenuTitleHeight), mainLayoutGroup.RectTransform, Anchor.TopLeft), true, Anchor.TopLeft);
        GUIUtil.Label(menuTitleLayoutGroup, 
            GetLocalizedString($"{LuaCsSetup.PackageName}.SettingsMenu.ModGameplayButton", "Mod Gameplay Settings"), 
            GUIStyle.LargeFont, new Vector2(1f, 1f));
        
        // page contents
        var contentAreaLayoutGroup = new GUILayoutGroup(
                new RectTransform(new Vector2(1f, 0.94f), mainLayoutGroup.RectTransform, Anchor.BottomLeft), false,
                Anchor.TopLeft);
        
        var searchBarLayoutGroup = new GUILayoutGroup(
            new RectTransform(new Vector2(1f, SearchBarLayoutHeight), contentAreaLayoutGroup.RectTransform, Anchor.TopCenter), true, Anchor.CenterLeft);
        GUIUtil.Label(searchBarLayoutGroup, "Search: ", GUIStyle.SubHeadingFont, new Vector2(SearchBarLabelWidth, 1f));
        var searchBar = new GUITextBox(
            new RectTransform(new Vector2(SearchBarTextBoxWidth, 0.1f), searchBarLayoutGroup.RectTransform, Anchor.TopLeft),
            createClearButton: true)
        {
            OnTextChangedDelegate = (btn, txt) =>
            {
                GenerateDisplayFromFilter(txt);
                return true;
            }
        };
        
        // main display area
        var settingsContentAreaGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, ContentDisplayAreaHeightContainer), contentAreaLayoutGroup.RectTransform, Anchor.BottomCenter));
        GUIUtil.Spacer(settingsContentAreaGroup, Vector2.One);
        (_modCategoryDisplayGroup, _settingsDisplayGroup) = GUIUtil.CreateSidebars(settingsContentAreaGroup, true);
        _modCategoryDisplayGroup.RectTransform.RelativeSize = new Vector2(ContentLeftRightSplitPosition, ContentDisplayAreaHeightInnerCategories);
        _settingsDisplayGroup.RectTransform.RelativeSize = new Vector2(1f-ContentLeftRightSplitPosition, ContentDisplayAreaHeightInnerSettings);
        
        // default category
        _selectedCategory = "All";

        OnApplyInstalledModsChanges = () =>
        {
            _settingsInstancesGameplay = configService.GetDisplayableConfigs()
                .ToImmutableArray();
            if (_selectedContentPackage is not null && !GetTargetPackagesList().Contains(_selectedContentPackage))
            {
                _selectedContentPackage = null;
                _selectedCategory = string.Empty;
            }
            
            GenerateCategoryListDisplay(_modCategoryDisplayGroup, GetTargetPackagesList(), GetDisplayCategoriesList());
            GenerateSettingsListDisplay(_settingsDisplayGroup, GetDisplaySettingsList());
        };
        
        GenerateCategoryListDisplay(_modCategoryDisplayGroup, GetTargetPackagesList(), GetDisplayCategoriesList());
        GenerateSettingsListDisplay(_settingsDisplayGroup, GetDisplaySettingsList());
        
        void GenerateDisplayFromFilter(string text)
        {
            _selectedSearchQuery = text;
            GenerateCategoryListDisplay(_modCategoryDisplayGroup, GetTargetPackagesList(), GetDisplayCategoriesList());
            GenerateSettingsListDisplay(_settingsDisplayGroup, GetDisplaySettingsList());
        }
        
        string GetLocalizedString(string identifier, string defaultValue)
        {
            var lstr = TextManager.Get(identifier);
            return lstr.IsNullOrWhiteSpace() ? defaultValue : lstr.Value;
        }

        // Filters by selected package and query text
        ImmutableArray<string> GetDisplayCategoriesList()
        {
            return GetFilteredSettingsList()
                .Select(s => GetLocalizedString(s.GetDisplayInfo().DisplayCategory, "General"))
                .Concat(new []{ "All" })
                .Distinct()
                .OrderBy(s => s)
                .ToImmutableArray();
        }

        // Filters by query text
        ImmutableArray<ContentPackage> GetTargetPackagesList()
        {
            return _settingsInstancesGameplay
                .Where(s => SettingMatchesQuery(s, _selectedSearchQuery))
                .Select(s => s.OwnerPackage)
                .Concat(new[] { ContentPackageManager.VanillaCorePackage })
                .Distinct()
                .OrderByDescending(p =>  p == ContentPackageManager.VanillaCorePackage ? 0 : 1)
                .ThenBy(p => p.Name)
                .ToImmutableArray();
        }

        // Filters by selected package, query text, and selected category.
        ImmutableArray<ISettingBase> GetDisplaySettingsList()
        {
            return GetFilteredSettingsList()
                .Where(s => _selectedCategory.IsNullOrWhiteSpace() 
                            || _selectedCategory == "All"
                            || GetLocalizedString(s.GetDisplayInfo().DisplayCategory, "General") == _selectedCategory)
                .OrderBy(s => GetLocalizedString(s.GetDisplayInfo().DisplayName, s.InternalName))
                .ToImmutableArray();
        }

        // Filters by selected package and by query text.
        ImmutableArray<ISettingBase> GetFilteredSettingsList()
        {
            return _settingsInstancesGameplay
                .Where(s => SettingMatchesQuery(s, _selectedSearchQuery))
                .Where(s => _selectedContentPackage is null 
                            || _selectedContentPackage == ContentPackageManager.VanillaCorePackage // vanilla is treated as all packages
                            || s.OwnerPackage == _selectedContentPackage)
                .OrderBy(s => GetLocalizedString(s.GetDisplayInfo().DisplayName, s.InternalName))
                .ToImmutableArray();
        }
        
        
        bool SettingMatchesQuery(ISettingBase setting, string queryText)
        {
            if (queryText.IsNullOrWhiteSpace())
            {
                return true;
            }
            
            queryText = queryText.ToLowerInvariant().Trim();

            if (setting.InternalName.ToLowerInvariant().Trim().Contains(queryText) || setting.OwnerPackage.Name.ToLowerInvariant().Trim().Contains(queryText))
            {
                return true;
            }

            var displayInfo = setting.GetDisplayInfo();
            return TextManager.Get(displayInfo.DisplayName).Value.ToLowerInvariant().Trim().Contains(queryText)
                || TextManager.Get(displayInfo.DisplayCategory).Value.ToLowerInvariant().Trim().Contains(queryText)
                || TextManager.Get(displayInfo.Description).Value.ToLowerInvariant().Trim().Contains(queryText)
                || TextManager.Get(displayInfo.Tooltip).Value.ToLowerInvariant().Trim().Contains(queryText);
        }

        string GetPackageName(ContentPackage package)
        {
            return package is null || package == ContentPackageManager.VanillaCorePackage ? "All" : package.Name;
        }

        ContentPackage GetCurrentSelectedPackage(ImmutableArray<ContentPackage> packages)
        {
            if (_selectedContentPackage is null)
            {
                return ContentPackageManager.VanillaCorePackage;
            }
            
            if (packages.Contains(_selectedContentPackage))
            {
                return _selectedContentPackage;
            }

            if (packages.Length > 0)
            {
                _selectedContentPackage = packages[0];
                return packages[0];
            }

            return null;
        }

        void GenerateCategoryListDisplay(GUILayoutGroup layoutGroup, ImmutableArray<ContentPackage> packagesList, 
            ImmutableArray<string> categories)
        {
            layoutGroup.ClearChildren();
            var packageSelectionList = GUIUtil.Dropdown<ContentPackage>(layoutGroup, cp => GetPackageName(cp), null,
                packagesList, GetCurrentSelectedPackage(packagesList), cp =>
                {
                    _selectedContentPackage = cp;
                    _selectedCategory = string.Empty; 
                    GenerateCategoryListDisplay(_modCategoryDisplayGroup, GetTargetPackagesList(), GetDisplayCategoriesList());
                    GenerateSettingsListDisplay(_settingsDisplayGroup, GetDisplaySettingsList());
                }, new Vector2(1f, PackageSelectionButtonHeight));
            var containerBox = new GUIListBox(new RectTransform(new Vector2(1f, CategoriesDisplayListHeight), layoutGroup.RectTransform));
            
            
            float sizeY = MathF.Max(categories.Length * CategoryButtonHeightRelative, 1f);
            var displayedCategoriesFrame = new GUIFrame(new RectTransform(new Vector2(1f, sizeY), containerBox.Content.RectTransform), style: null, color: Color.Black)
            {
                CanBeFocused = false
            };
            var displayCategoriesLayout = new GUILayoutGroup(new RectTransform(Vector2.One, displayedCategoriesFrame.RectTransform));

            foreach (var category in categories)
            {
                var btn = new GUIButton(new RectTransform(new Vector2(1f, CategoryButtonHeightRelative), displayCategoriesLayout.RectTransform), 
                    text: category, color: Color.TransparentBlack)
                {
                    CanBeFocused = true,
                    CanBeSelected = true,
                    TextColor = CategoryButtonTextColor,
                    HoverColor = CategoryButtonHoverSelectColor,
                    HoverTextColor = CategoryButtonTextColorSelected,
                    PressedColor = CategoryButtonColorPressed,
                    SelectedColor = CategoryButtonHoverSelectColor,
                    SelectedTextColor = CategoryButtonHoverSelectColor,
                    OnClicked = (btn, obj) =>
                    {
                        _selectedCategory = category;
                        GenerateSettingsListDisplay(_settingsDisplayGroup, GetDisplaySettingsList());
                        return true;
                    }
                };
            }
        }

        void GenerateSettingsListDisplay(GUILayoutGroup layoutGroup, ImmutableArray<ISettingBase> settings) 
        {
            layoutGroup.ClearChildren();
            _currentlyDisplayedSettings = settings;
            
            var containerBox = new GUIListBox(new RectTransform(new Vector2(1f, 1f-SettingsResetButtonDimensions.Y), layoutGroup.RectTransform));
            foreach (var setting in settings)
            {
                var entry = AddSettingToDisplay(
                    setting,
                    containerBox.Content.RectTransform,
                    settingHeight: SettingHeight,
                    labelSize: new Vector2(SettingLabelWidth, 1f),
                    controlSize: new Vector2(SettingControlWidth, 1f));
            }

            var spacer = new GUIFrame(new RectTransform(SettingsResetButtonTopSpacer, layoutGroup.RectTransform),
                style: null, color: Color.TransparentBlack);

            var resetSettingsButton = new GUIButton(
                new RectTransform(SettingsResetButtonDimensions, layoutGroup.RectTransform),
                GetLocalizedString(SettingsResetButtonText, "Reset Visible Settings"),
                style: SettingsResetButtonStyle)
            {
                CanBeSelected = true,
                CanBeFocused = true,
                Color = SettingsResetButtonColor,
                HoverColor = SettingsResetButtonHoverColor,
                SelectedColor = SettingsResetButtonHoverColor,
                SelectedTextColor = SettingsResetButtonTextColorSelected,
                TextColor = SettingsResetButtonTextColor,
                OnClicked = (btn, obj) =>
                {
                    DisplayResetConfirmationPrompt(settings);
                    return true;
                }
            };
        }
        
        (GUIFrame entryFrame, GUILayoutGroup entryLayoutGroup) 
            AddSettingToDisplay(ISettingBase setting, RectTransform parent, float settingHeight, Vector2 labelSize, Vector2 controlSize)
        {
            GUIFrame entryFrame = new GUIFrame(new RectTransform(new Vector2(1f, settingHeight), parent), 
                style: SettingGUIFrameStyle, color: SettingGUIFrameColor)
            {
                Color = Color.DarkGray
            };
            GUILayoutGroup entryLayoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, entryFrame.RectTransform), isHorizontal: true);

            // padding
            new GUIFrame(new RectTransform(new Vector2(0.02f, 1f), entryLayoutGroup.RectTransform),
                color: Color.TransparentBlack);
            
            // setting label
            new GUITextBlock(new RectTransform(labelSize - new Vector2(0.05f, 0f), entryLayoutGroup.RectTransform),
                GetLocalizedString(setting.GetDisplayInfo().DisplayName, setting.GetDisplayInfo().DisplayName),
                textColor: SettingEntryLabelTextColor,
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.Left)
            {
                ToolTip = GetLocalizedString(setting.GetDisplayInfo().Tooltip, string.Empty)
            };

            setting.AddDisplayComponent(entryLayoutGroup, controlSize, newValue =>
            {
                NewValuesCache[setting] = newValue;
            });
            return (entryFrame, entryLayoutGroup);
        }

        void DisplayResetConfirmationPrompt(ImmutableArray<ISettingBase> settings)
        {
            if (_promptOpen)
            {
                return;
            }

            _promptOpen = true;
            
            var msgBox = new GUIMessageBox(GetLocalizedString(SettingsResetPromptTitle, "Reset Visible Settings"),
                GetLocalizedString(SettingsResetPromptContents,
                    "Are you sure you want to reset the values for currently displayed settings?"),
                new LocalizedString[]
                {
                    GetLocalizedString(SettingsResetPromptYesText, "Yes"),
                    GetLocalizedString(SettingsResetPromptNoText, "No")
                }, ResetConfirmationPromptDimensions);
            msgBox.Buttons[0].OnClicked = (btn, obj) =>
            {
                ResetValuesForDisplayedSettings(settings);
                btn.Visible = false;
                _promptOpen = false;
                msgBox.Close();
                return true;
            };
            msgBox.Buttons[1].OnClicked = (btn, obj) =>
            {
                btn.Visible = false;
                _promptOpen = false;
                msgBox.Close();
                return true;
            };
        }
        
        void ResetValuesForDisplayedSettings(ImmutableArray<ISettingBase> settings)
        {
            if (settings.IsDefaultOrEmpty)
            {
                return;
            }

            NewValuesCache.Clear();
            foreach (var setting in settings)
            {
                var str = setting.GetDefaultStringValue();
                NewValuesCache[setting] = str;
                loggerService.LogDebug($"Resetting value for {setting.InternalName} to '{str}'");
            }
            
            ApplyInstalledModChanges();
        }
    }


    protected override void DisposeInternal()
    {
        NewValuesCache.Clear();
        _modCategoryDisplayGroup?.Parent.RemoveChild(_modCategoryDisplayGroup);
        _settingsDisplayGroup?.Parent.RemoveChild(_settingsDisplayGroup);
        _modCategoryDisplayGroup = null;
        _settingsDisplayGroup = null;
        
    }

    public override void ApplyInstalledModChanges()
    {
        foreach (var kvp in NewValuesCache)
        {
            if (kvp.Key.IsDisposed)
            {
                continue;
            }

            var success = kvp.Key.TrySetSerializedValue(kvp.Value);
            if (success)
            {
                ConfigService.SaveConfigValue(kvp.Key);
                _loggerService.LogDebug($"Applied save value for {kvp.Key.InternalName} of {kvp.Value.ToString()}");
            }
        }
        NewValuesCache.Clear();
        OnApplyInstalledModsChanges?.Invoke();
    }
}

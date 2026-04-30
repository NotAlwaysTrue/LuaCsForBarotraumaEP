using System;
using System.Collections.Generic;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
#nullable enable

namespace Barotrauma.LuaCs;

/// <summary>
/// A collection of helper GUI functions. Mostly ripped from "Barotrauma/ClientSource/Settings/SettingsMenu.cs"
/// </summary>
public static class GUIUtil
{
    public static (GUILayoutGroup Left, GUILayoutGroup Right) CreateSidebars(GUIFrame parent, bool split = false)
    {
        GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform), isHorizontal: true);
        GUILayoutGroup left = new GUILayoutGroup(new RectTransform((0.4875f, 1.0f), layout.RectTransform), isHorizontal: false);
        var centerFrame = new GUIFrame(new RectTransform((0.025f, 1.0f), layout.RectTransform), style: null);
        if (split)
        {
            new GUICustomComponent(new RectTransform(Vector2.One, centerFrame.RectTransform),
                onDraw: (sb, c) =>
                {
                    sb.DrawLine((c.Rect.Center.X, c.Rect.Top),
                        (c.Rect.Center.X, c.Rect.Bottom),
                        GUIStyle.TextColorDim,
                        2f);
                });
        }
        GUILayoutGroup right = new GUILayoutGroup(new RectTransform((0.4875f, 1.0f), layout.RectTransform), isHorizontal: false);
        return (left, right);
    }
    
    public static (GUILayoutGroup Left, GUILayoutGroup Right) CreateSidebars(GUILayoutGroup parent, bool split = false)
    {
        GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform), isHorizontal: true);
        GUILayoutGroup left = new GUILayoutGroup(new RectTransform((0.4875f, 1.0f), layout.RectTransform), isHorizontal: false);
        var centerFrame = new GUIFrame(new RectTransform((0.025f, 1.0f), layout.RectTransform), style: null);
        if (split)
        {
            new GUICustomComponent(new RectTransform(Vector2.One, centerFrame.RectTransform),
                onDraw: (sb, c) =>
                {
                    sb.DrawLine((c.Rect.Center.X, c.Rect.Top),
                        (c.Rect.Center.X, c.Rect.Bottom),
                        GUIStyle.TextColorDim,
                        2f);
                });
        }
        GUILayoutGroup right = new GUILayoutGroup(new RectTransform((0.4875f, 1.0f), layout.RectTransform), isHorizontal: false);
        return (left, right);
    }
    
    public static GUILayoutGroup CreateCenterLayout(GUIFrame parent)
        => new GUILayoutGroup(new RectTransform((0.5f, 1.0f), parent.RectTransform, Anchor.TopCenter, Pivot.TopCenter)) { ChildAnchor = Anchor.TopCenter };

    public static RectTransform NewItemRectT(GUILayoutGroup parent, Vector2 adjustRatio)
        => new RectTransform((1.0f * adjustRatio.X, 0.06f * adjustRatio.Y), parent.RectTransform, Anchor.CenterLeft);
    
    public static void Spacer(GUILayoutGroup parent, Vector2 adjustRatio) 
        => new GUIFrame(new RectTransform((1.0f * adjustRatio.X, 0.03f * adjustRatio.Y), parent.RectTransform, Anchor.CenterLeft), style: null);
    
    public static void ClearChildElements(GUIComponent component, bool clearSelfFromParent = false)
    {
        component.GetAllChildren().ForEachMod(c =>
        {
            c.Visible = false;
            component.RemoveChild(c);
        });
        if (clearSelfFromParent && component.Parent is not null)
            component.Parent.RemoveChild(component);
    }
    
    public static GUITextBlock Label(GUILayoutGroup parent, LocalizedString str, GUIFont font, Vector2 adjustRatio)
        => new GUITextBlock(NewItemRectT(parent, adjustRatio), str, font: font);
    
    public static GUIDropDown DropdownEnum<T>(GUILayoutGroup parent, Func<T, LocalizedString> textFunc, Func<T, LocalizedString>? tooltipFunc, T currentValue,
            Action<T> setter, Vector2 adjustRatio) where T : Enum
            => Dropdown(parent, textFunc, tooltipFunc, (T[])Enum.GetValues(typeof(T)), currentValue, setter, adjustRatio);
        
    public static GUIDropDown Dropdown<T>(GUILayoutGroup parent, Func<T, LocalizedString> textFunc, Func<T, 
        LocalizedString>? tooltipFunc, IReadOnlyList<T> values, T currentValue, Action<T> setter, Vector2 adjustRatio, float listBoxScale = 1)
    {
        var dropdown = new GUIDropDown(NewItemRectT(parent, adjustRatio), listBoxScale: listBoxScale);
        values.ForEach(v => dropdown.AddItem(text: textFunc(v), userData: v, toolTip: tooltipFunc?.Invoke(v) ?? null));
        int childIndex = values.IndexOf(currentValue);
        dropdown.Select(childIndex);
        dropdown.ListBox.ForceLayoutRecalculation();
        dropdown.ListBox.ScrollToElement(dropdown.ListBox.Content.GetChild(childIndex));
        dropdown.OnSelected = (dd, obj) =>
        {
            setter((T)obj);
            return true;
        };
        return dropdown;
    }

    public static (GUIScrollBar, GUITextBlock) Slider(GUILayoutGroup parent, Vector2 range, int steps, Func<float, 
        string> labelFunc, float currentValue, Action<float> setter, LocalizedString? tooltip, Vector2 adjustRatio)
    {
        var layout = new GUILayoutGroup(new RectTransform(adjustRatio, parent.RectTransform), isHorizontal: true);
        var slider = new GUIScrollBar(new RectTransform((0.72f, 1.0f), layout.RectTransform), style: "GUISlider")
        {
            Range = range,
            BarScrollValue = currentValue,
            Step = 1.0f / (float)(steps - 1),
            BarSize = 1.0f / steps
        };
        if (tooltip != null)
        {
            slider.ToolTip = tooltip;
        }
        var label = new GUITextBlock(new RectTransform((0.28f, 1.0f), layout.RectTransform),
            labelFunc(currentValue), wrap: false, textAlignment: Alignment.Center);
        slider.OnMoved = (sb, val) =>
        {
            label.Text = labelFunc(sb.BarScrollValue);
            setter(sb.BarScrollValue);
            return true;
        };
        return (slider, label);
    }

    public static GUITickBox Tickbox(GUILayoutGroup parent, LocalizedString label, LocalizedString tooltip, 
        bool currentValue, Action<bool> setter, Vector2 adjustRatio)
    {
        var tickbox = new GUITickBox(NewItemRectT(parent, adjustRatio), label)
        {
            Selected = currentValue,
            ToolTip = tooltip,
            OnSelected = (tb) =>
            {
                setter(tb.Selected);
                return true;
            }
        };
        return tickbox;
    }
    
    public static string Percentage(float v) => ToolBox.GetFormattedPercentage(v);

    public static int Round(float v) => (int)MathF.Round(v);
}

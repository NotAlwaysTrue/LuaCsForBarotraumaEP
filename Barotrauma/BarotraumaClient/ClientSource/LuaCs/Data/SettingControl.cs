using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.LuaCs.Data;
using Microsoft.Toolkit.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OneOf;

namespace Barotrauma.LuaCs.Data;

public sealed class SettingControl : SettingBase, ISettingControl
{
    public class Factory : ISettingBase.IFactory<ISettingBase>
    {
        public ISettingBase CreateInstance(IConfigInfo configInfo, Func<OneOf<string, XElement, object>, bool> valueChangePredicate)
        {
            Guard.IsNotNull(configInfo, nameof(configInfo));
            return new SettingControl(configInfo, valueChangePredicate);
        }
    }
    
    public SettingControl(IConfigInfo configInfo,  Func<OneOf<string, XElement, object>, bool> valueChangePredicate) : base(configInfo)
    {
        _valueChangePredicate = valueChangePredicate;
        TrySetSerializedValue(configInfo.Element);
    }

    protected override void OnDispose()
    {
        OnValueChanged = null;
    }

    private Func<OneOf<string, XElement, object>, bool> _valueChangePredicate;
    public override Type GetValueType() => typeof(KeyOrMouse);
    public override string GetStringValue() => Value.ToString();
    public override string GetDefaultStringValue() => new KeyOrMouse(Keys.NumLock).ToString();

    public override bool TrySetSerializedValue(OneOf<string, XElement> value)
    {
        var newVal = value.Match<KeyOrMouse>(
            (string v) => GetKeyOrMouse(v), 
            (XElement e) => e.GetAttributeKeyOrMouse("Value", null));
        
        if (newVal is null)
        {
            return false;
        }

        if (_valueChangePredicate is not null && !_valueChangePredicate.Invoke(newVal))
        {
            return false;
        }

        Value = newVal;
        OnValueChanged?.Invoke(this);
        return true;

        KeyOrMouse GetKeyOrMouse(string strValue)
        {
            strValue ??= string.Empty;
            if (Enum.TryParse(strValue, true, out Microsoft.Xna.Framework.Input.Keys key))
            {
                return key;
            }
            else if (Enum.TryParse(strValue, out MouseButton mouseButton))
            {
                return mouseButton;
            }
            else if (int.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out int mouseButtonInt) &&
                     Enum.GetValues<MouseButton>().Contains((MouseButton)mouseButtonInt))
            {
                return (MouseButton)mouseButtonInt;
            }
            else if (string.Equals(strValue, "LeftMouse", StringComparison.OrdinalIgnoreCase))
            {
                return !PlayerInput.MouseButtonsSwapped() ? MouseButton.PrimaryMouse : MouseButton.SecondaryMouse;
            }
            else if (string.Equals(strValue, "RightMouse", StringComparison.OrdinalIgnoreCase))
            {
                return !PlayerInput.MouseButtonsSwapped() ? MouseButton.SecondaryMouse : MouseButton.PrimaryMouse;
            }

            return null;
        }

    }

    public override event Action<ISettingBase> OnValueChanged;
    public override OneOf<string, XElement> GetSerializableValue() => Value.ToString();
    public KeyOrMouse Value { get; private set; } = new KeyOrMouse(Keys.NumLock);
    
    public bool TrySetValue(KeyOrMouse value)
    {
        Value = value;
        OnValueChanged?.Invoke(this);
        return true;
    }

    public bool IsDown()
    {
        if (this.Value is null)
            return false;
        switch (this.Value.MouseButton)
        {   
            case MouseButton.None:
                return Barotrauma.PlayerInput.KeyDown(this.Value.Key);
            case MouseButton.PrimaryMouse:
                return Barotrauma.PlayerInput.PrimaryMouseButtonHeld();
            case MouseButton.SecondaryMouse:
                return Barotrauma.PlayerInput.SecondaryMouseButtonHeld();
            case MouseButton.MiddleMouse:
                return Barotrauma.PlayerInput.MidButtonHeld();
            case MouseButton.MouseButton4:
                return Barotrauma.PlayerInput.Mouse4ButtonHeld();
            case MouseButton.MouseButton5:
                return Barotrauma.PlayerInput.Mouse5ButtonHeld();
            case MouseButton.MouseWheelUp:
                return Barotrauma.PlayerInput.MouseWheelUpClicked();
            case MouseButton.MouseWheelDown:
                return Barotrauma.PlayerInput.MouseWheelDownClicked();
        }
        return false;
    }
    
    public bool IsHit()
    {
        if (this.Value is null)
            return false;
        switch (this.Value.MouseButton)
        {   
            case MouseButton.None:
                return Barotrauma.PlayerInput.KeyHit(this.Value.Key);
            case MouseButton.PrimaryMouse:
                return Barotrauma.PlayerInput.PrimaryMouseButtonClicked();
            case MouseButton.SecondaryMouse:
                return Barotrauma.PlayerInput.SecondaryMouseButtonClicked();
            case MouseButton.MiddleMouse:
                return Barotrauma.PlayerInput.MidButtonClicked();
            case MouseButton.MouseButton4:
                return Barotrauma.PlayerInput.Mouse4ButtonClicked();
            case MouseButton.MouseButton5:
                return Barotrauma.PlayerInput.Mouse5ButtonClicked();
            case MouseButton.MouseWheelUp:
                return Barotrauma.PlayerInput.MouseWheelUpClicked();
            case MouseButton.MouseWheelDown:
                return Barotrauma.PlayerInput.MouseWheelDownClicked();
        }
        return false;
    }

#if CLIENT
    private static GUICustomComponent InputListener;
    
    public override void AddDisplayComponent(GUILayoutGroup layoutGroup, Vector2 relativeSize, Action<string> onSerializedValue)
    {
        var inputButton = new GUIButton(new RectTransform(relativeSize, layoutGroup.RectTransform), Alignment.Center, 
            style: "GUITextBoxNoIcon")
        {
            Text = this.Value.ToString(),
            OnClicked = (btn, obj) =>
            {
                if (InputListener is not null)
                {
                    // Another button is active
                    return true;
                }
                CoroutineManager.Invoke(() =>
                {
                    CreateListener(btn);
                }, 0f); // delay one frame for button inputs
                return true;
            }
        };
        inputButton.OutlineColor = Color.PeachPuff;
        inputButton.TextColor = Color.White;
        

        void ClearListener() 
        {
            InputListener?.Parent.RemoveChild(InputListener);
            InputListener = null;
        }
        
        void CreateListener(GUIButton button)
        {
            ClearListener();
            InputListener = new GUICustomComponent(new RectTransform(Vector2.Zero, layoutGroup.RectTransform), 
                onUpdate: (deltaTime, component) =>
                {
                    var pressedKeys = PlayerInput.GetKeyboardState.GetPressedKeys();
                    if (pressedKeys?.Any() ?? false)
                    {
                        if (pressedKeys.Contains(Keys.Escape))
                        {
                            ClearListener();
                            return;
                        }

                        ApplyValue(pressedKeys.First(), button);
                        return;
                    }
                    
                    if (PlayerInput.PrimaryMouseButtonClicked() &&
                        (GUI.MouseOn == null || !(GUI.MouseOn is GUIButton) || GUI.MouseOn.IsChildOf(layoutGroup)))
                    {
                        ApplyValue(MouseButton.PrimaryMouse, button);
                        return;
                    }
                    else if (PlayerInput.SecondaryMouseButtonClicked())
                    {
                        ApplyValue(MouseButton.SecondaryMouse, button);
                        return;
                    }
                    else if (PlayerInput.MidButtonClicked())
                    {
                        ApplyValue(MouseButton.MiddleMouse, button);
                        return;
                    }
                    else if (PlayerInput.Mouse4ButtonClicked())
                    {
                        ApplyValue(MouseButton.MouseButton4, button);
                        return;
                    }
                    else if (PlayerInput.Mouse5ButtonClicked())
                    {
                        ApplyValue(MouseButton.MouseButton5, button);
                        return;
                    }
                    else if (PlayerInput.MouseWheelUpClicked())
                    {
                        ApplyValue(MouseButton.MouseWheelUp, button);
                        return;
                    }
                    else if (PlayerInput.MouseWheelDownClicked())
                    {
                        ApplyValue(MouseButton.MouseWheelDown, button);
                        return;
                    }
                });
        }

        void ApplyValue(KeyOrMouse input, GUIButton button)
        {
            button.Text = input.ToString();
            onSerializedValue?.Invoke(input.ToString());
            ClearListener();
        }
    }
#endif
}

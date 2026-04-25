using System;
using System.Collections.Concurrent;
using System.Xml.Linq;
using Barotrauma.LuaCs.Data;
using Microsoft.Toolkit.Diagnostics;
using Microsoft.Xna.Framework;
using OneOf;

namespace Barotrauma.LuaCs.Data;

public abstract class SettingBase : ISettingBase
{
    protected SettingBase(IConfigInfo configInfo)
    {
        Guard.IsNotNull(configInfo, nameof(configInfo));
        ConfigInfo = configInfo;
    }
    
    protected IConfigInfo ConfigInfo { get; private set; }

    public string InternalName => ConfigInfo.InternalName;
    public ContentPackage OwnerPackage => ConfigInfo.OwnerPackage;

    public IConfigInfo GetConfigInfo() => ConfigInfo;
    #if CLIENT
    public IConfigDisplayInfo GetDisplayInfo() => ConfigInfo;
    #endif
    
    public virtual bool Equals(ISettingBase other)
    {
        return other is not null && (
            ReferenceEquals(this, other) || !IsDisposed &&
            OwnerPackage == other.OwnerPackage &&
            InternalName.Equals(other.InternalName));
    }

    private int _isDisposed = 0;
    public virtual bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }

    protected abstract void OnDispose();

    public virtual void Dispose()
    {
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }
        
        OnDispose();
        ConfigInfo = null;
        GC.SuppressFinalize(this);
    }
    
    // -- Must be implemented
    
    public abstract Type GetValueType();
    public abstract string GetStringValue();
    public abstract string GetDefaultStringValue();
    public abstract bool TrySetSerializedValue(OneOf<string, XElement> value);

    public abstract event Action<ISettingBase> OnValueChanged;
    public abstract OneOf<string, XElement> GetSerializableValue();
#if CLIENT
    public virtual void AddDisplayComponent(GUILayoutGroup layoutGroup, Vector2 relativeSize, Action<string> onSerializedValue)
    {
        new GUITextBox(new RectTransform(relativeSize, layoutGroup.RectTransform), font: GUIStyle.SmallFont)
        {
            Text = GetStringValue(),
            OnTextChangedDelegate = (box, txt) =>
            {
                onSerializedValue?.Invoke(txt);
                return true;
            }
        };
    }
#endif
}

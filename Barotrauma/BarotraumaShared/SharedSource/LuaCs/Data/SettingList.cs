using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Toolkit.Diagnostics;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs.Data;

public class SettingList<T> : SettingEntry<T>, ISettingList<T> where T : IEquatable<T>, IConvertible
{
    public class LFactory : ISettingBase.IFactory<ISettingList<T>>
    {
        public ISettingList<T> CreateInstance(IConfigInfo configInfo, Func<OneOf<string, XElement, object>, bool> valueChangePredicate)
        {
            Guard.IsNotNull(configInfo, nameof(configInfo));
            return new SettingList<T>(configInfo, valueChangePredicate);
        }
    }
    
    public SettingList(IConfigInfo configInfo, Func<OneOf<string, XElement, object>, bool> valueChangePredicate) : base(configInfo, valueChangePredicate)
    {
        if (!( 
                typeof(T).IsEnum || 
                typeof(T).IsPrimitive || 
                typeof(T) == typeof(string)))
        {
            ThrowHelper.ThrowArgumentException($"{nameof(ISettingBase<T>)}: The type of {nameof(T)} is not an allowed type.");
        }
        ValueChangePredicate = valueChangePredicate;

        var valuesElements = ConfigInfo.Element.GetChildElement("Values")?.GetChildElements("Value")?.ToImmutableArray();

        Guard.IsNotNull(valuesElements, this.InternalName);
        if (valuesElements.Value.IsEmpty)
        {
            ThrowHelper.ThrowArgumentNullException($"{this.InternalName}: Could not find any values in list!");
        }
        
        foreach (var element in valuesElements.Value)
        {
            if (!TryConvert(element, out var v1))
            {
                ThrowHelper.ThrowArgumentException($"{this.InternalName}: Error while parsing list values");
            }
            _valuesList.Add(v1);
        }

        if (TryConvert(ConfigInfo.Element, out var v) && _valuesList.Contains(v))
        {
            Value = v;
            DefaultValue = v;
        }
        else
        {
            Value = _valuesList[0];
            DefaultValue = _valuesList[0];
        }
        

        bool TryConvert(XElement element, out T value)
        {
            try
            {
                value = (T)Convert.ChangeType(element.GetAttributeString("Value", null), typeof(T));
                return true;
            }
            catch (Exception e) when (e is InvalidCastException or ArgumentNullException)
            {
                value = default(T);
                return false;
            }
        }
    }

    private readonly List<T> _valuesList = new();

    public override bool TrySetValue(T value)
    {
        if (!_valuesList.Contains(value))
        {
            return false;
        }
        
        return base.TrySetValue(value);
    }

    public bool TrySetValueByIndex(int index)
    {
        if (_valuesList.Count <= index)
        {
            return false;
        }
        return base.TrySetValue(_valuesList[index]);
    }

    public IReadOnlyList<T> Options => _valuesList.AsReadOnly();

    public IReadOnlyList<string> StringOptions => _valuesList.Select(e => e.ToString()).ToImmutableArray();

#if CLIENT
    public override void AddDisplayComponent(GUILayoutGroup layoutGroup, Vector2 relativeSize, Action<string> onSerializedValue)
    {
        GUIUtil.Dropdown(layoutGroup, (T val) => GetLocalizedString(val.ToString(), val.ToString()), null, Options, Value, (T val) =>
        {
            onSerializedValue?.Invoke(val.ToString());
        }, new Vector2(relativeSize.X, 1f));
        
        string GetLocalizedString(string identifier, string defaultValue)
        {
            var lstr = TextManager.Get($"{XmlConvert.EncodeLocalName(OwnerPackage.Name)}.{InternalName}.{identifier}.DisplayName");
            return lstr.IsNullOrWhiteSpace() ? defaultValue : lstr.Value;
        }
    }
#endif
    
}

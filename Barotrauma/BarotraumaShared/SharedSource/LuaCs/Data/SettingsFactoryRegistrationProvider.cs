using System;
using System.Xml.Linq;
using Barotrauma.LuaCs.Data;
using Barotrauma.LuaCs;
using OneOf;

namespace Barotrauma.LuaCs.Data;

public interface ISettingsRegistrationProvider : IService
{
    void RegisterTypeProviders(IConfigService configService, Func<OneOf<string, XElement, object>, bool> valueChangePredicate);
}

public class SettingsEntryRegistrar : ISettingsRegistrationProvider
{
    private ILuaCsInfoProvider _infoProvider;

    public SettingsEntryRegistrar(ILuaCsInfoProvider infoProvider)
    {
        _infoProvider = infoProvider;
    }

    public void RegisterTypeProviders(IConfigService configService, Func<OneOf<string, XElement, object>, bool> valueChangePredicate)
    {
        RegisterSettingEntry<bool>(configService, "bool", valueChangePredicate);
        RegisterSettingEntry<byte>(configService, "byte", valueChangePredicate);
        RegisterSettingEntry<sbyte>(configService, "sbyte", valueChangePredicate);
        RegisterSettingEntry<short>(configService, "short", valueChangePredicate);
        RegisterSettingEntry<ushort>(configService, "ushort", valueChangePredicate);
        RegisterSettingEntry<int>(configService, "int", valueChangePredicate);
        RegisterSettingEntry<uint>(configService, "uint", valueChangePredicate);
        RegisterSettingEntry<long>(configService, "long", valueChangePredicate);
        RegisterSettingEntry<ulong>(configService, "ulong", valueChangePredicate);
        RegisterSettingEntry<string>(configService, "string", valueChangePredicate);
        RegisterSettingEntry<float>(configService, "float", valueChangePredicate);
        RegisterSettingEntry<float>(configService, "single", valueChangePredicate);
        RegisterSettingEntry<double>(configService, "double", valueChangePredicate);
        
        // ISettingRangeBase<T>
        configService.RegisterSettingTypeInitializer("rangeInt", cfgInfo =>
        {
            return new SettingRangeInt.RangeFactory().CreateInstance(cfgInfo.Info, (val) =>
                IsValueChangeAllowed(cfgInfo.Info, val, valueChangePredicate));
        });
        
        configService.RegisterSettingTypeInitializer("rangeFloat", cfgInfo =>
        {
            return new SettingRangeFloat.RangeFactory().CreateInstance(cfgInfo.Info, (val) =>
                IsValueChangeAllowed(cfgInfo.Info, val, valueChangePredicate));
        });

#if CLIENT
        configService.RegisterSettingTypeInitializer("control" , cfgInfo =>
        {
            return new SettingControl.Factory().CreateInstance(cfgInfo.Info, val => 
                IsValueChangeAllowed(cfgInfo.Info, val, valueChangePredicate));
        });
#endif
        
        RegisterSettingList<bool>(configService, "listBool", valueChangePredicate);
        RegisterSettingList<byte>(configService, "listByte", valueChangePredicate);
        RegisterSettingList<sbyte>(configService, "listSbyte", valueChangePredicate);
        RegisterSettingList<short>(configService, "listShort", valueChangePredicate);
        RegisterSettingList<ushort>(configService, "listUshort", valueChangePredicate);
        RegisterSettingList<int>(configService, "listInt", valueChangePredicate);
        RegisterSettingList<uint>(configService, "listUint", valueChangePredicate);
        RegisterSettingList<long>(configService, "listLong", valueChangePredicate);
        RegisterSettingList<ulong>(configService, "listUlong", valueChangePredicate);
        RegisterSettingList<string>(configService, "listString", valueChangePredicate);
        RegisterSettingList<float>(configService, "listFloat", valueChangePredicate);
        RegisterSettingList<float>(configService, "listSingle", valueChangePredicate);
        RegisterSettingList<double>(configService, "listDouble", valueChangePredicate);
    }

    private void RegisterSettingList<T>(IConfigService configService, string typeName, Func<OneOf<string, XElement, object>, bool> valueChangePredicate) where T : IEquatable<T>, IConvertible
    {
        configService.RegisterSettingTypeInitializer(typeName, cfgInfo =>
        {
            return new SettingList<T>.LFactory().CreateInstance(cfgInfo.Info, (val) =>
                IsValueChangeAllowed(cfgInfo.Info, val, valueChangePredicate));
        });
    }
    
    private void RegisterSettingEntry<T>(IConfigService configService, string typeName, Func<OneOf<string, XElement, object>, bool> valueChangePredicate) where T : IEquatable<T>, IConvertible
    {
        configService.RegisterSettingTypeInitializer(typeName, cfgInfo =>
        {
            return new SettingEntry<T>.Factory().CreateInstance(cfgInfo.Info, (val) =>
                IsValueChangeAllowed(cfgInfo.Info, val, valueChangePredicate));
        });
    }

    private bool IsValueChangeAllowed(IConfigInfo info, OneOf<string, XElement, object> newValue,
        Func<OneOf<string, XElement, object>, bool> valueChangePredicate)
    {
#if CLIENT
        return !info.Element.GetAttributeBool("ReadOnly", false)
               || info.EditableStates < _infoProvider.CurrentRunState 
               || valueChangePredicate is null 
               || valueChangePredicate.Invoke(newValue);
#else
        // Server has absolute authority.
        return !info.Element.GetAttributeBool("ReadOnly", false);
#endif
    }

    public void Dispose()
    {
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }
        _infoProvider.Dispose();
        _infoProvider = null;
    }
    
    private int _isDisposed;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }
}

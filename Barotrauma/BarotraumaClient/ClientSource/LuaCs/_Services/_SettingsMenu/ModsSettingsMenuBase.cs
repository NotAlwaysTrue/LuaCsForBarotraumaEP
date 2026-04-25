using System;
using System.Collections.Concurrent;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.LuaCs.Data;
using Microsoft.Xna.Framework;
using OneOf;

namespace Barotrauma.LuaCs;

internal abstract class ModsSettingsMenuBase : IDisposable
{
    public GUIFrame ContentFrame { get; private set; }
    protected IPackageManagementService PackageManagementService { get; private set; }
    protected IConfigService ConfigService { get; private set; }
    protected SettingsMenu SettingsMenuInstance { get; private set; }
    protected readonly ConcurrentDictionary<ISettingBase, OneOf<string, XElement>> NewValuesCache = new();
    
    protected ModsSettingsMenuBase(GUIFrame contentFrame, 
        IPackageManagementService packageManagementService, 
        IConfigService configService, SettingsMenu settingsMenuInstance)
    {
        ContentFrame = contentFrame;
        PackageManagementService = packageManagementService;
        ConfigService = configService;
        SettingsMenuInstance = settingsMenuInstance;
    }

    protected abstract void DisposeInternal();
    public abstract void ApplyInstalledModChanges();

    public void Dispose()
    {
        DisposeInternal();
        ContentFrame?.Parent.RemoveChild(ContentFrame);
        SettingsMenuInstance = null;
        ContentFrame = null;
        PackageManagementService = null;
        ConfigService = null;
        NewValuesCache.Clear();
    }
}

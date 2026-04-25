using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Barotrauma.LuaCs.Data;
using Barotrauma.LuaCs.Events;
using Barotrauma.LuaCs;
using FluentResults;
using Microsoft.Toolkit.Diagnostics;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs;

public sealed partial class ConfigService : IConfigService
{
    #region Disposal_Locks_Reset

    private readonly AsyncReaderWriterLock _operationLock = new ();
    private readonly AsyncReaderWriterLock _settingsByPackageLock = new ();
    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }
    
    public void Dispose()
    {
        using var lck = _operationLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var settingsLck = _settingsByPackageLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }
        
        _logger.LogDebug($"{nameof(ConfigService)}: Disposing.");
        
        _configInfoParserService.Dispose();
        _configProfileInfoParserService.Dispose();

        if (!_settingsInstances.IsEmpty)
        {
            foreach (var instance in _settingsInstances)
            {
                try
                {
                    if (instance.Value is null)
                    {
                        continue;
                    }

                    _eventService.PublishEvent<IEventSettingInstanceLifetime>(sub =>
                        // ReSharper disable once AccessToDisposedClosure
                        sub.OnSettingInstanceDisposed(instance.Value));
                    instance.Value.Dispose();
                }
                catch 
                {
                    // ignored
                    continue;
                }
            }
        }
        
        _settingsInstances.Clear();
        _instanceFactory.Clear();
        _settingsInstancesByPackage.Clear();
        _commandsService.Dispose();
        
        _storageService = null;
        _logger = null;
        _eventService = null;
        _configInfoParserService = null;
        _configProfileInfoParserService = null;
        _commandsService = null;
        _infoProvider = null;
    }
    
    public FluentResults.Result Reset()
    {
        using var lck = _operationLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        var result = new FluentResults.Result();
        
        if (!_settingsInstances.IsEmpty)
        {
            foreach (var instance in _settingsInstances)
            {
                try
                {
                    if (instance.Value is null)
                    {
                        continue;
                    }

                    _eventService.PublishEvent<IEventSettingInstanceLifetime>(sub =>
                        // ReSharper disable once AccessToDisposedClosure
                        sub.OnSettingInstanceDisposed(instance.Value));
                    instance.Value.Dispose();
                }
                catch (Exception e)
                {
                    result.WithError(new ExceptionalError(e));
                }
            }
        }
        
        _settingsInstances.Clear();
        _instanceFactory.Clear();
        _settingsInstancesByPackage.Clear();
        _storageService.PurgeCache();

        return result;
    }

    #endregion

    private const string SaveDataFileName = "SettingsData.xml"; 
    
    // --- Settings
    private readonly ConcurrentDictionary<(ContentPackage OwnerPackage, string InternalName), ISettingBase> 
        _settingsInstances = new();
    private readonly ConcurrentDictionary<string, Func<(IConfigService ConfigService, IConfigInfo Info), ISettingBase>>
        _instanceFactory = new();
    private readonly ConcurrentDictionary<ContentPackage, ConcurrentBag<ISettingBase>>
        _settingsInstancesByPackage = new();

    // --- Profiles
    private readonly ConcurrentDictionary<(ContentPackage Package, string ProfileName), IConfigProfileInfo>
        _settingsProfiles = new();
    
    private IStorageService _storageService;
    private ILoggerService _logger;
    private IEventService _eventService;
    private IConsoleCommandsService _commandsService;
    private ILuaCsInfoProvider _infoProvider;
    private IParserServiceOneToManyAsync<IConfigResourceInfo, IConfigInfo> _configInfoParserService;
    private IParserServiceOneToManyAsync<IConfigResourceInfo, IConfigProfileInfo> _configProfileInfoParserService;

    public ConfigService(ILoggerService logger, 
        IStorageService storageService, 
        IParserServiceOneToManyAsync<IConfigResourceInfo, IConfigInfo> configInfoParserService, 
        IParserServiceOneToManyAsync<IConfigResourceInfo, IConfigProfileInfo> configProfileInfoParserService, 
        IEventService eventService, 
        IConsoleCommandsService commandsService,
        ILuaCsInfoProvider infoProvider)
    {
        _logger = logger;
        _storageService = storageService;
        _configInfoParserService = configInfoParserService;
        _configProfileInfoParserService = configProfileInfoParserService;
        _eventService = eventService;
        _commandsService = commandsService;
        _infoProvider = infoProvider;

        _storageService.UseCaching = false;
        InjectCommands(commandsService);
    }

    private void InjectCommands(IConsoleCommandsService commandsService)
    {
        commandsService.RegisterCommand("cfg_getvalue", "cfg_getvalue [Content Package] [InternalName] [ValueString]: gets a config value.", (string[] args) =>
            {
                if (args.Length < 1)
                {
                    _logger.LogError("Please specify the name of the package to set the config.");
                    return;
                }

                if (args.Length < 2)
                {
                    _logger.LogError("Please specify the name of the config.");
                    return;
                }

                var package = ContentPackageManager.RegularPackages.FirstOrDefault(p => p.Name == args[0]);
                if (package == null)
                {
                    _logger.LogError($"Could not find the package {args[0]}!");
                    return;
                }

                string internalName = args[1];

                if (!TryGetConfig(package, internalName, out ISettingBase setting))
                {
                    _logger.LogError($"Could not get config with name {internalName}");
                    return;
                }

                _logger.LogMessage($"config {internalName} value is {setting.GetStringValue()}", Color.Green);
            }, getValidArgs: () => new[]
            {
                 ContentPackageManager.RegularPackages.Select(p => p.Name).ToArray()
            });

        commandsService.RegisterCommand("cfg_setvalue", "cfg_setvalue [Content Package] [InternalName] [ValueString]: sets a config.", (string[] args) =>
            {
                if (args.Length < 1)
                {
                    _logger.LogError("Please specify the name of the package to set the config.");
                    return;
                }

                if (args.Length < 2)
                {
                    _logger.LogError("Please specify the name of the config.");
                    return;
                }

                if (args.Length < 3)
                {
                    _logger.LogError("Please specify the value to set the config to.");
                    return;
                }

                var package = ContentPackageManager.RegularPackages.FirstOrDefault(p => p.Name == args[0]);
                if (package == null)
                {
                    _logger.LogError($"Could not find the package {args[0]}!");
                    return;
                }

                string internalName = args[1];
                string valueString = args[2];

                if (!TryGetConfig(package, internalName, out ISettingBase setting))
                {
                    _logger.LogError($"Could not get config with name {internalName}");
                    return;
                }

                if (setting.TrySetSerializedValue(valueString))
                {
                    _logger.LogMessage($"Set config {internalName} value to {valueString}", Color.Green);
                    if (SaveConfigValue(setting) is { IsFailed: true } res)
                    {
                        _logger.LogMessage($"Failed to save new config data to disk. Reasons: {res.ToString()}");
                    }
                }
                else
                {
                    _logger.LogError($"Failed to set config value");
                }
            }, getValidArgs: () => new[]
            {
                 ContentPackageManager.RegularPackages.Select(p => p.Name).ToArray()
            });

        commandsService.RegisterCommand("cfg_setprofile", "cfg_setprofile [ContentPackage] [InternalProfileName]",
            (string[] args) =>
            {
                if (args.Length < 1 || args[0].IsNullOrWhiteSpace())
                {
                    _logger.LogError("Please specify the name of the package of the profile.");
                    return;
                }

                if (args.Length < 2 || args[1].IsNullOrWhiteSpace())
                {
                    _logger.LogError("Please specify the name of the profile.");
                    return;
                }
                
                var package = ContentPackageManager.RegularPackages.FirstOrDefault(p => p.Name == args[0], null);
                if (package == null)
                {
                    _logger.LogError($"Could not find the package {args[0]}!");
                    return;
                }

                var res = ApplyConfigProfile(package, args[1]);
                if (res.IsFailed)
                {
                    _logger.LogError($"Errors while applying profile {args[1]}!");
                    _logger.LogResults(res);
                    return;
                }
                _logger.Log($"Profile {args[1]} applied successfully!", Color.Green);
            }, getValidArgs: () => new[]
            {
                ContentPackageManager.RegularPackages.Select(p => p.Name).ToArray()
            }, false);
    }
    
    public void RegisterSettingTypeInitializer<T>(string typeIdentifier, Func<(IConfigService ConfigService, IConfigInfo Info), T> settingFactory) where T : class, ISettingBase
    {
        Guard.IsNotNullOrWhiteSpace(typeIdentifier, nameof(typeIdentifier));
        Guard.IsNotNull(settingFactory, nameof(settingFactory));
        using var lck = _operationLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        if (_instanceFactory.ContainsKey(typeIdentifier))
        {
            ThrowHelper.ThrowArgumentException($"{nameof(RegisterSettingTypeInitializer)}: The type identifier {typeIdentifier} is already registered.");
        }
        
        _instanceFactory[typeIdentifier] = settingFactory;
    }
    
    private static ImmutableArray<T> SelectCompatible<T>(ImmutableArray<T> resources) where T : IBaseResourceInfo
    {
        return resources
            .Where(r => r.SupportedPlatforms.HasFlag(ModUtils.Environment.CurrentPlatform))
            .Where(r => r.SupportedTargets.HasFlag(ModUtils.Environment.CurrentTarget))
            .OrderBy(r => r.Optional ? 1 : 0)   // optional content last
            .ThenBy(r => r.LoadPriority)
            .ToImmutableArray();
    }

    public async Task<FluentResults.Result> LoadConfigsAsync(ImmutableArray<IConfigResourceInfo> configResources)
    {
        using var lck = await _operationLock.AcquireReaderLock();
        IService.CheckDisposed(this);
        if (configResources.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Ok();
        }
        
        var result = new FluentResults.Result();

        var taskBuilder = ImmutableArray.CreateBuilder<Task<ImmutableArray<IConfigInfo>>>();
        var toProcessErrors = new ConcurrentStack<IError>();
        
        foreach (var resource in SelectCompatible(configResources))
        {
            taskBuilder.Add(await Task.Factory.StartNew<Task<ImmutableArray<IConfigInfo>>>(async Task<ImmutableArray<IConfigInfo>> () =>
            {
                var r = await _configInfoParserService.TryParseResourcesAsync(resource);
                if (r.IsFailed)
                {
                    toProcessErrors.PushRange(r.Errors.ToArray());
                    return ImmutableArray<IConfigInfo>.Empty;
                }
                return r.Value;
            }));
        }

        var taskResults = await Task.WhenAll(taskBuilder.ToImmutable());

        if (toProcessErrors.Count > 0)
        {
            return FluentResults.Result.Fail($"{nameof(LoadConfigsAsync)}: Errors while loading configuration info: ").WithErrors(toProcessErrors.ToArray());
        }
        
        var toProcessDocs = taskResults
            .Where(tr => !tr.IsDefaultOrEmpty)
            .SelectMany(tr => tr)
            .Where(icf => icf is not null)
            .ToImmutableArray();

        var instanceQueue = new Queue<(IConfigInfo configInfo, Func<(IConfigService ConfigService, IConfigInfo Info), ISettingBase> factory)>();
        
        foreach (var info in toProcessDocs)
        {
            if (!_instanceFactory.TryGetValue(info.DataType, out var factory))
            {
                result.WithError(
                    $"{nameof(LoadConfigsAsync)}: Could not retrieve the instance factory for the data type of '{info.DataType}'!");
                continue;
            }
            if (_settingsInstances.ContainsKey((info.OwnerPackage, info.InternalName)))
            {
                // duplicate for some reason (ie. double loading). This should never happen.
                ThrowHelper.ThrowInvalidOperationException($"{nameof(LoadConfigsAsync)}: A setting for the [ContentPackage].[InternalName] of '[{info.OwnerPackage.Name}].[{info.InternalName}]' already exists!");
            }
            
            instanceQueue.Enqueue((info, factory));
        }

        var toProcessInstanceQueue = new Queue<(IConfigInfo info, ISettingBase instance)>();

        while (instanceQueue.TryDequeue(out var instanceFactoryInfo))
        {
            try
            {
                toProcessInstanceQueue.Enqueue((instanceFactoryInfo.configInfo, instanceFactoryInfo.factory((this, instanceFactoryInfo.configInfo))));
            }
            catch (Exception e)
            {
                result.WithError(
                    $"{nameof(LoadConfigsAsync)}: Error while instancing setting for '{instanceFactoryInfo.configInfo.OwnerPackage}.{instanceFactoryInfo.configInfo.InternalName}': {e.Message}!");
                continue;
            }
        }

        using var settingsLck = await _settingsByPackageLock.AcquireWriterLock(); // block to protect new bag instance creation
        
        while (toProcessInstanceQueue.TryDequeue(out var newInstanceData))
        {
            _settingsInstances[(newInstanceData.info.OwnerPackage, newInstanceData.info.InternalName)] =  newInstanceData.instance;
            if (!_settingsInstancesByPackage.TryGetValue(newInstanceData.info.OwnerPackage, out _))
            {
                _settingsInstancesByPackage[newInstanceData.info.OwnerPackage] = new ConcurrentBag<ISettingBase>();
            }
            _settingsInstancesByPackage[newInstanceData.info.OwnerPackage].Add(newInstanceData.instance);
            result.WithReasons(_eventService.PublishEvent<IEventSettingInstanceLifetime>(sub =>
                sub.OnSettingInstanceCreated(newInstanceData.instance)).Reasons);
        }

        return result;
    }

    public async Task<FluentResults.Result> LoadConfigsProfilesAsync(ImmutableArray<IConfigResourceInfo> configProfileResources)
    {
        using var _ = await _operationLock.AcquireReaderLock();
        IService.CheckDisposed(this);
        if (configProfileResources.IsDefaultOrEmpty)
        {
            ThrowHelper.ThrowArgumentNullException($"{nameof(LoadConfigsProfilesAsync)}: {nameof(configProfileResources)} is empty.");
        }

        var result = new FluentResults.Result();
        
        foreach (var resource in SelectCompatible(configProfileResources))
        {
            var r = await _configProfileInfoParserService.TryParseResourcesAsync(resource);
            if (r.IsFailed)
            {
                result.WithErrors(r.Errors);
                continue;
            }

            foreach (var info in r.Value)
            {
                if (!_settingsProfiles.TryAdd((info.OwnerPackage, info.InternalName), info))
                {
                    result.WithErrors(r.Errors);
                    continue;
                }

                if (info.InternalName.Equals("default", StringComparison.InvariantCultureIgnoreCase))
                {
                    //apply it
                    foreach (var value in info.ProfileValues)
                    {
                        if (_settingsInstances.TryGetValue((info.OwnerPackage, value.SettingName), out var instance))
                        {
                            instance.TrySetSerializedValue(value.Element);
                        }
                    }
                }
            }
        }
        
        return result;
    }

    public FluentResults.Result LoadSavedValueForConfig(ISettingBase setting)
    {
        Guard.IsNotNull(setting, nameof(setting));
        using var lck = _operationLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        if (_storageService.LoadLocalXml(setting.OwnerPackage, SaveDataFileName) is not { } saveFileResult)
        {
#if DEBUG
            return FluentResults.Result.Fail(
                $"{nameof(LoadSavedValueForConfig)}: Could not open save file for setting [{setting.OwnerPackage.Name}.{setting.InternalName}]");
#endif
            return FluentResults.Result.Ok();
        }
        
        if (saveFileResult is { IsFailed: true })
        {
#if DEBUG
            _logger.LogResults(saveFileResult.ToResult());
            return FluentResults.Result.Fail(
                $"{nameof(LoadSavedValueForConfig)}: Could not open save file for setting [{setting.OwnerPackage.Name}.{setting.InternalName}]");
#endif
            return FluentResults.Result.Ok();
        }

        if (saveFileResult.Value.Root is not {} rootElement
            || !string.Equals(rootElement.Name.LocalName, "Configuration", StringComparison.InvariantCultureIgnoreCase))
        {
            return FluentResults.Result.Fail($"{nameof(LoadSavedValueForConfig)}: Root invalid for setting [{setting.OwnerPackage.Name}.{setting.InternalName}]");
        }

        if (rootElement.GetChildElement(XmlConvert.EncodeLocalName(setting.OwnerPackage.Name.Trim()), StringComparison.InvariantCultureIgnoreCase)
            ?.GetChildElement(setting.InternalName, StringComparison.InvariantCultureIgnoreCase) is not {} cfgValueElement)
        {
#if DEBUG
            return FluentResults.Result.Fail($"{nameof(LoadSavedValueForConfig)}: Could not find saved value for setting:[{setting.OwnerPackage.Name}.{setting.InternalName}]");
#endif
            return FluentResults.Result.Ok();
        }

        return FluentResults.Result.OkIf(setting.TrySetSerializedValue(cfgValueElement), new Error($"Failed to set value for [{setting.OwnerPackage.Name}.{setting.InternalName}]"));
    }
    
    public FluentResults.Result LoadSavedConfigsValues()
    {
        ImmutableArray<ISettingBase> cfgValues;
        using (var lck = _operationLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult())
        {
            IService.CheckDisposed(this);
            cfgValues = _settingsInstances.Select(kvp => kvp.Value).ToImmutableArray();
        }

        var ret = new FluentResults.Result();
        
        foreach (var settingBase in cfgValues)
        {
#if DEBUG
            // log in debug only.
            ret.WithReasons(LoadSavedValueForConfig(settingBase).Reasons);
#else
            LoadSavedValueForConfig(settingBase);
#endif
        }

        return ret;
    }

    public FluentResults.Result ApplyConfigProfile(ContentPackage package, string internalName)
    {
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        using var _ = _operationLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        if (!_settingsProfiles.TryGetValue((package, internalName), out var setting))
        {
            return FluentResults.Result.Fail($"{nameof(ApplyConfigProfile)}: Could not find profile [{package.Name}.{internalName}]");
        }

        var result = new FluentResults.Result();
        
        foreach (var profileValue in setting.ProfileValues)
        {
            if (!_settingsInstances.TryGetValue((package, profileValue.SettingName), out var instance))
            {
                result.WithError(new Error($"{nameof(ApplyConfigProfile)}: Could not find setting [{profileValue.SettingName}]."));
                continue;
            }

            if (!instance.TrySetSerializedValue(profileValue.Element))
            {
                result.WithError(new Error($"{nameof(ApplyConfigProfile)}: Failed to set value for [{profileValue.SettingName}]."));
            }
        }

        return result;
    }

    public FluentResults.Result SaveConfigValue(ISettingBase setting)
    {
        XDocument cpCfgValues;
        if (_storageService.LoadLocalXml(setting.OwnerPackage, SaveDataFileName) is not {} saveFileResult)
        {
            return FluentResults.Result.Fail($"{nameof(SaveConfigValue)}: Storage Service Failure while trying to load file for  setting [{setting.OwnerPackage.Name}.{setting.InternalName}]");
        }

        // get Configuration
        if (saveFileResult.IsFailed)
        {
            cpCfgValues = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), new XElement("Configuration"));
        }
        else
        {
            cpCfgValues = saveFileResult.Value;
        }

        if (cpCfgValues.Root is null || cpCfgValues.Root.Name != "Configuration")
        {
            return FluentResults.Result.Fail($"{nameof(SaveConfigValue)}: Bad save file format for setting: [{setting.OwnerPackage.Name}.{setting.InternalName}]");
        }

        XElement currentTarget = GetOrAddElement(cpCfgValues.Root, XmlConvert.EncodeLocalName(setting.OwnerPackage.Name.Trim()), name => new XElement(name));
        currentTarget = GetOrAddElement(currentTarget, setting.InternalName, name => new XElement(name));

        var ret = setting.GetSerializableValue().Match(str =>
            {
                var tgt = currentTarget.Attribute("Value");
                if (tgt is null)
                {
                    var attr = new XAttribute("Value", str);
                    currentTarget.Add(attr);
                }
                else
                {
                    tgt.Value = str;
                }

                return FluentResults.Result.Ok();
            },
            elem =>
            {  
                currentTarget.ReplaceNodes(new XElement("Value", elem));
                return FluentResults.Result.Ok();
            });

        ret.WithReasons(_storageService.SaveLocalXml(setting.OwnerPackage, SaveDataFileName, cpCfgValues).Reasons);
        return ret;
        
        XElement GetOrAddElement(XElement containerElement, string elementName, Func<string, XElement> factory)
        {
            var element = containerElement.Element(elementName);
            if (element is null)
            {
                element = factory(elementName);
                containerElement.Add(element);    
            }
            return element;
        }
    }
    

    public FluentResults.Result DisposePackageData(ContentPackage package)
    {
        Guard.IsNotNull(package, nameof(package));
        using var lck = _operationLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        ConcurrentBag<ISettingBase> toDispose;
        using (var settingsLck = _settingsByPackageLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult())
        {
            if (!_settingsInstancesByPackage.TryRemove(package, out toDispose) || toDispose is null)
            {
                return FluentResults.Result.Ok();
            }
        }

        var result = new FluentResults.Result();

        foreach (var setting in toDispose)
        {
            result.WithReasons(_eventService.PublishEvent<IEventSettingInstanceLifetime>(sub => sub.OnSettingInstanceDisposed(setting)).Reasons);
            try
            {
                _settingsInstances.TryRemove((setting.OwnerPackage, setting.InternalName), out _);
                setting.Dispose();
            }
            catch (Exception e)
            {
                result.WithError(new ExceptionalError(e));
            }
        }
        
        return result;
    }

    public FluentResults.Result DisposeAllPackageData()
    {
        return this.Reset();
    }

    public bool TryGetConfig<T>(ContentPackage package, string internalName, out T instance) where T : ISettingBase
    {
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        using var lck = _operationLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var settingsLck =
            _settingsByPackageLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        instance = default;
        
        if(!_settingsInstances.TryGetValue((package, internalName), out var inst))
        {
            return false;
        }

        if (inst is not T instanceT)
        {
            return false;
        }
        
        instance = instanceT;
        return true;
    }
}

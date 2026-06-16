using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml;
using Barotrauma.Extensions;
using Barotrauma.LuaCs.Data;
using FluentResults;
using Microsoft.Toolkit.Diagnostics;

namespace Barotrauma.LuaCs;

public sealed class PackageManagementService : IPackageManagementService
{
    // svc
    private ILoggerService _logger;
    private IModConfigService _modConfigService;
    private IConfigService _configService;
    private ILuaScriptManagementService _luaScriptManagementService;
    private IPluginManagementService _pluginManagementService;
    private IConsoleCommandsService _commandsService;
#if CLIENT
    private IUIStylesService _uiStylesService;
#endif
    private IPackageManagementServiceConfig _runConfig;
    // state
    private readonly ConcurrentDictionary<ContentPackage, IModConfigInfo> _loadedPackages = new();
    private readonly ConcurrentDictionary<ContentPackage, IModConfigInfo> _runningPackages = new();
    private readonly ConcurrentDictionary<string, ContentPackage> _packageNameCache = new();
    // control
    /// <summary>
    /// Service Disposal Lock.
    /// </summary>
    private readonly AsyncReaderWriterLock _operationsLock = new();
    /// <summary>
    /// Execution of packages lock.
    /// <br/> Read: Package loading/unloading (Multi-operation mode).
    /// <br/> Write: Package execution (exclusive mode).
    /// </summary>
    private readonly AsyncReaderWriterLock _executionLock = new();
    
    public PackageManagementService(ILoggerService logger, 
        IModConfigService modConfigService, 
        ILuaScriptManagementService luaScriptManagementService, 
        IPluginManagementService pluginManagementService, 
        IConfigService configService,
        IConsoleCommandsService commandsService,
#if CLIENT
        IUIStylesService uiStylesService,
#endif
        IPackageManagementServiceConfig runConfig)
    {
        _logger = logger;
        _modConfigService = modConfigService;
        _luaScriptManagementService = luaScriptManagementService;
        _pluginManagementService = pluginManagementService;
        _configService = configService;
        _runConfig = runConfig;
#if CLIENT
        _uiStylesService = uiStylesService;
#endif
        _commandsService = commandsService;
        commandsService.RegisterCommand("pms_getxmlname",
            "Gets the XML encoded name for the given package, as used in localization.",
            onExecute: args =>
            {
                if (args.Length < 1)
                {
                    _logger.LogError("Please specify the name of the package.");
                    return;
                }

                if (ContentPackageManager.AllPackages.FirstOrDefault(p => p.Name == args[0]) is { } pkg)
                {
                    _logger.Log($"Package Xml Name: '{XmlConvert.EncodeLocalName(pkg.Name)}'");
                    return;
                }
                _logger.Log($"Could not find package with the name '{args[0]}'");
            },
            getValidArgs: () =>
            {
                return new[]
                {
                    this._loadedPackages.Keys.Select(p => p.Name).ToArray()
                };
            });
    }
    
    public void Dispose()
    {
        using var lck = _operationsLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
            return;
        
        _logger.LogMessage($"{nameof(PackageManagementService)} is disposing.");
        _luaScriptManagementService.Dispose();
        _pluginManagementService.Dispose();
        _modConfigService.Dispose();
        _logger.Dispose();
#if CLIENT
        _uiStylesService.Dispose();
#endif

        _logger = null;
        _luaScriptManagementService = null;
        _pluginManagementService = null;
        _modConfigService = null;
#if CLIENT
        _uiStylesService = null;
#endif
        
        
        _loadedPackages.Clear();
        _runningPackages.Clear();
    }

    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref  _isDisposed);
        set => ModUtils.Threading.SetBool(ref  _isDisposed, value);
    }
    
    public FluentResults.Result Reset()
    {
        using var lck  = _operationsLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        if (IsDisposed)
            return FluentResults.Result.Fail($"{nameof(PackageManagementService)}failed to reset. Has already been disposed.");

        try
        {
            var operationResult = new FluentResults.Result();
            
            operationResult.WithReasons(_luaScriptManagementService.Reset().Reasons);
            operationResult.WithReasons(_pluginManagementService.Reset().Reasons);
            operationResult.WithReasons(_configService.Reset().Reasons);
#if CLIENT
            operationResult.WithReasons(_uiStylesService.Reset().Reasons);
#endif
            _runningPackages.Clear();
            _loadedPackages.Clear();
            _packageNameCache.Clear();
            return operationResult;
        }
        catch (Exception e)
        {
            return FluentResults.Result.Fail(new ExceptionalError(e));
        }
    }

    public bool TryGetLoadedPackageByName(string name, out ContentPackage package)
    {
        package = null;
        if (name.IsNullOrWhiteSpace())
        {
            return false;
        }
        
        using var _ = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        return _packageNameCache.TryGetValue(name, out package);
    }

    public FluentResults.Result LoadPackageInfo(ContentPackage package)
    {
        Guard.IsNotNull(package, nameof(package));
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        
        IService.CheckDisposed(this);
        if (_loadedPackages.TryGetValue(package, out var result))
        {
            _logger.LogWarning($"{nameof(LoadPackageInfo)}: Tried to load already-loaded package {package.Name}.");
            return FluentResults.Result.Ok();
        }

        var pkgCfgInfo = _modConfigService.CreateConfigAsync(package).ConfigureAwait(false).GetAwaiter().GetResult();
        if (pkgCfgInfo.IsFailed)
        {
            _logger.LogResults(pkgCfgInfo.ToResult());
            return pkgCfgInfo.ToResult();
        }
        return UnsafeAddPackageInternal(package, pkgCfgInfo.Value);
    }

    public FluentResults.Result LoadPackagesInfo(ImmutableArray<ContentPackage> packages)
    {
        if (packages.IsDefaultOrEmpty)
            ThrowHelper.ThrowArgumentException($"{nameof(LoadPackagesInfo)}: packages list is empty.");
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        
        IService.CheckDisposed(this);
        var result = new FluentResults.Result();
        var packages2 = packages.OrderBy(pkg => pkg.Name == LuaCsSetup.PackageName ? 0 : 1) // always run lua cs first.
            .ThenBy(packages.IndexOf)
            .ToImmutableArray();
            
        var pkgConfigs = _modConfigService.CreateConfigsAsync([..packages2]).ConfigureAwait(false).GetAwaiter().GetResult();
        foreach (var pkgConfig in pkgConfigs)
        {
            result.WithReasons(pkgConfig.Config.Reasons);
            if (pkgConfig.Config.IsSuccess)
            {
                result.WithReasons(UnsafeAddPackageInternal(pkgConfig.Source, pkgConfig.Config.Value).Reasons);
            }
        }

        return result;
    }

    private FluentResults.Result UnsafeAddPackageInternal(ContentPackage package, IModConfigInfo config)
    {
        if (_loadedPackages.TryGetValue(package, out _))
        {
            _logger.LogWarning($"Tried to load already-loaded package {package.Name}.");
            return FluentResults.Result.Ok();
        }

        // We need to touch ContentPath.Fullpath once in a single-threaded context to make it thread-safe.
        foreach (var info in config.Assemblies)
        {
            TouchMeFullPaths(info);
        }
        
        foreach (var info in config.Configs)
        {
            TouchMeFullPaths(info);
        }
        
        foreach (var info in config.LuaScripts)
        {
            TouchMeFullPaths(info);
        }

        // We need to touch ContentPath.Fullpath once in a single-threaded context to make it thread-safe.
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.PreserveSig)]
        void TouchMeFullPaths(IBaseResourceInfo info)
        {
            foreach (var contentPath in info.FilePaths)
            {
                var s = contentPath.FullPath;
            }
        }
        
        _loadedPackages[package] = config;
        _packageNameCache[package.Name] = package;
        try
        {
            var res = new FluentResults.Result();
            var tasks = ImmutableArray.CreateBuilder<Task<Task<FluentResults.Result>>>();

            if (!config.Configs.IsDefaultOrEmpty)
            {
                tasks.Add(Task.Factory.StartNew(async Task<FluentResults.Result> () =>
                    new FluentResults.Result()
                        .WithReasons((await _configService.LoadConfigsAsync(config.Configs)).Reasons)
                        .WithReasons((await _configService.LoadConfigsProfilesAsync(config.Configs)).Reasons)));
            }

            if (!config.LuaScripts.IsDefaultOrEmpty)
            {
                tasks.Add(Task.Factory.StartNew(async () =>
                    await _luaScriptManagementService.LoadScriptResourcesAsync(config.LuaScripts)));
            }

            if (tasks.Count == 0)
            {
                return FluentResults.Result.Ok();
            }

#if CLIENT
            if (!config.Styles.IsDefaultOrEmpty)
            {
                res.WithReasons(_uiStylesService.LoadAssets(config.Styles).Reasons);          
            }
#endif
            var r = Task.WhenAll(tasks.ToArray()).ConfigureAwait(false).GetAwaiter().GetResult();

            foreach (var task in r)
            {
                res.WithReasons(task.ConfigureAwait(false).GetAwaiter().GetResult().Reasons);
            }
            return res;
        }
        catch (Exception e)
        {
            return FluentResults.Result.Fail(new ExceptionalError(e));
        }
    }

    public FluentResults.Result ExecuteLoadedPackages(ImmutableArray<ContentPackage> executionOrder, bool executeCsAssemblies)
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        if (executionOrder.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Fail($"{nameof(ExecuteLoadedPackages)}: No packages in the execution order list.");
        }
        
        if (!_runningPackages.IsEmpty)
        {
            return FluentResults.Result.Fail(
                $"{nameof(ExecuteLoadedPackages)}: There are already packages running! List: {
                    _runningPackages.Aggregate(string.Empty, (acc, kvp) => "-" + kvp + "\n" + kvp.Key.Name)}");
        }

        if (_loadedPackages.IsEmpty)
        {
            return FluentResults.Result.Fail($"{nameof(ExecuteLoadedPackages)}: No packages loaded. Nothing to run!)");
        }

        var result = new FluentResults.Result();
        
        // get loading order. Note: packages not in the execution order list will load first.
        var loadingOrderedPackages = _loadedPackages
            .OrderBy(pkg => pkg.Key.Name == LuaCsSetup.PackageName ? 0 : 1) // always run lua cs first.
            .ThenBy(pkg => executionOrder.IndexOf(pkg.Key))
            .ToImmutableArray();
        var loadOrderByPackage = loadingOrderedPackages.Select(p => p.Key).ToImmutableArray();
        var toLoadPackagesIndents = loadingOrderedPackages
            .SelectMany(p => p.Key.AltNames.Union(new []{ p.Key.Name }).ToIdentifiers())
            .ToImmutableHashSet();


        // NOTE: Config/Settings are instanced in LoadPackages()

        if (executeCsAssemblies)
        {
            var plugins = SelectCompatible(loadingOrderedPackages
                .SelectMany(pkg => pkg.Value.Assemblies)
                .ToImmutableArray(), toLoadPackagesIndents, loadOrderByPackage);

            if (!plugins.IsDefaultOrEmpty)
            {
                result.WithReasons(_pluginManagementService.LoadAssemblyResources(plugins).Reasons);
                result.WithReasons(_pluginManagementService.ActivatePluginInstances(
                    plugins.Select(p => p.OwnerPackage).ToImmutableArray(), false).Reasons);
            }
        }

        //lua scripts
        var luaScripts = SelectCompatible(loadingOrderedPackages
            .Where(pkg => executeCsAssemblies 
                          || !pkg.Value.LuaScripts.Any(scr => scr.RunUnrestricted))
            .SelectMany(pkg => pkg.Value.LuaScripts)
            .ToImmutableArray(), toLoadPackagesIndents, loadOrderByPackage);
            
        if (!luaScripts.IsDefaultOrEmpty)
        {
            result.WithReasons(_luaScriptManagementService.ExecuteLoadedScripts(luaScripts, enableSandbox: !executeCsAssemblies).Reasons);
        }

        foreach (var package in loadingOrderedPackages)
        {
            _runningPackages[package.Key] = package.Value;
        }
        
        return result;
    }
    
    private static ImmutableArray<T> SelectCompatible<T>(ImmutableArray<T> resources, 
        ImmutableHashSet<Identifier> enabledPackagesIdents, 
        ImmutableArray<ContentPackage> loadingOrder)
        where T : IBaseResourceInfo
    {
        return resources
            .Where(r => r.SupportedPlatforms.HasFlag(ModUtils.Environment.CurrentPlatform))
            .Where(r => r.SupportedTargets.HasFlag(ModUtils.Environment.CurrentTarget))
            .Where(r => !r.Optional || (
            (r.RequiredPackages.IsDefaultOrEmpty || enabledPackagesIdents.Intersect(r.RequiredPackages).Any()) 
            && (r.IncompatiblePackages.IsDefaultOrEmpty || enabledPackagesIdents.Intersect(r.IncompatiblePackages).None())))
            .OrderBy(r => r.Optional ? 1 : 0)   // optional content last
            .ThenBy(r => loadingOrder.IndexOf(r.OwnerPackage))
            .ThenBy(r => r.LoadPriority)
            .ToImmutableArray();
    }
    
    
    public FluentResults.Result SyncLoadedPackagesList(ImmutableArray<ContentPackage> packages)
    {
        if (packages.IsDefaultOrEmpty)
            ThrowHelper.ThrowArgumentNullException(nameof(packages));
        if (!_runningPackages.IsEmpty)
            ThrowHelper.ThrowInvalidOperationException($"{nameof(SyncLoadedPackagesList)}: There are packages running!");
        
        var toRemove = _loadedPackages.Keys.Except(packages).ToImmutableArray();
        var toAdd = packages.Except(_loadedPackages.Keys)
            .OrderBy(pack => packages.IndexOf(pack)).ToImmutableArray();

        var result = new FluentResults.Result();

        if (!toRemove.IsDefaultOrEmpty)
        {
            result.WithReasons(UnloadPackages(toRemove).Reasons);
        }
        
        if (!toAdd.IsDefaultOrEmpty)
        {
            result.WithReasons(LoadPackagesInfo(toAdd).Reasons);
        }
        
        return result;
    }

    public FluentResults.Result StopRunningPackages()
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        if (_loadedPackages.IsEmpty || _runningPackages.IsEmpty)
        {
#if DEGUG
            _logger.LogWarning($"{nameof(StopRunningPackages)}: No packages are currently executing.");
#endif
            return FluentResults.Result.Ok();
        }
        
        var res = new FluentResults.Result();
        res.WithReasons(_luaScriptManagementService.UnloadActiveScripts().Reasons);
        res.WithReasons(_pluginManagementService.UnloadManagedAssemblies().Reasons);
        _runningPackages.Clear();
        return res;
    }
    
    public FluentResults.Result UnloadPackage(ContentPackage package)
    {
        Guard.IsNotNull(package, nameof(package));
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        if (!_loadedPackages.ContainsKey(package))
        {
            return FluentResults.Result.Fail($"{nameof(UnloadPackage)}: The package is not loaded.");
        }
        if (!_runningPackages.IsEmpty)
        {
            return FluentResults.Result.Fail($"{nameof(UnloadPackage)}: Packages are currently executing.");
        }
        var result = new  FluentResults.Result();
        result.WithReasons(_luaScriptManagementService.DisposePackageResources(package).Reasons);
        result.WithReasons(_configService.DisposePackageData(package).Reasons);
#if CLIENT
        result.WithReasons(_uiStylesService.UnloadPackage(package).Reasons);  
#endif
        _loadedPackages.TryRemove(package, out _);
        _packageNameCache.TryRemove(package.Name, out _);
        return result;
    }
    
    public FluentResults.Result UnloadPackages(ImmutableArray<ContentPackage> packages)
    {
        if (packages.IsDefaultOrEmpty)
            return FluentResults.Result.Fail($"{nameof(UnloadPackages)}: Package list is empty.");
        
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        var result =  new FluentResults.Result();
        foreach (var package in packages)
        {
            result.WithReasons(UnloadPackage(package).Reasons);
        }
        return result;
    }

    public FluentResults.Result UnloadAllPackages()
    {
        using var lck = _operationsLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        using var executeLock = _executionLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        if (_loadedPackages.IsEmpty)
            return FluentResults.Result.Ok();
        if (!_runningPackages.IsEmpty)
            return FluentResults.Result.Fail($"{nameof(UnloadAllPackages)}: Packages are currently executing.");
        var result = new FluentResults.Result();
        result.WithReasons(_luaScriptManagementService.DisposeAllPackageResources().Reasons);
        result.WithReasons(_configService.DisposeAllPackageData().Reasons);
        _loadedPackages.Clear();
        return result;
    }

    public ImmutableArray<ContentPackage> GetAllLoadedPackages()
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        return [.._loadedPackages.Keys];
    }

    public bool IsPackageRunning(ContentPackage package)
    {
        Guard.IsNotNull(package, nameof(package));
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        return _runningPackages.ContainsKey(package);
    }

    public bool IsAnyPackageLoaded()
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        return !_loadedPackages.IsEmpty;
    }

    public bool IsAnyPackageRunning()
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        return !_runningPackages.IsEmpty;
    }

    public ImmutableArray<ContentPackage> GetLoadedUnrestrictedPackages()
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (_loadedPackages.IsEmpty)
            return ImmutableArray<ContentPackage>.Empty;
        return [.._loadedPackages.Values
                .Where(cfg => !cfg.Assemblies.IsDefaultOrEmpty || cfg.LuaScripts.Any(scr => scr.RunUnrestricted))
                .Select(cfg => cfg.Package)];
    }

    public bool PackageContainsAnyRunnableResource(ContentPackage package)
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        var result = GetModConfigForPackage(package);

        if (result.IsSuccess)
        {
            return result.Value.Assemblies.Any() || result.Value.LuaScripts.Any();
        }
        else
        {
            return false;
        }
    }

    public Result<IModConfigInfo> GetModConfigForPackage(ContentPackage package)
    {
        using var lck = _operationsLock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        if (!_loadedPackages.TryGetValue(package, out var modConfig))
        {
            return FluentResults.Result.Fail($"Failed to find mod config for package {package.Name}");
        }

        return new FluentResults.Result<IModConfigInfo>().WithValue(modConfig);
    }
}

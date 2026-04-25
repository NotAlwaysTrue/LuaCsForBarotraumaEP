#nullable enable

using Barotrauma.LuaCs.Compatibility;
using Barotrauma.LuaCs.Data;
using Barotrauma.LuaCs.Events;
using Barotrauma.Networking;
using FluentResults;
using Microsoft.CodeAnalysis;
using Microsoft.Toolkit.Diagnostics;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Barotrauma.LuaCs;

class LuaScriptManagementService : ILuaScriptManagementService, ILuaDataService, IEventAssemblyUnloading
{
    public Script? InternalScript => _script;

    private Script? _script;
    private bool _isRunning;
    [MemberNotNullWhen(true, nameof(_script))]
    public bool IsRunning => _isRunning;
    private List<ILuaScriptResourceInfo> _resourcesInfo = new List<ILuaScriptResourceInfo>();

    private readonly AsyncReaderWriterLock _operationsLock = new ();

    private readonly ILuaUserDataService _userDataService;
    private readonly ISafeLuaUserDataService _safeUserDataService;

    private readonly ILuaScriptLoader _luaScriptLoader;
    private readonly ILuaScriptServicesConfig _luaScriptServicesConfig;
    private readonly ILoggerService _loggerService;
    private readonly LuaGame _luaGame;
    private readonly IEventService _eventService;
    private readonly ILuaCsTimer _luaCsTimer;
    private readonly IDefaultLuaRegistrar _defaultLuaRegistrar;
    private readonly IPluginManagementService _pluginManagementService;
    private readonly INetworkingService _networkingService;
    private readonly IConsoleCommandsService _commandsService;
    private readonly ILuaConfigService _configService;
    private readonly ILuaCsInfoProvider _luaCsInfoProvider;
    private readonly Lazy<IPackageManagementService> _packageManagementService;
    //private readonly ILuaCsUtility _luaCsUtility;

    public LuaScriptManagementService(
        ILoggerService loggerService,
        ILuaScriptLoader loader,
        ILuaUserDataService userDataService,
        ISafeLuaUserDataService safeUserDataService,
        IDefaultLuaRegistrar defaultLuaRegistrar,
        ILuaScriptServicesConfig luaScriptServicesConfig,
        IPluginManagementService pluginManagementService,
        INetworkingService networkingService,
        LuaGame luaGame,
        IEventService eventService,
        //ILuaCsUtility luaCsUtility,
        ILuaCsTimer luaCsTimer,
        IConsoleCommandsService commandsService,
        ILuaCsInfoProvider luaCsInfoProvider, 
        ILuaConfigService configService, 
        Lazy<IPackageManagementService> packageManagementService)
    {
        _luaScriptLoader = loader;
        _userDataService = userDataService;
        _safeUserDataService = safeUserDataService;
        _defaultLuaRegistrar = defaultLuaRegistrar;
        _luaScriptServicesConfig = luaScriptServicesConfig;
        _loggerService = loggerService;
        _pluginManagementService = pluginManagementService;
        _networkingService = networkingService;

        _luaGame = luaGame;
        _eventService = eventService;
        _commandsService = commandsService;
        _luaCsInfoProvider = luaCsInfoProvider;
        _configService = configService;
        _packageManagementService = packageManagementService;
        _luaCsTimer = luaCsTimer;

        RegisterLuaEvents();
        RegisterConsoleCommands(_commandsService);
    }

    private void RegisterConsoleCommands(IConsoleCommandsService commands)
    {
#if CLIENT
        commands.RegisterCommand("cl_reloadlua|cl_reloadcs|cl_reloadluacs", "Re-initializes the LuaCs environment.", (string[] args) =>
        {
            LuaCsSetup.Instance.EventService.PublishEvent<IEventReloadAllPackages>(sub => sub.OnReloadAllPackages());
        });

        commands.RegisterCommand("cl_lua", $"cl_lua: Runs a string on the client.", (string[] args) =>
        {
            if (GameMain.Client != null && !GameMain.Client.HasPermission(ClientPermissions.ConsoleCommands))
            {
                DebugConsole.ThrowError("Command not permitted.");
                return;
            }

            if (LuaCsSetup.Instance.CurrentRunState != RunState.Running)
            {
                DebugConsole.ThrowError("LuaCs not initialized, use the console command cl_reloadluacs to force initialization.");
                return;
            }

            var result = LuaCsSetup.Instance.LuaScriptManagementService.DoString(string.Join(" ", args));
            LuaCsSetup.Instance.Logger.LogResults(result.ToResult());
        });

        commands.RegisterCommand("cl_toggleluadebug", "Toggles the MoonSharp Debug Server.", (string[] args) =>
        {
            DebugConsole.Log($"This command is currently not implemented. Please open a github issue if you need this feature.");
            /*int port = 41912;

            if (args.Length > 0)
            {
                int.TryParse(args[0], out port);
            }

            throw new NotImplementedException();
            //GameMain.LuaCs.ToggleDebugger(port);*/
        });

#elif SERVER
        commands.RegisterCommand("lua", "lua: Runs a string.", (string[] args) =>
        {
            var result = LuaCsSetup.Instance.LuaScriptManagementService.DoString(string.Join(" ", args));
            LuaCsSetup.Instance.Logger.LogResults(result.ToResult());
        });

        commands.RegisterCommand("reloadlua|reloadcs|reloadluacs", "Re-initializes the LuaCs environment.", (string[] args) =>
        {
            LuaCsSetup.Instance.EventService.PublishEvent<IEventReloadAllPackages>(sub => sub.OnReloadAllPackages());
        });

        commands.RegisterCommand("toggleluadebug", "Toggles the MoonSharp Debug Server.", (string[] args) =>
        {
            int port = 41912;

            if (args.Length > 0)
            {
                int.TryParse(args[0], out port);
            }

            throw new NotImplementedException();
            //GameMain.LuaCs.ToggleDebugger(port);
        });
#endif

#if SERVER
        commands.RegisterCommand("install_cl_lua|install_cl|install_cl_cs|install_cl_luacs", "Installs Client-Side LuaCs into your client.", (string[] args) =>
        {
            LuaCsInstaller.Install();
        });
#endif
    }

    public bool IsDisposed { get; private set; }

    public void SetCachingPolicy(bool useCaching)
    {
        _luaScriptLoader?.SetCachingPolicy(useCaching);
    }

    public async Task<FluentResults.Result> LoadScriptResourcesAsync(ImmutableArray<ILuaScriptResourceInfo> resourcesInfo)
    {
        if (!_luaCsInfoProvider.UseCaching)
        {
            return FluentResults.Result.Ok();
        }
        
        // Do any exception checks you can before acquiring a lock to avoid needlessly holding up resources.
        if (resourcesInfo.IsDefaultOrEmpty)
        {
            ThrowHelper.ThrowArgumentNullException($"{nameof(LoadScriptResourcesAsync)}: The parameter is empty!");
        }
        
        // Acquire a lock:
        // Reader = Allow parallel operations (try to avoid nesting acquiring the lock when possible)
        // Writer = Exclusive use (ie. executing scripts or Dispose())
        using var lck = await _operationsLock.AcquireWriterLock();   // IDisposable using with generate a try-finally and release for you.
        IService.CheckDisposed(this);                                    // Check disposed after you have the lock  
        
        // If you use a ConcurrentDictionary instead of a List, it will handle threading issues for you.
        _resourcesInfo.AddRange(resourcesInfo.OrderBy(static r => r.LoadPriority));

        // Use the StorageService's caching function by just loading the file with caching turned on.
        // Right now the LuaScriptLoader has this on by default.
        var cacheRes = await _luaScriptLoader.CacheResourcesAsync(resourcesInfo);
        
        // Aggregate and return results to the caller to deal with. Optionally, log here if you want.
        // Automatically converted to a Task<T> when 'async' is in the method declaration.
        if (cacheRes.IsFailed)
        {
            return cacheRes.ToResult();
        }
        return new FluentResults.Result().WithReasons(cacheRes.Value.SelectMany(cr => cr.Item2.Reasons));
    }

    public FluentResults.Result<DynValue> DoString(string code)
    {
        IService.CheckDisposed(this);
        if (_script == null || !IsRunning) { throw new Exception("Disposed"); }

        try
        {
            var result = _script.DoString(code);
            return FluentResults.Result.Ok(result);
        }
        catch (Exception ex)
        {
            return FluentResults.Result.Fail(new ExceptionalError(ex));
        }
    }

    private DynValue DoFile(string file, Table? globalContext = null, string? codeStringFriendly = null)
    {
        if (_script == null)
        {
            throw new Exception("Not running");
        }

        if (!LuaCsFile.CanReadFromPath(file))
        {
            // TODO: Replace with LuaScriptLoader IsFileAccessible.
            throw new ScriptRuntimeException($"dofile: File access to {file} not allowed.");
        }

        if (!LuaCsFile.Exists(file))
        {
            // TODO: Replace with LuaScriptLoader IsFileAccessible.
            throw new ScriptRuntimeException($"dofile: File {file} not found.");
        }

        return _script.DoFile(file, globalContext, codeStringFriendly);
    }

    private DynValue LoadFile(string file, Table? globalContext = null, string? codeStringFriendly = null)
    {
        if (_script == null)
        {
            throw new Exception("Not running");
        }

        if (!LuaCsFile.CanReadFromPath(file))
        {
            throw new ScriptRuntimeException($"loadfile: File access to {file} not allowed.");
        }

        if (!LuaCsFile.Exists(file))
        {
            throw new ScriptRuntimeException($"loadfile: File {file} not found.");
        }

        return _script.LoadFile(file, globalContext, codeStringFriendly);
    }

    private void RegisterLuaEvents()
    {
        _eventService.Subscribe<IEventAssemblyUnloading>(this);

        _eventService.RegisterLuaEventAlias<IEventUpdate>("think", nameof(IEventUpdate.OnUpdate));
        _eventService.RegisterLuaEventAlias<IEventKeyUpdate>("keyUpdate", nameof(IEventKeyUpdate.OnKeyUpdate));
        _eventService.RegisterLuaEventAlias<IEventAfflictionUpdate>("afflictionUpdate", nameof(IEventAfflictionUpdate.OnAfflictionUpdate));

        _eventService.RegisterLuaEventAlias<IEventCharacterCreated>("character.created", nameof(IEventCharacterCreated.OnCharacterCreated));
        _eventService.RegisterLuaEventAlias<IEventCharacterDeath>("character.death", nameof(IEventCharacterDeath.OnCharacterDeath));
        _eventService.RegisterLuaEventAlias<IEventCharacterDamageLimb>("character.damageLimb", nameof(IEventCharacterDamageLimb.OnCharacterDamageLimb));
        _eventService.RegisterLuaEventAlias<IEventGiveCharacterJobItems>("character.giveJobItems", nameof(IEventGiveCharacterJobItems.OnGiveCharacterJobItems));
        _eventService.RegisterLuaEventAlias<IEventHumanCPRSuccess>("character.CPRSuccess", nameof(IEventHumanCPRSuccess.OnCharacterCPRSuccess));
        _eventService.RegisterLuaEventAlias<IEventHumanCPRFailed>("character.CPRFailed", nameof(IEventHumanCPRFailed.OnCharacterCPRFailed));
        _eventService.RegisterLuaEventAlias<IEventHumanCPRSuccess>("human.CPRSuccess", nameof(IEventHumanCPRSuccess.OnCharacterCPRSuccess));
        _eventService.RegisterLuaEventAlias<IEventHumanCPRFailed>("human.CPRFailed", nameof(IEventHumanCPRFailed.OnCharacterCPRFailed));
        _eventService.RegisterLuaEventAlias<IEventCharacterApplyDamage>("character.applyDamage", nameof(IEventCharacterApplyDamage.OnCharacterApplyDamage));
        _eventService.RegisterLuaEventAlias<IEventCharacterApplyAffliction>("character.applyAffliction", nameof(IEventCharacterApplyAffliction.OnCharacterApplyAffliction));

        _eventService.RegisterLuaEventAlias<IEventGapOxygenUpdate>("gapOxygenUpdate", nameof(IEventGapOxygenUpdate.OnGapOxygenUpdate));

        _eventService.RegisterLuaEventAlias<IEventClientControlHusk>("husk.clientControlHusk", nameof(IEventClientControlHusk.OnClientControlHusk));

        _eventService.RegisterLuaEventAlias<IEventMeleeWeaponHandleImpact>("meleeWeapon.handleImpact", nameof(IEventMeleeWeaponHandleImpact.OnMeleeWeaponHandleImpact));

        _eventService.RegisterLuaEventAlias<IEventServerLog>("serverLog", nameof(IEventServerLog.OnServerLog));

        _eventService.RegisterLuaEventAlias<IEventTryClientChangeName>("tryChangeClientName", nameof(IEventTryClientChangeName.OnTryClienChangeName));

        _eventService.RegisterLuaEventAlias<IEventChangeFallDamage>("changeFallDamage", nameof(IEventChangeFallDamage.OnChangeFallDamage));

        _eventService.RegisterLuaEventAlias<IEventChatMessage>("chatMessage", nameof(IEventChatMessage.OnChatMessage));

        _eventService.RegisterLuaEventAlias<IEventCanUseVoiceRadio>("canUseVoiceRadio", nameof(IEventCanUseVoiceRadio.OnCanUseVoiceRadio));
        _eventService.RegisterLuaEventAlias<IEventChangeLocalVoiceRange>("changeLocalVoiceRange", nameof(IEventChangeLocalVoiceRange.OnChangeLocalVoiceRange));

        _eventService.RegisterLuaEventAlias<IEventRoundStarted>("roundStart", nameof(IEventRoundStarted.OnRoundStart));
        _eventService.RegisterLuaEventAlias<IEventRoundEnded>("roundEnd", nameof(IEventRoundEnded.OnRoundEnd));
        _eventService.RegisterLuaEventAlias<IEventMissionsEnded>("missionsEnded", nameof(IEventMissionsEnded.OnMissionsEnded));

        _eventService.RegisterLuaEventAlias<IEventSignalReceived>("signalReceived", nameof(IEventSignalReceived.OnSignalReceived));

        _eventService.RegisterLuaEventAlias<IEventItemCreated>("item.created", nameof(IEventItemCreated.OnItemCreated));
        _eventService.RegisterLuaEventAlias<IEventItemRemoved>("item.removed", nameof(IEventItemRemoved.OnItemRemoved));
        _eventService.RegisterLuaEventAlias<IEventItemUse>("item.use", nameof(IEventItemUse.OnItemUsed));
        _eventService.RegisterLuaEventAlias<IEventItemSecondaryUse>("item.secondaryUse", nameof(IEventItemSecondaryUse.OnItemSecondaryUsed));
        _eventService.RegisterLuaEventAlias<IEventItemReadPropertyChange>("item.readPropertyChange", nameof(IEventItemReadPropertyChange.OnItemReadPropertyChange));
        _eventService.RegisterLuaEventAlias<IEventItemDeconstructed>("item.deconstructed", nameof(IEventItemDeconstructed.OnItemDeconstructed));

        _eventService.RegisterLuaEventAlias<IEventInventoryPutItem>("inventoryPutItem", nameof(IEventInventoryPutItem.OnInventoryPutItem));
        _eventService.RegisterLuaEventAlias<IEventInventoryItemSwap>("inventoryItemSwap", nameof(IEventInventoryItemSwap.OnInventoryItemSwap));

        // Compatibility
        _eventService.RegisterLuaEventAlias<IEventCharacterCreated>("characterCreated", nameof(IEventCharacterCreated.OnCharacterCreated));
        _eventService.RegisterLuaEventAlias<IEventCharacterDeath>("characterDeath", nameof(IEventCharacterDeath.OnCharacterDeath));

#if SERVER
        _eventService.RegisterLuaEventAlias<IEventClientConnected>("client.connected", nameof(IEventClientConnected.OnClientConnected));
        _eventService.RegisterLuaEventAlias<IEventClientDisconnected>("client.disconnected", nameof(IEventClientDisconnected.OnClientDisconnected));
        _eventService.RegisterLuaEventAlias<IEventJobsAssigned>("jobsAssigned", nameof(IEventJobsAssigned.OnJobsAssigned));

        _eventService.RegisterLuaEventAlias<IEventClientRawNetMessageReceived>("netMessageReceived", nameof(IEventClientRawNetMessageReceived.OnReceivedClientNetMessage));

        // Compatibility
        _eventService.RegisterLuaEventAlias<IEventClientConnected>("clientConnected", nameof(IEventClientConnected.OnClientConnected));
        _eventService.RegisterLuaEventAlias<IEventClientDisconnected>("clientDisconnected", nameof(IEventClientDisconnected.OnClientDisconnected));
        _eventService.RegisterLuaEventAlias<IEventModifyChatMessage>("modifyChatMessage", nameof(IEventModifyChatMessage.OnModifyMessagePredicate));
#elif CLIENT
        _eventService.RegisterLuaEventAlias<IEventServerRawNetMessageReceived>("netMessageReceived", nameof(IEventServerRawNetMessageReceived.OnReceivedServerNetMessage));
#endif
    }

    private void SetupEnvironment(bool enableSandbox)
    {
        _script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.Debug | CoreModules.IO | CoreModules.OS_System);
        _script.Options.DebugPrint = (string msg) =>
        {
            _loggerService.LogMessage($"[Lua] {msg}");
        };
        SetCachingPolicy(_luaCsInfoProvider.UseCaching);
        
        _script.Options.ScriptLoader = _luaScriptLoader;
        _script.Options.CheckThreadAccess = false;

        Script.GlobalOptions.ShouldPCallCatchException = (Exception ex) => { return true; };

        UserData.RegisterType<ILuaCsHook.HookMethodType>();
        UserData.RegisterType(typeof(LuaGame));
        StandardUserDataDescriptor descriptor = (StandardUserDataDescriptor)UserData.RegisterType(typeof(EventService));
        descriptor.AddDynValue("HookMethodType", UserData.CreateStatic<ILuaCsHook.HookMethodType>());
        UserData.RegisterType(typeof(ILuaCsNetworking));
        UserData.RegisterType(typeof(ILuaCsUtility));
        UserData.RegisterType(typeof(ILuaCsTimer));
        UserData.RegisterType(typeof(LuaCsFile));
        UserData.RegisterType(typeof(ILuaScriptResourceInfo));
        UserData.RegisterType(typeof(IResourceInfo));
        UserData.RegisterType(typeof(IUserDataDescriptor));
        UserData.RegisterType(typeof(INetworkingService));
        UserData.RegisterType(typeof(ILuaConfigService));
        UserData.RegisterType(typeof(ILoggerService));

        UserData.RegisterType(typeof(ISettingBase));
        UserData.RegisterType(typeof(IDataInfo));

        Type[] settingBaseTypes = [
            typeof(ISettingBase<bool>),
            typeof(ISettingBase<string>),
            typeof(ISettingBase<byte>),
            typeof(ISettingBase<sbyte>),
            typeof(ISettingBase<ushort>),
            typeof(ISettingBase<short>),
            typeof(ISettingBase<char>),
            typeof(ISettingBase<uint>),
            typeof(ISettingBase<int>),
            typeof(ISettingBase<ulong>),
            typeof(ISettingBase<long>),
            typeof(ISettingBase<float>),
            typeof(ISettingBase<double>),

            typeof(ISettingRangeBase<float>),
            typeof(ISettingRangeBase<int>),

            typeof(ISettingList<string>),
            typeof(ISettingList<byte>),
            typeof(ISettingList<sbyte>),
            typeof(ISettingList<ushort>),
            typeof(ISettingList<short>),
            typeof(ISettingList<char>),
            typeof(ISettingList<uint>),
            typeof(ISettingList<int>),
            typeof(ISettingList<ulong>),
            typeof(ISettingList<long>),
            typeof(ISettingList<float>),
            typeof(ISettingList<double>),
        ];

        Dictionary<string, Dictionary<string, object>> settingsTable = [];

        foreach (Type type in settingBaseTypes)
        {
            UserData.RegisterType(type);

            string baseName = type.Name.RemoveFromEnd("`1").Substring(1);

            if (!settingsTable.ContainsKey(baseName))
            {
                settingsTable[baseName] = new Dictionary<string, object>();
            }

            settingsTable[baseName][type.GetGenericArguments()[0].Name] = UserData.CreateStatic(type);
        }
        
        foreach (var keyPair in settingsTable)
        {
            _script.Globals[keyPair.Key] = keyPair.Value;
        }

        UserData.RegisterType(typeof(ISettingRangeBase<int>));
#if CLIENT
        UserData.RegisterType(typeof(ISettingControl));
#endif

        new LuaConverters(this).RegisterLuaConverters();

        var luaRequire = new LuaRequire(_script);

        _script.Globals["setmodulepaths"] = (string[] str) => ((LuaScriptLoader)_luaScriptLoader).ModulePaths = str;

        _script.Globals["dofile"] = (Func<string, Table, string, DynValue>)DoFile;
        _script.Globals["loadfile"] = (Func<string, Table, string, DynValue>)LoadFile;
        _script.Globals["require"] = (Func<string, Table, DynValue>)luaRequire.Require;

        _script.Globals["printerror"] = (DynValue o) => { _loggerService.LogError($"[Lua] {o.ToString()}"); };

        _script.Globals["dostring"] = (Func<string, Table, string, DynValue>)_script.DoString;
        _script.Globals["load"] = (Func<string, Table, string, DynValue>)_script.LoadString;
        _script.Globals["Game"] = _luaGame;
        _script.Globals["Hook"] = _eventService;
        _script.Globals["Timer"] = _luaCsTimer;
        _script.Globals["File"] = UserData.CreateStatic<LuaCsFile>();
        _script.Globals["ConfigService"] = _configService;
        _script.Globals["Networking"] = _networkingService;
        _script.Globals["trygetpackage"] = (string name, out ContentPackage package) =>
            _packageManagementService.Value.TryGetLoadedPackageByName(name, out package);
        _script.Globals["Logger"] = _loggerService; 
        //_script.Globals["Steam"] = Steam;

        if (enableSandbox)
        {
            UserData.RegisterType(typeof(SafeLuaUserDataService));
            _script.Globals["LuaUserData"] = _safeUserDataService;
        }
        else
        {
            UserData.RegisterType(typeof(LuaUserDataService));
            _script.Globals["LuaUserData"] = _userDataService;
        }

        Table eventsTable = new Table(_script);

        var typesValue = _pluginManagementService.GetImplementingTypes<IEvent>(includeInterfaces: true, includeAbstractTypes: true);
        if (typesValue.IsSuccess)
        {
            foreach (var eventType in typesValue.Value)
            {
                if (eventType.IsGenericType) { continue; }
                if (!eventType.IsInterface) { continue; }

                UserData.RegisterType(eventType);
                eventsTable[eventType.Name] = UserData.CreateStatic(eventType);
            }
        }

        _script.Globals["Events"] = eventsTable;

        _script.Globals["ExecutionNumber"] = 0;
        _script.Globals["CSActive"] = !enableSandbox;
        ((Table)_script.Globals["debug"])["breakpoint"] = () => { Debugger.Break(); };

        _script.Globals["SERVER"] = LuaCsSetup.IsServer;
        _script.Globals["CLIENT"] = LuaCsSetup.IsClient;

        _defaultLuaRegistrar.RegisterAll();
    }

    public FluentResults.Result ExecuteLoadedScripts(ImmutableArray<ILuaScriptResourceInfo> executionOrder, bool enableSandbox)
    {        
        if (_isRunning) 
        { 
            return FluentResults.Result.Fail("Tried to execute Lua scripts without unloading first."); 
        }

        _loggerService.LogMessage("[Lua] Executing scripts");

        SetupEnvironment(enableSandbox);

        if (_script == null) { return FluentResults.Result.Ok(); } // never happens

        var result = FluentResults.Result.Ok();

        _isRunning = true;

        var packages = executionOrder.Select(r => r.OwnerPackage)
            .Distinct()
            .Select(p => $"{p.Dir}/Lua/?.lua")
            .ToArray();

        ((LuaScriptLoader)_luaScriptLoader).ModulePaths = packages;
        Table package = (Table)_script.Globals["package"];
        package.Set("path", DynValue.FromObject(_script, packages));

#if CLIENT
        if (GameMain.NetworkMember is { IsClient: true })
        {
            var startMessage = _networkingService.Start("_luastart");

            var packagesToReport = ContentPackageManager.EnabledPackages.All
                .Where(p => _packageManagementService.Value.PackageContainsAnyRunnableResource(p))
                .Where(p => !p.NameMatches(LuaCsSetup.PackageName))
                .ToList();

            startMessage.WriteUInt16((UInt16)packagesToReport.Count());

            foreach (var enabledPackage in packagesToReport)
            {
                var id = enabledPackage.UgcId;
                string hash = enabledPackage.Hash.StringRepresentation ?? "";

                startMessage.WriteString(enabledPackage.Name);
                startMessage.WriteString(enabledPackage.ModVersion);
                if (id.TryUnwrap(out ContentPackageId? packageId) && packageId is SteamWorkshopId steamId)
                {
                    startMessage.WriteUInt64(steamId.Value);
                }
                else
                {
                    startMessage.WriteUInt64(0);
                }
                startMessage.WriteString(hash);
            }

            _networkingService.Send(startMessage);
        }
#elif SERVER
        _networkingService.Receive("_luastart", (message, client) =>
        {
            var num = message.ReadUInt16();
            List<Table> packages = new List<Table>();

            for (int i = 0; i < num; i++)
            {
                Table table = new Table(_script);

                table.Set("Name", DynValue.NewString(message.ReadString()));
                table.Set("Version", DynValue.NewString(message.ReadString()));
                table.Set("Id", DynValue.NewString(message.ReadUInt64().ToString()));
                table.Set("Hash", DynValue.NewString(message.ReadString()));

                packages.Add(table);
            }

            _eventService.Call("client.packages", client, packages);
        });
#endif


        foreach (ILuaScriptResourceInfo resource in executionOrder.Where(l => l.IsAutorun))
        {
            foreach (ContentPath filePath in resource.FilePaths)
            {
                try
                {
                    _loggerService.LogMessage($"[Lua] - Run {filePath.Value}");
                    _script.Call(_script.LoadFile(filePath.FullPath), resource.OwnerPackage.Dir);
                }
                catch(Exception e)
                {
                    result = result.WithError(new ExceptionalError(e));
                }
            }
        }

        _eventService.Call("loaded");

        return result;
    }

    public DynValue? CallFunctionSafe(object luaFunction, params object[] args)
    {
        if (!IsRunning) { return null; }

        lock (_script)
        {
            try
            {
                return _script.Call(luaFunction, args);
            }
            catch (Exception e)
            {
                _loggerService.HandleException(e);
            }
            return null;
        }
    }

    public FluentResults.Result UnloadActiveScripts()
    {
        _isRunning = false;

        _script = null;

        return FluentResults.Result.Ok();
    }

    public FluentResults.Result DisposePackageResources(ContentPackage package)
    {
        return FluentResults.Result.Ok();
    }

    public FluentResults.Result DisposeAllPackageResources()
    {
        if (IsRunning)
        {
            UnloadActiveScripts();
        }

        _resourcesInfo.Clear();
        _luaScriptLoader.ClearCaches();

        return FluentResults.Result.Ok();
    }

    public FluentResults.Result Reset()
    {
        IService.CheckDisposed(this);
        _luaScriptLoader.ClearCaches();
        _userDataService.Reset();
        _luaCsTimer.Reset();
        RegisterLuaEvents();
        return DisposeAllPackageResources();
    }

    public void Dispose()
    {
        IsDisposed = true;
        _userDataService.Dispose();
        _luaScriptLoader.Dispose();
        _commandsService.Dispose();
    }

    public object? GetGlobalTableValue(string tableName)
    {
        if (!IsRunning) { return null; }

        return _script.Globals[tableName];
    }

    public void OnAssemblyUnloading(Assembly assembly)
    {
        foreach (Type type in assembly.SafeGetTypes())
        {
            UserData.UnregisterType(type, deleteHistory: true);
        }
    }
}

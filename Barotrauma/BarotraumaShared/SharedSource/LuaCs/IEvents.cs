using Barotrauma.Items.Components;
using Barotrauma.LuaCs.Data;
using Barotrauma.Networking;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using MoonSharp.Interpreter;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma.LuaCs.Events;

/*
 * The following is a collection of interfaces that types can implement to be registered events.
 * Note: Internally-marked interfaces should be consumed using a publicizer. This is due to the Barotrauma source
 * types being internal by default.
*/

public interface IEvent
{
    bool IsLuaRunner() => false;
    
    public abstract class LuaWrapperBase : IEvent
    {
        protected readonly IDictionary<string, LuaCsFunc> LuaFuncs;
        protected LuaWrapperBase(IDictionary<string, LuaCsFunc> luaFuncs) => LuaFuncs = luaFuncs;
        public bool IsLuaRunner() => true;
    }
}

public interface IEvent<out T> : IEvent where T : class, IEvent<T>
{
    static virtual T GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
    {
        throw new InvalidOperationException($"Lua runners forbidden for  {typeof(T).Name}");
    }
}

#region RuntimeServiceEvents

/// <summary>
/// Called when the current <see cref="Screen"/> (game state) changes. Upstream Type 'Screen' is internal. 
/// </summary>
internal interface IEventScreenSelected : IEvent<IEventScreenSelected>
{
    void OnScreenSelected(Screen screen);
}

/// <summary>
/// Called whenever the list of all <see cref="ContentPackage"/> (enabled and disabled) on disk has changed.
/// </summary>
internal interface IEventAllPackageListChanged : IEvent<IEventAllPackageListChanged>
{
    void OnAllPackageListChanged(IEnumerable<CorePackage> corePackages, IEnumerable<RegularPackage> regularPackages);
}

/// <summary>
/// Called whenever the list of enabled <see cref="ContentPackage"/> has changed.
/// </summary>
internal interface IEventEnabledPackageListChanged : IEvent<IEventEnabledPackageListChanged>
{
    void OnEnabledPackageListChanged(CorePackage package, IEnumerable<RegularPackage> regularPackages);
}

internal interface IEventReloadAllPackages : IEvent<IEventReloadAllPackages>
{
    void OnReloadAllPackages();
}

internal interface IEventSettingInstanceLifetime : IEvent<IEventSettingInstanceLifetime>
{
    void OnSettingInstanceCreated<T>(T configInstance) where T : ISettingBase;
    void OnSettingInstanceDisposed<T>(T configInstance) where T : ISettingBase;
}

#endregion

#region GameEvents

#if SERVER
/// <summary>
/// Allows the user to modify a chat message on the server before it is sent to clients, or reject the message altogether.
/// </summary>
/// <remarks>Legacy Lua Event Name: "modifyChatMessage"</remarks>
internal interface IEventModifyChatMessage : IEvent<IEventModifyChatMessage>
{
    bool? OnModifyMessagePredicate(ChatMessage message, WifiComponent senderRadio);
    
    static IEventModifyChatMessage IEvent<IEventModifyChatMessage>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc) =>
        new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventModifyChatMessage
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }
        
        /// <summary>
        /// Called before a chat message is sent to clients.
        /// </summary>
        /// <param name="message">Message to be sent.</param>
        /// <param name="senderRadio"><b>[CanBeNull]</b> The source <see cref="ItemComponent"/>, if any.</param>
        /// <returns>Whether to <b><i>reject</i></b> the message.</returns>
        public bool? OnModifyMessagePredicate(ChatMessage message, WifiComponent senderRadio)
        {
            object result = LuaFuncs[nameof(OnModifyMessagePredicate)](message, senderRadio);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    } 
}

#endif

internal interface IEventAfflictionUpdate : IEvent<IEventAfflictionUpdate>
{
    void OnAfflictionUpdate(Affliction affliction, CharacterHealth characterHealth, Limb targetLimb, float deltaTime);

    static IEventAfflictionUpdate IEvent<IEventAfflictionUpdate>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc) =>
        new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventAfflictionUpdate
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }
        
        public void OnAfflictionUpdate(Affliction affliction, CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            LuaFuncs[nameof(OnAfflictionUpdate)](affliction, characterHealth, targetLimb, deltaTime);
        }
    }
}

internal interface IEventGiveCharacterJobItems : IEvent<IEventGiveCharacterJobItems>
{
    void OnGiveCharacterJobItems(Character character, WayPoint spawnPoint, bool isPvPMode);

    static IEventGiveCharacterJobItems IEvent<IEventGiveCharacterJobItems>.GetLuaRunner(
        IDictionary<string, LuaCsFunc> luaFunc) => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventGiveCharacterJobItems
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnGiveCharacterJobItems(Character character, WayPoint spawnPoint, bool isPvPMode)
        {
            LuaFuncs[nameof(OnGiveCharacterJobItems)](character, spawnPoint, isPvPMode);
        }
    }
}

internal interface IEventCharacterCreated : IEvent<IEventCharacterCreated>
{
    void OnCharacterCreated(Character character);

    static IEventCharacterCreated IEvent<IEventCharacterCreated>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventCharacterCreated
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }
        
        public void OnCharacterCreated(Character character)
        {
            LuaFuncs[nameof(OnCharacterCreated)](character);
        }
    }
}

// TODO: harmony-fy
internal interface IEventHumanCPRSuccess : IEvent<IEventHumanCPRSuccess>
{
    void OnCharacterCPRSuccess(HumanoidAnimController animController);

    static IEventHumanCPRSuccess IEvent<IEventHumanCPRSuccess>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventHumanCPRSuccess
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnCharacterCPRSuccess(HumanoidAnimController animController)
        {
            LuaFuncs[nameof(OnCharacterCPRSuccess)](animController);
        }
    }
}

// TODO: harmony-fy
internal interface IEventHumanCPRFailed : IEvent<IEventHumanCPRFailed>
{
    void OnCharacterCPRFailed(HumanoidAnimController animController);

    static IEventHumanCPRFailed IEvent<IEventHumanCPRFailed>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventHumanCPRFailed
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnCharacterCPRFailed(HumanoidAnimController animController)
        {
            LuaFuncs[nameof(OnCharacterCPRFailed)](animController);
        }
    }
}

// TODO: harmony-fy
internal interface IEventClientControlHusk : IEvent<IEventClientControlHusk>
{
    void OnClientControlHusk(Client client, Character husk);

    static IEventClientControlHusk IEvent<IEventClientControlHusk>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventClientControlHusk
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnClientControlHusk(Client client, Character husk)
        {
            LuaFuncs[nameof(OnClientControlHusk)](client, husk);
        }
    }
}

// TODO: harmony-fy
internal interface IEventMeleeWeaponHandleImpact : IEvent<IEventMeleeWeaponHandleImpact>
{
    void OnMeleeWeaponHandleImpact(MeleeWeapon meleeWeapon, Body target);

    static IEventMeleeWeaponHandleImpact IEvent<IEventMeleeWeaponHandleImpact>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventMeleeWeaponHandleImpact
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnMeleeWeaponHandleImpact(MeleeWeapon meleeWeapon, Body target)
        {
            LuaFuncs[nameof(OnMeleeWeaponHandleImpact)](meleeWeapon, target);
        }
    }
}

// TODO: harmony-fy
internal interface IEventServerLog : IEvent<IEventServerLog>
{
    void OnServerLog(string line, ServerLog.MessageType messageType);

    static IEventServerLog IEvent<IEventServerLog>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventServerLog
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnServerLog(string line, ServerLog.MessageType messageType)
        {
            LuaFuncs[nameof(OnServerLog)](line, messageType);
        }
    }
}

// TODO: harmony-fy
internal interface IEventChatMessage : IEvent<IEventChatMessage>
{
    bool? OnChatMessage(string messageText, Client sender, ChatMessageType type, ChatMessage message);

    static IEventChatMessage IEvent<IEventChatMessage>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventChatMessage
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnChatMessage(string messageText, Client sender, ChatMessageType type, ChatMessage message)
        {
            object result = LuaFuncs[nameof(OnChatMessage)](messageText, sender, type, message);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventTryClientChangeName : IEvent<IEventTryClientChangeName>
{
    bool? OnTryClienChangeName(Client client, string newName, Identifier newJob, CharacterTeamType newTeam);

    static IEventTryClientChangeName IEvent<IEventTryClientChangeName>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventTryClientChangeName
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnTryClienChangeName(Client client, string newName, Identifier newJob, CharacterTeamType newTeam)
        {
            var result = LuaFuncs[nameof(OnTryClienChangeName)](client, newName, newJob, newTeam);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventChangeFallDamage : IEvent<IEventChangeFallDamage>
{
    float? OnChangeFallDamage(float impactDamage, Character character, Vector2 impactPos, Vector2 velocity);

    static IEventChangeFallDamage IEvent<IEventChangeFallDamage>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventChangeFallDamage
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public float? OnChangeFallDamage(float impactDamage, Character character, Vector2 impactPos, Vector2 velocity)
        {
            var result = LuaFuncs[nameof(OnChangeFallDamage)](impactDamage, character, impactPos, velocity);
            if (result is DynValue dynValue && dynValue.Type == DataType.Number)
            {
                return (float)dynValue.Number;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventGapOxygenUpdate : IEvent<IEventGapOxygenUpdate>
{
    bool? OnGapOxygenUpdate(Gap gap, Hull hull1, Hull hull2);

    static IEventGapOxygenUpdate IEvent<IEventGapOxygenUpdate>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventGapOxygenUpdate
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnGapOxygenUpdate(Gap gap, Hull hull1, Hull hull2)
        {
            var result = LuaFuncs[nameof(OnGapOxygenUpdate)](gap, hull1, hull2);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventCharacterApplyDamage : IEvent<IEventCharacterApplyDamage>
{
    bool? OnCharacterApplyDamage(CharacterHealth characterHealth, AttackResult attackResult, Limb hitLimb, bool allowStacking);

    static IEventCharacterApplyDamage IEvent<IEventCharacterApplyDamage>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventCharacterApplyDamage
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnCharacterApplyDamage(CharacterHealth characterHealth, AttackResult attackResult, Limb hitLimb, bool allowStacking)
        {
            var result = LuaFuncs[nameof(OnCharacterApplyDamage)](characterHealth, attackResult, hitLimb, allowStacking);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventCharacterApplyAffliction : IEvent<IEventCharacterApplyAffliction>
{
    bool? OnCharacterApplyAffliction(CharacterHealth characterHealth, CharacterHealth.LimbHealth limbHealth, Affliction newAffliction, bool allowStacking);

    static IEventCharacterApplyAffliction IEvent<IEventCharacterApplyAffliction>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventCharacterApplyAffliction
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnCharacterApplyAffliction(CharacterHealth characterHealth, CharacterHealth.LimbHealth limbHealth, Affliction newAffliction, bool allowStacking)
        {
            var result = LuaFuncs[nameof(OnCharacterApplyAffliction)](characterHealth, limbHealth, newAffliction, allowStacking);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventItemReadPropertyChange : IEvent<IEventItemReadPropertyChange>
{
    bool? OnItemReadPropertyChange(Item item, SerializableProperty property, object parentObject, bool allowEditing, Client sender);

    static IEventItemReadPropertyChange IEvent<IEventItemReadPropertyChange>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventItemReadPropertyChange
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnItemReadPropertyChange(Item item, SerializableProperty property, object parentObject, bool allowEditing, Client sender)
        {
            var result = LuaFuncs[nameof(OnItemReadPropertyChange)](item, property, parentObject, allowEditing, sender);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventCanUseVoiceRadio : IEvent<IEventCanUseVoiceRadio>
{
    bool? OnCanUseVoiceRadio(Client sender, Client recipient);

    static IEventCanUseVoiceRadio IEvent<IEventCanUseVoiceRadio>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventCanUseVoiceRadio
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnCanUseVoiceRadio(Client sender, Client recipient)
        {
            var result = LuaFuncs[nameof(OnCanUseVoiceRadio)](sender, recipient);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventChangeLocalVoiceRange : IEvent<IEventChangeLocalVoiceRange>
{
    float? OnChangeLocalVoiceRange(Client sender, Client recipient);

    static IEventChangeLocalVoiceRange IEvent<IEventChangeLocalVoiceRange>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventChangeLocalVoiceRange
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public float? OnChangeLocalVoiceRange(Client sender, Client recipient)
        {
            var result = LuaFuncs[nameof(OnChangeLocalVoiceRange)](sender, recipient);
            if (result is DynValue dynValue && dynValue.Type == DataType.Number)
            {
                return (float)dynValue.Number;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventItemDeconstructed : IEvent<IEventItemDeconstructed>
{
    bool? OnItemDeconstructed(Item item, Deconstructor deconstructor, Character user, bool allowRemove);

    static IEventItemDeconstructed IEvent<IEventItemDeconstructed>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventItemDeconstructed
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnItemDeconstructed(Item item, Deconstructor deconstructor, Character user, bool allowRemove)
        {
            var result = LuaFuncs[nameof(OnItemDeconstructed)](item, deconstructor, user, allowRemove);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

// TODO: harmony-fy
internal interface IEventWifiSignalTransmitted : IEvent<IEventWifiSignalTransmitted>
{
    bool? OnWifiSignalTransmitted(WifiComponent wifiComponent, Signal signal, bool sentFromChat);

    static IEventWifiSignalTransmitted IEvent<IEventWifiSignalTransmitted>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventWifiSignalTransmitted
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnWifiSignalTransmitted(WifiComponent wifiComponent, Signal signal, bool sentFromChat)
        {
            var result = LuaFuncs[nameof(OnWifiSignalTransmitted)](wifiComponent, signal, sentFromChat);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

internal interface IEventCharacterDeath : IEvent<IEventCharacterDeath>
{
    void OnCharacterDeath(Character character, Affliction causeOfDeathAffliction, CauseOfDeathType causeOfDeathType);

    static IEventCharacterDeath IEvent<IEventCharacterDeath>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventCharacterDeath
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnCharacterDeath(Character character, Affliction causeOfDeathAffliction, CauseOfDeathType causeOfDeathType)
        {
            LuaFuncs[nameof(OnCharacterDeath)](character, causeOfDeathAffliction, causeOfDeathType);
        }
    }
}

public interface IEventKeyUpdate : IEvent<IEventKeyUpdate>
{
    void OnKeyUpdate(double deltaTime);

    static IEventKeyUpdate IEvent<IEventKeyUpdate>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventKeyUpdate
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnKeyUpdate(double deltaTime)
        {
            LuaFuncs[nameof(OnKeyUpdate)](deltaTime);
        }
    }
}

/// <summary>
/// Called as soon as round begins to load before any loading takes place.
/// </summary>
public interface IEventRoundStarting : IEvent<IEventRoundStarting>
{
    void OnRoundStarting();

    static IEventRoundStarting IEvent<IEventRoundStarting>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);
    
    public sealed class LuaWrapper : LuaWrapperBase, IEventRoundStarting
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnRoundStarting()
        {
            LuaFuncs[nameof(OnRoundStarting)]();
        }
    }
}

/// <summary>
/// Called when a round has started and fully loaded.
/// </summary>
public interface IEventRoundStarted : IEvent<IEventRoundStarted>
{
    void OnRoundStart();

    static IEventRoundStarted IEvent<IEventRoundStarted>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);
    
    public sealed class LuaWrapper : LuaWrapperBase, IEventRoundStarted
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnRoundStart()
        {
            LuaFuncs[nameof(OnRoundStart)]();
        }
    }
}

/// <summary>
/// Called when a round has ended.
/// </summary>
public interface IEventRoundEnded : IEvent<IEventRoundEnded>
{
    void OnRoundEnd();

    static IEventRoundEnded IEvent<IEventRoundEnded>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventRoundEnded
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnRoundEnd()
        {
            LuaFuncs[nameof(OnRoundEnd)]();
        }
    }
}

internal interface IEventMissionsEnded : IEvent<IEventMissionsEnded>
{
    void OnMissionsEnded(IReadOnlyList<Mission> missions);

    static IEventMissionsEnded IEvent<IEventMissionsEnded>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventMissionsEnded
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnMissionsEnded(IReadOnlyList<Mission> missions)
        {
            LuaFuncs[nameof(OnMissionsEnded)](missions);
        }
    }
}

/// <summary>
/// Called on game loop normal update.
/// </summary>
public interface IEventUpdate : IEvent<IEventUpdate>
{
    void OnUpdate(double fixedDeltaTime);
    static IEventUpdate IEvent<IEventUpdate>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
    => new LuaWrapper(luaFunc);
    
    public sealed class LuaWrapper : LuaWrapperBase, IEventUpdate
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnUpdate(double deltaTime)
        {
            LuaFuncs[nameof(OnUpdate)](deltaTime);
        }
    }
}

/// <summary>
/// Called on game loop draw update.
/// </summary>
public interface IEventDrawUpdate : IEvent<IEventDrawUpdate>
{
    void OnDrawUpdate(double deltaTime);

    static IEventDrawUpdate IEvent<IEventDrawUpdate>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);
    
    public sealed class LuaWrapper : LuaWrapperBase, IEventDrawUpdate
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnDrawUpdate(double deltaTime)
        {
            LuaFuncs[nameof(OnDrawUpdate)](deltaTime);
        }
    }
}

interface IEventSignalReceived : IEvent<IEventSignalReceived>
{
    void OnSignalReceived(Signal signal, Connection connection);

    static IEventSignalReceived IEvent<IEventSignalReceived>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventSignalReceived
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnSignalReceived(Signal signal, Connection connection)
        {
            LuaFuncs[nameof(OnSignalReceived)](signal, connection);
        }
    }
}

interface IEventItemCreated : IEvent<IEventItemCreated>
{
    void OnItemCreated(Item item);

    static IEventItemCreated IEvent<IEventItemCreated>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventItemCreated
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnItemCreated(Item item)
        {
            LuaFuncs[nameof(OnItemCreated)](item);
        }
    }
}

interface IEventItemRemoved : IEvent<IEventItemRemoved>
{
    void OnItemRemoved(Item item);

    static IEventItemRemoved IEvent<IEventItemRemoved>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventItemRemoved
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnItemRemoved(Item item)
        {
            LuaFuncs[nameof(OnItemRemoved)](item);
        }
    }
}

interface IEventItemUse : IEvent<IEventItemUse>
{
    bool? OnItemUsed(Item item, Character user, Limb targetLimb, Entity useTarget);

    static IEventItemUse IEvent<IEventItemUse>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventItemUse
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnItemUsed(Item item, Character user, Limb targetLimb, Entity useTarget)
        {
            var result = LuaFuncs[nameof(OnItemUsed)](item, user, targetLimb, useTarget);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

interface IEventItemSecondaryUse : IEvent<IEventItemSecondaryUse>
{
    bool? OnItemSecondaryUsed(Item item, Character user);

    static IEventItemSecondaryUse IEvent<IEventItemSecondaryUse>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventItemSecondaryUse
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnItemSecondaryUsed(Item item, Character user)
        {
            var result = LuaFuncs[nameof(OnItemSecondaryUsed)](item, user);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

interface IEventCharacterDamageLimb : IEvent<IEventCharacterDamageLimb>
{
    AttackResult? OnCharacterDamageLimb(Character character, Vector2 worldPosition, Limb hitLimb, IEnumerable<Affliction> afflictions, float stun, bool playSound, Vector2 attackImpulse, Character attacker = null, float damageMultiplier = 1, bool allowStacking = true, float penetration = 0f, bool shouldImplode = false);

    static IEventCharacterDamageLimb IEvent<IEventCharacterDamageLimb>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventCharacterDamageLimb
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public AttackResult? OnCharacterDamageLimb(Character character, Vector2 worldPosition, Limb hitLimb, IEnumerable<Affliction> afflictions, float stun, bool playSound, Vector2 attackImpulse, Character attacker = null, float damageMultiplier = 1, bool allowStacking = true, float penetration = 0f, bool shouldImplode = false)
        {
            object result = LuaFuncs[nameof(OnCharacterDamageLimb)](character, worldPosition, hitLimb, afflictions, stun, playSound, attackImpulse, attacker, damageMultiplier, allowStacking, penetration, shouldImplode);
            if (result is DynValue dynValue)
            {
                result = dynValue.ToObject();
            }

            if (result is AttackResult attackResult)
            {
                return attackResult;
            }

            return null;
        }
    }
}

interface IEventInventoryPutItem : IEvent<IEventInventoryPutItem>
{
    bool? OnInventoryPutItem(Inventory inventory, Item item, Character user, int i, bool removeItem);

    static IEventInventoryPutItem IEvent<IEventInventoryPutItem>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventInventoryPutItem
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnInventoryPutItem(Inventory inventory, Item item, Character user, int i, bool removeItem)
        {
            var result = LuaFuncs[nameof(OnInventoryPutItem)](inventory, item, user, i, removeItem);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

interface IEventInventoryItemSwap : IEvent<IEventInventoryItemSwap>
{
    bool? OnInventoryItemSwap(Inventory inventory, Item item, Character user, int i, bool swapWholeStack);

    static IEventInventoryItemSwap IEvent<IEventInventoryItemSwap>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventInventoryItemSwap
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnInventoryItemSwap(Inventory inventory, Item item, Character user, int i, bool swapWholeStack)
        {
            var result = LuaFuncs[nameof(OnInventoryItemSwap)](inventory, item, user, i, swapWholeStack);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

#endregion

#region Networking



#region Networking-Server
#if SERVER
public interface IEventClientRawNetMessageReceived : IEvent<IEventClientRawNetMessageReceived>
{
    bool? OnReceivedClientNetMessage(IReadMessage netMessage, ClientPacketHeader clientPacketHeader, NetworkConnection sender);

    static IEventClientRawNetMessageReceived IEvent<IEventClientRawNetMessageReceived>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventClientRawNetMessageReceived
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnReceivedClientNetMessage(IReadMessage netMessage, ClientPacketHeader clientPacketHeader, NetworkConnection sender)
        {
            if (GameMain.Server == null) { return null; }

            Client client = GameMain.Server.ConnectedClients.FirstOrDefault(c => c.Connection == sender);

            if (client == null) { return null; }

            var result = LuaFuncs[nameof(OnReceivedClientNetMessage)](netMessage, clientPacketHeader, client);

            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

/// <summary>
/// Called when a client connects to the server.
/// </summary>
interface IEventClientConnected : IEvent<IEventClientConnected>
{
    /// <summary>
    /// Called when a client connects to the server.
    /// </summary>
    /// <param name="client">The connecting client.</param>
    void OnClientConnected(Client client);

    static IEventClientConnected IEvent<IEventClientConnected>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);
    
    public sealed class LuaWrapper : LuaWrapperBase, IEventClientConnected
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnClientConnected(Client client)
        {
            LuaFuncs[nameof(OnClientConnected)](client);   
        }
    }
}

/// <summary>
/// Called when a client disconnects from the server.
/// </summary>
interface IEventClientDisconnected : IEvent<IEventClientDisconnected>
{
    /// <summary>
    /// Called when a client connects to the server.
    /// </summary>
    /// <param name="client">The connecting client.</param>
    void OnClientDisconnected(Client client);

    static IEventClientDisconnected IEvent<IEventClientDisconnected>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventClientDisconnected
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnClientDisconnected(Client client)
        {
            LuaFuncs[nameof(OnClientDisconnected)](client);
        }
    }
}

interface IEventJobsAssigned : IEvent<IEventJobsAssigned>
{
    /// <summary>
    /// Called when a client connects to the server.
    /// </summary>
    /// <param name="client">The connecting client.</param>
    void OnJobsAssigned(IReadOnlyList<Client> unassignedClients);

    static IEventJobsAssigned IEvent<IEventJobsAssigned>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventJobsAssigned
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnJobsAssigned(IReadOnlyList<Client> unassignedClients)
        {
            LuaFuncs[nameof(OnJobsAssigned)](unassignedClients);
        }
    }
}
#endif

#endregion

#region Networking-Client
#if CLIENT

public interface IEventServerRawNetMessageReceived : IEvent<IEventServerRawNetMessageReceived>
{
    bool? OnReceivedServerNetMessage(IReadMessage netMessage, ServerPacketHeader serverPacketHeader);

    static IEventServerRawNetMessageReceived IEvent<IEventServerRawNetMessageReceived>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
    => new LuaWrapper(luaFunc);

    public sealed class LuaWrapper : LuaWrapperBase, IEventServerRawNetMessageReceived
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public bool? OnReceivedServerNetMessage(IReadMessage netMessage, ServerPacketHeader serverPacketHeader)
        {
            var result = LuaFuncs[nameof(OnReceivedServerNetMessage)](netMessage, serverPacketHeader);
            if (result is DynValue dynValue && dynValue.Type == DataType.Boolean)
            {
                return dynValue.Boolean;
            }

            return null;
        }
    }
}

/// <summary>
/// Called when the client has connected to the server and loaded to the lobby.
/// </summary>
public interface IEventServerConnected : IEvent<IEventServerConnected>
{
    void OnServerConnected();

    static IEventServerConnected IEvent<IEventServerConnected>.GetLuaRunner(IDictionary<string, LuaCsFunc> luaFunc)
        => new LuaWrapper(luaFunc);
    
    public sealed class LuaWrapper : LuaWrapperBase, IEventServerConnected
    {
        public LuaWrapper(IDictionary<string, LuaCsFunc> luaFuncs) : base(luaFuncs)
        {
        }

        public void OnServerConnected()
        {
            LuaFuncs[nameof(OnServerConnected)]();
        }
    }
}
#endif
#endregion

#endregion

#region Assembly_PluginEvents

/// <summary>
/// Called on plugin normal, use this for basic/core loading that does not rely on any other modded content.
/// </summary>
public interface IEventPluginInitialize : IEvent<IEventPluginInitialize>
{
    void Initialize();
}

/// <summary>
/// Called once all plugins have been loaded. if you have integrations with any other mod, put that code here.
/// </summary>
public interface IEventPluginLoadCompleted : IEvent<IEventPluginLoadCompleted>
{
    void OnLoadCompleted();
}

/// <summary>
/// Called before Barotrauma initializes plugins. Use if you want to patch another plugin's behaviour 'unofficially'.
/// WARNING: This method is called before Initialize()!
/// </summary>
public interface IEventPluginPreInitialize : IEvent<IEventPluginPreInitialize>
{
    void PreInitPatching();
}

/// <summary>
/// Called whenever a new assembly is loaded.
/// </summary>
public interface IEventAssemblyLoaded : IEvent<IEventAssemblyLoaded>
{
    void OnAssemblyLoaded(Assembly assembly);
}

/// <summary>
/// Called whenever an <see cref="IAssemblyLoaderService"/> is instanced.
/// </summary>
public interface IEventAssemblyContextCreated : IEvent<IEventAssemblyContextCreated>
{
    void OnAssemblyCreated(IAssemblyLoaderService loaderService);
}

/// <summary>
/// Called whenever an <see cref="IAssemblyLoaderService"/> begins unloading.
/// </summary>
public interface IEventAssemblyContextUnloading : IEvent<IEventAssemblyContextUnloading>
{
    void OnAssemblyUnloading(WeakReference<IAssemblyLoaderService> loaderService);
}

public interface IEventAssemblyUnloading : IEvent<IEventAssemblyUnloading>
{
    void OnAssemblyUnloading(Assembly assembly);
}

#endregion

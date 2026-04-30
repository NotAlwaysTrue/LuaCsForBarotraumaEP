using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Barotrauma
{
    class DelayedListElement
    {
        public readonly long Id;
        public readonly DelayedEffect Parent;
        public readonly Entity Entity;

        private Vector2? _worldPosition;
        private readonly object _worldPositionLock = new object();
        public Vector2? WorldPosition
        {
            get
            {
                lock (_worldPositionLock)
                {
                    return _worldPosition;
                }
            }
            set
            {
                lock (_worldPositionLock)
                {
                    _worldPosition = value;
                }
            }
        }

        /// <summary>
        /// Should the delayed effect attempt to determine the position of the effect based on the targets, or just use the position that was passed to the constructor.
        /// </summary>
        public bool GetPositionBasedOnTargets;
        public readonly Vector2? StartPosition;
        public readonly List<ISerializableEntity> Targets;
        
        private volatile float _delay;
        public float Delay
        {
            get => _delay;
            set => _delay = value;
        }

        public DelayedListElement(DelayedEffect parentEffect, Entity parentEntity, IEnumerable<ISerializableEntity> targets, float delay, Vector2? worldPosition, Vector2? startPosition)
        {
            Id = Interlocked.Increment(ref DelayedEffect._delayElementIdCounter);
            Parent = parentEffect;
            Entity = parentEntity;
            Targets = new List<ISerializableEntity>(targets);
            Delay = delay;
            WorldPosition = worldPosition;
            StartPosition = startPosition;
        }
    }
    
    class DelayedEffect : StatusEffect
    {
        // Thread-safe counter for generating unique IDs for DelayedListElement
        internal static long _delayElementIdCounter;
        
        // Thread-safe dictionary for delayed effects
        public static readonly ConcurrentDictionary<long, DelayedListElement> DelayListDict = new ConcurrentDictionary<long, DelayedListElement>();
        
        /// <summary>
        /// Provides a thread-safe enumerable view of the delay list for iteration.
        /// </summary>
        public static IEnumerable<DelayedListElement> DelayList => DelayListDict.Values;

        private enum DelayTypes 
        { 
            Timer = 0,
            [Obsolete("The delay type is unsupported.")]
            ReachCursor = 1 
        }

        private readonly DelayTypes delayType;
        private readonly float delay;

        public DelayedEffect(ContentXElement element, string parentDebugName)
            : base(element, parentDebugName)
        {
            delayType = element.GetAttributeEnum("delaytype", DelayTypes.Timer);
            if (delayType == DelayTypes.ReachCursor)
            {
                DebugConsole.AddWarning($"Potential error in {parentDebugName}: the delay type {DelayTypes.ReachCursor} is not supported.", contentPackage: element.ContentPackage);
            }
            if (delayType is DelayTypes.Timer)
            {
                delay = element.GetAttributeFloat("delay", 1.0f);
            }
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target, Vector2? worldPosition = null)
        {
            if (this.type != type || !HasRequiredItems(entity)) { return; }
            if (!Stackable)
            {
                // Thread-safe iteration over ConcurrentDictionary
                foreach (var kvp in DelayListDict)
                {
                    if (kvp.Value.Parent == this && kvp.Value.Targets.FirstOrDefault() == target) 
                    {
                        return; 
                    }
                }
            }
            if (!IsValidTarget(target)) { return; }

            var targets = CurrentTargets;
            targets.Clear();
            targets.Add(target);
            if (!HasRequiredConditions(targets)) { return; }

            switch (delayType)
            {
                case DelayTypes.Timer:
                    var newDelayListElement = new DelayedListElement(this, entity, targets, delay, worldPosition ?? GetPosition(entity, targets, worldPosition), startPosition: null)
                    {
                        GetPositionBasedOnTargets = worldPosition == null
                    };
                    DelayListDict.TryAdd(newDelayListElement.Id, newDelayListElement);
                    break;
                case DelayTypes.ReachCursor:
                    Projectile projectile = (entity as Item)?.GetComponent<Projectile>();
                    if (projectile == null)
                    {
                        DebugConsole.LogError("Non-projectile using a delaytype of reachcursor");
                        return;
                    }

                    var user =
                        projectile.User ??
                        projectile.Attacker ??
                        projectile.Launcher?.GetRootInventoryOwner() as Character;
                    if (user == null)
                    {
#if DEBUG
                        DebugConsole.LogError($"Projectile \"{projectile.Item.Prefab.Identifier}\" missing user");
#endif
                        return;
                    }

                    var reachCursorElement = new DelayedListElement(this, entity, targets, Vector2.Distance(entity.WorldPosition, projectile.User.CursorWorldPosition), worldPosition, entity.WorldPosition);
                    DelayListDict.TryAdd(reachCursorElement.Id, reachCursorElement);
                    break;
            }
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Vector2? worldPosition = null)
        {
            if (this.type != type) { return; }
            if (Disabled) { return; }
            if (ShouldWaitForInterval(entity, deltaTime)) { return; }
            if (!HasRequiredItems(entity)) { return; }
            if (delayType == DelayTypes.ReachCursor && Character.Controlled == null) { return; }
            if (!Stackable) 
            { 
                // Thread-safe iteration over ConcurrentDictionary
                foreach (var kvp in DelayListDict)
                {
                    if (kvp.Value.Parent == this && kvp.Value.Targets.SequenceEqual(targets)) { return; }
                }
            }

            var localTargets = CurrentTargets;
            localTargets.Clear();
            foreach (ISerializableEntity target in targets)
            {
                if (!IsValidTarget(target)) { continue; }
                localTargets.Add(target);
            }

            if (!HasRequiredConditions(localTargets)) { return; }

            switch (delayType)
            {
                case DelayTypes.Timer:
                    var timerElement = new DelayedListElement(this, entity, localTargets, delay, worldPosition, null);
                    DelayListDict.TryAdd(timerElement.Id, timerElement);
                    break;
                case DelayTypes.ReachCursor:
                    Projectile projectile = (entity as Item)?.GetComponent<Projectile>();
                    if (projectile == null)
                    {
#if DEBUG
                        DebugConsole.LogError("Non-projectile using a delaytype of reachcursor");
#endif
                        return;
                    }

                    var user =
                        projectile.User ??
                        projectile.Attacker ??
                        projectile.Launcher?.GetRootInventoryOwner() as Character;
                    if (user == null)
                    {
#if DEBUG
                        DebugConsole.LogError($"Projectile \"{projectile.Item.Prefab.Identifier}\" missing user");
#endif
                        return;
                    }

                    var reachCursorElement = new DelayedListElement(this, entity, localTargets, Vector2.Distance(entity.WorldPosition, user.CursorWorldPosition), worldPosition, entity.WorldPosition);
                    DelayListDict.TryAdd(reachCursorElement.Id, reachCursorElement);
                    break;
            }
        }

        public static void Update(float deltaTime)
        {
            // Thread-safe iteration over ConcurrentDictionary
            foreach (var kvp in DelayListDict)
            {
                DelayedListElement element = kvp.Value;
                if (element.Parent.CheckConditionalAlways && !element.Parent.HasRequiredConditions(element.Targets))
                {
                    DelayListDict.TryRemove(element.Id, out _);
                    continue;
                }

                switch (element.Parent.delayType)
                {
                    case DelayTypes.Timer:
                        element.Delay -= deltaTime;
                        if (element.Delay > 0.0f) 
                        { 
                            //if the delayed effect is supposed to get the position from the targets,
                            //keep refreshing the position until the effect runs (so e.g. a delayed effect runs at the last known position of a monster before it despawned)
                            if (element.GetPositionBasedOnTargets && element.Entity is { Removed: false })
                            {
                                element.WorldPosition = element.Parent.GetPosition(element.Entity, element.Parent.CurrentTargets);
                            }
                            continue; 
                        }
                        break;
                    case DelayTypes.ReachCursor:
                        if (Vector2.Distance(element.Entity.WorldPosition, element.StartPosition.Value) < element.Delay) { continue; }
                        break;
                }

                element.Parent.Apply(deltaTime, element.Entity, element.Targets, element.WorldPosition);
                DelayListDict.TryRemove(element.Id, out _);
            }
        }
    }
}

using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        private PhysicsBody trigger;

        private Holdable holdable;

        private float deattachTimer;
        
        /// <summary>
        /// Flag to prevent multiple queued creation requests.
        /// Uses volatile to ensure visibility across threads.
        /// </summary>
        private volatile bool triggerBodyCreationQueued;

        [Serialize(1.0f, IsPropertySaveable.No, description: "How long it takes to deattach the item from the level walls (in seconds).")]
        public float DeattachDuration
        {
            get;
            set;
        }
        
        [Serialize(0.0f, IsPropertySaveable.No, description: "How far along the item is to being deattached. When the timer goes above DeattachDuration, the item is deattached.")]
        public float DeattachTimer
        {
            get { return deattachTimer; }
            set
            {
                //clients don't deattach the item until the server says so (handled in ClientRead)
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                {
                    return;
                }

                if (holdable == null) { return; }

                deattachTimer = Math.Max(0.0f, value);
#if SERVER
                if (deattachTimer >= DeattachDuration)
                {
                    if (holdable.Attached) { item.CreateServerEvent(this); }
                    holdable.DeattachFromWall();
                }
                else if (Math.Abs(lastSentDeattachTimer - deattachTimer) > 0.1f)
                {
                    item.CreateServerEvent(this);
                    lastSentDeattachTimer = deattachTimer;
                }
#else
                if (deattachTimer >= DeattachDuration)
                {
                    if (holdable.Attached)
                    {
                        //we don't need info of every collected resource, we can get a good sample size just by logging a small sample
                        if (GameAnalyticsManager.ShouldLogRandomSample())
                        {
                            GameAnalyticsManager.AddDesignEvent("ResourceCollected:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "none") + ":" + item.Prefab.Identifier);
                        }
                        holdable.DeattachFromWall();
                    }
                    trigger.Enabled = false;
                }
#endif
            }
        }

        [Serialize(1.0f, IsPropertySaveable.No, description: "How much the position of the item can vary from the wall the item spawns on.")]
        public float RandomOffsetFromWall
        {
            get;
            set;
        }

        public bool Attached
        {
            get { return holdable != null && holdable.Attached; }
        }
                
        public LevelResource(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (trigger != null && amount.LengthSquared() > 0.00001f)
            {
                // Defer physics operation if in parallel context (Farseer is not thread-safe)
                var capturedTrigger = trigger;
                var capturedPos = item.SimPosition;
                if (ignoreContacts)
                {
                    PhysicsBodyQueue.ExecuteOrDefer(() => capturedTrigger.SetTransformIgnoreContacts(capturedPos, 0.0f));
                }
                else
                {
                    PhysicsBodyQueue.ExecuteOrDefer(() => capturedTrigger.SetTransform(capturedPos, 0.0f));
                }
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (holdable != null && !holdable.Attached)
            {
                if (trigger != null)
                {
                    trigger.Enabled = false;
                }
                IsActive = false;
            }
            else
            {
                if (trigger == null && !triggerBodyCreationQueued) 
                { 
                    // Queue the physics body creation to be processed on the main thread.
                    // This is necessary because physics body creation is not thread-safe
                    // and Update() may be called from a parallel loop.
                    triggerBodyCreationQueued = true;
                    PhysicsBodyQueue.EnqueueCreation(() =>
                    {
                        // Double-check that trigger hasn't been created yet
                        // (in case this was called multiple times before queue processing)
                        if (trigger == null && !item.Removed)
                        {
                            CreateTriggerBody();
                        }
                        triggerBodyCreationQueued = false;
                    });
                }
                if (trigger != null && Vector2.DistanceSquared(item.SimPosition, trigger.SimPosition) > 0.01f)
                {
                    // Defer physics operation if in parallel context (Farseer is not thread-safe)
                    var capturedTrigger = trigger;
                    var capturedPos = item.SimPosition;
                    PhysicsBodyQueue.ExecuteOrDefer(() => capturedTrigger.SetTransform(capturedPos, 0.0f));
                }
                IsActive = false;
            }
        }

        public override void OnItemLoaded()
        {
            holdable = item.GetComponent<Holdable>();
            if (holdable == null)
            {
                IsActive = false;
                return;
            }
            holdable.Reattachable = false;
            if (RequiredItems.Any())
            {
                holdable.PickingTime = float.MaxValue;
            }
        }

        private void CreateTriggerBody()
        {
            System.Diagnostics.Debug.Assert(trigger == null, "LevelResource trigger already created!");
            var body = item.body ?? holdable?.Body;
            if (body != null && Attached)
            {
                trigger = new PhysicsBody(body.Width, body.Height, body.Radius,
                    body.Density,
                    BodyType.Static,
                    Physics.CollisionWall,
                    Physics.CollisionNone,
                    findNewContacts: false)
                {
                    UserData = item
                };
                trigger.FarseerBody.SetIsSensor(true);
            }
        }

        protected override void RemoveComponentSpecific()
        {
            if (trigger != null)
            {
                trigger.Remove();
                trigger = null;
            }
        }
    }
}

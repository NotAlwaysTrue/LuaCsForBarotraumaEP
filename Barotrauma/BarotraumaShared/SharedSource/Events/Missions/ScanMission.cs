using System;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.RuinGeneration;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class ScanMission : Mission
    {
        private readonly ContentXElement itemConfig;
        private readonly List<Item> startingItems = new List<Item>();
        private readonly List<Scanner> scanners = new List<Scanner>();
        private readonly Dictionary<Item, ushort> parentInventoryIDs = new Dictionary<Item, ushort>();
        private readonly Dictionary<Item, int> inventorySlotIndices = new Dictionary<Item, int>();
        private readonly Dictionary<Item, byte> parentItemContainerIndices = new Dictionary<Item, byte>();
        private readonly int totalTargetsToScan;
        private readonly Dictionary<WayPoint, bool> scanTargets = new Dictionary<WayPoint, bool>();
        private readonly HashSet<WayPoint> newTargetsScanned = new HashSet<WayPoint>();
        private readonly float minTargetDistance;
        
        private Ruin TargetRuin { get; set; }

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (AllTargetsScanned())
                {
                    return Enumerable.Empty<(LocalizedString Label, Vector2 Position)>();
                }
                else
                {
                    return scanTargets
                        .Where(kvp => !kvp.Value)
                        .Select(kvp => (Prefab.SonarLabel, kvp.Key.WorldPosition));
                }             
            }
        }

        public ScanMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            itemConfig = prefab.ConfigElement.GetChildElement("Items");
            totalTargetsToScan = prefab.ConfigElement.GetAttributeInt("targets", 1);
            minTargetDistance = prefab.ConfigElement.GetAttributeFloat("mintargetdistance", 0.0f);
        }

        protected override void StartMissionSpecific(Level level)
        {
            Reset();

            if (IsClient) { return; }

            if (itemConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize a Scan mission: item config is not set",
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            foreach (var element in itemConfig.Elements())
            {
                LoadItem(element, null);
            }
            GetScanners();

            TargetRuin = Level.Loaded?.Ruins?.GetRandom(randSync: Rand.RandSync.ServerAndClient);
            if (TargetRuin == null)
            {
                DebugConsole.ThrowError("Failed to initialize a Scan mission: level contains no alien ruins",
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            var ruinWaypoints = TargetRuin.Submarine.GetWaypoints(false);
            ruinWaypoints.RemoveAll(wp => wp.CurrentHull == null);
            if (ruinWaypoints.Count < totalTargetsToScan)
            {
                DebugConsole.ThrowError($"Failed to initialize a Scan mission: target ruin has less waypoints than required as scan targets ({ruinWaypoints.Count} < {totalTargetsToScan})",
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            //the distance we'll use if we otherwise fail to place the targets far enough from each other
            //(smallest extent should be large enough to fit the targets and one extra to be safe)
            float guaranteedDistance = Math.Min(TargetRuin.Area.Width, TargetRuin.Area.Height) / (totalTargetsToScan + 1);

            var availableWaypoints = new List<WayPoint>();
            const int MaxTries = 15;
            for (int tries = 0; tries < MaxTries; tries++)
            {
                float triesNormalized = tries / (float)(MaxTries - 1);  // 0.0 -> 1.0
                float desperationFactor = MathF.Pow(triesNormalized, 2);
                //try placing the targets the desired minimum distance apart, gradually lowering the distance requirement on each try
                float currentMinDistance = MathHelper.Lerp(minTargetDistance, guaranteedDistance, desperationFactor);
                float currentMinDistanceSquared = currentMinDistance * currentMinDistance;

                scanTargets.Clear();
                availableWaypoints.Clear();
                availableWaypoints.AddRange(ruinWaypoints);
                for (int i = 0; i < totalTargetsToScan; i++)
                {
                    var selectedWaypoint = availableWaypoints.GetRandom(randSync: Rand.RandSync.ServerAndClient);
                    scanTargets.Add(selectedWaypoint, false);
                    availableWaypoints.Remove(selectedWaypoint);
                    if (i < (totalTargetsToScan - 1))
                    {
                        availableWaypoints.RemoveAll(wp => wp.CurrentHull == selectedWaypoint.CurrentHull);
                        availableWaypoints.RemoveAll(wp => Vector2.DistanceSquared(wp.WorldPosition, selectedWaypoint.WorldPosition) < currentMinDistanceSquared);
                        if (availableWaypoints.None())
                        {
#if DEBUG
                            DebugConsole.ThrowError($"Error initializing a Scan mission: not enough targets available on try #{tries + 1} to reach the required scan target count (current targets: {scanTargets.Count}, required targets: {totalTargetsToScan})",
                                contentPackage: Prefab.ContentPackage);
#endif
                            break;
                        }
                    }
                }
                if (scanTargets.Count >= totalTargetsToScan)
                {
#if DEBUG
                    DebugConsole.NewMessage($"Successfully initialized a Scan mission: targets set on try #{tries + 1}", Color.Green);
#endif
                    break;
                }
            }
            if (scanTargets.Count < totalTargetsToScan)
            {
                DebugConsole.ThrowError($"Error initializing a Scan mission: not enough targets (current targets: {scanTargets.Count}, required targets: {totalTargetsToScan})", 
                    contentPackage: Prefab.ContentPackage);
            }
        }

        private void Reset()
        {
            startingItems.Clear();
            parentInventoryIDs.Clear();
            inventorySlotIndices.Clear();
            parentItemContainerIndices.Clear();
            scanners.Clear();
            TargetRuin = null;
            scanTargets.Clear();
        }

        private void LoadItem(XElement element, Item parent)
        {
            var itemPrefab = FindItemPrefab(element);
            Vector2? position = GetCargoSpawnPosition(itemPrefab, out Submarine cargoRoomSub);
            if (!position.HasValue) { return; }
            var item = new Item(itemPrefab, position.Value, cargoRoomSub);
            item.FindHull();
            startingItems.Add(item);
            if (parent?.GetComponent<ItemContainer>() is ItemContainer itemContainer)
            {
                parentInventoryIDs.Add(item, parent.ID);
                parentItemContainerIndices.Add(item, (byte)parent.GetComponentIndex(itemContainer));
                parent.Combine(item, user: null);
                inventorySlotIndices.Add(item, item.ParentInventory?.FindIndex(item) ?? -1);
            }
            foreach (XElement subElement in element.Elements())
            {
                int amount = subElement.GetAttributeInt("amount", 1);
                for (int i = 0; i < amount; i++)
                {
                    LoadItem(subElement, item);
                }
            }
        }

        protected override void MissionStateChanged(int previousState)
        {
            // detect successful scanned targets increasing after scan is completed
            if (previousState < State)
            {
#if CLIENT
                SteamTimelineManager.OnScanSuccessful(this);
#endif
            }
        }

        private void GetScanners()
        {
            foreach (var startingItem in startingItems)
            {
                if (startingItem.GetComponent<Scanner>() is Scanner scanner)
                {
                    scanner.OnScanStarted += OnScanStarted;
                    if (!IsClient)
                    {
                        scanner.OnScanCompleted += OnScanCompleted;
                    }
                    scanners.Add(scanner);
                }
            }
        }

        private void OnScanStarted(Scanner scanner)
        {
            float scanRadiusSquared = scanner.ScanRadius * scanner.ScanRadius;
            foreach (var kvp in scanTargets)
            {
                if (!IsValidScanPosition(scanner, kvp, scanRadiusSquared)) { continue; }
                scanner.DisplayProgressBar = true;
                break;
            }
        }

        private void OnScanCompleted(Scanner scanner)
        {
            if (IsClient) { return; }
            newTargetsScanned.Clear();
            float scanRadiusSquared = scanner.ScanRadius * scanner.ScanRadius;
            foreach (var kvp in scanTargets)
            {
                if (!IsValidScanPosition(scanner, kvp, scanRadiusSquared)) { continue; }
                newTargetsScanned.Add(kvp.Key);
            }
            foreach (var wp in newTargetsScanned)
            {
                scanTargets[wp] = true;
            }
#if SERVER
            // Server should make sure that the clients' scan target status is in-sync
            GameMain.Server?.UpdateMissionState(this);
#endif
        }

        private static bool IsValidScanPosition(Scanner scanner, KeyValuePair<WayPoint, bool> scanStatus, float scanRadiusSquared)
        {
            if (scanStatus.Value) { return false; }
            if (scanStatus.Key.Submarine != scanner.Item.Submarine) { return false; }
            if (Vector2.DistanceSquared(scanStatus.Key.WorldPosition, scanner.Item.WorldPosition) > scanRadiusSquared) { return false; }
            return true;
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (IsClient) { return; }
            // Allow the state to be set higher with MissionStateAction, but not lower.
            State = Math.Max(State, scanTargets.Count(kvp => kvp.Value));
        }
        
        private bool AllTargetsScanned() => State >= totalTargetsToScan;
        
        protected override bool DetermineCompleted(CampaignMode.TransitionType transitionType) => AllTargetsScanned();

        protected override void EndMissionSpecific(bool completed)
        {
            foreach (var scanner in scanners)
            {
                if (scanner.Item is { Removed: false })
                {
                    scanner.OnScanStarted -= OnScanStarted;
                    scanner.OnScanCompleted -= OnScanCompleted;
                    scanner.Item.Remove();
                }
            }
            Reset();
            failed = !completed;
        }
    }
}
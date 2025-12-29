using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        /// <summary>
        /// Thread-safe queue for received entity spawn/remove events from the server.
        /// </summary>
        private readonly ConcurrentQueue<(Entity entity, bool isRemoval)> receivedEventsQueue = new ConcurrentQueue<(Entity entity, bool isRemoval)>();

        /// <summary>
        /// Gets a thread-safe snapshot of received events.
        /// </summary>
        public IEnumerable<(Entity entity, bool isRemoval)> GetReceivedEventsSnapshot()
        {
            return receivedEventsQueue.ToArray();
        }

        /// <summary>
        /// Clears all received events from the queue.
        /// </summary>
        partial void ResetReceivedEvents()
        {
            while (receivedEventsQueue.TryDequeue(out _)) { }
        }

        public void ClientEventRead(IReadMessage message, float sendingTime)
        {
            bool remove = message.ReadBoolean();

            if (remove)
            {
                ushort entityId = message.ReadUInt16();
                var entity = FindEntityByID(entityId);
                if (entity != null)
                {
                    DebugConsole.Log($"Received entity removal message for \"{entity}\".");
                    if (entity is Item item && item.Container?.GetComponent<Deconstructor>() != null)
                    {
                        if (item.Prefab.ContentPackage == ContentPackageManager.VanillaCorePackage &&
                            /* we don't need info of every deconstructed item, we can get a good sample size just by logging 5% */
                            Rand.Range(0.0f, 1.0f) < 0.05f)
                        {
                            GameAnalyticsManager.AddDesignEvent("ItemDeconstructed:" + (GameMain.GameSession?.GameMode?.Preset.Identifier ?? "none".ToIdentifier()) + ":" + item.Prefab.Identifier);
                        }
                    }
                    entity.Remove();
                }
                else
                {
                    DebugConsole.Log("Received entity removal message for ID " + entityId + ". Entity with a matching ID not found.");
                }
                receivedEventsQueue.Enqueue((entity, true));
            }
            else
            {
                switch (message.ReadByte())
                {
                    case (byte)SpawnableType.Item:
                        var newItem = Item.ReadSpawnData(message, true);
                        if (newItem == null)
                        {
                            DebugConsole.ThrowError("Received an item spawn message, but spawning the item failed.");
                        }
                        else
                        {
                            if (newItem.Container?.GetComponent<Fabricator>() != null)
                            {
                                if (newItem.Prefab.ContentPackage == ContentPackageManager.VanillaCorePackage &&
                                    /* we don't need info of every fabricated item, we can get a good sample size just by logging 5% */
                                    Rand.Range(0.0f, 1.0f) < 0.05f)
                                {
                                    GameAnalyticsManager.AddDesignEvent("ItemFabricated:" + (GameMain.GameSession?.GameMode?.Preset.Identifier ?? "none".ToIdentifier()) + ":" + newItem.Prefab.Identifier);
                                }
                            }
                            receivedEventsQueue.Enqueue((newItem, false));
                        }
                        break;
                    case (byte)SpawnableType.Character:
                        var character = Character.ReadSpawnData(message);
                        if (character == null)
                        {
                            DebugConsole.ThrowError("Received character spawn message, but spawning the character failed.");
                        }
                        else
                        {
                            receivedEventsQueue.Enqueue((character, false));
                        }
                        break;
                    default:
                        DebugConsole.ThrowError("Received invalid entity spawn message (unknown spawnable type)");
                        break;
                }
            }
        }
    }
}

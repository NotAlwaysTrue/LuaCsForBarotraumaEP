#nullable enable
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

partial class HighlightAction : EventAction
{
    partial void SetHighlightProjSpecific(Entity entity, IEnumerable<Character>? targetCharacters)
    {
        if (entity is Item item && GameMain.Server != null)
        {
            IEnumerable<Client>? targetClients = null;
            if (targetCharacters != null)
            {
                // Create snapshot to avoid concurrent access issues during parallel updates
                var clients = GameMain.Server.ConnectedClients.ToArray();
                targetClients = targetCharacters
                    .Select(c => clients.FirstOrDefault(client => client.Character == c))
                    .Where(c => c != null)!;
            }
            GameMain.Server?.CreateEntityEvent(item, new Item.SetHighlightEventData(State, highlightColor, targetClients));
        }
    }
}
using System.Linq;

namespace Barotrauma
{
    partial class EventObjectiveAction : EventAction
    {
        partial void UpdateProjSpecific()
        {
            if (GameMain.Server == null) { return; }
            EventManager.NetEventObjective objective = new EventManager.NetEventObjective(
                Type, 
                Identifier, 
                ObjectiveTag, 
                TextTag,
                ParentObjectiveId,
                CanBeCompleted);

            // Create snapshot to avoid concurrent access issues during parallel updates
            var clients = GameMain.Server.ConnectedClients.ToArray();

            if (TargetTag.IsEmpty)
            {
                foreach (var client in clients)
                {
                    if (client.Character == null) { continue; }
                    EventManager.ServerWriteObjective(client, objective);
                }
            }
            else
            {
                foreach (var target in ParentEvent.GetTargets(TargetTag))
                {
                    if (target is not Character character) { continue; }
                    var ownerClient = clients.FirstOrDefault(c => c.Character == character);
                    if (ownerClient == null) { continue; }
                    EventManager.ServerWriteObjective(ownerClient, objective);
                }
            }
        }
    }
}

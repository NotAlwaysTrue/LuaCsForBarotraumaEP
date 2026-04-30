using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma.LuaCs.Compatibility;

internal interface ILuaCsNetworking : ILuaCsShim
{
    void CreateEntityEvent(INetSerializable entity, NetEntityEvent.IData extraData);
    ushort LastClientListUpdateID { get; set; }
    void HttpRequest(string url, LuaCsAction callback, string data = null, string method = "POST", string contentType = "application/json", Dictionary<string, string> headers = null, string savePath = null);
    void HttpPost(string url, LuaCsAction callback, string data, string contentType = "application/json", Dictionary<string, string> headers = null, string savePath = null);
    void HttpGet(string url, LuaCsAction callback, Dictionary<string, string> headers = null, string savePath = null);
    void RequestGetHTTP(string url, LuaCsAction callback, Dictionary<string, string> headers = null, string savePath = null);
    void RequestPostHTTP(string url, LuaCsAction callback, string data, string contentType = "application/json", Dictionary<string, string> headers = null, string savePath = null);

    void Receive(string netId, LuaCsAction action);
#if SERVER
    int FileSenderMaxPacketsPerUpdate { get; set; }
    void ClientWriteLobby(Client client);
    void UpdateClientPermissions(Client client);
    IWriteMessage Start();
    void Send(IWriteMessage mesage, NetworkConnection connection = null, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable);
#elif CLIENT
    void Send(IWriteMessage mesage, DeliveryMethod deliveryMethod = DeliveryMethod.Reliable);
#endif
}

using Barotrauma.LuaCs;
using Barotrauma.LuaCs.Compatibility;
using Barotrauma.LuaCs.Events;
using Barotrauma.Networking;
using FluentResults;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Barotrauma.LuaCs.Data;

namespace Barotrauma.LuaCs;

internal partial class NetworkingService : INetworkingService, IEventSettingInstanceLifetime
{
    public readonly record struct NetId
    {
        private readonly string _value;

        public NetId(string netId)
        {
            _value = netId;
        }

        public static void Write(IWriteMessage message, NetId netId)
        {
            message.WriteString(netId._value);
        }

        public static NetId Read(IReadMessage message)
        {
            return new NetId(message.ReadString());
        }
    }

    private enum ClientToServer
    {
        NetMessageInternalId,
        NetMessageNetId,
        RequestSingleNetId,
        RequestSync,
    }

    private enum ServerToClient
    {
        NetMessageInternalId,
        NetMessageNetId,
        ReceiveNetIds
    }

    private ClientPacketHeader? clientHeader = null;
    public ClientPacketHeader ClientHeader
    {
        get
        {
            if (clientHeader == null)
            {
                byte lastHeader = (byte)Enum.GetValues(typeof(ClientPacketHeader)).Cast<ClientPacketHeader>().Last();
                clientHeader = (ClientPacketHeader)(lastHeader + 1);
            }

            return (ClientPacketHeader)clientHeader;
        }
    }

    private ServerPacketHeader? serverHeader = null;
    public ServerPacketHeader ServerHeader
    {
        get
        {
            if (serverHeader == null)
            {
                byte lastHeader = (byte)Enum.GetValues(typeof(ServerPacketHeader)).Cast<ServerPacketHeader>().Last();
                serverHeader = (ServerPacketHeader)(lastHeader + 1);
            }

            return (ServerPacketHeader)serverHeader;
        }
    }


    private ConcurrentDictionary<INetworkSyncVar, NetId> netVars = [];

    private ConcurrentDictionary<NetId, NetMessageReceived> netReceives = [];
    private ConcurrentDictionary<ushort, NetId> packetToId = [];
    private ConcurrentDictionary<NetId, ushort> idToPacket = [];

    public bool IsActive
    {
        get
        {
            return GameMain.NetworkMember != null;
        }
    }

    public bool IsSynchronized { get; private set; }
    public bool IsDisposed { get; private set; }

    private readonly IEventService _eventService;
    private readonly ILoggerService _loggerService;
    private readonly INetworkIdProvider _networkIdProvider;

    public NetworkingService(IEventService eventService, INetworkIdProvider networkIdProvider, ILoggerService loggerService)
    {
        _eventService = eventService;
        _networkIdProvider = networkIdProvider;
        _loggerService = loggerService;

#if SERVER
        IsSynchronized = true;
#endif
        SubscribeToEvents();
    }

    public void Receive(string netIdString, LuaCsAction callback)
    {
#if SERVER
        Receive(new NetId(netIdString), (IReadMessage message, Client client) => callback(message, client));
#elif CLIENT
        Receive(new NetId(netIdString), (IReadMessage message) => callback(message, null));
#endif
    }

    public void Receive(string netIdString, NetMessageReceived callback) => Receive(new NetId(netIdString), callback);
    public void Receive(Guid netIdGuid, NetMessageReceived callback) => Receive(new NetId(netIdGuid.ToString()), callback);
    public IWriteMessage Start(string netIdString)
    {
        if (netIdString == null)
        {
            // idk why but Lua calls this method with null instead of the Start method with no arguments
            return new WriteOnlyMessage();
        }

        return Start(new NetId(netIdString));
    }
    public IWriteMessage Start(Guid netIdGuid) => Start(new NetId(netIdGuid.ToString()));
    public IWriteMessage Start() => new WriteOnlyMessage();

    internal void Receive(NetId netId, NetMessageReceived callback)
    {
#if SERVER
        RegisterId(netId);
#elif CLIENT
        RequestId(netId);
#endif
        netReceives[netId] = callback;
    }

    private void HandleNetMessage(IReadMessage netMessage, NetId netId, Client client = null)
    {
        if (netReceives.ContainsKey(netId))
        {
            try
            {
#if CLIENT
                netReceives[netId](netMessage);
#elif SERVER
                netReceives[netId](netMessage, client);
#endif
            }
            catch (Exception e)
            {
                _loggerService.LogResults(new ExceptionalError("Exception thrown inside NetMessageReceive({netId})", e));
            }
        }
        else
        {
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
#if SERVER
                _loggerService.LogError($"Received NetMessage for unknown netid {netId} from {GameServer.ClientLogName(client)}.");
#else
                _loggerService.LogError($"Received NetMessage for unknown netid {netId} from server.");
#endif
            }
        }
    }

    private void HandleNetMessageString(IReadMessage netMessage, Client client = null)
    {
        NetId netId = NetId.Read(netMessage);

        HandleNetMessage(netMessage, netId, client);
    }

    private void SubscribeToEvents()
    {
        _eventService.Subscribe<IEventSettingInstanceLifetime>(this);
#if CLIENT
        _eventService.Subscribe<IEventServerConnected>(this);
        _eventService.Subscribe<IEventServerRawNetMessageReceived>(this);
#elif SERVER
        _eventService.Subscribe<IEventClientRawNetMessageReceived>(this);
#endif
    }

    public Guid GetNetworkIdForInstance(INetworkSyncVar var)
    {
        return _networkIdProvider.GetNetworkIdForInstance(var);
    }

    public void RegisterNetVar(INetworkSyncVar netVar)
    {
        netVar.SetNetworkOwner(this);

        NetId netId = new NetId(netVar.InstanceId.ToString());
        netVars[netVar] = netId;

#if CLIENT
        Receive(netId, (IReadMessage message) =>
        {
            if (netVar.SyncType == NetSync.None)
            {
                _loggerService.LogWarning($"Received net var from server but {nameof(NetSync)} is {netVar.SyncType.ToString()}");
                return;
            }

            netVar.ReadNetMessage(message);
        });
#elif SERVER
        Receive(netId, (IReadMessage message, Client client) =>
        {
            if (netVar.SyncType == NetSync.None || netVar.SyncType == NetSync.ServerAuthority)
            {
                _loggerService.LogWarning($"Received net var from {GameServer.ClientLogName(client)} but {nameof(NetSync)} is {netVar.SyncType.ToString()}");
                return;
            }

            if (!client.HasPermission(netVar.WritePermissions))
            {
                _loggerService.LogWarning($"Received net var from {GameServer.ClientLogName(client)} but the client lacks permissions to modify it");
                return;
            }

            netVar.ReadNetMessage(message);

            // Sync back to all clients
            if (netVar.SyncType != NetSync.ClientOneWay)
            {
                SendNetVar(netVar);
            }
        });
#endif
    }

    public void DeregisterNetVar(INetworkSyncVar netVar)
    {
        if (netVar is null)
        {
            return;
        }

        netVar.SetNetworkOwner(null);
        netVars.TryRemove(netVar, out _);
    }

    public void SendNetVar(INetworkSyncVar netVar) => SendNetVar(netVar, null);

    public void SendNetVar(INetworkSyncVar netVar, NetworkConnection connection = null)
    {
        if (!netVars.TryGetValue(netVar, out NetId netId))
        {
            throw new InvalidOperationException("Tried to send net var across network without registering first");
        }

        if (netVar.SyncType == NetSync.None) { return; }
#if CLIENT
        if (netVar.SyncType == NetSync.ServerAuthority) { return; }
#elif SERVER
        if (netVar.SyncType == NetSync.ClientOneWay) { return; }
#endif

        IWriteMessage message = Start(netId);
        netVar.WriteNetMessage(message);
#if CLIENT
        SendToServer(message);
#elif SERVER
        SendToClient(message, connection);
#endif
    }

    public FluentResults.Result Reset()
    {
        IsSynchronized = false;
        netReceives = new ConcurrentDictionary<NetId, NetMessageReceived>();
        packetToId = new ConcurrentDictionary<ushort, NetId>();
        idToPacket = new ConcurrentDictionary<NetId, ushort>();
        netVars = new ConcurrentDictionary<INetworkSyncVar, NetId>();

        SubscribeToEvents();
        return FluentResults.Result.Ok();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }

    #region Compatiblity

    private static readonly HttpClient client = new HttpClient();

    public async void HttpRequest(string url, LuaCsAction callback, string data = null, string method = "POST", string contentType = "application/json", Dictionary<string, string> headers = null, string savePath = null)
    {
        try
        {
            HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), url);

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            if (data != null)
            {
                request.Content = new StringContent(data, Encoding.UTF8, contentType);
            }

            HttpResponseMessage response = await client.SendAsync(request);

            if (savePath != null)
            {
                if (LuaCsFile.IsPathAllowedException(savePath))
                {
                    byte[] responseData = await response.Content.ReadAsByteArrayAsync();

                    using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    {
                        fileStream.Write(responseData, 0, responseData.Length);
                    }
                }
            }

            string responseBody = await response.Content.ReadAsStringAsync();

            CrossThread.RequestExecutionOnMainThread(() =>
            {
                callback(responseBody, (int)response.StatusCode, response.Headers);
            });
        }
        catch (HttpRequestException e)
        {
            CrossThread.RequestExecutionOnMainThread(() => { callback(e.Message, e.StatusCode, null); });
        }
        catch (Exception e)
        {
            CrossThread.RequestExecutionOnMainThread(() => { callback(e.Message, null, null); });
        }
    }

    public void HttpPost(string url, LuaCsAction callback, string data, string contentType = "application/json", Dictionary<string, string> headers = null, string savePath = null)
    {
        HttpRequest(url, callback, data, "POST", contentType, headers, savePath);
    }

    public void RequestPostHTTP(string url, LuaCsAction callback, string data, string contentType = "application/json", Dictionary<string, string> headers = null, string savePath = null)
    {
        HttpRequest(url, callback, data, "POST", contentType, headers, savePath);
    }

    public void HttpGet(string url, LuaCsAction callback, Dictionary<string, string> headers = null, string savePath = null)
    {
        HttpRequest(url, callback, null, "GET", null, headers, savePath);
    }

    public void RequestGetHTTP(string url, LuaCsAction callback, Dictionary<string, string> headers = null, string savePath = null)
    {
        HttpRequest(url, callback, null, "GET", null, headers, savePath);
    }

    public void CreateEntityEvent(INetSerializable entity, NetEntityEvent.IData extraData)
    {
        GameMain.NetworkMember.CreateEntityEvent(entity, extraData);
    }

    public ushort LastClientListUpdateID
    {
        get { return GameMain.NetworkMember.LastClientListUpdateID; }
        set { GameMain.NetworkMember.LastClientListUpdateID = value; }
    }

#if SERVER
    public void ClientWriteLobby(Client client) => GameMain.Server.ClientWriteLobby(client);

    public void UpdateClientPermissions(Client client)
    {
        GameMain.Server.UpdateClientPermissions(client);
    }

    public int FileSenderMaxPacketsPerUpdate
    {
        get { return FileSender.FileTransferOut.MaxPacketsPerUpdate; }
        set { FileSender.FileTransferOut.MaxPacketsPerUpdate = value; }
    }
#endif

    #endregion

    public void OnSettingInstanceCreated<T>(T configInstance) where T : ISettingBase
    {
        if (configInstance is INetworkSyncVar syncVar)
        {
            RegisterNetVar(syncVar);
        }
    }

    public void OnSettingInstanceDisposed<T>(T configInstance) where T : ISettingBase
    {
        if (configInstance is INetworkSyncVar syncVar)
        {
            DeregisterNetVar(syncVar);
        }
    }
}

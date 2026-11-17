using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net.Steam
{
    /// <summary>
    /// Netcode for GameObjects transport over Steam Networking Sockets (Facepunch.Steamworks).
    /// Traffic goes through Valve's relay network, so nobody has to port-forward and IPs stay hidden.
    ///
    /// Host: opens a relay socket. Client: connects to the host's SteamID (from the lobby owner).
    /// Steam callbacks fire inside Receive(), which we call from PollEvent on the main thread,
    /// and are queued so Netcode consumes them one at a time.
    /// </summary>
    public class SteamTransport : NetworkTransport
    {
        const ulong HostId = 0;
        const int ReceiveBatch = 64;

        /// <summary>SteamID of the host to connect to. Set before StartClient.</summary>
        public ulong TargetSteamId { get; set; }

        public override ulong ServerClientId => HostId;

        struct QueuedEvent
        {
            public NetworkEvent Type;
            public ulong ClientId;
            public byte[] Buffer;
            public int Length;
            public float Time;
        }

        readonly Queue<QueuedEvent> _events = new Queue<QueuedEvent>();
        readonly Dictionary<ulong, Connection> _serverConnections = new Dictionary<ulong, Connection>();

        SocketManager _socket;          // host side
        ConnectionManager _connection;  // client side
        ServerHandler _serverHandler;
        ClientHandler _clientHandler;

        // --------------------------- NetworkTransport ---------------------------

        public override void Initialize(NetworkManager networkManager = null) { }

        public override bool StartServer()
        {
            if (!SteamService.IsReady)
            {
                Debug.LogError("[SteamTransport] Steam is not initialized.");
                return false;
            }
            _serverHandler = new ServerHandler(this);
            _socket = SteamNetworkingSockets.CreateRelaySocket(0, _serverHandler);
            return _socket != null;
        }

        public override bool StartClient()
        {
            if (!SteamService.IsReady)
            {
                Debug.LogError("[SteamTransport] Steam is not initialized.");
                return false;
            }
            if (TargetSteamId == 0)
            {
                Debug.LogError("[SteamTransport] No host SteamID set.");
                return false;
            }
            _clientHandler = new ClientHandler(this);
            _connection = SteamNetworkingSockets.ConnectRelay(new SteamId { Value = TargetSteamId }, 0, _clientHandler);
            return _connection != null;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            var sendType = delivery switch
            {
                NetworkDelivery.Unreliable or NetworkDelivery.UnreliableSequenced => SendType.Unreliable | SendType.NoNagle,
                _ => SendType.Reliable | SendType.NoNagle,
            };

            Connection conn;
            if (_connection != null && clientId == HostId) conn = _connection.Connection;
            else if (!_serverConnections.TryGetValue(clientId, out conn)) return;

            var result = conn.SendMessage(payload.Array, payload.Offset, payload.Count, sendType, 0);
            if (result != Result.OK && result != Result.NoConnection)
                Debug.LogWarning($"[SteamTransport] Send to {clientId} failed: {result}");
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            if (_events.Count == 0)
            {
                _socket?.Receive(ReceiveBatch, true);
                _connection?.Receive(ReceiveBatch, true);
            }

            if (_events.Count == 0)
            {
                clientId = 0;
                payload = default;
                receiveTime = Time.realtimeSinceStartup;
                return NetworkEvent.Nothing;
            }

            var e = _events.Dequeue();
            clientId = e.ClientId;
            payload = e.Buffer != null ? new ArraySegment<byte>(e.Buffer, 0, e.Length) : default;
            receiveTime = e.Time;
            return e.Type;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (_serverConnections.TryGetValue(clientId, out var conn))
            {
                conn.Close(false, 0, "Kicked");
                _serverConnections.Remove(clientId);
            }
        }

        public override void DisconnectLocalClient()
        {
            _connection?.Close(false, 0, "Left");
            _connection = null;
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (_connection != null && clientId == HostId)
                return (ulong)Mathf.Max(0, _connection.Connection.QuickStatus().Ping);
            if (_serverConnections.TryGetValue(clientId, out var conn))
                return (ulong)Mathf.Max(0, conn.QuickStatus().Ping);
            return 0;
        }

        public override void Shutdown()
        {
            try
            {
                foreach (var conn in _serverConnections.Values) conn.Close(false, 0, "Host shut down");
                _serverConnections.Clear();
                _socket?.Close();
                _connection?.Close(false, 0, "Shutdown");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamTransport] Shutdown: {e.Message}");
            }
            finally
            {
                _socket = null;
                _connection = null;
                _events.Clear();
            }
        }

        // --------------------------- Event plumbing ---------------------------

        void Enqueue(NetworkEvent type, ulong clientId, IntPtr data = default, int size = 0)
        {
            byte[] buffer = null;
            if (size > 0)
            {
                // Netcode may hold the segment until the next PollEvent, so each message gets its own copy.
                buffer = new byte[size];
                Marshal.Copy(data, buffer, 0, size);
            }
            _events.Enqueue(new QueuedEvent
            {
                Type = type,
                ClientId = clientId,
                Buffer = buffer,
                Length = size,
                Time = Time.realtimeSinceStartup,
            });
        }

        static ulong IdOf(Connection c) => c.Id;

        sealed class ServerHandler : ISocketManager
        {
            readonly SteamTransport _t;
            public ServerHandler(SteamTransport t) => _t = t;

            public void OnConnecting(Connection connection, ConnectionInfo info) => connection.Accept();

            public void OnConnected(Connection connection, ConnectionInfo info)
            {
                ulong id = IdOf(connection);
                _t._serverConnections[id] = connection;
                _t.Enqueue(NetworkEvent.Connect, id);
            }

            public void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                ulong id = IdOf(connection);
                if (_t._serverConnections.Remove(id))
                    _t.Enqueue(NetworkEvent.Disconnect, id);
                connection.Close(false, 0, "Disconnected");
            }

            public void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size,
                                  long messageNum, long recvTime, int channel)
                => _t.Enqueue(NetworkEvent.Data, IdOf(connection), data, size);
        }

        sealed class ClientHandler : IConnectionManager
        {
            readonly SteamTransport _t;
            public ClientHandler(SteamTransport t) => _t = t;

            public void OnConnecting(ConnectionInfo info) { }

            public void OnConnected(ConnectionInfo info) => _t.Enqueue(NetworkEvent.Connect, HostId);

            public void OnDisconnected(ConnectionInfo info)
            {
                Debug.Log($"[SteamTransport] Disconnected from host: {info.EndReason}");
                _t.Enqueue(NetworkEvent.Disconnect, HostId);
            }

            public void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
                => _t.Enqueue(NetworkEvent.Data, HostId, data, size);
        }
    }
}

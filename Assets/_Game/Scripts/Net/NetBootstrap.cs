using System;
using FriendSlop.Net.Steam;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>Which transport a session runs on.</summary>
    public enum NetBackend
    {
        /// <summary>Unity Transport over IP: localhost, LAN, or Tailscale.</summary>
        Direct,
        /// <summary>Steam relay via the test AppID 480. Added later in M0.</summary>
        Steam,
    }

    /// <summary>
    /// Single entry point for starting and stopping sessions.
    /// Picks the transport, handles connection approval (max players, spawn points),
    /// and reports a human-readable status for the menu.
    /// Lives on the same GameObject as the NetworkManager.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetBootstrap : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        public const ushort DefaultPort = 7777;

        [SerializeField] UnityTransport directTransport;
        [SerializeField] SteamTransport steamTransport;
        [SerializeField] Transform[] spawnPoints = Array.Empty<Transform>();
        [SerializeField] float spawnRingRadius = 3f;

        public static NetBootstrap Instance { get; private set; }
        public NetBackend Backend { get; private set; }
        public string Status { get; private set; } = "Offline";

        NetworkManager _nm;

        public NetworkManager Manager => _nm;
        public bool IsRunning => _nm != null && _nm.IsListening;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _nm = GetComponent<NetworkManager>();
            if (directTransport == null) directTransport = GetComponent<UnityTransport>();
            if (steamTransport == null) steamTransport = GetComponent<SteamTransport>();
        }

        void Start()
        {
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.ConnectionApprovalCallback = ApproveConnection;
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;
            ApplyCommandLine();
        }

        /// <summary>
        /// Dev/test flags for builds: -fsh-host · -fsh-join &lt;ip&gt; · -fsh-quit-after &lt;seconds&gt;.
        /// (-nosteam is read by SteamService.)
        /// </summary>
        void ApplyCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-fsh-host":
                        HostDirect();
                        break;
                    case "-fsh-join" when i + 1 < args.Length:
                        JoinDirect(args[i + 1]);
                        break;
                    case "-fsh-quit-after" when i + 1 < args.Length && float.TryParse(args[i + 1], out float secs):
                        Invoke(nameof(QuitForTest), secs);
                        break;
                }
            }
        }

        void QuitForTest()
        {
            Debug.Log($"[Net] Test quit. Status: {Status}");
            Leave();
            Application.Quit();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_nm == null) return;
            if (_nm.ConnectionApprovalCallback == ApproveConnection) _nm.ConnectionApprovalCallback = null;
            _nm.OnClientConnectedCallback -= OnClientConnected;
            _nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // ---------- Direct (Unity Transport) ----------

        /// <summary>Host on this machine, listening on all interfaces so LAN/Tailscale friends can join.</summary>
        public bool HostDirect(ushort port = DefaultPort)
        {
            if (IsRunning) return false;
            UseTransport(directTransport, NetBackend.Direct);
            directTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            return Report(_nm.StartHost(), $"Hosting on port {port}", "Failed to start host (is the port in use?)");
        }

        /// <summary>Join a host by IP (127.0.0.1 for same PC, LAN IP, or Tailscale IP).</summary>
        public bool JoinDirect(string address, ushort port = DefaultPort)
        {
            if (IsRunning) return false;
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            UseTransport(directTransport, NetBackend.Direct);
            directTransport.SetConnectionData(address, port);
            return Report(_nm.StartClient(), $"Connecting to {address}:{port}…", "Failed to start client");
        }

        // ---------- Steam (AppID 480) ----------

        /// <summary>Start hosting over the Steam relay. The lobby is created by SteamLobby afterwards.</summary>
        public bool HostSteam()
        {
            if (IsRunning || steamTransport == null) return false;
            UseTransport(steamTransport, NetBackend.Steam);
            return Report(_nm.StartHost(), "Hosting on Steam…", "Failed to start Steam host");
        }

        /// <summary>Connect to a host by SteamID (the lobby owner).</summary>
        public bool JoinSteam(ulong hostSteamId)
        {
            if (IsRunning || steamTransport == null) return false;
            steamTransport.TargetSteamId = hostSteamId;
            UseTransport(steamTransport, NetBackend.Steam);
            return Report(_nm.StartClient(), "Connecting through Steam relay…", "Failed to start Steam client");
        }

        // ---------- Shared ----------

        public void Leave()
        {
            if (_nm == null || !_nm.IsListening) return;
            _nm.Shutdown();
            if (TryGetComponent(out SteamLobby lobby)) lobby.LeaveLobby();
            SetStatus("Offline");
        }

        /// <summary>Lets other net components (e.g. the Steam lobby) surface status in the menu.</summary>
        public void ReportStatus(string status) => SetStatus(status);

        void UseTransport(NetworkTransport transport, NetBackend backend)
        {
            _nm.NetworkConfig.NetworkTransport = transport;
            Backend = backend;
        }

        bool Report(bool ok, string okStatus, string failStatus)
        {
            SetStatus(ok ? okStatus : failStatus);
            return ok;
        }

        void SetStatus(string status)
        {
            Status = status;
            Debug.Log($"[Net] {status}");
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
                               NetworkManager.ConnectionApprovalResponse response)
        {
            int current = _nm.ConnectedClientsIds.Count;
            bool hasRoom = current < MaxPlayers;

            response.Approved = hasRoom;
            response.Reason = hasRoom ? string.Empty : $"Lobby is full ({MaxPlayers} players max)";
            response.CreatePlayerObject = hasRoom;
            response.Pending = false;

            if (hasRoom)
            {
                GetSpawn(current, out var pos, out var rot);
                response.Position = pos;
                response.Rotation = rot;
            }
        }

        void GetSpawn(int index, out Vector3 position, out Quaternion rotation)
        {
            if (spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[index % spawnPoints.Length] != null)
            {
                var sp = spawnPoints[index % spawnPoints.Length];
                position = sp.position;
                rotation = sp.rotation;
                return;
            }

            float angle = index * (360f / MaxPlayers);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnRingRadius;
            position = transform.position + offset + Vector3.up * 0.1f;
            rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up); // face the middle
        }

        void OnClientConnected(ulong clientId)
        {
            if (_nm.IsServer)
                SetStatus($"Hosting — {_nm.ConnectedClientsIds.Count}/{MaxPlayers} players");
            else if (clientId == _nm.LocalClientId)
                SetStatus("Connected");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (_nm.IsServer && clientId != NetworkManager.ServerClientId)
            {
                // ConnectedClientsIds still includes the leaving client during this callback.
                SetStatus($"Hosting — {Mathf.Max(1, _nm.ConnectedClientsIds.Count - 1)}/{MaxPlayers} players");
                return;
            }

            if (!_nm.IsServer)
            {
                string reason = string.IsNullOrEmpty(_nm.DisconnectReason) ? "Disconnected" : $"Disconnected: {_nm.DisconnectReason}";
                if (TryGetComponent(out SteamLobby lobby)) lobby.LeaveLobby();
                SetStatus(reason);
            }
        }
    }
}

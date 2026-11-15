using FriendSlop.Net.Steam;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Throwaway IMGUI menu for the M0 networking spike: host/join (Direct and Steam), lobby
    /// browser, status, and controls help. Replaced by real UI in a later milestone.
    /// </summary>
    [RequireComponent(typeof(NetBootstrap))]
    public class ConnectionMenu : MonoBehaviour
    {
        string _address = "127.0.0.1";
        Vector2 _lobbyScroll;
        NetBootstrap _net;
        SteamLobby _steam;
        GUIStyle _title;
        GUIStyle _wrap;

        void Awake()
        {
            _net = GetComponent<NetBootstrap>();
            _steam = GetComponent<SteamLobby>();
        }

        void OnGUI()
        {
            if (_net == null || _net.Manager == null) return;
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true };

            if (!_net.IsRunning) DrawLobbyMenu();
            else DrawSessionHud();
        }

        void DrawLobbyMenu()
        {
            GUILayout.BeginArea(new Rect(20, 20, 340, Screen.height - 40), GUI.skin.box);
            GUILayout.Label("FriendSlop Hunting — M0 net test", _title);
            GUILayout.Space(8);

            // ---- Steam ----
            GUILayout.Label("<b>Steam</b> (AppID 480)", new GUIStyle(GUI.skin.label) { richText = true });
            if (_steam == null)
            {
                GUILayout.Label("SteamLobby component missing.");
            }
            else if (!SteamService.IsReady)
            {
                GUILayout.Label(SteamService.Error, _wrap);
            }
            else
            {
                GUILayout.Label($"Signed in as {SteamService.PersonaName}");
                GUI.enabled = !_steam.IsBusy;
                if (GUILayout.Button("Host on Steam", GUILayout.Height(32))) _steam.Host();
                GUI.enabled = !_steam.IsSearching;
                if (GUILayout.Button(_steam.IsSearching ? "Searching…" : "Find hunts", GUILayout.Height(26))) _steam.Refresh();
                GUI.enabled = true;

                _lobbyScroll = GUILayout.BeginScrollView(_lobbyScroll, GUILayout.Height(110));
                if (_steam.Found.Count == 0) GUILayout.Label("No hunts found yet. Click Find hunts.");
                foreach (var entry in _steam.Found)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{entry.Name}  ({entry.Members}/{entry.MaxMembers})");
                    GUI.enabled = !_steam.IsBusy;
                    if (GUILayout.Button("Join", GUILayout.Width(60))) _steam.Join(entry.Lobby);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(12);

            // ---- Direct ----
            GUILayout.Label("<b>Direct</b> (same PC / LAN / Tailscale)", new GUIStyle(GUI.skin.label) { richText = true });
            if (GUILayout.Button("Host", GUILayout.Height(28))) _net.HostDirect();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host IP", GUILayout.Width(55));
            _address = GUILayout.TextField(_address);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Join", GUILayout.Height(28))) _net.JoinDirect(_address);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Status: {_net.Status}", _wrap);
            GUILayout.EndArea();
        }

        void DrawSessionHud()
        {
            var nm = _net.Manager;
            bool cursorFree = Cursor.lockState != CursorLockMode.Locked;

            GUILayout.BeginArea(new Rect(20, 20, 320, cursorFree ? 260 : 90), GUI.skin.box);
            string role = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
            GUILayout.Label($"{role} · {_net.Backend} · id {nm.LocalClientId}");
            GUILayout.Label(_net.Status, _wrap);
            if (!nm.IsServer && nm.IsConnectedClient)
            {
                var transport = nm.NetworkConfig.NetworkTransport;
                GUILayout.Label($"Ping: {transport.GetCurrentRtt(transport.ServerClientId)} ms");
            }

            if (cursorFree)
            {
                GUILayout.Space(6);
                GUILayout.Label("WASD move · Shift sprint · Space jump\nLeft click shoot · Right click aim\nE pick up / drop · Left click throw (carrying)\nEsc toggle mouse");
                if (nm.IsHost && _net.Backend == NetBackend.Steam && _steam != null && _steam.Current.HasValue)
                {
                    if (GUILayout.Button("Invite friends (Steam overlay)", GUILayout.Height(26))) _steam.OpenInviteOverlay();
                }
                if (GUILayout.Button("Leave", GUILayout.Height(28))) _net.Leave();
            }
            GUILayout.EndArea();

            if (!cursorFree)
            {
                var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
                GUI.Label(new Rect(c.x - 5, c.y - 12, 20, 24), "+");
            }
        }
    }
}

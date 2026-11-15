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

            if (FriendSlop.Game.OptionsMenu.OpenFromMainMenu) return; // options screen is on top
            if (!_net.IsRunning)
            {
                FriendSlop.Game.GameMenus.Reset();
                if (FriendSlop.Profile.CustomizeMenu.IsOpen) return; // customize screen is on top
                DrawLobbyMenu();
                DrawHunterCard();
            }
            else
            {
                FriendSlop.Profile.CustomizeMenu.IsOpen = false;
                DrawSessionHud();
            }
        }

        GUIStyle _big, _bucks;

        /// <summary>Home screen: your hunter (live preview), name, bucks, and a Customize button.</summary>
        void DrawHunterCard()
        {
            var p = FriendSlop.Profile.PlayerProfile.Current;
            var preview = FriendSlop.Profile.CharacterPreview.Instance;
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            _bucks ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };

            float w = 340f, h = 520f;
            var card = new Rect(Screen.width - w - 30, (Screen.height - h) / 2f, w, h);
            GUI.Box(card, GUIContent.none);
            GUI.Label(new Rect(card.x, card.y + 10, w, 32), p.EffectiveName, _big);
            GUI.Label(new Rect(card.x, card.y + 42, w, 26), $"Hunter level {p.Level}  ·  {p.bucks:N0} bucks", _bucks);
            if (preview != null)
                GUI.DrawTexture(new Rect(card.x + 30, card.y + 72, w - 60, (w - 60) * 1.25f), preview.Render(p.outfit, p.head, p.hat, p.accessory, p.colorIndex), ScaleMode.ScaleToFit);
            if (GUI.Button(new Rect(card.x + 30, card.yMax - 52, w - 60, 38), "Customize"))
                FriendSlop.Profile.CustomizeMenu.IsOpen = true;
        }

        void DrawLobbyMenu()
        {
            GUILayout.BeginArea(new Rect(20, 20, 360, Screen.height - 40), GUI.skin.box);
            GUILayout.Label("FRIENDSLOP HUNTING", new GUIStyle(_title) { fontSize = 28 });
            GUILayout.Label("Hunt with your friends. Juggle carcasses. Get wrecked by geese.", _wrap);
            GUILayout.Space(10);

            // ---- Lobby rules (apply to hunts you host) ----
            bool ff = GUILayout.Toggle(FriendSlop.Game.LobbySettings.FriendlyFire, " Friendly fire (when you host)");
            if (ff != FriendSlop.Game.LobbySettings.FriendlyFire) FriendSlop.Game.LobbySettings.FriendlyFire = ff;
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
                    GUILayout.Label($"{entry.Name}  ({entry.Members}/{entry.MaxMembers}){(entry.FriendlyFire ? "  · friendly fire" : "")}");
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

            GUILayout.Space(10);
            if (GUILayout.Button("Customize hunter", GUILayout.Height(28))) FriendSlop.Profile.CustomizeMenu.IsOpen = true;
            if (GUILayout.Button("Options", GUILayout.Height(28))) FriendSlop.Game.OptionsMenu.OpenFromMainMenu = true;
            if (GUILayout.Button("Quit", GUILayout.Height(28))) Application.Quit();

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
            if (FriendSlop.Game.GameState.FriendlyFireOn) GUILayout.Label("Friendly fire: ON");
            if (!nm.IsServer && nm.IsConnectedClient)
            {
                var transport = nm.NetworkConfig.NetworkTransport;
                GUILayout.Label($"Ping: {transport.GetCurrentRtt(transport.ServerClientId)} ms");
            }

            if (cursorFree)
            {
                GUILayout.Space(6);
                GUILayout.Label("WASD move · Shift sprint · Space jump\nLeft click shoot · Right click aim · 1-0/scroll switch\nE interact / pick up · Left click throw (carrying)\nTab inventory · G drop weapon · Esc pause / options");
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

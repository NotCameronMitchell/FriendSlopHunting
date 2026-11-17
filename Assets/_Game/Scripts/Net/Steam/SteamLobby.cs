using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace FriendSlop.Net.Steam
{
    /// <summary>
    /// Steam lobbies on AppID 480. The lobby is only for finding each other: its owner is the
    /// Netcode host, and joiners connect to the owner's SteamID through <see cref="SteamTransport"/>.
    ///
    /// AppID 480 is shared by every dev testing on Spacewar, so our lobbies carry a game key and
    /// the browser filters on it.
    /// </summary>
    [RequireComponent(typeof(NetBootstrap))]
    public class SteamLobby : MonoBehaviour
    {
        const string GameKey = "fsh_game";
        const string GameValue = "FriendSlopHunting";
        const string VersionKey = "fsh_ver";
        const string NameKey = "fsh_name";
        const string HostKey = "fsh_host"; // SteamID of the Netcode host (lobby ownership can migrate; this doesn't)

        /// <summary>Bump when the network protocol changes so old builds don't see new lobbies.</summary>
        public const string ProtocolVersion = "m3";

        public struct LobbyEntry
        {
            public Lobby Lobby;
            public string Name;
            public int Members;
            public int MaxMembers;
            public bool FriendlyFire;
        }

        public Lobby? Current { get; private set; }
        public IReadOnlyList<LobbyEntry> Found => _found;
        public bool IsSearching { get; private set; }
        public bool IsBusy { get; private set; }

        readonly List<LobbyEntry> _found = new List<LobbyEntry>();
        NetBootstrap _net;
        bool _subscribed;

        void Awake() => _net = GetComponent<NetBootstrap>();

        void Start()
        {
            if (!SteamService.IsReady) return;
            SteamFriends.OnGameLobbyJoinRequested += OnInviteAccepted;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (_subscribed) SteamFriends.OnGameLobbyJoinRequested -= OnInviteAccepted;
            LeaveLobby();
        }

        // --------------------------- Host ---------------------------

        public async void Host()
        {
            if (!SteamService.IsReady || IsBusy || _net.IsRunning) return;
            IsBusy = true;
            try
            {
                if (!_net.HostSteam()) return;

                var created = await SteamMatchmaking.CreateLobbyAsync(NetBootstrap.MaxPlayers);
                if (!created.HasValue)
                {
                    _net.ReportStatus("Hosting, but Steam lobby creation failed (friends can't find you)");
                    return;
                }

                var lobby = created.Value;
                lobby.SetPublic();
                lobby.SetJoinable(true);
                lobby.SetData(GameKey, GameValue);
                lobby.SetData(VersionKey, ProtocolVersion);
                lobby.SetData(NameKey, $"{SteamService.PersonaName}'s hunt");
                lobby.SetData(HostKey, SteamService.LocalSteamId.ToString());
                lobby.SetData("fsh_ff", FriendSlop.Game.LobbySettings.FriendlyFire ? "1" : "0");
                Current = lobby;
                _net.ReportStatus($"Hosting on Steam as {SteamService.PersonaName}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _net.ReportStatus("Steam host failed (see console)");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Opens the Steam overlay invite dialog. Only works in builds, not the editor.</summary>
        public void OpenInviteOverlay()
        {
            if (Current.HasValue) SteamFriends.OpenGameInviteOverlay(Current.Value.Id);
        }

        // --------------------------- Browse / join ---------------------------

        public async void Refresh()
        {
            if (!SteamService.IsReady || IsSearching) return;
            IsSearching = true;
            try
            {
                var results = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithKeyValue(GameKey, GameValue)
                    .WithKeyValue(VersionKey, ProtocolVersion)
                    .WithMaxResults(20)
                    .RequestAsync();

                _found.Clear();
                if (results != null)
                {
                    foreach (var l in results)
                    {
                        // Skip abandoned lobbies and ones hosted by this same Steam account
                        // (e.g. another window on this PC). Steam can't connect you to yourself.
                        ulong host = HostOf(l);
                        if (l.MemberCount <= 0 || host == 0 || host == SteamService.LocalSteamId) continue;

                        _found.Add(new LobbyEntry
                        {
                            Lobby = l,
                            Name = string.IsNullOrEmpty(l.GetData(NameKey)) ? "Unnamed hunt" : l.GetData(NameKey),
                            Members = l.MemberCount,
                            MaxMembers = l.MaxMembers,
                            FriendlyFire = l.GetData("fsh_ff") == "1",
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                IsSearching = false;
            }
        }

        public async void Join(Lobby lobby)
        {
            if (!SteamService.IsReady || IsBusy || _net.IsRunning) return;
            IsBusy = true;
            try
            {
                await JoinAsync(lobby);
            }
            finally
            {
                IsBusy = false;
            }
        }

        async Task JoinAsync(Lobby lobby)
        {
            _net.ReportStatus("Joining Steam lobby…");
            var result = await lobby.Join();
            if (result != RoomEnter.Success)
            {
                _net.ReportStatus($"Couldn't join lobby: {result}");
                return;
            }

            Current = lobby;
            ulong hostId = HostOf(lobby);
            if (hostId == 0)
            {
                LeaveLobby();
                _net.ReportStatus("That hunt has no host anymore");
                return;
            }
            if (hostId == SteamService.LocalSteamId)
            {
                LeaveLobby();
                _net.ReportStatus("That hunt is hosted on your own Steam account. Steam tests need a friend on another PC.");
                return;
            }

            if (!_net.JoinSteam(hostId))
                LeaveLobby();
        }

        /// <summary>The Netcode host's SteamID from lobby data, or 0 if missing/invalid.</summary>
        static ulong HostOf(Lobby lobby) =>
            ulong.TryParse(lobby.GetData(HostKey), out var id) ? id : 0UL;

        void OnInviteAccepted(Lobby lobby, SteamId friend)
        {
            if (_net.IsRunning) _net.Leave();
            Join(lobby);
        }

        // --------------------------- Leave ---------------------------

        public void LeaveLobby()
        {
            if (!Current.HasValue) return;
            try { Current.Value.Leave(); }
            catch (Exception e) { Debug.LogWarning($"[Steam] Leave lobby: {e.Message}"); }
            Current = null;
        }
    }
}

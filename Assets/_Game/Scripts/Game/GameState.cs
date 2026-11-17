using System;
using FriendSlop.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Shared, host-owned session state: the team's money and a feed of announcements
    /// ("SOLD rabbit +$14, Headshot ×1.5"). One per scene, placed in-scene.
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        public static GameState Instance { get; private set; }

        /// <summary>Team wallet. Everyone shares one pot, How-to-Fish style.</summary>
        public readonly NetworkVariable<int> Money = new NetworkVariable<int>(0);

        /// <summary>Total animals sold this session (for fun stats later).</summary>
        public readonly NetworkVariable<int> AnimalsSold = new NetworkVariable<int>(0);

        /// <summary>Lobby rule chosen by the host when hosting: can players hurt each other?</summary>
        public readonly NetworkVariable<bool> FriendlyFire = new NetworkVariable<bool>(false);

        public static bool FriendlyFireOn => Instance != null && Instance.IsSpawned && Instance.FriendlyFire.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer) FriendlyFire.Value = LobbySettings.FriendlyFire;
        }

        /// <summary>Raised locally on every peer when the host announces something.</summary>
        public static event Action<string> Announced;

        [SerializeField] WeaponCatalog catalog;
        [SerializeField] GameObject tankPrefab;
        [SerializeField] GameObject fishPrefab;
        public GameObject FishPrefab => fishPrefab;
        [SerializeField] Vector3 garagePosition = new Vector3(-12f, 0f, -14f);
        [SerializeField] float garageYaw = 0f;
        public const int TankPrice = 7500;
        public const int TankLevel = 8;

        /// <summary>Ask for the team tank: buys it (team money), or recalls an empty one to the garage for free.</summary>
        public void RequestTank() => TankRpc();

        [Rpc(SendTo.Server)]
        void TankRpc(RpcParams rpcParams = default)
        {
            string who = PlayerName(rpcParams.Receive.SenderClientId);
            var spot = new Vector3(garagePosition.x, FriendSlop.World.Ground.HeightAt(garagePosition) + 0.6f, garagePosition.z);
            if (FriendSlop.World.Tank.All.Count > 0)
            {
                var t = FriendSlop.World.Tank.All[0];
                for (int i = 0; i < FriendSlop.World.Tank.Seats; i++)
                    if (t.Occupant(i) != FriendSlop.World.Tank.Empty) { AnnounceRpc("Someone's in the tank. Can't recall it now!"); return; }
                var rb = t.GetComponent<Rigidbody>();
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.position = spot; rb.rotation = Quaternion.Euler(0f, garageYaw, 0f);
                t.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, garageYaw, 0f));
                AnnounceRpc($"{who} called the tank back to the garage.");
                return;
            }
            if (tankPrefab == null) return;
            int lv = FriendSlop.Player.PlayerProgress.Of(rpcParams.Receive.SenderClientId) is { } p ? p.Level.Value : 1;
            if (lv < TankLevel) { AnnounceRpc($"{who} needs hunter level {TankLevel} to buy the tank."); return; }
            if (!ServerTrySpend(TankPrice)) { AnnounceRpc($"{who} can't afford the tank (${TankPrice:N0}). Go hunt!"); return; }
            var go = Instantiate(tankPrefab, spot, Quaternion.Euler(0f, garageYaw, 0f));
            go.GetComponent<NetworkObject>().Spawn(true);
            AnnounceRpc($"{who} bought a TANK! It's waiting at the garage.");
        }
        public WeaponCatalog Catalog => catalog;

        public static string PlayerName(ulong clientId) =>
            FriendSlop.Player.PlayerAppearance.NameOf(clientId);

        /// <summary>Client: buy a weapon with the team's money; it goes into your own inventory.</summary>
        public void RequestBuyWeapon(int index)
        {
            PlayerWeapons.Local?.NotePendingPurchase(index);
            BuyWeaponRpc(index);
        }

        [Rpc(SendTo.Server)]
        void BuyWeaponRpc(int index, RpcParams rpcParams = default)
        {
            var def = catalog != null ? catalog.Get(index) : null;
            if (def == null || index >= WeaponCatalog.MaxWeapons) return;

            ulong buyer = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(buyer, out var client) || client.PlayerObject == null) return;
            if (!client.PlayerObject.TryGetComponent(out PlayerWeapons weapons) || weapons.Owns(index)) return;

            string who = PlayerName(buyer);
            int level = FriendSlop.Player.PlayerProgress.Of(buyer) is { } prog ? prog.Level.Value : 1;
            if (level < def.requiredLevel)
            {
                AnnounceRpc($"{who} needs hunter level {def.requiredLevel} for the {def.displayName}.");
                return;
            }
            if (Money.Value < def.price)
            {
                AnnounceRpc($"{who} can't afford the {def.displayName} (${def.price:N0}). Go hunt!");
                return;
            }

            if (!weapons.ServerGive(index)) return;
            Money.Value -= def.price;
            AnnounceRpc($"{who} bought the {def.displayName} for ${def.price:N0}!");
        }

        /// <summary>Server only: spend team money if there's enough.</summary>
        public bool ServerTrySpend(int amount)
        {
            if (!IsServer || Money.Value < amount) return false;
            Money.Value -= amount;
            return true;
        }

        /// <summary>Server only: grant money without counting a sale (dev/testing).</summary>
        public void ServerGrantMoney(int amount)
        {
            if (IsServer) Money.Value += amount;
        }

        /// <summary>Editor-only cheat (F9): +$1,000. Ignored in builds, so friends can't use it.</summary>
        public void RequestDevMoney()
        {
            if (Application.isEditor) DevMoneyRpc();
        }

        [Rpc(SendTo.Server)]
        void DevMoneyRpc()
        {
            if (!Application.isEditor) return; // the host must also be the editor
            Money.Value += 1000;
            AnnounceRpc("[dev] +$1,000");
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameState] Duplicate GameState in scene; ignoring this one.");
                return;
            }
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>Server only: pay the team and tell everyone why.</summary>
        public void ServerAddMoney(int amount, string announcement)
        {
            if (!IsServer) return;
            Money.Value += amount;
            AnimalsSold.Value++;
            AnnounceRpc(announcement);
        }

        /// <summary>Raised on every peer when a Lucky Lodge station resolves a bet: (station, a, b, c, payout, info, stake).</summary>
        public static event System.Action<int, int, int, int, int, int, int> GambleResult;
        /// <summary>Raised on every peer for in-progress station actions (e.g. "scratch the next panel").</summary>
        public static event System.Action<int, int> GambleAction;

        /// <summary>Lucky Lodge progressive jackpot: every bet feeds it; three golden antlers on the slots win it.</summary>
        public readonly NetworkVariable<int> LodgeJackpot = new NetworkVariable<int>(JackpotSeed);
        public const int JackpotSeed = 500;

        /// <summary>Server: tell everyone how a gamble turned out (they animate it; the station pays out after).</summary>
        public void ServerGambleResult(int station, int a, int b, int c, int payout, int info, int stake)
        {
            if (IsServer) GambleResultRpc(station, a, b, c, payout, info, stake);
        }

        [Rpc(SendTo.Everyone)]
        void GambleResultRpc(int station, int a, int b, int c, int payout, int info, int stake) => GambleResult?.Invoke(station, a, b, c, payout, info, stake);

        /// <summary>Client: bet team money at a Lucky Lodge station (option = which bet, e.g. roulette colour).</summary>
        public void RequestGambleBet(int station, int option, int amount) => GambleBetRpc(station, option, amount);

        [Rpc(SendTo.Server)]
        void GambleBetRpc(int station, int option, int amount, RpcParams rpcParams = default)
        {
            foreach (var s in FriendSlop.World.GambleStation.All)
                if (s.id == station) { s.ServerBetMoney(rpcParams.Receive.SenderClientId, option, amount); return; }
        }

        /// <summary>Client: poke a station mid-game (scratch the next panel now).</summary>
        public void RequestGambleAction(int station, int action) => GambleActionServerRpc(station, action);

        [Rpc(SendTo.Server)]
        void GambleActionServerRpc(int station, int action) => GambleActionRpc(station, action);

        [Rpc(SendTo.Everyone)]
        void GambleActionRpc(int station, int action) => GambleAction?.Invoke(station, action);

        /// <summary>Server: pay a gamble win into the pot (without counting it as an animal sold).</summary>
        public void ServerPayGamble(int amount, string announcement)
        {
            if (!IsServer) return;
            Money.Value += amount;
            AnnounceRpc(announcement);
        }

        // ------------------------------------------------------------------ kill feed

        public const ulong NoPlayer = ulong.MaxValue;
        [Flags] public enum Feed { None = 0, Headshot = 1, LongShot = 2, JumpShot = 4, Boss = 8, PlayerDown = 16, Elite = 32 }

        /// <summary>Raised on every peer: (killerId or NoPlayer, killer name, weapon, victimId or NoPlayer, victim name, flags).</summary>
        public static event Action<ulong, string, string, ulong, string, Feed> KillFeed;

        /// <summary>Server: add a line to everyone's kill feed.</summary>
        public void ServerKillFeed(ulong killerId, string killer, string weapon, ulong victimId, string victim, Feed flags)
        {
            if (IsServer) KillFeedRpc(killerId, killer ?? "", weapon ?? "", victimId, victim ?? "", (int)flags);
        }

        [Rpc(SendTo.Everyone)]
        void KillFeedRpc(ulong killerId, string killer, string weapon, ulong victimId, string victim, int flags)
            => KillFeed?.Invoke(killerId, killer, weapon, victimId, victim, (Feed)flags);

        // ------------------------------------------------------------------ text chat

        public const int ChatMaxLength = 120;
        /// <summary>Raised on every peer: (sender id, sender name, message).</summary>
        public static event Action<ulong, string, string> ChatMessage;
        readonly System.Collections.Generic.Dictionary<ulong, float> _chatNext = new System.Collections.Generic.Dictionary<ulong, float>();

        /// <summary>Client: say something in team chat.</summary>
        public void SendChat(string text)
        {
            text = Clean(text);
            if (text.Length > 0) ChatServerRpc(text);
        }

        static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text) if (!char.IsControl(c)) sb.Append(c);
            string s = sb.ToString().Trim();
            return s.Length > ChatMaxLength ? s.Substring(0, ChatMaxLength) : s;
        }

        [Rpc(SendTo.Server)]
        void ChatServerRpc(string text, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            text = Clean(text);
            if (text.Length == 0) return;
            if (_chatNext.TryGetValue(sender, out float next) && Time.time < next) return; // light spam guard
            _chatNext[sender] = Time.time + 0.4f;
            ChatRpc(sender, PlayerName(sender), text);
        }

        [Rpc(SendTo.Everyone)]
        void ChatRpc(ulong sender, string name, string text)
        {
            Debug.Log($"[Chat] {name}: {text}");
            ChatMessage?.Invoke(sender, name, text);
        }

        /// <summary>Server only: broadcast a message to every player's HUD.</summary>
        public void ServerAnnounce(string announcement)
        {
            if (!IsServer) return;
            AnnounceRpc(announcement);
        }

        [Rpc(SendTo.Everyone)]
        void AnnounceRpc(string announcement)
        {
            Debug.Log($"[Game] {announcement}");
            Announced?.Invoke(announcement);
        }
    }
}

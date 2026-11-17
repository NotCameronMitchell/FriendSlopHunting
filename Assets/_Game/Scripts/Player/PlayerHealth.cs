using FriendSlop.Audio;
using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    /// <summary>
    /// Player HP for animals that fight back. The host owns health and decides when you go down;
    /// knockback and respawn teleports are sent to the owning client because movement is
    /// owner-authoritative. Going down drops what you carry; you respawn at camp after a few
    /// seconds with your inventory intact (friendslop: dying should be funny, not punishing).
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerHealth : NetworkBehaviour
    {
        public const float MaxHealth = 100f;
        /// <summary>Max health including the Thick Skin upgrade.</summary>
        public float MaxHp => MaxHealth + (TryGetComponent(out PlayerUpgrades up) ? up.HealthBonus : 0f);

        [SerializeField] float respawnSeconds = 5f;
        [SerializeField] float spawnProtectionSeconds = 3f;
        [SerializeField] Vector3 respawnPoint = new Vector3(0f, 0.2f, -3f);

        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(MaxHealth);
        public readonly NetworkVariable<bool> IsDown = new NetworkVariable<bool>(false);

        /// <summary>Local player's health (for the HUD).</summary>
        public static PlayerHealth Local { get; private set; }

        /// <summary>Local only: when the last hit landed (HUD red flash).</summary>
        public float LastHurtTime { get; private set; } = -10f;
        public float LastHurtAmount { get; private set; }
        public float RespawnAt { get; private set; }

        NetworkPlayer _player;
        float _protectedUntil;
        float _serverRespawnAt;

        void Awake() => _player = GetComponent<NetworkPlayer>();

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
            IsDown.OnValueChanged += OnDownChanged;
            _player.SetDownPose(IsDown.Value);
            if (IsServer) _protectedUntil = Time.time + spawnProtectionSeconds;
        }

        public override void OnNetworkDespawn()
        {
            IsDown.OnValueChanged -= OnDownChanged;
            if (Local == this) Local = null;
        }

        void OnDownChanged(bool previous, bool down) => _player.SetDownPose(down);

        // ============================================================ Host

        /// <summary>Host: hurt this player. Returns true if it took them down.</summary>
        /// <param name="attacker">What hurt them (an animal's name, "skunk stink", or the weapon for friendly fire).</param>
        /// <param name="killerId">The player who did it (friendly fire), else NoPlayer.</param>
        public bool ServerDamage(float amount, Vector3 knockback, string attacker, ulong killerId = GameState.NoPlayer, string weapon = null, bool boss = false)
        {
            if (!IsServer || IsDown.Value || Time.time < _protectedUntil || amount <= 0f) return false;

            Health.Value = Mathf.Max(0f, Health.Value - amount);
            HurtRpc(transform.position + Vector3.up * 1.2f, amount);
            if (knockback.sqrMagnitude > 0.01f) KnockbackRpc(knockback);

            if (Health.Value > 0f) return false;

            IsDown.Value = true;
            // With friends around, stay down longer so someone has time to come and revive you.
            float wait = NetworkManager.ConnectedClientsIds.Count > 1 ? 14f : respawnSeconds;
            _serverRespawnAt = Time.time + wait;
            DownedRpc(wait);
            string victim = GameState.PlayerName(OwnerClientId);
            string killer = killerId != GameState.NoPlayer ? GameState.PlayerName(killerId) : null;
            GameState.Instance?.ServerAnnounce(DeathLines.Pick(victim, attacker, killer, weapon, boss));
            GameState.Instance?.ServerKillFeed(killerId, killer ?? attacker, weapon ?? "", OwnerClientId, victim,
                GameState.Feed.PlayerDown | (boss ? GameState.Feed.Boss : GameState.Feed.None));
            return true;
        }

        /// <summary>A teammate held E next to us: get back up right here (half health) instead of walking from camp.</summary>
        public void RequestRevive() => ReviveRpc();

        [Rpc(SendTo.Server)]
        void ReviveRpc(RpcParams rpcParams = default)
        {
            ulong rescuer = rpcParams.Receive.SenderClientId;
            if (!IsDown.Value || rescuer == OwnerClientId) return;
            if (!NetworkManager.ConnectedClients.TryGetValue(rescuer, out var c) || c.PlayerObject == null) return;
            if ((c.PlayerObject.transform.position - transform.position).sqrMagnitude > 4f * 4f) return;
            Health.Value = MaxHp * 0.5f;
            IsDown.Value = false;
            _protectedUntil = Time.time + spawnProtectionSeconds;
            GameState.Instance?.ServerAnnounce($"{GameState.PlayerName(rescuer)} revived {GameState.PlayerName(OwnerClientId)}! Teamwork!");
            RevivedRpc();
        }

        [Rpc(SendTo.Everyone)]
        void RevivedRpc() => SfxPlayer.PlayAt(Sfx.HeadshotDing, transform.position + Vector3.up, 0.8f, 1.3f, 3f, 40f);

        void Update()
        {
            if (!IsServer || !IsSpawned || !IsDown.Value || Time.time < _serverRespawnAt) return;

            Health.Value = MaxHp;
            IsDown.Value = false;
            _protectedUntil = Time.time + spawnProtectionSeconds;
            Vector2 jitter = Random.insideUnitCircle * 1.5f;
            RespawnRpc(respawnPoint + new Vector3(jitter.x, 0f, jitter.y));
        }

        // ============================================================ Clients

        [Rpc(SendTo.Everyone)]
        void HurtRpc(Vector3 position, float amount)
        {
            if (IsOwner)
            {
                LastHurtTime = Time.time;
                SfxPlayer.Play2D(Sfx.Thud, 0.8f, 0.7f);
                _player.AddRecoil(Mathf.Clamp(amount * 0.4f, 2f, 12f));
                NetworkPlayer.Shake(Mathf.Clamp(amount / 60f, 0.15f, 0.6f));
                LastHurtAmount = amount;
            }
            else SfxPlayer.PlayAt(Sfx.Thud, position, 0.8f, 0.7f, 2f, 30f);
        }

        /// <summary>Host: restore some health (capped at max).</summary>
        public void ServerHeal(float amount)
        {
            if (IsServer && !IsDown.Value) Health.Value = Mathf.Min(MaxHp, Health.Value + amount);
        }

        /// <summary>Host: push a player without hurting them (explosion blasts, rocket jumps).</summary>
        public void ServerShove(Vector3 velocity)
        {
            if (IsServer && !IsDown.Value) KnockbackRpc(velocity);
        }

        [Rpc(SendTo.Owner)]
        void KnockbackRpc(Vector3 velocity) => _player.AddKnockback(velocity);

        [Rpc(SendTo.Owner)]
        void DownedRpc(float seconds)
        {
            RespawnAt = Time.time + seconds;
            _player.OnDowned();
        }

        [Rpc(SendTo.Owner)]
        void RespawnRpc(Vector3 position) => _player.Teleport(position);
    }
}

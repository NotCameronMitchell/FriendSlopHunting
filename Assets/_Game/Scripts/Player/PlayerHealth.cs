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

        [SerializeField] float respawnSeconds = 5f;
        [SerializeField] float spawnProtectionSeconds = 3f;
        [SerializeField] Vector3 respawnPoint = new Vector3(0f, 0.2f, -3f);

        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(MaxHealth);
        public readonly NetworkVariable<bool> IsDown = new NetworkVariable<bool>(false);

        /// <summary>Local player's health (for the HUD).</summary>
        public static PlayerHealth Local { get; private set; }

        /// <summary>Local only: when the last hit landed (HUD red flash).</summary>
        public float LastHurtTime { get; private set; } = -10f;
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
        public bool ServerDamage(float amount, Vector3 knockback, string attacker)
        {
            if (!IsServer || IsDown.Value || Time.time < _protectedUntil || amount <= 0f) return false;

            Health.Value = Mathf.Max(0f, Health.Value - amount);
            HurtRpc(transform.position + Vector3.up * 1.2f, amount);
            if (knockback.sqrMagnitude > 0.01f) KnockbackRpc(knockback);

            if (Health.Value > 0f) return false;

            IsDown.Value = true;
            _serverRespawnAt = Time.time + respawnSeconds;
            DownedRpc(respawnSeconds);
            GameState.Instance?.ServerAnnounce($"{GameState.PlayerName(OwnerClientId)} got wrecked by a {attacker}!");
            return true;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !IsDown.Value || Time.time < _serverRespawnAt) return;

            Health.Value = MaxHealth;
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
            }
            else SfxPlayer.PlayAt(Sfx.Thud, position, 0.8f, 0.7f, 2f, 30f);
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

using System.Collections.Generic;
using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Interaction;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Animals
{
    /// <summary>
    /// A huntable animal. Alive: the host drives it with a NavMeshAgent (wander, bolt from players
    /// and gunshots) and clients just see the replicated transform plus a local hop animation.
    /// Dead: the same object becomes a physics carcass that can be juggled with gunfire, carried,
    /// and sold. Sale value = species base × weight factor × style multiplier.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Rigidbody), typeof(Carryable))]
    public class Animal : NetworkBehaviour
    {
        /// <summary>Spawned animals (populated on every peer; AI only runs on the host).</summary>
        public static readonly List<Animal> All = new List<Animal>();

        [SerializeField] AnimalSpecies species;
        [Tooltip("Child that holds the meshes; bobbed for the hop animation. Colliders stay on the root's other children.")]
        [SerializeField] Transform visualRoot;
        [SerializeField] Renderer[] furRenderers = System.Array.Empty<Renderer>();

        public readonly NetworkVariable<bool> IsDead = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<float> WeightKg = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<int> KillStyle = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Juggles = new NetworkVariable<int>(0);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        NavMeshAgent _agent;
        Rigidbody _rb;
        Carryable _carry;

        // Server AI state
        float _health;
        Vector3 _home;
        float _nextThink;
        float _panicUntil;
        Vector3 _panicFrom;
        float _deathTime;

        // Visual state (all peers)
        Vector3 _lastPos;
        float _hopPhase;
        Vector3 _visualRestPos;

        public AnimalSpecies Species => species;
        public Carryable Carry => _carry;

        public int SaleValue
        {
            get
            {
                if (species == null) return 0;
                float weightFactor = WeightKg.Value / Mathf.Max(0.01f, species.AverageWeight);
                float value = species.basePrice * weightFactor * StylePricing.Multiplier((ShotStyle)KillStyle.Value, Juggles.Value);
                return Mathf.Max(1, Mathf.RoundToInt(value));
            }
        }

        public string StyleDescription => StylePricing.Describe((ShotStyle)KillStyle.Value, Juggles.Value);

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _rb = GetComponent<Rigidbody>();
            _carry = GetComponent<Carryable>();
            _agent.enabled = false; // only the host turns it on, once spawned on the navmesh
            if (visualRoot) _visualRestPos = visualRoot.localPosition;
            ApplyFurColor();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _lastPos = transform.position;
            IsDead.OnValueChanged += OnDeadChanged;
            Juggles.OnValueChanged += OnJugglesChanged;

            if (IsServer)
            {
                _health = species.maxHealth;
                _home = transform.position;
                if (WeightKg.Value <= 0f)
                    WeightKg.Value = Random.Range(species.minWeightKg, species.maxWeightKg);

                if (!IsDead.Value)
                {
                    _agent.enabled = true;
                    _agent.speed = species.wanderSpeed;
                    _agent.Warp(transform.position);
                }
            }
            ApplyDeadVisuals(IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            IsDead.OnValueChanged -= OnDeadChanged;
            Juggles.OnValueChanged -= OnJugglesChanged;
        }

        void OnDeadChanged(bool previous, bool dead)
        {
            ApplyDeadVisuals(dead);
            if (dead && !previous)
                SfxPlayer.PlayAt(Sfx.Squeak, transform.position, 0.9f, Random.Range(0.95f, 1.25f), 3f, 60f);
        }

        void OnJugglesChanged(int previous, int current)
        {
            if (current > previous)
                SfxPlayer.PlayAt(Sfx.Boing, transform.position, 0.9f, 1f + 0.12f * current, 4f, 80f);
        }

        void ApplyDeadVisuals(bool dead)
        {
            if (dead && visualRoot) visualRoot.localPosition = _visualRestPos;
        }

        // ============================== Update ==============================

        void Update()
        {
            if (!IsSpawned) return;

            if (!IsDead.Value) AnimateHop();

            if (!IsServer) return;
            if (IsDead.Value) ServerCarcassUpkeep();
            else ServerThink();
        }

        void FixedUpdate()
        {
            if (!IsSpawned) return;
            // Physics only runs where we have authority, for a dead body that nobody is holding.
            // Everywhere else the transform comes from the network (or the agent, or the holder).
            bool simulate = IsOwner && IsDead.Value && !_carry.IsHeld;
            if (_rb.isKinematic == simulate) _rb.isKinematic = !simulate;
        }

        void AnimateHop()
        {
            if (!visualRoot || species.hopHeight <= 0f) return;
            Vector3 pos = transform.position;
            float moved = new Vector3(pos.x - _lastPos.x, 0f, pos.z - _lastPos.z).magnitude;
            _lastPos = pos;

            _hopPhase += moved * species.hopsPerMeter * Mathf.PI;
            float speedFactor = Mathf.Clamp01(moved / Mathf.Max(0.0001f, Time.deltaTime) / 2f);
            float bob = Mathf.Abs(Mathf.Sin(_hopPhase)) * species.hopHeight * speedFactor;
            visualRoot.localPosition = _visualRestPos + Vector3.up * bob;
        }

        // ============================== Server AI ==============================

        void ServerThink()
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            // Bolt from the nearest player who gets too close, or from a recent gunshot.
            Vector3 threat;
            bool scared = TryGetNearbyPlayer(species.spookRadius, out threat);
            if (!scared && Time.time < _panicUntil)
            {
                threat = _panicFrom;
                scared = true;
            }

            if (scared)
            {
                _agent.speed = species.fleeSpeed;
                if (Time.time >= _nextThink || _agent.remainingDistance < 1.5f)
                {
                    Vector3 away = transform.position - threat;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                    // Zig-zag a little so they're fun to chase, not a straight line.
                    Vector3 dir = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * away.normalized;
                    SetDestinationNear(transform.position + dir * 10f, 4f);
                    _nextThink = Time.time + Random.Range(0.4f, 0.9f);
                }
                return;
            }

            _agent.speed = species.wanderSpeed;
            if (Time.time >= _nextThink && (!_agent.hasPath || _agent.remainingDistance < 0.5f))
            {
                // Graze for a bit, then hop somewhere new near home.
                if (Random.value < 0.4f)
                {
                    _agent.ResetPath();
                    _nextThink = Time.time + Random.Range(1.5f, 4f);
                }
                else
                {
                    Vector2 r = Random.insideUnitCircle * species.wanderRadius;
                    SetDestinationNear(_home + new Vector3(r.x, 0f, r.y), 3f);
                    _nextThink = Time.time + Random.Range(2f, 5f);
                }
            }
        }

        bool TryGetNearbyPlayer(float radius, out Vector3 position)
        {
            position = default;
            float best = radius * radius;
            bool found = false;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                Vector3 p = client.PlayerObject.transform.position;
                float d = (p - transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    position = p;
                    found = true;
                }
            }
            return found;
        }

        void SetDestinationNear(Vector3 target, float searchRadius)
        {
            if (NavMesh.SamplePosition(target, out var hit, searchRadius, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        /// <summary>Server: a shot landed near here.</summary>
        public void ServerHearShot(Vector3 impactPoint, Vector3 shooterPosition)
        {
            if (IsDead.Value) return;
            float hear = species.hearingRadius;
            if ((impactPoint - transform.position).sqrMagnitude > hear * hear &&
                (shooterPosition - transform.position).sqrMagnitude > hear * hear) return;
            _panicFrom = shooterPosition;
            _panicUntil = Time.time + Random.Range(2.5f, 4f);
            _nextThink = 0f;
        }

        // ============================== Damage ==============================

        /// <summary>Server: apply a validated hit from a player.</summary>
        public void ServerApplyHit(float damage, Vector3 direction, ShotStyle style, ulong shooterId)
        {
            if (!IsServer) return;

            if (IsDead.Value)
            {
                ServerJuggle(direction);
                return;
            }

            _health -= damage;
            if (_health > 0f)
            {
                _panicFrom = transform.position - direction * 10f;
                _panicUntil = Time.time + 5f;
                _nextThink = 0f;
                return;
            }

            ServerDie(direction, style, shooterId);
        }

        void ServerDie(Vector3 direction, ShotStyle style, ulong shooterId)
        {
            KillStyle.Value = (int)style;
            IsDead.Value = true;
            _deathTime = Time.time;

            _agent.enabled = false;
            _rb.isKinematic = false;
            // Comedic launch: up and away, spinning.
            _rb.linearVelocity = direction.normalized * 4f + Vector3.up * 5.5f;
            _rb.angularVelocity = Random.insideUnitSphere * 12f;

            _carry.ServerSetCarryable(true);

            string styleText = StyleDescription;
            string who = shooterId == NetworkManager.ServerClientId ? "Host" : $"Player {shooterId}";
            GameState.Instance?.ServerAnnounce(string.IsNullOrEmpty(styleText)
                ? $"{who} bagged a {species.displayName}!"
                : $"{who} bagged a {species.displayName}! {styleText}");
        }

        void ServerJuggle(Vector3 direction)
        {
            if (_carry.IsHeld) return;

            bool airborne = !Physics.Raycast(transform.position + Vector3.up * 0.05f, Vector3.down, 0.6f,
                ~0, QueryTriggerInteraction.Ignore) || _rb.linearVelocity.y > 1.5f;

            if (airborne && Juggles.Value < StylePricing.MaxJuggles)
            {
                Juggles.Value++;
                GameState.Instance?.ServerAnnounce($"JUGGLE ×{Juggles.Value}!  {species.displayName} now worth ${SaleValue}");
            }

            // Pop it back up so you can keep it in the air.
            if (_rb.isKinematic) _rb.isKinematic = false;
            _rb.linearVelocity = new Vector3(direction.x, 0f, direction.z).normalized * 2f + Vector3.up * 7f;
            _rb.angularVelocity = Random.insideUnitSphere * 10f;
        }

        void ServerCarcassUpkeep()
        {
            // Fell through the world or left to rot: clean up.
            if (transform.position.y < -30f || (!_carry.IsHeld && Time.time - _deathTime > 600f))
                NetworkObject.Despawn(true);
        }

        void ApplyFurColor()
        {
            if (species == null) return;
            var block = new MaterialPropertyBlock();
            foreach (var r in furRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, species.furColor);
                block.SetColor(ColorId, species.furColor);
                r.SetPropertyBlock(block);
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Interaction;
using FriendSlop.Player;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Animals
{
    /// <summary>
    /// A huntable animal. Alive: the host drives it with a NavMeshAgent and a small brain that
    /// depends on the species' temperament:
    ///   Skittish    – wander, bolt from players and gunshots.
    ///   Aggressive  – charge players who come close or shoot it.
    ///   Territorial – only charges if you get really close or hurt it.
    ///   Predator    – hunts players on sight; packmates join in.
    /// Clients just see the replicated transform plus local hop/lunge animation and sounds.
    /// Dead: the same object becomes a physics carcass that can be juggled, carried and sold.
    /// Sale value = species base × weight factor × style multiplier (+ instant bounty for bosses).
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
        [SerializeField] Renderer[] accentRenderers = System.Array.Empty<Renderer>();

        public readonly NetworkVariable<bool> IsDead = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<float> WeightKg = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<int> KillStyle = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Juggles = new NetworkVariable<int>(0);
        /// <summary>0..1, for boss health bars.</summary>
        public readonly NetworkVariable<float> HealthFraction = new NetworkVariable<float>(1f);
        /// <summary>True while hunting/charging someone (for HUD/sounds on clients).</summary>
        public readonly NetworkVariable<bool> IsAngry = new NetworkVariable<bool>(false);
        /// <summary>Opossum trick: lying "dead" for a few seconds (shows the dead face, then bolts).</summary>
        public readonly NetworkVariable<bool> PlayingDead = new NetworkVariable<bool>(false);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        enum Mode { Wander, Flee, Chase }

        NavMeshAgent _agent;
        Rigidbody _rb;
        Carryable _carry;

        // Server AI state
        float _health;
        Vector3 _home;
        Mode _mode;
        float _nextThink;
        float _panicUntil;
        Vector3 _panicFrom;
        PlayerHealth _target;
        float _chaseUntil;
        float _nextAttack;
        float _nextVoice;
        float _deathTime;
        float _playDeadUntil, _nextPlayDead;

        // Visual state (all peers)
        Vector3 _lastPos;
        float _hopPhase;
        Vector3 _visualRestPos;
        CritterRig _rig;

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

        /// <summary>Big animals fly less far when killed or juggled.</summary>
        float LaunchFactor => Mathf.Clamp(0.6f / Mathf.Max(0.1f, species.bodyLength * species.scale), 0.25f, 1f);

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _rb = GetComponent<Rigidbody>();
            _carry = GetComponent<Carryable>();
            _agent.enabled = false; // only the host turns it on, once spawned on the navmesh
            if (visualRoot) _visualRestPos = visualRoot.localPosition;
            _rig = GetComponentInChildren<CritterRig>(true);
            Tint(furRenderers, species != null ? species.furColor : Color.white);
            Tint(accentRenderers, species != null ? species.accentColor : Color.white);
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
                _nextVoice = Time.time + Random.Range(3f, 12f);
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
                PlayVoice(VoiceKind.Death);
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
            if (PlayingDead.Value)
            {
                if (Time.time < _playDeadUntil) return;
                PlayingDead.Value = false;
                if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = false;
                _nextThink = 0f;
            }
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            if (Time.time >= _nextVoice)
            {
                _nextVoice = Time.time + Random.Range(6f, 16f);
                VoiceRpc((int)VoiceKind.Idle);
            }

            if (species.Fights) ThinkFighter();
            if (_mode == Mode.Chase) { DoChase(); return; }

            // Fighters don't hang around camp: if one wandered in, walk it back out.
            if (species.Fights && SafeZone.Contains(transform.position, 4f))
            {
                _mode = Mode.Wander;
                _agent.speed = species.wanderSpeed * 1.5f;
                if (Time.time >= _nextThink)
                {
                    SetDestinationNear(SafeZone.ExitPointFor(transform.position), 6f);
                    _nextThink = Time.time + 1.5f;
                }
                return;
            }
            ThinkSkittishOrWander();
        }

        void ThinkFighter()
        {
            if (_mode == Mode.Chase) return;
            bool hurtEnoughToRun = species.fleeBelowHealth > 0f && _health < species.maxHealth * species.fleeBelowHealth;
            if (hurtEnoughToRun) return;

            float radius = species.aggroRadius;
            if (radius <= 0f) return;
            if (FriendSlop.World.DayNightCycle.NightNow) radius *= 1.35f; // bolder in the dark
            var victim = NearestPlayer(radius);
            if (victim != null) StartChase(victim, alertPack: true);
        }

        void StartChase(PlayerHealth victim, bool alertPack)
        {
            if (victim == null || victim.IsDown.Value) return;
            if (SafeZone.Contains(victim.transform.position)) return; // camp is a refuge
            bool wasChasing = _mode == Mode.Chase;
            _target = victim;
            _mode = Mode.Chase;
            _chaseUntil = Time.time + 14f;
            IsAngry.Value = true;
            _agent.speed = species.chargeSpeed;
            _agent.stoppingDistance = Mathf.Max(0.2f, species.attackRange * 0.6f);
            if (!wasChasing) VoiceRpc((int)VoiceKind.Aggro);

            if (!alertPack || species.packSize <= 1) return;
            foreach (var other in All)
            {
                if (other == this || other.species != species || other.IsDead.Value) continue;
                if ((other.transform.position - transform.position).sqrMagnitude < 25f * 25f)
                    other.StartChase(victim, alertPack: false);
            }
        }

        void StopChase()
        {
            _mode = Mode.Wander;
            _target = null;
            IsAngry.Value = false;
            _agent.speed = species.wanderSpeed;
            _agent.stoppingDistance = 0.2f;
            _nextThink = 0f;
        }

        void DoChase()
        {
            if (_target == null || !_target.IsSpawned || _target.IsDown.Value || Time.time > _chaseUntil)
            {
                // Look for someone else nearby before giving up (predators keep hunting).
                var next = species.temperament == Temperament.Predator ? NearestPlayer(species.aggroRadius) : null;
                if (next != null) { StartChase(next, alertPack: false); }
                else { StopChase(); return; }
            }

            Vector3 targetPos = _target.transform.position;
            if (SafeZone.Contains(targetPos))
            {
                // They made it back to camp: give up and wander off, grumbling.
                StopChase();
                VoiceRpc((int)VoiceKind.Hurt);
                SetDestinationNear(SafeZone.ExitPointFor(transform.position, 14f), 6f);
                _nextThink = Time.time + 3f;
                return;
            }
            Vector3 flat = targetPos - transform.position;
            flat.y = 0f;
            float dist = flat.magnitude;

            if (dist > species.loseInterestRadius) { StopChase(); return; }
            if (species.fleeBelowHealth > 0f && _health < species.maxHealth * species.fleeBelowHealth)
            {
                StopChase();
                Panic(targetPos, 6f);
                return;
            }

            _chaseUntil = Mathf.Max(_chaseUntil, Time.time + 2f);
            if (Time.time >= _nextThink)
            {
                _agent.SetDestination(targetPos);
                _nextThink = Time.time + 0.2f;
            }

            // Face the victim when close so the lunge reads right.
            if (dist < species.attackRange * 2.5f && dist > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 10f);

            float reach = species.attackRange + 0.35f + species.Radius * 0.5f;
            if (dist <= reach && Time.time >= _nextAttack)
            {
                _nextAttack = Time.time + species.attackCooldown;
                Vector3 dir = dist > 0.01f ? flat / dist : transform.forward;
                Vector3 knock = dir * species.knockback + Vector3.up * species.knockback * 0.45f;
                _target.ServerDamage(species.attackDamage, knock, species.isBoss ? species.bossTitle : species.displayName);
                AttackRpc();
            }
        }

        void ThinkSkittishOrWander()
        {
            // Skittish: bolt from the nearest player who gets too close, or from a recent gunshot.
            bool scared = false;
            Vector3 threat = default;
            if (species.temperament == Temperament.Skittish)
            {
                var near = NearestPlayer(species.spookRadius);
                if (near != null) { scared = true; threat = near.transform.position; }
            }
            if (!scared && Time.time < _panicUntil)
            {
                threat = _panicFrom;
                scared = true;
            }

            if (scared)
            {
                _mode = Mode.Flee;
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

            _mode = Mode.Wander;
            _agent.speed = species.wanderSpeed;
            if (Time.time >= _nextThink && (!_agent.hasPath || _agent.remainingDistance < 0.5f))
            {
                // Graze for a bit, then move somewhere new near home.
                if (Random.value < 0.4f)
                {
                    _agent.ResetPath();
                    _nextThink = Time.time + Random.Range(1.5f, 4f);
                }
                else
                {
                    Vector2 r = Random.insideUnitCircle * species.wanderRadius;
                    Vector3 dest = _home + new Vector3(r.x, 0f, r.y);
                    if (species.Fights && SafeZone.Contains(dest, 4f)) dest = SafeZone.ExitPointFor(dest, 10f);
                    SetDestinationNear(dest, 3f);
                    _nextThink = Time.time + Random.Range(2f, 5f);
                }
            }
        }

        PlayerHealth NearestPlayer(float radius)
        {
            PlayerHealth best = null;
            float bestSq = radius * radius;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerHealth ph)) continue;
                if (ph.IsDown.Value) continue;
                if (species.Fights && SafeZone.Contains(client.PlayerObject.transform.position)) continue; // camp is a refuge
                float d = (client.PlayerObject.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = ph; }
            }
            return best;
        }

        PlayerHealth PlayerById(ulong clientId) =>
            NetworkManager.ConnectedClients.TryGetValue(clientId, out var c) && c.PlayerObject != null
                ? c.PlayerObject.GetComponent<PlayerHealth>() : null;

        void SetDestinationNear(Vector3 target, float searchRadius)
        {
            if (NavMesh.SamplePosition(target, out var hit, searchRadius, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        void Panic(Vector3 from, float seconds)
        {
            if (species.playsDead && !PlayingDead.Value && Time.time > _nextPlayDead && Random.value < 0.6f)
            {
                // Flop over. The panic still applies once it gets back up.
                PlayingDead.Value = true;
                _playDeadUntil = Time.time + Random.Range(3f, 5f);
                _nextPlayDead = _playDeadUntil + 6f;
                if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            }
            _panicFrom = from;
            _panicUntil = Time.time + seconds;
            _nextThink = 0f;
        }

        /// <summary>Server: a shot landed near here.</summary>
        public void ServerHearShot(Vector3 impactPoint, Vector3 shooterPosition)
        {
            if (IsDead.Value || species.Fights) return; // fighters don't scare
            float hear = species.hearingRadius;
            if ((impactPoint - transform.position).sqrMagnitude > hear * hear &&
                (shooterPosition - transform.position).sqrMagnitude > hear * hear) return;
            Panic(shooterPosition, Random.Range(2.5f, 4f));
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
            HealthFraction.Value = Mathf.Clamp01(_health / species.maxHealth);
            if (_health > 0f)
            {
                if (species.Fights)
                {
                    bool bail = species.fleeBelowHealth > 0f && _health < species.maxHealth * species.fleeBelowHealth;
                    if (bail) { StopChase(); Panic(transform.position - direction * 10f, 6f); }
                    else
                    {
                        var shooter = PlayerById(shooterId);
                        if (shooter != null) StartChase(shooter, alertPack: true);
                    }
                    if (Random.value < 0.5f) VoiceRpc((int)VoiceKind.Hurt);
                }
                else Panic(transform.position - direction * 10f, 5f);
                return;
            }

            ServerDie(direction, style, shooterId);
        }

        void ServerDie(Vector3 direction, ShotStyle style, ulong shooterId)
        {
            KillStyle.Value = (int)style;
            IsDead.Value = true;
            IsAngry.Value = false;
            HealthFraction.Value = 0f;
            _deathTime = Time.time;
            _target = null;

            _agent.enabled = false;
            _rb.isKinematic = false;
            // Comedic launch: up and away, spinning (less so for big animals).
            float k = LaunchFactor;
            _rb.linearVelocity = (direction.normalized * 4f + Vector3.up * 5.5f) * k;
            _rb.angularVelocity = Random.insideUnitSphere * 12f * k;

            _carry.ServerSetCarryable(true);

            PlayingDead.Value = false;
            PlayerProgress.Of(shooterId)?.ServerCreditKill(species, style); // stats + achievements for the killer
            string who = GameState.PlayerName(shooterId);
            FriendSlop.Game.NightEvents.Instance?.ServerOnKill(this, shooterId);
            if (species.isBoss)
            {
                GameState.Instance?.ServerGrantMoney(species.bounty);
                GameState.Instance?.ServerAnnounce($"LEGENDARY! {who} took down {species.bossTitle}! Bounty +${species.bounty:N0}");
                return;
            }

            if (species.bounty > 0)
            {
                GameState.Instance?.ServerGrantMoney(species.bounty);
                GameState.Instance?.ServerAnnounce($"{who} bagged {(species.shadowy ? "the" : "a")} {species.displayName}! Bounty +${species.bounty:N0}");
                return;
            }

            string styleText = StyleDescription;
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
            float k = LaunchFactor;
            _rb.linearVelocity = (new Vector3(direction.x, 0f, direction.z).normalized * 2f + Vector3.up * 7f) * k;
            _rb.angularVelocity = Random.insideUnitSphere * 10f * k;
        }

        void ServerCarcassUpkeep()
        {
            // Fell through the world or left to rot: clean up.
            if (transform.position.y < -30f || (!_carry.IsHeld && Time.time - _deathTime > 600f))
                NetworkObject.Despawn(true);
        }

        // ============================== FX (everyone) ==============================

        enum VoiceKind { Idle, Aggro, Attack, Hurt, Death }

        [Rpc(SendTo.Everyone)]
        void VoiceRpc(int kind) => PlayVoice((VoiceKind)kind);

        [Rpc(SendTo.Everyone)]
        void AttackRpc()
        {
            PlayVoice(VoiceKind.Attack);
            if (visualRoot) StartCoroutine(Lunge());
            if (_rig) _rig.Attack();
        }

        void PlayVoice(VoiceKind kind)
        {
            float pitch = species.voicePitch;
            float volume = 0.8f;
            float range = 45f;
            switch (kind)
            {
                case VoiceKind.Aggro: volume = 1f; range = 70f; pitch *= 0.95f; break;
                case VoiceKind.Attack: pitch *= 1.1f; break;
                case VoiceKind.Hurt: pitch *= 1.2f; break;
                case VoiceKind.Death: pitch *= Random.Range(1.05f, 1.3f); volume = 0.9f; break;
                default: volume = 0.55f; range = 35f; break;
            }
            if (species.isBoss) range *= 2f;
            if (_rig && kind != VoiceKind.Death) _rig.Say(kind == VoiceKind.Idle ? 0.3f : 0.55f, kind == VoiceKind.Idle ? 0.7f : 1f);
            SfxPlayer.PlayAt(Sfx.ForVoice(species.voice), transform.position, volume, pitch, 3f * species.scale, range);
        }

        IEnumerator Lunge()
        {
            const float duration = 0.25f;
            float t = 0f;
            Vector3 forward = Vector3.forward * Mathf.Max(0.15f, species.bodyLength * 0.35f);
            while (t < duration && visualRoot)
            {
                float k = Mathf.Sin(t / duration * Mathf.PI);
                visualRoot.localPosition = _visualRestPos + forward * k;
                t += Time.deltaTime;
                yield return null;
            }
            if (visualRoot) visualRoot.localPosition = _visualRestPos;
        }

        static void Tint(Renderer[] renderers, Color color)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }
    }
}

using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Game
{
    /// <summary>
    /// Host-side night happenings:
    ///  • Every 50 geese the team kills, the flock remembers: the next night is a GHOST GOOSE night
    ///    (ghost geese hunt the team until dawn, then fade away).
    ///  • Each nightfall there's a small chance a SHADOW STAG appears far out by the mountains:
    ///    skittish, fast, gone at dawn, with a big bounty and an exclusive cosmetic for the shooter.
    /// </summary>
    public class NightEvents : NetworkBehaviour
    {
        public static NightEvents Instance { get; private set; }

        [SerializeField] GameObject ghostGoosePrefab;
        [SerializeField] GameObject shadowStagPrefab;
        [SerializeField] int geesePerHaunting = 50;
        [SerializeField] int ghostsPerNight = 6;
        [SerializeField, Range(0f, 1f)] float shadowStagChance = 0.2f;

        /// <summary>Geese killed this session (shown on the HUD as the haunting gets close).</summary>
        public readonly NetworkVariable<int> GeeseKilled = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> GhostNight = new NetworkVariable<bool>(false);

        int _nextHauntingAt;
        int _hauntingsOwed;
        readonly List<NetworkObject> _eventMobs = new List<NetworkObject>();

        public int GeesePerHaunting => geesePerHaunting;

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            _nextHauntingAt = geesePerHaunting;
            DayNightCycle.ServerDusk += OnDusk;
            DayNightCycle.ServerDawn += OnDawn;
        }

        public override void OnNetworkDespawn()
        {
            DayNightCycle.ServerDusk -= OnDusk;
            DayNightCycle.ServerDawn -= OnDawn;
        }

        /// <summary>Host: an animal died (called from Animal).</summary>
        public void ServerOnKill(Animal animal, ulong shooterId)
        {
            if (!IsServer || animal.Species == null) return;
            if (animal.Species.displayName != "Goose") return;
            GeeseKilled.Value++;
            if (GeeseKilled.Value < _nextHauntingAt) return;

            _nextHauntingAt += geesePerHaunting;
            _hauntingsOwed++;
            if (DayNightCycle.NightNow && !GhostNight.Value) StartGhostNight();
            else GameState.Instance?.ServerAnnounce($"{GeeseKilled.Value} geese... The flock will remember. Tonight, they come back.");
        }

        void OnDusk(int day)
        {
            GameState.Instance?.ServerAnnounce($"Night falls on day {day}. Stay close to the campfire...");
            if (_hauntingsOwed > 0) StartGhostNight();
            if (shadowStagPrefab != null && Random.value < shadowStagChance) SpawnShadowStag();
        }

        void OnDawn(int day)
        {
            bool any = false;
            foreach (var mob in _eventMobs)
            {
                if (mob == null || !mob.IsSpawned) continue;
                var a = mob.GetComponent<Animal>();
                if (a != null && (a.IsDead.Value || a.Carry.IsHeld)) continue; // bagged ones stay sellable
                mob.Despawn(true);
                any = true;
            }
            _eventMobs.Clear();
            if (GhostNight.Value) GameState.Instance?.ServerAnnounce("The ghost geese fade with the sunrise...");
            else if (any) GameState.Instance?.ServerAnnounce("The Shadow Stag melts away into the morning mist.");
            GhostNight.Value = false;
            GameState.Instance?.ServerAnnounce($"Rise and shine! Day {day}.");
        }

        void StartGhostNight()
        {
            _hauntingsOwed--;
            GhostNight.Value = true;
            GameState.Instance?.ServerAnnounce("HONK... The ghosts of geese past have come for revenge!");
            if (ghostGoosePrefab == null) return;
            var players = new List<Transform>();
            foreach (var c in NetworkManager.ConnectedClientsList) if (c.PlayerObject) players.Add(c.PlayerObject.transform);
            for (int i = 0; i < ghostsPerNight && players.Count > 0; i++)
            {
                var near = players[i % players.Count].position;
                Spawn(ghostGoosePrefab, near, 20f, 35f);
            }
        }

        void SpawnShadowStag()
        {
            // Far out, by the mountains.
            var mob = Spawn(shadowStagPrefab, Vector3.zero, 100f, 135f);
            if (mob != null) GameState.Instance?.ServerAnnounce("Something dark stalks the treeline tonight...");
        }

        NetworkObject Spawn(GameObject prefab, Vector3 around, float minDist, float maxDist)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var dir = Random.insideUnitCircle.normalized;
                var p = around + new Vector3(dir.x, 0f, dir.y) * Random.Range(minDist, maxDist);
                p.y = Ground.HeightAt(p);
                if (WaterVolume.DepthAt(p + Vector3.up * 0.1f) > 0f || SafeZone.Contains(p, 6f)) continue;
                if (!NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) continue;
                var go = Instantiate(prefab, hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                var no = go.GetComponent<NetworkObject>();
                no.Spawn(true);
                _eventMobs.Add(no);
                return no;
            }
            return null;
        }

        /// <summary>Dev: force tonight's events.</summary>
        public void ServerDebugHaunt() { _hauntingsOwed++; if (DayNightCycle.NightNow) StartGhostNight(); }
        public void ServerDebugStag() => SpawnShadowStag();
    }
}

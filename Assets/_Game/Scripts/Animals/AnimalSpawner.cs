using System.Collections;
using System.Collections.Generic;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Animals
{
    /// <summary>
    /// Host-side spawner that keeps one species' population alive in a ring around this point
    /// (away from camp). Population, pack size and spawn distances come from the species asset,
    /// so tuning happens in one place. Packs (wolves, ducks) spawn together. Dead animals
    /// (carcasses) don't count toward the population.
    /// </summary>
    public class AnimalSpawner : NetworkBehaviour
    {
        [SerializeField] GameObject animalPrefab;
        [SerializeField] float respawnDelay = 8f;

        const float FighterCampBuffer = 16f;

        readonly List<Animal> _spawned = new List<Animal>();
        AnimalSpecies _species;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            _species = animalPrefab != null && animalPrefab.TryGetComponent(out Animal a) ? a.Species : null;
            if (_species == null)
            {
                Debug.LogError($"[AnimalSpawner] {name}: prefab or species missing.");
                return;
            }
            if (_species.population > 0) StartCoroutine(SpawnLoop());
        }

        IEnumerator SpawnLoop()
        {
            // Fill the woods right away, then trickle in replacements.
            while (AliveCount() < _species.population && TrySpawnGroup()) { }

            var wait = new WaitForSeconds(respawnDelay);
            while (IsSpawned)
            {
                yield return wait;
                if (AliveCount() < _species.population) TrySpawnGroup();
            }
        }

        int AliveCount()
        {
            _spawned.RemoveAll(a => a == null || !a.IsSpawned);
            int alive = 0;
            foreach (var a in _spawned) if (!a.IsDead.Value) alive++;
            return alive;
        }

        bool TrySpawnGroup()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                float dist = Random.Range(_species.minSpawnDistance, _species.maxSpawnDistance);
                Vector3 center = transform.position + new Vector3(dir.x, 0f, dir.y) * dist;
                // Fighters keep well clear of camp (safe zone + a buffer), so the area around
                // camp stays friendly and danger grows with distance.
                if (_species.Fights && SafeZone.Contains(center, FighterCampBuffer)) continue;
                if (!NavMesh.SamplePosition(center, out var hit, 4f, NavMesh.AllAreas)) continue;

                int count = Mathf.Min(Mathf.Max(1, _species.packSize), _species.population - AliveCount());
                for (int i = 0; i < count; i++)
                {
                    Vector2 jitter = Random.insideUnitCircle * (i == 0 ? 0f : 3f);
                    Vector3 p = hit.position + new Vector3(jitter.x, 0f, jitter.y);
                    if (i > 0 && NavMesh.SamplePosition(p, out var near, 3f, NavMesh.AllAreas)) p = near.position;
                    else p = hit.position;
                    Spawn(p);
                }
                return true;
            }
            Debug.LogWarning($"[AnimalSpawner] Couldn't place a {_species.displayName}. Is the NavMesh baked?");
            return false;
        }

        Animal Spawn(Vector3 position)
        {
            var go = Instantiate(animalPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            var animal = go.GetComponent<Animal>();
            _spawned.Add(animal);
            return animal;
        }
    }
}

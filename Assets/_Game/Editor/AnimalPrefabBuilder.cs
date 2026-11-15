using System;
using System.Collections.Generic;
using System.IO;
using FriendSlop.Animals;
using FriendSlop.Interaction;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Species data + procedural animal prefabs. Species assets are created once with first-pass
    /// stats (hand-tuning in the Inspector survives rebuilds); prefabs are regenerated every build
    /// from the species' body measurements so models and hitboxes always match the data.
    /// </summary>
    public static class AnimalBuilder
    {
        const string DataDir = "Assets/_Game/Data/Animals";
        const string PrefabDir = "Assets/_Game/Prefabs/Animals";
        const string LegacyRabbitAsset = "Assets/_Game/Data/Rabbit.asset";

        public struct Built
        {
            public AnimalSpecies Species;
            public GameObject Prefab;
        }

        // ------------------------------------------------------------------ species recipes

        static IEnumerable<(string file, Action<AnimalSpecies> setup)> Recipes()
        {
            yield return ("Rabbit", s =>
            {
                Id(s, "Rabbit", C(0.55f, 0.4f, 0.28f), C(0.95f, 0.95f, 0.92f), 12, 1.5f, 3f, 50f, Voice.Squeak, 1f);
                Body(s, BodyStyle.Hopper, 0.5f, 0.25f, 0.1f, 0.24f); s.bigEars = true; s.hopHeight = 0.18f; s.hopsPerMeter = 1.6f;
                Move(s, 1.6f, 7.5f, spook: 9f); Spawn(s, 8, 1, 22f, 70f);
            });
            yield return ("Duck", s =>
            {
                Id(s, "Duck", C(0.35f, 0.42f, 0.24f), C(0.98f, 0.6f, 0.12f), 8, 1f, 1.6f, 30f, Voice.Quack, 1.1f);
                Body(s, BodyStyle.Bird, 0.4f, 0.24f, 0.12f, 0.14f); s.hopHeight = 0.04f; s.hopsPerMeter = 3f;
                Move(s, 1.2f, 6f, spook: 8f); Spawn(s, 6, 2, 20f, 65f);
            });
            yield return ("Goose", s =>
            {
                Id(s, "Goose", C(0.93f, 0.93f, 0.9f), C(0.98f, 0.55f, 0.1f), 20, 4f, 6f, 70f, Voice.Honk, 1f);
                Body(s, BodyStyle.Bird, 0.55f, 0.34f, 0.22f, 0.16f); s.hopHeight = 0.05f; s.hopsPerMeter = 2.5f;
                Move(s, 1.3f, 6f); Fight(s, Temperament.Aggressive, aggro: 8f, lose: 25f, dmg: 4f, range: 1.0f, cd: 1.2f, knock: 5f, charge: 6.5f, flee: 0.3f);
                Spawn(s, 4, 2, 22f, 60f);
            });
            yield return ("Deer", s =>
            {
                Id(s, "Deer", C(0.6f, 0.42f, 0.25f), C(0.85f, 0.8f, 0.7f), 45, 50f, 90f, 120f, Voice.Bellow, 1.9f);
                Body(s, BodyStyle.Quadruped, 1.3f, 0.55f, 0.75f, 0.3f); s.antlers = true; s.hopHeight = 0.05f; s.hopsPerMeter = 0.8f;
                Move(s, 1.8f, 11f, spook: 16f); s.hearingRadius = 45f; Spawn(s, 5, 2, 28f, 75f);
            });
            yield return ("Boar", s =>
            {
                Id(s, "Boar", C(0.3f, 0.22f, 0.18f), C(0.95f, 0.92f, 0.85f), 35, 60f, 110f, 150f, Voice.Grunt, 1f);
                Body(s, BodyStyle.Quadruped, 1.1f, 0.55f, 0.35f, 0.35f); s.tusks = true; s.hopHeight = 0.03f; s.hopsPerMeter = 1.2f;
                Move(s, 1.5f, 8f); Fight(s, Temperament.Aggressive, aggro: 10f, lose: 30f, dmg: 18f, range: 1.3f, cd: 1.4f, knock: 9f, charge: 8.5f, flee: 0.2f);
                Spawn(s, 4, 1, 30f, 75f);
            });
            yield return ("Wolf", s =>
            {
                Id(s, "Wolf", C(0.45f, 0.45f, 0.48f), C(0.85f, 0.85f, 0.85f), 40, 30f, 50f, 100f, Voice.Growl, 1.15f);
                Body(s, BodyStyle.Quadruped, 1.0f, 0.45f, 0.5f, 0.28f); s.bigEars = true; s.hopHeight = 0.04f; s.hopsPerMeter = 1f;
                Move(s, 2f, 9f); Fight(s, Temperament.Predator, aggro: 26f, lose: 45f, dmg: 12f, range: 1.4f, cd: 1.1f, knock: 6f, charge: 9.5f, flee: 0.25f);
                Spawn(s, 6, 3, 45f, 75f);
            });
            yield return ("Moose", s =>
            {
                Id(s, "Moose", C(0.32f, 0.22f, 0.15f), C(0.75f, 0.65f, 0.5f), 120, 350f, 500f, 450f, Voice.Bellow, 0.8f);
                Body(s, BodyStyle.Quadruped, 2.1f, 1.0f, 1.0f, 0.5f); s.antlers = true; s.hopHeight = 0.04f; s.hopsPerMeter = 0.5f;
                Move(s, 1.3f, 8f); Fight(s, Temperament.Territorial, aggro: 9f, lose: 30f, dmg: 30f, range: 1.9f, cd: 1.8f, knock: 14f, charge: 8f, flee: 0f);
                Spawn(s, 2, 1, 40f, 75f);
            });
            yield return ("Bear", s =>
            {
                Id(s, "Bear", C(0.25f, 0.17f, 0.1f), C(0.4f, 0.3f, 0.2f), 200, 200f, 350f, 600f, Voice.Roar, 1f);
                Body(s, BodyStyle.Quadruped, 1.8f, 0.9f, 0.6f, 0.55f); s.bigEars = true; s.hopHeight = 0.05f; s.hopsPerMeter = 0.6f;
                Move(s, 1.2f, 8f); Fight(s, Temperament.Predator, aggro: 20f, lose: 40f, dmg: 35f, range: 1.8f, cd: 1.6f, knock: 12f, charge: 8f, flee: 0f);
                Spawn(s, 1, 1, 50f, 75f);
            });
            yield return ("BigDoug", s =>
            {
                Id(s, "Elk", C(0.55f, 0.38f, 0.22f), C(0.95f, 0.9f, 0.75f), 400, 600f, 800f, 2500f, Voice.Bellow, 0.6f);
                Body(s, BodyStyle.Quadruped, 1.9f, 0.9f, 1.1f, 0.5f); s.antlers = true; s.scale = 1.8f; s.hopHeight = 0.05f; s.hopsPerMeter = 0.35f;
                Move(s, 1.5f, 9f); Fight(s, Temperament.Predator, aggro: 60f, lose: 120f, dmg: 40f, range: 2.6f, cd: 2.0f, knock: 18f, charge: 10f, flee: 0f);
                Spawn(s, 0, 1, 0f, 0f);
                s.isBoss = true; s.bossTitle = "Big Doug the Elk"; s.bounty = 800;
            });
        }

        static Color C(float r, float g, float b) => new Color(r, g, b);

        static void Id(AnimalSpecies s, string name, Color fur, Color accent, int price, float minKg, float maxKg, float hp, Voice voice, float pitch)
        {
            s.displayName = name; s.furColor = fur; s.accentColor = accent; s.basePrice = price;
            s.minWeightKg = minKg; s.maxWeightKg = maxKg; s.maxHealth = hp; s.voice = voice; s.voicePitch = pitch;
        }

        static void Body(AnimalSpecies s, BodyStyle style, float length, float height, float leg, float head)
        {
            s.body = style; s.bodyLength = length; s.bodyHeight = height; s.legLength = leg; s.headSize = head;
        }

        static void Move(AnimalSpecies s, float wander, float flee, float spook = 9f)
        {
            s.wanderSpeed = wander; s.fleeSpeed = flee; s.spookRadius = spook;
        }

        static void Fight(AnimalSpecies s, Temperament t, float aggro, float lose, float dmg, float range, float cd, float knock, float charge, float flee)
        {
            s.temperament = t; s.aggroRadius = aggro; s.loseInterestRadius = lose; s.attackDamage = dmg; s.attackRange = range;
            s.attackCooldown = cd; s.knockback = knock; s.chargeSpeed = charge; s.fleeBelowHealth = flee;
        }

        static void Spawn(AnimalSpecies s, int population, int pack, float min, float max)
        {
            s.population = population; s.packSize = pack; s.minSpawnDistance = min; s.maxSpawnDistance = max;
        }

        // ------------------------------------------------------------------ build

        /// <summary>Create missing species assets and (re)build every animal prefab.</summary>
        public static List<Built> BuildAll(Func<string, Material> mat)
        {
            EnsureFolder(DataDir);
            EnsureFolder(PrefabDir);
            if (AssetDatabase.LoadAssetAtPath<AnimalSpecies>(LegacyRabbitAsset) != null)
                AssetDatabase.DeleteAsset(LegacyRabbitAsset); // moved to Data/Animals
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Rabbit.prefab") != null)
                AssetDatabase.DeleteAsset("Assets/_Game/Prefabs/Rabbit.prefab"); // moved to Prefabs/Animals

            var result = new List<Built>();
            foreach (var (file, setup) in Recipes())
            {
                string path = $"{DataDir}/{file}.asset";
                var species = AssetDatabase.LoadAssetAtPath<AnimalSpecies>(path);
                if (species == null)
                {
                    species = ScriptableObject.CreateInstance<AnimalSpecies>();
                    setup(species);
                    AssetDatabase.CreateAsset(species, path);
                }
                Migrate(species, file);
                result.Add(new Built { Species = species, Prefab = BuildPrefab(species, file, mat) });
            }
            return result;
        }

        /// <summary>v2: the 300 m terrain map. Spread populations into difficulty rings (calm near camp, dangerous at the edge).</summary>
        static void Migrate(AnimalSpecies s, string file)
        {
            if (s.dataVersion >= 2) return;
            (int pop, float min, float max) ring = file switch
            {
                "Rabbit" => (12, 16f, 85f),
                "Duck" => (9, 20f, 110f),
                "Goose" => (6, 25f, 95f),
                "Deer" => (8, 35f, 125f),
                "Boar" => (6, 55f, 125f),
                "Wolf" => (8, 85f, 140f),
                "Moose" => (3, 70f, 140f),
                "Bear" => (2, 100f, 142f),
                _ => (s.population, s.minSpawnDistance, s.maxSpawnDistance),
            };
            s.population = ring.pop; s.minSpawnDistance = ring.min; s.maxSpawnDistance = ring.max;
            s.dataVersion = 2;
            EditorUtility.SetDirty(s);
        }

        static GameObject BuildPrefab(AnimalSpecies s, string file, Func<string, Material> mat)
        {
            float k = s.scale;
            float L = s.bodyLength * k, H = s.bodyHeight * k, leg = s.legLength * k, head = s.headSize * k;

            var root = new GameObject(file);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(s.AverageWeight, 2f, 900f);
            rb.isKinematic = true;                              // alive: the NavMeshAgent drives it
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Max(0.25f, Mathf.Max(H, L * 0.35f) * 0.6f);
            agent.height = leg + H * 1.6f;
            agent.baseOffset = 0f;
            agent.angularSpeed = s.Fights ? 540f : 720f;
            agent.acceleration = s.Fights ? 40f : 30f;
            agent.stoppingDistance = 0.2f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            root.AddComponent<Carryable>();

            var fur = new List<Renderer>();
            var accent = new List<Renderer>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // Chunky cartoon body + personality rig; hitboxes follow its measurements.
            var built = CritterBuilder.Build(s, file, visual, mat("Animal"));
            AddHitboxes(root, built.bodyCenter, built.bodyRadius, built.bodyLength, built.headPos, built.headRadius);

            var animal = root.AddComponent<Animal>();
            var so = new SerializedObject(animal);
            so.FindProperty("species").objectReferenceValue = s;
            so.FindProperty("visualRoot").objectReferenceValue = visual;
            SetArray(so.FindProperty("furRenderers"), fur);
            SetArray(so.FindProperty("accentRenderers"), accent);
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{file}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static void AddHitboxes(GameObject root, Vector3 bodyCenter, float radius, float length, Vector3 headPos, float headRadius)
        {
            var bodyGo = new GameObject("BodyHitbox");
            bodyGo.transform.SetParent(root.transform, false);
            var cap = bodyGo.AddComponent<CapsuleCollider>();
            cap.direction = 2;
            cap.center = bodyCenter;
            cap.radius = Mathf.Max(0.08f, radius);
            cap.height = Mathf.Max(length, cap.radius * 2f);
            bodyGo.AddComponent<Hitbox>();

            var headGo = new GameObject("HeadHitbox");
            headGo.transform.SetParent(root.transform, false);
            var sph = headGo.AddComponent<SphereCollider>();
            sph.center = headPos;
            sph.radius = Mathf.Max(0.06f, headRadius);
            var hb = headGo.AddComponent<Hitbox>();
            var so = new SerializedObject(hb);
            so.FindProperty("isHead").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Renderer Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            return go.GetComponent<Renderer>();
        }

        static void SetArray(SerializedProperty prop, List<Renderer> values)
        {
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}


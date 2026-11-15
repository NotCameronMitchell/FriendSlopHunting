using System.Collections.Generic;
using System.IO;
using FriendSlop.Animals;
using FriendSlop.Game;
using FriendSlop.Interaction;
using FriendSlop.Net;
using FriendSlop.Net.Steam;
using FriendSlop.Player;
using FriendSlop.Weapons;
using FriendSlop.World;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Builds the hunting test map and every prefab/material/data asset it needs, from code, so the
    /// whole setup is reproducible and reviewable in git. Safe to re-run: generated assets are
    /// overwritten. Menu: FriendSlop ▸ Build Hunt Test Scene
    /// </summary>
    public static partial class HuntSceneBuilder
    {
        const string Root = "Assets/_Game";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir = Root + "/Scenes";
        const string MatDir = Root + "/Materials";
        const string DataDir = Root + "/Data";

        const string ScenePath = SceneDir + "/HuntTest.unity";
        const string NavMeshPath = SceneDir + "/HuntTest_NavMesh.asset";
        const string OldM0ScenePath = SceneDir + "/M0_NetTest.unity";

        const float MapHalfSize = 150f; // = TerrainBuilder.Half
        const float CampClearRadius = 14f;
        static readonly Vector3 SellZonePos = new Vector3(8f, 0f, 0f);
        static readonly Vector3 GunShopPos = new Vector3(-11f, 0f, -1f);
        static readonly Vector3 TotemPos = new Vector3(TerrainBuilder.TotemXZ.x, 0f, TerrainBuilder.TotemXZ.y);
        static readonly Vector3 CabinPos = new Vector3(3f, 0f, 16f);
        static readonly Vector3 CampfirePos = new Vector3(2.5f, 0f, 5.5f);
        static readonly Vector3 LodgePos = new Vector3(-14f, 0f, 14f);
        static readonly Vector3 GaragePos = new Vector3(-12f, 0f, -15f);
        static readonly List<Light> _nightLights = new List<Light>();

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("FriendSlop/Build Hunt Test Scene", priority = 0)]
        public static void BuildFromMenu() => Build(interactive: true);

        /// <param name="interactive">False when automated: saves open scenes instead of prompting.</param>
        public static void Build(bool interactive)
        {
            if (Application.isPlaying)
            {
                if (interactive) EditorUtility.DisplayDialog("FriendSlop", "Exit Play Mode first.", "OK");
                return;
            }
            if (interactive)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            }
            else
            {
                EditorSceneManager.SaveOpenScenes(); // never lose edits, never block on a dialog
            }

            foreach (var dir in new[] { PrefabDir, SceneDir, MatDir, DataDir }) EnsureFolder(dir);
            Mats.Clear();
            CreateMaterials();
            NatureArt.Setup();
            VillageArt.Setup();

            var catalog = WeaponCatalogBuilder.CreateOrUpdate();
            var pickup = BuildWeaponPickupPrefab(catalog);
            var hunterRig = HunterRigBuilder.Build();
            var player = BuildPlayerPrefab(catalog, pickup, hunterRig);
            var box = BuildBoxPrefab();
            var animals = AnimalBuilder.BuildAll(name => Mats[name]);
            var spawner = BuildSpawnerPrefab();
            GameObject bossPrefab = null, hogPrefab = null;
            foreach (var a in animals)
                if (a.Species.isBoss) { if (a.Species.bossTitle.Contains("Hamzilla")) hogPrefab = a.Prefab; else bossPrefab = a.Prefab; }
            var totem = BuildTotemPrefab(bossPrefab);
            _hogTotemPrefab = BuildTotemPrefab(hogPrefab, "HogTotem", hog: true, cost: 350);
            _lootCratePrefab = BuildLootCratePrefab();
            var tank = BuildTankPrefab();
            var gameState = BuildGameStatePrefab(catalog, animals, tank);

            var networked = new List<GameObject> { player, box, gameState, spawner, pickup, totem, tank, _hogTotemPrefab, _lootCratePrefab };
            foreach (var a in animals) networked.Add(a.Prefab);
            var prefabList = RegisterNetworkPrefabs(networked.ToArray());

            BuildScene(player, box, gameState, spawner, totem, animals, prefabList, catalog);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OldM0ScenePath) != null)
                AssetDatabase.DeleteAsset(OldM0ScenePath);

            AssetDatabase.SaveAssets();
            Debug.Log($"[FriendSlop] Built {ScenePath}. Press Play → Host. Shoot rabbits, carry them to the yellow pad by the trader.");
        }

        // =========================================================== Data / materials

        static void CreateMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Mat("Grass", lit, new Color(0.42f, 0.58f, 0.24f));
            Mat("Bark", lit, new Color(0.38f, 0.26f, 0.16f));
            Mat("Leaves", lit, new Color(0.18f, 0.42f, 0.2f));
            Mat("Rock", lit, new Color(0.5f, 0.5f, 0.52f));
            Mat("SellPad", lit, new Color(0.95f, 0.78f, 0.2f));
            Mat("Trader", lit, new Color(0.25f, 0.35f, 0.75f));
            Mat("Gun", lit, new Color(0.15f, 0.13f, 0.12f));
            Mat("Tail", lit, new Color(0.95f, 0.95f, 0.92f));
            Mat("Tracer", unlit, new Color(1f, 0.9f, 0.4f));
            Mat("Plank", lit, new Color(0.58f, 0.42f, 0.27f));
            Mat("Roof", lit, new Color(0.45f, 0.18f, 0.14f));
            Mat("Gunsmith", lit, new Color(0.7f, 0.3f, 0.2f));
            Mat("Animal", lit, Color.white); // tinted per species at runtime
            Mat("Stone", lit, new Color(0.42f, 0.42f, 0.46f));
            Mat("TankOlive", lit, new Color(0.36f, 0.42f, 0.22f));
            Mat("TankOliveDark", lit, new Color(0.24f, 0.28f, 0.15f));
            Mat("Glow", unlit, new Color(1f, 0.45f, 0.15f));
            Mat("FlameYellow", unlit, new Color(1f, 0.78f, 0.22f));
            Mat("FlameCore", unlit, new Color(1f, 0.96f, 0.72f));
            Mat("Coal", unlit, new Color(0.85f, 0.22f, 0.06f));
            Mat("Smoke", lit, new Color(0.5f, 0.5f, 0.53f));
            Mat("Ash", lit, new Color(0.16f, 0.15f, 0.15f));
            Mat("Mud", lit, new Color(0.3f, 0.21f, 0.14f));
            Mat("MudWet", lit, new Color(0.2f, 0.14f, 0.1f));
            Mat("HogPink", lit, new Color(0.9f, 0.6f, 0.62f));
            Mat("Canvas", lit, new Color(0.74f, 0.68f, 0.52f));
            Mat("BedrollRed", lit, new Color(0.62f, 0.2f, 0.18f));
            var worldText = Shader.Find("FriendSlop/WorldText");
            if (worldText != null) Mat("WorldText", worldText, Color.white);
            else Debug.LogWarning("[FriendSlop] WorldText shader missing; signs will draw through walls.");
        }

        static Material Mat(string name, Shader shader, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
            Mats[name] = mat;
            return mat;
        }

        // =========================================================== Prefabs

        static GameObject BuildWeaponPickupPrefab(WeaponCatalog catalog)
        {
            var root = new GameObject("WeaponPickup");
            var col = root.AddComponent<BoxCollider>(); // resized at runtime to fit the weapon model
            col.size = new Vector3(0.15f, 0.15f, 0.6f);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1.5f; // light, so even a pistol shot sends it skidding
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.angularDamping = 0.5f;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            root.AddComponent<NetworkRigidbody>();
            root.AddComponent<NetworkPhysicsBox>(); // kick it, shoot it, blow it up: same as the camp boxes

            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);

            var pickup = root.AddComponent<WeaponPickup>();
            var so = new SerializedObject(pickup);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("modelRoot").objectReferenceValue = model;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, $"{PrefabDir}/WeaponPickup.prefab");
        }

        static GameObject BuildPlayerPrefab(WeaponCatalog catalog, GameObject pickupPrefab, GameObject hunterRig)
        {
            var root = new GameObject("Player");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.04f;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // co-op: you own your own body
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;

            // Chunky KayKit hunter; PlayerAppearance switches its parts to the player's outfit.
            var body = (GameObject)PrefabUtility.InstantiatePrefab(hunterRig, root.transform);
            body.name = "Hunter";
            body.transform.localScale = Vector3.one * 0.82f;

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);

            var cam = pivot.gameObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            cam.depth = 10f;
            cam.enabled = false; // only the owner's camera turns on
            var listener = pivot.gameObject.AddComponent<AudioListener>();
            listener.enabled = false;

            // Weapon models are built at runtime under this holder by PlayerWeapons.
            var gunHolder = new GameObject("GunHolder").transform;
            gunHolder.SetParent(pivot, false);
            gunHolder.localPosition = new Vector3(0.24f, -0.24f, 0.45f);

            var hso = new SerializedObject(body.GetComponent<HunterBody>());
            hso.FindProperty("aimTarget").objectReferenceValue = gunHolder;
            hso.ApplyModifiedPropertiesWithoutUndo();

            var hold = new GameObject("HoldPoint").transform;
            hold.SetParent(pivot, false);
            hold.localPosition = new Vector3(0f, -0.25f, 1.3f);

            var np = root.AddComponent<NetworkPlayer>();
            var so = new SerializedObject(np);
            so.FindProperty("cameraPivot").objectReferenceValue = pivot;
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("audioListener").objectReferenceValue = listener;
            so.FindProperty("holdPoint").objectReferenceValue = hold;
            so.FindProperty("bodyRenderers").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            var weapons = root.AddComponent<PlayerWeapons>();
            so = new SerializedObject(weapons);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("gunHolder").objectReferenceValue = gunHolder;
            so.FindProperty("tracerMaterial").objectReferenceValue = Mats["Tracer"];
            so.FindProperty("pickupPrefab").objectReferenceValue = pickupPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<PlayerHealth>();
            root.AddComponent<PlayerProgress>();
            root.AddComponent<PlayerUpgrades>();
            root.AddComponent<PlayerItems>();
            var look = root.AddComponent<PlayerAppearance>();
            var lso = new SerializedObject(look);
            lso.FindProperty("nameTagMaterial").objectReferenceValue = Mats.TryGetValue("WorldText", out var tagMat) ? tagMat : null;
            lso.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, $"{PrefabDir}/Player.prefab");
        }

        static GameObject BuildBoxPrefab()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "PhysicsBox";
            root.transform.localScale = Vector3.one * 1.2f;

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 15f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server; // host simulates physics
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            root.AddComponent<NetworkRigidbody>();
            root.AddComponent<NetworkPhysicsBox>();

            return SavePrefab(root, $"{PrefabDir}/PhysicsBox.prefab");
        }

        static NetworkPrefabsList RegisterNetworkPrefabs(params GameObject[] prefabs)
        {
            // Use NGO's default list so its auto-registration and ours agree on a single asset.
            string path = NetworkPrefabProcessor.DefaultNetworkPrefabsPath;
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (list == null)
            {
                EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, path);
            }
            // Drop entries for prefabs that were deleted or moved (e.g. the old Rabbit prefab).
            var stale = new List<NetworkPrefab>();
            foreach (var entry in list.PrefabList) if (entry == null || entry.Prefab == null) stale.Add(entry);
            foreach (var entry in stale) list.Remove(entry);

            foreach (var prefab in prefabs)
                if (!list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            return list;
        }

        // =========================================================== Scene

        static GameObject BuildGameStatePrefab(WeaponCatalog catalog, List<AnimalBuilder.Built> animals, GameObject tank)
        {
            var go = new GameObject("GameState");
            go.AddComponent<NetworkObject>();
            var state = go.AddComponent<GameState>();
            var so = new SerializedObject(state);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("tankPrefab").objectReferenceValue = tank;
            so.FindProperty("garagePosition").vector3Value = GaragePos;
            so.FindProperty("garageYaw").floatValue = Quaternion.LookRotation(-new Vector3(GaragePos.x, 0f, GaragePos.z).normalized).eulerAngles.y;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Shared clock + night happenings (ghost goose nights, the Shadow Stag).
            go.AddComponent<DayNightCycle>();
            var events = go.AddComponent<NightEvents>();
            var eso = new SerializedObject(events);
            foreach (var a in animals)
            {
                if (a.Species.ghostly) eso.FindProperty("ghostGoosePrefab").objectReferenceValue = a.Prefab;
                if (a.Species.shadowy) eso.FindProperty("shadowStagPrefab").objectReferenceValue = a.Prefab;
            }
            eso.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, $"{PrefabDir}/GameState.prefab");
        }

        /// <summary>Generic spawner; each scene instance overrides which animal prefab it spawns.</summary>
        static GameObject BuildSpawnerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/RabbitSpawner.prefab") != null)
                AssetDatabase.DeleteAsset($"{PrefabDir}/RabbitSpawner.prefab"); // replaced by AnimalSpawner
            var go = new GameObject("AnimalSpawner");
            go.AddComponent<NetworkObject>();
            go.AddComponent<AnimalSpawner>();
            return SavePrefab(go, $"{PrefabDir}/AnimalSpawner.prefab");
        }

        /// <summary>Standing stone with a glowing skull; pay to summon the boss.</summary>
        static GameObject BuildTotemPrefab(GameObject bossPrefab, string name = "BossTotem", bool hog = false, int cost = 250)
        {
            var root = new GameObject(name);
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(1.4f, 3.4f, 1.4f);
            col.center = new Vector3(0f, 1.7f, 0f);
            root.AddComponent<NetworkObject>();
            var totem = root.AddComponent<BossTotem>();
            var so = new SerializedObject(totem);
            so.FindProperty("bossPrefab").objectReferenceValue = bossPrefab;
            so.FindProperty("summonCost").intValue = cost;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (hog) { HogTotemVisuals(root.transform); return SavePrefab(root, $"{PrefabDir}/{name}.prefab"); }
            Visual(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(2.2f, 0.5f, 2.2f), "Stone");
            Visual(PrimitiveType.Cube, "Pillar", root.transform, new Vector3(0f, 1.8f, 0f), new Vector3(1.1f, 2.8f, 1.1f), "Stone");
            Visual(PrimitiveType.Sphere, "Skull", root.transform, new Vector3(0f, 3.5f, 0f), Vector3.one * 0.9f, "Tail");
            for (int e = -1; e <= 1; e += 2)
            {
                Visual(PrimitiveType.Cube, "Antler", root.transform, new Vector3(e * 0.7f, 4.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f), "Tail")
                    .transform.localRotation = Quaternion.Euler(0f, 0f, -e * 35f);
                Visual(PrimitiveType.Sphere, "Eye", root.transform, new Vector3(e * 0.18f, 3.55f, 0.4f), Vector3.one * 0.16f, "Glow");
            }

            return SavePrefab(root, $"{PrefabDir}/{name}.prefab");
        }

        static void BuildScene(GameObject playerPrefab, GameObject boxPrefab, GameObject gameStatePrefab,
                               GameObject spawnerPrefab, GameObject totemPrefab, List<AnimalBuilder.Built> animals,
                               NetworkPrefabsList prefabList, WeaponCatalog catalog)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var menuCam = Camera.main;
            if (menuCam != null)
            {
                menuCam.transform.SetPositionAndRotation(new Vector3(-3f, 6.5f, -11f), Quaternion.Euler(22f, 22f, 0f));
                menuCam.clearFlags = CameraClearFlags.Skybox;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0055f;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.86f);

            // ---------- Static scenery (baked into the navmesh) ----------
            var world = new GameObject("World").transform;

            // Hills, mountain border, lake, pond and streams (camp stays flat at y = 0).
            var terrain = TerrainBuilder.Build(world);
            float GroundY(Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;
            float FootY(Vector3 p, float r)
            {
                float y = GroundY(p);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    y = Mathf.Min(y, GroundY(p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)));
                }
                return y;
            }
            void Settle(Transform t, float embedFrac)
            {
                var rends = t.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return;
                var b = rends[0].bounds;
                for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
                // Lowest ground under the footprint (corners pulled in a bit: round shapes don't fill their box).
                Vector3 c = b.center, e = b.extents * 0.7f;
                float g = GroundY(c);
                g = Mathf.Min(g, GroundY(c + new Vector3(e.x, 0f, e.z)), GroundY(c + new Vector3(-e.x, 0f, e.z)));
                g = Mathf.Min(g, GroundY(c + new Vector3(e.x, 0f, -e.z)), GroundY(c + new Vector3(-e.x, 0f, -e.z)));
                float embed = Mathf.Clamp(b.size.y * embedFrac, 0.02f, 0.3f);
                t.position += Vector3.up * (g - embed - b.min.y);
            }
            float Steep(Vector3 p) => terrain.terrainData.GetSteepness(
                Mathf.Clamp01((p.x + MapHalfSize) / (MapHalfSize * 2f)), Mathf.Clamp01((p.z + MapHalfSize) / (MapHalfSize * 2f)));
            bool Wild(Vector3 p, float waterMargin, float maxSteep) =>
                !TerrainBuilder.NearWater(p, waterMargin) && Steep(p) <= maxSteep;

            var rng = new System.Random(1337); // same forest every rebuild
            var trees = new GameObject("Trees").transform;
            trees.SetParent(world, false);
            for (int i = 0, placed = 0; i < 4000 && placed < 520; i++)
            {
                Vector3 p = RandomInMap(rng, MapHalfSize - 4f);
                if (p.magnitude < CampClearRadius || NearGunShop(p) || NearTotem(p) || NearCabin(p) || NearLodge(p) || NearGarage(p) || NearLandmark(p, 4f) || !Wild(p, 3f, 30f)) continue;
                p.y = FootY(p, 0.7f) - 0.08f; // lowest ground around the trunk, so no side floats
                if (p.y > 22f) continue; // bare peaks
                NatureArt.Tree(trees, p, rng);
                placed++;
            }

            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(world, false);
            for (int i = 0, placed = 0; i < 1200 && placed < 130; i++)
            {
                Vector3 p = RandomInMap(rng, MapHalfSize - 4f);
                if (p.magnitude < CampClearRadius - 4f || NearGunShop(p) || NearTotem(p) || NearCabin(p) || NearLodge(p) || NearGarage(p) || NearLandmark(p, 2f) || (p - SellZonePos).magnitude < 9f || !Wild(p, 1.5f, 45f)) continue;
                p.y = GroundY(p) - 0.25f;
                NatureArt.Rock(rocks, p, rng);
                placed++;
            }

            var decor = new GameObject("Decor").transform;
            decor.SetParent(world, false);
            NatureArt.Decor(decor, rng, MapHalfSize - 3f, 1500,
                p => p.magnitude < CampClearRadius + 1f || NearGunShop(p) || NearTotem(p) || NearCabin(p) || NearLodge(p) || NearGarage(p) || NearLandmark(p, 0f) || (p - SellZonePos).magnitude < 4f || !Wild(p, 0.8f, 32f),
                GroundY);
            // Sit every rock and plant flush: drop each one so its lowest point is at the lowest ground under its footprint.
            foreach (Transform t in rocks) Settle(t, 0.12f);
            foreach (Transform t in decor) Settle(t, 0.06f);
            NatureArt.Grass(world, MapHalfSize - 1f, new[] { new Vector3(0f, 0f, 7f), new Vector3(GunShopPos.x, GunShopPos.z, 6f), new Vector3(SellZonePos.x, SellZonePos.z, 3.5f), new Vector3(TotemPos.x, TotemPos.z, 2.5f), new Vector3(CabinPos.x, CabinPos.z, 7f), new Vector3(CampfirePos.x, CampfirePos.z, 2.5f), new Vector3(LodgePos.x, LodgePos.z, 8f), new Vector3(GaragePos.x, GaragePos.z, 6f), new Vector3(TerrainBuilder.HogWallow.x, TerrainBuilder.HogWallow.z, 10f), new Vector3(TerrainBuilder.RangerTower.x, TerrainBuilder.RangerTower.z, 6f), new Vector3(TerrainBuilder.OldCamp.x, TerrainBuilder.OldCamp.z, 7f) });
            BuildLandmarks(world, scene, GroundY); // before the navmesh bake so animals path around them

            // Invisible walls at the map edge (behind the mountains).
            var bounds = new GameObject("Bounds").transform;
            bounds.SetParent(world, false);
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject($"Wall_{i}");
                wall.transform.SetParent(bounds, false);
                var col = wall.AddComponent<BoxCollider>();
                bool alongX = i < 2;
                float side = i % 2 == 0 ? 1f : -1f;
                wall.transform.position = alongX ? new Vector3(0f, 40f, side * MapHalfSize) : new Vector3(side * MapHalfSize, 40f, 0f);
                col.size = alongX ? new Vector3(MapHalfSize * 2f, 100f, 1f) : new Vector3(1f, 100f, MapHalfSize * 2f);
                wall.isStatic = true;
            }

            BuildGunShop(world, catalog);
            _nightLights.Clear();
            _nightLights.Add(BuildCabin(world));
            _nightLights.Add(BuildCampfire(world));
            BuildLuckyLodge(world);
            BuildGarage(world);

            // Day/night lighting rides on the scene's sun.
            Light sunLight = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sunLight = l; break; }
            if (sunLight != null)
            {
                sunLight.name = "Sun";
                sunLight.shadows = LightShadows.Soft;
                var visuals = sunLight.gameObject.AddComponent<DayNightVisuals>();
                var vso = new SerializedObject(visuals);
                vso.FindProperty("sun").objectReferenceValue = sunLight;
                vso.FindProperty("skybox").objectReferenceValue = RenderSettings.skybox;
                vso.FindProperty("starShader").objectReferenceValue = Shader.Find("FriendSlop/Stars");
                SetArray(vso.FindProperty("nightLights"), _nightLights.ToArray());
                vso.ApplyModifiedPropertiesWithoutUndo();
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

            // ---------- NavMesh (bake now, from physics colliders of the static world) ----------
            var navGo = new GameObject("NavMesh");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            EditorUtility.SetDirty(surface);

            // ---------- Camp: trader + sell zone ----------
            var camp = new GameObject("Camp").transform;
            camp.gameObject.AddComponent<SafeZone>(); // fighters stay away from camp

            // Drop-off deck: kit plank tiles with a timber rim (5 m square, matches the sell trigger).
            var deck = new GameObject("SellDeck").transform;
            deck.SetParent(camp, false);
            deck.position = SellZonePos;
            deck.rotation = Quaternion.LookRotation(-SellZonePos.normalized);
            const float tile = 5f / 3f;
            for (int ix = 0; ix < 3; ix++)
                for (int iz = 0; iz < 3; iz++)
                    VillageArt.Piece(ix == 1 && iz == 1 ? "Floor_WoodDark" : "Floor_WoodLight", deck, new Vector3((ix - 1) * tile, 0.09f, (iz - 1) * tile), (ix + iz) % 2 * 90f, solid: false, scale: tile / 2f);
            Solid(deck, "DeckBase", new Vector3(0f, 0.04f, 0f), new Vector3(5f, 0.08f, 5f), "Plank");
            for (int s = -1; s <= 1; s += 2)
            {
                Visual(PrimitiveType.Cube, "Rim", deck, new Vector3(s * 2.55f, 0.1f, 0f), new Vector3(0.18f, 0.2f, 5.3f), "Bark");
                Visual(PrimitiveType.Cube, "Rim", deck, new Vector3(0f, 0.1f, s * 2.55f), new Vector3(5.3f, 0.2f, 0.18f), "Bark");
            }
            var sell = new GameObject("SellZone");
            sell.transform.SetParent(camp, false);
            sell.transform.position = SellZonePos + Vector3.up * 1.5f;
            var trigger = sell.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5f, 3f, 5f);
            sell.AddComponent<SellZone>();

            var trader = new GameObject("Trader").transform;
            trader.SetParent(camp, false);
            trader.position = SellZonePos + new Vector3(3.6f, 0f, 0f);
            trader.rotation = Quaternion.LookRotation(-SellZonePos.normalized);
            Npc(trader, "outfit_barbarian", "head_barbarian", "bearhat", "none", 7);
            // Market stall over the trader, with crates and a cart.
            for (int s = -1; s <= 1; s += 2)
            {
                VillageArt.Piece("Corner_Exterior_Wood", trader, new Vector3(s * 1.2f, 0f, 0.9f), 0f, scale: 0.85f);
                VillageArt.Piece("Corner_Exterior_Wood", trader, new Vector3(s * 1.2f, 0f, -1.1f), 0f, scale: 0.85f);
                VillageArt.Piece("Roof_Wooden_2x1", trader, new Vector3(s * 1.13f, 2.55f, -1.2f), 0f, solid: false);
            }
            VillageArt.Piece("Prop_Crate", trader, new Vector3(2.0f, 0f, -0.6f), 10f);
            VillageArt.Piece("Prop_Crate", trader, new Vector3(2.0f, 1.06f, -0.6f), 40f, scale: 0.75f);
            VillageArt.Piece("Prop_Crate", trader, new Vector3(-2.0f, 0f, -0.2f), -15f, scale: 0.85f);
            VillageArt.Piece("Prop_Wagon", trader, new Vector3(-2.2f, 0f, -3.4f), 80f);

            // A rustic fence ring around camp, with gaps to walk through.
            var fences = new GameObject("CampFence").transform;
            fences.SetParent(world, false);
            const float fenceR = 24f;
            int segs = Mathf.RoundToInt(2f * Mathf.PI * fenceR / 2.06f);
            for (int i = 0; i < segs; i++)
            {
                if (i % 5 >= 3) continue; // three panels, then a gap
                float a = i * Mathf.PI * 2f / segs;
                var p = new Vector3(Mathf.Cos(a) * fenceR, 0f, Mathf.Sin(a) * fenceR);
                if (NearGunShop(p) || NearCabin(p) || NearLodge(p) || NearGarage(p) || Mathf.Abs(p.x) < 4f && p.z < 0f) continue; // keep the totem trail open
                p.y = GroundY(p) - 0.03f;
                var fence = VillageArt.Piece("Prop_WoodenFence_Single", fences, p, -a * Mathf.Rad2Deg + 90f);
                if (fence) fence.transform.position = p;
            }

            var sign = new GameObject("Sign");
            sign.transform.SetParent(camp, false);
            sign.transform.position = SellZonePos + new Vector3(0f, 3.4f, 0f);
            sign.transform.rotation = Quaternion.LookRotation(SellZonePos.normalized); // readable from camp center
            var text = sign.AddComponent<TextMesh>();
            text.text = "TRADER\ndrop carcasses on the pad";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.12f;
            text.fontSize = 48;
            text.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                sign.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                AddWorldText(sign);
            }

            // ---------- Networked scene objects ----------
            // Placed as prefab instances: NGO only treats a NetworkObject as "in-scene placed" reliably
            // when it comes from a prefab (bare new GameObject + AddComponent gets rejected at host start).
            PrefabUtility.InstantiatePrefab(gameStatePrefab, scene);

            // One spawner per (non-boss) species; population/packs/distances come from the species asset.
            var spawners = new GameObject("Spawners").transform;
            foreach (var a in animals)
            {
                if (a.Species.isBoss || a.Species.population <= 0) continue;
                var sp = (GameObject)PrefabUtility.InstantiatePrefab(spawnerPrefab, scene);
                sp.name = $"Spawner_{a.Species.displayName}";
                var sso = new SerializedObject(sp.GetComponent<AnimalSpawner>());
                sso.FindProperty("animalPrefab").objectReferenceValue = a.Prefab;
                sso.ApplyModifiedPropertiesWithoutUndo();
                sp.transform.SetParent(spawners, true);
            }

            // The legendary totem at the north edge of the woods.
            var totem = (GameObject)PrefabUtility.InstantiatePrefab(totemPrefab, scene);
            totem.transform.SetPositionAndRotation(new Vector3(TotemPos.x, GroundY(TotemPos), TotemPos.z), Quaternion.LookRotation(-new Vector3(TotemPos.x, 0f, TotemPos.z).normalized));

            var netGo = new GameObject("NetworkManager");
            var nm = netGo.AddComponent<NetworkManager>();
            var utp = netGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                PlayerPrefab = playerPrefab,
                ConnectionApproval = true,
                TickRate = 30,
            };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabList };
            netGo.AddComponent<SteamService>();
            var steamTransport = netGo.AddComponent<SteamTransport>();
            var bootstrap = netGo.AddComponent<NetBootstrap>();
            var bso = new SerializedObject(bootstrap);
            bso.FindProperty("directTransport").objectReferenceValue = utp;
            bso.FindProperty("steamTransport").objectReferenceValue = steamTransport;
            bso.ApplyModifiedPropertiesWithoutUndo();
            netGo.AddComponent<SteamLobby>();
            netGo.AddComponent<ConnectionMenu>();
            EditorUtility.SetDirty(nm);

            var hud = new GameObject("HUD");
            hud.AddComponent<GameHud>();
            hud.AddComponent<ShopMenu>();
            hud.AddComponent<MapOverlay>();
            hud.AddComponent<InventoryMenu>();
            hud.AddComponent<OptionsMenu>();
            hud.AddComponent<FriendSlop.Profile.CustomizeMenu>();
            var preview = hud.AddComponent<FriendSlop.Profile.CharacterPreview>();
            var pso = new SerializedObject(preview);
            pso.FindProperty("hunterPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/HunterRig.prefab");
            pso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        /// <summary>
        /// Open-front hut west of camp: counter, gunsmith (press E to shop), and a display wall
        /// with every weapon and its price. Built before the navmesh bake so animals path around it.
        /// </summary>
        static void BuildGunShop(Transform parent, WeaponCatalog catalog)
        {
            var shop = new GameObject("GunShop").transform;
            shop.SetParent(parent, false);
            shop.position = GunShopPos;

            // Shell from the village kit. The shell's +Z is the shop's open front (+X, toward camp).
            var shell = new GameObject("Shell").transform;
            shell.SetParent(shop, false);
            shell.localRotation = Quaternion.Euler(0f, 90f, 0f);
            const string brick = "Wall_UnevenBrick_Straight", brickWin = "Wall_UnevenBrick_Window_Wide_Round";
            VillageArt.Floor(shell, 4, 3, "Floor_WoodDark");
            VillageArt.Wall(shell, 4, 3, VillageArt.Side.Back, brick, brick, brick, brick); // the weapon rack hangs here
            VillageArt.Wall(shell, 4, 3, VillageArt.Side.Left, brick, brickWin, brick);
            VillageArt.Wall(shell, 4, 3, VillageArt.Side.Right, brick, brickWin, brick);
            VillageArt.Corners(shell, 4, 3, "Corner_Exterior_Brick");
            VillageArt.Roof(shell, 4, 3, ridgeAlongX: true);
            // Counter: a row of crates across the open front.
            for (int i = 0; i < 4; i++)
                VillageArt.Piece("Prop_Crate", shop, new Vector3(1.6f, 0.06f, -1.62f + i * 1.08f), i * 7f);
            VillageArt.Piece("Prop_Crate", shop, new Vector3(3.6f, 0f, 3.4f), 20f);
            VillageArt.Piece("Prop_Crate", shop, new Vector3(3.6f, 1.06f, 3.4f), 50f, scale: 0.7f);
            VillageArt.Piece("Prop_Chimney", shop, new Vector3(-2.2f, 4.2f, -2.4f), 0f, solid: false);

            // Gunsmith behind the counter
            var smith = new GameObject("Gunsmith").transform;
            smith.SetParent(shop, false);
            smith.localPosition = new Vector3(0.9f, 0.06f, 3.0f); // off to one side so the rack stays visible
            smith.localRotation = Quaternion.LookRotation(Vector3.right);
            Npc(smith, "outfit_knight", "head_knight", "cowboy", "scarf", 9);
            smith.gameObject.AddComponent<GunShop>();

            // Display wall
            var rack = new GameObject("WeaponRack");
            rack.transform.SetParent(shop, false);
            rack.transform.localPosition = new Vector3(-2.5f, 1.0f, 0f);
            rack.transform.localRotation = Quaternion.LookRotation(Vector3.right); // faces customers
            var r = rack.AddComponent<WeaponRack>();
            var rso = new SerializedObject(r);
            rso.FindProperty("textMaterial").objectReferenceValue = Mats.TryGetValue("WorldText", out var wtm) ? wtm : null;
            rso.ApplyModifiedPropertiesWithoutUndo();
            var so = new SerializedObject(r);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Sign hanging out front, readable from camp
            var sign = new GameObject("Sign");
            sign.transform.SetParent(shop, false);
            sign.transform.localPosition = new Vector3(4.3f, 3.2f, 0f);
            sign.transform.rotation = Quaternion.LookRotation(Vector3.left);
            var text = sign.AddComponent<TextMesh>();
            text.text = "GUN SHOP";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.12f;
            text.fontSize = 64;
            text.color = new Color(1f, 0.85f, 0.4f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                sign.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                AddWorldText(sign);
            }
        }

        /// <summary>Swap a TextMesh to the depth-tested, one-sided world text material.</summary>
        static void AddWorldText(GameObject textGo)
        {
            if (!Mats.TryGetValue("WorldText", out var m)) return;
            var wt = textGo.AddComponent<WorldText>();
            var so = new SerializedObject(wt);
            so.FindProperty("baseMaterial").objectReferenceValue = m;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool NearCabin(Vector3 p) => (new Vector3(p.x, 0f, p.z) - CabinPos).magnitude < 9f;
        static bool NearLodge(Vector3 p) => (new Vector3(p.x, 0f, p.z) - LodgePos).magnitude < 10f;
        static bool NearGarage(Vector3 p) => (new Vector3(p.x, 0f, p.z) - GaragePos).magnitude < 8f;

        static bool NearTotem(Vector3 p) => (new Vector3(p.x, 0f, p.z) - TotemPos).magnitude < 6f;

        static bool NearGunShop(Vector3 p) =>
            Mathf.Abs(p.x - GunShopPos.x) < 7f && Mathf.Abs(p.z - GunShopPos.z) < 7.5f;

        static void Solid(Transform parent, string name, Vector3 localPos, Vector3 scale, string mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mats[mat];
            go.isStatic = true;
        }

        /// <summary>A camp NPC: the hunter rig in a fixed outfit, with a solid body to bump into and aim at.</summary>
        static void Npc(Transform parent, string outfit, string head, string hat, string acc, int color)
        {
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/HunterRig.prefab"), parent);
            rig.name = "Body";
            rig.transform.localScale = Vector3.one * 0.9f;
            var look = parent.gameObject.AddComponent<FriendSlop.Player.NpcLook>();
            look.outfit = outfit; look.head = head; look.hat = hat; look.accessory = acc; look.colorIndex = color;
            var col = parent.gameObject.AddComponent<CapsuleCollider>();
            col.height = 1.9f; col.radius = 0.45f; col.center = new Vector3(0f, 0.95f, 0f);
        }

        static Vector3 RandomInMap(System.Random rng, float half) =>
            new Vector3(((float)rng.NextDouble() * 2f - 1f) * half, 0f, ((float)rng.NextDouble() * 2f - 1f) * half);

        // =========================================================== Tank & garage

        /// <summary>A chunky toy tank: hull on treads, rotating turret with a long barrel, four seats.</summary>
        static GameObject BuildTankPrefab()
        {
            var root = new GameObject("Tank");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 4000f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var hull = root.AddComponent<BoxCollider>();
            hull.size = new Vector3(3.2f, 1.2f, 5.2f);
            hull.center = new Vector3(0f, 0.85f, 0f);
            // Frictionless hull: the controller does traction and braking. Must be a saved asset, or the
            // prefab silently drops it and default friction pins the tank in place.
            const string treadPath = "Assets/_Game/Data/TankTread.physicMaterial";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(treadPath);
            if (mat == null) { mat = new PhysicsMaterial("TankTread"); AssetDatabase.CreateAsset(mat, treadPath); }
            mat.dynamicFriction = 0f; mat.staticFriction = 0f; mat.bounciness = 0f;
            mat.frictionCombine = PhysicsMaterialCombine.Minimum; mat.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(mat);
            hull.sharedMaterial = mat;
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // the driver simulates it
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            // No NetworkRigidbody: Tank toggles kinematic itself (it was pinning the driven body).

            var t = root.transform;
            Visual(PrimitiveType.Cube, "Hull", t, new Vector3(0f, 0.95f, 0f), new Vector3(2.6f, 0.75f, 4.6f), "TankOlive");
            Visual(PrimitiveType.Cube, "Glacis", t, new Vector3(0f, 1.05f, 2.35f), new Vector3(2.5f, 0.5f, 0.6f), "TankOlive").transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            Visual(PrimitiveType.Cube, "Deck", t, new Vector3(0f, 1.35f, -0.4f), new Vector3(2.3f, 0.08f, 3.2f), "TankOliveDark");
            for (int s = -1; s <= 1; s += 2)
            {
                Visual(PrimitiveType.Cube, "Tread", t, new Vector3(s * 1.45f, 0.55f, 0f), new Vector3(0.65f, 0.85f, 5.2f), "Gun");
                Visual(PrimitiveType.Cube, "Fender", t, new Vector3(s * 1.45f, 1.02f, 0f), new Vector3(0.75f, 0.08f, 5.3f), "TankOliveDark");
                for (int w = 0; w < 5; w++)
                    Visual(PrimitiveType.Cylinder, "Wheel", t, new Vector3(s * 1.8f, 0.45f, -1.9f + w * 0.95f), new Vector3(0.62f, 0.06f, 0.62f), "Stone")
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Visual(PrimitiveType.Cube, "Crate", t, new Vector3(s * 0.85f, 1.55f, -2.05f), new Vector3(0.6f, 0.4f, 0.45f), "Plank");
            }

            var turret = new GameObject("Turret").transform;
            turret.SetParent(t, false);
            turret.localPosition = new Vector3(0f, 1.4f, 0.35f);
            Visual(PrimitiveType.Cylinder, "Ring", turret, new Vector3(0f, 0.1f, 0f), new Vector3(1.9f, 0.12f, 1.9f), "TankOliveDark");
            Visual(PrimitiveType.Sphere, "Dome", turret, new Vector3(0f, 0.35f, 0f), new Vector3(1.8f, 0.85f, 2.0f), "TankOlive");
            Visual(PrimitiveType.Cylinder, "Hatch", turret, new Vector3(-0.35f, 0.78f, -0.25f), new Vector3(0.55f, 0.06f, 0.55f), "TankOliveDark");
            for (int s = -1; s <= 1; s += 2)
                Visual(PrimitiveType.Cylinder, "Star", turret, new Vector3(s * 0.88f, 0.38f, 0f), new Vector3(0.42f, 0.02f, 0.42f), "Tail")
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(turret, false);
            barrel.localPosition = new Vector3(0f, 0.38f, 0.85f);
            Visual(PrimitiveType.Cylinder, "Tube", barrel, new Vector3(0f, 0f, 1.35f), new Vector3(0.24f, 1.35f, 0.24f), "Gun").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Visual(PrimitiveType.Cylinder, "MuzzleBrake", barrel, new Vector3(0f, 0f, 2.7f), new Vector3(0.36f, 0.14f, 0.36f), "Gun").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(barrel, false);
            muzzle.localPosition = new Vector3(0f, 0f, 2.95f);

            // Seats: driver hatch (front), gunner on the turret, two riders on the back deck.
            var seats = new Transform[4];
            Vector3[] seatPos = { new Vector3(0.65f, 1.33f, 1.55f), new Vector3(-0.35f, 2.2f, 0.1f), new Vector3(-0.6f, 1.4f, -1.5f), new Vector3(0.6f, 1.4f, -1.5f) };
            for (int i = 0; i < 4; i++)
            {
                seats[i] = new GameObject($"Seat{i}").transform;
                seats[i].SetParent(i == 1 ? turret : t, false);
                seats[i].localPosition = i == 1 ? new Vector3(-0.35f, 0.8f, -0.25f) : seatPos[i];
            }

            var tank = root.AddComponent<Tank>();
            var so = new SerializedObject(tank);
            so.FindProperty("turret").objectReferenceValue = turret;
            so.FindProperty("barrel").objectReferenceValue = barrel;
            so.FindProperty("muzzle").objectReferenceValue = muzzle;
            SetArray(so.FindProperty("seatPoints"), seats);
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(root, $"{PrefabDir}/Tank.prefab");
        }

        /// <summary>Carport by the gun shop where the tank rolls out.</summary>
        static void BuildGarage(Transform world)
        {
            var g = new GameObject("Garage").transform;
            g.SetParent(world, false);
            g.position = GaragePos;
            g.rotation = Quaternion.LookRotation(-new Vector3(GaragePos.x, 0f, GaragePos.z).normalized);
            VillageArt.Floor(g, 3, 4, "Floor_WoodDark", 0.04f);
            VillageArt.Corners(g, 3, 4);
            VillageArt.Roof(g, 3, 4, ridgeAlongX: false, gables: false);
            VillageArt.Piece("Prop_Crate", g, new Vector3(-3.6f, 0f, -3f), 12f);
            VillageArt.Piece("Prop_Crate", g, new Vector3(-3.6f, 1.06f, -3f), 40f, scale: 0.7f);
            Text3D(g, "GARAGE", new Vector3(0f, 2.8f, 4.6f), 0.1f, new Color(1f, 0.85f, 0.35f));
            Text3D(g, "buy the tank at the gun shop", new Vector3(0f, 2.35f, 4.6f), 0.04f, new Color(1f, 0.95f, 0.85f));
        }

        // =========================================================== Lucky Lodge (gambling)

        /// <summary>The Lucky Lodge: an open-front gambling shack with slots, roulette and scratchers. Bet carcasses.</summary>
        static void BuildLuckyLodge(Transform world)
        {
            var lodge = new GameObject("LuckyLodge").transform;
            lodge.SetParent(world, false);
            lodge.position = LodgePos;
            lodge.rotation = Quaternion.LookRotation(-new Vector3(LodgePos.x, 0f, LodgePos.z).normalized); // opens toward camp

            // Brick market hall from the village kit: 12 m × 6 m, open front.
            const string brick = "Wall_UnevenBrick_Straight", win = "Wall_UnevenBrick_Window_Wide_Round";
            VillageArt.Floor(lodge, 6, 3, "Floor_RedBrick");
            VillageArt.Wall(lodge, 6, 3, VillageArt.Side.Back, brick, win, brick, brick, win, brick);
            VillageArt.Wall(lodge, 6, 3, VillageArt.Side.Left, brick, win, brick);
            VillageArt.Wall(lodge, 6, 3, VillageArt.Side.Right, brick, win, brick);
            VillageArt.Corners(lodge, 6, 3, "Corner_Exterior_Brick");
            VillageArt.Roof(lodge, 6, 3, ridgeAlongX: true);
            VillageArt.Piece("Prop_Chimney", lodge, new Vector3(-4f, 4.4f, -1.6f), 0f, solid: false);
            VillageArt.Piece("Prop_Wagon", lodge, new Vector3(8.2f, 0f, 1.5f), 70f);
            for (int i = 0; i < 3; i++) VillageArt.Piece("Prop_Crate", lodge, new Vector3(-7.4f, 0f, 2.6f - i * 1.15f), i * 25f);

            var signGlow = Text3D(lodge, "LUCKY LODGE", new Vector3(0f, 3.35f, 4.3f), 0.11f, new Color(1f, 0.85f, 0.25f));
            Text3D(lodge, "toss an animal on a pad to bet it", new Vector3(0f, 2.85f, 4.3f), 0.045f, new Color(1f, 0.95f, 0.85f));
            var lightGo = new GameObject("LodgeLight");
            lightGo.transform.SetParent(lodge, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.7f, 0.5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.9f); light.range = 10f; light.intensity = 1.6f; light.shadows = LightShadows.None;
            _nightLights.Add(light);
            signGlow.GetComponent<MeshRenderer>(); // (sign stays readable day and night)

            BuildSlots(lodge, new Vector3(-4.2f, 0.06f, -1.4f), 0);
            BuildRoulette(lodge, new Vector3(0f, 0.06f, -1.1f), 1);
            BuildScratchers(lodge, new Vector3(4.2f, 0.06f, -1.4f), 2);
        }

        static GambleStation Pad(Transform lodge, Vector3 machinePos, int id, GambleGame game, string padMat, out Transform pad)
        {
            pad = new GameObject($"Pad_{game}").transform;
            pad.SetParent(lodge, false);
            pad.localPosition = machinePos + new Vector3(0f, 0f, 2.2f);
            var col = pad.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1.8f, 1.4f, 1.8f);
            col.center = new Vector3(0f, 0.7f, 0f);
            if (padMat != null) Visual(PrimitiveType.Cube, "PadTop", pad, new Vector3(0f, 0.08f, 0f), new Vector3(1.8f, 0.06f, 1.8f), padMat);
            var station = pad.gameObject.AddComponent<GambleStation>();
            station.game = game;
            station.id = id;
            return station;
        }

        static void BuildSlots(Transform lodge, Vector3 at, int id)
        {
            var m = new GameObject("SlotMachine").transform;
            m.SetParent(lodge, false);
            m.localPosition = at;
            Solid(m, "Cabinet", new Vector3(0f, 1.0f, 0f), new Vector3(1.6f, 2.0f, 0.9f), "Roof");
            Visual(PrimitiveType.Cube, "Trim", m, new Vector3(0f, 2.05f, 0.05f), new Vector3(1.7f, 0.18f, 1.0f), "SellPad");
            Visual(PrimitiveType.Cube, "Screen", m, new Vector3(0f, 1.35f, 0.46f), new Vector3(1.35f, 0.55f, 0.04f), "Gun");
            Visual(PrimitiveType.Cylinder, "Lever", m, new Vector3(0.95f, 1.35f, 0.1f), new Vector3(0.07f, 0.35f, 0.07f), "Stone");
            Visual(PrimitiveType.Sphere, "Knob", m, new Vector3(0.95f, 1.72f, 0.1f), Vector3.one * 0.18f, "Glow");
            var station = Pad(lodge, at, id, GambleGame.Slots, "SellPad", out _);
            station.displays = new[]
            {
                Text3D(m, "RABBIT", new Vector3(-0.44f, 1.35f, 0.5f), 0.017f, Color.white),
                Text3D(m, "DUCK", new Vector3(0f, 1.35f, 0.5f), 0.017f, Color.white),
                Text3D(m, "GOOSE", new Vector3(0.44f, 1.35f, 0.5f), 0.017f, Color.white),
            };
            station.status = Text3D(m, "SLOTS", new Vector3(0f, 2.55f, 0.3f), 0.09f, new Color(1f, 0.85f, 0.3f));
        }

        static void BuildRoulette(Transform lodge, Vector3 at, int id)
        {
            var m = new GameObject("Roulette").transform;
            m.SetParent(lodge, false);
            m.localPosition = at;
            Solid(m, "Table", new Vector3(0f, 0.45f, 0f), new Vector3(2.4f, 0.9f, 1.6f), "Plank");
            Visual(PrimitiveType.Cube, "Felt", m, new Vector3(0f, 0.91f, 0f), new Vector3(2.3f, 0.02f, 1.5f), "Leaves");
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(m, false);
            wheel.localPosition = new Vector3(0f, 0.95f, 0f);
            Visual(PrimitiveType.Cylinder, "Rim", wheel, Vector3.zero, new Vector3(1.25f, 0.04f, 1.25f), "Bark");
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                var wedge = Visual(PrimitiveType.Cube, "Wedge", wheel, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.04f, 0.38f), new Vector3(0.18f, 0.03f, 0.42f), i == 0 ? "Leaves" : i % 2 == 0 ? "Roof" : "Gun");
                wedge.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            }
            Visual(PrimitiveType.Sphere, "Hub", wheel, new Vector3(0f, 0.08f, 0f), Vector3.one * 0.2f, "SellPad");
            var station = Pad(lodge, at + new Vector3(0f, 0f, 0.2f), id, GambleGame.Roulette, null, out var pad);
            // Bet by where the carcass lands: RED half (your left), BLACK half (your right), green bullseye.
            Visual(PrimitiveType.Cube, "Red", pad, new Vector3(0.45f, 0.08f, 0f), new Vector3(0.9f, 0.06f, 1.8f), "Roof");
            Visual(PrimitiveType.Cube, "Black", pad, new Vector3(-0.45f, 0.08f, 0f), new Vector3(0.9f, 0.06f, 1.8f), "Gun");
            Visual(PrimitiveType.Cylinder, "Green", pad, new Vector3(0f, 0.1f, 0f), new Vector3(0.9f, 0.03f, 0.9f), "Leaves");
            Text3D(pad, "RED", new Vector3(0.5f, 0.2f, -0.75f), 0.05f, Color.white);
            Text3D(pad, "BLACK", new Vector3(-0.5f, 0.2f, -0.75f), 0.05f, Color.white);
            station.wheel = wheel;
            station.displays = new[] { Text3D(m, "PLACE YOUR BETS", new Vector3(0f, 1.7f, 0f), 0.06f, Color.white) };
            station.status = Text3D(m, "ROULETTE", new Vector3(0f, 2.3f, 0f), 0.09f, new Color(1f, 0.85f, 0.3f));
        }

        static void BuildScratchers(Transform lodge, Vector3 at, int id)
        {
            var m = new GameObject("Scratchers").transform;
            m.SetParent(lodge, false);
            m.localPosition = at;
            Solid(m, "Booth", new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 1.1f, 0.8f), "Trader");
            Visual(PrimitiveType.Cube, "Board", m, new Vector3(0f, 1.75f, -0.2f), new Vector3(1.9f, 1.0f, 0.08f), "Plank");
            var station = Pad(lodge, at, id, GambleGame.Scratchers, "Trader", out _);
            var panels = new TextMesh[3];
            for (int i = 0; i < 3; i++)
            {
                Visual(PrimitiveType.Cube, "Panel", m, new Vector3(-0.6f + i * 0.6f, 1.75f, -0.15f), new Vector3(0.5f, 0.5f, 0.03f), "Stone");
                panels[i] = Text3D(m, "?", new Vector3(-0.6f + i * 0.6f, 1.75f, -0.12f), 0.022f, Color.gray);
            }
            station.displays = panels;
            station.status = Text3D(m, "SCRATCHERS", new Vector3(-0.35f, 2.55f, -0.2f), 0.06f, new Color(1f, 0.85f, 0.3f));
        }

        /// <summary>Depth-tested world text facing out of the parent's front (+Z), readable by someone standing in front.</summary>
        static TextMesh Text3D(Transform parent, string text, Vector3 localPos, float size, Color color)
        {
            var go = new GameObject($"Text_{text}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.color = color;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            AddWorldText(go);
            return tm;
        }

        // =========================================================== Cabin & campfire

        /// <summary>A log cabin north of camp with four beds and a lantern. Sleep here to skip the night.</summary>
        static Light BuildCabin(Transform world)
        {
            var cabin = new GameObject("Cabin").transform;
            cabin.SetParent(world, false);
            cabin.position = CabinPos;
            cabin.rotation = Quaternion.LookRotation(-CabinPos.normalized); // door faces camp

            // Half-timbered cottage from the village kit: 8 m × 6 m, door on the camp side.
            const string grid = "Wall_Plaster_WoodGrid", win = "Wall_Plaster_Window_Wide_Round", plain = "Wall_Plaster_Straight";
            VillageArt.Floor(cabin, 4, 3, "Floor_WoodDark");
            VillageArt.Wall(cabin, 4, 3, VillageArt.Side.Front, win, "Wall_Plaster_Door_Round", grid, win);
            VillageArt.Wall(cabin, 4, 3, VillageArt.Side.Back, grid, grid, grid, grid);
            VillageArt.Wall(cabin, 4, 3, VillageArt.Side.Left, plain, win, plain);
            VillageArt.Wall(cabin, 4, 3, VillageArt.Side.Right, plain, win, plain);
            VillageArt.Corners(cabin, 4, 3);
            VillageArt.Roof(cabin, 4, 3, ridgeAlongX: true);
            VillageArt.Piece("Prop_Chimney", cabin, new Vector3(2.4f, 4.3f, -1.4f), 0f, solid: false);
            VillageArt.Piece("Door_1_Round", cabin, new Vector3(-0.45f, 0.06f, 3.2f), -100f, solid: false); // swung open
            VillageArt.Piece("Prop_Crate", cabin, new Vector3(-4.8f, 0f, 2.2f), 15f);
            VillageArt.Piece("Prop_WoodenFence_Single", cabin, new Vector3(2.6f, 0f, 4.4f), 0f);
            VillageArt.Piece("Prop_WoodenFence_Single", cabin, new Vector3(4.65f, 0f, 4.4f), 0f);

            // Beds along the back wall
            const float D = 6f;
            float[] xs = { -2.7f, -0.9f, 0.9f, 2.7f };
            var blankets = new[] { new Color(0.75f, 0.2f, 0.2f), new Color(0.2f, 0.45f, 0.75f), new Color(0.3f, 0.6f, 0.3f), new Color(0.85f, 0.65f, 0.2f) };
            for (int n = 0; n < xs.Length * 2; n++) // bunk beds: 8 sleeping spots for a full lobby
            {
                int i = n % xs.Length;
                bool top = n >= xs.Length;
                var bed = new GameObject($"Bed{n}");
                bed.transform.SetParent(cabin, false);
                bed.transform.localPosition = new Vector3(xs[i], top ? 1.3f : 0.06f, -D / 2f + 1.45f);
                if (!top)
                    for (int px = -1; px <= 1; px += 2) for (int pz = -1; pz <= 1; pz += 2)
                        Visual(PrimitiveType.Cube, "BunkPost", bed.transform, new Vector3(px * 0.52f, 1.0f, pz * 0.98f), new Vector3(0.08f, 2.0f, 0.08f), "Plank");
                var col = bed.AddComponent<BoxCollider>();
                col.size = new Vector3(1.1f, 0.6f, 2.0f);
                col.center = new Vector3(0f, 0.3f, 0f);
                Visual(PrimitiveType.Cube, "Frame", bed.transform, new Vector3(0f, 0.22f, 0f), new Vector3(1.1f, 0.28f, 2.05f), "Plank");
                Visual(PrimitiveType.Cube, "Mattress", bed.transform, new Vector3(0f, 0.43f, 0.05f), new Vector3(0.95f, 0.16f, 1.85f), "Tail");
                Visual(PrimitiveType.Cube, "Pillow", bed.transform, new Vector3(0f, 0.56f, -0.72f), new Vector3(0.7f, 0.12f, 0.32f), "Tail");
                var blanket = Visual(PrimitiveType.Cube, "Blanket", bed.transform, new Vector3(0f, 0.53f, 0.3f), new Vector3(1.0f, 0.06f, 1.3f));
                string bmPath = $"{MatDir}/Blanket{i}.mat";
                var bm = AssetDatabase.LoadAssetAtPath<Material>(bmPath);
                if (bm == null) { bm = new Material(Mats["Plank"]); AssetDatabase.CreateAsset(bm, bmPath); }
                bm.SetColor("_BaseColor", blankets[i]); bm.color = blankets[i];
                blanket.GetComponent<Renderer>().sharedMaterial = bm;
                Visual(PrimitiveType.Cube, "Headboard", bed.transform, new Vector3(0f, 0.6f, -1.0f), new Vector3(1.1f, 0.8f, 0.08f), "Plank");
                var sleep = new GameObject("SleepPoint").transform;
                sleep.SetParent(bed.transform, false);
                sleep.localPosition = new Vector3(0f, 0.55f, 0.1f);
                sleep.localRotation = Quaternion.identity; // facing the foot of the bed
                bed.AddComponent<Bed>().sleepPoint = sleep;
            }

            // Lantern hanging from the ridge
            var lanternGo = new GameObject("Lantern");
            lanternGo.transform.SetParent(cabin, false);
            lanternGo.transform.localPosition = new Vector3(0f, 2.75f, 0.2f);
            Visual(PrimitiveType.Cube, "LanternBody", lanternGo.transform, Vector3.zero, new Vector3(0.18f, 0.25f, 0.18f), "Glow");
            var lantern = lanternGo.AddComponent<Light>();
            lantern.type = LightType.Point;
            lantern.color = new Color(1f, 0.72f, 0.4f);
            lantern.range = 9f;
            lantern.intensity = 2.2f;
            lantern.shadows = LightShadows.None;
            return lantern;
        }

        /// <summary>Matte see-through smoke (no shine, no reflections: glossy puffs read as soap bubbles).</summary>
        static Material SmokeMaterial()
        {
            string path = $"{MatDir}/SmokeFx.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.63f, 0.3f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Camp's campfire: logs, a stone ring and a flickering warm light that matters at night.</summary>
        static Light BuildCampfire(Transform world)
        {
            var fire = new GameObject("Campfire").transform;
            fire.SetParent(world, false);
            fire.position = CampfirePos;
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // Stone ring: chunky, uneven stones, some stacked.
            for (int i = 0; i < 11; i++)
            {
                float a = i * Mathf.PI * 2f / 11f + R(-0.08f, 0.08f);
                var st = Visual(PrimitiveType.Sphere, "Stone", fire, new Vector3(Mathf.Cos(a) * 0.82f, 0.1f, Mathf.Sin(a) * 0.82f), new Vector3(R(0.3f, 0.42f), R(0.2f, 0.3f), R(0.28f, 0.38f)), "Stone");
                st.transform.localRotation = Quaternion.Euler(R(-12f, 12f), a * Mathf.Rad2Deg, R(-12f, 12f));
            }
            // Ash bed and glowing coals.
            Visual(PrimitiveType.Cylinder, "Ash", fire, new Vector3(0f, 0.02f, 0f), new Vector3(1.25f, 0.02f, 1.25f), "Ash");
            for (int i = 0; i < 9; i++)
                Visual(PrimitiveType.Sphere, "Coal", fire, new Vector3(R(-0.35f, 0.35f), 0.06f, R(-0.35f, 0.35f)), new Vector3(R(0.1f, 0.18f), R(0.06f, 0.1f), R(0.1f, 0.18f)), "Coal");
            // Log teepee, charred where it meets the flames.
            for (int i = 0; i < 5; i++)
            {
                var log = Visual(PrimitiveType.Cylinder, "Log", fire, Vector3.zero, new Vector3(0.13f, 0.42f, 0.13f), "Bark").transform;
                log.localRotation = Quaternion.Euler(0f, i * 72f + R(-8f, 8f), 0f) * Quaternion.Euler(0f, 0f, 58f);
                log.localPosition = log.localRotation * new Vector3(0f, 0.36f, 0f) + Vector3.up * 0.05f;
            }
            // Two sitting logs.
            for (int e = -1; e <= 1; e += 2)
            {
                var seat = Visual(PrimitiveType.Cylinder, "SeatLog", fire, new Vector3(e * 2.1f, 0.22f, 0.4f * e), new Vector3(0.42f, 0.9f, 0.42f), "Bark").transform;
                seat.localRotation = Quaternion.Euler(0f, e * 20f, 90f);
                Visual(PrimitiveType.Cylinder, "Ring", seat, new Vector3(0f, 1.0f, 0f), new Vector3(0.92f, 0.01f, 0.92f), "Plank");
                Visual(PrimitiveType.Cylinder, "Ring", seat, new Vector3(0f, -1.0f, 0f), new Vector3(0.92f, 0.01f, 0.92f), "Plank");
                var sc = seat.gameObject.AddComponent<CapsuleCollider>();
                sc.direction = 1; sc.radius = 0.5f; sc.height = 2f;
            }
            var col = fire.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.8f; col.height = 0.6f; col.center = new Vector3(0f, 0.3f, 0f);
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(fire, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.25f);
            light.range = 16f;
            light.intensity = 3.2f;
            light.shadows = LightShadows.None; // point shadows cost 6 shadow maps; not worth it

            var fx = fire.gameObject.AddComponent<FriendSlop.World.CampfireFx>();
            var fso = new SerializedObject(fx);
            fso.FindProperty("outer").objectReferenceValue = Mats["Glow"];
            fso.FindProperty("mid").objectReferenceValue = Mats["FlameYellow"];
            fso.FindProperty("core").objectReferenceValue = Mats["FlameCore"];
            fso.FindProperty("ember").objectReferenceValue = Mats["FlameYellow"];
            fso.FindProperty("smoke").objectReferenceValue = SmokeMaterial();
            fso.FindProperty("fireLight").objectReferenceValue = light;
            fso.ApplyModifiedPropertiesWithoutUndo();
            return light;
        }

        // =========================================================== Helpers

        /// <summary>A primitive with no collider (pure visual).</summary>
        static GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, string mat = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (mat != null && Mats.TryGetValue(mat, out var m)) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void SetArray(SerializedProperty prop, params Object[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
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


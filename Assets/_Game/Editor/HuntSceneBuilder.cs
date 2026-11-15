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
    public static class HuntSceneBuilder
    {
        const string Root = "Assets/_Game";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir = Root + "/Scenes";
        const string MatDir = Root + "/Materials";
        const string DataDir = Root + "/Data";

        const string ScenePath = SceneDir + "/HuntTest.unity";
        const string NavMeshPath = SceneDir + "/HuntTest_NavMesh.asset";
        const string OldM0ScenePath = SceneDir + "/M0_NetTest.unity";

        const float MapHalfSize = 80f;
        const float CampClearRadius = 14f;
        static readonly Vector3 SellZonePos = new Vector3(8f, 0f, 0f);
        static readonly Vector3 GunShopPos = new Vector3(-11f, 0f, -1f);
        static readonly Vector3 TotemPos = new Vector3(0f, 0f, -36f);

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

            var catalog = WeaponCatalogBuilder.CreateOrUpdate();
            var pickup = BuildWeaponPickupPrefab(catalog);
            var hunterRig = HunterRigBuilder.Build();
            var player = BuildPlayerPrefab(catalog, pickup, hunterRig);
            var box = BuildBoxPrefab();
            var animals = AnimalBuilder.BuildAll(name => Mats[name]);
            var gameState = BuildGameStatePrefab(catalog);
            var spawner = BuildSpawnerPrefab();
            GameObject bossPrefab = null;
            foreach (var a in animals) if (a.Species.isBoss) bossPrefab = a.Prefab;
            var totem = BuildTotemPrefab(bossPrefab);

            var networked = new List<GameObject> { player, box, gameState, spawner, pickup, totem };
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
            Mat("Glow", unlit, new Color(1f, 0.45f, 0.15f));
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

        static GameObject BuildGameStatePrefab(WeaponCatalog catalog)
        {
            var go = new GameObject("GameState");
            go.AddComponent<NetworkObject>();
            var state = go.AddComponent<GameState>();
            var so = new SerializedObject(state);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();
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
        static GameObject BuildTotemPrefab(GameObject bossPrefab)
        {
            var root = new GameObject("BossTotem");
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(1.4f, 3.4f, 1.4f);
            col.center = new Vector3(0f, 1.7f, 0f);
            root.AddComponent<NetworkObject>();
            var totem = root.AddComponent<BossTotem>();
            var so = new SerializedObject(totem);
            so.FindProperty("bossPrefab").objectReferenceValue = bossPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            Visual(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(2.2f, 0.5f, 2.2f), "Stone");
            Visual(PrimitiveType.Cube, "Pillar", root.transform, new Vector3(0f, 1.8f, 0f), new Vector3(1.1f, 2.8f, 1.1f), "Stone");
            Visual(PrimitiveType.Sphere, "Skull", root.transform, new Vector3(0f, 3.5f, 0f), Vector3.one * 0.9f, "Tail");
            for (int e = -1; e <= 1; e += 2)
            {
                Visual(PrimitiveType.Cube, "Antler", root.transform, new Vector3(e * 0.7f, 4.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f), "Tail")
                    .transform.localRotation = Quaternion.Euler(0f, 0f, -e * 35f);
                Visual(PrimitiveType.Sphere, "Eye", root.transform, new Vector3(e * 0.18f, 3.55f, 0.4f), Vector3.one * 0.16f, "Glow");
            }

            return SavePrefab(root, $"{PrefabDir}/BossTotem.prefab");
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
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.86f);

            // ---------- Static scenery (baked into the navmesh) ----------
            var world = new GameObject("World").transform;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(world, false);
            ground.transform.localScale = new Vector3(MapHalfSize * 2f / 10f, 1f, MapHalfSize * 2f / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = Mats["Grass"];
            ground.isStatic = true;

            var rng = new System.Random(1337); // same forest every rebuild
            var trees = new GameObject("Trees").transform;
            trees.SetParent(world, false);
            for (int i = 0, placed = 0; i < 900 && placed < 170; i++)
            {
                Vector3 p = RandomInMap(rng, MapHalfSize - 6f);
                if (p.magnitude < CampClearRadius || NearGunShop(p) || NearTotem(p)) continue;
                NatureArt.Tree(trees, p, rng);
                placed++;
            }

            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(world, false);
            for (int i = 0, placed = 0; i < 300 && placed < 40; i++)
            {
                Vector3 p = RandomInMap(rng, MapHalfSize - 6f);
                if (p.magnitude < CampClearRadius - 4f || NearGunShop(p) || NearTotem(p)) continue;
                NatureArt.Rock(rocks, p, rng);
                placed++;
            }

            var decor = new GameObject("Decor").transform;
            decor.SetParent(world, false);
            NatureArt.Decor(decor, rng, MapHalfSize - 3f, p => p.magnitude < CampClearRadius + 1f || NearGunShop(p) || NearTotem(p) || (p - SellZonePos).magnitude < 4f);
            NatureArt.Grass(world, MapHalfSize - 1f, new[] { new Vector3(0f, 0f, 7f), new Vector3(GunShopPos.x, GunShopPos.z, 6f), new Vector3(SellZonePos.x, SellZonePos.z, 3.5f), new Vector3(TotemPos.x, TotemPos.z, 2.5f) });

            // Invisible walls at the map edge.
            var bounds = new GameObject("Bounds").transform;
            bounds.SetParent(world, false);
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject($"Wall_{i}");
                wall.transform.SetParent(bounds, false);
                var col = wall.AddComponent<BoxCollider>();
                bool alongX = i < 2;
                float side = i % 2 == 0 ? 1f : -1f;
                wall.transform.position = alongX ? new Vector3(0f, 5f, side * MapHalfSize) : new Vector3(side * MapHalfSize, 5f, 0f);
                col.size = alongX ? new Vector3(MapHalfSize * 2f, 10f, 1f) : new Vector3(1f, 10f, MapHalfSize * 2f);
                wall.isStatic = true;
            }

            BuildGunShop(world, catalog);

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

            var pad = Visual(PrimitiveType.Cube, "SellPad", camp, SellZonePos + new Vector3(0f, 0.05f, 0f), new Vector3(5f, 0.1f, 5f), "SellPad");
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

            // A couple of physics boxes to mess around with at camp.
            for (int i = 0; i < 2; i++)
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(boxPrefab, scene);
                b.transform.position = new Vector3(-5f - i * 1.6f, 0.7f, 4f + i * 1.2f);
                b.name = $"PhysicsBox_{i + 1}";
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
            totem.transform.SetPositionAndRotation(TotemPos, Quaternion.LookRotation(-TotemPos.normalized));

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

            // Shell (local space: open front faces +X, toward camp)
            Solid(shop, "Floor", new Vector3(0f, 0.1f, 0f), new Vector3(6f, 0.2f, 7.5f), "Bark");
            Solid(shop, "BackWall", new Vector3(-3f, 1.7f, 0f), new Vector3(0.3f, 3.2f, 7.5f), "Plank");
            Solid(shop, "WallN", new Vector3(0f, 1.7f, 3.6f), new Vector3(6f, 3.2f, 0.3f), "Plank");
            Solid(shop, "WallS", new Vector3(0f, 1.7f, -3.6f), new Vector3(6f, 3.2f, 0.3f), "Plank");
            Solid(shop, "Roof", new Vector3(0.3f, 3.45f, 0f), new Vector3(7f, 0.25f, 8.2f), "Roof");
            Solid(shop, "Counter", new Vector3(1.6f, 0.6f, 0f), new Vector3(0.8f, 1.0f, 4.2f), "Bark");
            Solid(shop, "PostN", new Vector3(2.9f, 1.7f, 3.6f), new Vector3(0.3f, 3.2f, 0.3f), "Bark");
            Solid(shop, "PostS", new Vector3(2.9f, 1.7f, -3.6f), new Vector3(0.3f, 3.2f, 0.3f), "Bark");

            // Gunsmith behind the counter
            var smith = new GameObject("Gunsmith").transform;
            smith.SetParent(shop, false);
            smith.localPosition = new Vector3(0.9f, 0.2f, 3.1f); // off to one side so the rack stays visible
            smith.localRotation = Quaternion.LookRotation(Vector3.right);
            Npc(smith, "outfit_knight", "head_knight", "cowboy", "scarf", 9);
            smith.gameObject.AddComponent<GunShop>();

            // Display wall
            var rack = new GameObject("WeaponRack");
            rack.transform.SetParent(shop, false);
            rack.transform.localPosition = new Vector3(-2.75f, 1.0f, 0f);
            rack.transform.localRotation = Quaternion.LookRotation(Vector3.right); // faces customers
            var r = rack.AddComponent<WeaponRack>();
            var rso = new SerializedObject(r);
            rso.FindProperty("textMaterial").objectReferenceValue = Mats.TryGetValue("WorldText", out var wtm) ? wtm : null;
            rso.ApplyModifiedPropertiesWithoutUndo();
            var so = new SerializedObject(r);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Sign over the entrance, readable from camp
            var sign = new GameObject("Sign");
            sign.transform.SetParent(shop, false);
            sign.transform.localPosition = new Vector3(3.1f, 4.35f, 0f); // above the roof edge (text is depth-tested now)
            sign.transform.rotation = Quaternion.LookRotation(Vector3.left);
            var text = sign.AddComponent<TextMesh>();
            text.text = "GUN SHOP";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.16f;
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


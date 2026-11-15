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

            var rabbitSpecies = CreateRabbitSpecies();
            var player = BuildPlayerPrefab();
            var box = BuildBoxPrefab();
            var rabbit = BuildRabbitPrefab(rabbitSpecies);
            var gameState = BuildGameStatePrefab();
            var spawner = BuildSpawnerPrefab(rabbit);
            var prefabList = RegisterNetworkPrefabs(player, box, rabbit, gameState, spawner);

            BuildScene(player, box, gameState, spawner, prefabList);

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
            Mat("Grass", lit, new Color(0.36f, 0.55f, 0.27f));
            Mat("Bark", lit, new Color(0.38f, 0.26f, 0.16f));
            Mat("Leaves", lit, new Color(0.18f, 0.42f, 0.2f));
            Mat("Rock", lit, new Color(0.5f, 0.5f, 0.52f));
            Mat("SellPad", lit, new Color(0.95f, 0.78f, 0.2f));
            Mat("Trader", lit, new Color(0.25f, 0.35f, 0.75f));
            Mat("Gun", lit, new Color(0.15f, 0.13f, 0.12f));
            Mat("Tail", lit, new Color(0.95f, 0.95f, 0.92f));
            Mat("Tracer", unlit, new Color(1f, 0.9f, 0.4f));
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

        static AnimalSpecies CreateRabbitSpecies()
        {
            string path = $"{DataDir}/Rabbit.asset";
            var s = AssetDatabase.LoadAssetAtPath<AnimalSpecies>(path);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<AnimalSpecies>();
                AssetDatabase.CreateAsset(s, path);
            }
            // Keep any hand-tuned values; only fill identity on first creation.
            if (string.IsNullOrEmpty(s.displayName)) s.displayName = "Rabbit";
            EditorUtility.SetDirty(s);
            return s;
        }

        // =========================================================== Prefabs

        static GameObject BuildPlayerPrefab()
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

            var body = Visual(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f));
            var nose = Visual(PrimitiveType.Cube, "Nose", root.transform, new Vector3(0f, 1.5f, 0.33f), new Vector3(0.25f, 0.15f, 0.25f));

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

            var gun = new GameObject("Rifle").transform;
            gun.SetParent(pivot, false);
            gun.localPosition = new Vector3(0.24f, -0.26f, 0.5f);
            Visual(PrimitiveType.Cube, "Barrel", gun, new Vector3(0f, 0.02f, 0.18f), new Vector3(0.035f, 0.035f, 0.62f), "Gun");
            Visual(PrimitiveType.Cube, "Stock", gun, new Vector3(0f, -0.02f, -0.2f), new Vector3(0.06f, 0.1f, 0.3f), "Bark");
            Visual(PrimitiveType.Cube, "Scope", gun, new Vector3(0f, 0.075f, 0.05f), new Vector3(0.04f, 0.04f, 0.18f), "Gun");
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, 0.02f, 0.5f);

            var hold = new GameObject("HoldPoint").transform;
            hold.SetParent(pivot, false);
            hold.localPosition = new Vector3(0f, -0.25f, 1.3f);

            var np = root.AddComponent<NetworkPlayer>();
            var so = new SerializedObject(np);
            so.FindProperty("cameraPivot").objectReferenceValue = pivot;
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("audioListener").objectReferenceValue = listener;
            so.FindProperty("holdPoint").objectReferenceValue = hold;
            SetArray(so.FindProperty("bodyRenderers"), body.GetComponent<Renderer>(), nose.GetComponent<Renderer>());
            so.ApplyModifiedPropertiesWithoutUndo();

            var rifle = root.AddComponent<Rifle>();
            so = new SerializedObject(rifle);
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("muzzle").objectReferenceValue = muzzle;
            so.FindProperty("gunModel").objectReferenceValue = gun;
            so.FindProperty("tracerMaterial").objectReferenceValue = Mats["Tracer"];
            so.ApplyModifiedPropertiesWithoutUndo();

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

        static GameObject BuildRabbitPrefab(AnimalSpecies species)
        {
            var root = new GameObject("Rabbit");

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 2.5f;
            rb.isKinematic = true;                                  // alive: the NavMeshAgent drives it
            rb.interpolation = RigidbodyInterpolation.None;         // agent/network move the transform
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.25f;
            agent.height = 0.5f;
            agent.baseOffset = 0f;
            agent.angularSpeed = 720f;
            agent.acceleration = 30f;
            agent.stoppingDistance = 0.2f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false; // the host enables it once spawned on the navmesh

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // host owns it, carriers borrow it
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;

            root.AddComponent<Carryable>();

            // Colliders (static relative to root, so headshots don't depend on the hop animation)
            var bodyCol = new GameObject("BodyHitbox");
            bodyCol.transform.SetParent(root.transform, false);
            var cap = bodyCol.AddComponent<CapsuleCollider>();
            cap.direction = 2; // Z
            cap.center = new Vector3(0f, 0.22f, 0f);
            cap.radius = 0.16f;
            cap.height = 0.5f;
            bodyCol.AddComponent<Hitbox>();

            var headCol = new GameObject("HeadHitbox");
            headCol.transform.SetParent(root.transform, false);
            var sph = headCol.AddComponent<SphereCollider>();
            sph.center = new Vector3(0f, 0.37f, 0.24f);
            sph.radius = 0.12f;
            var headHb = headCol.AddComponent<Hitbox>();
            var hso = new SerializedObject(headHb);
            hso.FindProperty("isHead").boolValue = true;
            hso.ApplyModifiedPropertiesWithoutUndo();

            // Visuals (bobbed by the hop animation)
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var body = Visual(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 0.22f, 0f), new Vector3(0.32f, 0.25f, 0.3f));
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var head = Visual(PrimitiveType.Sphere, "Head", visual, new Vector3(0f, 0.37f, 0.24f), Vector3.one * 0.24f);
            var earL = Visual(PrimitiveType.Cube, "EarL", visual, new Vector3(-0.05f, 0.53f, 0.22f), new Vector3(0.05f, 0.2f, 0.03f));
            var earR = Visual(PrimitiveType.Cube, "EarR", visual, new Vector3(0.05f, 0.53f, 0.22f), new Vector3(0.05f, 0.2f, 0.03f));
            Visual(PrimitiveType.Sphere, "Tail", visual, new Vector3(0f, 0.27f, -0.27f), Vector3.one * 0.11f, "Tail");

            var animal = root.AddComponent<Animal>();
            var so = new SerializedObject(animal);
            so.FindProperty("species").objectReferenceValue = species;
            so.FindProperty("visualRoot").objectReferenceValue = visual;
            SetArray(so.FindProperty("furRenderers"),
                body.GetComponent<Renderer>(), head.GetComponent<Renderer>(),
                earL.GetComponent<Renderer>(), earR.GetComponent<Renderer>());
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, $"{PrefabDir}/Rabbit.prefab");
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
            foreach (var prefab in prefabs)
                if (!list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            return list;
        }

        // =========================================================== Scene

        static GameObject BuildGameStatePrefab()
        {
            var go = new GameObject("GameState");
            go.AddComponent<NetworkObject>();
            go.AddComponent<GameState>();
            return SavePrefab(go, $"{PrefabDir}/GameState.prefab");
        }

        static GameObject BuildSpawnerPrefab(GameObject rabbitPrefab)
        {
            var go = new GameObject("RabbitSpawner");
            go.AddComponent<NetworkObject>();
            var spawner = go.AddComponent<AnimalSpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("animalPrefab").objectReferenceValue = rabbitPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, $"{PrefabDir}/RabbitSpawner.prefab");
        }

        static void BuildScene(GameObject playerPrefab, GameObject boxPrefab, GameObject gameStatePrefab,
                               GameObject spawnerPrefab, NetworkPrefabsList prefabList)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var menuCam = Camera.main;
            if (menuCam != null)
            {
                menuCam.transform.SetPositionAndRotation(new Vector3(-6f, 9f, -16f), Quaternion.Euler(25f, 25f, 0f));
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
            for (int i = 0, placed = 0; i < 400 && placed < 90; i++)
            {
                Vector3 p = RandomInMap(rng, MapHalfSize - 6f);
                if (p.magnitude < CampClearRadius) continue;
                Tree(trees, p, rng);
                placed++;
            }

            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(world, false);
            for (int i = 0, placed = 0; i < 200 && placed < 28; i++)
            {
                Vector3 p = RandomInMap(rng, MapHalfSize - 6f);
                if (p.magnitude < CampClearRadius - 4f) continue;
                Rock(rocks, p, rng);
                placed++;
            }

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
            var traderBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            traderBody.name = "Body";
            traderBody.transform.SetParent(trader, false);
            traderBody.transform.localPosition = new Vector3(0f, 1f, 0f);
            traderBody.GetComponent<Renderer>().sharedMaterial = Mats["Trader"];
            Visual(PrimitiveType.Cube, "Hat", trader, new Vector3(0f, 2.1f, 0f), new Vector3(0.9f, 0.12f, 0.9f), "Bark");

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
            PrefabUtility.InstantiatePrefab(spawnerPrefab, scene);

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

            new GameObject("HUD").AddComponent<GameHud>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void Tree(Transform parent, Vector3 pos, System.Random rng)
        {
            float h = 1.8f + (float)rng.NextDouble() * 1.2f;   // trunk half-height
            var tree = new GameObject("Tree").transform;
            tree.SetParent(parent, false);
            tree.position = pos;
            tree.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(tree, false);
            trunk.transform.localPosition = new Vector3(0f, h, 0f);
            trunk.transform.localScale = new Vector3(0.45f, h, 0.45f);
            trunk.GetComponent<Renderer>().sharedMaterial = Mats["Bark"];
            trunk.isStatic = true;

            float c = 3f + (float)rng.NextDouble() * 1.8f;
            var canopy = Visual(PrimitiveType.Sphere, "Canopy", tree, new Vector3(0f, h * 2f + c * 0.3f, 0f), new Vector3(c, c * 1.15f, c), "Leaves");
            canopy.isStatic = true;
            tree.gameObject.isStatic = true;
        }

        static void Rock(Transform parent, Vector3 pos, System.Random rng)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Rock";
            rock.transform.SetParent(parent, false);
            var s = new Vector3(1f + (float)rng.NextDouble() * 2f, 0.6f + (float)rng.NextDouble(), 1f + (float)rng.NextDouble() * 2f);
            rock.transform.localScale = s;
            rock.transform.position = pos + Vector3.up * (s.y * 0.35f);
            rock.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 12f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 12f);
            rock.GetComponent<Renderer>().sharedMaterial = Mats["Rock"];
            rock.isStatic = true;
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

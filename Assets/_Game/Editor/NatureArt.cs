using System.Collections.Generic;
using System.Linq;
using FriendSlop.World;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Sets up the Quaternius Stylized Nature MegaKit (CC0) for URP and scatters it over the map:
    /// our own materials (alpha-clipped, double-sided foliage; bark with normal maps) remapped onto
    /// every model, trees with trunk colliders, rocks with mesh colliders, decorative plants, and an
    /// instanced grass field. Difficulty rings show in the forest: leafy trees near camp, pines
    /// further out, dead trees at the edge.
    /// </summary>
    public static class NatureArt
    {
        const string Root = "Assets/_Game/ThirdParty/QuaterniusNature";
        const string Models = Root + "/Models";
        const string Tex = Root + "/Textures";
        const string MatDir = "Assets/_Game/Materials/Nature";

        static readonly Dictionary<string, Material> _mats = new();

        // ------------------------------------------------------------------ setup

        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/_Game/Materials", "Nature");

            foreach (var n in new[] { "Bark_DeadTree_Normal", "Bark_NormalTree_Normal", "Bark_TwistedTree_Normal" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{Tex}/{n}.png");
                if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            }
            foreach (var n in new[] { "Bark_DeadTree", "Bark_NormalTree", "Bark_TwistedTree", "Bark_DeadTree_Normal", "Bark_NormalTree_Normal", "Bark_TwistedTree_Normal", "Leaves", "Rocks_Diffuse", "Rocks_Desert_Diffuse" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{Tex}/{n}.png");
                if (ti != null && ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
            }

            _mats.Clear();
            Bark("Bark_NormalTree", "Bark_NormalTree", "Bark_NormalTree_Normal");
            Bark("Bark_DeadTree", "Bark_DeadTree", "Bark_DeadTree_Normal");
            Bark("Bark_TwistedTree", "Bark_TwistedTree", "Bark_TwistedTree_Normal");
            Foliage("Leaves_NormalTree", "Leaves_NormalTree_C", new Color(0.92f, 1f, 0.88f));
            Foliage("Leaves_TwistedTree", "Leaves_TwistedTree_C", Color.white);
            Foliage("Leaves_Pine", "Leaf_Pine_C", Color.white);
            Foliage("Leaves", "Leaves", Color.white);
            Foliage("Flowers", "Flowers", Color.white);
            Foliage("Grass", "Grass", new Color(0.95f, 1f, 0.9f));
            Opaque("Rocks", "Rocks_Diffuse");
            Opaque("PathRocks", "PathRocks_Diffuse");
            Opaque("Mushrooms", "Mushrooms");

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool changed = false;
                foreach (var m in go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct().ToList())
                {
                    if (!_mats.TryGetValue(m, out var mine)) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m);
                    var existing = importer.GetExternalObjectMap();
                    if (existing.TryGetValue(id, out var cur) && cur == mine) continue;
                    importer.AddRemap(id, mine);
                    changed = true;
                }
                if (importer.importAnimation || importer.importCameras || importer.importLights)
                {
                    importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        static Material Make(string name, out bool created)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = m == null;
            if (created)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            _mats[name] = m;
            return m;
        }

        static Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{n}.png");

        static void Bark(string name, string albedo, string normal)
        {
            var m = Make(name, out _);
            m.SetTexture("_BaseMap", T(albedo));
            m.SetTexture("_BumpMap", T(normal));
            m.SetFloat("_BumpScale", 0.6f);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", 0.08f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static void Opaque(string name, string albedo)
        {
            var m = Make(name, out _);
            m.SetTexture("_BaseMap", T(albedo));
            m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static void Foliage(string name, string albedo, Color tint)
        {
            var m = Make(name, out _);
            m.SetTexture("_BaseMap", T(albedo));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.45f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f); // double-sided cards
            m.doubleSidedGI = true;
            m.SetFloat("_Smoothness", 0.05f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ placement

        static GameObject Model(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{name}.fbx");

        static GameObject Place(string model, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var src = Model(model);
            if (src == null) { Debug.LogWarning($"[Nature] missing model {model}"); return null; }
            // Holder carries our placement; the model keeps its own import transform underneath.
            var go = new GameObject(model);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            PrefabUtility.InstantiatePrefab(src, go.transform);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            return go;
        }

        /// <summary>A tree chosen by distance from camp (the difficulty rings), with a trunk collider.</summary>
        public static void Tree(Transform parent, Vector3 pos, System.Random rng)
        {
            float d = new Vector2(pos.x, pos.z).magnitude;
            float r = (float)rng.NextDouble();
            string model; float scale; float trunkRadius;
            if (d < 40f)
            {
                if (r < 0.75f) { model = $"CommonTree_{1 + rng.Next(5)}"; scale = 1.05f + (float)rng.NextDouble() * 0.35f; trunkRadius = 0.32f; }
                else { model = $"Pine_{1 + rng.Next(5)}"; scale = 1f + (float)rng.NextDouble() * 0.3f; trunkRadius = 0.3f; }
            }
            else if (d < 62f)
            {
                if (r < 0.55f) { model = $"Pine_{1 + rng.Next(5)}"; scale = 1.1f + (float)rng.NextDouble() * 0.4f; trunkRadius = 0.32f; }
                else if (r < 0.85f) { model = $"CommonTree_{1 + rng.Next(5)}"; scale = 1.1f + (float)rng.NextDouble() * 0.3f; trunkRadius = 0.34f; }
                else { model = $"TwistedTree_{1 + rng.Next(5)}"; scale = 0.5f + (float)rng.NextDouble() * 0.15f; trunkRadius = 0.55f; }
            }
            else
            {
                if (r < 0.4f) { model = $"DeadTree_{1 + rng.Next(5)}"; scale = 0.7f + (float)rng.NextDouble() * 0.25f; trunkRadius = 0.4f; }
                else if (r < 0.8f) { model = $"Pine_{1 + rng.Next(5)}"; scale = 1.2f + (float)rng.NextDouble() * 0.4f; trunkRadius = 0.34f; }
                else { model = $"TwistedTree_{1 + rng.Next(5)}"; scale = 0.55f + (float)rng.NextDouble() * 0.2f; trunkRadius = 0.6f; }
            }

            var tree = Place(model, parent, pos, (float)rng.NextDouble() * 360f, scale);
            if (tree == null) return;
            tree.name = model;
            var col = tree.AddComponent<CapsuleCollider>();
            col.radius = trunkRadius / scale;
            col.height = 5f / scale;
            col.center = new Vector3(0f, col.height * 0.5f, 0f);
        }

        public static void Rock(Transform parent, Vector3 pos, System.Random rng)
        {
            var rock = Place($"Rock_Medium_{1 + rng.Next(3)}", parent, pos, (float)rng.NextDouble() * 360f, 0.55f + (float)rng.NextDouble() * 0.9f);
            if (rock == null) return;
            rock.name = "Rock";
            foreach (var mf in rock.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        /// <summary>Bushes, ferns, flowers, mushrooms and pebbles. No colliders.</summary>
        public static void Decor(Transform parent, System.Random rng, float half, System.Func<Vector3, bool> blocked)
        {
            (string name, float weight, float min, float max)[] table =
            {
                ("Bush_Common", 3f, 0.7f, 1.1f), ("Bush_Common_Flowers", 2f, 0.7f, 1.0f),
                ("Fern_1", 3f, 0.16f, 0.24f), ("Plant_1", 2f, 0.8f, 1.2f), ("Plant_1_Big", 1f, 0.5f, 0.7f),
                ("Plant_7", 2f, 1f, 1.4f), ("Plant_7_Big", 1.5f, 1f, 1.3f),
                ("Clover_1", 2f, 0.6f, 0.9f), ("Clover_2", 2f, 0.6f, 0.9f),
                ("Mushroom_Common", 2f, 0.7f, 1.2f), ("Mushroom_Laetiporus", 1f, 0.6f, 0.9f),
                ("Pebble_Round_1", 1f, 1f, 2f), ("Pebble_Round_3", 1f, 1f, 2f), ("Pebble_Square_2", 1f, 1f, 2f),
                ("Pebble_Square_5", 1f, 1f, 2f), ("RockPath_Round_Wide", 0.6f, 0.6f, 1f),
            };
            float total = table.Sum(t => t.weight);
            for (int i = 0, placed = 0; i < 1400 && placed < 520; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() * 2f - 1f) * half, 0f, ((float)rng.NextDouble() * 2f - 1f) * half);
                if (blocked(p)) continue;
                float pick = (float)rng.NextDouble() * total;
                var row = table[^1];
                foreach (var t in table) { pick -= t.weight; if (pick <= 0f) { row = t; break; } }
                var go = Place(row.name, parent, p, (float)rng.NextDouble() * 360f, Mathf.Lerp(row.min, row.max, (float)rng.NextDouble()));
                if (go != null) foreach (var rend in go.GetComponentsInChildren<Renderer>()) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                placed++;
            }
        }

        public static void Grass(Transform parent, float half, Vector3[] clearings)
        {
            var go = new GameObject("GrassField");
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<GrassField>();
            field.halfSize = half;
            field.clearings = clearings;
            field.prototypes = new[]
            {
                Proto("Grass_Common_Short", 6f, 0.3f, 0.45f),
                Proto("Grass_Common_Tall", 1.2f, 0.25f, 0.38f),
                Proto("Grass_Wispy_Short", 4f, 0.35f, 0.5f),
                Proto("Grass_Wispy_Tall", 1f, 0.28f, 0.4f),
                Proto("Flower_3_Group", 0.35f, 0.22f, 0.32f),
                Proto("Flower_4_Group", 0.35f, 0.2f, 0.3f),
                Proto("Petal_2", 0.5f, 0.6f, 0.9f),
            };
            field.count = 22000;
        }

        static GrassField.Prototype Proto(string model, float weight, float min, float max)
        {
            var src = Model(model);
            var mf = src.GetComponentInChildren<MeshFilter>();
            var mr = src.GetComponentInChildren<MeshRenderer>();
            return new GrassField.Prototype { mesh = mf.sharedMesh, materials = mr.sharedMaterials, weight = weight, scale = new Vector2(min, max), meshOffset = mf.transform.localToWorldMatrix };
        }
    }
}

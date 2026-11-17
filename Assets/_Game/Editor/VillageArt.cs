using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Quaternius Medieval Village MegaKit (CC0): material setup (normal maps, glassy windows, 1K
    /// textures) and a modular house builder on the kit's 2 m grid — walls, corner posts, floors,
    /// tiled roofs with gable ends — with mesh colliders so the buildings are solid and bake into
    /// the navmesh. Used for the gun shop, cabin, Lucky Lodge and trader stall.
    /// </summary>
    public static class VillageArt
    {
        const string Root = "Assets/_Game/ThirdParty/QuaterniusVillage";
        const string Models = Root + "/Models";
        const string Tex = Root + "/Textures";
        const string MatDir = "Assets/_Game/Materials/Village";
        public const float Cell = 2f, WallH = 3.12f;

        static readonly Dictionary<string, Material> _mats = new();

        public enum Side { Back, Front, Left, Right }

        // ------------------------------------------------------------------ setup

        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/_Game/Materials", "Village");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Tex }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool dirty = false;
                if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }
                bool normal = path.Contains("_Normal");
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }

            _mats.Clear();
            Surface("MI_WoodTrim", "T_WoodTrim_BaseColor", "T_WoodTrim_Normal", Color.white, 0.15f);
            Surface("MI_WoodTrim_Wear", "T_WoodTrim_BaseColor", "T_WoodTrim_Normal", new Color(0.82f, 0.78f, 0.74f), 0.1f);
            Surface("MI_Plaster", "T_Plaster_BaseColor", "T_Plaster_Normal", Color.white, 0.12f);
            Surface("MI_Brick", "T_Brick_BaseColor", "T_Brick_Normal", Color.white, 0.12f);
            Surface("MI_RedBrick", "T_RedBrick_BaseColor", "T_Brick_Normal", Color.white, 0.12f);
            Surface("MI_UnevenBrick", "T_UnevenBrick_BaseColor", "T_UnevenBrick_Normal", Color.white, 0.12f);
            Surface("MI_RockTrim", "T_RockTrim_BaseColor", "T_RockTrim_Normal", Color.white, 0.1f);
            Surface("MI_RoundTiles", "T_RoundTiles_BaseColor", "T_RoundTiles_Normal", Color.white, 0.2f);
            Glass("MI_WindowGlass");

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool changed = false;
                var map = importer.GetExternalObjectMap();
                foreach (var name in go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct().ToList())
                {
                    if (!_mats.TryGetValue(name, out var mine)) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                    if (map.TryGetValue(id, out var cur) && cur == mine) continue;
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

        static Material Make(string name)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            _mats[name] = m;
            return m;
        }

        static Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{n}.png");

        static void Surface(string name, string albedo, string normal, Color tint, float smooth)
        {
            var m = Make(name);
            m.SetTexture("_BaseMap", T(albedo));
            m.SetColor("_BaseColor", tint);
            var n = T(normal);
            if (n) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static void Glass(string name)
        {
            var m = Make(name);
            m.SetColor("_BaseColor", new Color(0.55f, 0.75f, 0.9f, 0.45f));
            m.SetFloat("_Smoothness", 0.95f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Place a kit piece (local to <paramref name="parent"/>), solid unless told otherwise.</summary>
        public static GameObject Piece(string model, Transform parent, Vector3 localPos, float yaw = 0f, bool solid = true, float scale = 1f)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{model}.fbx");
            if (src == null) { Debug.LogWarning($"[Village] missing {model}"); return null; }
            var holder = new GameObject(model);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPos;
            holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            holder.transform.localScale = Vector3.one * scale;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, holder.transform);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            if (solid)
                foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return holder;
        }

        /// <summary>
        /// A wall run along one side of a building <paramref name="cellsX"/>×<paramref name="cellsZ"/> cells, centered on
        /// the parent. <paramref name="pieces"/> names one kit wall per cell (null = leave the gap open).
        /// Walls face outward; the kit puts each wall's outside face on its +Z.
        /// </summary>
        public static void Wall(Transform parent, int cellsX, int cellsZ, Side side, params string[] pieces)
        {
            float hx = cellsX * Cell / 2f, hz = cellsZ * Cell / 2f;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (string.IsNullOrEmpty(pieces[i])) continue;
                float along = -((pieces.Length - 1) * Cell / 2f) + i * Cell;
                switch (side)
                {
                    case Side.Front: Piece(pieces[i], parent, new Vector3(along, 0f, hz), 0f); break;
                    case Side.Back: Piece(pieces[i], parent, new Vector3(-along, 0f, -hz), 180f); break;
                    case Side.Right: Piece(pieces[i], parent, new Vector3(hx, 0f, -along), 90f); break;
                    case Side.Left: Piece(pieces[i], parent, new Vector3(-hx, 0f, along), -90f); break;
                }
            }
        }

        public static void Corners(Transform parent, int cellsX, int cellsZ, string post = "Corner_Exterior_Wood")
        {
            float hx = cellsX * Cell / 2f, hz = cellsZ * Cell / 2f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Piece(post, parent, new Vector3(sx * hx, 0f, sz * hz), 0f, solid: false);
        }

        public static void Floor(Transform parent, int cellsX, int cellsZ, string tile = "Floor_WoodDark", float y = 0.06f)
        {
            for (int ix = 0; ix < cellsX; ix++)
                for (int iz = 0; iz < cellsZ; iz++)
                    Piece(tile, parent, new Vector3(-cellsX + 1f + ix * Cell, y, -cellsZ + 1f + iz * Cell), 0f, solid: false);
            // One flat collider instead of a mesh collider per tile.
            var col = new GameObject("FloorCollider").AddComponent<BoxCollider>();
            col.transform.SetParent(parent, false);
            col.size = new Vector3(cellsX * Cell, 0.12f, cellsZ * Cell);
            col.center = new Vector3(0f, y - 0.04f, 0f);
            col.gameObject.isStatic = true;
        }

        /// <summary>
        /// Tiled roof + gable ends. <paramref name="ridgeAlongX"/>: true when the building is wider (X) than deep (Z).
        /// Roof sizes come from the kit: span (across the ridge) 4/6/8, length 4–14.
        /// </summary>
        public static void Roof(Transform parent, int cellsX, int cellsZ, bool ridgeAlongX, bool gables = true)
        {
            int span = (ridgeAlongX ? cellsZ : cellsX) * 2, length = (ridgeAlongX ? cellsX : cellsZ) * 2;
            float yaw = ridgeAlongX ? 90f : 0f;
            Piece($"Roof_RoundTiles_{span}x{length}", parent, new Vector3(0f, WallH, 0f), yaw, solid: true);
            if (!gables) return;
            float half = length / 2f;
            string gable = $"Roof_Front_Brick{span}";
            if (ridgeAlongX)
            {
                Piece(gable, parent, new Vector3(half, WallH, 0f), 90f, solid: false);
                Piece(gable, parent, new Vector3(-half, WallH, 0f), -90f, solid: false);
            }
            else
            {
                Piece(gable, parent, new Vector3(0f, WallH, half), 0f, solid: false);
                Piece(gable, parent, new Vector3(0f, WallH, -half), 180f, solid: false);
            }
        }
    }
}

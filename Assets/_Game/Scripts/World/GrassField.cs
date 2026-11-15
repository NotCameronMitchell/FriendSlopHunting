using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.World
{
    /// <summary>
    /// Scatters thousands of grass/flower clumps over the map and draws them with GPU instancing,
    /// only near the camera. Placement is deterministic from the seed, so everyone sees the same
    /// meadow without any networking. Purely visual: no colliders.
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        [System.Serializable]
        public class Prototype
        {
            public Mesh mesh;
            public Material[] materials;
            public float weight = 1f;
            public Vector2 scale = new Vector2(0.8f, 1.2f);
            [Tooltip("The mesh's own import transform (FBX axis/scale fix-up), applied under each instance.")]
            public Matrix4x4 meshOffset = Matrix4x4.identity;
        }

        public Prototype[] prototypes;
        public int count = 9000;
        public float halfSize = 78f;
        public float drawDistance = 70f;
        public int seed = 4242;
        [Tooltip("XZ circles (x, z, radius) kept clear of grass, e.g. camp floor and shop interior.")]
        public Vector3[] clearings;
        [Tooltip("Sit on the terrain, skipping water, cliffs and bare peaks.")]
        public bool useTerrain;
        public float maxSteepness = 30f;
        public float maxHeight = 20f;

        const float CellSize = 16f;

        class Cell
        {
            public Bounds bounds;
            public List<Matrix4x4>[] perProto;
        }

        readonly List<Cell> _cells = new();
        readonly Plane[] _planes = new Plane[6];
        readonly Matrix4x4[] _batch = new Matrix4x4[1023];
        RenderParams[][] _params;

        void Start()
        {
            // Headless builds (dedicated test clients) have no GPU instancing; grass is cosmetic, so just skip it.
            if (!SystemInfo.supportsInstancing || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { enabled = false; return; }
            Generate();
        }

        void Generate()
        {
            _cells.Clear();
            if (prototypes == null || prototypes.Length == 0) return;
            float total = 0f;
            foreach (var p in prototypes) total += Mathf.Max(0f, p.weight);

            var rng = new System.Random(seed);
            var lookup = new Dictionary<Vector2Int, Cell>();
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector3(((float)rng.NextDouble() * 2f - 1f) * halfSize, 0f, ((float)rng.NextDouble() * 2f - 1f) * halfSize);
                float pick = (float)rng.NextDouble() * total;
                float yaw = (float)rng.NextDouble() * 360f;
                float s01 = (float)rng.NextDouble();
                float edge = (float)rng.NextDouble();
                if (InClearing(pos, edge)) continue;
                if (useTerrain)
                {
                    pos.y = Ground.HeightAt(pos);
                    if (pos.y < Ground.WaterY + 0.25f || pos.y > maxHeight) continue;
                    if (Ground.SteepnessAt(pos) > maxSteepness || WaterVolume.DepthAt(pos + Vector3.up * 0.1f) > 0f) continue;
                }

                int proto = 0;
                for (float acc = 0f; proto < prototypes.Length; proto++)
                {
                    acc += Mathf.Max(0f, prototypes[proto].weight);
                    if (pick <= acc) break;
                }
                proto = Mathf.Min(proto, prototypes.Length - 1);
                var pr = prototypes[proto];
                if (pr.mesh == null) continue;

                var key = new Vector2Int(Mathf.FloorToInt(pos.x / CellSize), Mathf.FloorToInt(pos.z / CellSize));
                if (!lookup.TryGetValue(key, out var cell))
                {
                    cell = new Cell
                    {
                        bounds = new Bounds(new Vector3((key.x + 0.5f) * CellSize, pos.y + 1f, (key.y + 0.5f) * CellSize), new Vector3(CellSize + 4f, 30f, CellSize + 4f)),
                        perProto = new List<Matrix4x4>[prototypes.Length],
                    };
                    lookup[key] = cell;
                    _cells.Add(cell);
                }
                cell.perProto[proto] ??= new List<Matrix4x4>();
                float s = Mathf.Lerp(pr.scale.x, pr.scale.y, s01);
                cell.perProto[proto].Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one * s) * pr.meshOffset);
            }

            Debug.Log($"[Grass] {_cells.Count} cells, {prototypes.Length} kinds");
            _params = new RenderParams[prototypes.Length][];
            for (int p = 0; p < prototypes.Length; p++)
            {
                var mats = prototypes[p].materials ?? new Material[0];
                _params[p] = new RenderParams[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null) mats[m].enableInstancing = true;
                    _params[p][m] = new RenderParams(mats[m])
                    {
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        worldBounds = new Bounds(Vector3.zero, Vector3.one * halfSize * 3f),
                    };
                }
            }
        }

        bool InClearing(Vector3 p, float edge)
        {
            if (clearings == null) return false;
            foreach (var c in clearings)
            {
                float dx = p.x - c.x, dz = p.z - c.y;
                float r = c.z + edge * edge * 5f; // soft, thinning edge instead of a hard line
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        void Update()
        {
            var lc = FriendSlop.Player.NetworkPlayer.LocalCamera;
            var cam = lc != null && lc.TryGetComponent<Camera>(out var c) && c.isActiveAndEnabled ? c : Camera.main;
            if (cam == null || _params == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            var camPos = cam.transform.position;
            float maxSqr = drawDistance * drawDistance;

            foreach (var cell in _cells)
            {
                if ((cell.bounds.ClosestPoint(camPos) - camPos).sqrMagnitude > maxSqr) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, cell.bounds)) continue;
                for (int p = 0; p < prototypes.Length; p++)
                {
                    var list = cell.perProto[p];
                    if (list == null || list.Count == 0) continue;
                    var mesh = prototypes[p].mesh;
                    for (int start = 0; start < list.Count; start += _batch.Length)
                    {
                        int n = Mathf.Min(_batch.Length, list.Count - start);
                        list.CopyTo(start, _batch, 0, n);
                        for (int m = 0; m < _params[p].Length && m < mesh.subMeshCount; m++)
                            if (_params[p][m].material != null)
                                Graphics.RenderMeshInstanced(_params[p][m], mesh, m, _batch, n);
                    }
                }
            }
        }
    }
}

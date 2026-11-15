using System.IO;
using FriendSlop.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Generates the hunting grounds: a 300 m square valley on a mountaintop, ringed by peaks (the
    /// map border), with rolling hills, a lake and a pond, a stream running down from the
    /// mountains into the lake and another draining out through a gorge. The ground is painted
    /// from slope/height/water (grass, forest floor, trampled dirt, sand banks, rock), textures are
    /// generated in code, and water bodies get flowing transparent surfaces. Camp stays flat at y=0.
    /// </summary>
    public static class TerrainBuilder
    {
        public const float Half = 150f;
        const float BaseY = -6f, Height = 84f;
        const int Res = 513, SplatRes = 512;
        const string Dir = "Assets/_Game/Data/Terrain";
        const string TexDir = "Assets/_Game/Materials/Ground";
        public static readonly Vector2 TotemXZ = new Vector2(-14f, -106f); // Big Doug's totem, far out on the north ridge

        struct Lake { public Vector2 c; public float r, depth; }

        class Stream
        {
            public Vector2[] pts; public float h0, h1;
            public float[] cum; public float len;
            public void Prepare()
            {
                cum = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
                len = cum[^1];
            }
            public float Profile(float arc) => Mathf.Lerp(h0, h1, arc / len);
            /// <summary>Distance to the stream's centerline and arc position of the closest point.</summary>
            public float Nearest(Vector2 q, out float arc)
            {
                float best = float.MaxValue; arc = 0f;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector2 a = pts[i], ab = pts[i + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    float d = (a + ab * t - q).sqrMagnitude;
                    if (d < best) { best = d; arc = cum[i] + t * ab.magnitude; }
                }
                return Mathf.Sqrt(best);
            }
        }

        /// <summary>Out-in-the-wild landmarks (x, flat-pad radius, z): hog wallow, ranger tower, abandoned camp.</summary>
        public static readonly Vector3 HogWallow = new Vector3(-62f, 12f, -68f);
        public static readonly Vector3 RangerTower = new Vector3(42f, 9f, -82f);
        public static readonly Vector3 OldCamp = new Vector3(-92f, 10f, -8f);
        public static Vector3[] Landmarks => new[] { HogWallow, RangerTower, OldCamp };

        static readonly Lake[] Lakes =
        {
            new Lake { c = new Vector2(64f, 66f), r = 24f, depth = 1.4f },
            new Lake { c = new Vector2(-62f, 38f), r = 11f, depth = 1.1f },
        };

        static Stream[] _streams;
        static Stream[] Streams
        {
            get
            {
                if (_streams != null) return _streams;
                _streams = new[]
                {
                    // Snowmelt creek: down from the north peaks into the lake.
                    new Stream { pts = new[] { new Vector2(-30f, 160f), new Vector2(-34f, 128f), new Vector2(-22f, 104f), new Vector2(6f, 96f), new Vector2(32f, 88f), new Vector2(52f, 76f), new Vector2(62f, 68f) },
                                 h0 = 7f, h1 = Ground.WaterY + 0.45f },
                    // Outflow: from the lake out through a gorge in the south-east peaks.
                    new Stream { pts = new[] { new Vector2(70f, 56f), new Vector2(90f, 34f), new Vector2(103f, 4f), new Vector2(120f, -28f), new Vector2(140f, -55f), new Vector2(165f, -78f) },
                                 h0 = Ground.WaterY + 0.45f, h1 = -1.8f },
                };
                foreach (var s in _streams) s.Prepare();
                return _streams;
            }
        }

        // ------------------------------------------------------------------ public

        public static Terrain Build(Transform parent)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game/Data", "Terrain");
            if (!AssetDatabase.IsValidFolder(TexDir)) AssetDatabase.CreateFolder("Assets/_Game/Materials", "Ground");

            // Order matters: (1) make the layer textures (importing assets can reload a fresh terrain asset
            // from disk and wipe unsaved heights), (2) create the asset, (3) set heights and paint, (4) save.
            // Paint set before CreateAsset isn't saved at all (the whole map came out grass).
            var layers = Layers();
            string path = $"{Dir}/HuntTerrain.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null) AssetDatabase.DeleteAsset(path);
            var td = new TerrainData { heightmapResolution = Res, size = new Vector3(Half * 2f, Height, Half * 2f) };
            AssetDatabase.CreateAsset(td, path);
            var heights = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float x = -Half + ix / (float)(Res - 1) * Half * 2f;
                    float z = -Half + iz / (float)(Res - 1) * Half * 2f;
                    heights[iz, ix] = Mathf.Clamp01((DesignHeight(x, z) - BaseY) / Height);
                }
            td.SetHeights(0, 0, heights);

            td.terrainLayers = layers;
            td.alphamapResolution = SplatRes;
            td.SetAlphamaps(0, 0, Splat(td));
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-Half, BaseY, -Half);
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 220f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.On;

            BuildWater(parent);
            Sky();
            return terrain;
        }

        /// <summary>True near any lake or stream (within <paramref name="margin"/> of the water's edge).</summary>
        public static bool NearWater(Vector3 p, float margin)
        {
            var q = new Vector2(p.x, p.z);
            foreach (var l in Lakes) if (Vector2.Distance(q, l.c) < l.r + margin) return true;
            foreach (var s in Streams) if (s.Nearest(q, out _) < 2.6f + margin) return true;
            return false;
        }

        // ------------------------------------------------------------------ shape

        static float P(float x, float z) => Mathf.PerlinNoise(x, z);

        static float Hills(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float hills = (P(x / 70f + 100f, z / 70f + 300f) - 0.5f) * 12f
                        + (P(x / 30f + 50f, z / 30f + 80f) - 0.5f) * 4.8f
                        + (P(x / 12f + 7f, z / 12f + 9f) - 0.5f) * 1.4f + 2.5f;
            hills = Mathf.Max(hills, 0.5f); // no dry pits below lake level (they'd look like they should hold water)
            return hills * Smooth(24f, 58f, r);  // camp stays flat
        }

        static float DesignHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            var q = new Vector2(x, z);

            float h = Hills(x, z);

            // Mountain ring: a rounded, wobbly ring of peaks and saddles around the valley. Long foothills
            // roll up out of the hills (no straight walls), crests are rounded, and height varies along the ring.
            float ax = Mathf.Abs(x), az = Mathf.Abs(z);
            float rounded = Mathf.Pow(ax * ax * ax * ax + az * az * az * az, 0.25f) / Half;            // rounded square
            float warp = (P(x / 55f + 3f, z / 55f + 5f) - 0.5f) * 0.2f + (P(x / 22f + 9f, z / 22f + 1f) - 0.5f) * 0.05f;
            float m = Smooth(0.58f, 1.04f, rounded + warp);
            float massif = 0.5f + 0.65f * P(x / 60f + 41f, z / 60f + 23f);                            // peaks vs saddles
            float ridge = 1f - Mathf.Abs(P(x / 32f + 11f, z / 32f + 13f) * 2f - 1f);
            ridge = ridge * ridge * (3f - 2f * ridge);                                                   // rounded crests
            float bumps = P(x / 14f + 31f, z / 14f + 17f);
            float gravel = (P(x / 3.5f + 5f, z / 3.5f + 8f) - 0.5f);                                   // rocky surface detail
            float mm = m * m * (3f - 2f * m);
            h += mm * massif * (22f + ridge * 26f + bumps * 8f) + m * gravel * 1.2f;

            // Flat pads for the landmarks, at the local hill height.
            foreach (var lm in Landmarks)
            {
                float d = Vector2.Distance(q, new Vector2(lm.x, lm.z));
                if (d < lm.y * 1.6f) h = Mathf.Lerp(Hills(lm.x, lm.z), h, Smooth(lm.y * 0.75f, lm.y * 1.55f, d));
            }

            // Flat pad for the boss totem.
            h = Mathf.Lerp(Hills(TotemXZ.x, TotemXZ.y), h, Smooth(6f, 15f, Vector2.Distance(q, TotemXZ)));

            foreach (var l in Lakes)
            {
                float d = Vector2.Distance(q, l.c);
                if (d > l.r + 30f) continue;
                float bowl = d < l.r ? Ground.WaterY - l.depth * (1f - (d / l.r) * (d / l.r)) : Ground.WaterY + (d - l.r) * 0.22f;
                h = Mathf.Min(h, bowl);
            }

            foreach (var s in Streams)
            {
                float d = s.Nearest(q, out float arc);
                if (d > 40f) continue;
                float prof = s.Profile(arc);
                h = Mathf.Min(h, prof + Mathf.Max(0f, d - 3f) * 0.3f);                    // valley walls
                if (d < 7f && !InLake(q, 3f))                                               // banks: never let the water float over a dip
                    h = Mathf.Max(h, Mathf.Lerp(h, prof + 0.35f, 1f - Smooth(4f, 7f, d)));
                if (d < 3f) h = Mathf.Min(h, prof - 1.1f * (1f - (d / 3f) * (d / 3f)));    // channel
            }
            return h;
        }

        static bool InLake(Vector2 q, float margin)
        {
            foreach (var l in Lakes) if (Vector2.Distance(q, l.c) < l.r + margin) return true;
            return false;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        // ------------------------------------------------------------------ paint

        static float[,,] Splat(TerrainData td)
        {
            var map = new float[SplatRes, SplatRes, 6]; // grass, forest floor, dirt, rock, sand, snow
            for (int iz = 0; iz < SplatRes; iz++)
                for (int ix = 0; ix < SplatRes; ix++)
                {
                    float u = ix / (float)(SplatRes - 1), v = iz / (float)(SplatRes - 1);
                    float x = -Half + u * Half * 2f, z = -Half + v * Half * 2f;
                    var q = new Vector2(x, z);
                    float h = td.GetInterpolatedHeight(u, v) + BaseY;
                    float steep = td.GetSteepness(u, v);
                    float r = q.magnitude;

                    float dWater = float.MaxValue, dStream = float.MaxValue;
                    foreach (var l in Lakes) dWater = Mathf.Min(dWater, Vector2.Distance(q, l.c) - l.r);
                    foreach (var s in Streams) dStream = Mathf.Min(dStream, s.Nearest(q, out _));
                    dWater = Mathf.Min(dWater, dStream - 2.5f);

                    float rock = Mathf.Clamp01(Smooth(30f, 44f, steep) + Smooth(18f, 30f, h) * 0.75f); // grassy foothills, rocky tops
                    float sand = (1f - Smooth(2f, 7f, dWater)) * (1f - Smooth(0.6f, 2.4f, h - Ground.WaterY)) + (1f - Smooth(1.5f, 4f, dStream)) * 0.8f;
                    float trail = 1f - Smooth(1.2f, 2.6f, DistToSegment(q, new Vector2(0f, -12f), TotemXZ));
                    foreach (var lm in Landmarks) // worn paths out to each landmark, and a trampled clearing there
                    {
                        var lc = new Vector2(lm.x, lm.z);
                        trail = Mathf.Max(trail, 1f - Smooth(1f, 2.2f, DistToSegment(q, lc.normalized * 14f, lc)));
                        trail = Mathf.Max(trail, (1f - Smooth(lm.y * 0.45f, lm.y * 0.9f, Vector2.Distance(q, lc))) * 0.8f);
                    }
                    float dirt = (1f - Smooth(8f, 17f, r)) * 0.85f + trail + Smooth(0.72f, 0.82f, P(x / 9f + 40f, z / 9f + 40f)) * 0.6f;
                    float forest = Smooth(0.45f, 0.65f, P(x / 18f + 20f, z / 18f + 60f)) * 0.9f * Smooth(16f, 30f, r);
                    float grass = 1f;

                    // Snow caps: above a wobbly snowline, sliding off the steepest faces.
                    float snowline = 27f + (P(x / 30f + 70f, z / 30f + 90f) - 0.5f) * 6f;
                    float snow = Smooth(snowline, snowline + 5f, h) * (1f - Smooth(52f, 66f, steep) * 0.7f);
                    rock *= 1f - snow;
                    float keep = (1f - rock) * (1f - snow);
                    float[] w = { grass * keep, forest * keep, dirt * keep, rock, Mathf.Clamp01(sand) * keep, snow };
                    float sum = 0f; foreach (var f in w) sum += f;
                    for (int k = 0; k < 6; k++) map[iz, ix, k] = w[k] / sum;
                }
            return map;
        }

        static float DistToSegment(Vector2 q, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(q, a + ab * t);
        }

        static TerrainLayer[] Layers()
        {
            return new[]
            {
                Layer("Grass", new Color(0.36f, 0.55f, 0.23f), new Color(0.46f, 0.63f, 0.27f), new Color(0.27f, 0.44f, 0.17f), 8f, speckle: new Color(0.6f, 0.72f, 0.3f)),
                Layer("ForestFloor", new Color(0.25f, 0.38f, 0.17f), new Color(0.33f, 0.42f, 0.18f), new Color(0.2f, 0.3f, 0.13f), 8f, speckle: new Color(0.45f, 0.32f, 0.16f)),
                Layer("Dirt", new Color(0.47f, 0.36f, 0.24f), new Color(0.55f, 0.43f, 0.3f), new Color(0.38f, 0.28f, 0.18f), 6f, speckle: new Color(0.62f, 0.56f, 0.48f)),
                Layer("Rock", new Color(0.47f, 0.47f, 0.49f), new Color(0.58f, 0.58f, 0.6f), new Color(0.33f, 0.33f, 0.36f), 16f, cracks: true),
                Layer("Sand", new Color(0.64f, 0.57f, 0.41f), new Color(0.72f, 0.65f, 0.48f), new Color(0.52f, 0.46f, 0.33f), 6f, speckle: new Color(0.45f, 0.4f, 0.3f)),
                Layer("Snow", new Color(0.92f, 0.95f, 1f), new Color(1f, 1f, 1f), new Color(0.78f, 0.84f, 0.93f), 9f, speckle: new Color(0.7f, 0.8f, 0.95f)),
            };
        }

        static TerrainLayer Layer(string name, Color mid, Color light, Color dark, float tile, Color? speckle = null, bool cracks = false)
        {
            var tex = SaveTexture($"{TexDir}/{name}.png", GroundTexture(mid, light, dark, speckle, cracks, name.GetHashCode(), name == "Snow" ? 0.14f : name == "Rock" ? 0.06f : 0.03f), false);
            string path = $"{TexDir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0.05f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // ------------------------------------------------------------------ procedural textures

        static int Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        static float Lattice(int x, int y, int period, int seed) =>
            (Hash(((x % period) + period) % period, ((y % period) + period) % period, seed) & 0xffff) / 65535f;

        /// <summary>Tileable value noise: <paramref name="u"/>,<paramref name="v"/> in 0..1, wraps at the edges.</summary>
        static float TileNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0, period, seed), b = Lattice(x0 + 1, y0, period, seed);
            float c = Lattice(x0, y0 + 1, period, seed), d = Lattice(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float TileFbm(float u, float v, int seed)
        {
            float sum = 0f, amp = 0.5f;
            for (int o = 0, period = 4; o < 5; o++, period *= 2) { sum += TileNoise(u, v, period, seed + o) * amp; amp *= 0.5f; }
            return sum / 0.97f;
        }

        static Texture2D GroundTexture(Color mid, Color light, Color dark, Color? speckle, bool cracks, int seed, float smoothness)
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n = TileFbm(u, v, seed);
                    Color c = n < 0.5f ? Color.Lerp(dark, mid, n * 2f) : Color.Lerp(mid, light, (n - 0.5f) * 2f);
                    if (speckle.HasValue && TileNoise(u, v, 64, seed + 99) > 0.82f) c = Color.Lerp(c, speckle.Value, 0.55f);
                    if (cracks)
                    {
                        // Layered strata bands (wobbly horizontal stripes) plus dark cracks and lichen.
                        float strata = Mathf.Sin((v * 9f + TileNoise(u, v, 4, seed + 3) * 1.6f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                        c = Color.Lerp(c, Color.Lerp(dark, light, 0.5f), (strata - 0.5f) * 0.35f);
                        float rn = 1f - Mathf.Abs(TileNoise(u, v, 8, seed + 7) * 2f - 1f);
                        if (rn > 0.93f) c = Color.Lerp(c, dark * 0.8f, (rn - 0.93f) * 6f); // faint cracks (bold ones read as camo from afar)
                        float rn2 = 1f - Mathf.Abs(TileNoise(u, v, 16, seed + 11) * 2f - 1f);
                        if (rn2 > 0.965f) c = Color.Lerp(c, dark * 0.85f, (rn2 - 0.965f) * 8f);
                        if (TileNoise(u, v, 32, seed + 21) > 0.8f) c = Color.Lerp(c, new Color(0.45f, 0.52f, 0.36f), 0.35f); // lichen
                    }
                    c.a = smoothness; // URP terrain reads smoothness from the diffuse alpha (1 = chrome)
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }

        static Texture2D SaveTexture(string path, Texture2D tex, bool normal)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material TerrainMaterial()
        {
            string path = $"{TexDir}/Terrain.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        // ------------------------------------------------------------------ water

        static void BuildWater(Transform parent)
        {
            var root = new GameObject("Water").transform;
            root.SetParent(parent, false);
            var lakeMat = WaterMaterial("WaterLake", new Color(0.2f, 0.48f, 0.6f, 0.78f));
            var streamMat = WaterMaterial("WaterStream", new Color(0.3f, 0.58f, 0.68f, 0.72f));

            for (int i = 0; i < Lakes.Length; i++)
            {
                var l = Lakes[i];
                var go = new GameObject(i == 0 ? "Lake" : "Pond");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(l.c.x, Ground.WaterY, l.c.y);
                go.AddComponent<MeshFilter>().sharedMesh = Disc(l.r + 2.5f, 64);
                go.AddComponent<MeshRenderer>().sharedMaterial = lakeMat;
                go.AddComponent<WaterSurface>().flow = new Vector2(0.004f, 0.007f);
                var vol = go.AddComponent<WaterVolume>();
                vol.isLake = true; vol.radius = l.r; vol.points = new[] { new Vector3(l.c.x, Ground.WaterY, l.c.y) };

                // Animals walk around lakes, not through them.
                var block = go.AddComponent<NavMeshModifierVolume>();
                block.size = new Vector3(l.r * 1.7f, 8f, l.r * 1.7f);
                block.center = Vector3.zero;
                block.area = 1; // Not Walkable
            }

            for (int i = 0; i < Streams.Length; i++)
            {
                var s = Streams[i];
                var go = new GameObject($"Stream{i}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = Ribbon(s, 2.6f);
                go.AddComponent<MeshRenderer>().sharedMaterial = streamMat;
                go.AddComponent<WaterSurface>().flow = new Vector2(0f, -0.35f);
                var vol = go.AddComponent<WaterVolume>();
                vol.radius = 2.6f;
                vol.points = new Vector3[s.pts.Length];
                for (int k = 0; k < s.pts.Length; k++) vol.points[k] = new Vector3(s.pts[k].x, s.Profile(s.cum[k]) - 0.45f, s.pts[k].y);
            }
        }

        static Mesh Disc(float radius, int segs)
        {
            var verts = new Vector3[segs + 1]; var uvs = new Vector2[segs + 1]; var tris = new int[segs * 3];
            verts[0] = Vector3.zero; uvs[0] = Vector2.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uvs[i + 1] = new Vector2(verts[i + 1].x, verts[i + 1].z) / 6f;
                tris[i * 3] = 0; tris[i * 3 + 1] = (i + 1) % segs + 1; tris[i * 3 + 2] = i + 1;
            }
            var m = new Mesh { name = "WaterDisc", vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Ribbon(Stream s, float halfWidth)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            const float step = 1.5f;
            for (float arc = 0f; arc <= s.len + 0.01f; arc += step)
            {
                // Position and tangent along the polyline at this arc length.
                int i = 0;
                while (i < s.pts.Length - 2 && s.cum[i + 1] < arc) i++;
                float segLen = s.cum[i + 1] - s.cum[i];
                float t = Mathf.Clamp01((arc - s.cum[i]) / Mathf.Max(0.01f, segLen));
                Vector2 c = Vector2.Lerp(s.pts[i], s.pts[i + 1], t);
                Vector2 dir = (s.pts[i + 1] - s.pts[i]).normalized;
                Vector2 n = new Vector2(-dir.y, dir.x);
                float y = s.Profile(arc) - 0.45f;
                verts.Add(new Vector3(c.x - n.x * halfWidth, y, c.y - n.y * halfWidth));
                verts.Add(new Vector3(c.x + n.x * halfWidth, y, c.y + n.y * halfWidth));
                uvs.Add(new Vector2(0f, arc / 5f)); uvs.Add(new Vector2(1f, arc / 5f));
                int b = verts.Count - 4;
                if (b >= 0) { tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); }
            }
            var m = new Mesh { name = "StreamRibbon" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Material WaterMaterial(string name, Color color)
        {
            string path = $"{TexDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            var ripple = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/WaterRipple.png") ?? SaveTexture($"{TexDir}/WaterRipple.png", RippleTexture(), false);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/WaterNormal.png") ?? SaveTexture($"{TexDir}/WaterNormal.png", RippleNormal(), true);
            m.SetTexture("_BaseMap", ripple);
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 0.5f);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", 0.92f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D RippleTexture()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = TileFbm(x / (float)N, y / (float)N, 4242);
                    float s = 0.85f + n * 0.25f;
                    tex.SetPixel(x, y, new Color(s, s, s, 1f));
                }
            tex.Apply();
            return tex;
        }

        static Texture2D RippleNormal()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            float H(int x, int y) => TileFbm(((x % N) + N) % N / (float)N, ((y % N) + N) % N / (float)N, 777);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (H(x + 1, y) - H(x - 1, y)) * 6f, dy = (H(x, y + 1) - H(x, y - 1)) * 6f;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
                }
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ sky

        static void Sky()
        {
            string path = $"{TexDir}/Sky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_SunDisk", 2f);
            m.SetFloat("_SunSize", 0.045f);
            m.SetFloat("_SunSizeConvergence", 6f);
            m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetColor("_SkyTint", new Color(0.45f, 0.6f, 0.9f));
            m.SetColor("_GroundColor", new Color(0.42f, 0.47f, 0.4f));
            m.SetFloat("_Exposure", 1.2f);
            EditorUtility.SetDirty(m);
            RenderSettings.skybox = m;
        }
    }
}

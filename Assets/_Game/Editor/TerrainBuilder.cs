using System.IO;
using FriendSlop.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Generates the hunting grounds: a 640 m terrain whose middle ~480 m is a walkable mountain valley
    /// (rolling hills, a lake and two ponds, a snowmelt creek and an outflow gorge), ringed by a real
    /// mountain range that keeps rising past the invisible walls, so there is never a visible edge.
    /// Mountains are ridged noise with domain warping, then shaped by a droplet hydraulic-erosion pass
    /// (gullies, ridges, scree fans) and a thermal pass (no impossible walls). The ground is painted
    /// from slope/height/water/erosion with eight generated layers (each with a normal map), and water
    /// surfaces get guaranteed banks so they can never float above the ground. Camp stays flat at y=0.
    /// </summary>
    public static class TerrainBuilder
    {
        public const float Half = 320f;                // terrain half-size (640 m square)
        public const float PlayHalf = Ground.PlayHalf; // invisible walls (480 m walkable)
        const float BaseY = -8f, Height = 190f;
        const int Res = 1025, SplatRes = 1024;
        const string Dir = "Assets/_Game/Data/Terrain";
        const string TexDir = "Assets/_Game/Materials/Ground";
        /// <summary>Mountain ring (meters from camp, rounded-square): foothills start, main peaks.</summary>
        const float RingStart = 160f, RingPeak = 245f;
        public static readonly Vector2 TotemXZ = new Vector2(-24f, -166f); // Big Doug's totem, out on the north ridge

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
            /// <summary>Water surface height along the stream.</summary>
            public float SurfaceY(float arc) => Profile(arc) - 0.45f;
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

        const float StreamHalfWidth = 2.6f;

        /// <summary>Out-in-the-wild landmarks (x, flat-pad radius, z): hog wallow, ranger tower, abandoned camp.</summary>
        public static readonly Vector3 HogWallow = new Vector3(-98f, 13f, -104f);
        public static readonly Vector3 RangerTower = new Vector3(74f, 10f, -132f);
        public static readonly Vector3 OldCamp = new Vector3(-150f, 11f, -14f);
        public static Vector3[] Landmarks => new[] { HogWallow, RangerTower, OldCamp };

        static readonly Lake[] Lakes =
        {
            new Lake { c = new Vector2(98f, 104f), r = 34f, depth = 1.6f },
            new Lake { c = new Vector2(-98f, 60f), r = 14f, depth = 1.2f },
            new Lake { c = new Vector2(40f, 150f), r = 9f, depth = 1f },
        };

        static Stream[] _streams;
        static Stream[] Streams
        {
            get
            {
                if (_streams != null) return _streams;
                _streams = new[]
                {
                    // Snowmelt creek: out of a gorge in the north range, winding down into the lake.
                    new Stream { pts = new[] { new Vector2(-46f, 268f), new Vector2(-54f, 222f), new Vector2(-40f, 182f), new Vector2(-8f, 158f),
                                               new Vector2(22f, 150f), new Vector2(52f, 136f), new Vector2(76f, 120f), new Vector2(90f, 110f) },
                                 h0 = 16f, h1 = Ground.WaterY + 0.45f },
                    // Outflow: from the lake south-east through a gorge that bends out of sight behind the range.
                    new Stream { pts = new[] { new Vector2(110f, 86f), new Vector2(140f, 50f), new Vector2(165f, 6f), new Vector2(190f, -44f),
                                               new Vector2(216f, -90f), new Vector2(244f, -118f), new Vector2(270f, -124f), new Vector2(292f, -110f) },
                                 h0 = Ground.WaterY + 0.45f, h1 = -2.6f },
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

            // (a) Big shapes: hills + the mountain range. (b) Erode the mountains. (c) Carve camp, pads, water.
            var hm = new float[Res, Res];
            var mask = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float x = X(ix), z = X(iz);
                    hm[iz, ix] = BaseHeight(x, z, out float m);
                    mask[iz, ix] = m;
                }
            _sediment = new float[Res, Res];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Erode(hm, mask, _sediment, 220000, 1337);
            Thermal(hm, mask, 110, 40f);
            Debug.Log($"[Terrain] erosion {sw.ElapsedMilliseconds} ms");

            var heights = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float h = Constrain(X(ix), X(iz), hm[iz, ix]);
                    heights[iz, ix] = Mathf.Clamp01((h - BaseY) / Height);
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
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 320f;
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
            foreach (var s in Streams) if (s.Nearest(q, out _) < StreamHalfWidth + margin) return true;
            return false;
        }

        static float X(int i) => -Half + i / (float)(Res - 1) * Half * 2f;

        // ------------------------------------------------------------------ shape

        static float P(float x, float z) => Mathf.PerlinNoise(x, z);

        static float Hills(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float hills = (P(x / 85f + 100f, z / 85f + 300f) - 0.5f) * 15f
                        + (P(x / 34f + 50f, z / 34f + 80f) - 0.5f) * 5.5f
                        + (P(x / 12f + 7f, z / 12f + 9f) - 0.5f) * 1.4f + 3f;
            hills = Mathf.Max(hills, 0.5f); // no dry pits below lake level (they'd look like they should hold water)
            return hills * Smooth(24f, 60f, r); // camp stays flat
        }

        /// <summary>Ridged multifractal: sharp, branching ridgelines (0..1).</summary>
        static float Ridged(float x, float z)
        {
            float sum = 0f, amp = 1f, freq = 1f / 150f, weight = 1f, norm = 0f;
            for (int o = 0; o < 6; o++)
            {
                float n = 1f - Mathf.Abs(P(x * freq + o * 17.3f + 3.1f, z * freq + o * 31.7f + 7.9f) * 2f - 1f);
                n *= n;
                n *= weight;
                weight = Mathf.Clamp01(n * 1.7f);
                sum += n * amp; norm += amp;
                amp *= 0.5f; freq *= 2.07f;
            }
            return Mathf.Clamp01(sum / norm / 0.62f);
        }

        /// <summary>Hills + the mountain range, before erosion. <paramref name="mountain"/> = 0 in the valley, 1 in the range.</summary>
        static float BaseHeight(float x, float z, out float mountain)
        {
            float h = Hills(x, z);
            float ax = Mathf.Abs(x), az = Mathf.Abs(z);
            float rounded = Mathf.Pow(ax * ax * ax * ax + az * az * az * az, 0.25f);                      // rounded square
            // Domain warp the ring so it wanders: bays of valley, spurs of mountain.
            float warp = (P(x / 95f + 3f, z / 95f + 5f) - 0.5f) * 46f + (P(x / 36f + 9f, z / 36f + 1f) - 0.5f) * 12f;
            float e = rounded + warp;
            float m = Smooth(RingStart, RingPeak, e);
            float outer = Smooth(RingPeak - 40f, Half + 50f, e);                                          // the backdrop keeps rising
            float wx = x + (P(x / 60f + 50f, z / 60f + 20f) - 0.5f) * 50f, wz = z + (P(x / 60f + 80f, z / 60f + 40f) - 0.5f) * 50f;
            float ridged = Ridged(wx, wz);
            float massif = 0.5f + 0.65f * P(x / 120f + 41f, z / 120f + 23f);                              // peaks vs saddles
            float mm = m * m * (3f - 2f * m);
            h += mm * massif * (12f + 54f * ridged) + outer * (22f + 40f * ridged);
            mountain = Mathf.Clamp01(m * 1.3f);
            return h;
        }

        /// <summary>After erosion: camp flat, landmark pads, the totem pad, lakes and streams (with water-safe banks).</summary>
        static float Constrain(float x, float z, float h)
        {
            var q = new Vector2(x, z);

            foreach (var lm in Landmarks)
            {
                float d = Vector2.Distance(q, new Vector2(lm.x, lm.z));
                if (d < lm.y * 1.6f) h = Mathf.Lerp(Hills(lm.x, lm.z), h, Smooth(lm.y * 0.75f, lm.y * 1.55f, d));
            }
            h = Mathf.Lerp(PadHeight(TotemXZ), h, Smooth(6f, 16f, Vector2.Distance(q, TotemXZ)));

            foreach (var l in Lakes)
            {
                float d = Vector2.Distance(q, l.c);
                if (d > l.r + 30f) continue;
                float bowl = d < l.r ? Ground.WaterY - l.depth * (1f - (d / l.r) * (d / l.r)) : Ground.WaterY + (d - l.r) * 0.22f;
                h = Mathf.Min(h, bowl);
            }

            float dStream = float.MaxValue;
            foreach (var s in Streams)
            {
                float d = s.Nearest(q, out float arc);
                dStream = Mathf.Min(dStream, d);
                float prof = s.Profile(arc), surf = s.SurfaceY(arc);
                // Valley walls run out until they meet the slope (no cut-off cliff). Past either end of the
                // stream the valley closes in steeply, so the gorges dead-end inside the mountains.
                Vector2 a0 = s.pts[0], a1 = s.pts[1], b0 = s.pts[^2], b1 = s.pts[^1];
                float over = Mathf.Max(Vector2.Dot(q - b1, (b1 - b0).normalized), Vector2.Dot(q - a0, (a0 - a1).normalized));
                float wall = Mathf.Lerp(0.32f, 1.1f, Smooth(0f, 25f, over));
                h = Mathf.Min(h, prof + Mathf.Max(0f, d - 3f) * wall);                                     // valley walls
                if (d > 40f) continue;
                if (d < 7f && !InLake(q, 3f))
                    h = Mathf.Max(h, Mathf.Lerp(h, prof + 0.35f, 1f - Smooth(4f, 7f, d)));                 // banks
                if (d < 3f) h = Mathf.Min(h, prof - 1.1f * (1f - (d / 3f) * (d / 3f)));                     // channel
                // Water-safe edges: the ribbon's rim always sits under ground, its middle always over the bed.
                if (!InLake(q, 3f))
                {
                    if (d > StreamHalfWidth - 0.7f && d < StreamHalfWidth + 1.2f) h = Mathf.Max(h, surf + 0.18f);
                    else if (d <= StreamHalfWidth - 0.7f) h = Mathf.Min(h, surf - 0.3f);
                }
            }

            // Lake rims: the disc reaches r + 2.5 m; keep the ground just above the surface from r + 0.8 out,
            // except where a stream channel enters (water continues there).
            foreach (var l in Lakes)
            {
                float d = Vector2.Distance(q, l.c);
                if (d > l.r + 0.8f && d < l.r + 4f && dStream > StreamHalfWidth + 0.5f)
                    h = Mathf.Max(h, Ground.WaterY + 0.12f + (d - l.r - 0.8f) * 0.08f);
            }
            return h;
        }

        static float PadHeight(Vector2 c) => Hills(c.x, c.y) + BaseHeightMountainOnly(c.x, c.y);
        static float BaseHeightMountainOnly(float x, float z) => BaseHeight(x, z, out _) - Hills(x, z);

        static bool InLake(Vector2 q, float margin)
        {
            foreach (var l in Lakes) if (Vector2.Distance(q, l.c) < l.r + margin) return true;
            return false;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        // ------------------------------------------------------------------ erosion

        static float[,] _sediment;

        /// <summary>
        /// Droplet hydraulic erosion (after Hans Beyer / Sebastian Lague): raindrops roll downhill, pick up
        /// soil where they speed up and drop it where they slow down, carving gullies and leaving fans.
        /// Works in grid units; only rains on the mountains (<paramref name="mask"/>).
        /// </summary>
        static void Erode(float[,] hm, float[,] mask, float[,] deposits, int droplets, int seed)
        {
            int n = Res;
            float cell = Half * 2f / (n - 1);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) hm[y, x] /= cell;

            const int radius = 3;
            var bx = new System.Collections.Generic.List<int>(); var by = new System.Collections.Generic.List<int>(); var bw = new System.Collections.Generic.List<float>();
            float wsum = 0f;
            for (int oy = -radius; oy <= radius; oy++)
                for (int ox = -radius; ox <= radius; ox++)
                {
                    float d = Mathf.Sqrt(ox * ox + oy * oy);
                    if (d > radius) continue;
                    float w = 1f - d / radius;
                    bx.Add(ox); by.Add(oy); bw.Add(w); wsum += w;
                }
            for (int i = 0; i < bw.Count; i++) bw[i] /= wsum;

            const float inertia = 0.06f, capacityK = 4f, minCap = 0.01f, depositK = 0.3f, erodeK = 0.25f, evaporate = 0.015f, gravity = 4f;
            const int life = 50;
            var rng = new System.Random(seed);

            for (int dI = 0; dI < droplets; dI++)
            {
                float px = 0f, py = 0f;
                bool ok = false;
                for (int tries = 0; tries < 8 && !ok; tries++)
                {
                    px = 4f + (float)rng.NextDouble() * (n - 9);
                    py = 4f + (float)rng.NextDouble() * (n - 9);
                    ok = rng.NextDouble() < mask[(int)py, (int)px];
                }
                if (!ok) continue;

                float dirX = 0f, dirY = 0f, speed = 1f, water = 1f, sediment = 0f;
                for (int step = 0; step < life; step++)
                {
                    int nx = (int)px, ny = (int)py;
                    float fx = px - nx, fy = py - ny;
                    HeightGrad(hm, px, py, out float h0, out float gx, out float gy);
                    dirX = dirX * inertia - gx * (1f - inertia);
                    dirY = dirY * inertia - gy * (1f - inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                    if (len < 1e-6f) break;
                    dirX /= len; dirY /= len;
                    px += dirX; py += dirY;
                    if (px < radius + 1 || py < radius + 1 || px > n - radius - 2 || py > n - radius - 2) break;

                    HeightGrad(hm, px, py, out float h1, out _, out _);
                    float dh = h1 - h0;
                    float cap = Mathf.Max(-dh * speed * water * capacityK, minCap);

                    if (sediment > cap || dh > 0f)
                    {
                        float amount = dh > 0f ? Mathf.Min(dh, sediment) : (sediment - cap) * depositK;
                        sediment -= amount;
                        hm[ny, nx] += amount * (1 - fx) * (1 - fy);
                        hm[ny, nx + 1] += amount * fx * (1 - fy);
                        hm[ny + 1, nx] += amount * (1 - fx) * fy;
                        hm[ny + 1, nx + 1] += amount * fx * fy;
                        deposits[ny, nx] += amount;
                    }
                    else
                    {
                        float amount = Mathf.Min((cap - sediment) * erodeK, -dh);
                        for (int b = 0; b < bw.Count; b++)
                        {
                            int ex = nx + bx[b], ey = ny + by[b];
                            hm[ey, ex] -= amount * bw[b];
                        }
                        sediment += amount;
                    }
                    speed = Mathf.Sqrt(Mathf.Max(0.0001f, speed * speed - dh * gravity));
                    water *= 1f - evaporate;
                }
            }
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { hm[y, x] *= cell; deposits[y, x] *= cell; }
        }

        static void HeightGrad(float[,] hm, float x, float y, out float h, out float gx, out float gy)
        {
            int ix = (int)x, iy = (int)y;
            float fx = x - ix, fy = y - iy;
            float nw = hm[iy, ix], ne = hm[iy, ix + 1], sw = hm[iy + 1, ix], se = hm[iy + 1, ix + 1];
            gx = (ne - nw) * (1 - fy) + (se - sw) * fy;
            gy = (sw - nw) * (1 - fx) + (se - ne) * fx;
            h = nw * (1 - fx) * (1 - fy) + ne * fx * (1 - fy) + sw * (1 - fx) * fy + se * fx * fy;
        }

        /// <summary>Thermal erosion: slopes steeper than the talus angle slump until they're not (no impossible walls).</summary>
        static void Thermal(float[,] hm, float[,] mask, int iterations, float talusDeg)
        {
            int n = Res;
            float cell = Half * 2f / (n - 1);
            float talus = Mathf.Tan(talusDeg * Mathf.Deg2Rad) * cell;
            for (int it = 0; it < iterations; it++)
                for (int y = 1; y < n - 1; y++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        float m = mask[y, x];
                        if (m <= 0.02f) continue;
                        float h = hm[y, x];
                        Slump(hm, x, y, x + 1, y, h, talus, m);
                        Slump(hm, x, y, x - 1, y, hm[y, x], talus, m);
                        Slump(hm, x, y, x, y + 1, hm[y, x], talus, m);
                        Slump(hm, x, y, x, y - 1, hm[y, x], talus, m);
                    }
        }

        static void Slump(float[,] hm, int x, int y, int x2, int y2, float h, float talus, float m)
        {
            float diff = h - hm[y2, x2];
            if (diff <= talus) return;
            float move = (diff - talus) * 0.45f * m;
            hm[y, x] -= move;
            hm[y2, x2] += move;
        }

        // ------------------------------------------------------------------ paint

        // grass, forest floor, dirt, rock, sand, snow, cliff, scree
        const int LayerCount = 8;

        static float[,,] Splat(TerrainData td)
        {
            var map = new float[SplatRes, SplatRes, LayerCount];
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

                    float sed = _sediment != null ? _sediment[Mathf.Clamp(Mathf.RoundToInt(v * (Res - 1)), 0, Res - 1), Mathf.Clamp(Mathf.RoundToInt(u * (Res - 1)), 0, Res - 1)] : 0f;
                    float noise = P(x / 22f + 70f, z / 22f + 90f);

                    float cliff = Smooth(38f, 52f, steep);
                    float rock = Mathf.Clamp01(Smooth(27f, 40f, steep) + Smooth(40f, 85f, h) * 0.5f) * (1f - cliff);
                    float scree = Mathf.Clamp01(sed * 1.6f) * Smooth(8f, 22f, h) * (1f - cliff) + Smooth(22f, 30f, steep) * Smooth(18f, 40f, h) * 0.35f;
                    float sand = (1f - Smooth(2f, 7f, dWater)) * (1f - Smooth(0.6f, 2.4f, h - Ground.WaterY)) + (1f - Smooth(1.5f, 4f, dStream)) * 0.8f;
                    float trail = 1f - Smooth(1.2f, 2.6f, DistToSegment(q, new Vector2(0f, -12f), TotemXZ));
                    foreach (var lm in Landmarks) // worn paths out to each landmark, and a trampled clearing there
                    {
                        var lc = new Vector2(lm.x, lm.z);
                        trail = Mathf.Max(trail, 1f - Smooth(1f, 2.2f, DistToSegment(q, lc.normalized * 14f, lc)));
                        trail = Mathf.Max(trail, (1f - Smooth(lm.y * 0.45f, lm.y * 0.9f, Vector2.Distance(q, lc))) * 0.8f);
                    }
                    float dirt = (1f - Smooth(8f, 17f, r)) * 0.85f + trail + Smooth(0.72f, 0.82f, P(x / 9f + 40f, z / 9f + 40f)) * 0.6f;
                    // Forest floor in patches, and under the pines on the lower slopes (thinning to the treeline).
                    float forest = Smooth(0.45f, 0.65f, P(x / 18f + 20f, z / 18f + 60f)) * 0.9f * Smooth(16f, 30f, r)
                                 + Smooth(10f, 22f, h) * (1f - Smooth(38f, 52f, h)) * 0.7f;
                    float grass = 1f;

                    // Snow above a wobbly snowline, sliding off the steepest faces.
                    float snowline = 70f + (noise - 0.5f) * 14f;
                    float snow = Smooth(snowline, snowline + 9f, h) * (1f - Smooth(46f, 62f, steep) * 0.8f);
                    rock *= 1f - snow; cliff *= 1f - snow * 0.6f; scree *= 1f - snow;
                    float hard = Mathf.Clamp01(rock + cliff + scree);
                    float keep = (1f - hard) * (1f - snow);
                    float[] w = { grass * keep, forest * keep, dirt * keep, rock, Mathf.Clamp01(sand) * keep, snow, cliff, scree };
                    float sum = 0f; foreach (var f in w) sum += f;
                    for (int k = 0; k < LayerCount; k++) map[iz, ix, k] = w[k] / sum;
                }
            return map;
        }

        static float DistToSegment(Vector2 q, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(q, a + ab * t);
        }

        enum Style { Soft, Rock, Cliff, Scree }

        static TerrainLayer[] Layers()
        {
            return new[]
            {
                Layer("Grass", new Color(0.36f, 0.55f, 0.23f), new Color(0.46f, 0.63f, 0.27f), new Color(0.27f, 0.44f, 0.17f), 8f, Style.Soft, speckle: new Color(0.6f, 0.72f, 0.3f)),
                Layer("ForestFloor", new Color(0.25f, 0.38f, 0.17f), new Color(0.33f, 0.42f, 0.18f), new Color(0.2f, 0.3f, 0.13f), 8f, Style.Soft, speckle: new Color(0.45f, 0.32f, 0.16f)),
                Layer("Dirt", new Color(0.47f, 0.36f, 0.24f), new Color(0.55f, 0.43f, 0.3f), new Color(0.38f, 0.28f, 0.18f), 6f, Style.Soft, speckle: new Color(0.62f, 0.56f, 0.48f)),
                Layer("Rock", new Color(0.47f, 0.46f, 0.47f), new Color(0.6f, 0.59f, 0.6f), new Color(0.32f, 0.31f, 0.33f), 14f, Style.Rock),
                Layer("Sand", new Color(0.64f, 0.57f, 0.41f), new Color(0.72f, 0.65f, 0.48f), new Color(0.52f, 0.46f, 0.33f), 6f, Style.Soft, speckle: new Color(0.45f, 0.4f, 0.3f)),
                Layer("Snow", new Color(0.92f, 0.95f, 1f), new Color(1f, 1f, 1f), new Color(0.78f, 0.84f, 0.93f), 11f, Style.Soft, speckle: new Color(0.7f, 0.8f, 0.95f)),
                Layer("Cliff", new Color(0.38f, 0.36f, 0.36f), new Color(0.52f, 0.5f, 0.49f), new Color(0.22f, 0.21f, 0.23f), 20f, Style.Cliff),
                Layer("Scree", new Color(0.5f, 0.47f, 0.43f), new Color(0.64f, 0.61f, 0.56f), new Color(0.34f, 0.32f, 0.3f), 5f, Style.Scree),
            };
        }

        static TerrainLayer Layer(string name, Color mid, Color light, Color dark, float tile, Style style, Color? speckle = null)
        {
            float smooth = name == "Snow" ? 0.14f : style == Style.Soft ? 0.03f : 0.06f;
            var albedo = GroundTexture(mid, light, dark, speckle, style, name.GetHashCode(), smooth, out var heightField);
            var tex = SaveTexture($"{TexDir}/{name}.png", albedo, false);
            var nrm = SaveTexture($"{TexDir}/{name}_N.png", NormalFromHeight(heightField, style == Style.Soft ? 2.5f : 5f), true);
            string path = $"{TexDir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
            layer.diffuseTexture = tex;
            layer.normalMapTexture = nrm;
            layer.normalScale = style == Style.Soft ? 0.6f : 1f;
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0.05f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static Texture2D NormalFromHeight(float[,] hf, float strength)
        {
            int n = hf.GetLength(0);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (hf[y, (x + 1) % n] - hf[y, (x - 1 + n) % n]) * strength;
                    float dy = (hf[(y + 1) % n, x] - hf[(y - 1 + n) % n, x]) * strength;
                    var nv = new Vector3(-dx, -dy, 1f).normalized;
                    tex.SetPixel(x, y, new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f));
                }
            tex.Apply();
            return tex;
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

        /// <summary>Tileable value noise with different cell counts across and down (for streaks).</summary>
        static float TileNoise2(float u, float v, int periodX, int periodY, int seed)
        {
            float x = u * periodX, y = v * periodY;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float L(int a, int b) => (Hash(((a % periodX) + periodX) % periodX, ((b % periodY) + periodY) % periodY, seed) & 0xffff) / 65535f;
            return Mathf.Lerp(Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), fx), Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), fx), fy);
        }

        static float TileFbm(float u, float v, int seed)
        {
            float sum = 0f, amp = 0.5f;
            for (int o = 0, period = 4; o < 5; o++, period *= 2) { sum += TileNoise(u, v, period, seed + o) * amp; amp *= 0.5f; }
            return sum / 0.97f;
        }

        /// <summary>
        /// Tileable ground texture plus a matching height field (for the normal map).
        /// Soft: fbm + speckles. Rock: chunky facets, fine strata, faint cracks, lichen.
        /// Cliff: weathered streaks and ledges (reads well even where the terrain UVs stretch).
        /// Scree: piles of pebbles (cellular).
        /// </summary>
        static Texture2D GroundTexture(Color mid, Color light, Color dark, Color? speckle, Style style, int seed, float smoothness, out float[,] heightField)
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            heightField = new float[N, N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n = TileFbm(u, v, seed);
                    float hgt = n;
                    Color c = n < 0.5f ? Color.Lerp(dark, mid, n * 2f) : Color.Lerp(mid, light, (n - 0.5f) * 2f);
                    if (speckle.HasValue && TileNoise(u, v, 64, seed + 99) > 0.82f) { c = Color.Lerp(c, speckle.Value, 0.55f); hgt += 0.08f; }
                    switch (style)
                    {
                        case Style.Rock:
                        {
                            // Chunky facets: quantized fbm gives flat planes with crisp steps, plus fine strata.
                            float facet = Mathf.Floor(TileFbm(u, v, seed + 5) * 7f) / 7f;
                            c = Color.Lerp(c, Color.Lerp(dark, light, facet), 0.45f);
                            float strata = Mathf.Sin((v * 14f + TileNoise(u, v, 4, seed + 3) * 2f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                            c = Color.Lerp(c, Color.Lerp(dark, light, 0.55f), (strata - 0.5f) * 0.08f);
                            float rn = 1f - Mathf.Abs(TileNoise(u, v, 8, seed + 7) * 2f - 1f);
                            if (rn > 0.94f) c = Color.Lerp(c, dark * 0.75f, (rn - 0.94f) * 7f);
                            if (TileNoise(u, v, 32, seed + 21) > 0.82f) c = Color.Lerp(c, new Color(0.45f, 0.52f, 0.36f), 0.3f); // lichen
                            hgt = facet * 0.7f + n * 0.3f - (rn > 0.94f ? (rn - 0.94f) * 4f : 0f);
                            break;
                        }
                        case Style.Cliff:
                        {
                            // Long weathering streaks (stretched noise) broken by ledges.
                            float streak = TileNoise(u, v, 4, seed + 1) * 0.3f + TileNoise(u * 1f, v, 32, seed + 2) * 0.0f;
                            float s2 = 0f;
                            for (int o = 0, px = 16; o < 3; o++, px *= 2) s2 += TileNoise2(u, v, px, Mathf.Max(1, px / 4), seed + 10 + o) * (0.5f / (o + 1));
                            float ledge = Mathf.Repeat(v * 6f + TileNoise(u, v, 4, seed + 4) * 1.2f, 1f);
                            float ledgeDark = ledge < 0.08f ? 1f - ledge / 0.08f : 0f;
                            float tone = Mathf.Clamp01(n * 0.5f + s2 * 0.8f + streak);
                            c = Color.Lerp(dark, light, tone);
                            c = Color.Lerp(c, dark * 0.8f, ledgeDark * 0.15f);
                            if (TileNoise(u, v, 32, seed + 21) > 0.85f) c = Color.Lerp(c, new Color(0.42f, 0.48f, 0.34f), 0.25f);
                            hgt = tone - ledgeDark * 0.25f;
                            break;
                        }
                        case Style.Scree:
                        {
                            // Pebbles: distance to jittered cell centers (tileable).
                            const int cells = 20;
                            float gx = u * cells, gy = v * cells;
                            int cx = Mathf.FloorToInt(gx), cy = Mathf.FloorToInt(gy);
                            float best = 9f; int bestId = 0;
                            for (int oy = -1; oy <= 1; oy++)
                                for (int ox = -1; ox <= 1; ox++)
                                {
                                    int ix = cx + ox, iy = cy + oy;
                                    int wx = ((ix % cells) + cells) % cells, wy = ((iy % cells) + cells) % cells;
                                    int hsh = Hash(wx, wy, seed);
                                    float jx = (hsh & 0xff) / 255f, jy = ((hsh >> 8) & 0xff) / 255f;
                                    float d = new Vector2(ix + jx - gx, iy + jy - gy).magnitude;
                                    if (d < best) { best = d; bestId = hsh; }
                                }
                            float pebble = Mathf.Clamp01(1f - best * 1.4f);
                            float tint = ((bestId >> 16) & 0xff) / 255f;
                            c = Color.Lerp(dark, Color.Lerp(mid, light, tint), Mathf.Sqrt(pebble));
                            hgt = pebble;
                            break;
                        }
                    }
                    c.a = smoothness; // URP terrain reads smoothness from the diffuse alpha (1 = chrome)
                    tex.SetPixel(x, y, c);
                    heightField[y, x] = hgt;
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
            // No built-in sun disc: DayNightVisuals draws its own sun and moon (the built-in one turned into a dark blot at night).
            m.SetFloat("_SunDisk", 0f);
            m.DisableKeyword("_SUNDISK_SIMPLE"); m.DisableKeyword("_SUNDISK_HIGH_QUALITY"); m.EnableKeyword("_SUNDISK_NONE");
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

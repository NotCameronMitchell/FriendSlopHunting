using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.World
{
    /// <summary>
    /// Local lighting for the shared clock: the sun arcs over and sets orange, a cold moon takes
    /// over at night, fog and ambient light darken, stars fade in, and night lights (campfire,
    /// cabin lantern) switch on. Outside a hunt (main menu) it holds a pleasant morning.
    /// </summary>
    public class DayNightVisuals : MonoBehaviour
    {
        [SerializeField] Light sun;
        [SerializeField] Material skybox;
        [Tooltip("Lights that only glow at night (campfire, lanterns).")]
        [SerializeField] Light[] nightLights = System.Array.Empty<Light>();
        [SerializeField] float menuTime = 0.36f;
        [SerializeField] Shader starShader;
        [SerializeField] Shader discShader;
        [SerializeField] Shader cloudShader;

        Light _moon;
        Transform _stars;
        Material _starMat;
        float[] _nightLightBase;

        static readonly Color DayFog = new Color(0.72f, 0.8f, 0.86f);
        static readonly Color DuskFog = new Color(0.86f, 0.62f, 0.48f);
        static readonly Color NightFog = new Color(0.04f, 0.05f, 0.09f);

        void Start()
        {
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(transform, false);
            _moon = moonGo.AddComponent<Light>();
            _moon.type = LightType.Directional;
            _moon.color = new Color(0.55f, 0.65f, 1f);
            _moon.shadows = LightShadows.Soft;
            _moon.shadowStrength = 0.6f;
            _nightLightBase = new float[nightLights.Length];
            for (int i = 0; i < nightLights.Length; i++) if (nightLights[i]) _nightLightBase[i] = nightLights[i].intensity;
            BuildStars();
            BuildSkyDiscs();
            BuildClouds();
            RenderSettings.ambientMode = AmbientMode.Trilight;
        }

        void Update()
        {
            float t = DayNightCycle.Instance != null && DayNightCycle.Instance.IsSpawned ? DayNightCycle.Instance.TimeOfDay : menuTime;

            // 0 at night, 1 in full day, ramps around sunrise/sunset.
            float day = Smooth(0.2f, 0.29f, t) * (1f - Smooth(0.71f, 0.8f, t));
            float horizon = 1f - Mathf.Abs(Mathf.Sin((t - 0.25f) * Mathf.PI * 2f)); // 1 at sunrise/sunset

            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler((t - 0.25f) * 360f, -35f, 0f);
                sun.intensity = 1.15f * day;
                sun.color = Color.Lerp(new Color(1f, 0.96f, 0.88f), new Color(1f, 0.55f, 0.3f), horizon * day);
                sun.enabled = day > 0.01f;
            }
            if (_moon)
            {
                _moon.transform.rotation = Quaternion.Euler((t - 0.75f) * 360f, 145f, 0f);
                _moon.intensity = 0.32f * (1f - day);
                _moon.enabled = day < 0.99f;
            }
            RenderSettings.sun = day > 0.15f ? sun : _moon;

            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.07f, 0.09f, 0.16f), new Color(0.58f, 0.64f, 0.76f), day);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.05f, 0.06f, 0.1f), new Color(0.48f, 0.52f, 0.5f), day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.03f, 0.03f, 0.05f), new Color(0.3f, 0.32f, 0.26f), day);

            Color fog = Color.Lerp(NightFog, Color.Lerp(DayFog, DuskFog, horizon * 0.7f), day);
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Lerp(0.009f, 0.0042f, day);
            var cam = Camera.main;
            if (cam && cam.clearFlags == CameraClearFlags.SolidColor) cam.backgroundColor = fog;

            if (skybox)
            {
                skybox.SetFloat("_Exposure", Mathf.Lerp(0.12f, 1.2f, day));
                skybox.SetColor("_SkyTint", Color.Lerp(new Color(0.25f, 0.3f, 0.6f), new Color(0.45f, 0.6f, 0.9f), day));
            }

            if (_starMat) _starMat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - day * 1.6f));
            var view = NetworkPlayerCamera();
            if (_stars && view) _stars.position = view.position;
            UpdateSkyDiscs(view, day, horizon);
            UpdateClouds(day, horizon);

            for (int i = 0; i < nightLights.Length; i++)
            {
                var l = nightLights[i];
                if (!l) continue;
                float flicker = 1f + (Mathf.PerlinNoise(Time.time * 3f, i * 7.3f) - 0.5f) * 0.35f;
                l.intensity = _nightLightBase[i] * (1f - day) * flicker;
                l.enabled = day < 0.9f;
            }
        }

        static Transform NetworkPlayerCamera()
        {
            var c = FriendSlop.Player.NetworkPlayer.LocalCamera;
            if (c) return c;
            var main = Camera.main;
            return main ? main.transform : null;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        // ------------------------------------------------------------------ sun & moon

        Transform _sunDisc, _moonDisc;
        Material _sunMat, _moonMat;

        void BuildSkyDiscs()
        {
            if (discShader == null) return;
            _sunMat = new Material(discShader);
            _sunMat.SetFloat("_Core", 0.26f); _sunMat.SetFloat("_Glow", 0.75f); _sunMat.SetFloat("_Craters", 0f);
            _moonMat = new Material(discShader);
            _moonMat.SetFloat("_Core", 0.62f); _moonMat.SetFloat("_Glow", 0.3f); _moonMat.SetFloat("_Craters", 1f);
            _sunDisc = Disc("SunDisc", _sunMat, 70f);
            _moonDisc = Disc("MoonDisc", _moonMat, 30f);
        }

        static Transform Disc(string name, Material mat, float size)
        {
            var go = new GameObject(name);
            var mesh = new Mesh { name = name };
            float h = size * 0.5f;
            mesh.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(h, h, 0), new Vector3(-h, h, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * size * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void UpdateSkyDiscs(Transform view, float day, float horizon)
        {
            if (_sunDisc == null || view == null) return;
            const float dist = 400f;
            // A directional light shines along its forward; the body sits the other way.
            Vector3 sunDir = sun ? -sun.transform.forward : Vector3.up;
            _sunDisc.position = view.position + sunDir * dist;
            _sunDisc.rotation = Quaternion.LookRotation(sunDir);
            float sunUp = Smooth(-0.08f, 0.06f, sunDir.y);
            _sunMat.color = Color.Lerp(new Color(1f, 0.97f, 0.86f), new Color(1f, 0.6f, 0.32f), horizon) * new Color(1f, 1f, 1f, sunUp);

            Vector3 moonDir = _moon ? -_moon.transform.forward : Vector3.up;
            _moonDisc.position = view.position + moonDir * dist;
            _moonDisc.rotation = Quaternion.LookRotation(moonDir);
            float moonUp = Smooth(-0.05f, 0.08f, moonDir.y) * Mathf.Clamp01(1.15f - day * 1.4f);
            _moonMat.color = new Color(0.9f, 0.93f, 1f, moonUp);
        }

        // ------------------------------------------------------------------ clouds

        Transform[] _clouds = System.Array.Empty<Transform>();
        Material _cloudMat;
        static readonly Vector3 Wind = new Vector3(2.4f, 0f, 0.9f);
        const float CloudWrap = 620f;

        /// <summary>Puffy clouds drifting over the valley: each one is a handful of squashed spheres merged into one mesh.</summary>
        void BuildClouds()
        {
            if (cloudShader == null) return;
            _cloudMat = new Material(cloudShader);
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            var rng = new System.Random(4242);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var list = new System.Collections.Generic.List<Transform>();
            for (int c = 0; c < 30; c++)
            {
                float size = R(22f, 60f);
                int puffs = 5 + rng.Next(7);
                var combine = new CombineInstance[puffs];
                for (int p = 0; p < puffs; p++)
                {
                    float along = (p / (float)(puffs - 1) - 0.5f) * size * 1.6f;
                    float s = size * R(0.45f, 0.8f) * (1f - Mathf.Abs(along) / (size * 1.4f));
                    var pos = new Vector3(along, R(0f, size * 0.22f) + (p % 2 == 0 ? 0f : s * 0.25f), R(-size * 0.25f, size * 0.25f));
                    combine[p] = new CombineInstance { mesh = sphere, transform = Matrix4x4.TRS(pos, Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * 0.62f, s * R(0.8f, 1.1f))) };
                }
                var mesh = new Mesh { name = "Cloud" };
                mesh.CombineMeshes(combine, true, true);
                var go = new GameObject("Cloud");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(R(-CloudWrap, CloudWrap), R(118f, 170f), R(-CloudWrap, CloudWrap));
                go.transform.rotation = Quaternion.Euler(0f, R(-25f, 25f) + 20f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _cloudMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                list.Add(go.transform);
            }
            _clouds = list.ToArray();
        }

        void UpdateClouds(float day, float horizon)
        {
            if (_cloudMat == null) return;
            float dt = Time.deltaTime;
            foreach (var c in _clouds)
            {
                var p = c.position + Wind * dt;
                if (p.x > CloudWrap) p.x -= CloudWrap * 2f;
                if (p.z > CloudWrap) p.z -= CloudWrap * 2f;
                c.position = p;
            }
            Color dayLit = Color.white, dayShade = new Color(0.72f, 0.78f, 0.9f);
            Color duskLit = new Color(1f, 0.76f, 0.58f), duskShade = new Color(0.58f, 0.46f, 0.58f);
            Color nightLit = new Color(0.2f, 0.23f, 0.34f), nightShade = new Color(0.08f, 0.09f, 0.15f);
            Color lit = Color.Lerp(nightLit, Color.Lerp(dayLit, duskLit, horizon * 0.85f), day);
            Color shade = Color.Lerp(nightShade, Color.Lerp(dayShade, duskShade, horizon * 0.85f), day);
            _cloudMat.SetColor("_Color", lit);
            _cloudMat.SetColor("_Shadow", shade);
            _cloudMat.SetColor("_Rim", Color.Lerp(new Color(0.3f, 0.35f, 0.5f), Color.white, day));
        }

        /// <summary>A dome of star quads around the viewer (drawn behind everything, faded by time of day).</summary>
        void BuildStars()
        {
            const int count = 1100;
            const float radius = 420f;
            var verts = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var tris = new int[count * 6];
            var rng = new System.Random(99);
            for (int i = 0; i < count; i++)
            {
                // Upper hemisphere, denser toward the horizon a little less.
                float u = (float)rng.NextDouble(), v = (float)rng.NextDouble() * 0.95f + 0.05f;
                float theta = u * Mathf.PI * 2f, phi = Mathf.Acos(v);
                var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                var center = dir * radius;
                var right = Vector3.Cross(dir, Vector3.up).normalized;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right;
                var up = Vector3.Cross(right, dir);
                float size = 0.6f + (float)rng.NextDouble() * (rng.NextDouble() < 0.08 ? 2.2f : 1f);
                verts[i * 4] = center + (-right - up) * size; verts[i * 4 + 1] = center + (right - up) * size;
                verts[i * 4 + 2] = center + (right + up) * size; verts[i * 4 + 3] = center + (-right + up) * size;
                uvs[i * 4] = new Vector2(0, 0); uvs[i * 4 + 1] = new Vector2(1, 0); uvs[i * 4 + 2] = new Vector2(1, 1); uvs[i * 4 + 3] = new Vector2(0, 1);
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
                tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 2;
            }
            var mesh = new Mesh { name = "Stars", vertices = verts, uv = uvs, triangles = tris };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.5f);
            var go = new GameObject("Stars");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (starShader == null) { Destroy(go); return; }
            _starMat = new Material(starShader);
            _starMat.color = new Color(1f, 1f, 1f, 0f);
            r.sharedMaterial = _starMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _stars = go.transform;
        }
    }
}

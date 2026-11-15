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
            RenderSettings.fogDensity = Mathf.Lerp(0.011f, 0.0055f, day);
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

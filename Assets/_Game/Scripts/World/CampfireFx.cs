using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// Cartoon campfire, all local and procedural: layered teardrop flame tongues (orange → yellow →
    /// hot core) that lick, sway and flicker, rising embers, lazy smoke puffs and a flickering light.
    /// </summary>
    public class CampfireFx : MonoBehaviour
    {
        [SerializeField] Material outer, mid, core, ember, smoke;
        [SerializeField] Light fireLight;
        [SerializeField] float size = 1f;

        struct Tongue { public Transform t; public Vector3 baseScale; public float seed, lean; }
        struct Mote { public Transform t; public Renderer r; public Vector3 vel; public float age, life, size; }
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _block;

        Tongue[] _tongues;
        Mote[] _embers, _smoke;
        float _baseIntensity, _baseRange;
        static Mesh _flameMesh;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            var mesh = FlameMesh();
            var list = new System.Collections.Generic.List<Tongue>();
            // (material, count, radius from center, height, width)
            void Ring(Material m, int n, float r, float h, float w, float yOff)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = (i + Random.value * 0.4f) / n * Mathf.PI * 2f;
                    var go = new GameObject("Flame");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(Mathf.Cos(a) * r, yOff, Mathf.Sin(a) * r) * size;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = m;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var s = new Vector3(w, h * Random.Range(0.8f, 1.15f), w) * size;
                    go.transform.localScale = s;
                    list.Add(new Tongue { t = go.transform, baseScale = s, seed = Random.value * 100f, lean = r > 0.01f ? 12f : 4f });
                }
            }
            Ring(outer, 7, 0.24f, 0.95f, 0.32f, 0.12f);
            Ring(mid, 5, 0.14f, 0.95f, 0.3f, 0.14f);
            Ring(core, 3, 0.05f, 0.6f, 0.24f, 0.16f);
            Ring(outer, 1, 0f, 1.25f, 0.4f, 0.12f); // tall central lick
            _tongues = list.ToArray();

            _embers = MakeMotes(14, ember, 0.05f);
            _smoke = MakeMotes(10, smoke, 0.26f);
            for (int i = 0; i < _embers.Length; i++) Respawn(ref _embers[i], true, Random.value);
            for (int i = 0; i < _smoke.Length; i++) Respawn(ref _smoke[i], false, Random.value);

            if (fireLight) { _baseIntensity = fireLight.intensity; _baseRange = fireLight.range; }
        }

        Mote[] MakeMotes(int n, Material m, float s)
        {
            var motes = new Mote[n];
            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(i % 2 == 0 || m == smoke ? PrimitiveType.Sphere : PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                motes[i] = new Mote { t = go.transform, r = mr, size = s };
            }
            return motes;
        }

        void Respawn(ref Mote m, bool isEmber, float startAge)
        {
            if (isEmber)
            {
                m.t.localPosition = new Vector3(Random.Range(-0.2f, 0.2f), 0.4f, Random.Range(-0.2f, 0.2f)) * size;
                m.vel = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.9f, 1.8f), Random.Range(-0.25f, 0.25f)) * size;
                m.life = Random.Range(1.2f, 2.2f);
            }
            else
            {
                m.t.localPosition = new Vector3(Random.Range(-0.1f, 0.1f), 1.2f, Random.Range(-0.1f, 0.1f)) * size;
                m.vel = new Vector3(Random.Range(-0.12f, 0.12f) + 0.08f, Random.Range(0.45f, 0.7f), Random.Range(-0.12f, 0.12f)) * size;
                m.life = Random.Range(3f, 4.5f);
            }
            m.age = startAge * m.life;
        }

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            foreach (var g in _tongues)
            {
                float n1 = Mathf.PerlinNoise(g.seed, t * 3.2f), n2 = Mathf.PerlinNoise(g.seed + 7f, t * 2.1f);
                float lick = 0.75f + n1 * 0.5f + Mathf.Max(0f, Mathf.Sin(t * 9f + g.seed)) * 0.12f;
                g.t.localScale = new Vector3(g.baseScale.x * (1.05f - n1 * 0.15f), g.baseScale.y * lick, g.baseScale.z * (1.05f - n2 * 0.15f));
                g.t.localRotation = Quaternion.Euler((n2 - 0.5f) * g.lean * 2f, g.seed * 40f, (n1 - 0.5f) * g.lean * 2f);
            }

            for (int i = 0; i < _embers.Length; i++)
            {
                ref var m = ref _embers[i];
                m.age += dt;
                if (m.age >= m.life) { Respawn(ref m, true, 0f); continue; }
                m.vel += new Vector3(Mathf.Sin(t * 4f + i) * 0.6f, 0f, Mathf.Cos(t * 3.3f + i) * 0.6f) * dt * size;
                m.t.localPosition += m.vel * dt;
                float k = 1f - m.age / m.life;
                m.t.localScale = Vector3.one * m.size * size * k;
                m.t.localRotation = Quaternion.Euler(t * 200f + i * 30f, t * 150f, 0f);
            }

            for (int i = 0; i < _smoke.Length; i++)
            {
                ref var m = ref _smoke[i];
                m.age += dt;
                if (m.age >= m.life) { Respawn(ref m, false, 0f); continue; }
                m.t.localPosition += m.vel * dt;
                float k = m.age / m.life;
                m.t.localScale = Vector3.one * m.size * size * (0.5f + k * 1.6f);
                // See-through grey that thins out as it rises (smoke uses a transparent material).
                _block.SetColor(BaseColorId, new Color(0.6f, 0.6f, 0.63f, 0.28f * (1f - k) * Mathf.Clamp01(m.age * 3f)));
                _block.SetColor(EmissionId, Color.black);
                m.r.SetPropertyBlock(_block);
            }

            if (fireLight)
            {
                float f = Mathf.PerlinNoise(t * 7f, 0.3f) * 0.6f + Mathf.PerlinNoise(t * 17f, 5f) * 0.4f;
                fireLight.intensity = _baseIntensity * (0.78f + f * 0.4f);
                fireLight.range = _baseRange * (0.92f + f * 0.12f);
            }
        }

        /// <summary>A teardrop lathe: round at the base, pointed at the tip (1 unit tall, 1 unit wide).</summary>
        static Mesh FlameMesh()
        {
            if (_flameMesh) return _flameMesh;
            const int rings = 12, segs = 12;
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float radius = 0.5f * Mathf.Sin(Mathf.PI * Mathf.Pow(v, 0.62f)) * (1f - v * 0.25f);
                for (int s = 0; s <= segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * radius, v, Mathf.Sin(a) * radius));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int i0 = r * (segs + 1) + s, i1 = i0 + segs + 1;
                    tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                    tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
                }
            _flameMesh = new Mesh { name = "Flame" };
            _flameMesh.SetVertices(verts);
            _flameMesh.SetTriangles(tris, 0);
            _flameMesh.RecalculateNormals();
            _flameMesh.RecalculateBounds();
            return _flameMesh;
        }
    }
}

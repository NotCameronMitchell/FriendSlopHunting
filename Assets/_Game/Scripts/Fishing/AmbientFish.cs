using UnityEngine;

namespace FriendSlop.Fishing
{
    /// <summary>
    /// Cosmetic life in a lake (every peer, no networking): dark fish shapes cruising just under the surface in
    /// lazy loops, and now and then one leaps out with a splash. Tells you "there are fish here" at a glance.
    /// Sits on a lake's WaterVolume.
    /// </summary>
    public class AmbientFish : MonoBehaviour
    {
        public int count = 10;
        World.WaterVolume _vol;
        Transform[] _fish;
        float[] _angle, _radius, _speed, _depth, _phase;
        Vector3 _center;
        float _lakeR, _nextJump;
        Transform _jumper;
        float _jumpT = -1f;
        Vector3 _jumpFrom, _jumpTo;
        static Material _mat;

        void Start()
        {
            _vol = GetComponent<World.WaterVolume>();
            if (_vol == null || !_vol.isLake || _vol.points.Length == 0) { enabled = false; return; }
            _center = _vol.points[0];
            _lakeR = _vol.radius;
            count = Mathf.Clamp(Mathf.RoundToInt(_lakeR * 0.45f), 4, 16);
            _mat ??= new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.05f, 0.12f, 0.16f, 1f) };
            _fish = new Transform[count];
            _angle = new float[count]; _radius = new float[count]; _speed = new float[count]; _depth = new float[count]; _phase = new float[count];
            for (int i = 0; i < count; i++)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(body.GetComponent<Collider>());
                body.name = "AmbientFish";
                body.transform.SetParent(transform, true);
                float len = Random.Range(0.25f, 0.6f);
                body.transform.localScale = new Vector3(len * 0.3f, len * 0.12f, len);
                body.GetComponent<Renderer>().sharedMaterial = _mat;
                body.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _fish[i] = body.transform;
                _angle[i] = Random.Range(0f, Mathf.PI * 2f);
                _radius[i] = Random.Range(0.25f, 0.8f) * _lakeR;
                _speed[i] = Random.Range(0.25f, 0.8f) * (Random.value < 0.5f ? -1f : 1f) / Mathf.Max(1f, _radius[i]);
                _depth[i] = Random.Range(0.35f, 0.9f);
                _phase[i] = Random.value * 10f;
            }
            _nextJump = Time.time + Random.Range(6f, 15f);
        }

        void Update()
        {
            if (_fish == null) return;
            float t = Time.time;
            for (int i = 0; i < _fish.Length; i++)
            {
                if (_fish[i] == _jumper) continue;
                _angle[i] += _speed[i] * Time.deltaTime * 2f;
                float wobble = Mathf.Sin(t * 0.3f + _phase[i]) * 0.15f;
                float r = _radius[i] * (1f + wobble);
                var p = _center + new Vector3(Mathf.Cos(_angle[i]) * r, -_depth[i], Mathf.Sin(_angle[i]) * r);
                var tangent = new Vector3(-Mathf.Sin(_angle[i]), 0f, Mathf.Cos(_angle[i])) * Mathf.Sign(_speed[i]);
                _fish[i].position = p;
                _fish[i].rotation = Quaternion.LookRotation(tangent) * Quaternion.Euler(0f, Mathf.Sin(t * 6f + _phase[i]) * 12f, 0f);
            }

            // Occasional leap: a fish arcs out of the water and splashes back in.
            if (_jumpT < 0f && t > _nextJump)
            {
                int i = Random.Range(0, _fish.Length);
                _jumper = _fish[i];
                _jumpFrom = _jumper.position;
                Vector3 dir = _jumper.forward; dir.y = 0f;
                _jumpTo = _jumpFrom + dir.normalized * Random.Range(1.2f, 2.4f);
                _jumpT = 0f;
                Audio.SfxPlayer.PlayAt(Audio.Sfx.Thud, new Vector3(_jumpFrom.x, _center.y, _jumpFrom.z), 0.25f, 2.4f, 3f, 35f);
            }
            if (_jumpT >= 0f)
            {
                _jumpT += Time.deltaTime / 0.7f;
                float k = Mathf.Clamp01(_jumpT);
                var p = Vector3.Lerp(_jumpFrom, _jumpTo, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.4f;
                _jumper.rotation = Quaternion.LookRotation((_jumpTo - _jumpFrom).normalized + Vector3.up * Mathf.Cos(k * Mathf.PI) * 1.2f);
                _jumper.position = p;
                if (_jumpT >= 1f)
                {
                    Audio.SfxPlayer.PlayAt(Audio.Sfx.Thud, new Vector3(_jumpTo.x, _center.y, _jumpTo.z), 0.3f, 2f, 3f, 35f);
                    var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(ring.GetComponent<Collider>());
                    ring.transform.position = new Vector3(_jumpTo.x, _center.y + 0.02f, _jumpTo.z);
                    ring.AddComponent<SplashRing>().size = 0.7f;
                    _jumpT = -1f; _jumper = null;
                    _nextJump = t + Random.Range(5f, 14f);
                }
            }
        }
    }
}

using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Profile
{
    /// <summary>
    /// Your hunter, rendered off-screen (high above the map) into a texture the menus draw. Shows
    /// the outfit you're browsing, idles and waves now and then; drag to spin. Only renders while
    /// a menu asks for it.
    /// </summary>
    public class CharacterPreview : MonoBehaviour
    {
        public static CharacterPreview Instance { get; private set; }

        [SerializeField] GameObject hunterPrefab;

        static readonly Vector3 StagePosition = new Vector3(0f, 500f, 0f);

        RenderTexture _texture;
        Camera _camera;
        Transform _rig;
        HunterBody _hunter;
        float _yaw = 200f;
        float _lastRequest = -10f;
        float _nextWave;
        string _key;

        void Awake()
        {
            Instance = this;
            BuildStage();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_texture != null) _texture.Release();
        }

        /// <summary>Get the preview image for this frame, showing the given look.</summary>
        public Texture Render(string outfit, string head, string hat, string accessory, int colorIndex)
        {
            _lastRequest = Time.unscaledTime;
            string key = $"{outfit}|{head}|{hat}|{accessory}|{colorIndex}";
            if (key != _key && _hunter != null)
            {
                bool first = _key == null;
                _key = key;
                _hunter.Apply(outfit, head, hat, accessory, Cosmetics.BodyColor(colorIndex, 0));
                if (!first) _hunter.Wave(); // a little "ooh, new look" flourish
            }
            return _texture;
        }

        public void Spin(float degrees) => _yaw += degrees;

        void Update()
        {
            bool wanted = Time.unscaledTime - _lastRequest < 0.25f;
            if (_camera.enabled != wanted) _camera.enabled = wanted;
            if (_rig) _rig.gameObject.SetActive(wanted);
            if (!wanted) return;
            _yaw += Time.unscaledDeltaTime * 10f; // slow idle turn
            _rig.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (Time.unscaledTime > _nextWave)
            {
                _nextWave = Time.unscaledTime + Random.Range(7f, 12f);
                if (_hunter) _hunter.Wave();
            }
        }

        void BuildStage()
        {
            _texture = new RenderTexture(512, 640, 24) { name = "CharacterPreview", antiAliasing = 4 };

            var stage = new GameObject("PreviewStage").transform;
            stage.SetParent(transform, false);
            stage.position = StagePosition;

            _rig = new GameObject("Turntable").transform;
            _rig.SetParent(stage, false);
            if (hunterPrefab != null)
            {
                var go = Instantiate(hunterPrefab, _rig);
                go.transform.localPosition = Vector3.zero;
                _hunter = go.GetComponent<HunterBody>();
                // The stage floats in the sky; keep the grounded check from flipping to "falling".
                var floorCol = new GameObject("PreviewFloorCollider").AddComponent<BoxCollider>();
                floorCol.transform.SetParent(stage, false);
                floorCol.size = new Vector3(3f, 0.1f, 3f);
                floorCol.center = new Vector3(0f, -0.05f, 0f);
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetParent(stage, false);
            floor.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            floor.transform.localScale = new Vector3(1.8f, 0.03f, 1.8f);
            floor.GetComponent<Renderer>().sharedMaterial = Simple(new Color(0.3f, 0.45f, 0.25f));

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = new Vector3(0f, 1.25f, 4.6f);
            camGo.transform.localRotation = Quaternion.Euler(4f, 180f, 0f);
            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.fieldOfView = 32f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.18f, 0.22f, 0.26f, 1f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;
            _camera.enabled = false;

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(stage, false);
            lightGo.transform.localPosition = new Vector3(1.5f, 3f, 2.5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 3f;
        }

        static Material Simple(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            m.SetColor("_BaseColor", c);
            return m;
        }
    }
}

using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>Scrolls a water material so lakes shimmer and streams visibly flow.</summary>
    public class WaterSurface : MonoBehaviour
    {
        public Vector2 flow = new Vector2(0.01f, 0.02f);
        Material _mat;
        Vector2 _offset;

        void Start()
        {
            var r = GetComponent<Renderer>();
            if (r) _mat = r.material; // instance: each body scrolls at its own pace
        }

        void Update()
        {
            if (_mat == null) return;
            _offset += flow * Time.deltaTime;
            _offset.x %= 1f; _offset.y %= 1f;
            _mat.SetTextureOffset("_BaseMap", _offset);
        }
    }
}

using UnityEngine;

namespace FriendSlop.Fishing
{
    /// <summary>A flat white ring that spreads and fades where something hits the water.</summary>
    public class SplashRing : MonoBehaviour
    {
        public float size = 0.5f;
        float _t;
        void Update()
        {
            _t += Time.deltaTime / 0.6f;
            float s = Mathf.Lerp(0.1f, size * 2f, 1f - (1f - _t) * (1f - _t));
            transform.localScale = new Vector3(s, 0.005f, s);
            if (_t >= 1f) Destroy(gameObject);
        }
    }
}

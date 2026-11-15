using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// Camp is a refuge. Inside this radius:
    ///  - fighters (aggressive/territorial/predator animals) never spawn (plus a margin),
    ///  - they won't start a fight with a player standing inside,
    ///  - they give up a chase the moment their target makes it back,
    ///  - and any fighter that wanders in walks back out.
    /// Skittish animals (rabbits, ducks, deer) can still roam near camp.
    /// One per map; first step toward distance-based difficulty rings on the bigger map.
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        [SerializeField] float radius = 24f;

        static SafeZone _instance;

        public static bool Exists => _instance != null;
        public static float Radius => _instance != null ? _instance.radius : 0f;
        public static Vector3 Center => _instance != null ? _instance.transform.position : Vector3.zero;

        void OnEnable() => _instance = this;
        void OnDisable() { if (_instance == this) _instance = null; }

        /// <summary>True if the point (ignoring height) is inside the zone grown by margin.</summary>
        public static bool Contains(Vector3 point, float margin = 0f)
        {
            if (_instance == null) return false;
            Vector3 d = point - _instance.transform.position;
            d.y = 0f;
            float r = _instance.radius + margin;
            return d.sqrMagnitude < r * r;
        }

        /// <summary>A point just outside the zone, in the direction of the given point.</summary>
        public static Vector3 ExitPointFor(Vector3 from, float extra = 8f)
        {
            Vector3 c = Center;
            Vector3 dir = from - c;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            return c + dir.normalized * (Radius + extra);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}

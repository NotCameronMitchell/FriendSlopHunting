using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// Registry of water bodies (lakes as circles, streams as chains of segments) so gameplay can
    /// ask "is this point in water, and how deep?" without colliders.
    /// </summary>
    public class WaterVolume : MonoBehaviour
    {
        public static readonly List<WaterVolume> All = new();

        [Tooltip("Lake: one point + radius. Stream: a polyline. x,z = position, y = water surface height.")]
        public Vector3[] points = System.Array.Empty<Vector3>();
        public float radius = 2.5f; // lake radius / stream half-width
        public bool isLake;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Depth of water above <paramref name="p"/> (0 when dry).</summary>
        public static float DepthAt(Vector3 p)
        {
            float best = 0f;
            foreach (var w in All)
            {
                float surface = w.SurfaceAt(p);
                if (!float.IsNaN(surface)) best = Mathf.Max(best, surface - p.y);
            }
            return best;
        }

        float SurfaceAt(Vector3 p)
        {
            if (points.Length == 0) return float.NaN;
            if (isLake)
            {
                var c = points[0];
                float dx = p.x - c.x, dz = p.z - c.z;
                return dx * dx + dz * dz <= radius * radius ? c.y : float.NaN;
            }
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = new Vector2(points[i].x, points[i].z), b = new Vector2(points[i + 1].x, points[i + 1].z);
                Vector2 q = new Vector2(p.x, p.z), ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                if ((a + ab * t - q).sqrMagnitude <= radius * radius) return Mathf.Lerp(points[i].y, points[i + 1].y, t);
            }
            return float.NaN;
        }
    }
}

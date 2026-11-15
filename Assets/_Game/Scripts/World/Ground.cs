using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>Terrain height and water queries shared by spawners, grass and players.</summary>
    public static class Ground
    {
        /// <summary>World height of lake surfaces (streams have their own sloping surfaces).</summary>
        public const float WaterY = -0.4f;

        /// <summary>Ground height under a point (terrain if present, else 0).</summary>
        public static float HeightAt(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }

        /// <summary>Slope in degrees at a point (0 without terrain).</summary>
        public static float SteepnessAt(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            if (t == null) return 0f;
            var local = p - t.transform.position;
            var size = t.terrainData.size;
            return t.terrainData.GetSteepness(Mathf.Clamp01(local.x / size.x), Mathf.Clamp01(local.z / size.z));
        }

        /// <summary>Snap a point onto the ground.</summary>
        public static Vector3 OnGround(Vector3 p) => new Vector3(p.x, HeightAt(p), p.z);
    }
}

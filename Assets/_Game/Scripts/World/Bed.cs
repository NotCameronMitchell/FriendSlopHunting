using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>A cabin bed. Look at it and press E at night to sleep; when every hunter is asleep the night is skipped.</summary>
    public class Bed : MonoBehaviour
    {
        public static readonly List<Bed> All = new();
        public const float InteractRange = 3f;

        [Tooltip("Where a sleeping hunter lies (feet end at -Z, head at +Z).")]
        public Transform sleepPoint;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public Vector3 SleepPosition => sleepPoint ? sleepPoint.position : transform.position + Vector3.up * 0.6f;
        public Quaternion SleepRotation => sleepPoint ? sleepPoint.rotation : transform.rotation;
    }
}

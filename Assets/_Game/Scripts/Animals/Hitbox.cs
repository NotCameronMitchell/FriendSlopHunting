using UnityEngine;

namespace FriendSlop.Animals
{
    /// <summary>Marks a collider as part of an animal's body. Head hitboxes give headshots.</summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] bool isHead;
        public bool IsHead => isHead;
    }
}

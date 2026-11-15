using UnityEngine;

namespace FriendSlop.Animals
{
    /// <summary>Tuning data for one kind of animal. One asset per species (Rabbit, Goose, Deer…).</summary>
    [CreateAssetMenu(menuName = "FriendSlop/Animal Species", fileName = "NewSpecies")]
    public class AnimalSpecies : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Rabbit";
        public Color furColor = new Color(0.55f, 0.4f, 0.28f);

        [Header("Economy")]
        [Tooltip("Price for an average-weight animal with no style bonus.")]
        public int basePrice = 12;
        public float minWeightKg = 1.5f;
        public float maxWeightKg = 3f;

        [Header("Combat")]
        public float maxHealth = 50f;

        [Header("Movement")]
        public float wanderSpeed = 1.6f;
        public float fleeSpeed = 7.5f;
        [Tooltip("Players closer than this make the animal bolt.")]
        public float spookRadius = 9f;
        [Tooltip("Gunshots landing closer than this make the animal bolt.")]
        public float hearingRadius = 30f;
        [Tooltip("How far from its spawn point it wanders.")]
        public float wanderRadius = 14f;

        [Header("Feel")]
        [Tooltip("Height of the hop animation in meters (0 for walkers).")]
        public float hopHeight = 0.18f;
        public float hopsPerMeter = 1.6f;

        public float AverageWeight => (minWeightKg + maxWeightKg) * 0.5f;
    }
}

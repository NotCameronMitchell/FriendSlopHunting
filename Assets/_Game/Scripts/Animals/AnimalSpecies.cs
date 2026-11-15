using UnityEngine;

namespace FriendSlop.Animals
{
    public enum Temperament
    {
        /// <summary>Runs from players and gunshots (rabbit, duck, deer).</summary>
        Skittish,
        /// <summary>Charges players who get close or shoot it (goose, boar).</summary>
        Aggressive,
        /// <summary>Minds its business unless you get really close or hurt it (moose).</summary>
        Territorial,
        /// <summary>Hunts players on sight, often in packs (wolf, bear, bosses).</summary>
        Predator,
    }

    public enum BodyStyle { Hopper, Quadruped, Bird }

    public enum Voice { Squeak, Quack, Honk, Grunt, Growl, Bellow, Roar }

    /// <summary>Tuning data for one kind of animal. One asset per species.</summary>
    [CreateAssetMenu(menuName = "FriendSlop/Animal Species", fileName = "NewSpecies")]
    public class AnimalSpecies : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Rabbit";
        public Color furColor = new Color(0.55f, 0.4f, 0.28f);
        public Color accentColor = new Color(0.9f, 0.9f, 0.85f);

        [Header("Economy")]
        [Tooltip("Price for an average-weight animal with no style bonus.")]
        public int basePrice = 12;
        public float minWeightKg = 1.5f;
        public float maxWeightKg = 3f;

        [Header("Combat")]
        public float maxHealth = 50f;
        public Temperament temperament = Temperament.Skittish;
        [Tooltip("Aggressive/territorial: trigger distance. Predator: hunting sight range.")]
        public float aggroRadius = 0f;
        [Tooltip("Gives up the chase beyond this distance.")]
        public float loseInterestRadius = 40f;
        public float attackDamage = 0f;
        public float attackRange = 1.5f;
        public float attackCooldown = 1.2f;
        public float knockback = 6f;
        [Tooltip("Run speed while chasing/charging.")]
        public float chargeSpeed = 8f;
        [Tooltip("Fighters bail out when HP drops below this fraction (0 = never).")]
        [Range(0f, 1f)] public float fleeBelowHealth = 0f;

        [Header("Movement")]
        public float wanderSpeed = 1.6f;
        public float fleeSpeed = 7.5f;
        [Tooltip("Skittish: players closer than this make it bolt.")]
        public float spookRadius = 9f;
        [Tooltip("Gunshots landing closer than this get a reaction.")]
        public float hearingRadius = 30f;
        [Tooltip("How far from its spawn point it wanders.")]
        public float wanderRadius = 14f;

        [Header("Body (procedural model, meters)")]
        public BodyStyle body = BodyStyle.Hopper;
        public float bodyLength = 0.5f;
        public float bodyHeight = 0.25f;
        public float legLength = 0.1f;
        public float headSize = 0.24f;
        public bool antlers;
        public bool tusks;
        public bool bigEars;
        [Tooltip("Uniform scale on top of the measurements (bosses).")]
        public float scale = 1f;

        [Header("Feel")]
        [Tooltip("Height of the hop animation in meters (0 for walkers).")]
        public float hopHeight = 0.18f;
        public float hopsPerMeter = 1.6f;
        public Voice voice = Voice.Squeak;
        public float voicePitch = 1f;

        [Header("Spawning")]
        public int population = 4;
        public int packSize = 1;
        public float minSpawnDistance = 22f;
        public float maxSpawnDistance = 70f;
        [HideInInspector] public int dataVersion; // builder migrations (e.g. v2 = big terrain map rings)

        [Header("Boss")]
        public bool isBoss;
        public string bossTitle = "";
        [Tooltip("Paid to the team instantly on kill.")]
        public int bounty = 0;

        public float AverageWeight => (minWeightKg + maxWeightKg) * 0.5f;
        public bool Fights => temperament != Temperament.Skittish && attackDamage > 0f;
        /// <summary>Approximate body radius for spacing, attacks and agent size.</summary>
        public float Radius => Mathf.Max(bodyHeight, bodyLength * 0.35f) * 0.6f * scale;
    }
}

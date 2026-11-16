using UnityEngine;

namespace FriendSlop.Weapons
{
    public enum WeaponKind
    {
        /// <summary>Instant ray (pistol, rifle, shotgun pellets, guns).</summary>
        Hitscan,
        /// <summary>Physical shot that flies and drops (bow, slingshot, bazooka).</summary>
        Projectile,
        /// <summary>Thrown, flies out and comes back to the thrower.</summary>
        Boomerang,
        /// <summary>Not a weapon: cast a bobber into water and reel in fish (see FishingRod).</summary>
        Rod,
        /// <summary>Not a weapon: a loud animal call that lures nearby animals toward you (predators too!).</summary>
        Call,
    }

    public enum FireMode { Semi, Auto }

    public enum ShotSound { Gunshot, HeavyGunshot, LightGunshot, Bow, Slingshot, Rocket, Throw, Call }

    public enum WeaponModel { Pistol, Revolver, Uzi, MachineGun, Minigun, Shotgun, Rifle, Bow, Slingshot, Boomerang, Bazooka, FishingRod, Call }

    /// <summary>
    /// Everything that makes a weapon feel different, as data. Ammo is unlimited by design;
    /// weapons are balanced by fire rate, spread, damage and price instead.
    /// </summary>
    [CreateAssetMenu(menuName = "FriendSlop/Weapon", fileName = "NewWeapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Shop")]
        public string displayName = "Pistol";
        [Tooltip("0 = free starter, always owned.")]
        public int price;
        [Tooltip("Hunter level needed to buy it in the shop (1 = from the start).")]
        public int requiredLevel = 1;
        [TextArea] public string blurb = "";

        [Header("Firing")]
        public WeaponKind kind = WeaponKind.Hitscan;
        public FireMode fireMode = FireMode.Semi;
        [Tooltip("Seconds between shots.")]
        public float fireInterval = 0.3f;
        [Tooltip("Seconds of holding the trigger before it starts firing (minigun).")]
        public float spinUpTime;
        public float damage = 30f;
        [Tooltip("Headshot damage = damage × this.")]
        public float headshotMultiplier = 3f;
        [Tooltip("Rays per shot (shotgun > 1).")]
        public int pellets = 1;
        [Tooltip("Cone half-angle in degrees when hip firing.")]
        public float spread = 1f;
        [Tooltip("Cone half-angle in degrees when aiming (right mouse).")]
        public float aimSpread = 0.2f;
        public float range = 150f;
        [Tooltip("Field of view when aiming. Lower = more zoom.")]
        public float aimFov = 55f;
        [Tooltip("Camera kick per shot, degrees.")]
        public float recoil = 2f;
        [Tooltip("Push applied to physics boxes and carcasses per hit.")]
        public float impulse = 10f;
        [Tooltip("Animals within this many meters of the shot panic. 0 = silent (bow).")]
        public float noiseRadius = 30f;

        [Header("Projectile / boomerang")]
        public float projectileSpeed = 40f;
        public float projectileGravity = 9.81f;
        [Tooltip("Boomerang: how far it flies before turning back.")]
        public float boomerangRange = 18f;

        [Header("Explosion (0 radius = none)")]
        public float explosionRadius;
        public float explosionDamage;
        public float explosionForce = 12f;

        [Header("Feel")]
        public ShotSound sound = ShotSound.Gunshot;
        public float soundPitch = 1f;
        public float soundVolume = 0.75f;
        public bool tracer = true;
        public WeaponModel model = WeaponModel.Pistol;
        public Color tint = new Color(0.15f, 0.14f, 0.13f);

        public bool IsExplosive => explosionRadius > 0f;
        public bool IsAuto => fireMode == FireMode.Auto;
    }
}

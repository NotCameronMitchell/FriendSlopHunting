using UnityEngine;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// The ordered list of every weapon. A weapon's index here is its network id and its
    /// number key (1 = index 0). Index 0 must be the free starter (the pistol).
    /// </summary>
    [CreateAssetMenu(menuName = "FriendSlop/Weapon Catalog", fileName = "WeaponCatalog")]
    public class WeaponCatalog : ScriptableObject
    {
        public const int MaxWeapons = 31; // unlocks are a bitmask in an int

        public WeaponDefinition[] weapons = System.Array.Empty<WeaponDefinition>();

        public int Count => weapons.Length;

        public WeaponDefinition Get(int index) =>
            index >= 0 && index < weapons.Length ? weapons[index] : null;

        public static bool IsOwned(int mask, int index) => index == 0 || (mask & (1 << index)) != 0;

        /// <summary>Key for an inventory slot: "1".."9", "0" for the tenth, "scroll" beyond that.</summary>
        public static string KeyLabel(int slot) => slot < 10 ? ((slot + 1) % 10).ToString() : "scroll";
    }
}

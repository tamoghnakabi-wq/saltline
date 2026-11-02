using UnityEngine;

namespace Saltline.Weapons
{
    /// <summary>Registry indexed by weapon id. One asset, referenced by the player prefab.</summary>
    [CreateAssetMenu(menuName = "Saltline/Weapon Database", fileName = "WeaponDatabase")]
    public class WeaponDatabase : ScriptableObject
    {
        public WeaponDefinition[] weapons;
        public byte defaultPrimary;
        public byte defaultSecondary = 4;

        public WeaponDefinition Get(byte id)
        {
            if (weapons == null) return null;
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] != null && weapons[i].id == id) return weapons[i];
            return null;
        }

        public bool IsValidPrimary(byte id)
        {
            var w = Get(id);
            return w != null && w.slot == WeaponSlot.Primary;
        }
    }
}

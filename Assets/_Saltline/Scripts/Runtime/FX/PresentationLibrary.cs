using UnityEngine;

namespace Saltline.FX
{
    /// <summary>Materials and other presentation assets, loaded from Resources so builds include them.</summary>
    [CreateAssetMenu(menuName = "Saltline/Presentation Library", fileName = "PresentationLibrary")]
    public class PresentationLibrary : ScriptableObject
    {
        public Material teamRed;
        public Material teamBlue;
        public Material neutralBody;
        public Material gunMetal;
        public Material gunAccent;
        public Material tracer;
        public Material impact;
        public Material muzzleFlash;
        [Tooltip("Imported weapon models by WeaponClass (null = greybox stand-in). Set by WeaponArtFactory.")]
        public GameObject[] weaponModels = new GameObject[5];
        [Tooltip("Weapon data for presentation-only code (menu stand, showcases). Gameplay reads GameConfig.")]
        public Saltline.Weapons.WeaponDatabase weapons;
        [Tooltip("Skinned operator (P_Operator with an OperatorRig). Null = hitbox-capsule bodies. Set by CharacterArtFactory.")]
        public GameObject operatorPrefab;
        public Material operatorTeamRed;
        public Material operatorTeamBlue;
        public Material operatorTeamNeutral;

        static PresentationLibrary s_Instance;
        public static PresentationLibrary Instance
        {
            get
            {
                if (s_Instance == null) s_Instance = Resources.Load<PresentationLibrary>("PresentationLibrary");
                return s_Instance;
            }
        }
    }
}

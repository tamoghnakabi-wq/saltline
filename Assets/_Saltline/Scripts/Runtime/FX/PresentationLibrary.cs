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
        [Tooltip("First-person arms (P_OperatorArms with FirstPersonArms). Null = the gun floats on its own. Set by CharacterArtFactory.")]
        public GameObject armsPrefab;
        public Material armsTeamRed;
        public Material armsTeamBlue;
        public Material armsTeamNeutral;

        [Header("Effects (set by FxArtFactory; null = plain fallbacks)")]
        public Material fxFlash;      // muzzle flash, front view (2x2 flipbook, additive)
        public Material fxFlame;      // muzzle flash, side jet on a cross mesh (2x2 flipbook, additive)
        public Material fxSpark;      // sparks (additive, HDR)
        public Material fxGlow;       // impact flashes (additive, soft)
        public Material fxSmoke;      // smoke and dust (2x2 flipbook, lit, soft)
        public Material fxDebris;     // chips (2x2 flipbook, lit, cutout)
        public Material bulletHole;   // 2x2 decal atlas: concrete, metal, wood, soft ground
        public Material brass;
        public Material shellHull;

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

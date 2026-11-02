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

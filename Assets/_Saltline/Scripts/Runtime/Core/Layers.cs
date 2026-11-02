using UnityEngine;

namespace Saltline
{
    /// <summary>Physics/render layer assignments. ProjectConfigurator writes the names into TagManager.</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int Environment = 6;
        public const int Player = 7;      // character controllers (movement only)
        public const int Hitbox = 8;      // reserved: analytic hitboxes are used instead of colliders
        public const int Viewmodel = 9;   // first-person arms/weapon, drawn by the overlay camera
        public const int LocalBody = 10;  // the local player's own body (hidden in first person)
        public const int Debris = 11;     // shell casings, gibs: never block anything
        public const int Trigger = 12;

        public const int EnvironmentMask = (1 << Default) | (1 << Environment);

        public static readonly (int index, string name)[] Named =
        {
            (Environment, "Environment"), (Player, "Player"), (Hitbox, "Hitbox"), (Viewmodel, "Viewmodel"),
            (LocalBody, "LocalBody"), (Debris, "Debris"), (Trigger, "Trigger"),
        };

        static bool s_configured;

        /// <summary>Players never collide with each other or with cosmetic layers.</summary>
        public static void ConfigureCollisionMatrix()
        {
            if (s_configured) return;
            s_configured = true;
            int[] nonBlocking = { Player, Hitbox, Viewmodel, LocalBody, Debris, Trigger };
            foreach (int other in nonBlocking) Physics.IgnoreLayerCollision(Player, other, true);
            foreach (int other in new[] { Hitbox, Viewmodel, LocalBody, Trigger }) Physics.IgnoreLayerCollision(Debris, other, true);
            Physics.IgnoreLayerCollision(Viewmodel, Default, true);
            Physics.IgnoreLayerCollision(Viewmodel, Environment, true);
        }

        public static void SetRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetRecursively(t.gameObject, layer);
        }
    }
}

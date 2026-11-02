using Saltline.Net;
using UnityEngine;

namespace Saltline.Match
{
    /// <summary>Placed in gameplay scenes. Forward (+Z) is the facing direction on spawn.</summary>
    public sealed class SpawnPoint : MonoBehaviour
    {
        public Team team = Team.None; // None = usable by either team (fallback)
        [Tooltip("Higher = preferred when several points score equally.")]
        public float priority;

        public float LastUsedTime { get; set; } = -100f;

        void OnDrawGizmos()
        {
            Gizmos.color = team == Team.Red ? new Color(1f, 0.3f, 0.25f) : team == Team.Blue ? new Color(0.3f, 0.6f, 1f) : Color.gray;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward * 0.8f);
        }
    }
}

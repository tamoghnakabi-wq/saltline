using UnityEngine;

namespace Saltline.Sim
{
    /// <summary>Tunable movement parameters. Shared by prediction and the host simulation.</summary>
    [CreateAssetMenu(menuName = "Saltline/Motor Settings", fileName = "MotorSettings")]
    public class MotorSettings : ScriptableObject
    {
        [Header("Speeds (m/s)")]
        public float runSpeed = 5.4f;
        public float sprintSpeed = 7.6f;
        public float crouchSpeed = 2.7f;
        public float aimSpeedMultiplier = 0.62f;
        public float backwardMultiplier = 0.82f;
        public float strafeMultiplier = 0.92f;

        [Header("Acceleration")]
        public float groundAcceleration = 62f;
        public float groundDeceleration = 48f;
        [Tooltip("Extra deceleration when input reverses direction, for crisp stops and turns.")]
        public float reverseBoost = 1.6f;
        public float airAcceleration = 14f;
        [Range(0f, 1f)] public float airControl = 0.45f;
        public float maxAirSpeed = 8.2f;

        [Header("Jumping")]
        public float gravity = 19f;
        public float jumpVelocity = 6.2f;
        public float coyoteTime = 0.1f;
        public float jumpBuffer = 0.12f;
        public float terminalVelocity = 40f;

        [Header("Crouch & slide")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.15f;
        public float slideHeight = 0.95f;
        public float standEye = 1.62f;
        public float crouchEye = 1.0f;
        public float slideEye = 0.82f;
        public float heightLerpSpeed = 10f;
        public float slideMinSpeed = 6.4f;
        public float slideBoost = 2.2f;
        public float slideFriction = 6.5f;
        public float slideEndSpeed = 3.6f;
        public float slideMaxTime = 1.05f;
        public float slideCooldown = 0.65f;
        public float slideSteer = 2.5f;

        [Header("Capsule")]
        public float radius = 0.36f;
        public float stepOffset = 0.35f;
        public float slopeLimit = 46f;
        public float skinWidth = 0.04f;

        [Header("Sprint rules")]
        [Tooltip("Sprint only applies when moving mostly forward.")]
        public float sprintForwardThreshold = 0.55f;
        [Tooltip("Seconds after sprinting before a weapon can fire.")]
        public float sprintToFireDelay = 0.16f;

        public static MotorSettings CreateDefault()
        {
            var s = CreateInstance<MotorSettings>();
            s.name = "MotorSettings (default)";
            return s;
        }
    }
}

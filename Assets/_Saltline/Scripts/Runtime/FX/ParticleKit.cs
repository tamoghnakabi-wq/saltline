using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.FX
{
    /// <summary>
    /// Builds the shared particle systems that effects emit into. Each system plays forever with
    /// its emission module off; FxManager adds particles with Emit(), so one system serves every
    /// shot of its kind and nothing is instantiated per shot.
    /// </summary>
    static class ParticleKit
    {
        public static ParticleSystem System(string name, Transform parent, int layer, Material mat, int max, bool world,
            ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.maxParticles = max;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 4f;
            ps.Play();
            return ps;
        }

        /// <summary>2x2 flipbook; every particle keeps one random frame for its whole life.</summary>
        public static void RandomFrame(ParticleSystem ps)
        {
            var t = ps.textureSheetAnimation;
            t.enabled = true;
            t.mode = ParticleSystemAnimationMode.Grid;
            t.numTilesX = 2;
            t.numTilesY = 2;
            t.animation = ParticleSystemAnimationType.WholeSheet;
            t.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            t.startFrame = 0f;
            t.cycleCount = 1;
        }

        public static void Size(ParticleSystem ps, float start, float end, float easeOut = 1f)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            var c = new AnimationCurve();
            const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float e = 1f - Mathf.Pow(1f - t, easeOut);
                c.AddKey(t, Mathf.Lerp(start, end, e));
            }
            s.size = new ParticleSystem.MinMaxCurve(1f, c);
        }

        /// <summary>Alpha fades in over <paramref name="fadeIn"/> and out from <paramref name="fadeOutFrom"/>; colour goes from a to b.</summary>
        public static void Fade(ParticleSystem ps, float fadeIn, float fadeOutFrom, Color a, Color b)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[]
                {
                    new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Max(0.001f, fadeIn)),
                    new GradientAlphaKey(1f, Mathf.Max(fadeIn + 0.001f, fadeOutFrom)),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = g;
        }

        /// <summary>Air drag: velocity loses <paramref name="drag"/> per second (scaled by speed, not size).</summary>
        public static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true;
            l.limit = 1000f;
            l.drag = drag;
            l.multiplyDragByParticleSize = false;
            l.multiplyDragByParticleVelocity = false;
        }

        public static void Rise(ParticleSystem ps, float upward)
        {
            var f = ps.forceOverLifetime;
            f.enabled = true;
            f.space = ParticleSystemSimulationSpace.World;
            f.x = 0f;
            f.y = upward;
            f.z = 0f;
        }

        public static void Turbulence(ParticleSystem ps, float strength, float frequency)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = frequency;
            n.scrollSpeed = 0.3f;
            n.quality = ParticleSystemNoiseQuality.Low;
            n.damping = true;
        }

        /// <summary>Particles bounce off the world (cheap planar-cached collision).</summary>
        public static void Bounce(ParticleSystem ps, float bounce, float dampen)
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.Low;
            c.collidesWith = Layers.EnvironmentMask;
            c.bounce = bounce;
            c.dampen = dampen;
            c.lifetimeLoss = 0f;
            c.radiusScale = 0.5f;
            c.maxCollisionShapes = 64;
            c.enableDynamicColliders = false;
        }

        /// <summary>Two crossed quads along +z from 0 to 1, one unit wide: the side view of a muzzle flame.
        /// u runs along the flame, v across it, matching T_FX_MuzzleSide.</summary>
        public static Mesh FlameCross()
        {
            var m = new Mesh { name = "FlameCross" };
            var v = new Vector3[8];
            var uv = new Vector2[8];
            for (int q = 0; q < 2; q++)
            {
                Vector3 side = q == 0 ? Vector3.right * 0.5f : Vector3.up * 0.5f;
                int o = q * 4;
                v[o + 0] = -side; uv[o + 0] = new Vector2(0f, 0f);
                v[o + 1] = side; uv[o + 1] = new Vector2(0f, 1f);
                v[o + 2] = -side + Vector3.forward; uv[o + 2] = new Vector2(1f, 0f);
                v[o + 3] = side + Vector3.forward; uv[o + 3] = new Vector2(1f, 1f);
            }
            m.vertices = v;
            m.uv = uv;
            m.triangles = new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

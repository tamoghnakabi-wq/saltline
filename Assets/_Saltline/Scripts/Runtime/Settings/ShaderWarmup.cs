using System.Collections;
using System.Diagnostics;
using Saltline.Core;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Debug = UnityEngine.Debug;

namespace Saltline.Settings
{
    /// <summary>
    /// Compiles the game's shader pipeline states before they are first drawn. Without this, the
    /// first launch on a machine (and any launch after macOS has discarded the game's shader cache)
    /// froze for over two seconds when a match started while Metal compiled them.
    ///
    /// The states come from a recording of the benchmark playing every preset
    /// (<c>-benchmark Kappa -tracegraphicsstates file.graphicsstate</c>, in a development build, which
    /// is the only kind Unity records in), stored in
    /// Resources/ShaderWarmup. A recording only fits the graphics API and platform it was made on,
    /// so a platform without one (Windows, for now) skips the warm-up.
    ///
    /// The warm-up runs a few states per frame from launch, while the menu is up. Once the cache is
    /// filled it takes a fraction of a second.
    /// </summary>
    public sealed class ShaderWarmup : MonoBehaviour
    {
        const string ResourceFolder = "ShaderWarmup";
        const int StatesPerFrame = 8;

        /// <summary>True once the warm-up has finished, or there was nothing to warm up.</summary>
        public static bool Done { get; private set; }

        /// <summary>Share of the states warmed up so far (0-1).</summary>
        public static float Progress { get; private set; }

        /// <summary>
        /// True while a warm-up that is taking a noticeable time is still running: on the first
        /// launch on a machine, when every state is compiled from scratch and the menu stutters.
        /// </summary>
        public static bool Slow => !Done && s_Started >= 0f && Time.realtimeSinceStartup - s_Started > 0.5f;

        static float s_Started = -1f;

        static GraphicsStateCollection s_Trace;
        static string s_TracePath;

        // Held for the whole session, so the next scene load does not unload it and the shaders it
        // loaded as unused assets.
        static GraphicsStateCollection s_Kept;

        /// <summary>Start recording (-tracegraphicsstates) or warming up, from the boot scene.</summary>
        public static void Begin(GameObject host)
        {
            if (CommandLine.Has("tracegraphicsstates"))
            {
                // Record what is drawn; warming up first would hide nothing but is not wanted here.
                s_TracePath = CommandLine.Get("tracegraphicsstates", "Saltline.graphicsstate");
                s_Trace = new GraphicsStateCollection();
                if (s_Trace.BeginTrace())
                {
                    Application.quitting += EndTrace;
                    Debug.Log($"[SALTLINE] Recording graphics states to {s_TracePath}");
                }
                else Debug.LogError("[SALTLINE] Graphics states can only be recorded in a development build (SaltlineBuild -devbuild).");
                Done = true;
                return;
            }
            if (!RunMode.Presentation) { Done = true; return; }
            host.AddComponent<ShaderWarmup>();
        }

        static void EndTrace()
        {
            if (s_Trace == null || !s_Trace.isTracing) return;
            s_Trace.EndTrace();
            bool saved = s_Trace.SaveToFile(s_TracePath);
            Debug.Log($"[SALTLINE] Graphics state recording: {s_Trace.variantCount} shader variants, {s_Trace.totalGraphicsStateCount} states " +
                      $"({s_Trace.graphicsDeviceType}, {s_Trace.runtimePlatform}, quality level {s_Trace.qualityLevelName}); saved to {s_TracePath}: {saved}");
        }

        /// <summary>The recording for this platform and graphics API, if there is one.</summary>
        public static GraphicsStateCollection Find()
        {
            foreach (var c in Resources.LoadAll<GraphicsStateCollection>(ResourceFolder))
                if (c.runtimePlatform == Application.platform && c.graphicsDeviceType == SystemInfo.graphicsDeviceType)
                    return c;
            return null;
        }

        IEnumerator Start()
        {
            var collection = Find();
            if (collection == null)
            {
                Debug.Log($"[SALTLINE] Shader warm-up: no recording for {Application.platform} / {SystemInfo.graphicsDeviceType}; shaders compile when first drawn");
                Done = true;
                yield break;
            }
            s_Kept = collection;
            var clock = Stopwatch.StartNew();
            s_Started = Time.realtimeSinceStartup;
            int frames = 0;
            JobHandle handle = default;
            while (!collection.isWarmedUp)
            {
                handle = collection.WarmUpProgressively(StatesPerFrame, handle);
                JobHandle.ScheduleBatchedJobs();
                frames++;
                Progress = collection.totalGraphicsStateCount > 0 ? (float)collection.completedWarmupCount / collection.totalGraphicsStateCount : 1f;
                yield return null;
            }
            handle.Complete();
            Progress = 1f;
            Done = true;
            Debug.Log($"[SALTLINE] Shader warm-up: {collection.completedWarmupCount} of {collection.totalGraphicsStateCount} states " +
                      $"({collection.variantCount} shader variants) in {clock.Elapsed.TotalSeconds:F2} s over {frames} frames");
        }
    }
}

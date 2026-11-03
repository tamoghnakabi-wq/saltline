using System;
using UnityEngine;

namespace Saltline.Audio
{
    /// <summary>
    /// Look-ahead peak limiter. Audio is delayed by the look-ahead so the gain is already down when
    /// a peak arrives, instead of the peak being cut off. The gain is held until the peak has passed
    /// and then recovers exponentially. Channels are linked, so the stereo image does not shift.
    /// Plain C# so tests can drive it; <see cref="MasterLimiter"/> runs it on the final mix.
    /// </summary>
    public sealed class PeakLimiter
    {
        public readonly float Ceiling;
        public readonly int Lookahead; // frames of delay

        readonly float m_AttackK, m_ReleaseK;
        int m_Channels, m_Pos, m_Hold;
        float m_Target = 1f, m_Gain = 1f;
        float[] m_Delay;
        float[] m_NeedDelay; // the gain each delayed frame needs, applied as a cap when it leaves

        // The last block processed.
        public float BlockInputPeak, BlockOutputPeak, BlockLowestGain;
        public int BlockLimitedFrames;

        public PeakLimiter(int sampleRate, float ceilingDb = -1f, float lookaheadSeconds = 0.002f, float releaseSeconds = 0.12f)
        {
            Ceiling = Mathf.Pow(10f, ceilingDb / 20f);
            Lookahead = Mathf.Max(1, Mathf.RoundToInt(lookaheadSeconds * sampleRate));
            // The gain gets over 99% of the way down within the look-ahead; a per-frame cap does the rest.
            m_AttackK = 1f - Mathf.Exp(-5f / Lookahead);
            m_ReleaseK = 1f - Mathf.Exp(-1f / (releaseSeconds * sampleRate));
        }

        public float Gain => m_Gain;

        /// <summary>Limits interleaved samples in place.</summary>
        public void Process(float[] data, int channels)
        {
            if (m_Delay == null || m_Channels != channels)
            {
                m_Channels = channels;
                m_Delay = new float[Lookahead * channels];
                m_NeedDelay = new float[Lookahead];
                for (int i = 0; i < Lookahead; i++) m_NeedDelay[i] = 1f;
                m_Pos = 0;
            }

            float inPeak = 0f, outPeak = 0f, lowest = 1f;
            int limited = 0;
            int frames = data.Length / channels;
            for (int f = 0; f < frames; f++)
            {
                int o = f * channels;
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float a = Math.Abs(data[o + c]);
                    if (a > peak) peak = a;
                }
                if (peak > inPeak) inPeak = peak;
                float need = peak > Ceiling ? Ceiling / peak : 1f;

                // Hold the reduction until the loudest sample in the look-ahead has gone out, then recover.
                if (m_Hold > 0) m_Hold--;
                else m_Target += (1f - m_Target) * m_ReleaseK;
                if (need < m_Target) { m_Target = need; m_Hold = Lookahead; }
                m_Gain = m_Target < m_Gain ? m_Gain + (m_Target - m_Gain) * m_AttackK : m_Target;

                // The smoothed gain is over 99% of the way down by now; the cap takes care of the rest.
                float g = Math.Min(m_Gain, m_NeedDelay[m_Pos]);
                m_NeedDelay[m_Pos] = need;
                if (g < 0.999f) limited++;
                if (g < lowest) lowest = g;

                int d = m_Pos * channels;
                for (int c = 0; c < channels; c++)
                {
                    float y = m_Delay[d + c] * g;
                    m_Delay[d + c] = data[o + c];
                    if (y > 1f) y = 1f;
                    else if (y < -1f) y = -1f;
                    data[o + c] = y;
                    float a = Math.Abs(y);
                    if (a > outPeak) outPeak = a;
                }
                if (++m_Pos == Lookahead) m_Pos = 0;
            }
            BlockInputPeak = inPeak;
            BlockOutputPeak = outPeak;
            BlockLowestGain = lowest;
            BlockLimitedFrames = limited;
        }
    }

    /// <summary>
    /// The limiter on the final mix. It sits on the active listener's GameObject, where Unity hands
    /// script filters the whole output. Unity's mixer has no ceiling of its own, so several loud
    /// sounds at once (held fire, a close shotgun, eight players) would otherwise clip. It holds the
    /// output under -1 dBFS with 2 ms of look-ahead and recovers over about 120 ms.
    /// </summary>
    public sealed class MasterLimiter : MonoBehaviour
    {
        public const float CeilingDb = -1f;

        /// <summary>Called on the audio thread with each limited block (the test capture records it).</summary>
        public static Action<float[], int> Tap;

        /// <summary>Written on the audio thread, read by the test summary on exit.</summary>
        public static class Stats
        {
            public static float InputPeak, OutputPeak, LowestGain = 1f;
            public static long Frames, LimitedFrames;

            public static string Summary()
            {
                static string Db(float v) => v > 0f ? $"{20f * Mathf.Log10(v):F1}" : "-inf";
                float share = Frames > 0 ? 100f * LimitedFrames / Frames : 0f;
                return $"limiter: input peak {Db(InputPeak)} dBFS, output peak {Db(OutputPeak)} dBFS, " +
                       $"turned down {share:F2}% of the time, by at most {-20f * Mathf.Log10(Mathf.Max(LowestGain, 1e-6f)):F1} dB";
            }
        }

        PeakLimiter m_Limiter; // created last in Awake; until then the audio thread passes audio through

        void Awake() => m_Limiter = new PeakLimiter(AudioSettings.outputSampleRate, CeilingDb);

        void OnAudioFilterRead(float[] data, int channels)
        {
            var lim = m_Limiter;
            if (lim == null || channels <= 0) return;
            lim.Process(data, channels);
            if (lim.BlockInputPeak > Stats.InputPeak) Stats.InputPeak = lim.BlockInputPeak;
            if (lim.BlockOutputPeak > Stats.OutputPeak) Stats.OutputPeak = lim.BlockOutputPeak;
            if (lim.BlockLowestGain < Stats.LowestGain) Stats.LowestGain = lim.BlockLowestGain;
            Stats.Frames += data.Length / channels;
            Stats.LimitedFrames += lim.BlockLimitedFrames;
            Tap?.Invoke(data, channels);
        }
    }
}

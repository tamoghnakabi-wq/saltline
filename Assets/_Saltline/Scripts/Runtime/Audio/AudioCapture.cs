using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Saltline.Audio
{
    /// <summary>
    /// <c>-audiocapture file.wav</c>: records what the active AudioListener hears (the final
    /// mix, after the <see cref="MasterLimiter"/>) to a 16-bit WAV, so tests can check levels and
    /// clipping without anyone listening. The limiter hands each block over on the audio thread,
    /// which writes it into a ring buffer that the main thread drains to disk.
    /// </summary>
    public sealed class AudioCapture : MonoBehaviour
    {
        string m_Path;
        FileStream m_File;
        int m_Channels, m_Rate;
        long m_Frames;

        // single producer (audio thread), single consumer (main thread)
        static float[] s_Ring = new float[48000 * 2 * 8];
        static int s_Write, s_Read;
        static int s_Channels;
        public static long Overruns;

        void Awake()
        {
            m_Path = Saltline.Core.CommandLine.Get("audiocapture", "");
            if (string.IsNullOrEmpty(m_Path)) { enabled = false; return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(m_Path)) ?? ".");
            m_Rate = AudioSettings.outputSampleRate;
            m_Channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            s_Channels = m_Channels;
            m_File = new FileStream(m_Path, FileMode.Create, FileAccess.Write);
            m_File.Write(new byte[44], 0, 44); // header written on close
            MasterLimiter.Tap = Write;
            Debug.Log($"[SALTLINE] Audio capture to {m_Path}: {m_Rate} Hz, {m_Channels} channel(s), DSP buffer {AudioSettings.GetConfiguration().dspBufferSize}.");
        }

        void Update()
        {
            if (m_File == null) return;
            Drain();
        }

        void Drain()
        {
            int w = Volatile.Read(ref s_Write);
            int r = s_Read;
            if (w == r) return;
            int n = (w - r + s_Ring.Length) % s_Ring.Length;
            var bytes = new byte[n * 2];
            for (int i = 0; i < n; i++)
            {
                float v = s_Ring[(r + i) % s_Ring.Length];
                short s = (short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
            }
            m_File.Write(bytes, 0, bytes.Length);
            m_Frames += n / Mathf.Max(1, m_Channels);
            Volatile.Write(ref s_Read, (r + n) % s_Ring.Length);
        }

        void OnApplicationQuit() => Close();
        void OnDestroy() => Close();

        void Close()
        {
            if (m_File == null) return;
            MasterLimiter.Tap = null;
            Drain();
            long dataBytes = m_Frames * m_Channels * 2;
            m_File.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(m_File, System.Text.Encoding.ASCII, true))
            {
                bw.Write("RIFF".ToCharArray()); bw.Write((int)(36 + dataBytes)); bw.Write("WAVE".ToCharArray());
                bw.Write("fmt ".ToCharArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)m_Channels);
                bw.Write(m_Rate); bw.Write(m_Rate * m_Channels * 2); bw.Write((short)(m_Channels * 2)); bw.Write((short)16);
                bw.Write("data".ToCharArray()); bw.Write((int)dataBytes);
            }
            m_File.Dispose();
            m_File = null;
            Debug.Log($"[SALTLINE][AUTOTEST] audio capture: {m_Frames / (double)Mathf.Max(1, m_Rate):F1} s written, {Overruns} buffer overruns");
        }

        /// <summary>Audio thread: one block of the final mix.</summary>
        static void Write(float[] data, int channels)
        {
            if (channels != s_Channels) return;
            int w = s_Write;
            int r = Volatile.Read(ref s_Read);
            int free = (r - w - 1 + s_Ring.Length) % s_Ring.Length;
            if (data.Length > free) { Interlocked.Increment(ref Overruns); return; }
            for (int i = 0; i < data.Length; i++) s_Ring[(w + i) % s_Ring.Length] = data[i];
            Volatile.Write(ref s_Write, (w + data.Length) % s_Ring.Length);
        }
    }
}

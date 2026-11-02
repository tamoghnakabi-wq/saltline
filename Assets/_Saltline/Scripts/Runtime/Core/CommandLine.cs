using System;
using System.Collections.Generic;

namespace Saltline.Core
{
    /// <summary>
    /// Parses "-key value" and "-flag" style process arguments once.
    /// Used for headless tests, multi-instance profiles and diagnostics.
    /// </summary>
    public static class CommandLine
    {
        static Dictionary<string, string> s_Args;

        static Dictionary<string, string> Args
        {
            get
            {
                if (s_Args != null) return s_Args;
                s_Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string[] raw;
                try { raw = Environment.GetCommandLineArgs(); }
                catch { raw = Array.Empty<string>(); }

                for (int i = 0; i < raw.Length; i++)
                {
                    string a = raw[i];
                    if (string.IsNullOrEmpty(a) || a[0] != '-') continue;
                    string key = a.TrimStart('-');
                    string value = "";
                    if (i + 1 < raw.Length && !raw[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        value = raw[i + 1];
                        i++;
                    }
                    s_Args[key] = value;
                }
                return s_Args;
            }
        }

        public static bool Has(string key) => Args.ContainsKey(key);

        public static string Get(string key, string fallback = null)
            => Args.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

        public static int GetInt(string key, int fallback)
            => int.TryParse(Get(key), out var v) ? v : fallback;

        public static float GetFloat(string key, float fallback)
            => float.TryParse(Get(key), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}

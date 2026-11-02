using System.Collections.Generic;
using Saltline.Combat;
using Saltline.Net;
using Saltline.Player;
using Saltline.Settings;
using Saltline.Weapons;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Saltline.UI
{
    /// <summary>
    /// Development HUD drawn with IMGUI: crosshair, hit markers, damage direction, health,
    /// ammo, kill feed, respawn timer, third-person obstruction marker and network stats.
    /// The production HUD (UI Toolkit) replaces this in the UI milestone.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        NetPlayer m_P;
        float m_HitMarker;
        bool m_HitKill, m_HitHead;
        float m_DamageTime = -10f;
        Vector3 m_DamageFrom;
        readonly List<(string text, float time)> m_Feed = new();
        bool m_ShowNet = true;
        float m_FpsTimer, m_Fps;
        int m_Frames;
        GUIStyle m_Big, m_Small, m_Center;

        public static DebugHud Attach(NetPlayer p)
        {
            var go = new GameObject("DebugHud");
            var hud = go.AddComponent<DebugHud>();
            hud.m_P = p;
            return hud;
        }

        void OnEnable()
        {
            CombatEvents.HitConfirmed += OnHit;
            CombatEvents.LocalDamaged += OnDamaged;
            CombatEvents.Killed += OnKilled;
        }

        void OnDisable()
        {
            CombatEvents.HitConfirmed -= OnHit;
            CombatEvents.LocalDamaged -= OnDamaged;
            CombatEvents.Killed -= OnKilled;
        }

        void OnHit(HitConfirm h)
        {
            m_HitMarker = 0.18f;
            m_HitKill = h.Kill;
            m_HitHead = h.Headshot;
        }

        void OnDamaged(DamageTaken d)
        {
            m_DamageTime = Time.time;
            m_DamageFrom = d.From;
        }

        void OnKilled(KillEvent k)
        {
            string killer = NameOf(k.Killer), victim = NameOf(k.Victim);
            string weapon = k.WeaponId == 255 ? "fell" : WeaponName(k.WeaponId);
            string line = k.Killer == k.Victim ? $"{victim} [{weapon}]" : $"{killer}  [{weapon}{(k.Headshot ? " • HEAD" : "")}]  {victim}";
            if (k.Assister != ulong.MaxValue) line += $"   (+{NameOf(k.Assister)})";
            m_Feed.Add((line, Time.time));
            if (m_Feed.Count > 6) m_Feed.RemoveAt(0);
        }

        string WeaponName(byte id)
        {
            var w = m_P != null ? m_P.Weapons.Get(id) : null;
            return w != null ? w.shortName : "?";
        }

        static string NameOf(ulong id) =>
            GameSession.Instance != null && GameSession.Instance.TryGet(id, out var r) ? r.Name.ToString() : $"#{id}";

        void Update()
        {
            if (m_P == null) { Destroy(gameObject); return; }
            m_HitMarker -= Time.deltaTime;
            m_Frames++;
            m_FpsTimer += Time.unscaledDeltaTime;
            if (m_FpsTimer >= 0.5f) { m_Fps = m_Frames / m_FpsTimer; m_Frames = 0; m_FpsTimer = 0f; }
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) m_ShowNet = !m_ShowNet;
            m_Feed.RemoveAll(f => Time.time - f.time > 7f);
        }

        void Styles()
        {
            if (m_Big != null) return;
            m_Big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            m_Small = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            m_Center = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        void OnGUI()
        {
            if (m_P == null || !m_P.IsSpawned) return;
            Styles();
            float sw = Screen.width, sh = Screen.height;
            var r = m_P.Render;
            var ctl = m_P.InputProvider as LocalPlayerController;
            var rig = ctl != null ? ctl.Rig : null;
            Vector2 c = new Vector2(sw * 0.5f, sh * 0.5f);

            if (r.Alive)
            {
                var w = m_P.ActiveWeapon;
                var ws = m_P.SimWeapon;
                bool scoped = w != null && w.hasScope && ws.Ads > 0.92f && (rig == null || !rig.ThirdPersonActive);
                if (scoped) DrawScope(c, sw, sh);
                else if (w != null)
                {
                    float spread = WeaponSim.ComputeSpread(w, ws, m_P.SimMotor, m_P.MotorConfig);
                    float vfov = rig != null ? rig.MainCamera.fieldOfView : 70f;
                    float gap = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad) * (sh * 0.5f);
                    gap = Mathf.Clamp(gap, 3f, sh * 0.25f);
                    Color col = new Color(1f, 1f, 1f, ws.Ads > 0.8f ? 0.6f : 0.9f);
                    Line(c + new Vector2(gap, 0), c + new Vector2(gap + 9, 0), 2, col);
                    Line(c - new Vector2(gap, 0), c - new Vector2(gap + 9, 0), 2, col);
                    Line(c + new Vector2(0, gap), c + new Vector2(0, gap + 9), 2, col);
                    Line(c - new Vector2(0, gap), c - new Vector2(0, gap + 9), 2, col);
                    Rect(new Rect(c.x - 1, c.y - 1, 2, 2), col);
                }

                if (rig != null && rig.AimObstructed && rig.MainCamera != null)
                {
                    Vector3 sp = rig.MainCamera.WorldToScreenPoint(rig.ObstructionPoint);
                    if (sp.z > 0)
                    {
                        Vector2 p = new Vector2(sp.x, sh - sp.y);
                        Color oc = new Color(1f, 0.35f, 0.2f, 0.95f);
                        Line(p + new Vector2(-7, -7), p + new Vector2(7, 7), 2, oc);
                        Line(p + new Vector2(-7, 7), p + new Vector2(7, -7), 2, oc);
                    }
                }

                if (m_HitMarker > 0f)
                {
                    Color hc = m_HitKill ? new Color(1f, 0.25f, 0.2f) : m_HitHead ? new Color(1f, 0.85f, 0.3f) : Color.white;
                    float a = 7f, b = 15f;
                    Line(c + new Vector2(a, a), c + new Vector2(b, b), 2, hc);
                    Line(c + new Vector2(-a, a), c + new Vector2(-b, b), 2, hc);
                    Line(c + new Vector2(a, -a), c + new Vector2(b, -b), 2, hc);
                    Line(c + new Vector2(-a, -a), c + new Vector2(-b, -b), 2, hc);
                }

                float since = Time.time - m_DamageTime;
                if (since < 1.2f && rig != null)
                {
                    Vector3 to = m_DamageFrom - r.Feet;
                    float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - (ctl != null ? ctl.ViewYaw : r.Yaw);
                    Vector2 d = new Vector2(Mathf.Sin(ang * Mathf.Deg2Rad), -Mathf.Cos(ang * Mathf.Deg2Rad));
                    Color dc = new Color(1f, 0.2f, 0.15f, 1f - since / 1.2f);
                    Vector2 p0 = c + d * 90f, p1 = c + d * 120f;
                    Line(p0, p1, 6, dc);
                }

                // Health
                float hp = m_P.Health.Value / (float)m_P.Config.maxHealth;
                Rect(new Rect(40, sh - 60, 260, 18), new Color(0, 0, 0, 0.5f));
                Rect(new Rect(42, sh - 58, 256 * hp, 14), hp > 0.35f ? new Color(0.9f, 0.95f, 1f, 0.9f) : new Color(1f, 0.3f, 0.25f, 0.95f));
                GUI.Label(new Rect(40, sh - 90, 260, 30), $"{m_P.Health.Value}", m_Center);

                if (w != null)
                {
                    string state = ws.Action == WeaponAction.Reloading || ws.Action == WeaponAction.ShellReloading ? "  RELOADING" : "";
                    GUI.Label(new Rect(sw - 420, sh - 100, 380, 50), $"{ws.Mag} / {ws.Reserve}", m_Big);
                    GUI.Label(new Rect(sw - 420, sh - 52, 380, 24), $"{w.displayName}{state}", new GUIStyle(m_Small) { alignment = TextAnchor.UpperRight });
                }
                if (m_P.Protected.Value) GUI.Label(new Rect(0, sh * 0.62f, sw, 30), "SPAWN PROTECTED", m_Center);
            }
            else
            {
                float left = m_P.RespawnAt.Value - (float)NetworkManager.Singleton.ServerTime.Time;
                GUI.Label(new Rect(0, sh * 0.45f, sw, 40), left > 0 ? $"RESPAWNING IN {Mathf.CeilToInt(left)}" : "RESPAWNING", m_Center);
            }

            // Kill feed
            float y = 20;
            foreach (var f in m_Feed)
            {
                GUI.Label(new Rect(sw - 620, y, 600, 22), f.text, new GUIStyle(m_Small) { alignment = TextAnchor.UpperRight });
                y += 22;
            }

            if (m_ShowNet) DrawNet();
            if (Keyboard.current != null && Keyboard.current.tabKey.isPressed) DrawScoreboard(sw, sh);
        }

        void DrawNet()
        {
            var nm = NetworkManager.Singleton;
            string rtt = "host";
            if (!nm.IsServer && nm.NetworkConfig.NetworkTransport is UnityTransport utp)
                rtt = $"{utp.GetCurrentRtt(NetworkManager.ServerClientId)} ms";
            string s = $"{m_Fps:F0} fps   rtt {rtt}   interp {NetClock.InterpolationTicks:F1}t\n" +
                       $"tick {m_P.LocalTick} ack {m_P.LastAck}  corrections {m_P.Reconciliations} (last {m_P.LastCorrectionMeters * 100f:F1} cm)\n" +
                       $"shots {m_P.ShotsPredicted} confirmed {m_P.HitsConfirmed} kills {m_P.KillsConfirmed}   [F3]";
            Rect(new Rect(10, 10, 470, 62), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(16, 12, 460, 60), s, m_Small);
        }

        void DrawScoreboard(float sw, float sh)
        {
            var gs = GameSession.Instance;
            if (gs == null) return;
            var rect = new Rect(sw * 0.5f - 320, sh * 0.2f, 640, 40 + gs.Roster.Count * 26);
            Rect(rect, new Color(0, 0, 0, 0.7f));
            GUI.Label(new Rect(rect.x + 16, rect.y + 8, 620, 24), "PLAYER                      TEAM      K     D     A    SCORE   PING", m_Small);
            float y = rect.y + 34;
            for (int i = 0; i < gs.Roster.Count; i++)
            {
                var r = gs.Roster[i];
                GUI.Label(new Rect(rect.x + 16, y, 620, 24),
                    $"{r.Name,-26}  {r.Team,-6}  {r.Kills,3}   {r.Deaths,3}   {r.Assists,3}   {r.Score,5}   {r.PingMs,4}", m_Small);
                y += 26;
            }
        }

        static void DrawScope(Vector2 c, float sw, float sh)
        {
            float rad = sh * 0.46f;
            Color black = new Color(0, 0, 0, 0.96f);
            Rect(new Rect(0, 0, c.x - rad, sh), black);
            Rect(new Rect(c.x + rad, 0, sw - c.x - rad, sh), black);
            Line(new Vector2(c.x - rad, c.y), new Vector2(c.x + rad, c.y), 1, Color.black);
            Line(new Vector2(c.x, c.y - rad), new Vector2(c.x, c.y + rad), 1, Color.black);
            Rect(new Rect(c.x - 1.5f, c.y - 1.5f, 3, 3), new Color(1f, 0.2f, 0.1f));
        }

        static void Rect(Rect r, Color col)
        {
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Line(Vector2 a, Vector2 b, float width, Color col)
        {
            var old = GUI.matrix;
            Vector2 d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, a);
            Rect(new Rect(a.x, a.y - width * 0.5f, d.magnitude, width), col);
            GUI.matrix = old;
        }
    }
}

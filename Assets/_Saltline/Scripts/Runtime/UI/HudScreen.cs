using System.Collections.Generic;
using Saltline.Combat;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Saltline.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>
    /// In-match HUD: team scores and timer, phase banners, kill feed, health, ammo, a crosshair
    /// that shows the real current spread, hit markers, damage direction, death/respawn overlay,
    /// spawn protection and the sniper scope.
    /// </summary>
    public sealed class HudScreen : UiScreen
    {
        const float FeedLife = 6f;
        const int FeedMax = 5;

        // score bar
        VisualElement m_RedBox, m_BlueBox;
        Label m_RedScore, m_BlueScore, m_Timer, m_Goal;
        // banner
        VisualElement m_Banner;
        Label m_BannerTitle, m_BannerSub;
        float m_BannerUntil;
        int m_LastMatchNumber = -1;
        bool m_MinuteWarned;
        // feed
        VisualElement m_Feed;
        readonly List<(VisualElement el, float time)> m_FeedEntries = new();
        // vitals + weapon
        Label m_Hp, m_Mag, m_Reserve, m_WeaponName, m_WeaponState, m_PrimaryPill, m_SecondaryPill;
        VisualElement m_HpFill, m_Vitals, m_WeaponBox;
        // centre
        VisualElement m_Crosshair, m_HitMarker, m_LowHp, m_TpsMarker;
        readonly VisualElement[] m_ChLines = new VisualElement[4];
        ScopeOverlay m_Scope;
        readonly VisualElement[] m_DmgRings = new VisualElement[4];
        readonly float[] m_DmgTime = new float[4];
        readonly Vector3[] m_DmgFrom = new Vector3[4];
        int m_DmgNext;
        Label m_Protect, m_KillMsg;
        float m_HitUntil, m_KillMsgUntil;
        // death
        VisualElement m_Death;
        Label m_DeathKiller, m_DeathCount;
        string m_LastKiller = "";

        protected override VisualElement Build()
        {
            var root = Ui.Div("hud");
            root.Ignore();

            m_LowHp = root.Put(Ui.Div("fill", "lowhp"));
            m_Scope = root.Put(new ScopeOverlay());
            m_Scope.AddToClassList("fill");

            // crosshair: four lines and a dot, positioned each frame from the weapon's spread
            m_Crosshair = root.Put(Ui.Div("crosshair"));
            for (int i = 0; i < 4; i++) m_ChLines[i] = m_Crosshair.Put(Ui.Div("ch-line"));
            m_Crosshair.Add(Ui.Div("ch-dot"));

            m_HitMarker = root.Put(Ui.Div("hitmarker"));
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? 1f : -1f, sy = (i & 2) == 0 ? 1f : -1f;
                var l = m_HitMarker.Put(Ui.Div("hm-line"));
                l.style.left = sx * 12f - 5.5f;
                l.style.top = sy * 12f - 1f;
                l.style.rotate = new Rotate(Angle.Degrees(sx * sy > 0 ? 45f : -45f));
            }

            for (int i = 0; i < m_DmgRings.Length; i++)
            {
                m_DmgRings[i] = root.Put(Ui.Div("dmg-ring"));
                m_DmgRings[i].Add(Ui.Div("dmg-wedge"));
                m_DmgTime[i] = -10f;
            }

            m_TpsMarker = root.Put(Ui.Div("tps-block"));
            var x1 = m_TpsMarker.Put(Ui.Div("hm-line"));
            x1.style.rotate = new Rotate(Angle.Degrees(45f));
            x1.style.top = 7; x1.style.left = 2;
            var x2 = m_TpsMarker.Put(Ui.Div("hm-line"));
            x2.style.rotate = new Rotate(Angle.Degrees(-45f));
            x2.style.top = 7; x2.style.left = 2;
            m_TpsMarker.AddToClassList("hm--kill");

            // top: score bar
            var bar = root.Put(Ui.Div("scorebar", "col"));
            var row = bar.Put(Ui.Div("scorebar-row"));
            m_RedBox = row.Put(Ui.Div("score-box", "score-box--red"));
            m_RedScore = m_RedBox.Put(Ui.Text("0", "score-num"));
            var tb = row.Put(Ui.Div("timer-box"));
            m_Timer = tb.Put(Ui.Text("10:00", "timer"));
            m_BlueBox = row.Put(Ui.Div("score-box", "score-box--blue"));
            m_BlueScore = m_BlueBox.Put(Ui.Text("0", "score-num"));
            m_Goal = bar.Put(Ui.Text("", "score-goal", "hud-text"));

            m_Banner = root.Put(Ui.Div("banner", "col", "banner--off"));
            m_BannerTitle = m_Banner.Put(Ui.Text("", "banner-title", "hud-text"));
            m_BannerSub = m_Banner.Put(Ui.Text("", "banner-sub", "hud-text"));

            m_Feed = root.Put(Ui.Div("killfeed", "col"));

            var center = root.Put(Ui.Div("center-msg", "col"));
            m_Protect = center.Put(Ui.Text("SPAWN PROTECTED", "protect-chip", "hud-text"));
            m_KillMsg = center.Put(Ui.Text("", "h2", "hud-text", "mt-8"));

            // bottom-left: health
            m_Vitals = root.Put(Ui.Div("vitals", "col"));
            m_Hp = m_Vitals.Put(Ui.Text("100", "hp-num", "hud-text"));
            var hpBar = m_Vitals.Put(Ui.Div("hp-bar"));
            m_HpFill = hpBar.Put(Ui.Div("hp-fill"));

            // bottom-right: weapon
            m_WeaponBox = root.Put(Ui.Div("weapon-box", "col"));
            m_WeaponName = m_WeaponBox.Put(Ui.Text("", "weapon-name", "hud-text"));
            var ammo = m_WeaponBox.Put(Ui.Div("ammo-row"));
            m_Mag = ammo.Put(Ui.Text("30", "ammo-mag", "hud-text"));
            m_Reserve = ammo.Put(Ui.Text("/ 120", "ammo-reserve", "hud-text"));
            m_WeaponState = m_WeaponBox.Put(Ui.Text("", "weapon-state", "hud-text"));
            var strip = m_WeaponBox.Put(Ui.Div("slot-strip"));
            m_PrimaryPill = strip.Put(Ui.Text("", "slot-pill"));
            m_SecondaryPill = strip.Put(Ui.Text("", "slot-pill"));

            m_Death = root.Put(Ui.Div("fill", "death", "col"));
            m_Death.Add(Ui.Text("ELIMINATED BY", "death-title", "hud-text"));
            m_DeathKiller = m_Death.Put(Ui.Text("", "death-killer", "hud-text"));
            m_DeathCount = m_Death.Put(Ui.Text("", "death-count", "hud-text"));

            CombatEvents.HitConfirmed += OnHit;
            CombatEvents.LocalDamaged += OnDamaged;
            CombatEvents.Killed += OnKilled;
            return root;
        }

        // ================================================================== events

        void OnHit(HitConfirm h)
        {
            m_HitUntil = Time.time + (h.Kill ? 0.35f : 0.18f);
            m_HitMarker.EnableInClassList("hm--kill", h.Kill);
            m_HitMarker.EnableInClassList("hm--head", h.Headshot && !h.Kill);
        }

        void OnDamaged(DamageTaken d)
        {
            int i = m_DmgNext++ % m_DmgRings.Length;
            m_DmgTime[i] = Time.time;
            m_DmgFrom[i] = d.From;
        }

        void OnKilled(KillEvent k)
        {
            var nm = NetworkManager.Singleton;
            ulong local = nm != null ? nm.LocalClientId : ulong.MaxValue;
            var p = NetPlayer.Local;

            var e = Ui.Div("feed-entry");
            bool self = k.Killer == k.Victim;
            if (!self) e.Add(Ui.Text(NameOf(k.Killer), "feed-name", Ui.TeamClass(TeamOf(k.Killer))));
            string weapon = k.WeaponId == 255 ? "FELL" : p != null && p.Weapons.Get(k.WeaponId) is WeaponDefinition w ? w.shortName.ToUpperInvariant() : "?";
            e.Add(Ui.Text(weapon, "feed-weapon"));
            if (k.Headshot) e.Add(Ui.Text("HEAD", "feed-tag"));
            if (k.Penetration) e.Add(Ui.Text("WALL", "feed-tag"));
            e.Add(Ui.Text(NameOf(k.Victim), "feed-name", Ui.TeamClass(TeamOf(k.Victim))));
            if (k.Assister != ulong.MaxValue) e.Add(Ui.Text($"+ {NameOf(k.Assister)}", "feed-weapon"));
            e.EnableInClassList("feed-entry--me", k.Killer == local || k.Victim == local || k.Assister == local);
            m_Feed.Add(e);
            m_FeedEntries.Add((e, Time.time));
            while (m_FeedEntries.Count > FeedMax) { m_FeedEntries[0].el.RemoveFromHierarchy(); m_FeedEntries.RemoveAt(0); }

            if (k.Victim == local) m_LastKiller = self ? "" : NameOf(k.Killer);
            if (k.Killer == local && !self)
            {
                m_KillMsg.text = k.Headshot ? $"HEADSHOT  ·  {NameOf(k.Victim)}" : $"ELIMINATED  ·  {NameOf(k.Victim)}";
                m_KillMsgUntil = Time.time + 2f;
            }
            else if (k.Assister == local)
            {
                m_KillMsg.text = $"ASSIST  ·  {NameOf(k.Victim)}";
                m_KillMsgUntil = Time.time + 2f;
            }
        }

        static string NameOf(ulong id) =>
            GameSession.Instance != null && GameSession.Instance.TryGet(id, out var r) ? r.Name.ToString() : $"#{id}";

        static Team TeamOf(ulong id) =>
            GameSession.Instance != null && GameSession.Instance.TryGet(id, out var r) ? r.Team : Team.None;

        void ShowBanner(string title, string sub, float seconds)
        {
            m_BannerTitle.text = title;
            m_BannerSub.text = sub;
            m_BannerUntil = Time.time + seconds;
        }

        // ================================================================== frame

        public override void Tick()
        {
            var p = NetPlayer.Local;
            var md = MatchDirector.Instance;
            if (p == null || !p.IsSpawned) return;
            var nm = NetworkManager.Singleton;
            float toPanel = Host.PixelsToPanel;

            TickScores(md, p);
            TickFeed();

            bool alive = p.Render.Alive;
            var ctl = p.InputProvider as LocalPlayerController;
            var rig = p.LocalRig;
            var w = p.ActiveWeapon;
            var ws = p.SimWeapon;

            m_Death.Show(!alive);
            m_Vitals.Show(alive);
            m_WeaponBox.Show(alive && w != null);
            m_Protect.Show(alive && p.Protected.Value);
            m_KillMsg.Show(Time.time < m_KillMsgUntil);

            if (!alive)
            {
                float left = p.RespawnAt.Value - (float)nm.ServerTime.Time;
                m_DeathKiller.text = string.IsNullOrEmpty(m_LastKiller) ? "THE ENVIRONMENT" : m_LastKiller.ToUpperInvariant();
                bool frozen = md != null && md.Phase.Value == MatchPhase.Results;
                m_DeathCount.text = frozen ? "" : left > 0.05f ? $"RESPAWN IN {Mathf.CeilToInt(left)}" : "RESPAWNING";
                m_Crosshair.Show(false);
                m_Scope.Show(false);
                m_HitMarker.Show(false);
                m_TpsMarker.Show(false);
                m_LowHp.style.opacity = 0f;
                foreach (var r in m_DmgRings) r.Show(false);
                return;
            }

            // health
            float hp = p.Health.Value / (float)p.Config.maxHealth;
            m_Hp.text = p.Health.Value.ToString();
            m_HpFill.style.width = Length.Percent(hp * 100f);
            m_HpFill.EnableInClassList("hp-fill--low", hp <= 0.35f);
            m_LowHp.style.opacity = hp < 0.35f ? (0.55f + 0.45f * Mathf.Sin(Time.time * 6f)) * (1f - hp / 0.35f) : 0f;

            // weapon
            if (w != null)
            {
                m_WeaponName.text = w.displayName.ToUpperInvariant();
                m_Mag.text = ws.Mag.ToString();
                m_Reserve.text = $"/ {ws.Reserve}";
                m_Mag.EnableInClassList("ammo-mag--empty", ws.Mag == 0);
                m_Mag.EnableInClassList("ammo-mag--low", ws.Mag > 0 && ws.Mag <= Mathf.Max(1, w.magazineSize / 4));
                m_WeaponState.text = ws.Action switch
                {
                    WeaponAction.Reloading or WeaponAction.ShellReloading => "RELOADING",
                    WeaponAction.Holstering or WeaponAction.Equipping => "SWITCHING",
                    _ => ws.Mag == 0 ? (ws.Reserve > 0 ? "RELOAD" : "NO AMMO") : "",
                };
                var prim = p.Weapons.Get(ws.PrimaryId);
                var sec = p.Weapons.Get(ws.SecondaryId);
                m_PrimaryPill.text = prim != null ? $"1  {prim.shortName.ToUpperInvariant()}" : "1";
                m_SecondaryPill.text = sec != null ? $"2  {sec.shortName.ToUpperInvariant()}" : "2";
                m_PrimaryPill.EnableInClassList("slot-pill--active", ws.Active == WeaponSlot.Primary);
                m_SecondaryPill.EnableInClassList("slot-pill--active", ws.Active == WeaponSlot.Secondary);
            }

            // crosshair / scope
            bool tps = rig != null && rig.ThirdPersonActive;
            bool scoped = w != null && w.hasScope && ws.Ads > 0.92f && !tps;
            m_Scope.Show(scoped);
            m_Crosshair.Show(!scoped && w != null);
            if (!scoped && w != null)
            {
                float spread = WeaponSim.ComputeSpread(w, ws, p.SimMotor, p.MotorConfig);
                float vfov = rig != null ? rig.MainCamera.fieldOfView : 70f;
                float gapPx = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad) * (Screen.height * 0.5f);
                float gap = Mathf.Clamp(gapPx * toPanel, 4f, 260f);
                const float len = 10f, thick = 2f;
                Place(m_ChLines[0], gap, -thick * 0.5f, len, thick);
                Place(m_ChLines[1], -gap - len, -thick * 0.5f, len, thick);
                Place(m_ChLines[2], -thick * 0.5f, gap, thick, len);
                Place(m_ChLines[3], -thick * 0.5f, -gap - len, thick, len);
                m_Crosshair.style.opacity = ws.Ads > 0.8f ? 0.45f : 1f;
            }

            m_HitMarker.Show(Time.time < m_HitUntil);

            // third person: mark where the eye-ray is blocked by cover the camera can see past
            bool obstructed = rig != null && rig.AimObstructed && rig.MainCamera != null;
            m_TpsMarker.Show(false);
            if (obstructed)
            {
                Vector3 sp = rig.MainCamera.WorldToScreenPoint(rig.ObstructionPoint);
                if (sp.z > 0f)
                {
                    m_TpsMarker.Show(true);
                    m_TpsMarker.style.left = sp.x * toPanel - 8f;
                    m_TpsMarker.style.top = (Screen.height - sp.y) * toPanel - 8f;
                }
            }

            // damage direction, relative to where the camera faces
            float viewYaw = ctl != null ? ctl.ViewYaw : p.Render.Yaw;
            for (int i = 0; i < m_DmgRings.Length; i++)
            {
                float since = Time.time - m_DmgTime[i];
                bool on = since < 1.2f;
                m_DmgRings[i].Show(on);
                if (!on) continue;
                Vector3 to = m_DmgFrom[i] - p.Render.Feet;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - viewYaw;
                m_DmgRings[i].style.rotate = new Rotate(Angle.Degrees(ang));
                m_DmgRings[i].style.opacity = 1f - since / 1.2f;
            }
        }

        static void Place(VisualElement e, float x, float y, float w, float h)
        {
            e.style.left = x;
            e.style.top = y;
            e.style.width = w;
            e.style.height = h;
        }

        void TickScores(MatchDirector md, NetPlayer p)
        {
            if (md == null) return;
            m_RedScore.text = md.RedScore.Value.ToString();
            m_BlueScore.text = md.BlueScore.Value.ToString();
            Team mine = p.TeamState.Value;
            m_RedBox.EnableInClassList("score-mine", mine == Team.Red);
            m_BlueBox.EnableInClassList("score-mine", mine == Team.Blue);
            var phase = md.Phase.Value;
            float left = md.SecondsLeft;
            m_Timer.text = Ui.Clock(left);
            m_Timer.EnableInClassList("timer--low", phase == MatchPhase.Live && left <= 60f);
            m_Goal.text = phase == MatchPhase.Warmup ? "WARMUP · KILLS DON'T COUNT" : $"FIRST TO {md.Rules.Value.ScoreLimit}";

            if (phase == MatchPhase.Warmup)
            {
                ShowBanner("WARMUP", $"MATCH STARTS IN {Ui.Clock(left)}", 0.2f);
                m_MinuteWarned = false;
            }
            else if (phase == MatchPhase.Live)
            {
                if (md.MatchNumber.Value != m_LastMatchNumber)
                {
                    m_LastMatchNumber = md.MatchNumber.Value;
                    m_MinuteWarned = false;
                    if (md.Rules.Value.TimeLimit - left < 3f)
                        ShowBanner("FIGHT", $"FIRST TEAM TO {md.Rules.Value.ScoreLimit} KILLS WINS", 2.8f);
                }
                if (!m_MinuteWarned && left <= 60f && md.Rules.Value.TimeLimit > 90f)
                {
                    m_MinuteWarned = true;
                    ShowBanner("1 MINUTE", "REMAINING", 2.5f);
                }
            }
            m_Banner.EnableInClassList("banner--off", Time.time >= m_BannerUntil);
        }

        void TickFeed()
        {
            for (int i = m_FeedEntries.Count - 1; i >= 0; i--)
            {
                var (el, t) = m_FeedEntries[i];
                float age = Time.time - t;
                if (age > FeedLife) el.AddToClassList("feed-entry--old");
                if (age > FeedLife + 0.5f) { el.RemoveFromHierarchy(); m_FeedEntries.RemoveAt(i); }
            }
        }

        protected override void OnHide()
        {
            foreach (var (el, _) in m_FeedEntries) el.RemoveFromHierarchy();
            m_FeedEntries.Clear();
            m_BannerUntil = 0f;
        }
    }

    /// <summary>Sniper scope: black mask with a circular cut-out, crosshair and range posts.</summary>
    public sealed class ScopeOverlay : VisualElement
    {
        public ScopeOverlay()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            var p = ctx.painter2D;
            Vector2 c = r.center;
            float rad = r.height * 0.47f;

            p.fillColor = new Color(0.01f, 0.01f, 0.012f, 1f);
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.MoveTo(c + new Vector2(rad, 0f));
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);

            p.strokeColor = new Color(0f, 0f, 0f, 0.92f);
            p.lineWidth = 1.6f;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-rad, 0f)); p.LineTo(c + new Vector2(rad, 0f));
            p.MoveTo(c + new Vector2(0f, -rad)); p.LineTo(c + new Vector2(0f, rad));
            p.Stroke();

            p.lineWidth = 7f;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-rad, 0f)); p.LineTo(c + new Vector2(-rad * 0.42f, 0f));
            p.MoveTo(c + new Vector2(rad * 0.42f, 0f)); p.LineTo(c + new Vector2(rad, 0f));
            p.MoveTo(c + new Vector2(0f, rad)); p.LineTo(c + new Vector2(0f, rad * 0.42f));
            p.Stroke();

            p.fillColor = new Color(0f, 0f, 0f, 0.92f);
            for (int i = 1; i <= 4; i++)
            {
                float d = rad * 0.085f * i;
                foreach (var o in new[] { new Vector2(d, 0f), new Vector2(-d, 0f), new Vector2(0f, d) })
                {
                    p.BeginPath();
                    p.Arc(c + o, 2.4f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                }
            }

            p.strokeColor = new Color(0f, 0f, 0f, 1f);
            p.lineWidth = 4f;
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }
    }
}

using System.Collections.Generic;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Unity.Netcode;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>Two team tables (player, K, D, A, score, ping), sorted by score. Used by Tab and Results.</summary>
    public sealed class ScoreboardView
    {
        public VisualElement Root { get; }
        readonly TeamTable m_Red, m_Blue;
        static readonly List<PlayerRecord> s_Rows = new();

        sealed class TeamTable
        {
            public Label Score;
            public Ui.RowPool<VisualElement> Rows;
        }

        public ScoreboardView()
        {
            Root = Ui.Div("board");
            m_Red = Table(Team.Red);
            m_Blue = Table(Team.Blue);
        }

        TeamTable Table(Team team)
        {
            var t = new TeamTable();
            var col = Root.Put(Ui.Div("board-team", "col"));
            var head = col.Put(Ui.Div("board-head", team == Team.Red ? "team-head--red" : "team-head--blue"));
            head.Add(Ui.Text(Ui.TeamName(team), "board-title", Ui.TeamClass(team)));
            t.Score = head.Put(Ui.Text("0", "board-score"));
            var cols = col.Put(Ui.Div("board-cols"));
            cols.Add(Ui.Text("PLAYER", "bc-head", "bc-name"));
            foreach (var h in new[] { "K", "D", "A" }) cols.Add(Ui.Text(h, "bc-head", "bc-num"));
            cols.Add(Ui.Text("SCORE", "bc-head", "bc-num", "bc-wide"));
            cols.Add(Ui.Text("PING", "bc-head", "bc-num", "bc-wide"));
            var body = col.Put(Ui.Div("col"));
            t.Rows = new Ui.RowPool<VisualElement>(body, () =>
            {
                var r = Ui.Div("board-row");
                r.Add(Ui.Text("", "bc-name", "nowrap"));
                for (int i = 0; i < 3; i++) r.Add(Ui.Text("", "bc-num"));
                r.Add(Ui.Text("", "bc-num", "bc-wide"));
                r.Add(Ui.Text("", "bc-num", "bc-wide"));
                return r;
            });
            return t;
        }

        public void Refresh()
        {
            var gs = GameSession.Instance;
            var md = MatchDirector.Instance;
            var nm = NetworkManager.Singleton;
            if (gs == null || nm == null) return;
            m_Red.Score.text = md != null ? md.RedScore.Value.ToString() : "0";
            m_Blue.Score.text = md != null ? md.BlueScore.Value.ToString() : "0";
            Fill(m_Red, Team.Red, gs, nm.LocalClientId);
            Fill(m_Blue, Team.Blue, gs, nm.LocalClientId);
        }

        static void Fill(TeamTable t, Team team, GameSession gs, ulong local)
        {
            s_Rows.Clear();
            foreach (var r in gs.Roster) if (r.Team == team) s_Rows.Add(r);
            s_Rows.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : b.Kills.CompareTo(a.Kills));
            int n = System.Math.Max(s_Rows.Count, BuildInfo.MaxPerTeam);
            t.Rows.Resize(n);
            for (int i = 0; i < n; i++)
            {
                var row = t.Rows[i];
                bool has = i < s_Rows.Count;
                row.EnableInClassList("board-row--empty", !has);
                row.EnableInClassList("board-row--me", has && s_Rows[i].ClientId == local);
                var labels = row.Query<Label>().ToList();
                if (!has) { foreach (var l in labels) l.text = ""; continue; }
                var p = s_Rows[i];
                labels[0].text = p.IsHost ? $"{p.Name}  ·  HOST" : p.Name.ToString();
                labels[1].text = p.Kills.ToString();
                labels[2].text = p.Deaths.ToString();
                labels[3].text = p.Assists.ToString();
                labels[4].text = p.Score.ToString();
                labels[5].text = p.IsHost ? "—" : p.PingMs.ToString();
            }
        }

        /// <summary>Highest score (kills break ties); false if nobody has played.</summary>
        public static bool TryGetMvp(out PlayerRecord mvp)
        {
            mvp = default;
            var gs = GameSession.Instance;
            if (gs == null) return false;
            bool found = false;
            foreach (var r in gs.Roster)
            {
                if (r.Team == Team.None) continue;
                if (!found || r.Score > mvp.Score || (r.Score == mvp.Score && r.Kills > mvp.Kills)) { mvp = r; found = true; }
            }
            return found && (mvp.Score > 0 || mvp.Kills > 0);
        }
    }

    /// <summary>Hold Tab during a match.</summary>
    public sealed class ScoreboardOverlay : UiScreen
    {
        ScoreboardView m_View;
        Label m_Caption;

        protected override VisualElement Build()
        {
            var root = Ui.Div("scrim-light", "board-overlay", "col");
            root.Ignore();
            m_Caption = root.Put(Ui.Text("", "board-caption", "hud-text"));
            m_View = new ScoreboardView();
            root.Add(m_View.Root);
            return root;
        }

        public override void Tick()
        {
            var md = MatchDirector.Instance;
            if (md != null)
            {
                MapCatalog.TryGet(md.MapId.Value.ToString(), out var map);
                string phase = md.Phase.Value == MatchPhase.Warmup ? "WARMUP" : Ui.Clock(md.SecondsLeft) + " LEFT";
                m_Caption.text = $"TEAM DEATHMATCH  ·  {(map.Name ?? "").ToUpperInvariant()}  ·  FIRST TO {md.Rules.Value.ScoreLimit}  ·  {phase}";
            }
            m_View.Refresh();
        }
    }
}

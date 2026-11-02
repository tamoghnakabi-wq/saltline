using System.Linq;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>
    /// Pre-match lobby: join code, team columns, ready state, map and rules. The host picks the
    /// map and starts the match once everyone else is ready.
    /// </summary>
    public sealed class LobbyScreen : UiScreen
    {
        Label m_SessionName, m_Code, m_Privacy, m_MapName, m_MapBlurb, m_RuleTime, m_RuleScore, m_Status;
        Label m_RedCount, m_BlueCount;
        Button m_CopyCode, m_JoinRed, m_JoinBlue, m_Action;
        DropdownField m_MapPick;
        VisualElement m_MapPickBox;
        readonly SlotView[] m_Red = new SlotView[BuildInfo.MaxPerTeam];
        readonly SlotView[] m_Blue = new SlotView[BuildInfo.MaxPerTeam];

        sealed class SlotView
        {
            public VisualElement Root;
            public Label Platform, Name, Ping, Ready, Empty;
        }

        protected override VisualElement Build()
        {
            var root = Ui.Div("lobby", "col");

            var header = root.Put(Ui.Div("lobby-header"));
            var titles = header.Put(Ui.Div("col", "grow"));
            titles.Add(Ui.Text("LOBBY", "eyebrow"));
            m_SessionName = titles.Put(Ui.Text("", "title", "nowrap"));
            m_Privacy = titles.Put(Ui.Text("", "chip", "mt-8", "self-start"));

            var codeBox = header.Put(Ui.Div("code-box", "col"));
            codeBox.Add(Ui.Text("JOIN CODE", "field-label"));
            var codeRow = codeBox.Put(Ui.Div("row"));
            m_Code = codeRow.Put(Ui.Text("------", "code-text"));
            m_Code.name = "lobbyCode";
            m_CopyCode = codeRow.Put(Ui.Button("COPY", CopyCode, "btn--ghost", "btn--small", "ml-16"));

            var body = root.Put(Ui.Div("lobby-body"));
            body.Add(TeamColumn(Team.Red, m_Red, out m_RedCount, out m_JoinRed));

            var center = body.Put(Ui.Div("lobby-center", "col"));
            var card = center.Put(Ui.Div("panel", "map-card", "col"));
            card.Add(Ui.Text("MAP", "eyebrow"));
            m_MapName = card.Put(Ui.Text("", "map-name"));
            m_MapBlurb = card.Put(Ui.Text("", "dim", "wrap", "small", "mt-8"));
            m_MapPickBox = card.Put(Ui.Div("col", "mt-16"));
            m_MapPick = new DropdownField(MapCatalog.Available().Select(m => m.Name).ToList(), 0);
            m_MapPick.name = "lobbyMap";
            m_MapPick.RegisterValueChangedCallback(e =>
            {
                var md = MatchDirector.Instance;
                var map = MapCatalog.Available().FirstOrDefault(m => m.Name == e.newValue);
                if (md != null && md.IsServer && map.Id != null) md.ServerSetMap(map.Id);
            });
            m_MapPickBox.Add(Ui.Field("CHANGE MAP", m_MapPick));

            card.Add(Ui.Text("RULES", "eyebrow", "mt-24"));
            card.Add(RuleRow("Mode", "Team Deathmatch"));
            m_RuleTime = RuleValue(card, "Time limit");
            m_RuleScore = RuleValue(card, "Score to win");
            card.Add(RuleRow("Respawn", "Timed, spawn protection"));
            card.Add(RuleRow("Friendly fire", "Off"));

            body.Add(TeamColumn(Team.Blue, m_Blue, out m_BlueCount, out m_JoinBlue));

            var footer = root.Put(Ui.Div("lobby-footer"));
            footer.Add(Ui.Button("LEAVE", () => Host.Confirm("LEAVE LOBBY?", IsHostMachine ? "You are the host. Leaving closes the lobby for everyone." : "You can rejoin with the code while the lobby is open.", "LEAVE", Host.LeaveSession, "STAY", true), "btn--ghost", "btn--wide"));
            footer.Add(Ui.Button("SETTINGS", () => Host.OpenSettings(), "btn--ghost", "ml-8"));
            footer.Add(Ui.Div("spacer"));
            m_Status = footer.Put(Ui.Text("", "status-text"));
            m_Action = footer.Put(Ui.Button("READY", OnAction, "btn--primary", "btn--wide"));
            m_Action.name = "lobbyAction";
            return root;
        }

        static VisualElement RuleRow(string key, string val)
        {
            var r = Ui.Div("rule-row");
            r.Add(Ui.Text(key, "rule-key"));
            r.Add(Ui.Text(val, "rule-val"));
            return r;
        }

        static Label RuleValue(VisualElement parent, string key)
        {
            var r = parent.Put(Ui.Div("rule-row"));
            r.Add(Ui.Text(key, "rule-key"));
            return r.Put(Ui.Text("", "rule-val"));
        }

        VisualElement TeamColumn(Team team, SlotView[] slots, out Label count, out Button join)
        {
            var col = Ui.Div("team-col", "col");
            var head = col.Put(Ui.Div("team-head", team == Team.Red ? "team-head--red" : "team-head--blue"));
            head.Add(Ui.Text(team == Team.Red ? "RED" : "BLUE", "team-name", Ui.TeamClass(team)));
            head.Add(Ui.Div("spacer"));
            count = head.Put(Ui.Text("0/4", "team-count"));
            for (int i = 0; i < slots.Length; i++)
            {
                var s = new SlotView();
                s.Root = col.Put(Ui.Div("slot"));
                s.Platform = s.Root.Put(Ui.Text("", "chip", "mr-16"));
                s.Name = s.Root.Put(Ui.Text("", "slot-name", "nowrap"));
                s.Ping = s.Root.Put(Ui.Text("", "slot-ping"));
                s.Ready = s.Root.Put(Ui.Text("", "chip", "slot-ready"));
                s.Empty = s.Root.Put(Ui.Text("OPEN SLOT", "slot-empty-text"));
                slots[i] = s;
            }
            join = col.Put(Ui.Button(team == Team.Red ? "JOIN RED" : "JOIN BLUE", () => GameSession.Instance?.RequestTeamRpc(team), "btn--ghost", "mt-8"));
            join.name = team == Team.Red ? "joinRed" : "joinBlue";
            return col;
        }

        static bool IsHostMachine => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        void CopyCode()
        {
            string code = SessionService.Current?.Code;
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;
            Host.Toast("Join code copied");
        }

        void OnAction()
        {
            var gs = GameSession.Instance;
            var md = MatchDirector.Instance;
            if (gs == null || md == null) return;
            if (IsHostMachine)
            {
                if (!md.ServerStartMatch(out string reason)) Host.Toast(reason);
                return;
            }
            if (gs.TryGet(NetworkManager.Singleton.LocalClientId, out var me)) gs.SetReadyRpc(!me.Ready);
        }

        public override void Tick()
        {
            var gs = GameSession.Instance;
            var md = MatchDirector.Instance;
            var nm = NetworkManager.Singleton;
            var session = SessionService.Current;
            if (gs == null || md == null || nm == null || session == null) return;

            m_SessionName.text = session.Name;
            m_Code.text = string.IsNullOrEmpty(session.Code) ? "------" : session.Code;
            m_Privacy.text = session.IsPrivate ? "PRIVATE · CODE ONLY" : "PUBLIC";

            bool host = nm.IsServer;
            MapCatalog.TryGet(md.MapId.Value.ToString(), out var map);
            m_MapName.text = map.Name ?? md.MapId.Value.ToString();
            m_MapBlurb.text = map.Blurb ?? "";
            m_MapPickBox.Show(host && m_MapPick.choices.Count > 1);
            if (host && map.Name != null && m_MapPick.value != map.Name) m_MapPick.SetValueWithoutNotify(map.Name);
            var rules = md.Rules.Value;
            m_RuleTime.text = $"{Mathf.RoundToInt(rules.TimeLimit / 60f)} min";
            m_RuleScore.text = $"{rules.ScoreLimit} kills";

            ulong localId = nm.LocalClientId;
            int r = 0, b = 0, notReady = 0;
            bool meReady = false;
            Team myTeam = Team.None;
            foreach (var rec in gs.Roster)
            {
                if (rec.ClientId == localId) { meReady = rec.Ready; myTeam = rec.Team; }
                if (!rec.IsHost && !rec.Ready) notReady++;
                if (rec.Team == Team.Red && r < m_Red.Length) Fill(m_Red[r++], rec, localId);
                else if (rec.Team == Team.Blue && b < m_Blue.Length) Fill(m_Blue[b++], rec, localId);
            }
            for (int i = r; i < m_Red.Length; i++) Clear(m_Red[i]);
            for (int i = b; i < m_Blue.Length; i++) Clear(m_Blue[i]);
            m_RedCount.text = $"{r}/{BuildInfo.MaxPerTeam}";
            m_BlueCount.text = $"{b}/{BuildInfo.MaxPerTeam}";
            m_JoinRed.SetEnabled(myTeam != Team.Red && r < BuildInfo.MaxPerTeam);
            m_JoinBlue.SetEnabled(myTeam != Team.Blue && b < BuildInfo.MaxPerTeam);

            if (host)
            {
                bool can = md.CanStart(out string reason);
                m_Action.text = "START MATCH";
                m_Action.SetEnabled(can);
                m_Status.text = can ? (gs.Roster.Count == 1 ? "You are alone. Share the code, or start a practice match." : "Everyone is ready.") : reason;
            }
            else
            {
                m_Action.text = meReady ? "NOT READY" : "READY";
                m_Action.EnableInClassList("btn--primary", !meReady);
                m_Action.SetEnabled(true);
                m_Status.text = meReady
                    ? (notReady > 0 ? $"Waiting for {notReady} more player{(notReady == 1 ? "" : "s")}…" : "Waiting for the host to start…")
                    : "Pick a team and ready up.";
            }
        }

        static void Fill(SlotView s, PlayerRecord rec, ulong localId)
        {
            s.Root.RemoveFromClassList("slot--empty");
            s.Root.EnableInClassList("slot--me", rec.ClientId == localId);
            s.Platform.Show(true);
            s.Platform.text = Ui.PlatformTag(rec.Platform);
            s.Name.Show(true);
            s.Name.text = rec.Name.ToString();
            s.Ping.Show(!rec.IsHost);
            s.Ping.text = $"{rec.PingMs} ms";
            s.Ready.Show(true);
            s.Ready.text = rec.IsHost ? "HOST" : rec.Ready ? "READY" : "NOT READY";
            s.Ready.EnableInClassList("chip--ok", rec.Ready && !rec.IsHost);
            s.Ready.EnableInClassList("chip--accent", rec.IsHost);
            s.Empty.Show(false);
        }

        static void Clear(SlotView s)
        {
            s.Root.AddToClassList("slot--empty");
            s.Root.RemoveFromClassList("slot--me");
            s.Platform.Show(false);
            s.Name.Show(false);
            s.Ping.Show(false);
            s.Ready.Show(false);
            s.Empty.Show(true);
        }
    }
}

using Saltline.Match;
using Saltline.Net;
using Unity.Netcode;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>End of match: verdict from the local team's point of view, final scores, MVP, countdown.</summary>
    public sealed class ResultsScreen : UiScreen
    {
        Label m_Verdict, m_Score, m_Reason, m_MvpName, m_MvpLine, m_Countdown;
        VisualElement m_Mvp;
        Button m_Back;
        ScoreboardView m_Board;

        protected override VisualElement Build()
        {
            var root = Ui.Div("results", "col");
            root.Add(Ui.Text("MATCH COMPLETE", "eyebrow"));
            m_Verdict = root.Put(Ui.Text("", "results-verdict"));
            m_Verdict.name = "verdict";
            m_Score = root.Put(Ui.Text("", "results-score"));
            m_Reason = root.Put(Ui.Text("", "results-reason"));
            m_Mvp = root.Put(Ui.Div("mvp"));
            m_Mvp.Add(Ui.Text("MVP", "chip", "chip--accent", "mr-16"));
            m_MvpName = m_Mvp.Put(Ui.Text("", "h2", "mr-16"));
            m_MvpLine = m_Mvp.Put(Ui.Text("", "dim"));
            m_Board = new ScoreboardView();
            root.Add(m_Board.Root);
            var foot = root.Put(Ui.Div("results-footer"));
            m_Countdown = foot.Put(Ui.Text("", "dim", "mr-16"));
            m_Back = foot.Put(Ui.Button("BACK TO LOBBY NOW", () => MatchDirector.Instance?.ServerReturnToLobby(), "btn--ghost", "btn--small"));
            return root;
        }

        public override void Tick()
        {
            var md = MatchDirector.Instance;
            var nm = NetworkManager.Singleton;
            if (md == null || nm == null) return;

            Team mine = Team.None;
            if (GameSession.Instance != null && GameSession.Instance.TryGet(nm.LocalClientId, out var me)) mine = me.Team;
            Team w = md.Winner.Value;
            bool draw = w == Team.None;
            m_Verdict.text = draw ? "DRAW" : mine == Team.None ? $"{Ui.TeamName(w)} WINS" : w == mine ? "VICTORY" : "DEFEAT";
            m_Verdict.EnableInClassList("verdict--win", !draw && w == mine);
            m_Verdict.EnableInClassList("verdict--loss", !draw && w != mine);
            m_Score.text = $"RED  {md.RedScore.Value}  —  {md.BlueScore.Value}  BLUE";
            m_Reason.text = md.EndReason.Value switch
            {
                MatchEndReason.ScoreLimit => $"FIRST TO {md.Rules.Value.ScoreLimit} KILLS",
                MatchEndReason.TimeLimit => draw ? "TIME LIMIT · SCORES LEVEL" : "TIME LIMIT REACHED",
                MatchEndReason.HostEnded => "ENDED BY THE HOST",
                _ => "",
            };

            bool hasMvp = ScoreboardView.TryGetMvp(out var mvp);
            m_Mvp.Show(hasMvp);
            if (hasMvp)
            {
                m_MvpName.text = mvp.Name.ToString();
                m_MvpLine.text = $"{mvp.Kills} kills  ·  {mvp.Deaths} deaths  ·  {mvp.Assists} assists  ·  {mvp.Score} score";
            }
            m_Board.Refresh();
            m_Countdown.text = $"Returning to the lobby in {UnityEngine.Mathf.CeilToInt(md.SecondsLeft)}";
            m_Back.Show(nm.IsServer);
        }
    }
}

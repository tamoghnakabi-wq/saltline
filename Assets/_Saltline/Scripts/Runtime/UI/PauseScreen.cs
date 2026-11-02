using Saltline.Match;
using Unity.Netcode;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>Esc during a match. The match keeps running underneath.</summary>
    public sealed class PauseScreen : UiScreen
    {
        Button m_End;

        protected override VisualElement Build()
        {
            var root = Ui.Div("scrim-light", "pause");
            var left = root.Put(Ui.Div("pause-left", "col"));
            left.Add(Ui.Text("MENU", "eyebrow"));
            left.Add(Ui.Text("PAUSED", "title"));
            left.Add(Ui.Text("The match keeps running.", "dim", "small", "mt-8"));
            var col = left.Put(Ui.Div("col", "mt-40", "pause-buttons"));
            col.Add(Ui.Button("RESUME", () => Host.ClosePause(), "btn--primary"));
            col.Add(Ui.Button("SETTINGS", () => Host.OpenSettings()));
            m_End = col.Put(Ui.Button("END MATCH", () => Host.Confirm("END THE MATCH?", "The match ends now with the current score, for everyone.", "END MATCH", () =>
            {
                Host.ClosePause();
                MatchDirector.Instance?.ServerEndMatchNow();
            }, "CANCEL", true)));
            col.Add(Ui.Button("LEAVE MATCH", () => Host.Confirm("LEAVE THE MATCH?",
                IsHost ? "You are the host. Leaving ends the match for everyone." : "You can rejoin with the code if the match is still running.",
                "LEAVE", Host.LeaveSession, "STAY", true), "btn--danger"));
            col.Add(Ui.Button("QUIT GAME", () => Host.QuitGame(), "btn--ghost"));
            return root;
        }

        static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        protected override void OnShow() => m_End.Show(IsHost);
    }
}

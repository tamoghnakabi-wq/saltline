using Saltline.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>Shown while connecting to a session and while the match map loads.</summary>
    public sealed class LoadingScreen : UiScreen
    {
        static readonly string[] Tips =
        {
            "Sprint, then crouch to slide. Sliding keeps your momentum through doorways and under low cover.",
            "Press V to switch between first and third person. Shots always come from your eyes, so cover works the same in both.",
            "Aiming down sights tightens your spread. Moving and jumping widen it.",
            "Headshots deal extra damage. A sniper headshot at close or mid range is lethal; a leg shot is not.",
            "You are protected for a moment after spawning. Firing ends the protection early.",
            "Hold Tab for the scoreboard. Press Esc for the menu; the match keeps running.",
            "Thin wood and sheet metal can be shot through. Concrete cannot.",
        };

        Label m_Eyebrow, m_Title, m_Blurb, m_Rules, m_TipText;
        VisualElement m_Fill;
        float m_TipTimer;
        int m_Tip;

        protected override VisualElement Build()
        {
            var root = Ui.Div("loading", "col");
            m_Eyebrow = root.Put(Ui.Text("LOADING", "eyebrow"));
            m_Title = root.Put(Ui.Text("", "loading-map"));
            m_Blurb = root.Put(Ui.Text("", "loading-blurb"));
            m_Rules = root.Put(Ui.Text("", "chip", "chip--accent", "mt-16", "self-start"));
            var bar = root.Put(Ui.Div("progress"));
            m_Fill = bar.Put(Ui.Div("progress-fill"));
            m_TipText = root.Put(Ui.Text("", "loading-tip"));
            m_Tip = Random.Range(0, Tips.Length);
            return root;
        }

        public void SetConnecting()
        {
            m_Eyebrow.text = "PLEASE WAIT";
            m_Title.text = "CONNECTING";
            m_Blurb.text = "Joining the session through Unity Relay.";
            m_Rules.Show(false);
        }

        public void SetMap(string mapId, MatchRules rules)
        {
            m_Eyebrow.text = "LOADING MAP";
            bool known = MapCatalog.TryGet(mapId, out var map);
            m_Title.text = known ? map.Name.ToUpperInvariant() : mapId.ToUpperInvariant();
            m_Blurb.text = known ? map.Blurb : "";
            m_Rules.Show(true);
            m_Rules.text = $"TEAM DEATHMATCH  ·  {Mathf.RoundToInt(rules.TimeLimit / 60f)} MIN  ·  FIRST TO {rules.ScoreLimit}";
        }

        protected override void OnShow()
        {
            m_Tip = (m_Tip + 1) % Tips.Length;
            m_TipText.text = "TIP  ·  " + Tips[m_Tip];
            m_TipTimer = 0f;
        }

        public override void Tick()
        {
            // Indeterminate progress: a bar segment sweeping left to right.
            float t = Mathf.Repeat(Time.unscaledTime * 0.55f, 1.3f) - 0.25f;
            m_Fill.style.left = Length.Percent(t * 100f);
            m_TipTimer += Time.unscaledDeltaTime;
            if (m_TipTimer > 7f) OnShow();
        }
    }
}

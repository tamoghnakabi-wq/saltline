using System.Collections.Generic;
using System.Linq;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>Title screen: callsign, host a match, join by code, browse public matches.</summary>
    public sealed class MainMenuScreen : UiScreen
    {
        const string NameKey = "saltline.callsign";
        enum Tab { Host, Join, Browse }

        TextField m_Name, m_SessionName, m_Code;
        Button m_NavHost, m_NavJoin, m_NavBrowse, m_Public, m_Private;
        VisualElement m_HostCard, m_JoinCard, m_BrowseCard, m_BrowseList, m_UpdateCard;
        Label m_HostError, m_JoinError, m_BrowseStatus, m_Footer, m_UpdateTitle, m_UpdateText;
        Button m_UpdateDownload;
        IVisualElementScheduledItem m_Poll;
        UpdateCheck.Status m_ShownUpdate = UpdateCheck.Status.NotStarted;
        DropdownField m_Map;
        bool m_IsPrivate;
        Tab m_Tab = Tab.Host;
        bool m_Browsing;

        public static string Callsign
        {
            get
            {
                string n = PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrWhiteSpace(n)) { n = $"Operator{Random.Range(100, 999)}"; PlayerPrefs.SetString(NameKey, n); }
                return n;
            }
            set => PlayerPrefs.SetString(NameKey, ConnectionPayload.SanitizeName(value));
        }

        protected override VisualElement Build()
        {
            var root = Ui.Div("menu");

            // ---- left column: brand, navigation, callsign
            var left = root.Put(Ui.Div("menu-left", "col"));
            left.Add(Ui.Text("SALTLINE", "logo"));
            left.Add(Ui.Div("logo-bar"));
            left.Add(Ui.Text("4 V 4   TEAM DEATHMATCH", "logo-sub"));

            var nav = left.Put(Ui.Div("menu-nav", "col"));
            m_NavHost = nav.Put(NavButton("HOST MATCH", () => Select(Tab.Host)));
            m_NavJoin = nav.Put(NavButton("JOIN WITH CODE", () => Select(Tab.Join)));
            m_NavBrowse = nav.Put(NavButton("BROWSE MATCHES", () => Select(Tab.Browse)));
            nav.Add(NavButton("SETTINGS", () => Host.OpenSettings()));
            nav.Add(NavButton("QUIT", () => Host.QuitGame()));

            m_Name = Ui.TextInput(Callsign, 24);
            m_Name.RegisterValueChangedCallback(e => Callsign = e.newValue);
            m_Name.name = "callsign";
            left.Add(Ui.Field("CALLSIGN", m_Name).Classes("menu-callsign"));

            left.Add(Ui.Div("spacer"));
            m_Footer = left.Put(Ui.Text("", "menu-footer"));
            m_Footer.name = "menuFooter";

            // ---- right column: the selected card
            var right = root.Put(Ui.Div("menu-right", "col"));
            right.Ignore();
            m_HostCard = right.Put(BuildHostCard());
            m_JoinCard = right.Put(BuildJoinCard());
            m_BrowseCard = right.Put(BuildBrowseCard());
            m_UpdateCard = right.Put(BuildUpdateCard());
            Select(Tab.Host);
            return root;
        }

        static Button NavButton(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("nav-btn");
            return b;
        }

        VisualElement BuildHostCard()
        {
            var card = Ui.Div("panel", "menu-card", "col");
            card.Add(Ui.Text("CREATE A MATCH", "eyebrow"));
            card.Add(Ui.Text("HOST A LOBBY", "title"));
            card.Add(Ui.Text("Your machine runs the match and other players connect to you through Unity Relay. Up to 8 players, 4 per team.", "dim", "wrap", "small"));

            m_SessionName = Ui.TextInput("", 40, "Leave empty to use your callsign");
            m_SessionName.name = "sessionName";
            card.Add(Ui.Field("MATCH NAME", m_SessionName).Classes("mt-24"));

            var seg = Ui.Div("segment");
            m_Public = seg.Put(Ui.Button("PUBLIC", () => SetPrivate(false), "segment__item"));
            m_Private = seg.Put(Ui.Button("PRIVATE · CODE ONLY", () => SetPrivate(true), "segment__item"));
            card.Add(Ui.Field("VISIBILITY", seg).Classes("mt-16"));
            SetPrivate(false);

            var maps = MapCatalog.Available().ToList();
            var choices = maps.Count > 0 ? maps.Select(m => m.Name).ToList() : new List<string> { "Proving Ground" };
            m_Map = new DropdownField(choices, 0);
            m_Map.name = "map";
            card.Add(Ui.Field("MAP", m_Map).Classes("mt-16"));

            var create = card.Put(Ui.Button("CREATE LOBBY", HostMatch, "btn--primary", "mt-24"));
            create.name = "createLobby";
            m_HostError = card.Put(Ui.Text("", "error-text"));
            return card;
        }

        VisualElement BuildJoinCard()
        {
            var card = Ui.Div("panel", "menu-card", "col");
            card.Add(Ui.Text("PRIVATE OR PUBLIC", "eyebrow"));
            card.Add(Ui.Text("JOIN WITH CODE", "title"));
            card.Add(Ui.Text("Ask the host for the code shown at the top of their lobby.", "dim", "small"));
            m_Code = Ui.TextInput("", 12, "CODE");
            m_Code.AddToClassList("code-field");
            m_Code.name = "joinCode";
            m_Code.RegisterValueChangedCallback(e =>
            {
                string up = (e.newValue ?? "").ToUpperInvariant().Replace(" ", "");
                if (up != e.newValue) m_Code.SetValueWithoutNotify(up);
            });
            card.Add(Ui.Field("JOIN CODE", m_Code).Classes("mt-24"));
            var join = card.Put(Ui.Button("JOIN MATCH", JoinByCode, "btn--primary", "mt-24"));
            join.name = "joinByCode";
            m_JoinError = card.Put(Ui.Text("", "error-text"));
            return card;
        }

        VisualElement BuildBrowseCard()
        {
            var card = Ui.Div("panel", "menu-card", "col");
            var head = card.Put(Ui.Div("row"));
            var titles = head.Put(Ui.Div("col", "grow"));
            titles.Add(Ui.Text("SAME VERSION ONLY", "eyebrow"));
            titles.Add(Ui.Text("PUBLIC MATCHES", "title"));
            head.Add(Ui.Button("REFRESH", Refresh, "btn--ghost", "btn--small"));

            var cols = card.Put(Ui.Div("row", "mt-16", "browse-head"));
            cols.Add(Ui.Text("MATCH", "bc-head", "browse-name"));
            cols.Add(Ui.Text("MAP", "bc-head", "browse-map"));
            cols.Add(Ui.Text("STATUS", "bc-head", "browse-phase"));
            cols.Add(Ui.Text("PLAYERS", "bc-head", "browse-count"));

            var scroll = card.Put(new ScrollView(ScrollViewMode.Vertical).Classes("browse-list"));
            m_BrowseList = scroll.contentContainer;
            m_BrowseStatus = card.Put(Ui.Text("", "dim", "small", "mt-8"));
            return card;
        }

        /// <summary>Shown instead of the cards when this build is too old to play online.</summary>
        VisualElement BuildUpdateCard()
        {
            var card = Ui.Div("panel", "menu-card", "col", "update-card");
            card.name = "updateCard";
            card.Add(Ui.Text("UPDATE REQUIRED", "eyebrow"));
            m_UpdateTitle = card.Put(Ui.Text("A NEW VERSION IS OUT", "title"));
            m_UpdateText = card.Put(Ui.Text("", "dim", "wrap"));
            m_UpdateDownload = card.Put(Ui.Button("DOWNLOAD", OpenDownload, "btn--primary", "mt-24"));
            m_UpdateDownload.name = "updateDownload";
            card.Add(Ui.Text($"This is version {BuildInfo.Version}, network protocol {BuildInfo.NetProtocol}. Settings and Quit still work.",
                "dim", "small", "wrap", "mt-16"));
            card.Show(false);
            return card;
        }

        static void OpenDownload()
        {
            string url = UpdateCheck.Current.DownloadUrl;
            if (!string.IsNullOrEmpty(url) && url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)) Application.OpenURL(url);
        }

        /// <summary>Applies the update check's answer: blocks Host, Join and Browse when an update is required.</summary>
        void ApplyUpdateState()
        {
            var u = UpdateCheck.Current;
            m_ShownUpdate = u.Status;
            bool blocked = UpdateCheck.Blocking;
            m_NavHost.SetEnabled(!blocked);
            m_NavJoin.SetEnabled(!blocked);
            m_NavBrowse.SetEnabled(!blocked);
            if (blocked)
            {
                m_UpdateTitle.text = string.IsNullOrEmpty(u.Latest) ? "A NEW VERSION IS OUT" : $"SALTLINE {u.Latest} IS OUT";
                m_UpdateText.text = u.Message;
                m_UpdateDownload.Show(!string.IsNullOrEmpty(u.DownloadUrl));
            }
            ShowCards();
            UpdateFooter();
        }

        void SetPrivate(bool p)
        {
            m_IsPrivate = p;
            m_Public.EnableInClassList("btn--selected", !p);
            m_Private.EnableInClassList("btn--selected", p);
        }

        void Select(Tab t)
        {
            m_Tab = t;
            ShowCards();
            if (t == Tab.Browse && !UpdateCheck.Blocking) Refresh();
        }

        void ShowCards()
        {
            bool blocked = UpdateCheck.Blocking;
            m_HostCard.Show(!blocked && m_Tab == Tab.Host);
            m_JoinCard.Show(!blocked && m_Tab == Tab.Join);
            m_BrowseCard.Show(!blocked && m_Tab == Tab.Browse);
            m_UpdateCard.Show(blocked);
            m_NavHost.EnableInClassList("nav-btn--active", !blocked && m_Tab == Tab.Host);
            m_NavJoin.EnableInClassList("nav-btn--active", !blocked && m_Tab == Tab.Join);
            m_NavBrowse.EnableInClassList("nav-btn--active", !blocked && m_Tab == Tab.Browse);
        }

        void UpdateFooter()
        {
            string text = $"v{BuildInfo.Version}  ·  protocol {BuildInfo.NetProtocol}  ·  {Ui.PlatformTag(PlayerRecord.LocalPlatform())}";
            if (Saltline.Settings.ShaderWarmup.Slow)
                text += $"  ·  preparing shaders for this machine {Mathf.RoundToInt(Saltline.Settings.ShaderWarmup.Progress * 100f)}%";
            var u = UpdateCheck.Current;
            switch (u.Status)
            {
                case UpdateCheck.Status.Checking: text += "  ·  checking for updates"; break;
                case UpdateCheck.Status.UpdateAvailable: text += $"  ·  version {u.Latest} is available"; break;
                case UpdateCheck.Status.UpdateRequired: text += "  ·  update required"; break;
                case UpdateCheck.Status.Failed: text += "  ·  could not check for updates"; break;
            }
            m_Footer.text = text;
        }

        protected override void OnShow()
        {
            m_Name.SetValueWithoutNotify(Callsign);
            m_HostError.text = "";
            m_JoinError.text = "";
            ApplyUpdateState();
            // On the first launch on a machine the shaders are compiled while the menu is up, which
            // makes it stutter for a few seconds: say so until it is done. The update check also
            // answers after the menu is up.
            if (!Saltline.Settings.ShaderWarmup.Done || UpdateCheck.Pending)
            {
                if (m_Poll == null)
                    m_Poll = m_Footer.schedule.Execute(() =>
                    {
                        // runs once more after both are done, to clear the notes
                        if (UpdateCheck.Current.Status != m_ShownUpdate) ApplyUpdateState();
                        else UpdateFooter();
                        if (Saltline.Settings.ShaderWarmup.Done && !UpdateCheck.Pending) m_Poll.Pause();
                    }).Every(200);
                else m_Poll.Resume();
            }
            if (!string.IsNullOrEmpty(GameBootstrap.PendingNotice))
            {
                Host.Alert("MATCH ENDED", GameBootstrap.PendingNotice);
                GameBootstrap.PendingNotice = null;
            }
            if (m_Tab == Tab.Browse && !UpdateCheck.Blocking) Refresh();
        }

        string MapIdFromChoice()
        {
            var maps = MapCatalog.Available().ToList();
            int i = m_Map.index;
            return i >= 0 && i < maps.Count ? maps[i].Id : MapCatalog.DefaultId();
        }

        void HostMatch()
        {
            m_HostError.text = "";
            string map = MapIdFromChoice();
            var settings = new HostSettings
            {
                PlayerName = Callsign,
                SessionName = m_SessionName.value,
                IsPrivate = m_IsPrivate,
                MapId = map,
            };
            Host.RunBusy("CREATING LOBBY", async () =>
            {
                await SessionService.HostAsync(settings);
                MatchDirector.Spawned -= ApplyMap;
                s_PendingMap = map;
                MatchDirector.Spawned += ApplyMap;
                if (MatchDirector.Instance != null) ApplyMap(MatchDirector.Instance);
            }, e => m_HostError.text = SessionService.Describe(e));
        }

        static string s_PendingMap;
        static void ApplyMap(MatchDirector md)
        {
            MatchDirector.Spawned -= ApplyMap;
            if (md.IsServer && !string.IsNullOrEmpty(s_PendingMap)) md.ServerSetMap(s_PendingMap);
        }

        void JoinByCode()
        {
            m_JoinError.text = "";
            string code = m_Code.value;
            Host.RunBusy("JOINING", async () => await SessionService.JoinByCodeAsync(code, Callsign),
                e => m_JoinError.text = SessionService.Describe(e));
        }

        void JoinById(string id)
        {
            Host.RunBusy("JOINING", async () => await SessionService.JoinByIdAsync(id, Callsign),
                e => { m_BrowseStatus.text = SessionService.Describe(e); Refresh(); });
        }

        async void Refresh()
        {
            if (m_Browsing) return;
            m_Browsing = true;
            m_BrowseStatus.text = "Searching…";
            try
            {
                var list = await SessionService.BrowseAsync();
                m_BrowseList.Clear();
                if (list.Count == 0)
                    m_BrowseList.Add(Ui.Text("No public matches right now. Host one, or refresh in a moment.", "empty-state"));
                foreach (var s in list) m_BrowseList.Add(Row(s));
                m_BrowseStatus.text = $"{list.Count} match{(list.Count == 1 ? "" : "es")} found · {System.DateTime.Now:HH:mm:ss}";
            }
            catch (System.Exception e)
            {
                m_BrowseList.Clear();
                m_BrowseStatus.text = SessionService.Describe(e);
            }
            finally { m_Browsing = false; }
        }

        VisualElement Row(ISessionInfo s)
        {
            string Prop(string key) => s.Properties != null && s.Properties.TryGetValue(key, out var p) ? p.Value : "";
            string mapId = Prop(BuildInfo.PropMap);
            string mapName = MapCatalog.TryGet(mapId, out var m) ? m.Name : (string.IsNullOrEmpty(mapId) ? "—" : mapId);
            string phase = Prop(BuildInfo.PropPhase);
            int players = s.MaxPlayers - s.AvailableSlots;

            var row = Ui.Div("browse-row");
            row.Add(Ui.Text(s.Name, "browse-cell", "browse-name", "nowrap"));
            row.Add(Ui.Text(mapName, "browse-cell", "browse-map", "nowrap"));
            row.Add(Ui.Text(string.IsNullOrEmpty(phase) ? "LOBBY" : phase.ToUpperInvariant(), "browse-cell", "browse-phase"));
            row.Add(Ui.Text($"{players}/{s.MaxPlayers}", "browse-cell", "browse-count"));
            row.Add(Ui.Div("spacer"));
            string id = s.Id;
            row.Add(Ui.Button("JOIN", () => JoinById(id), "btn--small"));
            return row;
        }
    }
}

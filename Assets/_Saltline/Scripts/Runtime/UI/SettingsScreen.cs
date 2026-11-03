using System;
using System.Collections.Generic;
using System.Linq;
using Saltline.Input;
using Saltline.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>Settings overlay: gameplay, video, audio and key bindings. Changes apply immediately.</summary>
    public sealed class SettingsScreen : UiScreen
    {
        enum Tab { Gameplay, Video, Audio, Controls }

        readonly Dictionary<Tab, Button> m_TabButtons = new();
        ScrollView m_Body;
        Tab m_Tab = Tab.Gameplay;
        InputActionRebindingExtensions.RebindingOperation m_Rebind;
        int m_RebindEndFrame = -10;

        /// <summary>True while waiting for a key press to rebind, and for the frame after, so the
        /// Esc that cancels a rebind does not also close the settings.</summary>
        public bool Listening => m_Rebind != null || Time.frameCount - m_RebindEndFrame <= 1;

        protected override VisualElement Build()
        {
            var root = Ui.Div("scrim", "settings");
            var panel = root.Put(Ui.Div("panel", "settings-panel", "col"));
            var head = panel.Put(Ui.Div("settings-head"));
            var titles = head.Put(Ui.Div("col", "grow"));
            titles.Add(Ui.Text("OPTIONS", "eyebrow"));
            titles.Add(Ui.Text("SETTINGS", "title"));

            var tabs = panel.Put(Ui.Div("tabs"));
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = new Button(() => Select(tab)) { text = t.ToString().ToUpperInvariant() };
                b.AddToClassList("tab");
                b.name = "tab" + t;
                tabs.Add(b);
                m_TabButtons[t] = b;
            }

            m_Body = panel.Put(new ScrollView(ScrollViewMode.Vertical).Classes("settings-body"));
            var foot = panel.Put(Ui.Div("settings-foot"));
            foot.Add(Ui.Button("RESET THIS TAB", ResetTab, "btn--ghost", "btn--small"));
            foot.Add(Ui.Div("spacer"));
            foot.Add(Ui.Button("DONE", () => Host.CloseSettings(), "btn--primary", "btn--wide"));
            return root;
        }

        protected override void OnShow() => Select(m_Tab);

        protected override void OnHide()
        {
            CancelRebind();
            GameInput.Instance.SaveBindings();
            GameSettings.Save();
            PlayerPrefs.Save();
        }

        void Select(Tab t)
        {
            CancelRebind();
            m_Tab = t;
            foreach (var kv in m_TabButtons) kv.Value.EnableInClassList("tab--active", kv.Key == t);
            var c = m_Body.contentContainer;
            c.Clear();
            switch (t)
            {
                case Tab.Gameplay: BuildGameplay(c); break;
                case Tab.Video: BuildVideo(c); break;
                case Tab.Audio: BuildAudio(c); break;
                case Tab.Controls: BuildControls(c); break;
            }
        }

        void ResetTab()
        {
            switch (m_Tab)
            {
                case Tab.Gameplay:
                    GameSettings.MouseSensitivity = 1f; GameSettings.AdsSensitivity = 0.85f; GameSettings.ScopeSensitivity = 0.75f;
                    GameSettings.GamepadSensitivity = 1f; GameSettings.InvertY = false; GameSettings.ToggleAim = false;
                    GameSettings.ToggleCrouch = false; GameSettings.FieldOfView = 100f; GameSettings.StartInThirdPerson = false; GameSettings.CameraBob = true;
                    break;
                case Tab.Video:
                    VideoSettings.Preset = VideoSettings.DetectDefault(); VideoSettings.RenderScaleOverride = 0f;
                    VideoSettings.VSync = true; VideoSettings.FrameCap = 0;
                    break;
                case Tab.Audio:
                    GameSettings.MasterVolume = 0.9f;
                    GameSettings.EffectsVolume = 1f;
                    GameSettings.InterfaceVolume = 0.8f;
                    GameSettings.AmbienceVolume = 0.8f;
                    break;
                case Tab.Controls:
                    GameInput.Instance.ResetBindings();
                    break;
            }
            Select(m_Tab);
        }

        // ================================================================== rows

        static VisualElement Row(VisualElement parent, string label, string hint = null)
        {
            var row = parent.Put(Ui.Div("set-row"));
            var l = row.Put(Ui.Div("col", "set-label"));
            l.Add(Ui.Text(label));
            if (!string.IsNullOrEmpty(hint)) l.Add(Ui.Text(hint, "set-hint"));
            return row.Put(Ui.Div("set-control"));
        }

        static void Section(VisualElement parent, string text) => parent.Add(Ui.Text(text, "set-section"));

        static void SliderRow(VisualElement parent, string label, float min, float max, float value, Func<float, string> format, Action<float> set, string hint = null)
        {
            var ctl = Row(parent, label, hint);
            var s = ctl.Put(new Slider(min, max) { value = value }.Classes("grow"));
            var v = ctl.Put(Ui.Text(format(value), "set-value"));
            s.RegisterValueChangedCallback(e => { set(e.newValue); v.text = format(e.newValue); });
        }

        static void ToggleRow(VisualElement parent, string label, bool value, Action<bool> set, string hint = null)
        {
            var ctl = Row(parent, label, hint);
            var t = ctl.Put(new Toggle { value = value });
            t.RegisterValueChangedCallback(e => set(e.newValue));
        }

        static void ChoiceRow(VisualElement parent, string label, List<string> choices, int index, Action<int> set, string hint = null)
        {
            var ctl = Row(parent, label, hint);
            var d = ctl.Put(new DropdownField(choices, Mathf.Clamp(index, 0, choices.Count - 1)).Classes("grow"));
            d.RegisterValueChangedCallback(_ => set(d.index));
        }

        // ================================================================== tabs

        static void BuildGameplay(VisualElement c)
        {
            Section(c, "AIM");
            SliderRow(c, "Mouse sensitivity", 0.05f, 5f, GameSettings.MouseSensitivity, v => v.ToString("0.00"), v => GameSettings.MouseSensitivity = v,
                $"1.00 = {GameSettings.DegreesPerCount:0.000}° per mouse count");
            SliderRow(c, "Aim-down-sights multiplier", 0.1f, 2f, GameSettings.AdsSensitivity, v => v.ToString("0.00"), v => GameSettings.AdsSensitivity = v);
            SliderRow(c, "Scope multiplier", 0.1f, 2f, GameSettings.ScopeSensitivity, v => v.ToString("0.00"), v => GameSettings.ScopeSensitivity = v);
            SliderRow(c, "Gamepad look speed", 0.1f, 4f, GameSettings.GamepadSensitivity, v => v.ToString("0.00"), v => GameSettings.GamepadSensitivity = v);
            ToggleRow(c, "Invert vertical look", GameSettings.InvertY, v => GameSettings.InvertY = v);
            ToggleRow(c, "Toggle aim (instead of hold)", GameSettings.ToggleAim, v => GameSettings.ToggleAim = v);
            ToggleRow(c, "Toggle crouch (instead of hold)", GameSettings.ToggleCrouch, v => GameSettings.ToggleCrouch = v);
            Section(c, "CAMERA");
            SliderRow(c, "Field of view", 70f, 120f, GameSettings.FieldOfView, v => $"{Mathf.RoundToInt(v)}°", v => GameSettings.FieldOfView = Mathf.Round(v),
                "Horizontal, adjusted for your aspect ratio");
            ToggleRow(c, "Start in third person", GameSettings.StartInThirdPerson, v => GameSettings.StartInThirdPerson = v, "Press V in a match to switch at any time");
            ToggleRow(c, "Camera bob", GameSettings.CameraBob, v => GameSettings.CameraBob = v);
        }

        static void BuildVideo(VisualElement c)
        {
            Section(c, "DISPLAY");
            var modes = new List<(string name, FullScreenMode mode)>
            {
                ("Borderless fullscreen", FullScreenMode.FullScreenWindow),
                ("Windowed", FullScreenMode.Windowed),
            };
#if UNITY_STANDALONE_WIN
            modes.Insert(0, ("Exclusive fullscreen", FullScreenMode.ExclusiveFullScreen));
#endif
            int mi = Mathf.Max(0, modes.FindIndex(m => m.mode == VideoSettings.DisplayMode));
            ChoiceRow(c, "Display mode", modes.Select(m => m.name).ToList(), mi, i => VideoSettings.DisplayMode = modes[i].mode);

            var res = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().OrderByDescending(r => r.x * r.y).ToList();
            if (res.Count == 0) res.Add(new Vector2Int(Screen.width, Screen.height));
            var saved = VideoSettings.SavedResolution;
            var current = saved.x > 0 ? saved : new Vector2Int(Screen.width, Screen.height);
            int ri = Mathf.Max(0, res.IndexOf(current));
            ChoiceRow(c, "Resolution", res.Select(r => $"{r.x} × {r.y}").ToList(), ri, i => VideoSettings.SetResolution(res[i].x, res[i].y));
            ToggleRow(c, "VSync", VideoSettings.VSync, v => VideoSettings.VSync = v, "Off gives the lowest input latency; may tear");
            var caps = VideoSettings.FrameCaps.Select(f => f == 0 ? "Unlimited" : $"{f} fps").ToList();
            ChoiceRow(c, "Frame rate limit", caps, Mathf.Max(0, Array.IndexOf(VideoSettings.FrameCaps, VideoSettings.FrameCap)), i => VideoSettings.FrameCap = VideoSettings.FrameCaps[i]);

            Section(c, "QUALITY");
            ChoiceRow(c, "Graphics preset", VideoSettings.PresetNames.ToList(), (int)VideoSettings.Preset, i => VideoSettings.Preset = (QualityPreset)i,
                "Laptop lowers render scale and shadow range for thin-and-light machines");
            SliderRow(c, "Render scale", 0.5f, 1f, VideoSettings.EffectiveRenderScale, v => $"{Mathf.RoundToInt(v * 100)}%", v => VideoSettings.RenderScaleOverride = v,
                "Below 100% renders fewer pixels and upscales (FSR)");
            var pv = VideoSettings.Values(VideoSettings.Preset);
            c.Add(Ui.Text($"Preset detail: MSAA {(pv.Msaa > 1 ? pv.Msaa + "x" : "off")} · shadows {pv.ShadowDistance:0} m, {pv.Cascades} cascade{(pv.Cascades > 1 ? "s" : "")} · HDR {(pv.Hdr ? "on" : "off")} · post-processing {(pv.PostFx ? "on" : "off")}", "set-hint", "mt-16"));
        }

        static void BuildAudio(VisualElement c)
        {
            Section(c, "VOLUME");
            SliderRow(c, "Master volume", 0f, 1f, GameSettings.MasterVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => { GameSettings.MasterVolume = v; AudioListener.volume = v; });
            SliderRow(c, "Effects", 0f, 1f, GameSettings.EffectsVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.EffectsVolume = v,
                "Weapons, footsteps, impacts and hit sounds");
            SliderRow(c, "Interface", 0f, 1f, GameSettings.InterfaceVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.InterfaceVolume = v,
                "Menu sounds and match stings");
            SliderRow(c, "Ambience", 0f, 1f, GameSettings.AmbienceVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.AmbienceVolume = v,
                "Wind, sea and machinery");
        }

        // ================================================================== controls

        static readonly string[] BindableActions =
        {
            "Move", "Jump", "Crouch", "Sprint", "Fire", "Aim", "Reload", "Primary", "Secondary", "SwapWeapon", "ToggleView", "ShoulderSwap", "Scoreboard",
        };

        static string Pretty(string action) => action switch
        {
            "SwapWeapon" => "Swap weapon",
            "ToggleView" => "First / third person",
            "ShoulderSwap" => "Swap shoulder (third person)",
            "Primary" => "Primary weapon",
            "Secondary" => "Sidearm",
            _ => action,
        };

        void BuildControls(VisualElement c)
        {
            Section(c, "KEYBOARD & MOUSE · CLICK A BINDING, THEN PRESS THE NEW KEY (ESC CANCELS)");
            var gi = GameInput.Instance;
            foreach (string name in BindableActions)
            {
                var action = gi.Asset.FindAction(name);
                if (action == null) continue;
                int shown = 0;
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var b = action.bindings[i];
                    if (b.isComposite) continue;
                    string path = b.effectivePath ?? "";
                    if (!path.StartsWith("<Keyboard>") && !path.StartsWith("<Mouse>")) continue;
                    string label = b.isPartOfComposite ? $"{Pretty(name)} · {b.name}" : Pretty(name);
                    if (!b.isPartOfComposite && shown++ > 0) label += " (alternate)";
                    var ctl = Row(c, label);
                    int index = i;
                    Button btn = null;
                    btn = Ui.Button(action.GetBindingDisplayString(i), () => StartRebind(action, index, btn), "bind-btn");
                    ctl.Add(btn);
                }
            }
            c.Add(Ui.Text("Gamepad bindings are fixed in this version. Esc always opens the menu.", "set-hint", "mt-16"));
        }

        void StartRebind(InputAction action, int index, Button btn)
        {
            CancelRebind();
            btn.text = "PRESS A KEY…";
            btn.AddToClassList("bind-btn--listening");
            m_Rebind = GameInput.Instance.StartRebind(action, index, ok =>
            {
                m_Rebind = null;
                m_RebindEndFrame = Time.frameCount;
                btn.RemoveFromClassList("bind-btn--listening");
                btn.text = action.GetBindingDisplayString(index);
            });
        }

        void CancelRebind()
        {
            var op = m_Rebind;
            m_Rebind = null;
            op?.Cancel();
        }
    }
}

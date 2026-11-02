using System;
using System.Collections.Generic;
using Saltline.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>Small helpers for building UI Toolkit trees in code. Styling lives in Saltline.uss.</summary>
    public static class Ui
    {
        public static VisualElement Div(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        public static T Put<T>(this VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        public static T Classes<T>(this T e, params string[] classes) where T : VisualElement
        {
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static void Show(this VisualElement e, bool show) => e.EnableInClassList("hidden", !show);

        public static void Ignore(this VisualElement e) => e.pickingMode = PickingMode.Ignore;

        /// <summary>Label + control stacked vertically (forms).</summary>
        public static VisualElement Field(string label, VisualElement control)
        {
            var box = Div("col");
            box.Add(Text(label, "field-label"));
            box.Add(control);
            return box;
        }

        public static TextField TextInput(string value, int maxLength, string placeholder = null)
        {
            var f = new TextField { value = value ?? "", maxLength = maxLength };
            if (!string.IsNullOrEmpty(placeholder)) f.textEdition.placeholder = placeholder;
            return f;
        }

        public static string TeamClass(Team t) => t == Team.Red ? "text-red" : t == Team.Blue ? "text-blue" : "dim";
        public static string TeamName(Team t) => t == Team.Red ? "RED" : t == Team.Blue ? "BLUE" : "—";

        public static string PlatformTag(ClientPlatform p) => p switch
        {
            ClientPlatform.Windows => "WIN",
            ClientPlatform.MacOS => "MAC",
            ClientPlatform.Linux => "LINUX",
            ClientPlatform.Editor => "EDITOR",
            _ => "?",
        };

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>Keeps a list of child rows in sync with data without rebuilding each frame.</summary>
        public sealed class RowPool<T> where T : VisualElement
        {
            readonly VisualElement m_Parent;
            readonly Func<T> m_Create;
            readonly List<T> m_Rows = new();

            public RowPool(VisualElement parent, Func<T> create) { m_Parent = parent; m_Create = create; }

            public T this[int i] => m_Rows[i];

            public void Resize(int count)
            {
                while (m_Rows.Count < count) { var r = m_Create(); m_Rows.Add(r); m_Parent.Add(r); }
                for (int i = 0; i < m_Rows.Count; i++) m_Rows[i].Show(i < count);
            }
        }
    }

    /// <summary>A full-screen piece of UI that UiRoot shows or hides.</summary>
    public abstract class UiScreen
    {
        public VisualElement Root { get; protected set; }
        public bool Visible { get; private set; }
        protected UiRoot Host { get; private set; }

        public void Init(UiRoot host, VisualElement layer)
        {
            Host = host;
            Root = Build();
            Root.AddToClassList("layer");
            layer.Add(Root);
            Root.Show(false);
        }

        protected abstract VisualElement Build();

        public void SetVisible(bool visible)
        {
            if (visible == Visible) return;
            Visible = visible;
            Root.Show(visible);
            if (visible) OnShow(); else OnHide();
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        public virtual void Tick() { }
    }
}

using System;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// 系统窗口 — 查看 Ticker 列表和系统执行顺序。
    /// </summary>
    public class SystemsWindow : EditorWindow
    {
        private int m_SelectedTicker;
        private string[] m_TickerNames = Array.Empty<string>();
        private Vector2 m_ScrollPos;
        private bool m_HighlightListeners;

        public static void Open()
        {
            var window = GetWindow<SystemsWindow>("Ember Systems");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnGUI()
        {
            var manager = ECSManager.Active;
            if (manager == null || manager.World == null)
            {
                EditorGUILayout.HelpBox(
                    "No active ECSManager found.\nSet ECSManager.Active = yourManager in your game code.",
                    MessageType.Info);
                return;
            }

            var views = manager.GetDebugViews();
            if (views.Length == 0)
            {
                EditorGUILayout.LabelField("No tickers registered.");
                return;
            }

            EditorGUILayout.Space();

            // Ticker dropdown
            if (m_TickerNames.Length != views.Length)
            {
                m_TickerNames = new string[views.Length];
                for (int i = 0; i < views.Length; i++)
                    m_TickerNames[i] = $"Ticker #{views[i].Index} ({views[i].Systems.Length} systems)";
            }

            if (m_SelectedTicker >= views.Length)
                m_SelectedTicker = 0;

            EditorGUILayout.BeginHorizontal();
            m_SelectedTicker = EditorGUILayout.Popup("Ticker", m_SelectedTicker, m_TickerNames);
            m_HighlightListeners = EditorGUILayout.ToggleLeft("Show Lifecycle Listeners", m_HighlightListeners);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("#", EditorStyles.toolbarButton, GUILayout.Width(30));
            EditorGUILayout.LabelField("System", EditorStyles.toolbarButton);
            EditorGUILayout.LabelField("Type", EditorStyles.toolbarButton, GUILayout.Width(100));
            EditorGUILayout.LabelField("Hooks", EditorStyles.toolbarButton, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            // System list
            var view = views[m_SelectedTicker];
            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);
            for (int i = 0; i < view.Systems.Length; i++)
            {
                var sys = view.Systems[i];
                bool isListener = sys.HasEntityCreatedHook || sys.HasEntityDestroyedHook ||
                                  sys.HasComponentAddedHook || sys.HasComponentRemovedHook;

                var bgColor = GUI.backgroundColor;
                if (m_HighlightListeners && isListener)
                    GUI.backgroundColor = new Color(0.3f, 0.5f, 0.3f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(30));

                GUIStyle nameStyle = sys.BaseType == "SystemGroup" ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUILayout.LabelField(sys.Name, nameStyle);

                EditorGUILayout.LabelField(sys.BaseType, GUILayout.Width(100));

                string hooks = FormatHooks(sys);
                EditorGUILayout.LabelField(hooks, GUILayout.Width(80));

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = bgColor;
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Ticker #{view.Index}: {view.Systems.Length} systems",
                EditorStyles.miniLabel);
        }

        private static string FormatHooks(SystemInfoDebugView sys)
        {
            if (!sys.HasEntityCreatedHook && !sys.HasEntityDestroyedHook &&
                !sys.HasComponentAddedHook && !sys.HasComponentRemovedHook)
                return "—";

            var parts = new System.Collections.Generic.List<string>(4);
            if (sys.HasEntityCreatedHook) parts.Add("EC");
            if (sys.HasEntityDestroyedHook) parts.Add("ED");
            if (sys.HasComponentAddedHook) parts.Add("CA");
            if (sys.HasComponentRemovedHook) parts.Add("CR");
            return string.Join(", ", parts);
        }
    }
}

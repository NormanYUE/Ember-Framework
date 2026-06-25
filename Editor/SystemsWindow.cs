using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    public class SystemsWindow : EditorWindow
    {
        private const float W_INDEX = 25f;
        private const float W_TYPE = 80f;
        private const float W_HOOKS = 45f;
        private const float W_TIME = 72f;
        private const float W_TIME_SMALL = 55f;
        private const float W_COUNT = 50f;
        private const float W_ERROR = 140f;

        private int m_SelectedTicker;
        private string[] m_TickerNames = Array.Empty<string>();
        private Vector2 m_ScrollPos;
        private bool m_HighlightListeners;
        private bool m_ShowLayers;
        private bool m_ShowGraph;

        // Graph view state
        private DependencyGraphDebugView m_GraphView;
        private int m_SelectedGraphNode = -1;
        private int m_SelectedGraphLayer = -1;
        private Texture2D m_ParallelTex;
        private Texture2D m_SerialTex;
        private Texture2D m_NodeBg;

        public static void Open()
        {
            var window = GetWindow<SystemsWindow>("Ember Systems");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnDestroy()
        {
            if (m_ParallelTex) DestroyImmediate(m_ParallelTex);
            if (m_SerialTex) DestroyImmediate(m_SerialTex);
            if (m_NodeBg) DestroyImmediate(m_NodeBg);
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

            // ── Toolbar ──
            DrawToolbar(manager, views);

            EditorGUILayout.Space();

            if (m_ShowGraph)
                DrawTickerGraph(manager);
            else
                DrawTickerTable(views);
        }

        private void DrawToolbar(ECSManager manager, SystemTickerDebugView[] views)
        {
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

            // Graph / Table toggle
            var origColor = GUI.backgroundColor;
            GUI.backgroundColor = m_ShowGraph ? new Color(0.3f, 0.5f, 0.8f) : Color.gray;
            if (GUILayout.Button("Graph", EditorStyles.toolbarButton, GUILayout.Width(55)))
                m_ShowGraph = true;
            GUI.backgroundColor = !m_ShowGraph ? new Color(0.3f, 0.5f, 0.8f) : Color.gray;
            if (GUILayout.Button("Table", EditorStyles.toolbarButton, GUILayout.Width(55)))
                m_ShowGraph = false;
            GUI.backgroundColor = origColor;

            m_HighlightListeners = EditorGUILayout.ToggleLeft("Lifecycle", m_HighlightListeners, GUILayout.Width(70));
            m_ShowLayers = EditorGUILayout.ToggleLeft("Layers", m_ShowLayers, GUILayout.Width(65));
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════
        //  TABLE VIEW
        // ══════════════════════════════════════════════════

        private void DrawTickerTable(SystemTickerDebugView[] views)
        {
            var view = views[m_SelectedTicker];
            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            // Header
            var headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(headerRect, new Color(0.22f, 0.22f, 0.22f, 1f));
            var rr = new Rect(headerRect.x + 4, headerRect.y, headerRect.width - 8, headerRect.height);
            rr.width = W_INDEX;
            EditorGUI.LabelField(rr, "#", EditorStyles.miniLabel);
            rr.x += W_INDEX;
            rr.width = headerRect.width - 8 - W_INDEX - W_TYPE - W_HOOKS - W_TIME - W_TIME_SMALL * 2 - W_COUNT - W_ERROR;
            EditorGUI.LabelField(rr, "System", EditorStyles.miniLabel);
            rr.x += rr.width;
            rr.width = W_TYPE;
            EditorGUI.LabelField(rr, "Type", EditorStyles.miniLabel);
            rr.x += W_TYPE;
            rr.width = W_HOOKS;
            EditorGUI.LabelField(rr, "Hooks", EditorStyles.miniLabel);
            rr.x += W_HOOKS;
            rr.width = W_TIME;
            EditorGUI.LabelField(rr, "LastTick", EditorStyles.miniLabel);
            rr.x += W_TIME;
            rr.width = W_TIME_SMALL;
            EditorGUI.LabelField(rr, "Avg", EditorStyles.miniLabel);
            rr.x += W_TIME_SMALL;
            rr.width = W_TIME_SMALL;
            EditorGUI.LabelField(rr, "Max", EditorStyles.miniLabel);
            rr.x += W_TIME_SMALL;
            rr.width = W_COUNT;
            EditorGUI.LabelField(rr, "Count", EditorStyles.miniLabel);
            rr.x += W_COUNT;
            rr.width = W_ERROR;
            EditorGUI.LabelField(rr, "Error", EditorStyles.miniLabel);

            for (int i = 0; i < view.Systems.Length; i++)
            {
                var sys = view.Systems[i];
                bool isListener = sys.HasEntityCreatedHook || sys.HasEntityDestroyedHook ||
                                  sys.HasComponentAddedHook || sys.HasComponentRemovedHook;

                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 3);

                if (m_HighlightListeners && isListener)
                    EditorGUI.DrawRect(rowRect, new Color(0.2f, 0.4f, 0.2f, 0.3f));

                var cr = new Rect(rowRect.x + 4, rowRect.y, rowRect.width - 8, rowRect.height);
                cr.width = W_INDEX;
                EditorGUI.LabelField(cr, i.ToString());
                cr.x += W_INDEX;
                cr.width = rowRect.width - 8 - W_INDEX - W_TYPE - W_HOOKS - W_TIME - W_TIME_SMALL * 2 - W_COUNT - W_ERROR;

                GUIStyle nameStyle = sys.BaseType == "SystemGroup" ? EditorStyles.boldLabel : EditorStyles.label;
                GUI.enabled = sys.BaseType == "SystemGroup";
                EditorGUI.LabelField(cr, sys.Name, nameStyle);
                GUI.enabled = true;

                cr.x += cr.width;
                cr.width = W_TYPE;
                EditorGUI.LabelField(cr, sys.BaseType);
                cr.x += W_TYPE;
                cr.width = W_HOOKS;
                EditorGUI.LabelField(cr, FormatHooks(sys));

                cr.x += W_HOOKS;
                cr.width = W_TIME;
                GUI.color = sys.LastTickMs > 1.0 ? Color.red : Color.gray;
                EditorGUI.LabelField(cr, sys.LastTickMs >= 0 ? $"{sys.LastTickMs:F2}ms" : "—", EditorStyles.miniLabel);
                cr.x += W_TIME;
                cr.width = W_TIME_SMALL;
                GUI.color = Color.gray;
                EditorGUI.LabelField(cr, sys.AvgTickMs >= 0 ? $"{sys.AvgTickMs:F2}" : "—", EditorStyles.miniLabel);
                cr.x += W_TIME_SMALL;
                cr.width = W_TIME_SMALL;
                EditorGUI.LabelField(cr, sys.MaxTickMs >= 0 ? $"{sys.MaxTickMs:F2}" : "—", EditorStyles.miniLabel);
                cr.x += W_TIME_SMALL;
                cr.width = W_COUNT;
                EditorGUI.LabelField(cr, sys.TickCount.ToString(), EditorStyles.miniLabel);

                cr.x += W_COUNT;
                cr.width = W_ERROR;
                string errText = sys.LastError ?? "—";
                GUI.color = sys.LastError != null ? Color.red : Color.gray;
                EditorGUI.LabelField(cr, errText.Length > 20 ? errText.Substring(0, 20) + "…" : errText, EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Ticker #{view.Index}: {view.Systems.Length} systems", EditorStyles.miniLabel);

            if (m_ShowLayers)
                DrawParallelLayers(views);
        }

        private void DrawParallelLayers(SystemTickerDebugView[] views)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parallel Layers", EditorStyles.boldLabel);

            var info = views[m_SelectedTicker];
            // Build layer info from the debug views (existing system)
            var gv = m_GraphView;
            if (gv.Layers == null) return;

            for (int i = 0; i < gv.Layers.Length; i++)
            {
                var layer = gv.Layers[i];
                var bgColor = GUI.backgroundColor;
                GUI.backgroundColor = layer.IsParallel ? new Color(0.2f, 0.5f, 0.2f, 0.3f) : GUI.backgroundColor;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string label = layer.IsParallel
                    ? $"Layer {i} (PARALLEL — {layer.SystemNames.Length} systems)"
                    : $"Layer {i} (serial — {layer.SystemNames.Length} systems)";
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.Join(" → ", layer.SystemNames), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                GUI.backgroundColor = bgColor;
            }
        }

        // ══════════════════════════════════════════════════
        //  GRAPH VIEW
        // ══════════════════════════════════════════════════

        private void DrawTickerGraph(ECSManager manager)
        {
            // Build graph data from the selected ticker
            var ticker = manager.GetTicker(m_SelectedTicker);
            if (ticker == null) return;

            m_GraphView = null;
            // Invoke the debug method via reflection since it's public on SystemTicker
            // Actually it IS public now, but we don't have direct access.
            // Use GetDependencyGraphDebugView() through the ticker.
            try
            {
                var method = ticker.GetType().GetMethod("GetDependencyGraphDebugView",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null)
                    m_GraphView = method.Invoke(ticker, null) as DependencyGraphDebugView? ?? default;
            }
            catch { }

            if (m_GraphView.Layers == null || m_GraphView.Layers.Length == 0)
            {
                EditorGUILayout.HelpBox("No dependency graph data available.", MessageType.Info);
                return;
            }

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            var nodeRects = new Dictionary<string, Rect>();
            float availableWidth = position.width - 40;
            float layerLabelWidth = 70f;

            for (int li = 0; li < m_GraphView.Layers.Length; li++)
            {
                var layer = m_GraphView.Layers[li];

                // Layer header
                EditorGUILayout.BeginHorizontal();

                // Layer label column
                EditorGUILayout.BeginVertical(GUILayout.Width(layerLabelWidth));
                GUI.color = layer.IsParallel ? Color.green : Color.gray;
                EditorGUILayout.LabelField($"L{li}", EditorStyles.boldLabel, GUILayout.Width(layerLabelWidth));
                GUI.color = Color.white;
                EditorGUILayout.LabelField(layer.IsParallel ? "parallel" : "serial", EditorStyles.miniLabel,
                    GUILayout.Width(layerLabelWidth));
                EditorGUILayout.EndVertical();

                // System nodes row
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var bgColor = GUI.backgroundColor;
                GUI.backgroundColor = layer.IsParallel ? new Color(0.15f, 0.35f, 0.15f) : new Color(0.25f, 0.25f, 0.25f);
                var layerBox = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.MinHeight(70));
                GUI.backgroundColor = bgColor;

                for (int si = 0; si < layer.SystemNames.Length; si++)
                {
                    bool selected = m_SelectedGraphLayer == li && m_SelectedGraphNode == si;
                    bool isJob = layer.SystemTypes[si] == "JobSystemBase";

                    var nodeStyle = new GUIStyle(EditorStyles.helpBox);
                    if (selected)
                        nodeStyle.normal.background = MakeTex(2, 2, new Color(0.3f, 0.5f, 0.8f, 0.6f));
                    else if (isJob)
                        nodeStyle.normal.background = MakeTex(2, 2, new Color(0.25f, 0.3f, 0.4f, 0.5f));
                    else
                        nodeStyle.normal.background = MakeTex(2, 2, new Color(0.3f, 0.3f, 0.35f, 0.5f));

                    EditorGUILayout.BeginVertical(nodeStyle, GUILayout.Width(130), GUILayout.MinHeight(50));

                    // System name
                    EditorGUILayout.LabelField(layer.SystemNames[si], EditorStyles.boldLabel);

                    // Type badge
                    GUI.color = isJob ? new Color(0.5f, 0.7f, 1f) : Color.gray;
                    EditorGUILayout.LabelField(layer.SystemTypes[si], EditorStyles.miniLabel);
                    GUI.color = Color.white;

                    // Read/Write summary
                    if (layer.ReadComponents[si].Length > 0)
                        EditorGUILayout.LabelField($"R: {string.Join(",", layer.ReadComponents[si])}",
                            EditorStyles.miniLabel);
                    if (layer.WriteComponents[si].Length > 0)
                        EditorGUILayout.LabelField($"W: {string.Join(",", layer.WriteComponents[si])}",
                            EditorStyles.miniLabel);

                    // Click detection
                    var clickRect = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown && clickRect.Contains(Event.current.mousePosition))
                    {
                        m_SelectedGraphLayer = li;
                        m_SelectedGraphNode = si;
                        Event.current.Use();
                    }

                    EditorGUILayout.EndVertical();

                    // Store node rect for arrow drawing (use the whole vertical block rect)
                    if (Event.current.type == EventType.Repaint)
                    {
                        var lastRect = GUILayoutUtility.GetLastRect();
                        nodeRects[layer.SystemNames[si]] = lastRect;
                    }

                    // Arrow between consecutive systems in same layer
                    if (si < layer.SystemNames.Length - 1)
                    {
                        GUILayout.Space(5);
                        EditorGUILayout.LabelField("→", EditorStyles.boldLabel, GUILayout.Width(20));
                        GUILayout.Space(5);
                    }
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
            }

            // Draw dependency arrows (Repaint event)
            if (Event.current.type == EventType.Repaint)
            {
                foreach (var layer in m_GraphView.Layers)
                {
                    foreach (var edge in layer.Edges)
                    {
                        if (edge.FromIndex >= layer.SystemNames.Length ||
                            edge.ToIndex >= layer.SystemNames.Length)
                            continue;

                        var fromName = layer.SystemNames[edge.FromIndex];
                        var toName = layer.SystemNames[edge.ToIndex];

                        if (nodeRects.TryGetValue(fromName, out var fRect) &&
                            nodeRects.TryGetValue(toName, out var tRect))
                        {
                            // Draw arrow from right of 'to' node to left of 'from' node
                            var start = new Vector3(
                                nodeRects[layer.SystemNames[edge.ToIndex]].xMax + 15,
                                nodeRects[layer.SystemNames[edge.ToIndex]].center.y, 0);
                            var end = new Vector3(
                                nodeRects[layer.SystemNames[edge.FromIndex]].x - 10,
                                nodeRects[layer.SystemNames[edge.FromIndex]].center.y, 0);

                            Handles.BeginGUI();
                            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                            Handles.DrawLine(start, end);
                            Handles.EndGUI();
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            // ── Selected node detail ──
            DrawSelectedNodeDetail();

            // Summary
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Ticker #{m_SelectedTicker}: {m_GraphView.SystemCount} systems, {m_GraphView.LayerCount} layers",
                EditorStyles.miniLabel);
        }

        private void DrawSelectedNodeDetail()
        {
            if (m_SelectedGraphLayer < 0 || m_SelectedGraphNode < 0 ||
                m_SelectedGraphLayer >= m_GraphView.Layers.Length ||
                m_SelectedGraphNode >= m_GraphView.Layers[m_SelectedGraphLayer].SystemNames.Length)
            {
                m_SelectedGraphLayer = -1;
                m_SelectedGraphNode = -1;
                return;
            }

            var layer = m_GraphView.Layers[m_SelectedGraphLayer];
            int si = m_SelectedGraphNode;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField(layer.SystemNames[si], EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Type", layer.SystemTypes[si]);
            EditorGUILayout.LabelField("Layer", $"{m_SelectedGraphLayer} ({(layer.IsParallel ? "parallel" : "serial")})");

            // Hooks from the table view info
            if (TryGetSystemInfo(layer.SystemNames[si], out var sysInfo))
            {
                string hooks = FormatHooks(sysInfo);
                EditorGUILayout.LabelField("Hooks", hooks);
            }

            EditorGUILayout.Space();

            // Read components
            if (layer.ReadComponents[si].Length > 0)
            {
                EditorGUILayout.LabelField("Read Components", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(string.Join(", ", layer.ReadComponents[si]), EditorStyles.wordWrappedMiniLabel);
            }

            // Write components
            if (layer.WriteComponents[si].Length > 0)
            {
                EditorGUILayout.LabelField("Write Components", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(string.Join(", ", layer.WriteComponents[si]), EditorStyles.wordWrappedMiniLabel);
            }

            // Dependencies
            if (layer.Edges.Length > 0)
            {
                var deps = new System.Collections.Generic.List<string>();
                foreach (var e in layer.Edges)
                {
                    if (e.FromIndex == si)
                        deps.Add($"  → {layer.SystemNames[e.ToIndex]}");
                }

                if (deps.Count > 0)
                {
                    EditorGUILayout.LabelField("Depends On", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(string.Join("\n", deps), EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private bool TryGetSystemInfo(string name, out SystemInfoDebugView info)
        {
            var manager = ECSManager.Active;
            if (manager?.World == null)
            {
                info = default;
                return false;
            }

            var views = manager.GetDebugViews();
            if (m_SelectedTicker >= views.Length)
            {
                info = default;
                return false;
            }

            foreach (var s in views[m_SelectedTicker].Systems)
            {
                if (s.Name == name)
                {
                    info = s;
                    return true;
                }
            }

            info = default;
            return false;
        }

        // ── Shared helpers ──

        private static string FormatHooks(SystemInfoDebugView sys)
        {
            if (!sys.HasEntityCreatedHook && !sys.HasEntityDestroyedHook &&
                !sys.HasComponentAddedHook && !sys.HasComponentRemovedHook)
                return "—";

            var parts = new List<string>(4);
            if (sys.HasEntityCreatedHook) parts.Add("EC");
            if (sys.HasEntityDestroyedHook) parts.Add("ED");
            if (sys.HasComponentAddedHook) parts.Add("CA");
            if (sys.HasComponentRemovedHook) parts.Add("CR");
            return string.Join(",", parts);
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}

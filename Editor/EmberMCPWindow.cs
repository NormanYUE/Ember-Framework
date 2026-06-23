using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    public class EmberMCPWindow : EditorWindow
    {
        private static readonly string s_ProjectRoot = EmberBridge.ProjectRoot;
        private static string RelativeServerPath => EmberBridge.RelativeServerPath;

        private readonly List<LogEntry> m_Log = new();
        private Vector2 m_LogScroll;
        private bool m_LogExpanded = true;
        private bool m_ServerExpanded = true;
        private bool m_ClientExpanded = true;
        private bool m_SettingsExpanded;
        private int m_LastLogCount;
        private DateTime m_BridgeStartTime;

        [MenuItem("Window/Ember/MCP")]
        public static void Open()
        {
            var window = GetWindow<EmberMCPWindow>("Ember MCP");
            window.minSize = new Vector2(420, 350);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            m_BridgeStartTime = DateTime.Now;
            AutoUpdateConfigPaths();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            // Poll log updates from bridge
            if (EmberBridge.IsRunning && EmberBridge.LastRequest != null)
            {
                if (m_Log.Count == 0 || m_LastLogCount != EmberBridge.RequestCount)
                {
                    m_LastLogCount = EmberBridge.RequestCount;
                    if (m_Log.Count >= 100) m_Log.RemoveAt(0);
                    m_Log.Add(new LogEntry
                    {
                        Method = EmberBridge.LastRequest,
                        Response = EmberBridge.LastResponse ?? "",
                        TimeMs = EmberBridge.LastRequestMs,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss")
                    });
                }
            }

            Repaint();
        }

        private void OnGUI()
        {
            DrawStatusBar();
            EditorGUILayout.Space();

            m_ClientExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_ClientExpanded, "Client Setup");
            if (m_ClientExpanded) DrawClientSetup();
            EditorGUILayout.EndFoldoutHeaderGroup();

            m_ServerExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_ServerExpanded, "Server");
            if (m_ServerExpanded) DrawServerInfo();
            EditorGUILayout.EndFoldoutHeaderGroup();

            m_LogExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_LogExpanded,
                $"Request Log ({m_Log.Count})");
            if (m_LogExpanded) DrawRequestLog();
            EditorGUILayout.EndFoldoutHeaderGroup();

            m_SettingsExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_SettingsExpanded, "Settings");
            if (m_SettingsExpanded) DrawSettings();
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ── Status Bar ──

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: status indicator + start/stop
            EditorGUILayout.BeginHorizontal();

            bool running = EmberBridge.IsRunning;
            bool connected = EmberBridge.IsConnected;

            // Status dot with animation
            GUI.color = connected ? Color.green : (running ? Color.yellow : Color.gray);
            EditorGUILayout.LabelField(connected ? "● Connected" : (running ? "◉ Waiting" : "○ Stopped"),
                EditorStyles.boldLabel, GUILayout.Width(120));
            GUI.color = Color.white;

            if (running)
                EditorGUILayout.LabelField($"Port {EmberBridge.ActivePort}", EditorStyles.miniLabel, GUILayout.Width(65));

            GUILayout.FlexibleSpace();

            if (running)
            {
                var origColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("Stop Server", GUILayout.Width(90)))
                {
                    EmberBridge.Stop();
                    m_Log.Clear();
                    m_LastLogCount = 0;
                }
                GUI.backgroundColor = origColor;
            }
            else
            {
                var origColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                if (GUILayout.Button("Start Server", GUILayout.Width(90)))
                {
                    EmberBridge.Start();
                    m_BridgeStartTime = DateTime.Now;
                }
                GUI.backgroundColor = origColor;
            }

            EditorGUILayout.EndHorizontal();

            // Row 2: real-time stats
            EditorGUILayout.BeginHorizontal();

            if (running)
            {
                var uptime = DateTime.Now - m_BridgeStartTime;
                EditorGUILayout.LabelField(
                    $"Uptime: {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}",
                    EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                EditorGUILayout.LabelField(
                    $"{EmberBridge.RequestCount} requests",
                    EditorStyles.miniLabel, GUILayout.Width(100));
            }
            else
            {
                EditorGUILayout.LabelField("Server not running", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── Client Setup ──

        private void DrawClientSetup()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string ccProjectConfig = Path.Combine(s_ProjectRoot, ".mcp.json");
            DrawClientRow("Claude Code", ccProjectConfig);

            string codexProjectConfig = Path.Combine(s_ProjectRoot, ".codex", "config.toml");
            DrawClientRow("Codex", codexProjectConfig);

            EditorGUILayout.EndVertical();
        }

        private void DrawClientRow(string clientName, string configPath)
        {
            EditorGUILayout.BeginHorizontal();

            bool configExists = File.Exists(configPath);
            bool hasEmber = configExists && ConfigHasEmber(configPath);
            string status = hasEmber ? "● Installed"
                : (configExists ? "○ Not installed" : "— No config");

            EditorGUILayout.LabelField(clientName, GUILayout.Width(100));
            GUI.color = hasEmber ? Color.green : Color.gray;
            EditorGUILayout.LabelField(status, GUILayout.Width(130));
            GUI.color = Color.white;

            EditorGUILayout.LabelField(
                configPath.Replace(s_ProjectRoot, "").TrimStart('/'),
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (hasEmber)
            {
                if (GUILayout.Button("Uninstall", GUILayout.Width(70)))
                    EmberBridge.RemoveEmberFromConfig(configPath, clientName);
            }
            else
            {
                if (GUILayout.Button("Install", GUILayout.Width(70)))
                    EmberBridge.InstallEmberToConfig(configPath, clientName);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── Config helpers ──

        /// 窗口打开时确认配置（Bridge 若已运行则在 Start 时已自动修复，此处二次幂等确认）。
        private void AutoUpdateConfigPaths() => EmberBridge.AutoUpdateConfigPaths(silent: false);

        private bool ConfigHasEmber(string configPath) => EmberBridge.ConfigHasEmber(configPath);

        // ── Server Info ──

        private void DrawServerInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("MCP Server", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Protocol", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("JSON-lines over TCP");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("DLL Path", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField(RelativeServerPath, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Mode", EditorStyles.miniLabel, GUILayout.Width(60));
            GUI.color = EditorApplication.isPlaying ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(
                EditorApplication.isPlaying ? "Read + Write (Play Mode)" : "Read Only (Edit Mode)",
                EditorStyles.miniLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            // Live connection stats
            if (EmberBridge.IsRunning)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Client", EditorStyles.miniLabel, GUILayout.Width(60));
                GUI.color = EmberBridge.IsConnected ? Color.green : Color.yellow;
                EditorGUILayout.LabelField(
                    EmberBridge.IsConnected ? "MCP client connected" : "Waiting for MCP client...",
                    EditorStyles.miniLabel);
                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        // ── Request Log ──

        private void DrawRequestLog()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Toolbar with clear button and last request time
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                m_Log.Clear();
            GUILayout.FlexibleSpace();
            if (m_Log.Count > 0)
                EditorGUILayout.LabelField(
                    $"Last: {m_Log[^1].Timestamp}",
                    EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();

            m_LogScroll = EditorGUILayout.BeginScrollView(
                m_LogScroll, GUILayout.Height(Mathf.Max(150, position.height - 500)));

            if (m_Log.Count == 0)
            {
                EditorGUILayout.LabelField("Waiting for requests...",
                    EditorStyles.centeredGreyMiniLabel, GUILayout.Height(40));
            }
            else
            {
                for (int i = m_Log.Count - 1; i >= 0; i--)
                {
                    var entry = m_Log[i];
                    EditorGUILayout.BeginHorizontal();

                    // Color-code by latency
                    Color bg = entry.TimeMs > 100 ? new Color(0.5f, 0.2f, 0.2f) :
                        (entry.TimeMs > 10 ? new Color(0.4f, 0.4f, 0.1f) : Color.clear);
                    var origBg = GUI.backgroundColor;
                    if (bg != Color.clear) GUI.backgroundColor = bg;

                    // Timestamp
                    EditorGUILayout.LabelField(entry.Timestamp, EditorStyles.miniLabel, GUILayout.Width(70));

                    // Method name
                    EditorGUILayout.LabelField(entry.Method, EditorStyles.boldLabel, GUILayout.Width(210));

                    // Latency
                    GUI.color = entry.TimeMs > 100 ? Color.red : (entry.TimeMs > 10 ? Color.yellow : Color.green);
                    EditorGUILayout.LabelField($"{entry.TimeMs:F1}ms", EditorStyles.miniLabel, GUILayout.Width(55));
                    GUI.color = Color.white;

                    // Response preview
                    EditorGUILayout.LabelField(entry.Response, EditorStyles.wordWrappedMiniLabel);

                    GUI.backgroundColor = origBg;
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            // Auto-scroll to bottom on new entries
            if (m_Log.Count > 0 && Event.current.type == EventType.Repaint)
                m_LogScroll.y = float.MaxValue;

            EditorGUILayout.EndVertical();
        }

        // ── Settings ──

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            bool autoStart = EditorGUILayout.ToggleLeft("Auto Start on Launch", EmberBridge.AutoStart);
            if (EditorGUI.EndChangeCheck())
                EmberBridge.AutoStart = autoStart;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Port Range", GUILayout.Width(80));
            EditorGUILayout.LabelField("9090 — 9099");
            EditorGUILayout.EndHorizontal();

            if (EmberBridge.IsRunning)
                EditorGUILayout.LabelField($"Active Port: {EmberBridge.ActivePort}", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private struct LogEntry
        {
            public string Timestamp;
            public string Method;
            public string Response;
            public double TimeMs;
        }
    }
}

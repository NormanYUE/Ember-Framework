using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// Ember MCP 窗口 — 状态监控 + 请求日志 + 客户端一键安装。
    /// </summary>
    public class EmberMCPWindow : EditorWindow
    {
        private static readonly string s_HomeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static readonly string s_ServerPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Packages", "com.ember.ecs", "Tools~", "Ember.Mcp.Server.dll"));

        private readonly List<LogEntry> m_Log = new();
        private Vector2 m_LogScroll;
        private bool m_LogExpanded = true;
        private bool m_ServerExpanded = true;
        private bool m_ClientExpanded = true;
        private bool m_SettingsExpanded;

        private double m_LastRefreshTime;

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
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup - m_LastRefreshTime > 0.5)
            {
                m_LastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
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

            // Refresh log from bridge
            if (EmberBridge.RequestCount > m_Log.Count ||
                (m_Log.Count > 0 && m_Log[^1].Method != EmberBridge.LastRequest))
            {
                if (EmberBridge.LastRequest != null)
                {
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
        }

        // ── Status Bar ──

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            bool connected = EmberBridge.IsConnected;
            GUI.color = connected ? Color.green : (EmberBridge.ActivePort > 0 ? Color.yellow : Color.red);
            EditorGUILayout.LabelField(connected ? "● Connected" : (EmberBridge.ActivePort > 0 ? "◉ Waiting" : "○ Stopped"),
                EditorStyles.boldLabel);
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Port: {EmberBridge.ActivePort}", GUILayout.Width(80));
            EditorGUILayout.LabelField(Application.productName, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ── Client Setup ──

        private void DrawClientSetup()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawClientRow("Claude Code",
                Path.Combine(s_HomeDir, ".claude", "settings.json"),
                ".claude/settings.json (project)");

            DrawClientRow("Codex",
                Path.Combine(s_HomeDir, ".codex", "config.toml"),
                ".codex/config.toml (project)");

            EditorGUILayout.EndVertical();
        }

        private void DrawClientRow(string clientName, string globalPath, string projectRelPath)
        {
            EditorGUILayout.BeginHorizontal();

            bool globalExists = File.Exists(globalPath);
            bool hasEmberGlobal = globalExists && ConfigHasEmber(globalPath);
            string status = hasEmberGlobal ? "● Installed" : (globalExists ? "○ Not installed" : "— No config");

            EditorGUILayout.LabelField(clientName, GUILayout.Width(100));
            GUI.color = hasEmberGlobal ? Color.green : Color.gray;
            EditorGUILayout.LabelField(status, GUILayout.Width(120));
            GUI.color = Color.white;

            if (hasEmberGlobal)
            {
                if (GUILayout.Button("Uninstall", GUILayout.Width(70)))
                {
                    RemoveEmberFromConfig(globalPath);
                }
            }
            else
            {
                if (GUILayout.Button("Install", GUILayout.Width(70)))
                {
                    InstallEmberToConfig(globalPath, clientName);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool ConfigHasEmber(string configPath)
        {
            try
            {
                string content = File.ReadAllText(configPath);
                if (configPath.EndsWith(".json"))
                {
                    using var doc = JsonDocument.Parse(content);
                    return doc.RootElement.TryGetProperty("mcpServers", out var servers) &&
                           servers.TryGetProperty("ember", out _);
                }
                else if (configPath.EndsWith(".toml"))
                {
                    // Simple TOML check for [mcp_servers.ember]
                    return content.Contains("[mcp_servers.ember]") ||
                           content.Contains("[mcp_servers]\nember") ||
                           content.Contains("\"ember\"");
                }
            }
            catch { }
            return false;
        }

        private void InstallEmberToConfig(string configPath, string clientName)
        {
            try
            {
                bool isToml = configPath.EndsWith(".toml");

                if (File.Exists(configPath))
                {
                    // Append to existing config
                    string content = File.ReadAllText(configPath);
                    if (content.Contains("\"ember\"") || content.Contains("[mcp_servers.ember]"))
                        return; // already installed

                    string entry = isToml
                        ? $"\n[mcp_servers.ember]\ncommand = \"dotnet\"\nargs = [\"exec\", \"{s_ServerPath}\", \"--port\", \"{EmberBridge.ActivePort}\"]\n"
                        : content.TrimEnd().EndsWith("}")
                            ? content.TrimEnd().TrimEnd('}').TrimEnd() +
                              $",\n  \"mcpServers\": {{\n    \"ember\": {{\n      \"command\": \"dotnet\",\n      \"args\": [\"exec\", \"{s_ServerPath}\", \"--port\", \"{EmberBridge.ActivePort}\"]\n    }}\n  }}\n}}"
                            : content;

                    File.Copy(configPath, configPath + ".bak", true);
                    File.WriteAllText(configPath, entry);
                }
                else
                {
                    // Create new config
                    string dir = Path.GetDirectoryName(configPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string newConfig = isToml
                        ? $"[mcp_servers.ember]\ncommand = \"dotnet\"\nargs = [\"exec\", \"{s_ServerPath}\", \"--port\", \"{EmberBridge.ActivePort}\"]\n"
                        : $"{{\n  \"mcpServers\": {{\n    \"ember\": {{\n      \"command\": \"dotnet\",\n      \"args\": [\"exec\", \"{s_ServerPath}\", \"--port\", \"{EmberBridge.ActivePort}\"]\n    }}\n  }}\n}}\n";

                    File.WriteAllText(configPath, newConfig);
                }

                EditorUtility.DisplayDialog("Ember MCP",
                    $"Installed to {clientName}.\nRestart {clientName} for changes to take effect.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Install Failed", ex.Message, "OK");
            }
        }

        private void RemoveEmberFromConfig(string configPath)
        {
            try
            {
                string content = File.ReadAllText(configPath);
                // Simple removal — find the ember server entry and remove it
                int start = content.IndexOf("\"ember\"");
                if (start < 0)
                {
                    start = content.IndexOf("[mcp_servers.ember]");
                    if (start < 0) return;
                    int end = content.IndexOf("[", start + 1);
                    if (end < 0) end = content.Length;
                    content = content.Remove(start, end - start);
                }
                else
                {
                    // JSON: remove the ember key and its value
                    int braceDepth = 0;
                    int end = start;
                    for (int i = start; i < content.Length; i++)
                    {
                        if (content[i] == '{') braceDepth++;
                        if (content[i] == '}') braceDepth--;
                        if (braceDepth == 0)
                        {
                            end = i + 1;
                            break;
                        }
                    }

                    // Backtrack to remove comma
                    int commaBefore = content.LastIndexOf(',', start);
                    content = commaBefore >= 0
                        ? content.Remove(commaBefore, end - commaBefore)
                        : content.Remove(start, end - start);
                }

                File.Copy(configPath, configPath + ".bak", true);
                File.WriteAllText(configPath, content);

                EditorUtility.DisplayDialog("Ember MCP", "Uninstalled successfully.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Uninstall Failed", ex.Message, "OK");
            }
        }

        // ── Server Info ──

        private void DrawServerInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("MCP Server", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Protocol", "JSON-lines over TCP");
            EditorGUILayout.LabelField("Server Path", s_ServerPath);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Mode", "Read + Write");

            var manager = ECSManager.Active;
            bool inPlayMode = EditorApplication.isPlaying;
            GUI.color = inPlayMode ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(inPlayMode ? "(Play Mode — write enabled)" : "(Edit Mode — read only)",
                EditorStyles.miniLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── Request Log ──

        private void DrawRequestLog()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            m_LogScroll = EditorGUILayout.BeginScrollView(m_LogScroll, GUILayout.Height(200));

            for (int i = m_Log.Count - 1; i >= 0; i--)
            {
                var entry = m_Log[i];
                EditorGUILayout.BeginHorizontal();

                Color bg = entry.TimeMs > 100 ? new Color(0.5f, 0.2f, 0.2f) :
                    (entry.TimeMs > 10 ? new Color(0.4f, 0.4f, 0.1f) : Color.clear);
                var origBg = GUI.backgroundColor;
                if (bg != Color.clear) GUI.backgroundColor = bg;

                EditorGUILayout.LabelField(entry.Timestamp, GUILayout.Width(70));
                EditorGUILayout.LabelField(entry.Method, EditorStyles.boldLabel, GUILayout.Width(200));
                EditorGUILayout.LabelField($"{entry.TimeMs:F1}ms", GUILayout.Width(50));
                EditorGUILayout.LabelField(entry.Response, EditorStyles.wordWrappedMiniLabel);

                GUI.backgroundColor = origBg;
                EditorGUILayout.EndHorizontal();
            }

            if (m_Log.Count == 0)
                EditorGUILayout.LabelField("No requests yet.", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
                m_Log.Clear();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── Settings ──

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Port Range", GUILayout.Width(80));
            EditorGUILayout.LabelField("9090", GUILayout.Width(40));
            EditorGUILayout.LabelField("—", GUILayout.Width(15));
            EditorGUILayout.LabelField("9099", GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
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

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// Ember MCP Bridge — Unity 侧 TCP 服务端。
    /// 后台线程监听 TCP 连接，接收 JSON 命令，主线程执行后返回结果。
    /// </summary>
    [InitializeOnLoad]
    public static class EmberBridge
    {
        private const int PortStart = 9090;
        private const int PortEnd = 9099;
        private const string InstanceFile = "~/.ember/instance.json";
        private const string PrefKeyAutoStart = "Ember.MCP.AutoStart";

        private static TcpListener s_Listener;
        private static Thread s_Thread;
        private static bool s_Running;
        private static TcpClient s_Client;
        private static StreamReader s_Reader;
        private static StreamWriter s_Writer;
        private static readonly object s_WriteLock = new();

        internal static int ActivePort { get; private set; } = -1;

        internal static readonly string ProjectRoot = Path.GetFullPath(
            Path.Combine(Application.dataPath, ".."));
        internal static bool IsRunning => s_Running;
        internal static bool IsConnected => s_Client?.Connected ?? false;

        private static readonly Lazy<string> s_RelativeServerPath = new(() =>
        {
            var pkgPath = Path.Combine(ProjectRoot, "Packages", "com.ember.ecs");
            if (!Directory.Exists(pkgPath))
            {
                var cacheDir = Path.Combine(ProjectRoot, "Library", "PackageCache");
                if (Directory.Exists(cacheDir))
                    foreach (var d in Directory.GetDirectories(cacheDir, "com.ember.ecs@*"))
                    { pkgPath = d; break; }
            }
            return Path.Combine(
                Path.GetRelativePath(ProjectRoot, pkgPath),
                "Tools~", "Ember.Mcp.Server.dll");
        });

        internal static string RelativeServerPath => s_RelativeServerPath.Value;
        internal static int RequestCount { get; private set; }
        internal static string LastRequest { get; private set; }
        internal static string LastResponse { get; private set; }
        internal static double LastRequestMs { get; private set; }

        /// <summary>
        /// Unity 启动时是否自动启动 Bridge。
        /// </summary>
        internal static bool AutoStart
        {
            get => EditorPrefs.GetBool(PrefKeyAutoStart, true);
            set => EditorPrefs.SetBool(PrefKeyAutoStart, value);
        }

        // Thread-safe queue: background thread → main thread
        private static readonly ConcurrentQueue<PendingRequest> s_Queue = new();

        public struct PendingRequest
        {
            public string Id;
            public string Method;
            public string Params; // raw JSON string (or null)
        }

        static EmberBridge()
        {
            if (AutoStart)
                Start();
        }

        public static void Start()
        {
            ActivePort = -1;
            for (int port = PortStart; port <= PortEnd; port++)
            {
                try
                {
                    s_Listener = new TcpListener(IPAddress.Loopback, port);
                    s_Listener.Start();
                    ActivePort = port;
                    break;
                }
                catch { }
            }

            if (ActivePort < 0)
            {
                Debug.LogWarning($"[Ember MCP] No available port in range {PortStart}-{PortEnd}.");
                return;
            }

            s_Running = true;
            s_Thread = new Thread(ListenLoop) { IsBackground = true, Name = "EmberBridge" };
            s_Thread.Start();
            EditorApplication.update += ProcessQueue;
            WriteInstanceFile();
            AutoUpdateConfigPaths(silent: true);
            Debug.Log($"[Ember MCP] Bridge started on port {ActivePort}");
        }

        private static void ListenLoop()
        {
            while (s_Running)
            {
                try
                {
                    s_Client = s_Listener.AcceptTcpClient();
                    var stream = s_Client.GetStream();
                    s_Reader = new StreamReader(stream, Encoding.UTF8);
                    s_Writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                    Debug.Log("[Ember MCP] Client connected");
                    while (s_Running)
                    {
                        var line = s_Reader.ReadLine();
                        if (line == null || line.Length == 0) break;

                        Debug.Log($"[Ember MCP] ← received: {line.Substring(0, Math.Min(line.Length, 100))}");

                        var id = SimpleJson.GetString(line, "id");
                        var method = SimpleJson.GetString(line, "method");
                        var args = SimpleJson.GetObject(line, "params");
                        if (method == null) continue;

                        Debug.Log($"[Ember MCP] Enqueuing: method={method} id={id}");
                        s_Queue.Enqueue(new PendingRequest
                        {
                            Id = id,
                            Method = method,
                            Params = args
                        });
                    }
                    Debug.Log("[Ember MCP] Client disconnected (ReadLine returned null/empty)");
                }
                catch (ThreadAbortException) { break; }
                catch (IOException)
                {
                    Debug.Log("[Ember MCP] Client disconnected (IO)");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Ember MCP] Listener error: {ex.Message}");
                    break;
                }
                finally { CloseClient(); }
            }
        }

        private static void ProcessQueue()
        {
            while (s_Queue.TryDequeue(out var pending))
            {
                Debug.Log($"[Ember MCP] Processing: method={pending.Method} id={pending.Id}");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string result = null;
                string error = null;

                try
                {
                    result = EmberBridgeCommands.Execute(pending.Method, pending.Params);
                    LastRequest = pending.Method;
                    LastResponse = result?.Length > 200 ? result.Substring(0, 200) : result;
                    Debug.Log($"[Ember MCP] Execute OK: {pending.Method}, result len={result?.Length ?? 0}");
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    LastResponse = $"Error: {error}";
                    Debug.LogWarning($"[Ember MCP] Execute error: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    sw.Stop();
                    LastRequestMs = sw.Elapsed.TotalMilliseconds;
                    RequestCount++;
                }

                Debug.Log($"[Ember MCP] Sending response for {pending.Id}");
                SendResponse(pending.Id, result, error);
                Debug.Log($"[Ember MCP] Response sent for {pending.Id}");
            }
        }

        private static void SendResponse(string id, string result, string error)
        {
            lock (s_WriteLock)
            {
                if (s_Writer == null)
                {
                    Debug.LogWarning($"[Ember MCP] Cannot send response — s_Writer is null (connection may have dropped)");
                    return;
                }
                try
                {
                    var resp = SimpleJson.BuildJson(
                        ("id", $"\"{id ?? "null"}\""),
                        ("result", result ?? "null"),
                        ("error", error != null ? $"\"{Escape(error)}\"" : "null")
                    );
                    s_Writer.WriteLine(resp);
                }
                catch { }
            }
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void CloseClient()
        {
            s_Reader?.Dispose();
            s_Writer?.Dispose();
            s_Client?.Dispose();
            s_Reader = null;
            s_Writer = null;
            s_Client = null;
        }

        public static void Stop()
        {
            s_Running = false;
            EditorApplication.update -= ProcessQueue;
            CloseClient();
            s_Listener?.Stop();
            s_Thread?.Join(1000);
            ActivePort = -1;
        }

        private static void WriteInstanceFile()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ember");
                Directory.CreateDirectory(dir);
                var path = InstanceFile.Replace("~", dir);
                var instJson = SimpleJson.BuildJson(
                    ("project", $"\"{Application.productName}\""),
                    ("port", ActivePort.ToString()),
                    ("pid", System.Diagnostics.Process.GetCurrentProcess().Id.ToString()));
                var json = SimpleJson.BuildJson(
                    ("active", $"\"127.0.0.1:{ActivePort}\""),
                    ("instances", $"[{instJson}]"));
                File.WriteAllText(path, json);
            }
            catch { /* non-critical */ }
        }

        private static void DeleteInstanceFile()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ember");
                var path = InstanceFile.Replace("~", dir);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* non-critical */ }
        }

        // ── Config Management ──

        /// <summary>
        /// 检查 AI 客户端配置文件中的 Server DLL 路径是否过期，过期则自动修复。
        /// silent=true 时只记录日志不弹对话框（Bridge 自动启动场景）。
        /// </summary>
        internal static void AutoUpdateConfigPaths(bool silent = false)
        {
            string ccConfig = Path.Combine(ProjectRoot, ".mcp.json");
            string codexConfig = Path.Combine(ProjectRoot, ".codex", "config.toml");

            TryAutoFixConfig(ccConfig, "Claude Code", silent);
            TryAutoFixConfig(codexConfig, "Codex", silent);
        }

        private static void TryAutoFixConfig(string configPath, string clientName, bool silent)
        {
            if (!File.Exists(configPath) || !ConfigHasEmber(configPath))
                return;

            string content = File.ReadAllText(configPath);
            if (content.Contains(RelativeServerPath))
                return; // 路径已最新，无需修复

            string updated = PatchEmberDllPath(content, RelativeServerPath);
            if (updated == content)
                return; // 未能定位到旧路径，放弃静默修复

            Debug.Log($"[Ember MCP] Auto-updating {Path.GetFileName(configPath)} path to {RelativeServerPath}");
            try
            {
                File.Copy(configPath, configPath + ".bak", true);
                File.WriteAllText(configPath, updated);
                if (!silent)
                    EditorUtility.DisplayDialog("Ember MCP",
                        $"Updated {Path.GetFileName(configPath)} path.\n" +
                        $"Restart {clientName} to apply.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ember MCP] Auto-update failed for {configPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 在配置文本中精确替换 Ember.Mcp.Server.dll 的路径字符串。
        /// 定位到 "Ember.Mcp.Server.dll" → 向两侧扩展到引号 → 替换引号内的路径。
        /// </summary>
        private static string PatchEmberDllPath(string content, string newPath)
        {
            const string dllName = "Ember.Mcp.Server.dll";
            int dllIdx = content.IndexOf(dllName, StringComparison.Ordinal);
            if (dllIdx < 0) return content;

            // 向左侧找到路径的起始引号
            int openQuote = content.LastIndexOf('"', dllIdx);
            if (openQuote < 0) return content;

            // 向右侧找到路径的结束引号
            int closeQuote = content.IndexOf('"', dllIdx + dllName.Length);
            if (closeQuote < 0) return content;

            // 替换引号之间的完整旧路径
            return content.Substring(0, openQuote + 1) + newPath + content.Substring(closeQuote);
        }

        private static string FindDotNet()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates;

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                candidates = new[]
                {
                    @"C:\Program Files\dotnet\dotnet.exe",
                    @"C:\Program Files (x86)\dotnet\dotnet.exe",
                };
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                candidates = new[]
                {
                    Path.Combine(home, ".dotnet", "dotnet"),
                    "/usr/local/share/dotnet/dotnet",
                    "/usr/local/bin/dotnet",
                    "/opt/homebrew/bin/dotnet",
                };
            }
            else
            {
                candidates = new[]
                {
                    "/usr/share/dotnet/dotnet",
                    "/usr/local/bin/dotnet",
                    Path.Combine(home, ".dotnet", "dotnet"),
                };
            }

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;

            try
            {
                var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Application.platform == RuntimePlatform.WindowsEditor ? "where" : "which",
                        Arguments = "dotnet",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                var result = proc.StandardOutput.ReadToEnd().Trim().Split('\n')[0].Trim();
                proc.WaitForExit(3000);
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                    return result;
            }
            catch { }

            return "dotnet";
        }

        internal static bool ConfigHasEmber(string configPath)
        {
            try
            {
                string content = File.ReadAllText(configPath);
                if (configPath.EndsWith(".json"))
                {
                    var servers = SimpleJson.GetObject(content, "mcpServers");
                    return servers != null && SimpleJson.HasKey(servers, "ember");
                }
                if (configPath.EndsWith(".toml"))
                    return content.Contains("[mcp_servers.ember]") || content.Contains("\"ember\"");
                return false;
            }
            catch { return false; }
        }

        internal static void InstallEmberToConfig(string configPath, string clientName, bool silent = false)
        {
            bool isToml = configPath.EndsWith(".toml");
            string dotnetPath = FindDotNet();
            string emberBlock = isToml
                ? $"[mcp_servers.ember]\ncommand = \"{dotnetPath}\"\nargs = [\"exec\", \"{RelativeServerPath}\"]\n"
                : $"    \"ember\": {{\n" +
                  $"      \"command\": \"{dotnetPath}\",\n" +
                  $"      \"args\": [\"exec\", \"{RelativeServerPath}\"]\n" +
                  "    }";

            if (File.Exists(configPath))
            {
                string existing = File.ReadAllText(configPath);
                if (ConfigHasEmber(configPath))
                {
                    // 配置文件已有 ember 但路径过期 → 替换旧路径
                    // 简单策略：删除旧 ember 条目后重新插入
                    var removed = RemoveEmberFromConfigInternal(configPath, existing, isToml);
                    File.Copy(configPath, configPath + ".bak", true);
                    if (!removed.Contains("\"mcpServers\"") && !removed.Contains("[mcp_servers."))
                    {
                        // 删除后配置中没有 mcpServers 了，重新创建
                        File.WriteAllText(configPath, isToml
                            ? emberBlock
                            : $"{{\n  \"mcpServers\": {{\n{emberBlock}\n  }}\n}}\n");
                    }
                    else if (isToml)
                    {
                        File.WriteAllText(configPath, removed.TrimEnd() + "\n" + emberBlock);
                    }
                    else
                    {
                        int serversIdx = removed.IndexOf("\"mcpServers\"");
                        int braceOpen = removed.IndexOf('{', serversIdx);
                        int depth = 1;
                        int i = braceOpen + 1;
                        while (i < removed.Length && depth > 0)
                        {
                            if (removed[i] == '{') depth++;
                            else if (removed[i] == '}') depth--;
                            i++;
                        }
                        int insertPos = i - 1;
                        bool isEmpty = insertPos == braceOpen + 1 ||
                                       removed.Substring(braceOpen + 1, insertPos - braceOpen - 1).Trim().Length == 0;
                        string prefix = isEmpty ? "" : ",";
                        string merged = removed.Insert(insertPos,
                            $"{prefix}\n{emberBlock}\n  ");
                        File.WriteAllText(configPath, merged);
                    }
                    if (!silent) EditorUtility.DisplayDialog("Ember MCP",
                        $"Updated to {Path.GetFileName(configPath)}\n" +
                        $"Path: {RelativeServerPath}\n" +
                        $"Restart {clientName} for changes to take effect.", "OK");
                    return;
                }

                File.Copy(configPath, configPath + ".bak", true);

                if (isToml)
                {
                    File.AppendAllText(configPath, "\n" + emberBlock);
                }
                else
                {
                    int serversIdx = existing.IndexOf("\"mcpServers\"");
                    if (serversIdx >= 0)
                    {
                        int braceOpen = existing.IndexOf('{', serversIdx);
                        int depth = 1;
                        int i = braceOpen + 1;
                        while (i < existing.Length && depth > 0)
                        {
                            if (existing[i] == '{') depth++;
                            else if (existing[i] == '}') depth--;
                            i++;
                        }
                        int insertPos = i - 1;
                        bool isEmpty = insertPos == braceOpen + 1 ||
                                       existing.Substring(braceOpen + 1, insertPos - braceOpen - 1).Trim().Length == 0;
                        string prefix = isEmpty ? "" : ",";
                        string merged = existing.Insert(insertPos,
                            $"{prefix}\n{emberBlock}\n  ");
                        File.WriteAllText(configPath, merged);
                    }
                    else
                    {
                        int lastBrace = existing.LastIndexOf('}');
                        int firstBrace = existing.IndexOf('{');
                        bool isEmpty = firstBrace >= 0 && lastBrace > firstBrace &&
                                       existing.Substring(firstBrace + 1, lastBrace - firstBrace - 1).Trim().Length == 0;
                        string prefix = isEmpty ? "" : ",";
                        string merged = existing.Insert(lastBrace,
                            $"{prefix}\n  \"mcpServers\": {{\n{emberBlock}\n  }}");
                        File.WriteAllText(configPath, merged);
                    }
                }
            }
            else
            {
                string dir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string newConfig = isToml
                    ? emberBlock
                    : $"{{\n  \"mcpServers\": {{\n{emberBlock}\n  }}\n}}\n";
                File.WriteAllText(configPath, newConfig);
            }

            if (!silent)
            {
                EditorUtility.DisplayDialog("Ember MCP",
                    $"Installed to {configPath}\n" +
                    $"Relative path: {RelativeServerPath}\n" +
                    $"Restart {clientName} for changes to take effect.", "OK");
            }
        }

        internal static void RemoveEmberFromConfig(string configPath, string clientName)
        {
            try
            {
                string content = File.ReadAllText(configPath);
                File.Copy(configPath, configPath + ".bak", true);
                string result = RemoveEmberFromConfigInternal(configPath, content,
                    configPath.EndsWith(".toml"));
                File.WriteAllText(configPath, result);
                EditorUtility.DisplayDialog("Ember MCP",
                    $"Removed from {Path.GetFileName(configPath)}.\n" +
                    $"Restart {clientName} for changes to take effect.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Uninstall Failed", ex.Message, "OK");
            }
        }

        private static string RemoveEmberFromConfigInternal(string configPath, string content, bool isToml)
        {
            if (isToml)
            {
                int start = content.IndexOf("[mcp_servers.ember]");
                if (start < 0) return content;
                int end = content.IndexOf('[', start + 1);
                if (end < 0) end = content.Length;
                return content.Remove(start, end - start).TrimEnd();
            }
            else
            {
                int serversIdx = content.IndexOf("\"mcpServers\"");
                if (serversIdx < 0) return content;
                int braceStart = content.IndexOf('{', serversIdx);
                if (braceStart < 0) return content;
                int depth = 0;
                int braceEnd = braceStart;
                for (int i = braceStart; i < content.Length; i++)
                {
                    if (content[i] == '{') depth++;
                    else if (content[i] == '}') depth--;
                    if (depth == 0) { braceEnd = i + 1; break; }
                }
                int comma = content.LastIndexOf(',', serversIdx);
                if (comma >= 0 && comma > serversIdx - 10)
                    return content.Remove(comma, braceEnd - comma);
                else
                    return content.Remove(serversIdx, braceEnd - serversIdx);
            }
        }
    }
}

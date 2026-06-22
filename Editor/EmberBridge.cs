using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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

        private static TcpListener s_Listener;
        private static Thread s_Thread;
        private static bool s_Running;
        private static TcpClient s_Client;
        private static StreamReader s_Reader;
        private static StreamWriter s_Writer;
        private static readonly object s_WriteLock = new();

        internal static int ActivePort { get; private set; } = -1;
        internal static bool IsConnected => s_Client?.Connected ?? false;
        internal static int RequestCount { get; private set; }
        internal static string LastRequest { get; private set; }
        internal static string LastResponse { get; private set; }
        internal static double LastRequestMs { get; private set; }

        // Thread-safe queue: background thread → main thread
        private static readonly ConcurrentQueue<PendingRequest> s_Queue = new();
        private static readonly JsonSerializerOptions s_JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public struct PendingRequest
        {
            public string Id;
            public string Method;
            public JsonElement? Params;
        }

        static EmberBridge()
        {
            Start();
        }

        private static void Start()
        {
            // Find available port
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

            // Register main thread callback
            EditorApplication.update += ProcessQueue;

            // Write instance file
            WriteInstanceFile();

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

                    while (s_Running && s_Client.Connected)
                    {
                        var line = s_Reader.ReadLine();
                        if (line == null) break;

                        var request = JsonSerializer.Deserialize<BridgeRequest>(line, s_JsonOpts);
                        if (request == null) continue;

                        s_Queue.Enqueue(new PendingRequest
                        {
                            Id = request.Id,
                            Method = request.Method,
                            Params = request.Params
                        });
                    }
                }
                catch (ThreadAbortException) { break; }
                catch (IOException) { /* client disconnected */ }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Ember MCP] Listener error: {ex.Message}");
                }
                finally
                {
                    CloseClient();
                }
            }
        }

        private static void ProcessQueue()
        {
            while (s_Queue.TryDequeue(out var pending))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string result = null;
                string error = null;

                try
                {
                    result = EmberBridgeCommands.Execute(pending.Method, pending.Params);
                    LastRequest = pending.Method;
                    LastResponse = result?.Substring(0, Math.Min(result.Length, 200));
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    LastResponse = $"Error: {error}";
                }
                finally
                {
                    sw.Stop();
                    LastRequestMs = sw.Elapsed.TotalMilliseconds;
                    RequestCount++;
                }

                SendResponse(pending.Id, result, error);
            }
        }

        private static void SendResponse(string id, string result, string error)
        {
            lock (s_WriteLock)
            {
                if (s_Writer == null) return;
                try
                {
                    var resp = new BridgeResponse
                    {
                        Id = id,
                        Result = result != null ? JsonSerializer.Deserialize<JsonElement>(result) : null,
                        Error = error
                    };
                    s_Writer.WriteLine(JsonSerializer.Serialize(resp, s_JsonOpts));
                }
                catch { }
            }
        }

        private static void CloseClient()
        {
            s_Reader?.Dispose();
            s_Writer?.Dispose();
            s_Client?.Dispose();
            s_Reader = null;
            s_Writer = null;
            s_Client = null;
        }

        internal static void Stop()
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
                var json = JsonSerializer.Serialize(new
                {
                    active = $"127.0.0.1:{ActivePort}",
                    instances = new[]
                    {
                        new
                        {
                            project = Application.productName,
                            port = ActivePort,
                            pid = System.Diagnostics.Process.GetCurrentProcess().Id
                        }
                    }
                });
                File.WriteAllText(path, json);
            }
            catch { /* non-critical */ }
        }

        // Deserialization types
        private struct BridgeRequest
        {
            public string Id { get; set; }
            public string Method { get; set; }
            public JsonElement? Params { get; set; }
        }

        private struct BridgeResponse
        {
            public string Id { get; set; }
            public JsonElement? Result { get; set; }
            public string Error { get; set; }
        }
    }
}

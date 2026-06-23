using System;
using System.Collections.Generic;
using System.Text;

namespace Ember.Editor
{
    /// <summary>
    /// 极简 JSON 工具 — 不依赖 System.Text.Json（Unity 2022.3 中部分类型为 internal）。
    /// 仅供 Ember Editor 脚本内部使用。
    /// </summary>
    internal static class SimpleJson
    {
        /// <summary>从 "{\"k\":\"v\"}" 中提取字符串字段。未找到返回 null。</summary>
        public static string GetString(string json, string key)
        {
            var idx = FindKey(json, key);
            if (idx < 0) return null;
            // Skip past the key to the colon, then find the value
            int p = idx + key.Length + 2; // skip "key"
            while (p < json.Length && (json[p] == ' ' || json[p] == ':' || json[p] == '\t' || json[p] == '\r' || json[p] == '\n')) p++;
            if (p >= json.Length || json[p] != '"') return null;
            var start = p;
            var end = json.IndexOf('"', start + 1);
            if (end < 0) return null;
            return json.Substring(start + 1, end - start - 1);
        }

        /// <summary>提取整数字段。未找到返回 defaultValue。</summary>
        public static int GetInt(string json, string key, int defaultValue = 0)
        {
            var idx = FindKey(json, key);
            if (idx < 0) return defaultValue;
            // skip whitespace and colon
            int p = idx + key.Length + 2;
            while (p < json.Length && (json[p] == ' ' || json[p] == ':' || json[p] == '"')) p++;
            var neg = false;
            if (p < json.Length && json[p] == '-') { neg = true; p++; }
            int val = 0;
            while (p < json.Length && json[p] >= '0' && json[p] <= '9')
            {
                val = val * 10 + (json[p] - '0');
                p++;
            }
            return neg ? -val : val;
        }

        /// <summary>提取嵌套对象的原始 JSON 文本。未找到返回 null。</summary>
        public static string GetObject(string json, string key)
        {
            var idx = FindKey(json, key);
            if (idx < 0) return null;
            var start = json.IndexOf('{', idx + key.Length);
            if (start < 0) return null;
            return ExtractBrace(json, start);
        }

        /// <summary>提取字符串数组。未找到返回空数组。</summary>
        public static string[] GetStringArray(string json, string key)
        {
            var idx = FindKey(json, key);
            if (idx < 0) return Array.Empty<string>();
            var start = json.IndexOf('[', idx + key.Length);
            if (start < 0) return Array.Empty<string>();
            var end = json.IndexOf(']', start);
            if (end < 0) return Array.Empty<string>();
            var content = json.Substring(start + 1, end - start - 1);
            var parts = content.Split(',');
            var result = new List<string>();
            foreach (var p in parts)
            {
                var trimmed = p.Trim().Trim('"');
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result.ToArray();
        }

        /// <summary>检查 JSON 对象中是否有指定 key。</summary>
        public static bool HasKey(string json, string key)
        {
            return FindKey(json, key) >= 0;
        }

        private static int FindKey(string json, string key)
        {
            var search = $"\"{key}\"";
            return json.IndexOf(search, StringComparison.Ordinal);
        }

        private static string ExtractBrace(string json, int start)
        {
            // start points at '{'
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;
                if (depth == 0)
                    return json.Substring(start, i - start + 1);
            }
            return null;
        }

        /// <summary>构建简单 JSON 对象字符串。</summary>
        public static string BuildJson(params (string key, string value)[] fields)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                sb.Append(fields[i].key);
                sb.Append("\":");
                // If value starts with { or [ or is a number/bool, don't quote
                var v = fields[i].value;
                if (v == "null") sb.Append("null");
                else if (v == "true" || v == "false") sb.Append(v);
                else if (v.Length > 0 && (v[0] == '{' || v[0] == '[' || char.IsDigit(v[0]) || v[0] == '-'))
                    sb.Append(v);
                else
                {
                    sb.Append('"');
                    sb.Append(v.Replace("\\", "\\\\").Replace("\"", "\\\""));
                    sb.Append('"');
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>用 ToJson 序列化对象。</summary>
        public static string Serialize(object obj)
        {
            return UnityEngine.JsonUtility.ToJson(obj);
        }
    }
}

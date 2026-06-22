using System;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// 原型窗口 — 查看 Archetype 分布、Chunk 填充率、内存布局。
    /// </summary>
    public class ArchetypesWindow : EditorWindow
    {
        private Vector2 m_ScrollPos;
        private int m_SelectedIndex = -1;
        private WorldDebugView m_CachedView;
        private double m_LastRefreshTime;

        public static void Open()
        {
            var window = GetWindow<ArchetypesWindow>("Ember Archetypes");
            window.minSize = new Vector2(550, 400);
            window.Show();
        }

        private void OnGUI()
        {
            var manager = ECSManager.Active;
            if (manager?.World == null)
            {
                EditorGUILayout.HelpBox(
                    "No active ECSManager found.\nSet ECSManager.Active = yourManager in your game code.",
                    MessageType.Info);
                return;
            }

            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - m_LastRefreshTime > 1.0)
                Refresh(manager);

            if (!EditorApplication.isPlaying && m_CachedView.Archetypes == null)
                Refresh(manager);

            DrawSummaryBar();
            EditorGUILayout.Space();
            DrawArchetypeTable(manager.World);
            EditorGUILayout.Space();
            DrawChunkHistogram();
            EditorGUILayout.Space();
            DrawSelectedDetail(manager.World);
        }

        private void Refresh(ECSManager manager)
        {
            m_CachedView = manager.World.Debug;
            m_LastRefreshTime = EditorApplication.timeSinceStartup;
        }

        private void DrawSummaryBar()
        {
            if (m_CachedView.Archetypes == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            // Fill rate bar
            Rect barRect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
            EditorGUI.ProgressBar(barRect, m_CachedView.GlobalFillRate,
                $"Fill: {m_CachedView.AliveEntityCount}/{m_CachedView.TotalEntityCapacity}");

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                $"{m_CachedView.ArchetypeCount} archetypes · {m_CachedView.ChunkCount} chunks · " +
                $"{m_CachedView.AliveEntityCount}/{m_CachedView.TotalEntityCapacity} entities",
                EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawArchetypeTable(World world)
        {
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Archetype", EditorStyles.toolbarButton);
            EditorGUILayout.LabelField("Types", EditorStyles.toolbarButton, GUILayout.Width(50));
            EditorGUILayout.LabelField("Chunks", EditorStyles.toolbarButton, GUILayout.Width(60));
            EditorGUILayout.LabelField("Entities", EditorStyles.toolbarButton, GUILayout.Width(70));
            EditorGUILayout.LabelField("Fill", EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            if (m_CachedView.Archetypes != null)
            {
                for (int i = 0; i < m_CachedView.Archetypes.Length; i++)
                {
                    var arch = m_CachedView.Archetypes[i];
                    bool selected = i == m_SelectedIndex;

                    var bgColor = GUI.backgroundColor;
                    if (selected)
                        GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);
                    else if (arch.FillRate < 0.25f)
                        GUI.backgroundColor = new Color(0.6f, 0.3f, 0.3f);

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                    if (GUILayout.Button(string.Join(", ", arch.ComponentNames), EditorStyles.label))
                        m_SelectedIndex = i;

                    EditorGUILayout.LabelField(arch.ComponentTypeCount.ToString(), GUILayout.Width(50));
                    EditorGUILayout.LabelField(arch.ChunkCount.ToString(), GUILayout.Width(60));
                    EditorGUILayout.LabelField(arch.TotalEntities.ToString(), GUILayout.Width(70));

                    // Fill bar
                    Rect fillRect = EditorGUILayout.GetControlRect(GUILayout.Width(80), GUILayout.Height(16));
                    EditorGUI.ProgressBar(fillRect, arch.FillRate, $"{arch.FillRate * 100:F0}%");

                    EditorGUILayout.EndHorizontal();
                    GUI.backgroundColor = bgColor;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawChunkHistogram()
        {
            if (m_CachedView.Archetypes == null) return;

            EditorGUILayout.LabelField("Chunk Fill Histogram", EditorStyles.boldLabel);

            // Bucket chunks by fill rate
            var buckets = new int[5]; // 100%, 75%+, 50%+, 25%+, <25%
            foreach (var arch in m_CachedView.Archetypes)
            {
                foreach (var chunk in arch.Chunks)
                {
                    float rate = chunk.FillRate;
                    if (rate >= 1f) buckets[0]++;
                    else if (rate >= 0.75f) buckets[1]++;
                    else if (rate >= 0.5f) buckets[2]++;
                    else if (rate >= 0.25f) buckets[3]++;
                    else buckets[4]++;
                }
            }

            string[] labels = { "100%", "75%+", "50%+", "25%+", "<25%" };
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] == 0) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(labels[i], GUILayout.Width(40));

                Rect barRect = EditorGUILayout.GetControlRect(GUILayout.Height(16));
                float maxVal = Mathf.Max(1, buckets[0]); // normalize to max
                EditorGUI.ProgressBar(barRect, (float)buckets[i] / maxVal,
                    $"{buckets[i]} chunks");

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSelectedDetail(World world)
        {
            if (m_SelectedIndex < 0 || m_CachedView.Archetypes == null ||
                m_SelectedIndex >= m_CachedView.Archetypes.Length)
                return;

            var arch = m_CachedView.Archetypes[m_SelectedIndex];

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField($"Archetype #{m_SelectedIndex} Detail", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Components", string.Join(", ", arch.ComponentNames));
            EditorGUILayout.LabelField("Type Count", arch.ComponentTypeCount.ToString());
            EditorGUILayout.LabelField("Chunks", arch.ChunkCount.ToString());
            EditorGUILayout.LabelField("Total Entities", arch.TotalEntities.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-Chunk Breakdown", EditorStyles.miniBoldLabel);

            int maxCount = 0;
            foreach (var c in arch.Chunks)
                if (c.Count > maxCount) maxCount = c.Count;

            for (int i = 0; i < arch.Chunks.Length; i++)
            {
                var chunk = arch.Chunks[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Chunk #{i}", GUILayout.Width(70));
                Rect barRect = EditorGUILayout.GetControlRect(GUILayout.Height(14));
                float max = Mathf.Max(1, maxCount);
                EditorGUI.ProgressBar(barRect, (float)chunk.Count / max,
                    $"{chunk.Count} / {chunk.Capacity}  ({chunk.FillRate * 100:F0}%)");
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }
    }
}

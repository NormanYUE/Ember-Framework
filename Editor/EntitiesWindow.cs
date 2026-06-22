using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// 实体窗口 — 浏览所有实体，按组件过滤，分页显示。
    /// </summary>
    public class EntitiesWindow : EditorWindow
    {
        private const int PageSize = 200;

        private string m_FilterText = "";
        private int m_Page;
        private Vector2 m_ScrollPos;
        private int m_SelectedIndex = -1;
        private WorldDebugView m_CachedView;
        private double m_LastRefreshTime;
        private readonly List<EntityDebugView> m_FilteredEntities = new();

        public static void Open()
        {
            var window = GetWindow<EntitiesWindow>("Ember Entities");
            window.minSize = new Vector2(500, 400);
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

            // Refresh cache every second
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - m_LastRefreshTime > 1.0)
                Refresh(manager);

            if (!EditorApplication.isPlaying && m_CachedView.AliveEntities == null)
                Refresh(manager);

            DrawToolbar(manager);
            EditorGUILayout.Space();
            DrawEntityTable();
            EditorGUILayout.Space();
            DrawPagination();
        }

        private void Refresh(ECSManager manager)
        {
            m_CachedView = manager.World.Debug;
            m_LastRefreshTime = EditorApplication.timeSinceStartup;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            m_FilteredEntities.Clear();
            if (m_CachedView.AliveEntities == null) return;

            foreach (var entity in m_CachedView.AliveEntities)
            {
                if (MatchesFilter(entity))
                    m_FilteredEntities.Add(entity);
            }

            // Clamp page
            int maxPage = Mathf.Max(0, (m_FilteredEntities.Count - 1) / PageSize);
            if (m_Page > maxPage) m_Page = maxPage;
        }

        private bool MatchesFilter(EntityDebugView entity)
        {
            if (string.IsNullOrWhiteSpace(m_FilterText)) return true;

            var filterParts = m_FilterText.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (filterParts.Length == 0) return true;

            foreach (var part in filterParts)
            {
                bool found = false;
                foreach (var compName in entity.ComponentNames)
                {
                    if (compName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found) return false;
            }

            return true;
        }

        private void DrawToolbar(ECSManager manager)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Filter
            EditorGUI.BeginChangeCheck();
            m_FilterText = EditorGUILayout.TextField("Filter", m_FilterText, EditorStyles.toolbarTextField,
                GUILayout.MinWidth(150));
            if (EditorGUI.EndChangeCheck())
            {
                m_Page = 0;
                ApplyFilter();
            }

            GUILayout.FlexibleSpace();

            // Refresh button
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Refresh(manager);

            // Entity count
            int total = m_CachedView.AliveEntities?.Length ?? 0;
            GUILayout.Label($"{total} alive", EditorStyles.toolbarButton);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntityTable()
        {
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Entity", EditorStyles.toolbarButton, GUILayout.Width(120));
            EditorGUILayout.LabelField("Archetype", EditorStyles.toolbarButton);
            EditorGUILayout.LabelField("Components", EditorStyles.toolbarButton);
            EditorGUILayout.LabelField("Placed", EditorStyles.toolbarButton, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            int start = m_Page * PageSize;
            int end = Mathf.Min(start + PageSize, m_FilteredEntities.Count);

            for (int i = start; i < end; i++)
            {
                var entity = m_FilteredEntities[i];
                bool selected = i == m_SelectedIndex;

                var bgColor = GUI.backgroundColor;
                if (selected)
                    GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);
                else if (!entity.Placed)
                    GUI.backgroundColor = new Color(0.6f, 0.5f, 0.1f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                if (GUILayout.Button($"E({entity.Index}, v{entity.Version})",
                        EditorStyles.label, GUILayout.Width(120)))
                {
                    m_SelectedIndex = i;
                    NotifySelectionChanged();
                }

                EditorGUILayout.LabelField($"Arch #{entity.ArchetypeIndex}", GUILayout.Width(80));
                EditorGUILayout.LabelField(string.Join(", ", entity.ComponentNames));
                EditorGUILayout.LabelField(entity.Placed ? "✓" : "pending", GUILayout.Width(50));

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = bgColor;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPagination()
        {
            EditorGUILayout.BeginHorizontal();
            int totalPages = Mathf.Max(1, (m_FilteredEntities.Count + PageSize - 1) / PageSize);

            if (GUILayout.Button("◀◀", GUILayout.Width(40)))
                m_Page = 0;
            if (GUILayout.Button("◀", GUILayout.Width(30)) && m_Page > 0)
                m_Page--;

            EditorGUILayout.LabelField($"Page {m_Page + 1} / {totalPages}", EditorStyles.centeredGreyMiniLabel);

            if (GUILayout.Button("▶", GUILayout.Width(30)) && m_Page < totalPages - 1)
                m_Page++;
            if (GUILayout.Button("▶▶", GUILayout.Width(40)))
                m_Page = totalPages - 1;

            EditorGUILayout.LabelField(
                $"Showing {Mathf.Min(m_FilteredEntities.Count - m_Page * PageSize, PageSize)} of {m_FilteredEntities.Count} entities",
                EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void NotifySelectionChanged()
        {
            if (m_SelectedIndex < 0 || m_SelectedIndex >= m_FilteredEntities.Count)
                return;

            var manager = ECSManager.Active;
            if (manager?.World == null) return;

            var entityInfo = m_FilteredEntities[m_SelectedIndex];
            var entity = new Entity(entityInfo.Index, entityInfo.Version);
            EntityInspectorWindow.Inspect(manager.World, entity);
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// 组件类型窗口 — 查看所有注册的组件类型及内存信息。
    /// </summary>
    public class ComponentTypesWindow : EditorWindow
    {
        private string m_SearchText = "";
        private int m_SelectedIndex = -1;
        private Vector2 m_ScrollPos;
        private readonly List<ComponentTypeInfo> m_FilteredTypes = new();
        private ComponentTypeInfo[] m_AllTypes;

        public static void Open()
        {
            var window = GetWindow<ComponentTypesWindow>("Ember Component Types");
            window.minSize = new Vector2(450, 350);
            window.Show();
        }

        private void OnGUI()
        {
            RefreshTypes();

            EditorGUILayout.Space();

            // Summary
            bool sealed_ = ComponentTypeRegistry.MaxTypeId > 0;
            EditorGUILayout.LabelField(
                $"Registered: {ComponentTypeRegistry.Count} types  ·  Max TypeId: {ComponentTypeRegistry.MaxTypeId}",
                EditorStyles.miniLabel);

            // Search
            EditorGUI.BeginChangeCheck();
            m_SearchText = EditorGUILayout.TextField("Search", m_SearchText, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilter();
            }

            EditorGUILayout.Space();

            DrawTypeTable();
            EditorGUILayout.Space();
            DrawSelectedDetail();
        }

        private void RefreshTypes()
        {
            if (m_AllTypes == null || m_AllTypes.Length != ComponentTypeRegistry.Count)
            {
                m_AllTypes = new ComponentTypeInfo[ComponentTypeRegistry.Count];
                for (int i = 0; i < ComponentTypeRegistry.Count; i++)
                {
                    var typeId = new ComponentTypeId(i);
                    m_AllTypes[i] = ComponentTypeRegistry.GetInfo(typeId);
                }

                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            m_FilteredTypes.Clear();
            foreach (var info in m_AllTypes)
            {
                if (info.Type == null) continue;

                if (string.IsNullOrWhiteSpace(m_SearchText) ||
                    info.Type.Name.IndexOf(m_SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    m_FilteredTypes.Add(info);
                }
            }
        }

        private void DrawTypeTable()
        {
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("TypeId", EditorStyles.toolbarButton, GUILayout.Width(55));
            EditorGUILayout.LabelField("Component", EditorStyles.toolbarButton);
            EditorGUILayout.LabelField("Kind", EditorStyles.toolbarButton, GUILayout.Width(100));
            EditorGUILayout.LabelField("Size", EditorStyles.toolbarButton, GUILayout.Width(60));
            EditorGUILayout.LabelField("Align", EditorStyles.toolbarButton, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            for (int i = 0; i < m_FilteredTypes.Count; i++)
            {
                var info = m_FilteredTypes[i];
                bool selected = i == m_SelectedIndex;

                var bgColor = GUI.backgroundColor;
                if (selected)
                    GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                EditorGUILayout.LabelField(info.TypeId.Value.ToString(), GUILayout.Width(55));

                if (GUILayout.Button(info.Type.Name, EditorStyles.label))
                    m_SelectedIndex = i;

                EditorGUILayout.LabelField(info.Kind.ToString(), GUILayout.Width(100));
                EditorGUILayout.LabelField($"{info.Size} B", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{info.Alignment} B", GUILayout.Width(60));

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = bgColor;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectedDetail()
        {
            if (m_SelectedIndex < 0 || m_SelectedIndex >= m_FilteredTypes.Count)
                return;

            var info = m_FilteredTypes[m_SelectedIndex];

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField($"Selected: {info.Type.Name}", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Full Type", info.Type.FullName);
            EditorGUILayout.LabelField("Assembly", info.Type.Assembly.GetName().Name);
            EditorGUILayout.LabelField("TypeId", info.TypeId.Value.ToString());
            EditorGUILayout.LabelField("Kind", info.Kind.ToString());
            EditorGUILayout.LabelField("Size", $"{info.Size} bytes");
            EditorGUILayout.LabelField("Alignment", $"{info.Alignment} bytes");

            if (info.Kind == ComponentKind.Tag)
                EditorGUILayout.HelpBox("Tag components have zero size — they are not stored in chunk memory.",
                    MessageType.Info);

            EditorGUILayout.EndVertical();
        }
    }
}

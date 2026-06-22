using System;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// 实体检查器窗口 — 显示选中实体的所有组件值。
    /// </summary>
    public class EntityInspectorWindow : EditorWindow
    {
        private World m_World;
        private Entity m_Entity;
        private bool m_HasEntity;

        /// <summary>
        /// 通知检查器窗口显示指定实体的组件值。
        /// </summary>
        public static void Inspect(World world, Entity entity)
        {
            var window = GetWindow<EntityInspectorWindow>("Entity Inspector");
            window.minSize = new Vector2(350, 250);
            window.m_World = world;
            window.m_Entity = entity;
            window.m_HasEntity = true;
            window.Repaint();
            window.Show();
        }

        public static void Open()
        {
            GetWindow<EntityInspectorWindow>("Entity Inspector").Show();
        }

        private void OnGUI()
        {
            if (!m_HasEntity)
            {
                EditorGUILayout.HelpBox(
                    "No entity selected.\nClick an entity in the Entities window to inspect its components.",
                    MessageType.Info);
                return;
            }

            if (m_World == null)
            {
                EditorGUILayout.HelpBox("World reference lost.", MessageType.Warning);
                return;
            }

            ComponentInspector.DrawEntityComponents(m_World, m_Entity);
        }
    }
}

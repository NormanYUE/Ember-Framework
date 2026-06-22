using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Ember.Editor
{
    /// <summary>
    /// 在 Editor GUI 中渲染实体组件数据的静态工具类。
    /// </summary>
    public static class ComponentInspector
    {
        private static readonly GUIStyle s_HeaderStyle = new(EditorStyles.boldLabel)
        {
            fontSize = 13
        };

        private static readonly GUIStyle s_FieldNameStyle = new(EditorStyles.wordWrappedMiniLabel);

        /// <summary>
        /// 绘制实体的所有组件值。在 EditorWindow 的 OnGUI 中调用。
        /// </summary>
        public static void DrawEntityComponents(World world, Entity entity)
        {
            if (world == null || entity.IsNull)
            {
                EditorGUILayout.HelpBox("No entity selected.", MessageType.Info);
                return;
            }

            if (!world.Exists(entity))
            {
                EditorGUILayout.HelpBox("Entity is no longer alive.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();

            // Entity info
            EditorGUILayout.LabelField("Entity Info", s_HeaderStyle);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Index", entity.Index.ToString());
            EditorGUILayout.LabelField("Version", entity.Version.ToString());
            EditorGUILayout.LabelField("Handle", $"E({entity.Index}, v{entity.Version})");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Components
            EditorGUILayout.LabelField("Components", s_HeaderStyle);

            var typeIds = world.GetEntityComponentTypes(entity);
            if (typeIds.Length == 0)
            {
                EditorGUILayout.HelpBox("Entity has no components.", MessageType.Info);
                return;
            }

            foreach (var typeId in typeIds)
            {
                var typeInfo = ComponentTypeRegistry.GetInfo(typeId);
                DrawComponentFoldout(world, entity, typeId, typeInfo);
            }
        }

        private static void DrawComponentFoldout(World world, Entity entity, ComponentTypeId typeId,
            ComponentTypeInfo typeInfo)
        {
            string title = $"{typeInfo.Type.Name} ({typeInfo.Kind})";
            var foldout = EditorGUILayout.BeginFoldoutHeaderGroup(true, title);
            if (foldout)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                if (typeInfo.Kind == ComponentKind.Tag)
                {
                    EditorGUILayout.LabelField("(no data — tag component)", s_FieldNameStyle);
                }
                else if (typeInfo.Kind == ComponentKind.BufferElement)
                {
                    EditorGUILayout.LabelField("(buffer element — see BufferStore)", s_FieldNameStyle);
                }
                else
                {
                    try
                    {
                        object boxed = world.GetComponentBoxed(entity, typeId);
                        if (boxed != null)
                        {
                            DrawStructFields(boxed, typeInfo.Type);
                        }
                    }
                    catch (Exception ex)
                    {
                        EditorGUILayout.HelpBox($"Failed to read component: {ex.Message}", MessageType.Error);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawStructFields(object boxed, Type type)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length == 0)
            {
                EditorGUILayout.LabelField("(empty struct)", s_FieldNameStyle);
                return;
            }

            foreach (var field in fields)
            {
                var value = field.GetValue(boxed);
                string displayValue = FormatValue(value);
                EditorGUILayout.LabelField(field.Name, displayValue);
            }
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "null";

            var type = value.GetType();
            if (type.IsValueType && !type.IsPrimitive)
            {
                // For compound value types (e.g. float3), show fields inline
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fields.Length > 0 && fields.Length <= 4)
                {
                    var parts = new string[fields.Length];
                    for (int i = 0; i < fields.Length; i++)
                        parts[i] = $"{fields[i].Name}={fields[i].GetValue(value)}";
                    return string.Join(", ", parts);
                }
            }

            return value.ToString();
        }
    }
}

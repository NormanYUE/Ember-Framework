using UnityEditor;

namespace Ember.Editor
{
    /// <summary>
    /// Ember 调试工具的菜单入口。
    /// </summary>
    public static class EmberDebugger
    {
        [MenuItem("Window/Ember/Systems")]
        public static void OpenSystemsWindow()
        {
            SystemsWindow.Open();
        }

        [MenuItem("Window/Ember/Entities")]
        public static void OpenEntitiesWindow()
        {
            EntitiesWindow.Open();
        }

        [MenuItem("Window/Ember/Entity Inspector")]
        public static void OpenEntityInspectorWindow()
        {
            EntityInspectorWindow.Open();
        }

        [MenuItem("Window/Ember/Archetypes")]
        public static void OpenArchetypesWindow()
        {
            ArchetypesWindow.Open();
        }

        [MenuItem("Window/Ember/Component Types")]
        public static void OpenComponentTypesWindow()
        {
            ComponentTypesWindow.Open();
        }
    }
}

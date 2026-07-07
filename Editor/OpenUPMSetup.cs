#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace Ember.Editor
{
    /// <summary>
    /// 自动配置 OpenUPM registry 的 Editor 工具
    /// </summary>
    public static class OpenUPMSetup
    {
        [MenuItem("Tools/Ember/Setup OpenUPM Registry")]
        public static void SetupOpenUPM()
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");

            if (!File.Exists(manifestPath))
            {
                EditorUtility.DisplayDialog("错误", "找不到 manifest.json 文件", "确定");
                return;
            }

            string manifest = File.ReadAllText(manifestPath);

            // 检查是否已经配置了 OpenUPM
            if (manifest.Contains("package.openupm.com"))
            {
                EditorUtility.DisplayDialog("提示", "OpenUPM registry 已配置", "确定");
                return;
            }

            // 添加 scopedRegistries 配置
            string openUPMConfig = @",
  ""scopedRegistries"": [
    {
      ""name"": ""OpenUPM"",
      ""url"": ""https://package.openupm.com"",
      ""scopes"": [
        ""com.ember.ecs"",
        ""com.unity.collections"",
        ""com.unity.mathematics"",
        ""com.unity.nuget.newtonsoft-json""
      ]
    }
  ]";

            // 在第一个 { 后插入配置
            int insertIndex = manifest.IndexOf('{') + 1;
            manifest = manifest.Insert(insertIndex, openUPMConfig);

            File.WriteAllText(manifestPath, manifest);

            EditorUtility.DisplayDialog("成功", "OpenUPM registry 配置完成！\n\n现在可以在 Package Manager 中安装 com.ember.ecs", "确定");

            // 刷新 Asset Database
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Ember/Install Ember Package")]
        public static void InstallEmberPackage()
        {
            // 检查是否已配置 OpenUPM
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            string manifest = File.ReadAllText(manifestPath);

            if (!manifest.Contains("package.openupm.com"))
            {
                if (EditorUtility.DisplayDialog("提示", "需要先配置 OpenUPM registry，是否现在配置？", "配置", "取消"))
                {
                    SetupOpenUPM();
                }
                return;
            }

            // 添加依赖
            if (manifest.Contains("\"com.ember.ecs\""))
            {
                EditorUtility.DisplayDialog("提示", "Ember 包已安装", "确定");
                return;
            }

            // 在 dependencies 中添加 com.ember.ecs
            string dependency = @"""com.ember.ecs"": ""0.12.2""";

            // 找到 dependencies 块
            int depStart = manifest.IndexOf("\"dependencies\"");
            if (depStart == -1)
            {
                EditorUtility.DisplayDialog("错误", "找不到 dependencies 配置", "确定");
                return;
            }

            // 找到 dependencies 的 { 后的位置
            int braceIndex = manifest.IndexOf('{', depStart);
            manifest = manifest.Insert(braceIndex + 1, "\n    " + dependency + ",");

            File.WriteAllText(manifestPath, manifest);

            EditorUtility.DisplayDialog("成功", "Ember 包依赖已添加！\n\nUnity 正在下载...", "确定");

            AssetDatabase.Refresh();
        }
    }
}
#endif

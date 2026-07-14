#!/bin/bash
# 自动配置 OpenUPM registry 的脚本
# 用法: ./setup-openupm.sh /path/to/unity/project

if [ -z "$1" ]; then
    echo "用法: $0 /path/to/unity/project"
    exit 1
fi

PROJECT_PATH="$1"
MANIFEST_FILE="$PROJECT_PATH/Packages/manifest.json"

if [ ! -f "$MANIFEST_FILE" ]; then
    echo "错误: 找不到 manifest.json: $MANIFEST_FILE"
    exit 1
fi

echo "配置 OpenUPM registry..."

# 检查是否已经配置了 OpenUPM
if grep -q "package.openupm.com" "$MANIFEST_FILE"; then
    echo "✓ OpenUPM registry 已配置"
else
    # 添加 scopedRegistries
    if grep -q '"scopedRegistries"' "$MANIFEST_FILE"; then
        # 已有 scopedRegistries，添加 com.ember.ecs 到 scopes
        echo "添加 com.ember.ecs 到现有 registry..."
        # 使用 jq 或 sed 修改 JSON
        if command -v jq &> /dev/null; then
            jq '.scopedRegistries[0].scopes += ["com.ember.ecs"]' "$MANIFEST_FILE" > tmp.json && mv tmp.json "$MANIFEST_FILE"
        else
            echo "警告: 需要 jq 来修改 JSON，请手动添加 com.ember.ecs 到 scopes"
        fi
    else
        # 没有 scopedRegistries，添加整个配置
        echo "添加 OpenUPM registry..."
        if command -v jq &> /dev/null; then
            jq '. + {"scopedRegistries": [{"name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.ember.ecs", "com.unity.collections", "com.unity.mathematics", "com.unity.nuget.newtonsoft-json"]}]}' "$MANIFEST_FILE" > tmp.json && mv tmp.json "$MANIFEST_FILE"
        else
            # 手动添加（简单的 sed 方式）
            sed -i '' '1s/^/{\n  "scopedRegistries": [\n    {\n      "name": "OpenUPM",\n      "url": "https://package.openupm.com",\n      "scopes": ["com.ember.ecs", "com.unity.collections", "com.unity.mathematics", "com.unity.nuget.newtonsoft-json"]\n    }\n  ],\n/' "$MANIFEST_FILE"
        fi
    fi
fi

echo ""
echo "配置完成！现在可以在 Unity Package Manager 中安装 com.ember.ecs"
echo ""
echo "或者手动添加依赖到 manifest.json:"
echo '  "dependencies": {'
echo '    "com.ember.ecs": "0.12.2"'
echo '  }'

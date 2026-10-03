using System.Text;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Replaces the default renderer-feature inspector so surfaceMode drives the UI while
    // the legacy preserveSourceMaterials field stays serialized but hidden.
    [CustomEditor(typeof(CartoonRendererFeature))]
    public sealed class CartoonRendererFeatureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = serializedObject.FindProperty("settings");
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Settings missing.", MessageType.Error);
                return;
            }

            var it = settings.Copy();
            var end = settings.GetEndProperty();
            bool enterChildren = true;
            bool projected = settings.FindPropertyRelative("projectedShapes").boolValue;
            while (it.Next(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                if (it.name == "preserveSourceMaterials")
                    continue; // legacy v1 field, superseded by surfaceMode
                // Channel controls are shared by both pipelines, not legacy-only fields.
                // Character must include its children so its Enabled switch and style are editable.
                if (projected && it.name != "enabled" && it.name != "sceneChannelEnabled" &&
                    it.name != "character" && it.name != "projectedShapes" &&
                    !it.name.StartsWith("projection") && it.name != "backgroundMode" &&
                    it.name != "backgroundColor")
                    continue;
                EditorGUILayout.PropertyField(it, includeChildren: true);
                if (it.name == "projectionIdleUpdatesPerSecond" &&
                    settings.FindPropertyRelative("projectionAdaptiveRefresh").boolValue)
                    EditorGUILayout.HelpBox(
                        "静止刷新率：0.5 = 每两秒一次；0 = 保持已有色块。镜头转动时恢复运动刷新率，" +
                        "停稳后补一次最终视角重绘。0 模式下动态场景需主动通知变化；角色动画不受限制。",
                        MessageType.Info);
                if (it.name == "character" && it.isExpanded)
                    EditorGUILayout.HelpBox(
                        "角色开关：Character → Enabled。角色根节点需要 CartoonCharacterRenderController，" +
                        "Mode 使用 Inherit 或 Projected Shapes；角色重绘仅在 Play 模式下预览。",
                        MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
            if(projected)
            {
                EditorGUILayout.HelpBox("Original albedo → color regions → shared simplified contours → 2D polygon redraw. No region baking required. Legacy lighting and outline controls are bypassed.\n" + ProjectedShapePass.LastStatistics, MessageType.Info);
                return;
            }
            DrawMissingRegionDataWarning();
        }

        private void DrawMissingRegionDataWarning()
        {
            if (serializedObject.targetObject is not CartoonRendererFeature feature ||
                feature.settings.surfaceMode != CartoonSurfaceMode.MaterialColorBlocks)
                return;

            var missing = new StringBuilder();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                // Explicit shape parts without a region binding fall back to fillColor.
                if (renderer.GetComponent<CartoonShapePart>() == null ||
                    renderer.GetComponent<CartoonColorRegionBinding>() != null)
                    continue;
                missing.AppendLine(renderer.name);
                if (missing.Length > 400)
                    break;
            }

            if (missing.Length > 0)
                EditorGUILayout.HelpBox(
                    "MaterialColorBlocks is active but these renderers have no baked region data and will fall back to CartoonShapePart colors:\n" + missing,
                    MessageType.Warning);
        }
    }
}

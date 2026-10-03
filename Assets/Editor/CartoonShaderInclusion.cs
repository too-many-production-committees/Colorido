using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The cartoon passes resolve their shaders through Shader.Find at runtime. Nothing
// references those shaders from a scene or material, so a player build would strip them
// and the projection would silently stop working. Keep them in the always-included list.
[InitializeOnLoad]
public static class CartoonShaderInclusion
{
    private static readonly string[] Shaders =
    {
        "Hidden/CartoonProjection/ProjectedCapture",
        "Hidden/CartoonProjection/ProjectedRedraw",
        "Hidden/CartoonProjection/ProjectedCharacter",
        "Hidden/CartoonProjection/SpriteReadback",
        "Custom/Billboard Image",
        "Custom/Unlit Transparent Double Sided",
        "Custom/Pixelated Model"
    };

    static CartoonShaderInclusion()
    {
        EditorApplication.delayCall += EnsureShadersIncluded;
    }

    [MenuItem("Cartoon Migration/Ensure Shaders Always Included")]
    public static void EnsureShadersIncluded()
    {
        var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        if (graphicsSettings == null)
            return;

        var serialized = new SerializedObject(graphicsSettings);
        SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
        if (list == null)
            return;

        var present = new HashSet<Shader>();
        for (int i = 0; i < list.arraySize; i++)
        {
            var element = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (element != null)
                present.Add(element);
        }

        bool changed = false;
        foreach (string name in Shaders)
        {
            Shader shader = Shader.Find(name);
            if (shader == null || present.Contains(shader))
                continue;

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            changed = true;
        }

        if (!changed)
            return;

        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log("[CartoonShaderInclusion] Added projection shaders to Always Included Shaders.");
    }
}

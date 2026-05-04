using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProjectionSolid))]
public class ProjectionSolidEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ProjectionSolid solid = (ProjectionSolid)target;

        EditorGUI.BeginChangeCheck();
        bool activeInProjection = EditorGUILayout.Toggle("Active In Projection", solid.activeInProjection);
        bool includeChildren = EditorGUILayout.Toggle("Include Children", solid.includeChildren);
        ProjectionViewMask activeViews = (ProjectionViewMask)EditorGUILayout.EnumFlagsField("Active Views", solid.activeViews);
        bool alwaysProjected = EditorGUILayout.Toggle("Always Projected", solid.alwaysProjected);
        ProjectionSolidColliderMode colliderMode = (ProjectionSolidColliderMode)EditorGUILayout.EnumPopup("Collider Mode", solid.colliderMode);
        Vector2 padding = EditorGUILayout.Vector2Field("Padding", solid.padding);

        if (!EditorGUI.EndChangeCheck())
            return;

        Undo.RecordObject(solid, "Edit Projection Solid");
        solid.activeInProjection = activeInProjection;
        solid.includeChildren = includeChildren;
        solid.activeViews = activeViews;
        solid.alwaysProjected = alwaysProjected;
        solid.colliderMode = colliderMode;
        solid.padding = padding;
        EditorUtility.SetDirty(solid);
    }
}

[CustomEditor(typeof(ProjectionWalkable))]
public class ProjectionWalkableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ProjectionWalkable walkable = (ProjectionWalkable)target;

        EditorGUI.BeginChangeCheck();
        bool activeInProjection = EditorGUILayout.Toggle("Active In Projection", walkable.activeInProjection);
        bool includeChildren = EditorGUILayout.Toggle("Include Children", walkable.includeChildren);
        ProjectionViewMask activeViews = (ProjectionViewMask)EditorGUILayout.EnumFlagsField("Active Views", walkable.activeViews);
        bool alwaysProjected = EditorGUILayout.Toggle("Always Projected", walkable.alwaysProjected);
        Vector2 padding = EditorGUILayout.Vector2Field("Padding", walkable.padding);

        if (!EditorGUI.EndChangeCheck())
            return;

        Undo.RecordObject(walkable, "Edit Projection Walkable");
        walkable.activeInProjection = activeInProjection;
        walkable.includeChildren = includeChildren;
        walkable.activeViews = activeViews;
        walkable.alwaysProjected = alwaysProjected;
        walkable.padding = padding;
        EditorUtility.SetDirty(walkable);
    }
}

[CustomEditor(typeof(ProjectionInteractable))]
public class ProjectionInteractableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}

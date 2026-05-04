using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProjectionOccluderAutoAssigner
{
    private const string MenuPath = "Tools/Projection/Add Occluder To Scene Objects";

    [MenuItem(MenuPath)]
    public static void AddOccluderToSceneObjects()
    {
        GameObject[] sceneObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        int addedCount = 0;
        int eligibleCount = 0;

        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject == null || !sceneObject.scene.IsValid() || !sceneObject.scene.isLoaded)
                continue;

            if (!HasColliderOrRenderer(sceneObject))
                continue;

            eligibleCount++;

            if (sceneObject.GetComponent<ProjectionOccluder>() != null)
                continue;

            if (ShouldSkipObject(sceneObject))
                continue;

            ProjectionOccluder occluder = Undo.AddComponent<ProjectionOccluder>(sceneObject);
            occluder.participateInOcclusion = true;
            addedCount++;
        }

        if (addedCount > 0)
            EditorSceneManager.MarkAllScenesDirty();

        EditorUtility.DisplayDialog(
            "Projection Occluder Auto Assigner",
            $"Added ProjectionOccluder to {addedCount} scene object(s).\nChecked {eligibleCount} object(s) with Collider or Renderer.",
            "OK");
    }

    private static bool HasColliderOrRenderer(GameObject sceneObject)
    {
        return sceneObject.GetComponent<Collider>() != null ||
               sceneObject.GetComponent<Renderer>() != null;
    }

    private static bool ShouldSkipObject(GameObject sceneObject)
    {
        return IsPlayer(sceneObject) ||
               IsCamera(sceneObject) ||
               IsSystemManagementObject(sceneObject);
    }

    private static bool IsPlayer(GameObject sceneObject)
    {
        if (sceneObject.GetComponent<PlayerController>() != null)
            return true;

        if (HasTag(sceneObject, "Player"))
            return true;

        return string.Equals(sceneObject.name, "Player", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCamera(GameObject sceneObject)
    {
        return sceneObject.GetComponent<Camera>() != null ||
               sceneObject.GetComponent<AudioListener>() != null ||
               sceneObject.name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSystemManagementObject(GameObject sceneObject)
    {
        return sceneObject.GetComponent<ProjectionManager>() != null ||
               sceneObject.GetComponent<ProjectionPhysicsBuilder>() != null ||
               sceneObject.GetComponent<FezCameraController>() != null ||
               sceneObject.name.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneObject.name.IndexOf("ProjectionPhysicsRoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneObject.name.IndexOf("GeneratedProjectionAreas", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasTag(GameObject sceneObject, string tagName)
    {
        try
        {
            return sceneObject.CompareTag(tagName);
        }
        catch (UnityException)
        {
            return false;
        }
    }
}

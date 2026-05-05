using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ProjectionObjectRole
{
    None,
    Collision,
    Interactable,
    CollisionAndInteractable
}

[DisallowMultipleComponent]
public class ProjectionObjectRoleMarker : MonoBehaviour
{
    public ProjectionObjectRole role = ProjectionObjectRole.None;

    public void ApplyRole()
    {
        switch (role)
        {
            case ProjectionObjectRole.None:
                Debug.Log("[ProjectionObjectRoleMarker] Role is None. No projection state component was added.", this);
                break;

            case ProjectionObjectRole.Collision:
                EnsureProjectionSolid();
                break;

            case ProjectionObjectRole.Interactable:
                EnsureProjectionInteractable();
                break;

            case ProjectionObjectRole.CollisionAndInteractable:
                EnsureProjectionSolid();
                EnsureProjectionInteractable();
                break;
        }
    }

    void EnsureProjectionSolid()
    {
        if (TryGetComponent(out ProjectionSolid _))
        {
            Debug.Log("[ProjectionObjectRoleMarker] 已有 ProjectionSolid 组件，已复用。", this);
            return;
        }

        AddProjectionComponent<ProjectionSolid>();
        Debug.Log("[ProjectionObjectRoleMarker] Added ProjectionSolid.", this);
    }

    void EnsureProjectionInteractable()
    {
        if (TryGetComponent(out ProjectionInteractable _))
        {
            Debug.Log("[ProjectionObjectRoleMarker] 已有 ProjectionInteractable 组件，已复用。", this);
            return;
        }

        AddProjectionComponent<ProjectionInteractable>();
        Debug.Log("[ProjectionObjectRoleMarker] Added ProjectionInteractable.", this);
    }

    T AddProjectionComponent<T>() where T : Component
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return Undo.AddComponent<T>(gameObject);
#endif

        return gameObject.AddComponent<T>();
    }
}

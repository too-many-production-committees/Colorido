using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ProjectionObjectRole
{
    None = 0,
    Collision = 1,
    Interactable = 2,
    CollisionAndInteractable = 3,
    Ground = 4,
    Platform = 5,
    Obstacle = 6
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

            case ProjectionObjectRole.Ground:
            case ProjectionObjectRole.Platform:
                ConfigureProjectionSolid(ProjectionSolidColliderMode.TopSurface);
                break;

            case ProjectionObjectRole.Obstacle:
                ConfigureProjectionSolid(ProjectionSolidColliderMode.Box);
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

    void ConfigureProjectionSolid(ProjectionSolidColliderMode colliderMode)
    {
        ProjectionSolid solid = EnsureProjectionSolid();
        if (solid != null)
            solid.colliderMode = colliderMode;
    }

    ProjectionSolid EnsureProjectionSolid()
    {
        if (TryGetComponent(out ProjectionSolid solid))
        {
            Debug.Log("[ProjectionObjectRoleMarker] Reusing existing ProjectionSolid component.", this);
            return solid;
        }

        solid = AddProjectionComponent<ProjectionSolid>();
        Debug.Log("[ProjectionObjectRoleMarker] Added ProjectionSolid.", this);
        return solid;
    }

    void EnsureProjectionInteractable()
    {
        if (TryGetComponent(out ProjectionInteractable _))
        {
            Debug.Log("[ProjectionObjectRoleMarker] Reusing existing ProjectionInteractable component.", this);
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

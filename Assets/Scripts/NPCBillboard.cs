using UnityEngine;

/// <summary>
/// Keeps an NPC visual child aligned to the current camera without rotating the NPC root.
/// Attach this to the visual billboard object, not the collider or interaction root.
/// </summary>
[ExecuteAlways]
public class NPCBillboard : MonoBehaviour
{
    [Tooltip("Camera transform to face. Leave empty to use Camera.main.")]
    public Transform targetCameraTransform;

    [Tooltip("Euler correction applied after matching the camera rotation. Use this to fix front/back sprite offsets.")]
    public Vector3 rotationCorrectionEuler = new Vector3(0f, 180f, 0f);

    private Camera cachedMainCamera;

    void LateUpdate()
    {
        Transform cameraTransform = ResolveCameraTransform();
        if (cameraTransform == null)
            return;

        transform.rotation = cameraTransform.rotation * Quaternion.Euler(rotationCorrectionEuler);
    }

    Transform ResolveCameraTransform()
    {
        if (targetCameraTransform != null)
            return targetCameraTransform;

        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
            cachedMainCamera = Camera.main;

        return cachedMainCamera != null ? cachedMainCamera.transform : null;
    }
}

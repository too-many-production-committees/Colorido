using UnityEngine;

/// <summary>
/// 场景图片（广告牌）组件。
/// 挂在 Quad 或 Sprite 物体上，可选择是否始终朝向摄像机。
/// - faceCamera = false：图片固定在场景中不旋转。
/// - faceCamera = true：LateUpdate 中朝向摄像机。
///   - yAxisOnly = true：仅绕 Y 轴旋转，保持竖直。
///   - yAxisOnly = false：完全朝向摄像机。
/// 不依赖项目中任何其他脚本，不影响物理、投影或遮挡系统。
/// </summary>
public class SimpleBillboardImage : MonoBehaviour
{
    [Tooltip("要朝向的摄像机，留空则自动使用 Camera.main")]
    public Camera targetCamera;

    [Tooltip("是否始终朝向摄像机")]
    public bool faceCamera = false;

    [Tooltip("仅绕 Y 轴旋转（保持竖直），关闭后完全朝向摄像机")]
    public bool yAxisOnly = true;

    void LateUpdate()
    {
        if (!faceCamera)
            return;

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        if (yAxisOnly)
        {
            Vector3 forward = targetCamera.transform.position - transform.position;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
        else
        {
            transform.rotation = targetCamera.transform.rotation;
        }
    }
}
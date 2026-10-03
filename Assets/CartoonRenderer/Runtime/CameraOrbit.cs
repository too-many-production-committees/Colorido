using UnityEngine;

namespace CartoonProjection
{
    public sealed class CameraOrbit : MonoBehaviour
    {
        public Transform target;
        [Min(0f)] public float degreesPerSecond = 12f;
        public bool orbit = true;

        private void LateUpdate()
        {
            if (target == null) return;
            if (orbit) transform.RotateAround(target.position, Vector3.up, degreesPerSecond * Time.deltaTime);
            transform.LookAt(target.position);
        }
    }
}

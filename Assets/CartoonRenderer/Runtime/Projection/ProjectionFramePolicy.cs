using UnityEngine;

namespace CartoonProjection
{
    // Camera state belongs to the captured frame, not to the eventual readback callback.
    public struct ProjectionView
    {
        public Vector3 position;
        public Quaternion rotation;
        public Matrix4x4 projection;
        public bool orthographic;
        public float size, aspect;
        public int pixelWidth, pixelHeight;

        public static ProjectionView Capture(Camera camera) => new ProjectionView {
            position = camera.transform.position, rotation = camera.transform.rotation,
            projection = camera.projectionMatrix, orthographic = camera.orthographic,
            size = camera.orthographicSize, aspect = camera.aspect,
            pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight
        };

        public bool Changed(ProjectionView other)
        {
            if ((position - other.position).sqrMagnitude > 0.00000001f ||
                Quaternion.Angle(rotation, other.rotation) > .001f || orthographic != other.orthographic ||
                pixelWidth != other.pixelWidth || pixelHeight != other.pixelHeight) return true;
            for (int i = 0; i < 16; i++) if (Mathf.Abs(projection[i] - other.projection[i]) > .00001f) return true;
            return false;
        }

        public bool IsCameraCut(ProjectionView other) => orthographic != other.orthographic ||
            Quaternion.Angle(rotation, other.rotation) > 25f ||
            (position - other.position).sqrMagnitude > Mathf.Pow(Mathf.Max(2f, size), 2);

        // Exact screen-plane alignment for standard parallel orthographic cameras.
        // Rotation/perspective need depth-aware reprojection and deliberately stay unwarped.
        public Matrix4x4 AlignmentTo(ProjectionView current)
        {
            if (!orthographic || !current.orthographic || Quaternion.Angle(rotation, current.rotation) > .01f ||
                size <= 0 || current.size <= 0 || aspect <= 0 || current.aspect <= 0 ||
                Mathf.Abs(projection.m02) > .00001f || Mathf.Abs(projection.m12) > .00001f ||
                Mathf.Abs(current.projection.m02) > .00001f || Mathf.Abs(current.projection.m12) > .00001f)
                return Matrix4x4.identity;
            float xScale = size * aspect / (current.size * current.aspect);
            float yScale = size / current.size;
            var shift = position - current.position;
            var result = Matrix4x4.identity;
            result.m00 = xScale;
            result.m11 = yScale;
            result.m03 = .5f - .5f * xScale + Vector3.Dot(shift, current.rotation * Vector3.right) / (2 * current.size * current.aspect);
            result.m13 = .5f - .5f * yScale + Vector3.Dot(shift, current.rotation * Vector3.up) / (2 * current.size);
            return result;
        }
    }

    public sealed class ProjectionFramePolicy
    {
        bool initialized;
        ProjectionView previous;
        double lastMotion;
        public bool Moving { get; private set; }
        public bool Interacting { get; private set; }
        public bool Idle { get; private set; }
        public double CaptureInterval { get; private set; }
        public bool Changed { get; private set; }
        public int Width { get; private set; }
        public int UpdatesPerSecond { get; private set; }
        public ProjectionView Current { get; private set; }

        public void Observe(Camera camera, double now, CartoonRenderSettings settings, bool fixedTooling)
        {
            Current = ProjectionView.Capture(camera);
            Changed = initialized && previous.Changed(Current);
            if (Changed || !initialized) lastMotion = now;
            Interacting = !fixedTooling &&
                now - lastMotion < Mathf.Max(.05f, settings.projectionSettleSeconds);
            Moving = settings.projectionLowLatency && Interacting;
            // A new camera starts in interaction quality; lastMotion above also prevents
            // adaptive idle throttling from delaying its first capture.
            Width = Mathf.Clamp(Moving ? Mathf.Min(settings.projectionWidth, settings.projectionMotionWidth) : settings.projectionWidth, 128, 640);
            UpdatesPerSecond = Mathf.Max(1, Moving ? settings.projectionMotionUpdatesPerSecond : settings.projectionUpdatesPerSecond);
            Idle = !fixedTooling && settings.projectionAdaptiveRefresh && !Interacting;
            float idleRate = Mathf.Clamp(settings.projectionIdleUpdatesPerSecond, 0f, 2f);
            CaptureInterval = Idle ? (idleRate > 0 ? 1.0 / idleRate : double.PositiveInfinity) : 1.0 / UpdatesPerSecond;
            previous = Current;
            initialized = true;
        }
    }
}

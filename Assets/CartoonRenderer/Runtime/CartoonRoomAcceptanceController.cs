using UnityEngine;

namespace CartoonProjection
{
    // Isolated visual acceptance controls; deliberately independent of game physics.
    public sealed class CartoonRoomAcceptanceController : MonoBehaviour
    {
        public Camera viewCamera;
        public CartoonRendererFeature feature;
        public Transform character;
        public Transform characterVisual;
        public GameObject[] cutawayWalls;
        public Vector3[] outwardNormals;
        public Vector3 focus = new Vector3(0, 1.5f, 0);
        public float yaw = -22f;
        public float pitch = 24f;
        public bool autoOrbit;
        bool previousEnabled, previousProjected, previousContours, restoreSettings;

        void OnEnable()
        {
            if (!Application.isPlaying || !feature) return;
            previousEnabled = feature.settings.enabled;
            previousProjected = feature.settings.projectedShapes;
            previousContours = feature.settings.projectionShowContours;
            restoreSettings = true;
            feature.settings.enabled = true;
            feature.settings.projectedShapes = true;
            feature.settings.projectionShowContours = false;
            ApplyView();
        }

        void OnDisable()
        {
            if (!restoreSettings || !feature) return;
            feature.settings.enabled = previousEnabled;
            feature.settings.projectedShapes = previousProjected;
            feature.settings.projectionShowContours = previousContours;
            restoreSettings = false;
        }

        void Update()
        {
            if (!viewCamera || !feature || !character) return;
            if (Input.GetKeyDown(KeyCode.T)) feature.settings.enabled = !feature.settings.enabled;
            if (Input.GetKeyDown(KeyCode.C)) feature.settings.projectionShowContours = !feature.settings.projectionShowContours;
            if (Input.GetKeyDown(KeyCode.Space)) autoOrbit = !autoOrbit;
            if (Input.GetKeyDown(KeyCode.R)) ResetView();
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetOcclusionPose(false);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetOcclusionPose(true);
            float rotation = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            yaw += (rotation * 45f + (autoOrbit ? 12f : 0f)) * Time.deltaTime;
            var motion = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), 0,
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            var position = character.position + motion.normalized * (2.5f * Time.deltaTime);
            character.position = new Vector3(Mathf.Clamp(position.x, -5.5f, 5.5f), 0, Mathf.Clamp(position.z, -3.5f, 3.5f));
        }

        void LateUpdate() => ApplyView();

        public void ApplyView()
        {
            if (!viewCamera) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            viewCamera.transform.SetPositionAndRotation(focus + rotation * new Vector3(0, 0, -18), rotation);
            if (characterVisual) characterVisual.rotation = viewCamera.transform.rotation;
            if (cutawayWalls == null || outwardNormals == null) return;
            for (int i = 0; i < Mathf.Min(cutawayWalls.Length, outwardNormals.Length); i++)
            {
                var wall = cutawayWalls[i];
                if (wall) wall.SetActive(Vector3.Dot(viewCamera.transform.position - wall.transform.position, outwardNormals[i]) < 0);
            }
        }

        public void ResetView()
        {
            yaw = -22;
            pitch = 24;
            autoOrbit = false;
            if (character) character.position = new Vector3(-0.3f, 0, -2.6f);
            ApplyView();
        }

        public void SetOcclusionPose(bool behind)
        {
            autoOrbit = false;
            yaw = 0;
            pitch = 12;
            if (character) character.position = new Vector3(-1.9f, 0, behind ? 0.6f : -1.9f);
            ApplyView();
        }

        void OnGUI()
        {
            if (!feature) return;
            GUILayout.BeginArea(new Rect(10, 10, 440, 185), GUI.skin.box);
            GUILayout.Label("COLORIDO / 3D -> 2D SHAPE ACCEPTANCE");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(feature.settings.enabled ? "T: Effect ON" : "T: Effect OFF"))
                feature.settings.enabled = !feature.settings.enabled;
            if (GUILayout.Button("C: Contours")) feature.settings.projectionShowContours = !feature.settings.projectionShowContours;
            if (GUILayout.Button("R: Reset")) ResetView();
            GUILayout.EndHorizontal();
            GUILayout.Label("Q / E: orbit   Space: auto orbit   WASD: move character");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1: In front of table")) SetOcclusionPose(false);
            if (GUILayout.Button("2: Behind table")) SetOcclusionPose(true);
            GUILayout.EndHorizontal();
            GUILayout.Label("Movement is a visual probe, not gameplay / collision.");
            GUILayout.Label(feature.settings.enabled ? ProjectedShapePass.LastStatistics : "Original URP lighting (projection disabled)");
            if (feature.settings.enabled)
                GUILayout.Label($"Projection {ProjectedShapePass.LastProjectionWidth}px / capture -> redraw submit {ProjectedShapePass.LastCaptureToRedrawMilliseconds:F1} ms");
            GUILayout.EndArea();
        }
    }
}

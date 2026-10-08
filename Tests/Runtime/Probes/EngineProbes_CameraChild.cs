using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Where the camera variant's UI camera can live: under its own Screen Space - Camera canvas, or beside it under a shared parent.
    public sealed class EngineProbes_CameraChild
    {
        const int k_Frames = 60;
        const float k_Tolerance = 0.0001f;
        const int k_TargetSize = 64;

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();
        Camera m_Camera;

        [TearDown]
        public void TearDown()
        {
            // The texture is destroyed with the rest, so the camera must let go of it first.
            if (m_Camera != null)
                m_Camera.targetTexture = null;

            m_Cleanup.DestroyAll();
        }

        // Recorded, not asserted: the canvas sits on its own child camera's position, so the camera cannot draw it.
        [UnityTest]
        public IEnumerator P9b_CameraChildOfItsOwnCameraModeCanvas()
        {
            var root = NewRoot(null);
            var camera = NewCamera(root.transform);
            yield return Observe("child", root, camera);
        }

        [UnityTest]
        public IEnumerator P9b_CameraBesideItsCanvasUnderASharedParent_IsDrawnAndStaysPut()
        {
            var holder = m_Cleanup.Track(new GameObject("ProbeHolder"));
            var root = NewRoot(holder.transform);
            var camera = NewCamera(holder.transform);
            var observation = new Observation();
            yield return Observe("sibling", root, camera, observation);

            Assert.That(observation.CameraDrift, Is.LessThan(k_Tolerance), "the UI camera does not move frame to frame");
            Assert.That(observation.CanvasDrift, Is.LessThan(k_Tolerance), "the canvas does not move frame to frame");
            Assert.That(observation.Drawn, Is.True, "the canvas is drawn by the camera beside it (centre pixel " + observation.Centre + ")");
        }

        sealed class Observation
        {
            public float CameraDrift;
            public float CanvasDrift;
            public Color Centre;
            public bool Drawn;
        }

        GameObject NewRoot(Transform parent)
        {
            var root = new GameObject("ProbeRoot", typeof(RectTransform));
            if (parent == null)
                m_Cleanup.Track(root);
            else
                root.transform.SetParent(parent, false);

            root.layer = 5;
            root.AddComponent<Canvas>();
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            var view = ProbeUi.NewNestedCanvas("View", root.transform, 10, true, false);
            ProbeUi.NewImage("Red", view.transform, Vector2.zero, Vector2.one).color = Color.red;

            // The UI camera draws only the UI layer, so everything under the root is put on it.
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 5;

            return root;
        }

        Camera NewCamera(Transform parent)
        {
            var cameraObject = new GameObject("UI Camera");
            cameraObject.layer = 5;
            cameraObject.transform.SetParent(parent, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.cullingMask = 1 << 5;

            // Drawn into a texture and read back, so "it stays put" also means "it is drawn".
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = m_Cleanup.Track(new RenderTexture(k_TargetSize, k_TargetSize, 24, RenderTextureFormat.ARGB32));
            m_Camera = camera;
            return camera;
        }

        IEnumerator Observe(string arrangement, GameObject root, Camera camera, Observation observation = null)
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 100f;

            foreach (var step in ProbeUi.Frames(5))
                yield return step;

            var cameraStart = camera.transform.position;
            var canvasStart = root.transform.position;
            var cameraDrift = 0f;
            var canvasDrift = 0f;
            for (var i = 0; i < k_Frames; i++)
            {
                yield return null;
                cameraDrift = Mathf.Max(cameraDrift, Vector3.Distance(cameraStart, camera.transform.position));
                canvasDrift = Mathf.Max(canvasDrift, Vector3.Distance(canvasStart, root.transform.position));
            }

            var centre = ReadCentre(camera);
            var drawn = centre.r > 0.5f && centre.g < 0.5f && centre.b < 0.5f;
            ProbeLog.Write("P9b",
                "arrangement", arrangement,
                "cameraPosition", camera.transform.position,
                "canvasPosition", root.transform.position,
                "maxCameraDrift", cameraDrift,
                "maxCanvasDrift", canvasDrift,
                "centrePixel", centre,
                "drawn", drawn);

            if (observation == null)
                yield break;

            observation.CameraDrift = cameraDrift;
            observation.CanvasDrift = canvasDrift;
            observation.Centre = centre;
            observation.Drawn = drawn;
        }

        Color ReadCentre(Camera camera)
        {
            var target = camera.targetTexture;
            var readback = m_Cleanup.Track(new Texture2D(k_TargetSize, k_TargetSize, TextureFormat.RGBA32, false));
            Canvas.ForceUpdateCanvases();
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            try
            {
                readback.ReadPixels(new Rect(0, 0, k_TargetSize, k_TargetSize), 0, 0, false);
                return readback.GetPixel(k_TargetSize / 2, k_TargetSize / 2);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}

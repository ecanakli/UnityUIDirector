using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // A Screen Space - Camera root with a nested override-sorting canvas.
    public sealed class EngineProbes_CameraRoot
    {
        sealed class Rig
        {
            public GameObject CameraObject;
            public Camera Camera;
            public Canvas Root;
            public Canvas Nested;
            public GraphicRaycaster NestedRaycaster;
            public Button Button;
        }

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator P9_NestedCanvas_FollowsRootRenderModeAndCamera()
        {
            var rig = BuildRig();
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            var point = ProbeUi.ScreenPointOf((RectTransform)rig.Button.transform, rig.Camera);
            var hits = ProbeUi.Raycast(point);
            ProbeLog.Write("P9", "case", "nestedFollowsRoot",
                "rootRenderMode", rig.Root.renderMode,
                "nestedRenderMode", rig.Nested.renderMode,
                "rootWorldCamera", rig.Root.worldCamera,
                "nestedWorldCamera", rig.Nested.worldCamera,
                "nestedRootCanvasIsRoot", rig.Nested.rootCanvas == rig.Root,
                "nestedIsRootCanvas", rig.Nested.isRootCanvas,
                "nestedRaycasterEventCamera", rig.NestedRaycaster.eventCamera,
                "buttonScreenPoint", point,
                "hitButton", ProbeUi.Hits(hits, rig.Button.gameObject),
                "hits", ProbeUi.Describe(hits));

            Assert.AreEqual(RenderMode.ScreenSpaceCamera, rig.Nested.renderMode, "The nested canvas must follow the root's render mode.");
            Assert.IsTrue(rig.Nested.worldCamera == rig.Camera, "The nested canvas must follow the root's camera.");
            Assert.IsTrue(rig.Nested.rootCanvas == rig.Root, "rootCanvas of the nested canvas must be the root.");
            Assert.IsFalse(rig.Nested.isRootCanvas, "The nested canvas is not a root canvas.");
        }

        [UnityTest]
        public IEnumerator P9_RootCameraDestroyed()
        {
            var rig = BuildRig();
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            var pointBefore = ProbeUi.ScreenPointOf((RectTransform)rig.Button.transform, rig.Camera);
            var hitBefore = ProbeUi.Hits(ProbeUi.Raycast(pointBefore), rig.Button.gameObject);

            // Destroying the camera may log; that is part of the observation, not a failure.
            var capture = new ProbeLogCapture();
            try
            {
                Object.Destroy(rig.CameraObject);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var pointAfter = ProbeUi.ScreenPointOf((RectTransform)rig.Button.transform, null);
                var hitAtOldPoint = ProbeUi.Hits(ProbeUi.Raycast(pointBefore), rig.Button.gameObject);
                var hitAtNewPoint = ProbeUi.Hits(ProbeUi.Raycast(pointAfter), rig.Button.gameObject);

                ProbeLog.Write("P9", "case", "cameraDestroyed",
                    "rootRenderMode", rig.Root.renderMode,
                    "rootWorldCameraIsNull", rig.Root.worldCamera == null,
                    "nestedWorldCameraIsNull", rig.Nested.worldCamera == null,
                    "buttonPointBefore", pointBefore,
                    "buttonPointAfter", pointAfter,
                    "hitBeforeDestroy", hitBefore,
                    "hitAtOldPoint", hitAtOldPoint,
                    "hitAtNewPoint", hitAtNewPoint,
                    "logCount", capture.Count,
                    "firstLog", capture.First);
            }
            finally
            {
                capture.Dispose();
            }
        }

        Rig BuildRig()
        {
            m_Cleanup.Track(ProbeUi.NewEventSystem(false));

            var rig = new Rig();
            rig.CameraObject = m_Cleanup.Track(new GameObject("ProbeCamera"));
            rig.CameraObject.transform.position = new Vector3(0f, 0f, -10f);
            rig.Camera = rig.CameraObject.AddComponent<Camera>();
            rig.Camera.orthographic = true;
            rig.Camera.orthographicSize = 5f;
            rig.Camera.nearClipPlane = 0.1f;
            rig.Camera.farClipPlane = 100f;
            rig.Camera.clearFlags = CameraClearFlags.SolidColor;

            rig.Root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(rig.Root.gameObject);
            rig.Root.renderMode = RenderMode.ScreenSpaceCamera;
            rig.Root.worldCamera = rig.Camera;
            rig.Root.planeDistance = 5f;

            rig.Nested = ProbeUi.NewNestedCanvas("Nested", rig.Root.transform, 5);
            rig.NestedRaycaster = rig.Nested.GetComponent<GraphicRaycaster>();
            rig.Button = ProbeUi.NewButton("Button", rig.Nested.transform, new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f));
            return rig;
        }
    }
}

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // UguiStage constructor: a layer list it refuses leaves nothing under the root.
    [TestFixture]
    public sealed class UguiStageTests
    {
        private GameObject _rootObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_rootObject != null)
            {
                Object.Destroy(_rootObject);
                _rootObject = null;
            }

            // Destroy is deferred; the next test must not see this root.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Ctor_LayerListedTwice_LeavesNoObjectsUnderRoot()
        {
            var canvas = CreateRootCanvas();
            var root = (RectTransform)canvas.transform;
            var diagnostics = new Diagnostics();
            var layers = new[] { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.Popups };

            var exception = Assert.Throws<ArgumentException>(() => new UguiStage(
                root,
                canvas,
                layers,
                null,
                null,
                null,
                Color.black,
                null,
                diagnostics,
                new ErrorRouter(null, diagnostics),
                false));

            // The rects built before the repeat are removed with Object.Destroy, which takes effect at the end of the frame.
            Assert.That(root.childCount, Is.EqualTo(2), "rects built before the repeat, still present in the throwing frame");
            yield return null;
            Assert.That(exception.ParamName, Is.EqualTo("layers"), "parameter name");
            Assert.That(root.childCount, Is.EqualTo(0), "children left under the root");
        }

        [UnityTest]
        public IEnumerator Ctor_LayerListContainsNull_LeavesNoObjectsUnderRoot()
        {
            var canvas = CreateRootCanvas();
            var root = (RectTransform)canvas.transform;
            var diagnostics = new Diagnostics();
            var layers = new[] { ViewLayers.Screens, null };

            var exception = Assert.Throws<ArgumentException>(() => new UguiStage(
                root,
                canvas,
                layers,
                null,
                null,
                null,
                Color.black,
                null,
                diagnostics,
                new ErrorRouter(null, diagnostics),
                false));

            Assert.That(root.childCount, Is.EqualTo(1), "rect built before the null entry, still present in the throwing frame");
            yield return null;
            Assert.That(exception.ParamName, Is.EqualTo("layers"), "parameter name");
            Assert.That(root.childCount, Is.EqualTo(0), "children left under the root");
        }

        private Canvas CreateRootCanvas()
        {
            _rootObject = new GameObject("TestStageRoot", typeof(RectTransform));
            var canvas = _rootObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }
    }
}

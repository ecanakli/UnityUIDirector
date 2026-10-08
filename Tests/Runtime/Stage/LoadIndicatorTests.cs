using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests
{
    // The load indicator draws above every view: its own override canvas one above the shield, on the root's sorting layer.
    [TestFixture]
    public sealed class LoadIndicatorTests
    {
        private const int BaseOrder = 200;

        private StageWorld _world;
        private GameObject _indicator;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _indicator = null;
            if (_world == null)
                yield break;

            _world.Dispose();
            _world = null;

            // Destroy is deferred; the next test must not see this world's objects.
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetLoadIndicator_IndicatorWithoutCanvas_GetsOverrideCanvasOneAboveShield()
        {
            _world = new StageWorld(root => AddIndicator(root, false));
            yield return null;

            ShowWithShieldUp();

            var canvases = _indicator.GetComponents<Canvas>();
            Assert.That(canvases, Has.Length.EqualTo(1), "canvases on the indicator");
            Assert.That(_indicator.activeSelf, Is.True, "indicator active");
            Assert.That(canvases[0].overrideSorting, Is.True, "override sorting");
            Assert.That(canvases[0].sortingOrder, Is.EqualTo(ExpectedShieldOrder() + 1), "indicator order");
            Assert.That(canvases[0].sortingOrder, Is.EqualTo(ShieldCanvas().sortingOrder + 1), "one above the shield canvas");
            Assert.That(canvases[0].sortingLayerID, Is.EqualTo(_world.RootCanvas.sortingLayerID), "sorting layer");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SetLoadIndicator_IndicatorWithOwnCanvas_ReusesItWithTheSameSettings()
        {
            _world = new StageWorld(root => AddIndicator(root, true));
            yield return null;
            var authored = _indicator.GetComponent<Canvas>();

            ShowWithShieldUp();

            var canvases = _indicator.GetComponents<Canvas>();
            Assert.That(canvases, Has.Length.EqualTo(1), "canvases on the indicator");
            Assert.That(canvases[0], Is.SameAs(authored), "the authored canvas is kept");
            Assert.That(authored.overrideSorting, Is.True, "override sorting");
            Assert.That(authored.sortingOrder, Is.EqualTo(ExpectedShieldOrder() + 1), "indicator order");
            Assert.That(authored.sortingLayerID, Is.EqualTo(_world.RootCanvas.sortingLayerID), "sorting layer");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SetLoadIndicator_CameraRootSortingLayerChangedAfterInit_IndicatorTakesIt()
        {
            _world = new StageWorld(root => AddIndicator(root, false), cameraMode: true);
            yield return null;

            // Changed after initialisation, so only a read at show time can pass.
            var id = SortingLayer.NameToID("UIDirectorTest");
            Assume.That(id, Is.Not.EqualTo(0), "the host project must define the UIDirectorTest sorting layer; layers loaded: " + SortingLayer.layers.Length);
            _world.RootCanvas.sortingLayerID = id;
            Assume.That(_world.RootCanvas.sortingLayerID, Is.EqualTo(id), "the root canvas took the sorting layer");

            ShowWithShieldUp();

            var canvas = _indicator.GetComponent<Canvas>();
            Assert.That(canvas, Is.Not.Null, "indicator canvas");
            Assert.That(canvas.sortingLayerID, Is.EqualTo(id), "sorting layer");
            Assert.That(canvas.overrideSorting, Is.True, "override sorting");
            Assert.That(canvas.sortingOrder, Is.EqualTo(ExpectedShieldOrder() + 1), "indicator order");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SetLoadIndicator_Shown_AddsNoGraphicRaycaster()
        {
            _world = new StageWorld(root => AddIndicator(root, false));
            yield return null;

            ShowWithShieldUp();

            Assert.That(_indicator.GetComponentsInChildren<GraphicRaycaster>(true), Is.Empty, "raycasters on the indicator");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IndicatorOrder_ShieldAtTheCeiling_StaysBelowIt()
        {
            Assert.That(UguiStage.IndicatorOrder(3000), Is.EqualTo(3001), "ordinary shield");
            Assert.That(UguiStage.IndicatorOrder(Projection.SortingCeiling - 1), Is.EqualTo(Projection.SortingCeiling - 1), "highest shield the intake accepts");
            Assert.That(UguiStage.IndicatorOrder(int.MaxValue), Is.EqualTo(Projection.SortingCeiling - 1), "no 16-bit wrap");
        }

        // An inactive child of the root, set as the root's load indicator before initialisation.
        private void AddIndicator(ViewRoot root, bool withCanvas)
        {
            StageWorld.SetField(root, "_baseSortingOrder", BaseOrder);

            var indicator = new GameObject("LoadIndicator", typeof(RectTransform));
            indicator.transform.SetParent(root.transform, false);
            indicator.AddComponent<Image>();
            if (withCanvas)
                indicator.AddComponent<Canvas>();

            indicator.SetActive(false);
            StageWorld.SetField(root, "_loadIndicator", indicator);
            _indicator = indicator;
        }

        // The navigator raises the shield before a load shows the indicator, so the test does the same.
        private void ShowWithShieldUp()
        {
            _world.Navigator.HoldInput("load indicator test", _world.Owner);
            Assume.That(ShieldCanvas().sortingOrder, Is.EqualTo(ExpectedShieldOrder()), "the navigator placed the shield");
            _world.Stage.SetLoadIndicator(true);
        }

        private Canvas ShieldCanvas()
        {
            return _world.Stage.Shield.GetComponent<Canvas>();
        }

        private static int ExpectedShieldOrder()
        {
            return Projection.ShieldOrder(BaseOrder, ViewLayers.System.Order);
        }
    }
}

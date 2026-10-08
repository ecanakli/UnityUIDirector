using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // ViewRoot.OnValidate: Inspector values are clamped when applied through a SerializedObject; Awake does not run in Edit Mode.
    [TestFixture]
    public sealed class ViewRootValidateTests
    {
        private const string LoadTimeout = "_loadTimeoutSeconds";
        private const string TransitionTimeout = "_transitionTimeoutSeconds";
        private const string IndicatorDelay = "_indicatorDelaySeconds";
        private const string WaitingCap = "_waitingCap";
        private const string BaseSortingOrder = "_baseSortingOrder";

        private GameObject _rootObject;
        private ViewRoot _root;

        [SetUp]
        public void SetUp()
        {
            _rootObject = new GameObject("ValidateRoot", typeof(RectTransform));
            _root = _rootObject.AddComponent<ViewRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootObject != null)
                Object.DestroyImmediate(_rootObject);

            _rootObject = null;
            _root = null;
        }

        [Test]
        public void OnValidate_TimeoutBelowMinimum_ClampsToPointOne(
            [Values(LoadTimeout, TransitionTimeout)] string field,
            [Values(0f, -5f, 0.05f)] float value)
        {
            ApplyFloat(field, value);

            Assert.That(ReadFloat(field), Is.EqualTo(LayerRunner.MinTimeoutSeconds), field + " after applying " + value);
        }

        [Test]
        public void OnValidate_TimeoutNaN_ClampsToPointOne([Values(LoadTimeout, TransitionTimeout)] string field)
        {
            ApplyFloat(field, float.NaN);

            Assert.That(ReadFloat(field), Is.EqualTo(LayerRunner.MinTimeoutSeconds), field + " after applying NaN");
        }

        [Test]
        public void OnValidate_TimeoutInfinity_IsKept([Values(LoadTimeout, TransitionTimeout)] string field)
        {
            ApplyFloat(field, float.PositiveInfinity);

            Assert.That(ReadFloat(field), Is.EqualTo(float.PositiveInfinity), field + " after applying infinity, which means no timeout");
        }

        [Test]
        public void OnValidate_WaitingCapBelowOne_ClampsToOne([Values(0, -3)] int value)
        {
            ApplyInt(WaitingCap, value);

            Assert.That(ReadInt(WaitingCap), Is.EqualTo(1), "waiting cap after applying " + value);
        }

        [Test]
        public void OnValidate_IndicatorDelayNegative_ClampsToZero()
        {
            ApplyFloat(IndicatorDelay, -2f);

            Assert.That(ReadFloat(IndicatorDelay), Is.EqualTo(0f), "indicator delay after applying -2");
        }

        [TestCase(40000, 32767)]
        [TestCase(-40000, -32768)]
        public void OnValidate_BaseSortingOrderOutOfRange_ClampsToSigned16Bit(int value, int expected)
        {
            ApplyInt(BaseSortingOrder, value);

            Assert.That(ReadInt(BaseSortingOrder), Is.EqualTo(expected), "base sorting order after applying " + value);
        }

        [Test]
        public void OnValidate_ValuesInRange_AreUnchanged()
        {
            var serialized = new SerializedObject(_root);
            serialized.FindProperty(LoadTimeout).floatValue = 3.5f;
            serialized.FindProperty(TransitionTimeout).floatValue = 0.1f;
            serialized.FindProperty(IndicatorDelay).floatValue = 0f;
            serialized.FindProperty(WaitingCap).intValue = 1;
            serialized.FindProperty(BaseSortingOrder).intValue = -1200;
            serialized.ApplyModifiedProperties();

            Assert.That(ReadFloat(LoadTimeout), Is.EqualTo(3.5f), "load timeout");
            Assert.That(ReadFloat(TransitionTimeout), Is.EqualTo(0.1f), "transition timeout at the minimum");
            Assert.That(ReadFloat(IndicatorDelay), Is.EqualTo(0f), "indicator delay at zero");
            Assert.That(ReadInt(WaitingCap), Is.EqualTo(1), "waiting cap at one");
            Assert.That(ReadInt(BaseSortingOrder), Is.EqualTo(-1200), "base sorting order");
        }

        private void ApplyFloat(string field, float value)
        {
            var serialized = new SerializedObject(_root);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedProperties();
        }

        private void ApplyInt(string field, int value)
        {
            var serialized = new SerializedObject(_root);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedProperties();
        }

        // A fresh SerializedObject, so the value read is the one OnValidate left on the component.
        private float ReadFloat(string field)
        {
            return new SerializedObject(_root).FindProperty(field).floatValue;
        }

        private int ReadInt(string field)
        {
            return new SerializedObject(_root).FindProperty(field).intValue;
        }
    }
}

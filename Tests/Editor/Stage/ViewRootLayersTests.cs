using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // ViewRoot.BuildLayers: the default layers plus the extra ones, validated and stably sorted before anything is built.
    [TestFixture]
    public sealed class ViewRootLayersTests
    {
        // The ways a layer can appear twice in the full list.
        public enum Repeat
        {
            DefaultInstanceInExtras,
            ExtraInstanceTwice,
            NewInstanceWithDefaultId,
            TwoExtrasSharingAnId
        }

        [TestCase(Repeat.DefaultInstanceInExtras, "popups")]
        [TestCase(Repeat.ExtraInstanceTwice, "toasts")]
        [TestCase(Repeat.NewInstanceWithDefaultId, "screens")]
        [TestCase(Repeat.TwoExtrasSharingAnId, "toasts")]
        public void BuildLayers_LayerRepeated_ThrowsArgumentExceptionNamingTheId(Repeat repeat, string repeatedId)
        {
            var extras = ExtrasFor(repeat);

            var exception = Assert.Throws<ArgumentException>(() => ViewRoot.BuildLayers(extras));

            Assert.That(exception.Message, Does.Contain("'" + repeatedId + "'"), "message names the repeated id");
            Assert.That(exception.ParamName, Is.EqualTo("setup"), "parameter name");
        }

        [Test]
        public void BuildLayers_NullExtraEntries_SkipsThem()
        {
            var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);

            var layers = ViewRoot.BuildLayers(new[] { null, toasts });

            Assert.That(IdsOf(layers), Is.EqualTo(new[] { "screens", "popups", "system", "toasts" }));
        }

        [Test]
        public void BuildLayers_ExtraLayers_SortsStablyByOrder()
        {
            var extras = new[]
            {
                new ViewLayer("late", 5, ViewLayerRules.None),
                new ViewLayer("zeta", 1, ViewLayerRules.None),
                new ViewLayer("early", -1, ViewLayerRules.None),
                new ViewLayer("alpha", 1, ViewLayerRules.None)
            };

            var layers = ViewRoot.BuildLayers(extras);

            // Equal orders keep the given sequence: popups (a default) first, then zeta before alpha.
            Assert.That(IdsOf(layers), Is.EqualTo(new[] { "early", "screens", "popups", "zeta", "alpha", "system", "late" }));
        }

        [Test]
        public void BuildLayers_MoreThanSixteenExtrasWithEqualOrder_KeepTheGivenSequence()
        {
            // Above 16 elements an unstable sort partitions and swaps equal keys, so this size exposes one.
            const int equalCount = 20;
            var extras = new List<ViewLayer> { new ViewLayer("high", 5, ViewLayerRules.None) };
            var expected = new List<string> { "low", "screens", "popups" };
            for (var i = 0; i < equalCount; i++)
            {
                var id = "e" + i.ToString("00");
                extras.Add(new ViewLayer(id, 1, ViewLayerRules.None));
                expected.Add(id);
            }

            extras.Add(new ViewLayer("low", -1, ViewLayerRules.None));
            expected.Add("system");
            expected.Add("high");

            var layers = ViewRoot.BuildLayers(extras);

            Assert.That(IdsOf(layers), Is.EqualTo(expected), "equal orders keep the given sequence after the default popups");
        }

        private static readonly object[] NoExtras =
        {
            new object[] { null },
            new object[] { new ViewLayer[0] }
        };

        [TestCaseSource(nameof(NoExtras))]
        public void BuildLayers_NoExtraLayers_ReturnsTheThreeDefaults(IReadOnlyList<ViewLayer> extras)
        {
            var layers = ViewRoot.BuildLayers(extras);

            Assert.That(IdsOf(layers), Is.EqualTo(new[] { "screens", "popups", "system" }));
        }

        [Test]
        public void BuildLayers_IdDiffersOnlyByCase_AddsTheLayer()
        {
            var extras = new[]
            {
                new ViewLayer("Screens", 5, ViewLayerRules.None),
                new ViewLayer("POPUPS", 6, ViewLayerRules.None)
            };

            List<ViewLayer> layers = null;
            Assert.DoesNotThrow(() => layers = ViewRoot.BuildLayers(extras), "ids compare ordinally, so case makes them distinct");

            Assert.That(IdsOf(layers), Is.EqualTo(new[] { "screens", "popups", "system", "Screens", "POPUPS" }));
        }

        [Test]
        public void BuildLayers_RepeatAfterNullEntry_StillThrows()
        {
            var extras = new[] { null, ViewLayers.Popups };

            var exception = Assert.Throws<ArgumentException>(() => ViewRoot.BuildLayers(extras));

            Assert.That(exception.ParamName, Is.EqualTo("setup"), "parameter name");
            Assert.That(exception.Message, Does.Contain("'popups'"), "message names the repeated id");
        }

        private static IReadOnlyList<ViewLayer> ExtrasFor(Repeat repeat)
        {
            switch (repeat)
            {
                case Repeat.DefaultInstanceInExtras:
                    return new[] { ViewLayers.Popups };
                case Repeat.ExtraInstanceTwice:
                    var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
                    return new[] { toasts, toasts };
                case Repeat.NewInstanceWithDefaultId:
                    return new[] { new ViewLayer("screens", 5, ViewLayerRules.None) };
                case Repeat.TwoExtrasSharingAnId:
                    return new[] { new ViewLayer("toasts", 3, ViewLayerRules.None), new ViewLayer("toasts", 4, ViewLayerRules.Modal) };
                default:
                    throw new ArgumentOutOfRangeException(nameof(repeat), repeat, null);
            }
        }

        private static List<string> IdsOf(List<ViewLayer> layers)
        {
            return layers.ConvertAll(layer => layer.Id);
        }
    }
}

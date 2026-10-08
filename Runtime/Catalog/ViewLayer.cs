using System;

namespace Ecanakli.UIDirector
{
    /// <summary>An ordered band of the root with one set of rules.</summary>
    public sealed class ViewLayer
    {
        /// <summary>Creates a layer; the id must not be null or empty.</summary>
        public ViewLayer(string id, int order, ViewLayerRules rules)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (id.Length == 0)
                throw new ArgumentException("The layer id must not be empty.", nameof(id));

            Id = id;
            Order = order;
            Rules = rules;
        }

        /// <summary>The id a catalog row uses to name this layer.</summary>
        public string Id { get; }

        /// <summary>Position of the layer; a higher order is drawn above a lower one.</summary>
        public int Order { get; }

        /// <summary>The rules applied to every view on this layer.</summary>
        public ViewLayerRules Rules { get; }

        internal bool Has(ViewLayerRules rule)
        {
            return (Rules & rule) == rule;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Ecanakli.UIDirector
{
    // The rows one catalog registration contributed; kept so the registration can be removed again.
    internal sealed class CatalogRegistration
    {
        internal readonly List<ViewEntry> Rows = new List<ViewEntry>();

        public CatalogRegistration(ViewCatalog catalog)
        {
            Catalog = catalog;
        }

        public ViewCatalog Catalog { get; }
    }

    // Rows by id and the layers of the root; refuses rows with an empty id, an unknown layer or a taken id.
    internal sealed class ViewRegistry
    {
        private readonly Diagnostics _diagnostics;
        private readonly List<ViewLayer> _layers = new List<ViewLayer>();
        private readonly Dictionary<string, ViewEntry> _rows = new Dictionary<string, ViewEntry>(StringComparer.Ordinal);

        // Layers are stored by ascending order; equal orders keep the given sequence. A null or duplicate layer throws.
        public ViewRegistry(IReadOnlyList<ViewLayer> layers, Diagnostics diagnostics)
        {
            if (layers == null)
                throw new ArgumentNullException(nameof(layers));

            _diagnostics = diagnostics;

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null)
                    throw new ArgumentNullException(nameof(layers), "A layer is null.");
                if (IndexOfLayerId(layer.Id) >= 0)
                    throw new ArgumentException("The layer id '" + layer.Id + "' is used twice.", nameof(layers));

                var position = _layers.Count;
                while (position > 0 && _layers[position - 1].Order > layer.Order)
                    position--;
                _layers.Insert(position, layer);
            }
        }

        public int LayerCount
        {
            get { return _layers.Count; }
        }

        public int RowCount
        {
            get { return _rows.Count; }
        }

        public ViewLayer LayerAt(int index)
        {
            return _layers[index];
        }

        // Layers are matched by id, so an equal layer built elsewhere resolves to the registered one.
        public bool TryGetLayerIndex(string layerId, out int index)
        {
            index = layerId == null ? -1 : IndexOfLayerId(layerId);
            return index >= 0;
        }

        public bool TryGetLayerIndex(ViewLayer layer, out int index)
        {
            index = layer == null ? -1 : IndexOfLayerId(layer.Id);
            return index >= 0;
        }

        public int HighestLayerOrder()
        {
            return _layers.Count == 0 ? 0 : _layers[_layers.Count - 1].Order;
        }

        public bool TryGetRow(string id, out ViewEntry row)
        {
            if (id == null)
            {
                row = null;
                return false;
            }

            return _rows.TryGetValue(id, out row);
        }

        // True while this very row is registered; a row of a removed catalog, or one replaced by another row with its id, is not.
        public bool IsRegistered(ViewEntry row)
        {
            ViewEntry current;
            return row != null && row.Id != null && _rows.TryGetValue(row.Id, out current) && ReferenceEquals(current, row);
        }

        // Registers every acceptable row; refuses and reports rows with an empty id, an unknown layer or a taken id.
        public CatalogRegistration AddCatalog(ViewCatalog catalog)
        {
            var registration = new CatalogRegistration(catalog);
            var entries = catalog.Entries;

            for (var i = 0; i < entries.Count; i++)
            {
                var row = entries[i];
                if (row == null)
                    continue;

                if (string.IsNullOrWhiteSpace(row.Id))
                {
                    _diagnostics.Error(
                        DiagnosticIds.RowWithEmptyId,
                        "Row " + i + " of the catalog '" + catalog.name + "' has an empty id; the row was refused.");
                    continue;
                }

                if (IndexOfLayerId(row.LayerId ?? string.Empty) < 0)
                {
                    _diagnostics.Error(
                        DiagnosticIds.UnknownLayer,
                        "The row '" + row.Id + "' names the unknown layer '" + row.LayerId + "'; the row was refused.");
                    continue;
                }

                if (_rows.ContainsKey(row.Id))
                {
                    _diagnostics.Error(
                        DiagnosticIds.DuplicateViewId,
                        "The view id '" + row.Id + "' is already registered; the second row was refused.");
                    continue;
                }

                _rows.Add(row.Id, row);
                registration.Rows.Add(row);
            }

            return registration;
        }

        // Removes the rows of the registration and appends their ids to removedIds; views already open keep their row.
        public void RemoveCatalog(CatalogRegistration registration, List<string> removedIds)
        {
            for (var i = 0; i < registration.Rows.Count; i++)
            {
                var row = registration.Rows[i];
                ViewEntry current;
                if (!_rows.TryGetValue(row.Id, out current) || !ReferenceEquals(current, row))
                    continue;

                _rows.Remove(row.Id);
                if (removedIds != null)
                    removedIds.Add(row.Id);
            }

            registration.Rows.Clear();
        }

        public void Describe(StringBuilder output)
        {
            output.Append("layers:");
            for (var i = 0; i < _layers.Count; i++)
                output.Append(' ').Append(_layers[i].Id).Append('(').Append(_layers[i].Order).Append(')');
            output.Append('\n');
            output.Append("rows: ").Append(_rows.Count).Append('\n');
        }

        private int IndexOfLayerId(string layerId)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                if (string.Equals(_layers[i].Id, layerId, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }
    }
}

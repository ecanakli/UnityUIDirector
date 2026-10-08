using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ecanakli.UIDirector
{
    // Applies the visual state of one view to its object; owned by its ViewHost, reuses its lists.
    internal sealed class ViewPresentation
    {
        private readonly ViewHost _host;
        private readonly GameObject _root;
        private readonly Canvas _ownCanvas;

        private readonly List<Canvas> _canvasScratch = new List<Canvas>();
        private readonly List<Canvas> _switchedCanvases = new List<Canvas>();
        private readonly List<GraphicRaycaster> _raycasterScratch = new List<GraphicRaycaster>();
        private readonly List<GraphicRaycaster> _switchedRaycasters = new List<GraphicRaycaster>();

        private bool _canvasesOff;
        private bool _raycastersOff;
        private bool _interactive;
        private int _sortingOrder;

        public ViewPresentation(ViewHost host)
        {
            _host = host;
            _root = host.gameObject;
            _ownCanvas = host.GetComponent<Canvas>();
        }

        // The last sorting order applied to the view's own canvas; 0 before the first.
        public int SortingOrder
        {
            get { return _sortingOrder; }
        }

        public void PrepareForPlace()
        {
            // Nothing is switched here: the caller makes the first Apply in the same synchronous step, before any frame renders.
            _interactive = false;
        }

        public void Apply(in ViewVisualState state, int sortingLayerId)
        {
            if (state.Hidden)
            {
                if (state.CoveredMode == CoveredMode.Deactivate)
                    SetActive(false);
                else
                    CanvasesOff();
            }
            else
            {
                SetActive(true);
                CanvasesOn();
            }

            if (state.Interactive)
            {
                RaycastersOn();
                _interactive = true;
            }
            else
            {
                RaycastersOff();
                if (_interactive)
                    ClearSelectionUnderView();
                _interactive = false;
            }

            ApplySorting(state.SortingOrder, sortingLayerId);
        }

        public void PrepareForStash()
        {
            ClearSelectionUnderView();
            SetActive(false);
            _interactive = false;

            // The package gives back only what it switched off; the view's own writes stay.
            CanvasesOn();
            RaycastersOn();
        }

        private void CanvasesOff()
        {
            if (_canvasesOff || _root == null)
                return;

            _canvasesOff = true;
            _root.GetComponentsInChildren(true, _canvasScratch);
            for (var i = 0; i < _canvasScratch.Count; i++)
            {
                var canvas = _canvasScratch[i];
                if (!canvas.enabled)
                    continue;

                canvas.enabled = false;
                _switchedCanvases.Add(canvas);
            }

            _canvasScratch.Clear();
        }

        private void CanvasesOn()
        {
            if (!_canvasesOff)
                return;

            _canvasesOff = false;
            for (var i = 0; i < _switchedCanvases.Count; i++)
            {
                var canvas = _switchedCanvases[i];
                if (canvas != null)
                    canvas.enabled = true;
            }

            _switchedCanvases.Clear();
        }

        private void RaycastersOff()
        {
            if (_raycastersOff || _root == null)
                return;

            _raycastersOff = true;
            _root.GetComponentsInChildren(true, _raycasterScratch);
            for (var i = 0; i < _raycasterScratch.Count; i++)
            {
                var raycaster = _raycasterScratch[i];
                if (!raycaster.enabled)
                    continue;

                raycaster.enabled = false;
                _switchedRaycasters.Add(raycaster);
            }

            _raycasterScratch.Clear();
        }

        private void RaycastersOn()
        {
            if (!_raycastersOff)
                return;

            _raycastersOff = false;
            for (var i = 0; i < _switchedRaycasters.Count; i++)
            {
                var raycaster = _switchedRaycasters[i];
                if (raycaster != null)
                    raycaster.enabled = true;
            }

            _switchedRaycasters.Clear();
        }

        // Submit and Cancel go to the selection, so a view that loses input must not keep it.
        private void ClearSelectionUnderView()
        {
            if (_root == null)
                return;

            var eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.alreadySelecting)
                return;

            var selected = eventSystem.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(_root.transform))
                eventSystem.SetSelectedGameObject(null);
        }

        private void SetActive(bool active)
        {
            if (_root == null || _root.activeSelf == active)
                return;

            _host.BeginPackageChange();
            try
            {
                _root.SetActive(active);
            }
            finally
            {
                _host.EndPackageChange();
            }
        }

        private void ApplySorting(int sortingOrder, int sortingLayerId)
        {
            _sortingOrder = sortingOrder;
            if (_ownCanvas == null)
                return;

            if (_ownCanvas.overrideSorting
                && _ownCanvas.sortingOrder == sortingOrder
                && _ownCanvas.sortingLayerID == sortingLayerId)
                return;

            _ownCanvas.overrideSorting = true;
            _ownCanvas.sortingOrder = sortingOrder;
            _ownCanvas.sortingLayerID = sortingLayerId;
        }
    }
}

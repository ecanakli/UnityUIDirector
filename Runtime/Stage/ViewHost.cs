using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector
{
    /// <summary>Required on every view prefab root; connects the view object to its request.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed class ViewHost : MonoBehaviour
    {
        [SerializeField] private ViewTransition _transition;

        private ViewPresentation _presentation;
        private IViewHostSink _sink;
        private object _instance;
        private ViewHandle _handle;
        private int _packageChanges;

        /// <summary>True while the request this object shows is open or in flight.</summary>
        public bool IsOpen
        {
            get { return _handle.IsAlive; }
        }

        /// <summary>The handle of the request this object shows; inert once that request has ended.</summary>
        public ViewHandle Handle
        {
            get { return _handle; }
        }

        /// <summary>The sorting order of the view's own canvas, for particles and sub-canvases inside the view.</summary>
        public int SortingOrder
        {
            get { return _presentation == null ? 0 : _presentation.SortingOrder; }
        }

        /// <summary>Inspector entry point: closes this view without a value, as the user dismissing it; never throws.</summary>
        public void Close()
        {
            _handle.Dismiss();
        }

        // Null means the view shows and hides instantly; a destroyed reference reads as null.
        internal ViewTransition Transition
        {
            get { return _transition != null ? _transition : null; }
        }

        // Created on first use, because Awake has not run while the object is still staged inactive.
        private ViewPresentation Presentation
        {
            get
            {
                if (_presentation == null)
                    _presentation = new ViewPresentation(this);

                return _presentation;
            }
        }

        internal void Attach(IViewHostSink sink, object instance)
        {
            _sink = sink;
            _instance = instance;
        }

        internal void Detach()
        {
            _sink = null;
            _instance = null;
            _handle = default;
        }

        internal void Link(ViewHandle handle)
        {
            _handle = handle;
        }

        internal void BeginPackageChange()
        {
            _packageChanges++;
        }

        internal void EndPackageChange()
        {
            if (_packageChanges > 0)
                _packageChanges--;
        }

        internal void PrepareForPlace()
        {
            Presentation.PrepareForPlace();
        }

        internal void Apply(in ViewVisualState state, int sortingLayerId)
        {
            Presentation.Apply(in state, sortingLayerId);
        }

        internal void PrepareForStash()
        {
            Presentation.PrepareForStash();
        }

        private void OnDisable()
        {
            ReportLoss(false);
        }

        private void OnDestroy()
        {
            ReportLoss(true);
        }

        // Every report goes to the stage, which coalesces them and decides on the next frame what actually happened.
        private void ReportLoss(bool destroyed)
        {
            if (_sink == null || _packageChanges > 0 || QuitSignal.IsQuitting)
                return;

            _sink.OnHostLost(_instance, destroyed);
        }
    }
}

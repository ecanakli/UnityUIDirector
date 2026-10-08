using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// A fade, scale and offset transition driven by DOTween on unscaled time: shown is alpha 1, scale 1 and the authored position;
    /// hidden is alpha 0, the hidden scale and that position moved by a fraction of the layer rect. Each part can be turned off.
    /// </summary>
    public sealed class TweenTransition : ViewTransition
    {
        [SerializeField, Tooltip("Fades a CanvasGroup on this object, added when missing.")]
        private bool _fade = true;

        [SerializeField, Tooltip("Scales this object's RectTransform.")]
        private bool _scale = true;

        [SerializeField, Tooltip("Uniform scale of the hidden pose.")]
        private float _hiddenScale = 0.85f;

        [SerializeField, Tooltip("Moves this object's anchoredPosition by a fraction of the layer rect.")]
        private bool _offset;

        [SerializeField, Tooltip("Hidden offset as a fraction of the layer rect's size; (0, -1) is one layer height below.")]
        private Vector2 _hiddenOffset = new Vector2(0f, -1f);

        [SerializeField, Min(0f), Tooltip("Seconds of unscaled time per play.")]
        private float _duration = 0.25f;

        [SerializeField, Tooltip("DOTween ease when showing.")]
        private Ease _showEase = Ease.OutCubic;

        [SerializeField, Tooltip("DOTween ease when hiding.")]
        private Ease _hideEase = Ease.InCubic;

        private RectTransform _target;
        private CanvasGroup _group;
        private Vector2 _shownPosition;
        private bool _shownCaptured;

        // Tweened values; the getters read these, so a play never reads a destroyed component.
        private float _alphaValue;
        private float _scaleValue;
        private Vector2 _positionValue;

        // Created once per component, so a play allocates no getter or setter closure.
        private DOGetter<float> _getAlpha;
        private DOSetter<float> _setAlpha;
        private DOGetter<float> _getScale;
        private DOSetter<float> _setScale;
        private DOGetter<Vector2> _getPosition;
        private DOSetter<Vector2> _setPosition;
        private Action<Tween> _sequenceKilled;

        // Held only until its kill callback runs, so a recycled tween is never touched.
        private Sequence _sequence;
        private bool _destroying;
        private int _ownKills;

        /// <summary>Tweens the enabled parts from the opposite end pose to the shown or hidden pose; returns early when ct is cancelled or every part is off.</summary>
        public override UniTask PlayAsync(bool show, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || this == null)
                return UniTask.CompletedTask;

            KillRunning();

            var target = ResolveTarget();
            var fade = _fade && ResolveGroup() != null;
            var scale = _scale && target != null;
            var offset = _offset && target != null;
            if (!fade && !scale && !offset)
                return UniTask.CompletedTask;

            WritePose(!show);
            if (_duration <= 0f)
            {
                WritePose(show);
                return UniTask.CompletedTask;
            }

            EnsureDelegates();
            var ease = show ? _showEase : _hideEase;
            var sequence = DOTween.Sequence();
            if (fade)
                sequence.Join(DOTween.To(_getAlpha, _setAlpha, show ? 1f : 0f, _duration).SetEase(ease));

            if (scale)
                sequence.Join(DOTween.To(_getScale, _setScale, show ? 1f : _hiddenScale, _duration).SetEase(ease));

            if (offset)
            {
                var end = show ? _shownPosition : _shownPosition + HiddenOffset(target);
                sequence.Join(DOTween.To(_getPosition, _setPosition, end, _duration).SetEase(ease));
            }

            // Explicit, so project-wide DOTween defaults (ease, auto-kill) cannot change the result or stall the await.
            sequence.SetEase(Ease.Linear).SetAutoKill(true).SetUpdate(true);
            _sequence = sequence;
            return AwaitSequenceAsync(sequence, ct);
        }

        // A kill made by this component (Snap, a new play, OnDestroy) resumes the caller on the next player-loop step, never inside that call.
        private async UniTask AwaitSequenceAsync(Sequence sequence, CancellationToken ct)
        {
            var kills = _ownKills;
            await TweenAwaiter.AwaitKillAsync(sequence, _sequenceKilled, ct);
            if (_ownKills != kills || _destroying)
                await UniTask.Yield();
        }

        /// <summary>Kills a running sequence, then writes the shown or hidden pose of the enabled parts; synchronous, idempotent and never throws.</summary>
        public override void Snap(bool shown)
        {
            // Snap never throws; adding the fade group can fail on an object that is being destroyed.
            try
            {
                KillRunning();
                if (this == null)
                    return;

                WritePose(shown);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void OnDestroy()
        {
            _destroying = true;
            KillRunning();
        }

        private void KillRunning()
        {
            // Dropped before the kill, so the kill callback finds nothing stale.
            var running = _sequence;
            _sequence = null;
            if (running != null && running.IsActive())
            {
                // Counted before the kill, since the kill callback can resume the awaiting play synchronously.
                _ownKills++;
                running.Kill(false);
            }
        }

        private void OnSequenceKilled(Tween killed)
        {
            if (ReferenceEquals(_sequence, killed))
                _sequence = null;
        }

        private void WritePose(bool shown)
        {
            var target = ResolveTarget();
            if (_fade && ResolveGroup() != null)
                SetAlpha(shown ? 1f : 0f);

            if (_scale && target != null)
                SetScale(shown ? 1f : _hiddenScale);

            if (_offset && target != null)
            {
                CaptureShownPosition(target);
                SetPosition(shown ? _shownPosition : _shownPosition + HiddenOffset(target));
            }
        }

        private float GetAlpha()
        {
            return _alphaValue;
        }

        private void SetAlpha(float value)
        {
            _alphaValue = value;

            // The group may be destroyed mid-play.
            var group = _group;
            if (group != null)
                group.alpha = Mathf.Clamp01(value);
        }

        private float GetScale()
        {
            return _scaleValue;
        }

        private void SetScale(float value)
        {
            _scaleValue = value;
            var target = _target;
            if (target != null)
                target.localScale = new Vector3(value, value, 1f);
        }

        private Vector2 GetPosition()
        {
            return _positionValue;
        }

        private void SetPosition(Vector2 value)
        {
            _positionValue = value;
            var target = _target;
            if (target != null)
                target.anchoredPosition = value;
        }

        private void EnsureDelegates()
        {
            if (_sequenceKilled != null)
                return;

            _getAlpha = GetAlpha;
            _setAlpha = SetAlpha;
            _getScale = GetScale;
            _setScale = SetScale;
            _getPosition = GetPosition;
            _setPosition = SetPosition;
            _sequenceKilled = OnSequenceKilled;
        }

        private void CaptureShownPosition(RectTransform target)
        {
            if (_shownCaptured)
                return;

            _shownPosition = target.anchoredPosition;
            _shownCaptured = true;
        }

        // Measured at play time from the view's parent (the layer rect), so a resized layer is honoured; never Screen size.
        private Vector2 HiddenOffset(RectTransform target)
        {
            var layer = target.parent as RectTransform;
            var size = layer != null ? layer.rect.size : target.rect.size;
            return Vector2.Scale(_hiddenOffset, size);
        }

        private RectTransform ResolveTarget()
        {
            if (_target == null && this != null)
                _target = transform as RectTransform;

            return _target;
        }

        private CanvasGroup ResolveGroup()
        {
            if (_group == null && this != null)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null)
                    _group = gameObject.AddComponent<CanvasGroup>();
            }

            return _group;
        }
    }
}

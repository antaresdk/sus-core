using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Handle for a playing <see cref="SusMotion"/> (or a stagger composite).
    /// Stopping steals control from the target(s).
    /// </summary>
    public readonly struct SusMotionHandle
    {
        readonly SusMotion _motion;
        readonly Func<bool> _isPlaying;
        readonly Action<bool> _stop;

        internal SusMotionHandle(SusMotion motion)
        {
            _motion = motion;
            _isPlaying = null;
            _stop = null;
        }

        internal SusMotionHandle(Func<bool> isPlaying, Action<bool> stop)
        {
            _motion = null;
            _isPlaying = isPlaying;
            _stop = stop;
        }

        /// <summary>True while the scheduled tick is active.</summary>
        public bool IsPlaying =>
            _motion != null ? _motion.IsPlaying : (_isPlaying?.Invoke() ?? false);

        /// <summary>Stop this play; optionally apply the motion's restore mode.</summary>
        public void Stop(bool applyRestore = true)
        {
            if (_motion != null) _motion.Stop(applyRestore);
            else _stop?.Invoke(applyRestore);
        }
    }

    /// <summary>
    /// Code-driven tween builder for UITK inline styles (opacity / scale / translate / rotate / background-color).
    /// Tick model: <c>schedule.Execute(…).Every(16)</c> with fixed <c>+0.016s</c> (not wall-clock alone).
    /// One active Play per target: a new Play stops the previous with that play's restore mode.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SusTransition"/> (USS enter/leave phases). Color animates
    /// <c>style.backgroundColor</c> only.
    /// </remarks>
    public sealed class SusMotion
    {
        enum PropKind : byte
        {
            Opacity,
            Scale,
            Translate,
            Rotate,
            Color,
        }

        sealed class Step
        {
            public PropKind Kind;
            public float Duration;
            public SusEase Ease;
            public float FromOpacity, ToOpacity;
            public Vector2 FromScale, ToScale;
            public Vector2 FromTranslate, ToTranslate;
            public float FromRotate, ToRotate;
            public Color FromColor, ToColor;
        }

        sealed class Group
        {
            public readonly List<Step> Steps = new List<Step>(4);
            public bool TogetherWithPrevious;
            public int Repeat = 1; // 1 = once; <=0 = forever
        }

        struct StyleSnapshot
        {
            public StyleFloat Opacity;
            public StyleScale Scale;
            public StyleTranslate Translate;
            public StyleRotate Rotate;
            public StyleColor BackgroundColor;
            public bool HasOpacity, HasScale, HasTranslate, HasRotate, HasColor;
        }

        struct GroupTiming
        {
            public float Start;
            public float OneShotDuration;
            public int Repeat;
        }

        static readonly Dictionary<VisualElement, SusMotion> ActiveByTarget =
            new Dictionary<VisualElement, SusMotion>();

#if UNITY_EDITOR
        // With Domain Reload disabled ActiveByTarget survives leaving Play Mode (T-1103): it would
        // keep VisualElement references from the destroyed panel of the previous session pinned
        // alive via a static dictionary, and a forever-Repeat motion (Play() with Repeat<=0) never
        // reaches CompleteInternal() on its own — its schedule.Execute(Tick).Every(16) item and
        // dictionary entry would outlive the panel that created them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ActiveByTarget.Clear();
        }
#endif

        readonly VisualElement _target;
        readonly List<Group> _groups = new List<Group>(4);
        Group _current;
        float _delay;
        SusRestoreMode _restore = SusRestoreMode.Keep;

        bool _seedOpacity, _seedScale, _seedTranslate, _seedRotate;
        float _fromOpacity;
        Vector2 _fromScale = Vector2.one;
        Vector2 _fromTranslate;
        float _fromRotate;

        IVisualElementScheduledItem _item;
        float _elapsed;
        Action _onComplete;
        StyleSnapshot _snapshot;
        readonly HashSet<PropKind> _written = new HashSet<PropKind>();
        List<GroupTiming> _timings;
        float _totalDuration;
        bool _playing;
        bool _forever;
        bool _detachRegistered;

        SusMotion(VisualElement target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _current = new Group();
            _groups.Add(_current);
        }

        /// <summary>Begin a motion builder for <paramref name="target"/>.</summary>
        public static SusMotion On(VisualElement target) => new SusMotion(target);

        internal bool IsPlaying => _playing;

        public SusMotion Opacity(float to, float durationS, SusEase ease = SusEase.QuadOut)
        {
            EnsureCurrent();
            _current.Steps.Add(new Step
            {
                Kind = PropKind.Opacity,
                Duration = Mathf.Max(0f, durationS),
                Ease = ease,
                ToOpacity = to,
            });
            return this;
        }

        public SusMotion Scale(float to, float durationS, SusEase ease = SusEase.QuadOut) =>
            Scale(new Vector2(to, to), durationS, ease);

        public SusMotion Scale(Vector2 to, float durationS, SusEase ease = SusEase.QuadOut)
        {
            EnsureCurrent();
            _current.Steps.Add(new Step
            {
                Kind = PropKind.Scale,
                Duration = Mathf.Max(0f, durationS),
                Ease = ease,
                ToScale = to,
            });
            return this;
        }

        public SusMotion Translate(Vector2 toPx, float durationS, SusEase ease = SusEase.QuadOut)
        {
            EnsureCurrent();
            _current.Steps.Add(new Step
            {
                Kind = PropKind.Translate,
                Duration = Mathf.Max(0f, durationS),
                Ease = ease,
                ToTranslate = toPx,
            });
            return this;
        }

        public SusMotion Rotate(float degrees, float durationS, SusEase ease = SusEase.QuadOut)
        {
            EnsureCurrent();
            _current.Steps.Add(new Step
            {
                Kind = PropKind.Rotate,
                Duration = Mathf.Max(0f, durationS),
                Ease = ease,
                ToRotate = degrees,
            });
            return this;
        }

        /// <summary>Animate <c>style.backgroundColor</c> (background only).</summary>
        public SusMotion Color(StyleColor to, float durationS, SusEase ease = SusEase.QuadOut)
        {
            EnsureCurrent();
            var c = to.keyword == StyleKeyword.Undefined || to.keyword == StyleKeyword.Null
                ? UnityEngine.Color.clear
                : to.value;
            _current.Steps.Add(new Step
            {
                Kind = PropKind.Color,
                Duration = Mathf.Max(0f, durationS),
                Ease = ease,
                ToColor = c,
            });
            return this;
        }

        public SusMotion FromOpacity(float from)
        {
            _seedOpacity = true;
            _fromOpacity = from;
            return this;
        }

        public SusMotion FromScale(Vector2 from)
        {
            _seedScale = true;
            _fromScale = from;
            return this;
        }

        public SusMotion FromTranslate(Vector2 from)
        {
            _seedTranslate = true;
            _fromTranslate = from;
            return this;
        }

        public SusMotion FromRotate(float degrees)
        {
            _seedRotate = true;
            _fromRotate = degrees;
            return this;
        }

        public SusMotion Delay(float seconds)
        {
            _delay = Mathf.Max(0f, seconds);
            return this;
        }

        /// <summary>Next property-step group shares start time with the previous group.</summary>
        public SusMotion Together()
        {
            CloseGroup(togetherWithPrevious: true);
            return this;
        }

        /// <summary>Next property-step group waits until the previous group finishes (default).</summary>
        public SusMotion Sequence()
        {
            CloseGroup(togetherWithPrevious: false);
            return this;
        }

        /// <summary>
        /// Repeat the last group. <paramref name="times"/> ≤ 0 means forever until <see cref="Stop"/>.
        /// </summary>
        public SusMotion Repeat(int times)
        {
            EnsureCurrent();
            if (_current.Steps.Count == 0 && _groups.Count > 1)
            {
                _groups[_groups.Count - 2].Repeat = times;
            }
            else
            {
                _current.Repeat = times;
                _current = new Group { TogetherWithPrevious = false };
                _groups.Add(_current);
            }
            return this;
        }

        public SusMotion Restore(SusRestoreMode mode)
        {
            _restore = mode;
            return this;
        }

        /// <summary>Start playback. Steals any prior Play on the same target (stop + restore of the prior).</summary>
        public SusMotionHandle Play(Action onComplete = null)
        {
            if (_target == null) return default;

            if (ActiveByTarget.TryGetValue(_target, out var prior) && prior != this && prior._playing)
            {
                SusLog.Verbose("[SusMotion] steal play on target — stopping prior");
                prior.Stop(applyRestore: true);
            }

            if (_current != null && _current.Steps.Count == 0 && _groups.Count > 1)
                _groups.RemoveAt(_groups.Count - 1);

            CaptureSnapshot();
            ApplySeeds();
            ResolveFromValues();

            _timings = BuildTimings();
            _forever = false;
            _totalDuration = 0f;
            for (int i = 0; i < _timings.Count; i++)
            {
                var tm = _timings[i];
                if (_groups[i].Steps.Count == 0) continue;
                if (tm.Repeat <= 0)
                {
                    _forever = true;
                    _totalDuration = float.PositiveInfinity;
                    break;
                }
                _totalDuration = Mathf.Max(_totalDuration, tm.Start + tm.OneShotDuration * tm.Repeat);
            }

            _onComplete = onComplete;
            _elapsed = 0f;
            _written.Clear();
            // Re-apply seeds as written
            if (_seedOpacity) _written.Add(PropKind.Opacity);
            if (_seedScale) _written.Add(PropKind.Scale);
            if (_seedTranslate) _written.Add(PropKind.Translate);
            if (_seedRotate) _written.Add(PropKind.Rotate);

            _playing = true;
            ActiveByTarget[_target] = this;

            // T-1103: a target that detaches mid-play (element removed/pooled) must not keep a
            // forever-Repeat motion ticking forever on it — stop and unregister on detach.
            if (!_detachRegistered)
            {
                _target.RegisterCallback<DetachFromPanelEvent>(OnTargetDetached);
                _detachRegistered = true;
            }

            _item?.Pause();
            _item = _target.schedule.Execute(Tick).Every(16);

            if (!_forever && _totalDuration <= 0f)
            {
                ApplyAllEnds();
                CompleteInternal();
            }

            return new SusMotionHandle(this);
        }

        public void Stop(bool applyRestore = true)
        {
            if (!_playing) return;
            SusLog.Verbose("[SusMotion] stop");
            _item?.Pause();
            _item = null;
            _playing = false;
            RemoveDetachHandler();
            if (ActiveByTarget.TryGetValue(_target, out var cur) && cur == this)
                ActiveByTarget.Remove(_target);

            if (applyRestore)
                ApplyRestore();
            _onComplete = null;
        }

        /// <summary>
        /// T-1103: target left the panel while this motion was playing. A forever-Repeat chain
        /// (Repeat &lt;= 0) never calls CompleteInternal() on its own, so without this the
        /// schedule item keeps ticking a detached element and ActiveByTarget pins it alive.
        /// </summary>
        void OnTargetDetached(DetachFromPanelEvent evt) => Stop();

        void RemoveDetachHandler()
        {
            if (!_detachRegistered) return;
            _target?.UnregisterCallback<DetachFromPanelEvent>(OnTargetDetached);
            _detachRegistered = false;
        }

        void Tick()
        {
            if (!_playing || _target == null) return;

            _elapsed += 0.016f;
            ApplyAtTime(_elapsed);

            if (!_forever && _elapsed >= _totalDuration - 1e-5f)
            {
                ApplyAllEnds();
                CompleteInternal();
            }
        }

        /// <summary>
        /// Test hook: one fixed +0.016s tick (same as schedule Every(16) path).
        /// Pauses the UITK schedule item so EditMode tests are deterministic.
        /// </summary>
        internal void AdvanceFixedTickForTests()
        {
            _item?.Pause();
            Tick();
        }

        void CompleteInternal()
        {
            _item?.Pause();
            _item = null;
            _playing = false;
            RemoveDetachHandler();
            if (ActiveByTarget.TryGetValue(_target, out var cur) && cur == this)
                ActiveByTarget.Remove(_target);

            ApplyRestore();
            var cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }

        void EnsureCurrent()
        {
            if (_current == null)
            {
                _current = new Group();
                _groups.Add(_current);
            }
        }

        void CloseGroup(bool togetherWithPrevious)
        {
            if (_current == null || _current.Steps.Count == 0)
            {
                if (_current != null)
                    _current.TogetherWithPrevious = togetherWithPrevious;
                else
                {
                    _current = new Group { TogetherWithPrevious = togetherWithPrevious };
                    _groups.Add(_current);
                }
                return;
            }

            _current = new Group { TogetherWithPrevious = togetherWithPrevious };
            _groups.Add(_current);
        }

        void ResolveFromValues()
        {
            float? lastOpacity = _seedOpacity ? _fromOpacity : (float?)null;
            Vector2? lastScale = _seedScale ? _fromScale : (Vector2?)null;
            Vector2? lastTranslate = _seedTranslate ? _fromTranslate : (Vector2?)null;
            float? lastRotate = _seedRotate ? _fromRotate : (float?)null;
            Color? lastColor = null;

            foreach (var g in _groups)
            {
                foreach (var s in g.Steps)
                {
                    switch (s.Kind)
                    {
                        case PropKind.Opacity:
                            s.FromOpacity = lastOpacity ?? ReadOpacity();
                            lastOpacity = s.ToOpacity;
                            break;
                        case PropKind.Scale:
                            s.FromScale = lastScale ?? ReadScale();
                            lastScale = s.ToScale;
                            break;
                        case PropKind.Translate:
                            s.FromTranslate = lastTranslate ?? ReadTranslate();
                            lastTranslate = s.ToTranslate;
                            break;
                        case PropKind.Rotate:
                            s.FromRotate = lastRotate ?? ReadRotate();
                            lastRotate = s.ToRotate;
                            break;
                        case PropKind.Color:
                            s.FromColor = lastColor ?? ReadBackgroundColor();
                            lastColor = s.ToColor;
                            break;
                    }
                }
            }
        }

        void ApplySeeds()
        {
            if (_seedOpacity)
                _target.style.opacity = _fromOpacity;
            if (_seedScale)
                WriteScale(_fromScale);
            if (_seedTranslate)
                WriteTranslate(_fromTranslate);
            if (_seedRotate)
                WriteRotate(_fromRotate);
        }

        void CaptureSnapshot()
        {
            _snapshot = default;
            void Mark(PropKind kind)
            {
                switch (kind)
                {
                    case PropKind.Opacity:
                        if (!_snapshot.HasOpacity)
                        {
                            _snapshot.Opacity = _target.style.opacity;
                            _snapshot.HasOpacity = true;
                        }
                        break;
                    case PropKind.Scale:
                        if (!_snapshot.HasScale)
                        {
                            _snapshot.Scale = _target.style.scale;
                            _snapshot.HasScale = true;
                        }
                        break;
                    case PropKind.Translate:
                        if (!_snapshot.HasTranslate)
                        {
                            _snapshot.Translate = _target.style.translate;
                            _snapshot.HasTranslate = true;
                        }
                        break;
                    case PropKind.Rotate:
                        if (!_snapshot.HasRotate)
                        {
                            _snapshot.Rotate = _target.style.rotate;
                            _snapshot.HasRotate = true;
                        }
                        break;
                    case PropKind.Color:
                        if (!_snapshot.HasColor)
                        {
                            _snapshot.BackgroundColor = _target.style.backgroundColor;
                            _snapshot.HasColor = true;
                        }
                        break;
                }
            }

            foreach (var g in _groups)
                foreach (var s in g.Steps)
                    Mark(s.Kind);

            if (_seedOpacity) Mark(PropKind.Opacity);
            if (_seedScale) Mark(PropKind.Scale);
            if (_seedTranslate) Mark(PropKind.Translate);
            if (_seedRotate) Mark(PropKind.Rotate);
        }

        List<GroupTiming> BuildTimings()
        {
            var list = new List<GroupTiming>(_groups.Count);
            float lastGroupStart = _delay;
            float lastGroupEnd = _delay;
            bool any = false;

            foreach (var g in _groups)
            {
                if (g.Steps.Count == 0)
                {
                    list.Add(default);
                    continue;
                }

                float groupDur = 0f;
                foreach (var s in g.Steps)
                    groupDur = Mathf.Max(groupDur, s.Duration);

                float start;
                if (!any)
                    start = _delay;
                else if (g.TogetherWithPrevious)
                    start = lastGroupStart;
                else
                    start = lastGroupEnd;

                list.Add(new GroupTiming
                {
                    Start = start,
                    OneShotDuration = groupDur,
                    Repeat = g.Repeat,
                });

                lastGroupStart = start;
                float span = g.Repeat <= 0 ? groupDur : groupDur * Mathf.Max(1, g.Repeat);
                lastGroupEnd = start + span;
                any = true;
            }

            return list;
        }

        void ApplyAtTime(float time)
        {
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                var g = _groups[gi];
                if (g.Steps.Count == 0) continue;
                var tm = _timings[gi];
                float local = time - tm.Start;
                if (local < 0f) continue;

                float cycle = tm.OneShotDuration;
                float elapsedInCycle;
                if (cycle <= 0f)
                {
                    elapsedInCycle = float.MaxValue;
                }
                else if (tm.Repeat <= 0)
                {
                    elapsedInCycle = local % cycle;
                }
                else if (local >= cycle * tm.Repeat - 1e-5f)
                {
                    elapsedInCycle = cycle;
                }
                else
                {
                    elapsedInCycle = local % cycle;
                }

                foreach (var s in g.Steps)
                {
                    float st = s.Duration <= 0f ? 1f : Mathf.Clamp01(elapsedInCycle / s.Duration);
                    float e = SusEaseUtil.Evaluate(s.Ease, st);
                    ApplyStep(s, e);
                }
            }
        }

        void ApplyAllEnds()
        {
            foreach (var g in _groups)
                foreach (var s in g.Steps)
                    ApplyStep(s, 1f);
        }

        void ApplyStep(Step s, float eased01)
        {
            switch (s.Kind)
            {
                case PropKind.Opacity:
                    _target.style.opacity = Mathf.LerpUnclamped(s.FromOpacity, s.ToOpacity, eased01);
                    _written.Add(PropKind.Opacity);
                    break;
                case PropKind.Scale:
                    WriteScale(Vector2.LerpUnclamped(s.FromScale, s.ToScale, eased01));
                    _written.Add(PropKind.Scale);
                    break;
                case PropKind.Translate:
                    WriteTranslate(Vector2.LerpUnclamped(s.FromTranslate, s.ToTranslate, eased01));
                    _written.Add(PropKind.Translate);
                    break;
                case PropKind.Rotate:
                    WriteRotate(Mathf.LerpUnclamped(s.FromRotate, s.ToRotate, eased01));
                    _written.Add(PropKind.Rotate);
                    break;
                case PropKind.Color:
                    _target.style.backgroundColor = UnityEngine.Color.LerpUnclamped(s.FromColor, s.ToColor, eased01);
                    _written.Add(PropKind.Color);
                    break;
            }
        }

        void ApplyRestore()
        {
            if (_restore == SusRestoreMode.Keep) return;

            foreach (var kind in _written)
            {
                if (_restore == SusRestoreMode.KeywordNull)
                {
                    switch (kind)
                    {
                        case PropKind.Opacity: _target.style.opacity = StyleKeyword.Null; break;
                        case PropKind.Scale: _target.style.scale = StyleKeyword.Null; break;
                        case PropKind.Translate: _target.style.translate = StyleKeyword.Null; break;
                        case PropKind.Rotate: _target.style.rotate = StyleKeyword.Null; break;
                        case PropKind.Color: _target.style.backgroundColor = StyleKeyword.Null; break;
                    }
                }
                else
                {
                    switch (kind)
                    {
                        case PropKind.Opacity:
                            if (_snapshot.HasOpacity) _target.style.opacity = _snapshot.Opacity;
                            break;
                        case PropKind.Scale:
                            if (_snapshot.HasScale) _target.style.scale = _snapshot.Scale;
                            break;
                        case PropKind.Translate:
                            if (_snapshot.HasTranslate) _target.style.translate = _snapshot.Translate;
                            break;
                        case PropKind.Rotate:
                            if (_snapshot.HasRotate) _target.style.rotate = _snapshot.Rotate;
                            break;
                        case PropKind.Color:
                            if (_snapshot.HasColor) _target.style.backgroundColor = _snapshot.BackgroundColor;
                            break;
                    }
                }
            }
        }

        float ReadOpacity()
        {
            var inline = _target.style.opacity;
            if (inline.keyword == StyleKeyword.Undefined || inline.keyword == StyleKeyword.Null)
                return _target.resolvedStyle.opacity;
            return inline.value;
        }

        Vector2 ReadScale()
        {
            var inline = _target.style.scale;
            if (inline.keyword == StyleKeyword.Undefined || inline.keyword == StyleKeyword.Null)
            {
                var v = _target.resolvedStyle.scale.value;
                return new Vector2(v.x, v.y);
            }
            var s = inline.value.value;
            return new Vector2(s.x, s.y);
        }

        Vector2 ReadTranslate()
        {
            var inline = _target.style.translate;
            if (inline.keyword == StyleKeyword.Undefined || inline.keyword == StyleKeyword.Null)
            {
                var t = _target.resolvedStyle.translate;
                return new Vector2(t.x, t.y);
            }
            var tr = inline.value;
            return new Vector2(tr.x.value, tr.y.value);
        }

        float ReadRotate()
        {
            var inline = _target.style.rotate;
            if (inline.keyword == StyleKeyword.Undefined || inline.keyword == StyleKeyword.Null)
                return _target.resolvedStyle.rotate.angle.value;
            return inline.value.angle.value;
        }

        Color ReadBackgroundColor()
        {
            var inline = _target.style.backgroundColor;
            if (inline.keyword == StyleKeyword.Undefined || inline.keyword == StyleKeyword.Null)
                return _target.resolvedStyle.backgroundColor;
            return inline.value;
        }

        void WriteScale(Vector2 v) =>
            _target.style.scale = new Scale(new Vector3(v.x, v.y, 1f));

        void WriteTranslate(Vector2 v) =>
            _target.style.translate = new Translate(v.x, v.y);

        void WriteRotate(float degrees) =>
            _target.style.rotate = new Rotate(Angle.Degrees(degrees));
    }
}

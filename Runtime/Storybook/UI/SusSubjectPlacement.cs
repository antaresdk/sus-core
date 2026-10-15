using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook.UI
{
    /// <summary>
    /// How the storybook frames a mounted subject - the live instance on the stage, the instance
    /// in a matrix box (card T-4522, POOL-01 R1: U-041, U-050…U-075). Everything here is done to
    /// the FRAME, an element the engine owns: the subject - a product instance - carries no class
    /// of the shell (T-3421) and no style written by it.
    ///
    /// A subject that lays out <c>position: absolute</c> - a screen or a HUD root over "its parent"
    /// (<c>.sus-screen</c>, <c>.sus-fill</c>), an overlay - takes no room in its frame. The stage's
    /// frame is the viewport content, as tall as its in-flow content: it stayed 0 px tall and
    /// clipped the subject to nothing, and 26 stories showed an empty "live instance" on the site
    /// and in the Editor alike (measured: game-select 507x420 inside a 507x0 viewport content). The
    /// stories that did render had wrapped their screen in a relative box of their own.
    ///
    /// So on the subject's first layout the frame is marked (<see cref="FrameFillClass"/>: at least
    /// as tall as the visible canvas, which a filling screen then fills) and, on the next one, held
    /// open to the size the subject actually took - a screen whose story asks for 420 px gets 420
    /// px and scrolls inside the canvas (D1) instead of being cut at the canvas edge. USS has no
    /// number for "as tall as this absolute child turned out to be", hence the one measured write
    /// (the matrix sizes its cells the same way).
    ///
    /// A subject that is 0 px tall while in flow (every child of it absolute) is the story's to
    /// size - the engine cannot know what height a parallax stack wants - and is only REPORTED
    /// (<see cref="Empty"/>).
    ///
    /// And one rule decided at mount: when the subject's role is <c>control</c> or <c>group</c> (a
    /// button, a chip, a menu button, a tab strip) the frame lays its children out at their
    /// natural width (<see cref="FrameNaturalClass"/>) instead of stretching them across the canvas
    /// - "buttons now huge, now small" (U-004, U-041).
    /// </summary>
    public sealed class SusSubjectPlacement
    {
        /// <summary>On the frame while its subject is absolute: at least as tall as the visible canvas.</summary>
        public const string FrameFillClass = "sb-subject-frame--fill";

        /// <summary>On the frame while its subject is a control or a group: natural width.</summary>
        public const string FrameNaturalClass = "sb-subject-frame--natural";

        readonly VisualElement _subject;
        readonly VisualElement _frame;
        bool _watching;
        bool _marked;

        SusSubjectPlacement(VisualElement subject, VisualElement frame)
        {
            _subject = subject;
            _frame = frame;
        }

        /// <summary>
        /// Starts framing <paramref name="subject"/> in <paramref name="frame"/>.
        /// <paramref name="component"/> is the instance whose role decides the width rule - the
        /// subject itself, or the instance inside a host the story declared (then the host is the
        /// subject and the rule does not apply: the story placed it). Returns null when either is
        /// null.
        /// </summary>
        public static SusSubjectPlacement Watch(VisualElement subject, VisualElement frame, SusComponent component)
        {
            if (subject == null || frame == null) return null;
            Reset(frame);
            var placement = new SusSubjectPlacement(subject, frame);
            if (component != null && subject == component && KeepsNaturalWidth(SusStateRoles.RoleOf(component)))
                frame.AddToClassList(FrameNaturalClass);
            subject.RegisterCallback<GeometryChangedEvent>(placement.OnLaidOut);
            placement._watching = true;
            return placement;
        }

        /// <summary>
        /// Roles shown at natural width: a control and a group of controls. Measured over all 175
        /// stories before choosing: at natural width these stay readable (a button 109 px, a chip
        /// group 349 px), while a showcase scene shrank to 180 px and a screen scene to 0 - those
        /// keep the canvas width.
        /// </summary>
        public static bool KeepsNaturalWidth(string role) =>
            role == SusStateRoles.Control || role == SusStateRoles.Group;

        /// <summary>The subject being framed.</summary>
        public VisualElement Subject => _subject;

        /// <summary>The frame it is framed in.</summary>
        public VisualElement Frame => _frame;

        /// <summary>True when the subject laid out absolute and the frame holds room for it.</summary>
        public bool Framed => _frame.ClassListContains(FrameFillClass);

        /// <summary>True when the subject laid out in flow with no height at all - the story's to size.</summary>
        public bool Empty { get; private set; }

        /// <summary>True while the first layouts are still being judged.</summary>
        public bool Watching => _watching;

        /// <summary>Stops judging and gives the frame back as it was.</summary>
        public void Stop()
        {
            StopWatching();
            Reset(_frame);
        }

        static void Reset(VisualElement frame)
        {
            frame.RemoveFromClassList(FrameFillClass);
            frame.RemoveFromClassList(FrameNaturalClass);
            frame.style.minHeight = StyleKeyword.Null;
            frame.style.minWidth = StyleKeyword.Null;
        }

        void StopWatching()
        {
            if (!_watching) return;
            _watching = false;
            _subject.UnregisterCallback<GeometryChangedEvent>(OnLaidOut);
        }

        void OnLaidOut(GeometryChangedEvent evt)
        {
            if (!_watching || evt.target != _subject) return;

            if (_subject.resolvedStyle.position != Position.Absolute)
            {
                Empty = _subject.layout.height < 1f;
                StopWatching();
                return;
            }

            // Hold the frame open to what the subject took, so a subject taller or wider than the
            // canvas scrolls instead of being cut. Written on the first pass already: a screen
            // whose own min-height is bigger than the canvas does not change size when the frame
            // is marked, and no second event would come.
            HoldOpen();
            if (!_marked)
            {
                // First pass: give a filling subject something to fill, and look once more - a
                // subject sized as a percentage of its frame takes the new room on the next pass.
                _marked = true;
                _frame.AddToClassList(FrameFillClass);
                return;
            }
            // Second pass: done. A further write would chase a subject that follows its frame.
            StopWatching();
        }

        void HoldOpen()
        {
            var size = _subject.layout.size;
            // sus:uss-impossible measured size of a live absolute subject, from this layout pass
            if (size.y > 0f) _frame.style.minHeight = Mathf.Ceil(size.y);
            // Wider only: the stage's frame already spans the canvas (min-width: 100%), and an
            // inline value narrower than that would replace the percentage, not add to it.
            // sus:uss-impossible same measured size, same pass
            if (size.x > _frame.layout.width + 0.5f) _frame.style.minWidth = Mathf.Ceil(size.x);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    public abstract partial class SusComponent
    {
        // ─── Slot Infrastructure ────────────────────────────────────────

        private Dictionary<string, List<VisualElement>> _slotContent = new();
        private Dictionary<string, VisualElement> _slotContainers = new();
        private Dictionary<string, List<Func<SlotPropMap, VisualElement>>> _scopedSlotBuilders = new();
        private Dictionary<string, SlotPropMap> _scopedSlotProps = new();

        /// <summary>
        /// Registers slot content provided by the parent component.
        /// Called by generated Build() code for v-slot / v-slot:name.
        /// </summary>
        protected void RegisterSlotContent(string name, VisualElement content,
            Func<Dictionary<string, object>, VisualElement> builder)
        {
            if (content == null && builder == null) return;
            if (string.IsNullOrEmpty(name)) name = "default";

            if (content != null)
            {
                if (!_slotContent.TryGetValue(name, out var list))
                {
                    list = new List<VisualElement>();
                    _slotContent[name] = list;
                }
                if (list.Contains(content))
                    OnDuplicateSlot?.Invoke(name, "duplicate RegisterSlotContent");
                else
                    list.Add(content);
            }

            // Legacy builder signature (Dictionary) — wrap into SlotPropMap factory
            if (builder != null)
            {
                RegisterScopedSlot(name, props => builder(props));
            }
        }

        /// <summary>
        /// Registers a scoped-slot factory (F3). The factory receives props published
        /// by the child via <see cref="ProvideSlotProps"/> / <c>&lt;slot :item="..."&gt;</c>.
        /// </summary>
        protected void RegisterScopedSlot(string name, Func<SlotPropMap, VisualElement> factory)
        {
            if (factory == null) return;
            if (string.IsNullOrEmpty(name)) name = "default";

            if (!_scopedSlotBuilders.TryGetValue(name, out var list))
            {
                list = new List<Func<SlotPropMap, VisualElement>>();
                _scopedSlotBuilders[name] = list;
            }
            if (list.Contains(factory))
                OnDuplicateSlot?.Invoke(name, "duplicate RegisterScopedSlot");
            else
                list.Add(factory);
        }

        /// <summary>
        /// Publishes scoped props for a named slot (called from generated code for
        /// <c>&lt;slot :item="expr"&gt;</c>). Must be called before <see cref="BuildSlot"/>.
        /// </summary>
        protected void ProvideSlotProps(string name, SlotPropMap props)
        {
            if (string.IsNullOrEmpty(name)) name = "default";
            _scopedSlotProps[name] = props ?? new SlotPropMap();
        }

        /// <summary>
        /// Convenience: publish a single scoped prop.
        /// </summary>
        protected void ProvideSlotProp(string name, string propName, object value)
        {
            if (string.IsNullOrEmpty(name)) name = "default";
            if (!_scopedSlotProps.TryGetValue(name, out var map))
            {
                map = new SlotPropMap();
                _scopedSlotProps[name] = map;
            }
            map[propName] = value;
        }

        /// <summary>USS class present on every slot container (T-530).</summary>
        public const string SlotContainerClass = "sus-slot";

        /// <summary>
        /// Returns the USS modifier class of a named slot container:
        /// <c>sus-slot--&lt;name&gt;</c> (e.g. <c>sus-slot--append</c>, <c>sus-slot--default</c>).
        /// </summary>
        public static string SlotContainerClassFor(string name)
            => SlotContainerClass + "--" + (string.IsNullOrEmpty(name) ? "default" : name);

        /// <summary>
        /// Returns the container element where slot content should be projected.
        /// Created lazily — first call per slot name creates a VisualElement
        /// placeholder that will receive projected content.
        /// The container carries the stable classes <c>sus-slot sus-slot--&lt;name&gt;</c>
        /// (T-530): a <c>&lt;slot&gt;</c> compiles to this extra element nested INSIDE the
        /// author's wrapper, so a row-direction wrapper lays out only the container, not the
        /// projected children. Component USS targets the container explicitly:
        /// <c>.my-wrapper &gt; .sus-slot { flex-direction: row; }</c>. The container itself
        /// adds no layout of its own (UI Toolkit column defaults) — see
        /// <c>_global.uss</c> for the shared <c>.sus-slot</c> rule.
        /// </summary>
        protected VisualElement GetSlotContainer(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "default";

            if (!_slotContainers.TryGetValue(name, out var container))
            {
                container = new VisualElement { name = "slot-" + name };
                container.AddToClassList(SlotContainerClass);
                container.AddToClassList(SlotContainerClassFor(name));
                _slotContainers[name] = container;
            }
            return container;
        }

        /// <summary>
        /// Projects registered slot content into the container.
        /// Prefers scoped factories (F3) when present; otherwise moves static content.
        /// If wrapper is provided, it wraps each projected element.
        /// </summary>
        protected void BuildSlot(string name, Func<VisualElement, VisualElement> wrapper,
            VisualElement container)
        {
            if (container == null) return;
            if (string.IsNullOrEmpty(name)) name = "default";

            _scopedSlotProps.TryGetValue(name, out var props);
            props ??= new SlotPropMap();

            // Scoped factories take priority (parent provided v-slot with scope)
            if (_scopedSlotBuilders.TryGetValue(name, out var factories) && factories.Count > 0)
            {
                container.Clear();
                foreach (var factory in factories)
                {
                    var built = factory(props);
                    if (built == null) continue;
                    var toAdd = wrapper != null ? wrapper(built) : built;
                    container.Add(toAdd);
                }
                return;
            }

            if (_slotContent.TryGetValue(name, out var list) && list.Count > 0)
            {
                // Only clear fallback content when parent provides slot content
                container.Clear();

                foreach (var content in list)
                {
                    var clone = CloneSlotContent(content);
                    if (clone == null) continue;

                    var toAdd = wrapper != null ? wrapper(clone) : clone;
                    container.Add(toAdd);
                }
            }
        }

        /// <summary>
        /// Clones slot content recursively. Since VisualElement doesn't
        /// support Copy/Paste, we re-parent the content (it gets removed
        /// from the parent's hierarchy and placed into the child's slot).
        /// This is the standard Vue slot projection model.
        /// </summary>
        private static VisualElement CloneSlotContent(VisualElement source)
        {
            if (source == null) return null;

            // In Vue-like slot projection, content is MOVED (not copied)
            // from the parent scope into the child's slot container.
            source.RemoveFromHierarchy();
            return source;
        }

        /// <summary>
        /// Public accessor for adding runtime content to a named slot.
        /// Use after Build() has run (e.g. in OnOpened callback or via schedule.Execute).
        /// </summary>
        public VisualElement Slot(string name)
        {
            return GetSlotContainer(name);
        }
    }
}

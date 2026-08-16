using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Reactive binding helpers (analogous to Vue directives: v-if, v-show, :text, :class, v-for).
    /// All bindings use ReactiveEffect — auto-subscribe to Prop/Computed and re-apply on change.
    /// Subscriptions are automatically cleaned up on component detach via DisposeAllBindings.
    /// </summary>
    public abstract partial class SusComponent
    {
        /// <summary>
        /// v-if: add/remove element from visual tree reactively.
        /// Parent AND sibling index are remembered on hide and used to re-insert on show,
        /// so the element returns to its original template position instead of jumping
        /// to the end of the parent's children.
        /// </summary>
        /// <remarks>
        /// Sharq currently emits <c>BindVisibility</c> before <c>parent.Add(el)</c>.
        /// When the condition is false on first run, <paramref name="el"/> has no parent yet —
        /// we force <see cref="DisplayStyle.None"/> so the element stays hidden after the
        /// subsequent Add (e.g. SusSelect search field with Searchable=false).
        ///
        /// Bug history: re-adding via <c>parent.Add(el)</c> always appended at the END of the
        /// children list, regardless of where the element was in the template. Any v-if element
        /// that starts hidden (falsy prop at Mounted) and is later revealed — e.g. a leading
        /// caption Label bound to a Prop set after construction via a Bind() helper — would jump
        /// from its authored position (first child) to last child, landing after siblings that
        /// should visually follow it (T-421, 2026-08-13: SusWedgeSlider label rendered BELOW the
        /// control instead of above). Fix: remember the sibling index at removal time and
        /// <see cref="VisualElement.Insert"/> back at that index (clamped to current child count)
        /// on re-show.
        /// </remarks>
        protected WatchHandle BindVisibility(VisualElement el, Func<bool> getter)
        {
            if (el == null) throw new ArgumentNullException(nameof(el));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            VisualElement rememberedParent = null;
            int rememberedIndex = -1;

            var h = ReactiveEffect(() =>
            {
                bool show = getter();
                if (show)
                {
                    if (el.style.display == DisplayStyle.None)
                        el.style.display = StyleKeyword.Null;

                    if (el.parent == null && rememberedParent != null)
                    {
                        var insertAt = rememberedIndex >= 0
                            ? Math.Min(rememberedIndex, rememberedParent.childCount)
                            : rememberedParent.childCount;
                        rememberedParent.Insert(insertAt, el);
                    }
                }
                else
                {
                    if (el.parent != null)
                    {
                        rememberedParent = el.parent;
                        rememberedIndex = rememberedParent.IndexOf(el);
                        el.RemoveFromHierarchy();
                    }
                    else
                    {
                        // Not parented yet (generator BindVisibility-before-Add) — hide until removed.
                        el.style.display = DisplayStyle.None;
                    }
                }
            });

            TrackBinding(h);
            return h;
        }

        /// <summary>
        /// v-show: toggle display style reactively (None / Flex).
        /// </summary>
        protected WatchHandle BindShow(VisualElement el, Func<bool> getter)
        {
            if (el == null) throw new ArgumentNullException(nameof(el));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            var h = ReactiveEffect(() =>
            {
                el.style.display = getter()
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            });

            TrackBinding(h);
            return h;
        }

        /// <summary>
        /// :text: bind a string value to a Label reactively.
        /// </summary>
        protected WatchHandle BindText(Label label, Func<string> getter)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            var h = ReactiveEffect(() =>
            {
                label.text = getter() ?? string.Empty;
            });

            TrackBinding(h);
            return h;
        }

        /// <summary>
        /// :class: toggle a CSS class on a VisualElement reactively.
        /// </summary>
        protected WatchHandle BindClass(VisualElement el, string className, Func<bool> getter)
        {
            if (el == null) throw new ArgumentNullException(nameof(el));
            if (className == null) throw new ArgumentNullException(nameof(className));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            var h = ReactiveEffect(() =>
            {
                if (getter())
                    el.AddToClassList(className);
                else
                    el.RemoveFromClassList(className);
            });

            TrackBinding(h);
            return h;
        }

        /// <summary>
        /// v-for: render a list with key-based diffing reactively.
        /// When the source changes, items are added/removed/reordered by key.
        /// The source is a Func so the ReactiveEffect can track Prop dependencies
        /// (pass e.g. () =&gt; Items or () =&gt; Items.Value for a Prop&lt;List&lt;T&gt;&gt;).
        /// </summary>
        protected WatchHandle BindList<T>(VisualElement container,
            Func<IEnumerable<T>> source,
            Func<T, int, VisualElement> itemBuilder,
            Func<T, object> keySelector = null)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (itemBuilder == null) throw new ArgumentNullException(nameof(itemBuilder));

            var keyMap = new Dictionary<object, (VisualElement element, int index)>();

            var h = ReactiveEffect(() =>
            {
                var items = source()?.ToList() ?? new List<T>();
                var seenKeys = new HashSet<object>();

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var key = keySelector?.Invoke(item) ?? item;
                    // Guard: null keys collide — use index as key when item is null
                    if (key == null)
                        key = $"__null_{i}";
                    seenKeys.Add(key);

                    if (keyMap.TryGetValue(key, out var existing))
                    {
                        // Reuse existing element, ensure correct order
                        if (existing.element.parent == null)
                            container.Add(existing.element);

                        // Move to correct position in container
                        var currentIndex = container.IndexOf(existing.element);
                        if (currentIndex >= 0 && currentIndex != i)
                            container.Insert(i, existing.element);
                    }
                    else
                    {
                        // Create new element
                        var el = itemBuilder(item, i);
                        if (el != null)
                        {
                            container.Add(el);
                            keyMap[key] = (el, i);
                        }
                    }
                }

                // Remove stale items
                var toRemove = new List<object>();
                foreach (var kvp in keyMap)
                {
                    if (!seenKeys.Contains(kvp.Key))
                    {
                        kvp.Value.element.RemoveFromHierarchy();
                        toRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in toRemove)
                    keyMap.Remove(key);
            });

            TrackBinding(h);
            // P2.2: on teardown, detach reused elements and drop keyMap refs so the
            // dictionary (and the elements it pins) doesn't outlive the component.
            TrackBinding(new WatchHandle(() =>
            {
                foreach (var kv in keyMap) kv.Value.element?.RemoveFromHierarchy();
                keyMap.Clear();
            }));
            return h;
        }

        /// <summary>
        /// v-for with <see cref="SusDataTemplateSelector{T}"/> — picks item factory by runtime type (F4).
        /// </summary>
        protected WatchHandle BindList<T>(VisualElement container,
            Func<IEnumerable<T>> source,
            SusDataTemplateSelector<T> selector,
            Func<T, object> keySelector = null)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            return BindList(container, source, selector.Build, keySelector);
        }

        /// <summary>
        /// v-for (untyped): reactive list binding when the item type cannot be inferred.
        /// The source Func returns the collection (or a Prop&lt;...&gt; which is unwrapped
        /// via reflection). Re-renders reactively on change, keyed diff like BindList&lt;T&gt;.
        /// </summary>
        protected WatchHandle BindList(VisualElement container,
            Func<object> source,
            Func<object, int, VisualElement> itemBuilder,
            Func<object, object> keySelector = null)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (itemBuilder == null) throw new ArgumentNullException(nameof(itemBuilder));

            var keyMap = new Dictionary<object, (VisualElement element, int index)>();

            var h = ReactiveEffect(() =>
            {
                var raw = source();

                // Unwrap Prop<...> via reflection (getter read inside effect → tracked)
                if (raw != null)
                {
                    var t = raw.GetType();
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Prop<>))
                        raw = t.GetProperty("Value")?.GetValue(raw);
                }

                var items = new List<object>();
                if (raw is System.Collections.IEnumerable en)
                    foreach (var o in en) items.Add(o);

                var seenKeys = new HashSet<object>();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var key = keySelector?.Invoke(item) ?? item;
                    if (key == null)
                        key = $"__null_{i}";
                    seenKeys.Add(key);

                    if (keyMap.TryGetValue(key, out var existing))
                    {
                        if (existing.element.parent == null)
                            container.Add(existing.element);
                        var currentIndex = container.IndexOf(existing.element);
                        if (currentIndex >= 0 && currentIndex != i)
                            container.Insert(i, existing.element);
                    }
                    else
                    {
                        var el = itemBuilder(item, i);
                        if (el != null)
                        {
                            container.Add(el);
                            keyMap[key] = (el, i);
                        }
                    }
                }

                var toRemove = new List<object>();
                foreach (var kvp in keyMap)
                {
                    if (!seenKeys.Contains(kvp.Key))
                    {
                        kvp.Value.element.RemoveFromHierarchy();
                        toRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in toRemove)
                    keyMap.Remove(key);
            });

            TrackBinding(h);
            // P2.2: clear keyMap + detach elements on teardown (see typed overload).
            TrackBinding(new WatchHandle(() =>
            {
                foreach (var kv in keyMap) kv.Value.element?.RemoveFromHierarchy();
                keyMap.Clear();
            }));
            return h;
        }

        /// <summary>
        /// v-for (IEnumerable version): render a list from an IEnumerable source.
        /// The Sharq compiler passes source directly (not as Func); for full reactivity,
        /// pass a Prop&lt;List&lt;T&gt;&gt; and use BindList instead.
        /// This overload compiles from generated :for bindings.
        /// </summary>
        protected void BindListFor<T>(VisualElement container,
            IEnumerable<T> source,
            Func<T, int, VisualElement> itemBuilder,
            Func<T, object> keySelector = null)
        {
            if (container == null || source == null || itemBuilder == null) return;
            var items = source.ToList();
            for (int i = 0; i < items.Count; i++)
                container.Add(itemBuilder(items[i], i));
        }

        /// <summary>
        /// v-model: two-way binding for standard UI Toolkit input controls.
        /// Reads the control's value into the Prop and writes Prop changes back to the control.
        /// </summary>
        protected void BindModel(TextField field, Prop<string> prop)
        {
            if (field == null || prop == null) return;

            // Prop → field
            var h = ReactiveEffect(() =>
            {
                if (field.value != prop.Value)
                    field.value = prop.Value;
            });
            TrackBinding(h);

            // field → Prop (tracked — unregistered on detach)
            EventCallback<ChangeEvent<string>> cb = null;
            cb = evt => prop.Value = evt.newValue;
            field.RegisterValueChangedCallback(cb);
            TrackBinding(new WatchHandle(() => field.UnregisterValueChangedCallback(cb)));
        }

        /// <summary>
        /// v-model: two-way binding for Slider/float controls.
        /// </summary>
        protected void BindModel(Slider slider, Prop<float> prop)
        {
            if (slider == null || prop == null) return;

            var h = ReactiveEffect(() =>
            {
                if (Math.Abs(slider.value - prop.Value) > float.Epsilon)
                    slider.value = prop.Value;
            });
            TrackBinding(h);

            EventCallback<ChangeEvent<float>> cb = null;
            cb = evt => prop.Value = evt.newValue;
            slider.RegisterValueChangedCallback(cb);
            TrackBinding(new WatchHandle(() => slider.UnregisterValueChangedCallback(cb)));
        }

        /// <summary>
        /// v-model: two-way binding for Toggle.
        /// </summary>
        protected void BindModel(Toggle toggle, Prop<bool> prop)
        {
            if (toggle == null || prop == null) return;

            var h = ReactiveEffect(() =>
            {
                if (toggle.value != prop.Value)
                    toggle.value = prop.Value;
            });
            TrackBinding(h);

            EventCallback<ChangeEvent<bool>> cb = null;
            cb = evt => prop.Value = evt.newValue;
            toggle.RegisterValueChangedCallback(cb);
            TrackBinding(new WatchHandle(() => toggle.UnregisterValueChangedCallback(cb)));
        }

        /// <summary>
        /// v-model: two-way binding for DropdownField.
        /// </summary>
        protected void BindModel(DropdownField dropdown, Prop<string> prop)
        {
            if (dropdown == null || prop == null) return;

            var h = ReactiveEffect(() =>
            {
                if (dropdown.value != prop.Value)
                    dropdown.value = prop.Value;
            });
            TrackBinding(h);

            EventCallback<ChangeEvent<string>> cb = null;
            cb = evt => prop.Value = evt.newValue;
            dropdown.RegisterValueChangedCallback(cb);
            TrackBinding(new WatchHandle(() => dropdown.UnregisterValueChangedCallback(cb)));
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Selects an item factory by runtime item type (Noesis DataTemplateSelector analogue).
    /// Register factories with <see cref="Register{TData}"/>, then pass to
    /// <see cref="SusComponent.BindList{T}(VisualElement, Func{IEnumerable{T}}, SusDataTemplateSelector{T}, Func{T, object})"/>.
    /// </summary>
    public sealed class SusDataTemplateSelector<T>
    {
        private readonly List<(Type type, Func<T, int, VisualElement> factory)> _typed = new();
        private Func<T, int, VisualElement> _fallback;

        public SusDataTemplateSelector<T> Register<TData>(Func<TData, int, VisualElement> factory)
            where TData : T
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _typed.Add((typeof(TData), (item, i) => factory((TData)(object)item, i)));
            return this;
        }

        public SusDataTemplateSelector<T> Register(Type dataType, Func<T, int, VisualElement> factory)
        {
            if (dataType == null) throw new ArgumentNullException(nameof(dataType));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _typed.Add((dataType, factory));
            return this;
        }

        public SusDataTemplateSelector<T> Fallback(Func<T, int, VisualElement> factory)
        {
            _fallback = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public VisualElement Build(T item, int index)
        {
            if (item != null)
            {
                var itemType = item.GetType();
                // Most-specific first: exact type, then assignable
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (var (type, factory) in _typed)
                    {
                        bool match = pass == 0
                            ? type == itemType
                            : type.IsAssignableFrom(itemType);
                        if (match) return factory(item, index);
                    }
                }
            }

            if (_fallback != null) return _fallback(item, index);

            // Default: Label with ToString
            return new Label(item?.ToString() ?? "");
        }
    }
}

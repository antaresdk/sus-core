using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Child-prop accessors: reflection-based get/set of a named field or property on a child
    /// VisualElement (with Prop&lt;T&gt; unwrap/rewrap), used by the Sharq compiler for
    /// prop-passing (<c>:prop="expr"</c>) on custom child components. Results are cached per
    /// (child type, prop name) pair to avoid repeat reflection lookups.
    /// </summary>
    public abstract partial class SusComponent
    {
        /// <summary>
        /// Cached descriptor for a (child type, prop name) pair resolved once by
        /// <see cref="SetChildProp"/> — avoids re-running <c>GetField</c>/<c>GetProperty</c>
        /// reflection lookups on every prop-bind change (R-A2/P0-2).
        /// </summary>
        private sealed class ChildPropAccessor
        {
            internal static readonly ChildPropAccessor NotFound = new ChildPropAccessor { Found = false };

            internal bool Found = true;
            internal System.Reflection.FieldInfo Field;
            internal System.Reflection.PropertyInfo Property;
            internal bool IsPropWrapper;
            internal System.Reflection.PropertyInfo InnerValueProperty; // Prop<T>.Value, only when IsPropWrapper
            internal Type InnerValueType;
        }

        // (child type) → (prop name, case-insensitive) → accessor. Built lazily, once per pair.
        private static readonly Dictionary<Type, Dictionary<string, ChildPropAccessor>> s_childPropCache =
            new Dictionary<Type, Dictionary<string, ChildPropAccessor>>();

        // Type → "Value" PropertyInfo of that Prop<T>-shaped type. Shared by the incoming-value
        // unwrap in SetChildProp and by ChildPropAccessor construction.
        private static readonly Dictionary<Type, System.Reflection.PropertyInfo> s_propValuePropertyCache =
            new Dictionary<Type, System.Reflection.PropertyInfo>();

        private static System.Reflection.PropertyInfo GetPropValueProperty(Type propWrapperType)
        {
            if (!s_propValuePropertyCache.TryGetValue(propWrapperType, out var pi))
            {
                pi = propWrapperType.GetProperty("Value");
                s_propValuePropertyCache[propWrapperType] = pi;
            }
            return pi;
        }

        private static ChildPropAccessor GetChildPropAccessor(Type childType, string propName)
        {
            if (!s_childPropCache.TryGetValue(childType, out var byName))
            {
                byName = new Dictionary<string, ChildPropAccessor>(StringComparer.OrdinalIgnoreCase);
                s_childPropCache[childType] = byName;
            }
            if (byName.TryGetValue(propName, out var accessor))
                return accessor;

            accessor = BuildChildPropAccessor(childType, propName);
            byName[propName] = accessor;
            return accessor;
        }

        private static ChildPropAccessor BuildChildPropAccessor(Type childType, string propName)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.IgnoreCase;

            var field = childType.GetField(propName, flags);
            if (field != null)
            {
                var isWrapper = IsPropType(field.FieldType);
                System.Reflection.PropertyInfo innerProp = null;
                Type innerType = null;
                if (isWrapper)
                {
                    innerProp = GetPropValueProperty(field.FieldType);
                    innerType = innerProp?.PropertyType;
                    isWrapper = innerProp != null;
                }
                return new ChildPropAccessor
                {
                    Field = field,
                    IsPropWrapper = isWrapper,
                    InnerValueProperty = innerProp,
                    InnerValueType = innerType,
                };
            }

            var prop = childType.GetProperty(propName, flags);
            if (prop != null && prop.CanWrite)
            {
                var isWrapper = IsPropType(prop.PropertyType);
                System.Reflection.PropertyInfo innerProp = null;
                Type innerType = null;
                if (isWrapper)
                {
                    innerProp = GetPropValueProperty(prop.PropertyType);
                    innerType = innerProp?.PropertyType;
                    isWrapper = innerProp != null;
                }
                return new ChildPropAccessor
                {
                    Property = prop,
                    IsPropWrapper = isWrapper,
                    InnerValueProperty = innerProp,
                    InnerValueType = innerType,
                };
            }

            return ChildPropAccessor.NotFound;
        }

        /// <summary>
        /// Sets a property or field by name (case-insensitive) on a VisualElement child.
        /// Used by the Sharq compiler for prop passing: &lt;sus:Child variant="primary" /&gt;
        /// and &lt;sus:Child :variant="SourceProp" /&gt;.
        ///
        /// For Prop&lt;T&gt; fields: mutates .Value without replacing the instance
        /// (preserves internal bindings subscribed to the Prop).
        /// For plain types: direct property/field assignment.
        ///
        /// The (child type, propName) accessor is resolved via reflection once and cached
        /// — repeat calls (every reactive re-apply of a :prop bind) skip GetField/
        /// GetProperty entirely.
        /// </summary>
        public static void SetChildProp(VisualElement child, string propName, object value)
        {
            if (child == null || string.IsNullOrEmpty(propName)) return;

            // Auto-unwrap Prop<T> → T.
            // When generator emits () => SomeProp (where SomeProp is Prop<int>),
            // the getter returns the Prop wrapper, not the inner value.
            if (value != null && IsPropType(value.GetType()))
            {
                var vp = GetPropValueProperty(value.GetType());
                if (vp != null)
                    value = vp.GetValue(value);
            }

            var accessor = GetChildPropAccessor(child.GetType(), propName);
            if (!accessor.Found) return;

            if (accessor.Field != null)
            {
                var fieldValue = accessor.Field.GetValue(child);
                if (fieldValue != null && accessor.IsPropWrapper)
                {
                    // Prop<T> — mutate .Value
                    var converted = ConvertValue(value, accessor.InnerValueType);
                    accessor.InnerValueProperty.SetValue(fieldValue, converted);
                    return;
                }

                // Plain field — direct assignment
                var convertedField = ConvertValue(value, accessor.Field.FieldType);
                accessor.Field.SetValue(child, convertedField);
                return;
            }

            // Property path
            var propValue = accessor.Property.GetValue(child);
            if (propValue != null && accessor.IsPropWrapper)
            {
                var converted = ConvertValue(value, accessor.InnerValueType);
                accessor.InnerValueProperty.SetValue(propValue, converted);
                return;
            }

            var convertedProp = ConvertValue(value, accessor.Property.PropertyType);
            accessor.Property.SetValue(child, convertedProp);
        }

        /// <summary>
        /// Reactive version of SetChildProp. Sets a child property immediately, then
        /// auto-updates whenever reactive sources read inside <paramref name="getter"/> change.
        /// Generated by the Sharq compiler for :prop="expr" bindings on custom components.
        /// </summary>
        protected void BindChildProp(VisualElement child, string propName, Func<object> getter)
        {
            if (child == null || string.IsNullOrEmpty(propName) || getter == null) return;

            // Initial set
            SetChildProp(child, propName, getter());

            // Reactive tracking — re-apply whenever any Prop/Computed read in getter changes
            WatchEffect(() =>
            {
                SetChildProp(child, propName, getter());
            });
        }

        private static bool IsPropType(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Prop<>);
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType == typeof(bool) && value is string s)
                return bool.TryParse(s, out var b) && b;

            if (targetType == typeof(string))
                return value.ToString();

            if (targetType.IsEnum && value is string es)
                return Enum.Parse(targetType, es, ignoreCase: true);

            try { return Convert.ChangeType(value, targetType); }
            catch { return value; }
        }
    }
}

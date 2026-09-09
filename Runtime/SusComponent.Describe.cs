using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Sharq.Core
{
    /// <summary>
    /// Introspection: what a component can tell about itself from OUTSIDE, without the caller
    /// knowing its type (plan ARCH-20260907-STORYBOOK-ENGINE §4.2, card T-3031).
    ///
    /// Three answers, all on the instance, none of them referencing kit or game:
    /// <see cref="DescribeProps"/> (public <c>Prop&lt;T&gt;</c> fields),
    /// <see cref="DescribeAllowed"/> (sets recorded by <c>UseAllowed</c>) and
    /// <see cref="DescribeEvents"/> (public <c>On*</c> delegate fields).
    ///
    /// Reflection walks the FIELD TYPE, not an attribute: <c>[CreateProperty]</c> covers only
    /// 482 of 644 kit props and 80 of 266 game props, so an attribute-driven reader would be
    /// blind to ~70% of the game API.
    /// </summary>
    public abstract partial class SusComponent
    {
        const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

        // ── Per-type descriptor caches ──────────────────────────────────────
        // Reflection over a component type is done ONCE; per instance we only read values.

        static readonly Dictionary<Type, PropDescriptor[]> PropCache = new();
        static readonly Dictionary<Type, EventDescriptor[]> EventCache = new();

        sealed class PropDescriptor
        {
            public FieldInfo Field;
            public string Name;
            public Type ValueType;
            public Type DeclaringType;
            public bool ReadOnly;
            public SusRangeAttribute Range;
            public SusDependsOnAttribute[] DependsOn;
        }

        sealed class EventDescriptor
        {
            public FieldInfo Field;
            public string Name;
            public string BusName;
            public Type ArgType;
            public Type DeclaringType;
        }

        // ── DescribeProps ───────────────────────────────────────────────────

        /// <summary>
        /// Every public <c>Prop&lt;T&gt;</c> (and <c>ReadonlyProp&lt;T&gt;</c>) field of this
        /// component: name, value type, current value, group, allowed set, declared range and
        /// dependencies, plus the "nobody reads it" observation
        /// (<see cref="SusPropInfo.Dead"/>).
        ///
        /// Values are read through <see cref="ISusPropAccess.BoxedValue"/>, so describing a
        /// component neither registers reactive dependencies nor makes a dead prop look alive.
        /// Order follows reflection order of the type (declaration order in practice).
        /// </summary>
        public IReadOnlyList<SusPropInfo> DescribeProps()
        {
            var descriptors = GetPropDescriptors(GetType());
            if (descriptors.Length == 0) return Array.Empty<SusPropInfo>();

            var allowed = DescribeAllowed();
            var result = new List<SusPropInfo>(descriptors.Length);

            for (int i = 0; i < descriptors.Length; i++)
            {
                var d = descriptors[i];
                if (d.Field.GetValue(this) is not ISusPropAccess access) continue;

                allowed.TryGetValue(d.Name, out var allowedInfo);

                var info = new SusPropInfo(
                    d.Name,
                    d.ValueType,
                    d.DeclaringType,
                    access,
                    SusPropGrouping.Classify(d.Name, d.ValueType, allowedInfo != null),
                    d.ReadOnly,
                    d.Range,
                    d.DependsOn)
                {
                    Allowed = allowedInfo,
                };
                result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// True when every <c>[SusDependsOn]</c> of <paramref name="prop"/> currently holds, i.e.
        /// the control for it is meaningful right now. A control whose condition is false must be
        /// disabled with the reason shown (<see cref="DescribeDependency"/>), not hidden silently.
        /// </summary>
        public bool IsDependencySatisfied(SusPropInfo prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            var groups = GroupConditions(prop.DependsOn);
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                bool any = false;
                for (int i = 0; i < group.Count && !any; i++) any = ConditionHolds(group[i]);
                if (!any) return false;   // every AND-group needs one member to hold
            }
            return true;
        }

        /// <summary>
        /// Human-readable reason a dependent control is off ("visible when ContentMode = icon"),
        /// or null when the prop has no conditions.
        /// </summary>
        public string DescribeDependency(SusPropInfo prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            var conditions = prop.DependsOn;
            if (conditions.Count == 0) return null;

            var groups = GroupConditions(conditions);
            var parts = new string[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var members = new string[group.Count];
                for (int i = 0; i < group.Count; i++) members[i] = group[i].Describe();
                parts[g] = string.Join(" or ", members);
            }
            return string.Join(" & ", parts);
        }

        /// <summary>
        /// Conditions bucketed by <see cref="SusDependsOnAttribute.Group"/>, declaration order
        /// kept: members of one bucket are OR'ed, buckets are AND'ed. An ungrouped condition is
        /// a bucket of its own, so the pre-T-3080 behaviour (plain AND) is the default.
        /// </summary>
        static List<List<SusDependsOnAttribute>> GroupConditions(
            IReadOnlyList<SusDependsOnAttribute> conditions)
        {
            var groups = new List<List<SusDependsOnAttribute>>();
            Dictionary<string, int> named = null;

            for (int i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                if (c == null) continue;

                if (string.IsNullOrEmpty(c.Group))
                {
                    groups.Add(new List<SusDependsOnAttribute> { c });
                    continue;
                }

                named ??= new Dictionary<string, int>(StringComparer.Ordinal);
                if (named.TryGetValue(c.Group, out var index))
                {
                    groups[index].Add(c);
                }
                else
                {
                    named[c.Group] = groups.Count;
                    groups.Add(new List<SusDependsOnAttribute> { c });
                }
            }
            return groups;
        }

        bool ConditionHolds(SusDependsOnAttribute condition)
        {
            if (condition == null || string.IsNullOrEmpty(condition.Prop)) return true;

            var descriptors = GetPropDescriptors(GetType());
            for (int i = 0; i < descriptors.Length; i++)
            {
                if (!string.Equals(descriptors[i].Name, condition.Prop, StringComparison.Ordinal))
                    continue;
                if (descriptors[i].Field.GetValue(this) is not ISusPropAccess access) return false;

                var value = access.BoxedValue;
                bool raw;
                if (condition.Value == null)
                {
                    // "any meaningful value": true for bool, non-empty for string, non-default
                    // for everything else.
                    if (value is bool b) raw = b;
                    else if (value is string s) raw = !string.IsNullOrEmpty(s);
                    else raw = value != null;
                }
                else
                {
                    var current = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    raw = string.Equals(current, condition.Value, StringComparison.OrdinalIgnoreCase);
                }
                return condition.Negate ? !raw : raw;
            }
            // The named prop does not exist — a typo in the attribute must not silently disable
            // a working control, so the condition is treated as unmet and named by the caller.
            // Negate does NOT flip this: a typo is a typo in both directions.
            return false;
        }

        // ── DescribeAllowed ─────────────────────────────────────────────────

        /// <summary>
        /// Allowed sets of this component, keyed by prop name: legal values, fallback, aliases.
        ///
        /// Before T-3031 the set lived only inside the <c>Watch</c> closure of
        /// <c>UseAllowed</c>, so nothing outside the component could answer "which values are
        /// legal here" — and the sets themselves (<c>SusOptionSets</c>) live in kit, which core
        /// must not reference. Now <c>UseAllowed</c> records the set on the instance and this
        /// method reads it back. Dynamic (resolver-backed) sets are evaluated on every call.
        /// </summary>
        public IReadOnlyDictionary<string, SusAllowedInfo> DescribeAllowed()
        {
            var records = AllowedRecords;
            if (records.Count == 0) return EmptyAllowed;

            var descriptors = GetPropDescriptors(GetType());
            var map = new Dictionary<string, SusAllowedInfo>(records.Count, StringComparer.Ordinal);

            for (int i = 0; i < records.Count; i++)
            {
                var rec = records[i];
                var name = ResolvePropName(descriptors, rec, i);

                var info = new SusAllowedInfo
                {
                    PropName = name,
                    ValueType = rec.ValueType,
                    Fallback = Convert.ToString(rec.Fallback, CultureInfo.InvariantCulture),
                    Aliases = rec.Aliases ?? EmptyAliases,
                    AllowEmpty = rec.AllowEmpty,
                    IsDynamic = rec.IntValues != null,
                };

                if (rec.StringValues != null)
                {
                    var values = new string[rec.StringValues.Count];
                    var raw = new object[rec.StringValues.Count];
                    for (int v = 0; v < rec.StringValues.Count; v++)
                    {
                        values[v] = rec.StringValues[v];
                        raw[v] = rec.StringValues[v];
                    }
                    info.Values = values;
                    info.RawValues = raw;
                }
                else if (rec.IntValues != null)
                {
                    var live = rec.IntValues();
                    var count = live?.Count ?? 0;
                    var values = new string[count];
                    var raw = new object[count];
                    for (int v = 0; v < count; v++)
                    {
                        values[v] = live[v].ToString(CultureInfo.InvariantCulture);
                        raw[v] = live[v];
                    }
                    info.Values = values;
                    info.RawValues = raw;
                    if (rec.Fallback == null && count > 0)
                        info.Fallback = values[0];
                }
                else
                {
                    info.Values = Array.Empty<string>();
                    info.RawValues = Array.Empty<object>();
                }

                map[name] = info;
            }
            return map;
        }

        static readonly Dictionary<string, SusAllowedInfo> EmptyAllowed =
            new(0, StringComparer.Ordinal);

        static readonly Dictionary<string, string> EmptyAliases =
            new(0, StringComparer.Ordinal);

        /// <summary>
        /// Name of the prop a recorded set belongs to. Resolved by IDENTITY (the field holding
        /// this very Prop object), not by the optional <c>propName</c> string — the string is a
        /// log label ("SusRating.Size"), may be absent and may drift from the field name.
        /// </summary>
        string ResolvePropName(PropDescriptor[] descriptors, AllowedRecord rec, int index)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                if (ReferenceEquals(rec.PropObject, descriptors[i].Field.GetValue(this)))
                    return descriptors[i].Name;
            }
            if (!string.IsNullOrEmpty(rec.DeclaredName))
            {
                var dot = rec.DeclaredName.LastIndexOf('.');
                return dot >= 0 && dot < rec.DeclaredName.Length - 1
                    ? rec.DeclaredName.Substring(dot + 1)
                    : rec.DeclaredName;
            }
            return "prop#" + index.ToString(CultureInfo.InvariantCulture);
        }

        // ── DescribeEvents ──────────────────────────────────────────────────

        /// <summary>
        /// Every public <c>Action</c> / <c>Action&lt;T&gt;</c> field named <c>On*</c>: name, bus
        /// name and payload type. A consumer subscribes generically through
        /// <see cref="SubscribeEvent"/> — no knowledge of <c>T</c> needed.
        /// </summary>
        public IReadOnlyList<SusEventInfo> DescribeEvents()
        {
            var descriptors = GetEventDescriptors(GetType());
            if (descriptors.Length == 0) return Array.Empty<SusEventInfo>();

            var result = new List<SusEventInfo>(descriptors.Length);
            for (int i = 0; i < descriptors.Length; i++)
            {
                var d = descriptors[i];
                result.Add(new SusEventInfo
                {
                    Name = d.Name,
                    BusName = d.BusName,
                    ArgType = d.ArgType,
                    DeclaringType = d.DeclaringType,
                    HasSubscribers = d.Field.GetValue(this) is Delegate,
                });
            }
            return result;
        }

        /// <summary>
        /// Subscribes to an event described by <see cref="DescribeEvents"/> without knowing its
        /// payload type: the handler receives the argument boxed (null for a parameterless
        /// <c>Action</c>). Dispose the result to detach.
        /// Returns null when <paramref name="eventName"/> is not a described event.
        /// </summary>
        public IDisposable SubscribeEvent(string eventName, Action<object> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrEmpty(eventName)) return null;

            var descriptors = GetEventDescriptors(GetType());
            for (int i = 0; i < descriptors.Length; i++)
            {
                var d = descriptors[i];
                if (!string.Equals(d.Name, eventName, StringComparison.Ordinal) &&
                    !string.Equals(d.BusName, eventName, StringComparison.Ordinal))
                    continue;

                Delegate attached;
                if (d.ArgType == null)
                {
                    attached = new Action(() => handler(null));
                }
                else
                {
                    var helperType = typeof(EventForwarder<>).MakeGenericType(d.ArgType);
                    var helper = Activator.CreateInstance(helperType, handler);
                    var forward = helperType.GetMethod(nameof(EventForwarder<object>.Forward));
                    attached = Delegate.CreateDelegate(d.Field.FieldType, helper, forward);
                }

                var existing = d.Field.GetValue(this) as Delegate;
                d.Field.SetValue(this, existing != null ? Delegate.Combine(existing, attached) : attached);

                var field = d.Field;
                return new EventSubscription(() =>
                {
                    var current = field.GetValue(this) as Delegate;
                    field.SetValue(this, Delegate.Remove(current, attached));
                });
            }
            return null;
        }

        sealed class EventForwarder<T>
        {
            readonly Action<object> _handler;
            public EventForwarder(Action<object> handler) => _handler = handler;
            public void Forward(T value) => _handler(value);
        }

        sealed class EventSubscription : IDisposable
        {
            Action _detach;
            public EventSubscription(Action detach) => _detach = detach;
            public void Dispose()
            {
                _detach?.Invoke();
                _detach = null;
            }
        }

        // ── Reflection caches ───────────────────────────────────────────────

        static PropDescriptor[] GetPropDescriptors(Type type)
        {
            if (PropCache.TryGetValue(type, out var cached)) return cached;

            var list = new List<PropDescriptor>();
            foreach (var field in type.GetFields(MemberFlags))
            {
                var ft = field.FieldType;
                if (!ft.IsGenericType) continue;

                var def = ft.GetGenericTypeDefinition();
                bool readOnly;
                if (def == typeof(Prop<>)) readOnly = false;
                else if (def == typeof(ReadonlyProp<>)) readOnly = true;
                else continue;

                var raw = field.GetCustomAttributes(typeof(SusDependsOnAttribute), inherit: true);
                var conditions = new SusDependsOnAttribute[raw.Length];
                for (int c = 0; c < raw.Length; c++) conditions[c] = (SusDependsOnAttribute)raw[c];

                list.Add(new PropDescriptor
                {
                    Field = field,
                    Name = field.Name,
                    ValueType = ft.GetGenericArguments()[0],
                    DeclaringType = field.DeclaringType,
                    ReadOnly = readOnly,
                    Range = (SusRangeAttribute)Attribute.GetCustomAttribute(field, typeof(SusRangeAttribute)),
                    DependsOn = conditions,
                });
            }

            var array = list.ToArray();
            PropCache[type] = array;
            return array;
        }

        static EventDescriptor[] GetEventDescriptors(Type type)
        {
            if (EventCache.TryGetValue(type, out var cached)) return cached;

            var list = new List<EventDescriptor>();
            foreach (var field in type.GetFields(MemberFlags))
            {
                if (!field.Name.StartsWith("On", StringComparison.Ordinal)) continue;
                if (field.Name.Length < 3) continue;

                var ft = field.FieldType;
                Type argType;
                if (ft == typeof(Action)) argType = null;
                else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Action<>))
                    argType = ft.GetGenericArguments()[0];
                else continue;

                list.Add(new EventDescriptor
                {
                    Field = field,
                    Name = field.Name,
                    BusName = ToBusName(field.Name),
                    ArgType = argType,
                    DeclaringType = field.DeclaringType,
                });
            }

            var array = list.ToArray();
            EventCache[type] = array;
            return array;
        }

        /// <summary>
        /// Inverse of the bus bridge in <c>SusComponent.Events.cs</c>: it maps
        /// <c>"change"</c> to the field <c>OnChange</c>, so <c>OnValueChanged</c> maps back to
        /// <c>"valueChanged"</c>. Same rule, one direction each.
        /// </summary>
        static string ToBusName(string fieldName)
        {
            var tail = fieldName.Substring(2);
            if (tail.Length == 0) return tail;
            return char.ToLowerInvariant(tail[0]) + tail.Substring(1);
        }
    }
}

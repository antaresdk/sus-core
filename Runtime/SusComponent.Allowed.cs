using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Clamps component <see cref="Prop{T}"/> values to an allowed set (with optional aliases).
    /// Invalid external/init values are coerced to a fallback so CSS BindClass always matches.
    /// </summary>
    public abstract partial class SusComponent
    {
        /// <summary>
        /// Keep <paramref name="prop"/> within <paramref name="allowed"/>.
        /// Aliases (e.g. lg→large) are applied first. Empty string is allowed only if
        /// <paramref name="allowEmpty"/> is true or <c>""</c> is listed in allowed.
        /// </summary>
        protected void UseAllowed(
            Prop<string> prop,
            IReadOnlyList<string> allowed,
            string fallback = null,
            IReadOnlyDictionary<string, string> aliases = null,
            bool allowEmpty = false,
            string propName = null)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (allowed == null || allowed.Count == 0)
                throw new ArgumentException("allowed must be non-empty", nameof(allowed));

            var fb = fallback ?? allowed[0];
            // T-3031: the set used to live ONLY in the closure below, so nothing outside the
            // component could ever answer "which values are legal here". Record it first.
            RegisterAllowed(new AllowedRecord
            {
                PropObject = prop,
                ValueType = typeof(string),
                DeclaredName = propName,
                StringValues = allowed,
                Fallback = fb,
                Aliases = aliases,
                AllowEmpty = allowEmpty,
            });
            ClampStringProp(prop, allowed, fb, aliases, allowEmpty, propName);

            Watch(prop, (_, __) =>
                ClampStringProp(prop, allowed, fb, aliases, allowEmpty, propName));
        }

        /// <summary>
        /// Keep <paramref name="prop"/> within values returned by <paramref name="getAllowed"/>
        /// (re-evaluated on each change — for dynamic option lists like table page sizes).
        /// </summary>
        protected void UseAllowed(
            Prop<int> prop,
            Func<IReadOnlyList<int>> getAllowed,
            int? fallback = null,
            string propName = null)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            if (getAllowed == null) throw new ArgumentNullException(nameof(getAllowed));

            void Clamp()
            {
                var allowed = getAllowed();
                if (allowed == null || allowed.Count == 0) return;
                ClampIntProp(prop, allowed, fallback ?? allowed[0], propName);
            }

            // T-3031: dynamic sets are recorded as the RESOLVER, not a snapshot — a table's
            // page-size list changes with the data and a stale copy would lie.
            RegisterAllowed(new AllowedRecord
            {
                PropObject = prop,
                ValueType = typeof(int),
                DeclaredName = propName,
                IntValues = getAllowed,
                Fallback = fallback,
                AllowEmpty = false,
            });

            Clamp();
            Watch(prop, (_, __) => Clamp());
        }

        /// <summary>One-shot coerce (no Watch). Useful for controllers outside SusComponent.</summary>
        public static string CoerceAllowed(
            string value,
            IReadOnlyList<string> allowed,
            string fallback,
            IReadOnlyDictionary<string, string> aliases = null,
            bool allowEmpty = false)
        {
            if (allowed == null || allowed.Count == 0) return fallback;
            var fb = fallback ?? allowed[0];
            return NormalizeString(value, allowed, fb, aliases, allowEmpty, out _);
        }

        /// <summary>One-shot coerce for int option lists.</summary>
        public static int CoerceAllowed(
            int value,
            IReadOnlyList<int> allowed,
            int fallback)
        {
            if (allowed == null || allowed.Count == 0) return fallback;
            for (int i = 0; i < allowed.Count; i++)
            {
                if (allowed[i] == value) return value;
            }
            return fallback;
        }

        static void ClampStringProp(
            Prop<string> prop,
            IReadOnlyList<string> allowed,
            string fallback,
            IReadOnlyDictionary<string, string> aliases,
            bool allowEmpty,
            string propName)
        {
            var raw = prop.Peek();
            var next = NormalizeString(raw, allowed, fallback, aliases, allowEmpty, out var changed);
            if (!changed) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SusLog.Warn(
                $"[PropAllowed] {propName ?? "Prop"}: '{raw}' → '{next}' (not in allowed set)");
#endif
            prop.Value = next;
        }

        static void ClampIntProp(
            Prop<int> prop,
            IReadOnlyList<int> allowed,
            int fallback,
            string propName)
        {
            var raw = prop.Peek();
            var next = CoerceAllowed(raw, allowed, fallback);
            if (next == raw) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SusLog.Warn(
                $"[PropAllowed] {propName ?? "Prop"}: {raw} → {next} (not in allowed set)");
#endif
            prop.Value = next;
        }

        static string NormalizeString(
            string value,
            IReadOnlyList<string> allowed,
            string fallback,
            IReadOnlyDictionary<string, string> aliases,
            bool allowEmpty,
            out bool changed)
        {
            changed = false;
            var v = value ?? "";

            if (aliases != null && aliases.TryGetValue(v, out var mapped))
                v = mapped;

            if (string.IsNullOrEmpty(v))
            {
                if (allowEmpty || ContainsIgnoreCase(allowed, ""))
                    return value == v ? value : Mark(v, ref changed, value);
                return Mark(fallback, ref changed, value);
            }

            if (ContainsIgnoreCase(allowed, v))
            {
                // Canonicalize casing to the allowed entry
                var canonical = Canonical(allowed, v);
                return Mark(canonical, ref changed, value);
            }

            return Mark(fallback, ref changed, value);
        }

        static string Mark(string next, ref bool changed, string original)
        {
            if (!string.Equals(next, original, StringComparison.Ordinal))
                changed = true;
            return next;
        }

        static bool ContainsIgnoreCase(IReadOnlyList<string> allowed, string value)
        {
            for (int i = 0; i < allowed.Count; i++)
            {
                if (string.Equals(allowed[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string Canonical(IReadOnlyList<string> allowed, string value)
        {
            for (int i = 0; i < allowed.Count; i++)
            {
                if (string.Equals(allowed[i], value, StringComparison.OrdinalIgnoreCase))
                    return allowed[i];
            }
            return value;
        }

        // ── Allowed-set registry (T-3031, plan ARCH-20260907-STORYBOOK-ENGINE §4.2) ──────

        /// <summary>One recorded <see cref="UseAllowed"/> call. Kept per instance.</summary>
        internal sealed class AllowedRecord
        {
            public object PropObject;
            public Type ValueType;
            public string DeclaredName;
            public IReadOnlyList<string> StringValues;
            public Func<IReadOnlyList<int>> IntValues;
            public object Fallback;
            public IReadOnlyDictionary<string, string> Aliases;
            public bool AllowEmpty;
        }

        private List<AllowedRecord> _allowedRecords;

        internal IReadOnlyList<AllowedRecord> AllowedRecords =>
            (IReadOnlyList<AllowedRecord>)_allowedRecords ?? Array.Empty<AllowedRecord>();

        private void RegisterAllowed(AllowedRecord record)
        {
            _allowedRecords ??= new List<AllowedRecord>();
            // Re-registration for the same prop (a component may call UseAllowed again after a
            // data swap) replaces the record instead of stacking a second, stale answer.
            for (int i = 0; i < _allowedRecords.Count; i++)
            {
                if (ReferenceEquals(_allowedRecords[i].PropObject, record.PropObject))
                {
                    _allowedRecords[i] = record;
                    return;
                }
            }
            _allowedRecords.Add(record);
        }

    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Sharq.Core.Storybook.Nav
{
    /// <summary>
    /// One address of the storybook: which story, and what the controls and environment axes were
    /// set to (plan §4.6). Immutable — a history entry that could be edited in place would rewrite
    /// the past.
    ///
    /// Canonical text form (mock-up "Storybook Shell", card T-3033):
    /// <c>#/&lt;package&gt;/&lt;group&gt;/&lt;slug&gt;?Prop=val&amp;Prop2=val2</c>.
    /// The query holds ONLY what differs from the story's defaults, so an untouched story has a
    /// short, stable link and a shared link says exactly what the sharer changed.
    ///
    /// <see cref="TryParse"/> also accepts the pre-engine form <c>#story=&lt;id&gt;&amp;k=v</c>
    /// that today's shell writes, so links already pasted into docs, cards and frames keep
    /// resolving (plan D11).
    /// </summary>
    public sealed class SusStoryRoute : IEquatable<SusStoryRoute>
    {
        static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>(StringComparer.Ordinal);

        readonly Dictionary<string, string> _query;

        public SusStoryRoute(string storyId, IReadOnlyDictionary<string, string> query = null, bool fromUrl = false)
        {
            StoryId = string.IsNullOrWhiteSpace(storyId) ? null : storyId.Trim().Trim('/');
            FromUrl = fromUrl;
            if (query == null || query.Count == 0)
            {
                _query = null;
            }
            else
            {
                _query = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in query)
                    if (!string.IsNullOrEmpty(kv.Key)) _query[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        /// <summary>Story address <c>kit/atoms/select</c>, or null for "no story selected".</summary>
        public string StoryId { get; }

        /// <summary>Control and environment values that differ from the story defaults.</summary>
        public IReadOnlyDictionary<string, string> Query =>
            (IReadOnlyDictionary<string, string>)_query ?? Empty;

        /// <summary>
        /// True when this route was APPLIED FROM the address bar rather than produced by a click.
        /// The loop guard of §4.6: such a route is never pushed back into the address bar.
        /// </summary>
        public bool FromUrl { get; }

        /// <summary>Package segment, or null.</summary>
        public string Package => Segment(0);

        /// <summary>Group segment, or null.</summary>
        public string Group => Segment(1);

        /// <summary>Slug segment, or null.</summary>
        public string Slug => Segment(2);

        string Segment(int i)
        {
            if (StoryId == null) return null;
            var parts = StoryId.Split('/');
            return parts.Length == 3 ? parts[i] : null;
        }

        /// <summary>Same address and query, marked as coming from (or not from) the address bar.</summary>
        public SusStoryRoute AsFromUrl(bool fromUrl) =>
            fromUrl == FromUrl ? this : new SusStoryRoute(StoryId, _query, fromUrl);

        /// <summary>Same story, different query.</summary>
        public SusStoryRoute WithQuery(IReadOnlyDictionary<string, string> query) =>
            new SusStoryRoute(StoryId, query, FromUrl);

        /// <summary>Same story with one key set (empty or null value removes the key).</summary>
        public SusStoryRoute WithValue(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return this;
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_query != null)
                foreach (var kv in _query) next[kv.Key] = kv.Value;
            if (value == null) next.Remove(key);
            else next[key] = value;
            return new SusStoryRoute(StoryId, next, FromUrl);
        }

        /// <summary>
        /// Builds the route for a story from a full value set and the story's defaults: only the
        /// differences reach the query (mock-up rule, and the reason a shared link is readable).
        /// </summary>
        public static SusStoryRoute FromValues(
            string storyId,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<string, string> defaults)
        {
            var q = new Dictionary<string, string>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (var kv in values)
                {
                    if (kv.Key == null) continue;
                    string def = null;
                    if (defaults != null) defaults.TryGetValue(kv.Key, out def);
                    if (!string.Equals(def ?? string.Empty, kv.Value ?? string.Empty, StringComparison.Ordinal))
                        q[kv.Key] = kv.Value ?? string.Empty;
                }
            }
            return new SusStoryRoute(storyId, q);
        }

        /// <summary>The canonical hash: <c>#/kit/atoms/select?Size=lg</c>.</summary>
        public string ToHash()
        {
            var sb = new StringBuilder("#/");
            if (StoryId != null) sb.Append(StoryId);
            if (_query != null && _query.Count > 0)
            {
                bool first = true;
                // Sorted so the same state always yields the same link — a link that reshuffles
                // between runs cannot be compared, cached or used as a test expectation.
                var keys = new List<string>(_query.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys)
                {
                    sb.Append(first ? '?' : '&');
                    first = false;
                    sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(_query[k]));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses a hash, a full URL, or a bare id. Accepts the canonical
        /// <c>#/pkg/group/slug?K=v</c> form and the legacy <c>#story=pkg/group/slug&amp;K=v</c> form.
        /// Returns false when no story id can be read.
        /// </summary>
        public static bool TryParse(string text, out SusStoryRoute route)
        {
            route = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s = text.Trim();
            int hash = s.IndexOf('#');
            if (hash >= 0) s = s.Substring(hash + 1);
            else if (s.IndexOf("://", StringComparison.Ordinal) >= 0) return false;   // URL without a hash
            if (s.Length == 0) return false;

            string id;
            var query = new Dictionary<string, string>(StringComparer.Ordinal);

            if (s.StartsWith("story=", StringComparison.OrdinalIgnoreCase) ||
                s.IndexOf("&story=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Legacy form: every pair sits in one &-list and "story" is one of the keys.
                id = null;
                foreach (var pair in s.Split('&'))
                {
                    if (pair.Length == 0) continue;
                    SplitPair(pair, out var k, out var v);
                    if (string.Equals(k, "story", StringComparison.OrdinalIgnoreCase)) id = v;
                    else if (k.Length > 0) query[k] = v;
                }
            }
            else
            {
                int q = s.IndexOf('?');
                var path = q >= 0 ? s.Substring(0, q) : s;
                var tail = q >= 0 ? s.Substring(q + 1) : string.Empty;

                id = Uri.UnescapeDataString(path.Trim().Trim('/'));

                foreach (var pair in tail.Split('&'))
                {
                    if (pair.Length == 0) continue;
                    SplitPair(pair, out var k, out var v);
                    if (k.Length > 0) query[k] = v;
                }
            }

            if (string.IsNullOrWhiteSpace(id)) return false;
            route = new SusStoryRoute(id, query, fromUrl: true);
            return true;
        }

        /// <summary>Parses, or returns null.</summary>
        public static SusStoryRoute Parse(string text) => TryParse(text, out var r) ? r : null;

        static void SplitPair(string pair, out string key, out string value)
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                key = Uri.UnescapeDataString(pair);
                value = string.Empty;
                return;
            }
            key = Uri.UnescapeDataString(pair.Substring(0, eq));
            value = Uri.UnescapeDataString(pair.Substring(eq + 1));
        }

        // Equality ignores FromUrl on purpose: "the same address" is a fact about the address,
        // not about how it arrived. History de-duplication depends on this.
        public bool Equals(SusStoryRoute other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            if (!string.Equals(StoryId, other.StoryId, StringComparison.Ordinal)) return false;
            if (Query.Count != other.Query.Count) return false;
            foreach (var kv in Query)
            {
                if (!other.Query.TryGetValue(kv.Key, out var v)) return false;
                if (!string.Equals(kv.Value, v, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SusStoryRoute);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = StoryId != null ? StoryId.GetHashCode() : 0;
                foreach (var kv in Query)
                    h ^= kv.Key.GetHashCode() * 31 ^ (kv.Value != null ? kv.Value.GetHashCode() : 0);
                return h;
            }
        }

        public override string ToString() => ToHash();
    }
}

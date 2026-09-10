using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// One family of `docs-canon/data/dimension-scale.json` (T-3293, plan §4.1 D-7/D-18) — only
    /// the fields <see cref="RungResolver"/> needs to resolve `rung(family, rung)`. This is a
    /// compiler INPUT reader, not a mirror of the whole schema: the Node-side generator that
    /// produces this file and R133/dim-scale already own judging the data itself (monotonicity,
    /// dup tokens, pool/band…) on every commit — re-deciding any of that here would be a second judge
    /// for the same fact, the exact failure mode D-1/D-18 of the plan spend a paragraph avoiding.
    /// </summary>
    internal sealed class DimensionFamily
    {
        public string Name;

        /// <summary>`"variable" | "derived" | "invariant"` (dim-scale.mjs's own vocabulary).</summary>
        public string Breakpoint;

        /// <summary>Token NAME TEMPLATE, e.g. <c>--sk-control-h-{rung}</c> — null when the family
        /// uses <see cref="Tokens"/> instead (named-tier families: box-max, touch-min).</summary>
        public string Token;

        /// <summary>Explicit `tokenName -> rung` map — null when the family uses <see cref="Token"/>.</summary>
        public Dictionary<string, string> Tokens;

        public readonly List<string> Rungs = new();

        /// <summary>`steps[stepIndex][rungIndex]` — null for `derived`/`invariant` families.</summary>
        public List<List<long>> Steps;

        public readonly Dictionary<string, long> Bp = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> Dens = new(StringComparer.Ordinal);

        /// <summary>`derived` only: donor family name.</summary>
        public string DeriveFrom;

        /// <summary>`derived` only: `"half" | "neg"`.</summary>
        public string DeriveOp;

        /// <summary>
        /// True when this family carries a real rung ladder `rung()` can resolve against — a
        /// tiered `variable` family with its own steps, or a `derived` family riding a donor's.
        /// False for `invariant` families (border-width, radius, tracking, unity-slice,
        /// hairline-offset, nudge-neg — no `bp`/`dens`/`steps` by design, plan §0) and for any
        /// family missing rungs entirely: this is D-7's "family outside the ladder".
        /// </summary>
        public bool HasLadder => Rungs.Count > 0 && (Steps != null || Breakpoint == "derived");

        /// <summary>Token name for <paramref name="rung"/>, read from the family's own data
        /// (never invented from the call site) — null when the rung has no token entry.</summary>
        public string TokenNameFor(string rung)
        {
            if (Tokens != null)
            {
                foreach (var kv in Tokens)
                    if (string.Equals(kv.Value, rung, StringComparison.Ordinal))
                        return kv.Key;
                return null;
            }
            return string.IsNullOrEmpty(Token) ? null : Token.Replace("{rung}", rung);
        }
    }

    /// <summary>Parsed `docs-canon/data/dimension-scale.json` — see <see cref="DimensionFamily"/>.</summary>
    internal sealed class DimensionScale
    {
        public readonly Dictionary<string, DimensionFamily> Families = new(StringComparer.Ordinal);

        public static DimensionScale Parse(string json)
        {
            var scale = new DimensionScale();
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> root)) return scale;
            if (!root.TryGetValue("families", out var famsObj) || !(famsObj is Dictionary<string, object> famsDict))
                return scale;

            foreach (var kv in famsDict)
            {
                if (!(kv.Value is Dictionary<string, object> f)) continue;
                var fam = new DimensionFamily
                {
                    Name = kv.Key,
                    Breakpoint = AsString(f, "breakpoint"),
                    Token = AsString(f, "token"),
                };

                if (f.TryGetValue("tokens", out var toksObj) && toksObj is Dictionary<string, object> toksDict)
                {
                    fam.Tokens = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var t in toksDict) fam.Tokens[t.Key] = t.Value as string;
                }

                if (f.TryGetValue("rungs", out var rungsObj) && rungsObj is List<object> rungsList)
                    foreach (var r in rungsList) fam.Rungs.Add(Convert.ToString(r, CultureInfo.InvariantCulture));

                if (f.TryGetValue("steps", out var stepsObj) && stepsObj is List<object> stepsList)
                {
                    fam.Steps = new List<List<long>>();
                    foreach (var rowObj in stepsList)
                    {
                        var row = new List<long>();
                        if (rowObj is List<object> rowList)
                            foreach (var cell in rowList) row.Add(Convert.ToInt64(cell));
                        fam.Steps.Add(row);
                    }
                }

                if (f.TryGetValue("bp", out var bpObj) && bpObj is Dictionary<string, object> bpDict)
                    foreach (var b in bpDict) fam.Bp[b.Key] = Convert.ToInt64(b.Value);
                if (f.TryGetValue("dens", out var densObj) && densObj is Dictionary<string, object> densDict)
                    foreach (var d in densDict) fam.Dens[d.Key] = Convert.ToInt64(d.Value);

                if (f.TryGetValue("derive", out var derObj) && derObj is Dictionary<string, object> derDict)
                {
                    fam.DeriveFrom = AsString(derDict, "from");
                    fam.DeriveOp = AsString(derDict, "op");
                }

                scale.Families[kv.Key] = fam;
            }

            return scale;
        }

        /// <summary>Family names carrying a real ladder — used to compose "did you mean" errors.</summary>
        public IEnumerable<string> LadderFamilyNames() =>
            Families.Where(kv => kv.Value.HasLadder).Select(kv => kv.Key).OrderBy(n => n, StringComparer.Ordinal);

        private static string AsString(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) ? v as string : null;
    }
}

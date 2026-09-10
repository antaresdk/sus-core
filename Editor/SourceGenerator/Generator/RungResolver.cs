using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Compile-time resolution of <c>rung(&lt;family&gt;, &lt;rung&gt;)</c> — the ONLY arithmetic
    /// of the Sharq style layer (T-3293, plan §4.1 D-7). A declaration value like
    /// <c>min-height: rung(control-height, xs);</c> becomes
    /// <c>min-height: var(--sk-control-h-xs, 28px);</c>: the token NAME comes from the family's
    /// own <c>token</c>/<c>tokens</c> field in <c>docs-canon/data/dimension-scale.json</c> (never
    /// invented from the call site — "имя токена берётся из поля token семейства, фолбэк — из
    /// лестницы, а не из головы автора"), and the fallback NUMBER is the family's value at the
    /// <c>lg</c>×<c>default</c> step (T-3011's "base row" — the same step the sheet's un-suffixed
    /// <c>.breakpoint-lg</c>/no-density block resolves to).
    ///
    /// Runs as a pure TEXT pass over the raw <c>&lt;style&gt;</c> body inside
    /// <see cref="SharqFileParser"/>, before <see cref="CssScanner"/> ever sees it — so every
    /// downstream stage (<see cref="VariantsCompiler"/>, <see cref="ScopedCssGenerator"/>,
    /// <see cref="BuildMethodGenerator"/>) sees an ordinary <c>var(...)</c> call and needs zero
    /// awareness of <c>rung()</c> having existed, and both compilation entry points
    /// (<c>BuildMethodGenerator.GenerateCode</c> and <c>StyleParser.Parse</c>, called on the same
    /// <see cref="SharqFileModel"/> — T-3292's report) see IDENTICAL already-resolved text rather
    /// than risking two independent readings of the same call. None of the 194-file corpus or the
    /// 3 fixtures (T-3289/T-3290) contains the text <c>rung(</c> — the pass is a no-op on all of
    /// them (single substring check before the regex even runs), which is what keeps D-13's
    /// zero-diff guarantee true here BY CONSTRUCTION, exactly like T-3291/T-3292's own gates.
    ///
    /// "Семейство вне лестницы" / "рунг вне семейства" (card T-3293) are BOTH compile errors, not
    /// silent passthrough: a `rung()` call that fails to resolve throws, naming the `.sharq` file
    /// and the 1-based line the call sits on (computed by <see cref="SharqFileParser"/> from the
    /// real file, not just "somewhere in <style>" — plan says "с именем файла и строкой").
    /// </summary>
    internal static class RungResolver
    {
        public const string DataFileRel = "docs-canon/data/dimension-scale.json";

        private static readonly Regex CallRe = new(
            @"rung\(\s*([A-Za-z][\w-]*)\s*,\s*([A-Za-z0-9][\w-]*)\s*\)", RegexOptions.Compiled);

        // Process-wide: the ladder is static data that does not change mid-compile, and every
        // domain reload (each EditMode test run included) clears this anyway.
        private static DimensionScale _cachedScale;
        private static string _cachedFromDir;

        /// <summary>
        /// Resolves every <c>rung(...)</c> call in <paramref name="body"/> (the already-trimmed
        /// <c>&lt;style&gt;</c> text). Returns <paramref name="body"/> UNCHANGED (same reference)
        /// when it contains no <c>rung(</c> at all — the corpus/fixture fast path. Throws
        /// <see cref="InvalidOperationException"/> naming file + line for every call that failed
        /// to resolve (collected, not one-at-a-time, so a file with several bad calls reports all
        /// of them in one message — same convention <see cref="StyleParser"/> already uses).
        /// </summary>
        public static string ResolveAll(string body, string sourcePath, string className, int startLine)
        {
            if (string.IsNullOrEmpty(body) || body.IndexOf("rung(", StringComparison.Ordinal) < 0)
                return body;

            var scale = LoadScale(sourcePath, out var loadError);
            var fileLabel = string.IsNullOrEmpty(className) ? Path.GetFileName(sourcePath) : className + ".sharq";
            return ResolveAll(body, scale, loadError, fileLabel, startLine);
        }

        /// <summary>
        /// Pure overload — no disk access, takes an already-parsed <paramref name="scale"/>
        /// (null ⇒ every call fails with <paramref name="loadError"/>). Exists so the resolution
        /// arithmetic (<see cref="TryResolve"/>) is testable against a small synthetic ladder
        /// without depending on the real, evolving `docs-canon/data/dimension-scale.json` — the
        /// disk-backed overload above is a thin wrapper around this one.
        /// </summary>
        internal static string ResolveAll(string body, DimensionScale scale, string loadError,
            string fileLabel, int startLine)
        {
            if (string.IsNullOrEmpty(body) || body.IndexOf("rung(", StringComparison.Ordinal) < 0)
                return body;

            var errors = new List<string>();

            var result = CallRe.Replace(body, m =>
            {
                var line = startLine + CountNewlines(body, 0, m.Index);
                if (scale == null)
                {
                    errors.Add($"{fileLabel}:{line}: rung({m.Groups[1].Value}, {m.Groups[2].Value}): {loadError}");
                    return m.Value;
                }
                if (!TryResolve(scale, m.Groups[1].Value, m.Groups[2].Value, out var replacement, out var err))
                {
                    errors.Add($"{fileLabel}:{line}: {err}");
                    return m.Value;
                }
                return replacement;
            });

            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" | ", errors));

            return result;
        }

        internal static bool TryResolve(DimensionScale scale, string familyName, string rung,
            out string replacement, out string error)
        {
            replacement = null;

            if (!scale.Families.TryGetValue(familyName, out var fam) || !fam.HasLadder)
            {
                error = $"rung(): семейство «{familyName}» вне лестницы размеров ({DataFileRel}) — "
                    + $"доступны: {string.Join(", ", scale.LadderFamilyNames())}";
                return false;
            }

            if (!fam.Rungs.Contains(rung))
            {
                error = $"rung(): рунг «{rung}» вне семейства «{familyName}» — "
                    + $"доступны рунги: {string.Join(", ", fam.Rungs)}";
                return false;
            }

            var tokenName = fam.TokenNameFor(rung);
            if (string.IsNullOrEmpty(tokenName))
            {
                error = $"rung(): у семейства «{familyName}» нет имени токена для рунга «{rung}» "
                    + "— дефект данных лестницы, не вызова";
                return false;
            }

            if (!TryValueAtBaseRow(scale, fam, rung, out var value, out error))
                return false;

            replacement = $"var({tokenName}, {value}px)";
            return true;
        }

        /// <summary>Value at the `lg`×`default` step — T-3011's base row (D-7's "значение на
        /// ступени lg × default"). `derived` families (pill, space-neg) have no ladder of their
        /// own: the step and the raw number both come from the donor, then <c>DeriveOp</c>
        /// (half/neg) is applied — the SAME arithmetic <c>dim-scale.mjs</c>'s `valuesOf` uses to
        /// build the shipped `.g.uss` sheet, read here rather than re-derived by a second formula.</summary>
        private static bool TryValueAtBaseRow(DimensionScale scale, DimensionFamily fam, string rung,
            out long value, out string error)
        {
            value = 0;
            error = null;

            if (fam.Breakpoint == "derived")
            {
                if (fam.DeriveFrom == null || !scale.Families.TryGetValue(fam.DeriveFrom, out var donor)
                    || donor.Steps == null)
                {
                    error = $"rung(): семейство «{fam.Name}» ссылается на донора «{fam.DeriveFrom}», "
                        + $"которого в {DataFileRel} нет как лестницы — дефект данных, не вызова";
                    return false;
                }

                var donorRungIndex = donor.Rungs.IndexOf(rung);
                if (donorRungIndex < 0)
                {
                    error = $"rung(): донор «{fam.DeriveFrom}» семейства «{fam.Name}» не несёт рунг "
                        + $"«{rung}» — дефект данных, не вызова";
                    return false;
                }

                var donorStep = ClampStep(donor);
                var raw = donor.Steps[donorStep][donorRungIndex];
                value = fam.DeriveOp == "half"
                    ? (long)Math.Round(raw / 2.0, MidpointRounding.AwayFromZero)
                    : fam.DeriveOp == "neg" ? -raw : raw;
                return true;
            }

            var rungIndex = fam.Rungs.IndexOf(rung);
            var step = ClampStep(fam);
            if (fam.Steps == null || step >= fam.Steps.Count || rungIndex >= fam.Steps[step].Count)
            {
                error = $"rung(): семейство «{fam.Name}» не несёт значение на ступени lg×default для "
                    + $"рунга «{rung}» — дефект данных лестницы, не вызова";
                return false;
            }

            value = fam.Steps[step][rungIndex];
            return true;
        }

        /// <summary>`clamp(bp[lg] + dens[default], 0, len(steps)-1)` — same formula as
        /// `dim-scale.mjs`'s `stepOf`, evaluated at the fixed `lg`×`default` pair only (D-7 does
        /// not vary this pair, unlike the shipped sheet, which emits all 15 blocks).</summary>
        private static int ClampStep(DimensionFamily fam)
        {
            var raw = (fam.Bp.TryGetValue("lg", out var b) ? b : 0)
                + (fam.Dens.TryGetValue("default", out var d) ? d : 0);
            var len = fam.Steps?.Count ?? 1;
            return (int)Math.Max(0, Math.Min(len - 1, raw));
        }

        private static DimensionScale LoadScale(string sourcePath, out string error)
        {
            error = null;

            var dir = string.IsNullOrEmpty(sourcePath) ? null : Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (_cachedScale != null && string.Equals(_cachedFromDir, FindRoot(dir), StringComparison.OrdinalIgnoreCase))
                return _cachedScale;

            var found = FindUpwards(dir, DataFileRel);
            if (found == null)
            {
                error = $"`{DataFileRel}` не найден ни в одном родительском каталоге `{sourcePath}` "
                    + "— rung() резолвится только внутри дерева, где рядом лежит лестница размеров "
                    + "(докос-канон монорепо); отдельно распространяемый .sharq без неё скомпилировать нельзя";
                return null;
            }

            try
            {
                var json = File.ReadAllText(found);
                var scale = DimensionScale.Parse(json);
                _cachedScale = scale;
                _cachedFromDir = FindRoot(dir);
                return scale;
            }
            catch (Exception ex)
            {
                error = $"`{found}` не читается как JSON: {ex.Message}";
                return null;
            }
        }

        private static string FindRoot(string startDir)
        {
            var found = FindUpwards(startDir, DataFileRel);
            return found == null ? null : Path.GetDirectoryName(found);
        }

        private static string FindUpwards(string startDir, string relPath)
        {
            var relNative = relPath.Replace('/', Path.DirectorySeparatorChar);
            var dir = startDir;
            for (var depth = 0; depth < 32 && !string.IsNullOrEmpty(dir); depth++)
            {
                var candidate = Path.Combine(dir, relNative);
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private static int CountNewlines(string s, int from, int to)
        {
            var n = 0;
            var end = Math.Min(to, s.Length);
            for (var i = from; i < end; i++)
                if (s[i] == '\n') n++;
            return n;
        }
    }
}

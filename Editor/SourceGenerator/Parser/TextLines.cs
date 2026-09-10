namespace Sharq.Core.Editor
{
    /// <summary>
    /// Shared newline-counting helper (T-3295, plan §4.4 карта соответствия): turns a
    /// character range inside a string into a 1-based line delta. New code (<see
    /// cref="CssScanner"/>'s per-node <c>DeclLine</c>, <see cref="StyleParser"/>'s
    /// identity-map path, <see cref="SharqSourceMapWriter"/>) shares this instead of
    /// growing its own private copy — <c>SharqFileParser</c>'s pre-existing one (T-3293,
    /// rung() error lines) is left untouched: it already ships tested, and this card's
    /// job is the map, not a refactor of code steps 1-5 already proved correct.
    /// </summary>
    internal static class TextLines
    {
        public static int CountNewlines(string s, int from, int to)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var n = 0;
            var end = to < s.Length ? to : s.Length;
            for (var i = from; i < end; i++)
                if (s[i] == '\n') n++;
            return n;
        }
    }
}

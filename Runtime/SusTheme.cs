namespace Sharq.Core
{
    /// <summary>
    /// Named theme identifier.
    /// Use static constants (Dark, Light) or <c>new SusTheme("midnight")</c> for custom themes.
    /// </summary>
    public readonly struct SusTheme
    {
        /// <summary>Theme class suffix — applied as <c>.theme-{Name}</c>.</summary>
        public string Name { get; }

        /// <summary>Creates a named theme. Null/empty defaults to "dark".</summary>
        public SusTheme(string name)
        {
            Name = string.IsNullOrEmpty(name) ? "dark" : name;
        }

        /// <summary>Dark theme (default).</summary>
        public static SusTheme Dark => new("dark");

        /// <summary>Light theme.</summary>
        public static SusTheme Light => new("light");

        /// <summary>Returns the CSS class: <c>"theme-dark"</c>, <c>"theme-light"</c>, etc.</summary>
        public string CssClass => $"theme-{Name}";

        /// <inheritdoc />
        public override string ToString() => Name;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is SusTheme other && other.Name == Name;

        /// <inheritdoc />
        public override int GetHashCode() => Name?.GetHashCode() ?? 0;

        public static bool operator ==(SusTheme a, SusTheme b) => a.Name == b.Name;
        public static bool operator !=(SusTheme a, SusTheme b) => a.Name != b.Name;
    }
}

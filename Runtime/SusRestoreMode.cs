namespace Sharq.Core
{
    /// <summary>What happens to animated inline styles when a motion completes or stops.</summary>
    public enum SusRestoreMode
    {
        /// <summary>Leave final animated values on the element.</summary>
        Keep,

        /// <summary>Restore inline style values captured at Play start.</summary>
        Snapshot,

        /// <summary>Clear animated inline props to <see cref="UnityEngine.UIElements.StyleKeyword.Null"/> (cascade wins).</summary>
        KeywordNull,
    }
}

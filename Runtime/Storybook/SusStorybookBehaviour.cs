using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// Scene entry point: puts a <see cref="SusStorybookHost"/> on a <see cref="UIDocument"/>.
    ///
    /// The shell stylesheet arrives as a SERIALISED REFERENCE, not through <c>Resources.Load</c>
    /// (plan §0.1): a package's <c>Resources</c> folder is copied into every player build
    /// unconditionally and is never trimmed by usage, so a buyer who never opens the storybook
    /// would pay for its skin in bytes. A GUID reference costs nothing until something points at
    /// it — which is exactly the guarantee DoD §7 p. 11 checks.
    ///
    /// Leave the field empty in the Editor and the sheet is resolved by package path instead;
    /// in a player build an empty field means the shell renders unstyled, which is a visible
    /// defect rather than a silent one.
    /// </summary>
    [AddComponentMenu("SUS/Storybook Host")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class SusStorybookBehaviour : MonoBehaviour
    {
        /// <summary>Path used only in the Editor when <see cref="_styleSheet"/> is empty.</summary>
        public const string StyleSheetPackagePath =
            "Packages/com.sharq-it.sus.core/Runtime/Storybook/Storybook.uss";

        [Tooltip("Shell stylesheet (Runtime/Storybook/Storybook.uss). Referenced by GUID so the " +
                 "engine ships no assets under Resources/.")]
        [SerializeField] StyleSheet _styleSheet;

        [Tooltip("Document to mount into. Defaults to the UIDocument on this GameObject.")]
        [SerializeField] UIDocument _document;

        SusStorybookHost _host;

        /// <summary>The live shell, or null before <c>OnEnable</c>.</summary>
        public SusStorybookHost Host => _host;

        void OnEnable()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                SusLog.Error("[storybook] no UIDocument root to mount into.");
                return;
            }

            SusBootstrap.LoadTokenCascade(root);

            _host = new SusStorybookHost(ResolveStyleSheet());
            root.Add(_host);
            _host.Focus();
        }

        void OnDisable()
        {
            if (_host == null) return;
            _host.Dispose();
            _host.RemoveFromHierarchy();
            _host = null;
        }

        StyleSheet ResolveStyleSheet()
        {
            if (_styleSheet != null) return _styleSheet;
#if UNITY_EDITOR
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPackagePath);
            if (sheet == null)
                SusLog.Warn("[storybook] shell stylesheet not found at " + StyleSheetPackagePath);
            return sheet;
#else
            SusLog.Warn("[storybook] no shell stylesheet assigned — the shell will render unstyled.");
            return null;
#endif
        }
    }
}

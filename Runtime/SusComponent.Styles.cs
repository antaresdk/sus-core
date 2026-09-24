using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Companion USS auto-loading: resolves and (re)loads the generated stylesheet(s) for a
    /// component's own type from Resources (with an editor-only fallback scan for stale/missing
    /// copies), and the hot-reload variants used by UssHotReloadService.
    /// </summary>
    public abstract partial class SusComponent
    {
        // ─── USS Auto-loading ────────────────────────────────────────────

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only development fallback dirs for companion USS resolution when the
        /// Resources copy is missing/stale. Defaults to the current project's Sharq output
        /// and sus-core's own Generated dir. Downstream packages register their own style
        /// output dir via <see cref="RegisterEditorGeneratedDir"/> — core has no hardcoded
        /// knowledge of them. (ARCH-20260917-PKG-REFACTOR-I §5 S1) A package descriptor's
        /// <c>uss</c> mode decides WHERE that dir is (its own <c>Generated</c> for the legacy
        /// default, or its Resources mirror for <c>"uss": "resources"</c>) — this fallback
        /// list is agnostic to which, it just needs the right path registered.
        /// </summary>
        private static readonly System.Collections.Generic.List<string> s_editorGeneratedDirs = new()
        {
            "Assets/SusUI/Generated",                                 // project-local
            "Packages/com.sharq-it.sus.core/Runtime/Generated",      // core's own (self-reference OK)
        };

        /// <summary>
        /// Registers an editor-only fallback directory (AssetDatabase path, e.g.
        /// "Packages/com.example.ui/Runtime/Generated") for companion USS lookup.
        /// Idempotent. Editor-only — production builds resolve via Resources.
        /// </summary>
        public static void RegisterEditorGeneratedDir(string assetDir)
        {
            if (string.IsNullOrEmpty(assetDir)) return;
            if (!s_editorGeneratedDirs.Contains(assetDir))
                s_editorGeneratedDirs.Add(assetDir);
        }
#endif

        private static readonly string[] s_companionSuffixes = { "_static.g", "_scoped.g", ".g" };

        /// <summary>
        /// Caches, per most-derived component type, which type in its base-type chain actually
        /// owns the companion stylesheet(s) found on disk. A Tier-B C# subclass with no
        /// .sharq of its own (e.g. LfScoreboard : SusTable, LfNavButton : SusButton) has nothing
        /// under its OWN name — without this fallback its base's entire companion stylesheet
        /// silently never attaches. Caching avoids repeating the failing exact-name lookup (and
        /// the walk up the chain) on every construction of the same derived type — same pattern
        /// as <see cref="s_updatedOverrideCache"/> / prop-accessor cache.
        /// </summary>
        private static readonly Dictionary<Type, Type> s_companionOwnerTypeCache = new Dictionary<Type, Type>();

        /// <summary>
        /// Loads all companion USS files for this component from Resources/SusRuntime/.
        /// Tries: {ClassName}_static.g.uss, {ClassName}_scoped.g.uss, {ClassName}.g.uss
        /// Called automatically from generated Build().
        /// </summary>
        protected void LoadCompanionStyleSheets() => LoadCompanionStyleSheets(null);

        /// <summary>
        /// Loads companion USS, optionally restricted to specific suffixes
        /// ("_static.g" / "_scoped.g" / ".g") for partial hot reload.
        ///
        /// Resolves the companion filename from <c>GetType()</c> first (exact match — the common
        /// case, and unchanged behavior for any type that has its own companion sheet). When that
        /// yields nothing at all, walks up the base-type chain (stopping at <see cref="SusComponent"/>)
        /// so a Tier-B subclass with no .sharq of its own inherits its nearest styled ancestor's
        /// companion stylesheet(s) instead of silently getting none.
        /// </summary>
        private void LoadCompanionStyleSheets(ICollection<string> onlySuffixes)
        {
            LoadCompanionStyleSheetsCore(onlySuffixes);
            // A (re)load appends companion sheets at the end — keep trailing sheets after them.
            OnOwnStyleSheetsChanged();
        }

        private void LoadCompanionStyleSheetsCore(ICollection<string> onlySuffixes)
        {
            var mostDerived = GetType();

            // Fast path for the full load (the hot path — every construction): reuse the owner
            // type resolved for a prior instance of the same class instead of re-walking.
            if (onlySuffixes == null && s_companionOwnerTypeCache.TryGetValue(mostDerived, out var cachedOwner))
            {
                LoadCompanionStyleSheetsForType(cachedOwner, onlySuffixes);
                return;
            }

            for (var type = mostDerived; type != null; type = type.BaseType)
            {
                var anyLoaded = LoadCompanionStyleSheetsForType(type, onlySuffixes);
                if (anyLoaded)
                {
                    if (onlySuffixes == null)
                        s_companionOwnerTypeCache[mostDerived] = type;
                    return;
                }
                if (type == typeof(SusComponent)) break; // reached root — nothing found anywhere
            }

            // Nothing found anywhere in the chain (a component that simply has no companion USS
            // at all — the common case for plain code-only components). Cache the type itself, NOT
            // the root ancestor: RemoveCompanionStyleSheets/ApplyHotReloadStyleSheet key off this
            // cached owner too, and must keep matching sheet names built from THIS type's own name
            // (e.g. hand-added sheets named "<mostDerived>_scoped.g" in hot-reload/tests) — caching
            // the root here would make removal look for "SusComponent_scoped.g" instead and silently
            // fail to match anything.
            if (onlySuffixes == null)
                s_companionOwnerTypeCache[mostDerived] = mostDerived;
        }

        /// <summary>Loads companion sheets named after <paramref name="type"/> specifically (no fallback).</summary>
        private bool LoadCompanionStyleSheetsForType(Type type, ICollection<string> onlySuffixes)
        {
            var name = type.Name;
            var anyLoaded = false;
            foreach (var suf in s_companionSuffixes)
            {
                if (onlySuffixes != null && !onlySuffixes.Contains(suf)) continue;
                var fileName = suf.Length > 0 ? $"{name}{suf}" : name;
                var sheet = UnityEngine.Resources.Load<StyleSheet>(SusBootstrap.ResourcePath + fileName);

#if UNITY_EDITOR
                // Development fallback: Resources is the primary path; when the copy is
                // missing/stale, scan the registered Generated dirs (project + packages).
                if (sheet == null)
                {
                    foreach (var dir in s_editorGeneratedDirs)
                    {
                        sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>($"{dir}/{fileName}.uss");
                        if (sheet != null) break;
                    }
                }
#endif

                if (sheet != null)
                {
                    styleSheets.Add(sheet);
                    anyLoaded = true;
                }
            }
            return anyLoaded;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || SUS_RUNTIME_MCP
        /// <summary>
        /// Reload companion stylesheets (.g.uss, _scoped.g.uss, _static.g.uss)
        /// without recreating the component. Used by UssHotReloadService for hot reload.
        /// Removes old companion sheets, loads new ones, and forces a redraw.
        /// <paramref name="onlySuffixes"/> — limit reload to specific
        /// suffixes ("_static.g" / "_scoped.g" / ".g"); null = all.
        /// </summary>
        public void ReloadCompanionStyleSheets(System.Collections.Generic.ICollection<string> onlySuffixes = null)
        {
            RemoveCompanionStyleSheets(onlySuffixes);

            // Reload from Resources / Editor fallback
            LoadCompanionStyleSheets(onlySuffixes);

            // Force UI Toolkit to re-resolve styles
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Remove companion sheets without reloading (source deleted — stale styles).
        /// Keeps the global cascade (theme, tokens, reset, etc.) intact.
        /// </summary>
        public void RemoveCompanionStyleSheets(System.Collections.Generic.ICollection<string> onlySuffixes = null)
        {
            // Tier-B subclasses load companion sheets under an ANCESTOR type's name
            // (e.g. LfScoreboard loads "SusTable.g.uss"); remove by the resolved owner type so
            // hot-reload actually strips the sheets that were actually added, not zero of them.
            var mostDerived = GetType();
            var ownerType = s_companionOwnerTypeCache.TryGetValue(mostDerived, out var cached) ? cached : mostDerived;
            var className = ownerType.Name;

            for (int i = styleSheets.count - 1; i >= 0; i--)
            {
                var sheet = styleSheets[i];
                if (sheet == null) continue;

                var sheetName = sheet.name;
                if (sheetName == null || !sheetName.Contains(className) || !sheetName.Contains(".g"))
                    continue;

                if (onlySuffixes != null)
                {
                    var match = false;
                    foreach (var suf in onlySuffixes)
                        if (sheetName == $"{className}{suf}") { match = true; break; }
                    if (!match) continue;
                }

                styleSheets.Remove(sheet);
            }

            OnOwnStyleSheetsChanged();
        }

        /// <summary>
        /// Hot-reload: replace companion sheet for <paramref name="suffix"/> with an in-memory
        /// <see cref="StyleSheet"/> (from USS text). Used by <c>SusRuntimeHotReload.ApplyUss</c>.
        /// </summary>
        public void ApplyHotReloadStyleSheet(string suffix, StyleSheet sheet)
        {
            if (sheet == null || string.IsNullOrEmpty(suffix)) return;
            var className = GetType().Name;
            var expectedName = $"{className}{suffix}";
            sheet.name = expectedName;

            for (int i = styleSheets.count - 1; i >= 0; i--)
            {
                var existing = styleSheets[i];
                if (existing != null && existing.name == expectedName)
                    styleSheets.Remove(existing);
            }

            styleSheets.Add(sheet);
            OnOwnStyleSheetsChanged();
            MarkDirtyRepaint();
        }
#endif
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// One-click project onboarding for SUS (integration roadmap 2.1).
    /// Menu: <c>Tools → SUS → Setup Project</c>.
    ///
    /// Automates the manual steps a new project used to need:
    ///   1. Environment check   — Unity ≥ 6000, packages core/router, UI Toolkit
    ///   2. sus.config.json     — writes Assets/sus.config.json with the chosen UI root
    ///   3. Scaffold            — optional HomeScreen.sharq + starter MonoBehaviour
    ///   4. First Sharq compile — regenerates .g.cs/.uss so the starter compiles
    ///                            (solves the "chicken-and-egg" without GenerateSharq.ps1)
    ///   5. Scene               — GameObject + UIDocument + PanelSettings, starter attached
    ///
    /// The starter component is attached to the scene GameObject on the NEXT domain reload
    /// (its type doesn't exist until the freshly-written .cs compiles) — see
    /// <see cref="SusSetupFinisher"/>.
    /// </summary>
    public sealed class SusSetupWizard : EditorWindow
    {
        // ─── User input ──────────────────────────────────────────────────
        private string _uiRoot = "Assets/SusUI";
        private string _appName = "MyApp";
        private bool _createExampleScreen = true;
        private bool _createCustomization = true;
        private bool _createScene = true;
        private string _sceneName = "SusApp";
        private Vector2 _scroll;

        // ─── Environment status (refreshed on open / button) ─────────────
        private bool _unityOk, _uitkOk, _coreOk, _routerOk;
        private string _unityVersion = "";

        [MenuItem("Window/SUS/Setup Project", priority = 0)]
        public static void Open()
        {
            var w = GetWindow<SusSetupWizard>(true, "SUS — Setup Project", true);
            w.minSize = new Vector2(480, 560);
            w.RefreshEnvironment();
            w.Show();
        }

        private void OnEnable() => RefreshEnvironment();

        // ─── Environment checks ──────────────────────────────────────────

        private void RefreshEnvironment()
        {
            _unityVersion = Application.unityVersion;
            _unityOk = ParseMajor(_unityVersion) >= 6000;
            _uitkOk = typeof(UIDocument) != null;
            _coreOk = typeof(SusApp) != null; // this assembly references core
            _routerOk = TypeExists("Sharq.Router.SusRouter", "com.sharq-it.sus.router");
        }

        private static int ParseMajor(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            var dot = version.IndexOf('.');
            var head = dot > 0 ? version.Substring(0, dot) : version;
            return int.TryParse(head, out var major) ? major : 0;
        }

        private static bool TypeExists(string fullName, string assemblyName)
        {
            if (Type.GetType($"{fullName}, {assemblyName}") != null) return true;
            // Fallback: scan loaded assemblies (assembly name may differ from asmdef name).
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetType(fullName) != null) return true;
                }
                catch (ReflectionTypeLoadException) { /* skip */ }
            }
            return false;
        }

        // ─── GUI ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("SUS Project Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configures a fresh project for SUS: writes sus.config.json, scaffolds an " +
                "optional example screen + starter, runs the first Sharq generation, and " +
                "creates a ready-to-Play scene.", MessageType.Info);

            DrawEnvironment();
            EditorGUILayout.Space(6);
            DrawOptions();
            EditorGUILayout.Space(10);
            DrawRunButton();

            EditorGUILayout.EndScrollView();
        }

        private void DrawEnvironment()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                        RefreshEnvironment();
                }

                StatusRow($"Unity {_unityVersion} (≥ 6000)", _unityOk, required: true);
                StatusRow("UI Toolkit (UIDocument)", _uitkOk, required: true);
                StatusRow("sus-core package", _coreOk, required: true);
                StatusRow("sus-router package (navigation)", _routerOk, required: false);
            }
        }

        private static void StatusRow(string label, bool ok, bool required)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var mark = ok ? "\u2713" : (required ? "\u2717" : "\u2013");
                var color = ok ? new Color(0.4f, 0.8f, 0.4f)
                              : (required ? new Color(0.9f, 0.4f, 0.4f) : Color.gray);
                var prev = GUI.color;
                GUI.color = color;
                GUILayout.Label(mark, GUILayout.Width(16));
                GUI.color = prev;
                GUILayout.Label(label);
                GUILayout.FlexibleSpace();
                if (!ok)
                    GUILayout.Label(required ? "required" : "optional", EditorStyles.miniLabel);
            }
        }

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

            _uiRoot = EditorGUILayout.TextField(
                new GUIContent("UI Root Folder", "Where your .sharq sources live. Generated " +
                    "code + runtime USS go into <root>/Generated (auto, safe to gitignore)."),
                _uiRoot);

            if (!string.IsNullOrEmpty(_uiRoot))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Generated", _uiRoot + "/Generated", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Resources", _uiRoot + "/Generated/Resources/SusRuntime", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            _createExampleScreen = EditorGUILayout.Toggle(
                new GUIContent("Create example screen", "Scaffolds HomeScreen.sharq + a starter " +
                    "MonoBehaviour wired to it."), _createExampleScreen);

            using (new EditorGUI.DisabledScope(!_createExampleScreen && !_createScene))
                _appName = EditorGUILayout.TextField(
                    new GUIContent("Starter class name"), _appName);

            EditorGUILayout.Space(4);
            _createCustomization = EditorGUILayout.Toggle(
                new GUIContent("Create customization scaffold",
                    "Creates <root>/Customization/ with live, editable examples of the four " +
                    "customization axes: tokens (branding.uss), fonts (AppFonts.asset), " +
                    "icons (AppIcons) and a custom component (AppButton.sharq), all wired " +
                    "through the starter."), _createCustomization);

            EditorGUILayout.Space(4);
            _createScene = EditorGUILayout.Toggle(
                new GUIContent("Create & open scene", "Creates a scene with a UIDocument + " +
                    "PanelSettings and attaches the starter."), _createScene);

            using (new EditorGUI.DisabledScope(!_createScene))
                _sceneName = EditorGUILayout.TextField(new GUIContent("Scene name"), _sceneName);
        }

        private void DrawRunButton()
        {
            bool blocked = !_unityOk || !_uitkOk || !_coreOk || string.IsNullOrWhiteSpace(_uiRoot);
            if (!IsValidAssetsPath(_uiRoot))
            {
                EditorGUILayout.HelpBox("UI Root Folder must be inside 'Assets/'.", MessageType.Warning);
                blocked = true;
            }
            if (!IsValidIdentifier(_appName) && (_createExampleScreen || _createScene))
            {
                EditorGUILayout.HelpBox("Starter class name must be a valid C# identifier.", MessageType.Warning);
                blocked = true;
            }

            using (new EditorGUI.DisabledScope(blocked))
            {
                if (GUILayout.Button("Run Setup", GUILayout.Height(34)))
                    RunSetup();
            }
        }

        // ─── Setup execution ─────────────────────────────────────────────

        private void RunSetup()
        {
            try
            {
                var root = Directory.GetCurrentDirectory();
                string generatedDir = _uiRoot + "/Generated";
                string resourcesDir = _uiRoot + "/Generated/Resources/SusRuntime";

                Directory.CreateDirectory(Path.Combine(root, _uiRoot));
                Directory.CreateDirectory(Path.Combine(root, resourcesDir));

                // 1. Config
                WriteConfig(_uiRoot, generatedDir, resourcesDir);
                SusConfig.Reload();

                // 2. Scaffold from committed Starter~ (HomeScreen.sharq + pre-generated .g.cs + SusApp entry).
                //    Avoids chicken-and-egg: Mount<HomeScreen>() compiles without waiting for RegenerateAll.
                var uiRootAbs = Path.Combine(root, _uiRoot);
                if (_createExampleScreen)
                {
                    SusStarterAssets.CopyHomeScreen(uiRootAbs);
                    SusStarterAssets.CopyAppEntry(uiRootAbs, _appName,
                        withExample: true, withCustomization: _createCustomization);
                }
                else if (_createScene)
                {
                    SusStarterAssets.CopyAppEntry(uiRootAbs, _appName,
                        withExample: false, withCustomization: _createCustomization);
                }

                // 2b. Customization scaffold
                string customizationMsg = "";
                if (_createCustomization)
                    customizationMsg = CreateCustomizationScaffold(root);

                AssetDatabase.Refresh();

                // 3. Base HomeScreen.g.cs is already copied from Starter~.

                // 4. PanelSettings
                string panelPath = _uiRoot + "/SusPanelSettings.asset";
                var ps = LoadOrCreatePanelSettings(panelPath);

                // 5. Scene (+ deferred starter attach after the new .cs compiles)
                string sceneMsg = "";
                if (_createScene)
                    sceneMsg = CreateScene(panelPath, _appName, out _);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("SUS Setup",
                    "Setup complete.\n\n" +
                    $"• Config: Assets/sus.config.json\n" +
                    $"• UI root: {_uiRoot}\n" +
                    (_createExampleScreen
                        ? $"• Example: {_uiRoot}/HomeScreen.sharq + Generated/HomeScreen.g.cs + {_appName}.cs\n"
                        : "") +
                    customizationMsg +
                    (ps != null ? $"• Panel: {panelPath}\n" : "") +
                    sceneMsg +
                    "\nWait for the scripts to finish compiling, then press Play.",
                    "OK");

                Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SusSetup] Failed: {ex}");
                EditorUtility.DisplayDialog("SUS Setup", "Setup failed — see Console for details.", "OK");
            }
        }

        private static void WriteConfig(string sharqDir, string generatedDir, string resourcesDir)
        {
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"    \"SharqDirectory\": \"{sharqDir}\",");
            json.AppendLine($"    \"GeneratedDirectory\": \"{generatedDir}\",");
            json.AppendLine($"    \"ResourcesDirectory\": \"{resourcesDir}\",");
            json.AppendLine("    \"EnableValidation\": true,");
            json.AppendLine("    \"StrictVForKey\": true,");
            json.AppendLine("    \"LogGeneratedFiles\": true");
            json.AppendLine("}");

            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "sus.config.json");
            File.WriteAllText(configPath, json.ToString(), new UTF8Encoding(false));
        }

        private static void WriteIfMissing(string absPath, string content)
        {
            if (File.Exists(absPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(absPath));
            File.WriteAllText(absPath, content, new UTF8Encoding(false));
        }

        private static PanelSettings LoadOrCreatePanelSettings(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
            if (existing != null) return existing;

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = Path.GetFileNameWithoutExtension(assetPath);
            // ConstantPixelSize: breakpoints see real panel width — no Unity auto-scale.
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            AssetDatabase.CreateAsset(ps, assetPath);
            return ps;
        }

        private string CreateScene(string panelSettingsPath, string appName, out string scenePath)
        {
            scenePath = _uiRoot + "/" + _sceneName + ".unity";

            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            // DefaultGameObjects includes Main Camera + Directional Light — EmptyScene
            // leaves Game View with "No cameras rendering" over the UIDocument overlay.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject(appName);
            var uidoc = go.AddComponent<UIDocument>();

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            if (ps != null)
            {
                var so = new SerializedObject(uidoc);
                var prop = so.FindProperty("m_PanelSettings");
                if (prop != null)
                {
                    prop.objectReferenceValue = ps;
                    so.ApplyModifiedProperties();
                }
            }

            // The starter type doesn't exist yet (its .cs was just written). Attach it on
            // the next domain reload once it compiles.
            string fontsAssetPath = _createCustomization
                ? _uiRoot + "/Customization/Fonts/AppFonts.asset" : "";

            var starterType = FindType(appName);
            if (starterType != null)
            {
                var comp = go.AddComponent(starterType);
                SusSetupFinisher.TryBindFontsAsset(comp, fontsAssetPath);
            }
            else
            {
                SessionState.SetString(SusSetupFinisher.PendingSceneKey, scenePath);
                SessionState.SetString(SusSetupFinisher.PendingGoKey, appName);
                SessionState.SetString(SusSetupFinisher.PendingTypeKey, appName);
                SessionState.SetString(SusSetupFinisher.PendingFontsKey, fontsAssetPath);
            }

            EditorSceneManager.SaveScene(scene, scenePath);
            return $"• Scene: {scenePath}\n";
        }

        internal static Type FindType(string simpleOrFullName)
        {
            if (string.IsNullOrEmpty(simpleOrFullName)) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.FullName == simpleOrFullName || t.Name == simpleOrFullName)
                    {
                        if (typeof(MonoBehaviour).IsAssignableFrom(t))
                            return t;
                    }
                }
            }
            return null;
        }

        // ─── Validation helpers ──────────────────────────────────────────

        private static bool IsValidAssetsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var p = path.Replace("\\", "/").TrimEnd('/');
            return p == "Assets" || p.StartsWith("Assets/");
        }

        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
            return name.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        // ─── Customization scaffold ──────────────────────────────────────

        /// <summary>
        /// Creates <c>{uiRoot}/Customization/</c> — a designated, discoverable place for
        /// project-level SUS customization with one live example per axis:
        /// tokens (branding.uss), fonts (AppFonts.asset), icons (AppIcons collection)
        /// and a custom component (AppButton.sharq, optional downstream package). Idempotent: existing
        /// files are never overwritten.
        /// </summary>
        private string CreateCustomizationScaffold(string projectRoot)
        {
            var custRel = _uiRoot + "/Customization";
            var custAbs = Path.Combine(projectRoot, custRel);

            WriteIfMissing(Path.Combine(custAbs, "README.md"),
                BuildCustomizationReadme());

            // Tokens: UseCustomStyles loads via Resources.Load, so the file must live
            // under a Resources/ folder — resource path is just "branding".
            WriteIfMissing(Path.Combine(custAbs, "Theme", "Resources", "branding.uss"),
                BrandingUssTemplate);

            // Icons: ResourcesFolderIconProvider("app") resolves
            // Resources.Load("SusRuntime/Icons/app/{weight}/{name}") — from ANY Resources folder.
            WriteIfMissing(
                Path.Combine(custAbs, "Icons", "Resources", "SusRuntime", "Icons", "app", "regular", "app-logo.svg"),
                PlaceholderIconSvg);

            // Fonts: empty SusFontAsset the user fills with project typefaces.
            var fontsDirRel = custRel + "/Fonts";
            Directory.CreateDirectory(Path.Combine(projectRoot, fontsDirRel));
            AssetDatabase.Refresh();
            var fontsAssetRel = fontsDirRel + "/AppFonts.asset";
            if (AssetDatabase.LoadAssetAtPath<SusFontAsset>(fontsAssetRel) == null)
            {
                var fonts = ScriptableObject.CreateInstance<SusFontAsset>();
                fonts.name = "AppFonts";
                AssetDatabase.CreateAsset(fonts, fontsAssetRel);
            }

            return $"• Customization: {custRel}/ (README, branding.uss, AppFonts.asset, AppIcons)\n";
        }

        // ─── Templates ───────────────────────────────────────────────────
        // Starter MonoBehaviour + HomeScreen.sharq come from Editor/Setup/Starter~
        // via SusStarterAssets (inline BuildStarter/BuildHomeScreen removed).

        // Loaded by UseCustomStyles("branding") AFTER the whole token cascade
        // (core L1–L3 + downstream L4/L5), on the root AND the OverlayHost — so every
        // variable set here wins, including inside popups/tooltips/modals.
        // :root matches the element the stylesheet is attached to.
        private const string BrandingUssTemplate =
@"/* ────────────────────────────────────────────────────────────────
   branding.uss — YOUR project token overrides (the top cascade layer).

   Loaded by App.cs: .UseCustomStyles(""branding"")
   (the file must stay inside a Resources/ folder — the path is the
   resource name, without extension).

   Only the variables you declare here are overridden; everything else
   keeps cascading from the layers below. Uncomment, edit, press Play.
   Layer reference: Packages/com.sharq-it.sus.core/Docs/DESIGN_TOKENS.md
   ──────────────────────────────────────────────────────────────── */

:root {
    /* ── Colors (L2 aliases) — brand accent for buttons/links/accents ──
       Theme-dependent colors go through --thm-*; overriding them here
       recolors BOTH themes. For per-theme values use .theme-dark/.theme-light
       blocks below. */
    /* --thm-primary:         rgb(103, 80, 164); */
    /* --thm-primary-hover:   rgb(125, 103, 190); */
    /* --thm-primary-pressed: rgb(86, 61, 140);  */

    /* ── Sizes & shape — L3 core or registered L4 (same override scheme as colors) ── */
    /* --sus-font-size-body: 15px;   base text size */
    /* Registered L4 may expose radius/body tokens — override those after cascade load. */
}

/* Per-theme overrides — win only while the theme class is active: */
/* .theme-dark {
    --thm-primary: rgb(140, 120, 200);
} */
/* .theme-light {
    --thm-primary: rgb(80, 60, 140);
} */
";

        // Minimal valid SVG the Vector Graphics importer turns into a VectorImage.
        // Served by ResourcesFolderIconProvider("app") as icon ""app-logo"".
        private const string PlaceholderIconSvg =
@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 256 256"">
  <path d=""M128 24l28 62 68 8-50 46 13 67-59-33-59 33 13-67-50-46 68-8z"" fill=""#888888""/>
</svg>
";

        private static string BuildCustomizationReadme()
        {
            return
"# Customization - place to customize SUS in this project\n" +
"\n" +
"Each file here is a live, editable example of one customization axis.\n" +
"All of them are connected in the starter (`App.cs` next to this folder) - one at a time\n" +
"`Use*`-line per axis. Edit the file → Play → see the change.\n" +
"\n" +
"| I want to change | Edit file | How is it connected |\n" +
"|---|---|---|\n" +
"| Colors, sizes, radii | `Theme/Resources/branding.uss` | `.UseCustomStyles(\\\"branding\\\")` |\n" +
"| Fonts | `Fonts/AppFonts.asset` (Inspector) | `.UseFonts(_fonts)` |\n" +
"| Icons | `Icons/Resources/SusRuntime/Icons/app/{weight}/*.svg` | `.UseIcons(new ResourcesFolderIconProvider(\"app\"))` |\n" +
"| Your component | add `.sharq` under `Components/` | reference them in your screens |\n" +
"\n" +
"## How does this work\n" +
"\n" +
"- **branding.uss** loaded AFTER the entire cascade of tokens (core L1–L3 + downstream L4/L5)\n" +
"  to root and OverlayHost - overrides only declared variables,\n" +
"  including popups/tooltips. Layers: `Packages/com.sharq-it.sus.core/Docs/DESIGN_TOKENS.md`.\n" +
"- **AppFonts.asset** — assign fonts to Inspector; applied to the root and\n" +
"  overlays via `-unity-font-definition`.\n" +
"- **Icons** - put `.svg` in `Icons/Resources/SusRuntime/Icons/app/regular/`\n" +
"  (or `bold/`, `fill/`, ...). Usage: `new SusIconElement { Name = { Value = \"app-logo\" } }`\n" +
"  or `Icon=\"app-logo\"` in Sharq components. Your icons overlap the built-in ones.\n" +
"- **Your own components** - `.sharq` files anywhere under the UI root are compiled\n" +
"  automatically. Optional UI libraries are separate products at https://sus-ui.dev.\n";
        }
    }

    /// <summary>
    /// Completes <see cref="SusSetupWizard"/> across the domain reload that follows writing
    /// the starter .cs: once the starter type compiles, attaches it to the scene GameObject.
    /// </summary>
    [InitializeOnLoad]
    internal static class SusSetupFinisher
    {
        internal const string PendingSceneKey = "SusSetup.PendingScene";
        internal const string PendingGoKey = "SusSetup.PendingGo";
        internal const string PendingTypeKey = "SusSetup.PendingType";
        internal const string PendingFontsKey = "SusSetup.PendingFonts";

        static SusSetupFinisher()
        {
            EditorApplication.delayCall += TryFinish;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnReload() => EditorApplication.delayCall += TryFinish;

        private static void TryFinish()
        {
            var scenePath = SessionState.GetString(PendingSceneKey, "");
            var goName = SessionState.GetString(PendingGoKey, "");
            var typeName = SessionState.GetString(PendingTypeKey, "");
            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(typeName))
                return;

            var type = SusSetupWizard.FindType(typeName);
            if (type == null)
                return; // starter not compiled yet — wait for the next reload

            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                bool needsOpen = scene.path != scenePath;
                if (needsOpen)
                {
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }

                var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == goName);
                if (go != null && go.GetComponent(type) == null)
                {
                    var comp = go.AddComponent(type);
                    TryBindFontsAsset(comp, SessionState.GetString(PendingFontsKey, ""));
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[SusSetup] Attached '{typeName}' to '{goName}' in {scenePath}. Press Play.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SusSetup] Could not attach starter automatically: {ex.Message}. " +
                                 $"Add the '{typeName}' component to '{goName}' manually.");
            }
            finally
            {
                SessionState.EraseString(PendingSceneKey);
                SessionState.EraseString(PendingGoKey);
                SessionState.EraseString(PendingTypeKey);
                SessionState.EraseString(PendingFontsKey);
            }
        }

        /// <summary>
        /// Assigns the scaffolded AppFonts.asset into the starter's serialized
        /// <c>_fonts</c> field (customization scaffold, C2). No-op when the starter
        /// has no such field or the asset path is empty/missing.
        /// </summary>
        internal static void TryBindFontsAsset(Component starter, string fontsAssetPath)
        {
            if (starter == null || string.IsNullOrEmpty(fontsAssetPath)) return;

            var fonts = AssetDatabase.LoadAssetAtPath<SusFontAsset>(fontsAssetPath);
            if (fonts == null) return;

            var so = new SerializedObject(starter);
            var prop = so.FindProperty("_fonts");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) return;

            prop.objectReferenceValue = fonts;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

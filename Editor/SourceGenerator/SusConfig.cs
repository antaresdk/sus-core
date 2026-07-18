using System.IO;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Configuration for the Sharq compiler. Reads from Assets/sus.config.json.
    /// If the file doesn't exist, defaults are used.
    /// </summary>
    internal class SusConfig
    {
        // Single common root: drop .sharq anywhere under SharqDirectory; build/generation
        // artifacts and runtime assets live in dedicated sub-folders of the same root.
        public string SharqDirectory = "Assets/SusUI";
        public string GeneratedDirectory = "Assets/SusUI/Generated";
        // Compiler-synced component USS. Kept under Generated/ (auto, gitignored) so it never
        // mixes with hand-authored runtime assets. Must end with ".../Resources/SusRuntime"
        // so runtime Resources.Load("SusRuntime/..") resolves (Unity merges all such folders).
        // Hand-authored icons/fonts/theme overrides go in Assets/SusUI/Resources/SusRuntime (tracked).
        public string ResourcesDirectory = "Assets/SusUI/Generated/Resources/SusRuntime";
        public bool EnableValidation = true;
        public bool StrictVForKey = true;  // warn if v-for lacks :key
        public bool LogGeneratedFiles = true;
        /// <summary>
        /// E2b: snapshot Prop&lt;T&gt; across domain reload while Playing
        /// (requires Script Changes While Playing = Recompile And Continue Playing).
        /// </summary>
        public bool HotReloadStatePreserve = true;

        private static SusConfig _instance;
        internal static SusConfig Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        internal static void Reload() => _instance = Load();

        /// <summary>Writes current config to Assets/sus.config.json and reloads Instance.</summary>
        internal static void Save(SusConfig config)
        {
            if (config == null) return;
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "sus.config.json");
            try
            {
                var dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // JsonUtility skips private fields; SusConfig fields are public — OK.
                var json = JsonUtility.ToJson(config, prettyPrint: true);
                File.WriteAllText(configPath, json);
                _instance = config;
                Debug.Log($"[SusConfig] Saved → {configPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SusConfig] Save failed: {ex.Message}");
            }
        }

        /// <summary>Absolute path to Assets/sus.config.json.</summary>
        internal static string ConfigFilePath =>
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "sus.config.json");

        private static SusConfig Load()
        {
            var config = new SusConfig();
            var configPath = ConfigFilePath;
            if (!File.Exists(configPath)) return config;

            try
            {
                var json = File.ReadAllText(configPath);
                var parsed = JsonUtility.FromJson<SusConfig>(json);
                return parsed ?? config;
            }
            catch
            {
                Debug.LogWarning("[SusConfig] Failed to parse sus.config.json — using defaults");
                return config;
            }
        }
    }
}

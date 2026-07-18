using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Shim SusConfig implementation for CLI bootstrap.
    /// Replaces the Editor version (UnityEngine.JsonUtility → System.Text.Json).
    /// Identical API — SharqFileImporter uses the same fields.
    /// </summary>
    internal class SusConfig
    {
        // System.Text.Json serializes properties, not fields.
        // { get; init; } — deserialization from JSON.
        public string SharqDirectory { get; init; } = "Assets/SusUI";
        public string GeneratedDirectory { get; init; } = "Assets/SusUI/Generated";
        public string ResourcesDirectory { get; init; } = "Assets/SusUI/Generated/Resources/SusRuntime";

        [JsonIgnore]
        public bool EnableValidation { get; set; } = true;

        [JsonIgnore]
        public bool StrictVForKey { get; set; } = true;

        [JsonIgnore]
        public bool LogGeneratedFiles { get; set; } = true;

        private static SusConfig? _instance;

        internal static SusConfig Instance
        {
            get
            {
                _instance ??= Load();
                return _instance;
            }
        }

        internal static void Reload() => _instance = Load();

        private static SusConfig Load()
        {
            var config = new SusConfig();
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "sus.config.json");
            if (!File.Exists(configPath)) return config;

            try
            {
                var json = File.ReadAllText(configPath);
                var parsed = JsonSerializer.Deserialize<SusConfig>(json);
                return parsed ?? config;
            }
            catch
            {
                Console.Error.WriteLine("[SusConfig] Failed to parse sus.config.json — using defaults");
                return config;
            }
        }
    }
}

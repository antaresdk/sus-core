using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Per-section content hash for incremental regeneration.
    /// If only &lt;style&gt; changed, skip .g.cs and only regenerate .uss.
    /// </summary>
    internal static class SharqSectionCache
    {
        private static string DefaultCacheDir => SusConfig.Instance.GeneratedDirectory;

        internal static SectionHashes GetStoredHashes(string className, string cacheDir = null)
        {
            var result = new SectionHashes();
            var cachePath = GetCachePath(className, cacheDir);
            if (!File.Exists(cachePath)) return result;

            try
            {
                var json = File.ReadAllText(cachePath, Encoding.UTF8);
                result = UnityEngine.JsonUtility.FromJson<SectionHashes>(json) ?? new SectionHashes();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SharqSectionCache] Failed to read section cache for '{className}': {ex.Message}. Treating as dirty — full regeneration.");
            }

            return result;
        }

        internal static void StoreHashes(string className, SectionHashes hashes, string cacheDir = null)
        {
            var cachePath = GetCachePath(className, cacheDir);
            var dir = Path.GetDirectoryName(cachePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            try
            {
                var json = UnityEngine.JsonUtility.ToJson(hashes, prettyPrint: false);
                File.WriteAllText(cachePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SharqSectionCache] Failed to write section cache for '{className}': {ex.Message}.");
            }
        }

        /// <summary>
        /// Removes cached section hashes for a deleted/moved .sharq component.
        /// </summary>
        internal static void Clear(string className, string cacheDir = null)
        {
            var cachePath = GetCachePath(className, cacheDir);
            try
            {
                if (File.Exists(cachePath))
                    File.Delete(cachePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SharqSectionCache] Failed to delete stale cache for '{className}': {ex.Message}. Incremental detection may be incorrect.");
            }
        }

        /// <summary>
        /// Returns which sections changed since last compilation.
        /// <paramref name="cacheDir"/> — package generated dir; null → project SusConfig.GeneratedDirectory.
        /// </summary>
        internal static SectionChanged WhatChanged(string className,
            string templateXml, string scriptBody, string styleBody, string cacheDir = null)
        {
            var stored = GetStoredHashes(className, cacheDir);
            var result = new SectionChanged();

            var tHash = ComputeHash(templateXml ?? "");
            var sHash = ComputeHash(scriptBody ?? "");
            var cHash = ComputeHash(styleBody ?? "");

            result.TemplateChanged = tHash != stored.TemplateHash;
            result.ScriptChanged = sHash != stored.ScriptHash;
            result.StyleChanged = cHash != stored.StyleHash;

            result.NewHashes = new SectionHashes
            {
                TemplateHash = tHash,
                ScriptHash = sHash,
                StyleHash = cHash
            };

            return result;
        }

        private static string ComputeHash(string content)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string GetCachePath(string className, string cacheDir = null)
        {
            var fullDir = Path.GetFullPath(cacheDir ?? DefaultCacheDir);
            return Path.Combine(fullDir, $"{className}.sections.json");
        }
    }

    [System.Serializable]
    internal class SectionHashes
    {
        public string TemplateHash = "";
        public string ScriptHash = "";
        public string StyleHash = "";
    }

    internal class SectionChanged
    {
        public bool TemplateChanged = true;
        public bool ScriptChanged = true;
        public bool StyleChanged = true;
        public SectionHashes NewHashes;

        public bool OnlyStyle => StyleChanged && !TemplateChanged && !ScriptChanged;
        public bool Any => TemplateChanged || ScriptChanged || StyleChanged;
    }
}

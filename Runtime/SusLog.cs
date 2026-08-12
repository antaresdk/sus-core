using System;
using System.IO;
using UnityEngine;

namespace Sharq.Core
{
    /// <summary>
    /// Process-wide log verbosity. Higher values emit more messages.
    /// Default buyer level is <see cref="Warn"/> (Error + critical Warn).
    /// </summary>
    public enum SusLogLevel
    {
        Error = 0,
        Warn = 1,
        Info = 2,
        Verbose = 3,
    }

    /// <summary>
    /// Process-wide gated logger for SUS runtime. Proxies to <see cref="Debug"/> so
    /// <c>SusConsoleService</c> and Editor Console keep working.
    /// </summary>
    /// <remarks>
    /// Priority on first access: scripting define <c>SUS_VERBOSE_LOGS</c> (floor Verbose)
    /// → else <c>Assets/sus.config.json</c> <c>logLevel</c> → default <see cref="SusLogLevel.Warn"/>.
    /// <see cref="Level"/> / <see cref="SusApp.UseLogLevel"/> override config; the define
    /// floor still prevents lowering below Verbose when set.
    /// </remarks>
    public static class SusLog
    {
        private static bool _initialized;
        private static bool _defineFloor;
        private static SusLogLevel _level = SusLogLevel.Warn;

        /// <summary>
        /// Minimum level that is emitted. Default <see cref="SusLogLevel.Warn"/>.
        /// Setting below Verbose is ignored when <c>SUS_VERBOSE_LOGS</c> is defined.
        /// </summary>
        public static SusLogLevel Level
        {
            get
            {
                EnsureInitialized();
                return _level;
            }
            set
            {
                EnsureInitialized();
                if (_defineFloor && value < SusLogLevel.Verbose)
                    _level = SusLogLevel.Verbose;
                else
                    _level = value;
            }
        }

        /// <summary>True when messages at <paramref name="level"/> would be emitted.</summary>
        public static bool IsEnabled(SusLogLevel level)
        {
            EnsureInitialized();
            return level <= _level;
        }

        /// <summary>Alias for <c>IsEnabled(Verbose)</c> — prefer before expensive dumps.</summary>
        public static bool IsVerbose => IsEnabled(SusLogLevel.Verbose);

        /// <summary>Always emits (gate never silences Error).</summary>
        public static void Error(string message)
        {
            EnsureInitialized();
            Debug.LogError(message);
        }

        /// <summary>Always emits (gate never silences Error).</summary>
        public static void Error(string message, UnityEngine.Object context)
        {
            EnsureInitialized();
            Debug.LogError(message, context);
        }

        /// <summary>Emits when <see cref="Level"/> ≥ <see cref="SusLogLevel.Warn"/>.</summary>
        public static void Warn(string message)
        {
            if (!IsEnabled(SusLogLevel.Warn)) return;
            Debug.LogWarning(message);
        }

        /// <summary>Emits when <see cref="Level"/> ≥ <see cref="SusLogLevel.Warn"/>.</summary>
        public static void Warn(string message, UnityEngine.Object context)
        {
            if (!IsEnabled(SusLogLevel.Warn)) return;
            Debug.LogWarning(message, context);
        }

        /// <summary>Emits when <see cref="Level"/> ≥ <see cref="SusLogLevel.Info"/>.</summary>
        public static void Info(string message)
        {
            if (!IsEnabled(SusLogLevel.Info)) return;
            Debug.Log(message);
        }

        /// <summary>Emits when <see cref="Level"/> ≥ <see cref="SusLogLevel.Verbose"/>.</summary>
        public static void Verbose(string message)
        {
            if (!IsEnabled(SusLogLevel.Verbose)) return;
            Debug.Log(message);
        }

        /// <summary>Same gate as <see cref="Verbose"/> — sugar for audit / diagnostic call-sites.</summary>
        public static void Diagnostic(string message) => Verbose(message);

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

#if SUS_VERBOSE_LOGS
            _defineFloor = true;
            _level = SusLogLevel.Verbose;
#else
            TryApplyConfigFile();
#endif
        }

        /// <summary>
        /// Reads optional <c>logLevel</c> from <c>Assets/sus.config.json</c>.
        /// Does not load Editor <c>SusConfig</c> (player-safe thin reader).
        /// </summary>
        private static void TryApplyConfigFile()
        {
            try
            {
                var path = Path.Combine(Application.dataPath, "sus.config.json");
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                var dto = JsonUtility.FromJson<SusLogConfigDto>(json);
                if (dto == null || string.IsNullOrWhiteSpace(dto.logLevel)) return;

                if (TryParseLevel(dto.logLevel, out var parsed))
                    _level = parsed;
            }
            catch
            {
                // Keep default Warn — misconfig must not break bootstrap.
            }
        }

        internal static bool TryParseLevel(string raw, out SusLogLevel level)
        {
            level = SusLogLevel.Warn;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Enum.TryParse(raw.Trim(), ignoreCase: true, out level);
        }

        /// <summary>Test-only reset of process gate (define floor simulated via flag).</summary>
        internal static void ResetForTests(SusLogLevel level = SusLogLevel.Warn, bool defineFloor = false)
        {
            _initialized = true;
            _defineFloor = defineFloor;
            if (defineFloor && level < SusLogLevel.Verbose)
                _level = SusLogLevel.Verbose;
            else
                _level = level;
        }

        [Serializable]
        private sealed class SusLogConfigDto
        {
            // ReSharper disable once InconsistentNaming — matches sus.config.json key.
            public string logLevel;
        }
    }
}

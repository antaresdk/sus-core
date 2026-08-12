using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Icon weight — matches Phosphor Icons six weights. Providers whose icon set has no
    /// weights may ignore it.
    /// </summary>
    public enum SusIconWeight
    {
        Thin,
        Light,
        Regular,
        Bold,
        Fill,
        Duotone
    }

    /// <summary>
    /// Icon facade: resolves an alias/name (+ optional Phosphor-style weight suffix) to a
    /// <see cref="VectorImage"/> by querying registered <see cref="ISusIconProvider"/>s in
    /// priority order. Alias map and providers are configurable — the registry itself is
    /// icon-set-agnostic. The default provider is <see cref="CoreIconProvider"/> (minimal
    /// built-in set); <see cref="PhosphorIconBootstrap"/> self-registers a lower-priority
    /// provider for the full Phosphor set, which resolves once the optional
    /// <c>PhosphorIcons</c> sample is imported.
    ///
    /// Backward-compatible: existing callers keep using <see cref="Load(string, SusIconWeight)"/>,
    /// <see cref="KnownAliases"/> and <see cref="Categories"/> unchanged.
    /// </summary>
    public static class SusIconRegistry
    {
        // ── Providers (priority order — first match wins) ──
        private static readonly List<ISusIconProvider> s_providers = new() { new CoreIconProvider() };

        // ── Resolved-alias → image cache ──
        private static readonly Dictionary<string, VectorImage> s_cache = new();

        /// <summary>
        /// Registers an icon provider. <paramref name="asHighestPriority"/> = true (default)
        /// inserts it before the built-in providers so it is consulted first (project icons
        /// override Phosphor). Idempotent per instance. Invalidates caches.
        /// </summary>
        public static void RegisterProvider(ISusIconProvider provider, bool asHighestPriority = true)
        {
            if (provider == null || s_providers.Contains(provider)) return;
            if (asHighestPriority) s_providers.Insert(0, provider);
            else s_providers.Add(provider);
            InvalidateCache();
        }

        /// <summary>Removes a previously registered provider. Invalidates caches.</summary>
        public static void UnregisterProvider(ISusIconProvider provider)
        {
            if (provider != null && s_providers.Remove(provider))
                InvalidateCache();
        }

        // ── Known names — union of all providers, for browsing ──
        public static HashSet<string> KnownAliases => s_knownAliases ??= BuildKnownAliases();
        private static HashSet<string> s_knownAliases;

        private static HashSet<string> BuildKnownAliases()
        {
            var set = new HashSet<string>();
            foreach (var p in s_providers)
                foreach (var n in p.KnownNames)
                    set.Add(n);
            return set;
        }

        // ── Categories for icon browsing (lazy — rebuilt after providers change) ──
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Categories
            => s_categories ??= BuildCategories();
        private static Dictionary<string, IReadOnlyList<string>> s_categories;

        private static Dictionary<string, IReadOnlyList<string>> BuildCategories()
        {
            var cats = new Dictionary<string, IReadOnlyList<string>>
            {
            ["Arrows"] = FilterByPrefix("arrow-", "arrows-", "caret-"),
            ["Buildings"] = FilterByPrefix("building", "buildings", "house", "storefront", "warehouse", "factory", "door", "garage"),
            ["Business"] = FilterByPrefix("briefcase", "chart-", "graph", "presentation", "trend-", "strategy"),
            ["Communication"] = FilterByPrefix("chat-", "envelope-", "phone-", "megaphone", "microphone", "speaker-", "headphones", "headset", "voicemail"),
            ["Design"] = FilterByPrefix("bezier", "pen-nib", "paint-", "palette", "swatches", "eyedropper", "compass-tool", "ruler", "scribble", "pencil-", "pen"),
            ["Development"] = FilterByPrefix("code-", "terminal-", "git-", "database", "cpu", "brackets-", "command"),
            ["Devices"] = FilterByPrefix("device-", "laptop", "monitor", "display", "keyboard", "mouse", "printer", "projector", "tablet", "phone-", "watch", "webcam", "sim-card", "television"),
            ["Editor"] = FilterByPrefix("text-", "type", "selection", "paragraph", "textbox", "highlighter-circle"),
            ["Education"] = FilterByPrefix("exam", "graduation", "student", "backpack", "book-", "notebook", "certificate", "chalkboard"),
            ["Emojis"] = FilterByPrefix("smiley", "wink", "grin", "alien", "ghost"),
            ["Files"] = FilterByPrefix("file-", "folder-", "archive", "floppy", "hard-drive", "save", "paperclip"),
            ["Finance"] = FilterByPrefix("currency-", "bank", "piggy-bank", "wallet", "coin", "cash", "credit-card", "receipt", "invoice", "money", "vault"),
            ["Gaming"] = FilterByPrefix("game-controller", "sword", "bomb", "skull", "crown-", "ghost", "poker-chip", "dice-", "puzzle-piece", "target", "joystick", "shooting-star", "alien", "chess", "pawn"),
            ["Health"] = FilterByPrefix("heartbeat", "first-aid", "pill", "syringe", "stethoscope", "hospital", "ambulance", "tooth", "dna", "brain"),
            ["Logos"] = FilterSuffix("-logo", "logo"),
            ["Maps"] = FilterByPrefix("map-", "navigation", "compass", "globe", "location", "pin-", "route", "signpost"),
            ["Math"] = FilterByPrefix("calculator", "equals", "function", "plus-minus", "divide", "percent", "infinity", "pi", "sigma", "sqrt", "radical", "number-", "not-equals"),
            ["Media"] = FilterByPrefix("camera-", "cassette", "film-", "music-", "play-", "video-", "vinyl", "radio", "speaker-"),
            ["Nature"] = FilterByPrefix("flower", "leaf", "plant", "tree", "seedling", "fern", "palm", "cactus", "rose", "tulip", "mountain", "fire", "rainbow", "snowflake"),
            ["People"] = FilterByPrefix("user", "users", "person", "handshake", "people", "baby", "gender", "identification", "hand"),
            ["Shapes"] = FilterByPrefix("circle", "square", "triangle", "diamond", "hexagon", "octagon", "pentagon", "polygon", "rectangle", "star-", "heart-"),
            ["Shopping"] = FilterByPrefix("bag", "cart", "shop", "basket", "barcode", "qr-code", "tag", "ticket", "gift"),
            ["Sports"] = FilterByPrefix("basketball", "football", "soccer", "baseball", "tennis", "volleyball", "hockey", "cricket", "barbell", "racquet", "golf", "bowling", "boxing"),
            ["Technology"] = FilterByPrefix("cpu", "database", "server", "cloud-", "wifi", "bluetooth", "usb", "memory", "circuitry", "network"),
            ["Time"] = FilterByPrefix("clock-", "calendar-", "hourglass", "timer", "alarm", "stopwatch"),
            ["Transportation"] = FilterByPrefix("airplane", "bicycle", "bus", "car-", "motorcycle", "train", "truck", "ship", "rocket", "helicopter", "scooter", "skateboard", "wheelchair", "boat", "taxi", "tractor", "van"),
            ["UI"] = FilterByPrefix("check", "grid", "list-", "sidebar", "layout", "crosshair", "cursor", "selection-", "toggle", "magnifying-glass", "funnel", "sliders", "rows", "columns", "dots-"),
            ["Weather"] = FilterByPrefix("cloud-", "rain", "sun", "moon-", "wind-", "snow", "thunder", "hurricane", "tornado", "thermometer-", "umbrella", "drop"),
            };

            // Populate "General" with all icons not in any other category.
            var assigned = new HashSet<string>();
            foreach (var kv in cats)
                foreach (var n in kv.Value) assigned.Add(n);
            var general = new List<string>();
            foreach (var name in KnownAliases)
                if (!assigned.Contains(name))
                    general.Add(name);
            general.Sort();
            cats["General"] = general;
            return cats;
        }

        private static List<string> FilterByPrefix(params string[] prefixes)
        {
            var result = new List<string>();
            foreach (var name in KnownAliases)
                foreach (var p in prefixes)
                    if (name.StartsWith(p))
                    {
                        result.Add(name);
                        break;
                    }
            result.Sort();
            return result;
        }

        private static List<string> FilterSuffix(params string[] suffixes)
        {
            var result = new List<string>();
            foreach (var name in KnownAliases)
                foreach (var s in suffixes)
                    if (name.EndsWith(s))
                    {
                        result.Add(name);
                        break;
                    }
            result.Sort();
            return result;
        }

        // ── Aliases — convenience shortcuts (configurable via AddAlias) ──
        private static readonly Dictionary<string, string> s_aliases = new()
        {
            ["settings"] = "gear",
            ["home"] = "house",
            ["shop"] = "storefront",
            ["store"] = "storefront",
            ["user"] = "user",
            ["users"] = "users",
            ["close"] = "x",
            ["bug"] = "bug",
            ["logout"] = "sign-out",
            ["login"] = "sign-in",
            ["edit"] = "pencil",
            ["delete"] = "trash",
            ["help"] = "question",
            ["warning"] = "warning",
            ["error"] = "x-circle",
            ["success"] = "check-circle",
            ["info"] = "info",
            ["menu"] = "list",
            ["more"] = "dots-three",
            ["refresh"] = "arrows-clockwise",
            ["notifications"] = "bell",
            ["attach"] = "paperclip",
            ["add"] = "plus",
            ["remove"] = "minus",
            ["save"] = "floppy-disk",
            ["undo"] = "arrow-counter-clockwise",
            ["redo"] = "arrow-clockwise",
            ["copy"] = "copy",
            ["paste"] = "clipboard",
            ["cut"] = "scissors",
            ["drag"] = "dots-six-vertical",
            ["expand"] = "arrows-out",
            ["collapse"] = "arrows-in",
            ["external"] = "arrow-square-out",
            ["filter"] = "funnel",
            ["sort-asc"] = "sort-ascending",
            ["sort-desc"] = "sort-descending",
            ["columns"] = "columns",
            ["visible"] = "eye",
            ["hidden"] = "eye-slash",
            ["locked"] = "lock",
            ["unlocked"] = "lock-open",
            // Game
            ["attack"] = "sword",
            ["defense"] = "shield",
            ["health"] = "heartbeat",
            ["armor"] = "shield-check",
            ["speed"] = "lightning",
            ["magic"] = "magic-wand",
            ["gold"] = "coin",
            ["mana"] = "drop-half",
            ["stamina"] = "battery-full",
        };

        /// <summary>
        /// Adds/overrides an alias (e.g. "settings" → "gear"). Case-insensitive key.
        /// Lets a project map semantic names to whatever its icon provider supplies.
        /// </summary>
        public static void AddAlias(string alias, string target)
        {
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(target)) return;
            s_aliases[alias.ToLowerInvariant()] = target;
            s_cache.Clear();
        }

        /// <summary>Clear cached VectorImages and provider scans — call after replacing assets at runtime.</summary>
        public static void InvalidateCache()
        {
            s_cache.Clear();
            s_knownAliases = null;
            s_categories = null;
            foreach (var p in s_providers)
                p.Invalidate();
        }

        /// <summary>
        /// Resolves an icon alias to a <see cref="VectorImage"/> via the registered providers.
        /// Returns null if no provider supplies it.
        /// </summary>
        public static VectorImage Load(string alias, SusIconWeight weight = SusIconWeight.Regular)
        {
            if (string.IsNullOrEmpty(alias)) return null;

            var name = ResolveAlias(alias);

            // Auto-detect weight from Phosphor naming suffix: "star-fill" → weight=Fill, name="star".
            // No suffix → caller's weight is preserved.
            var detected = DetectWeightSuffix(name, out var stripped);
            if (stripped != null)
            {
                weight = detected;
                name = stripped;
            }

            var key = $"{(int)weight}/{name}";
            if (s_cache.TryGetValue(key, out var cached))
                return cached;

            VectorImage img = null;
            foreach (var provider in s_providers)
            {
                img = provider.Load(name, weight);
                if (img != null) break;
            }

            if (img != null)
                s_cache[key] = img;
            return img;
        }

        private static string ResolveAlias(string alias)
        {
            var lower = alias.ToLowerInvariant();
            return s_aliases.TryGetValue(lower, out var resolved) ? resolved : lower.Replace(" ", "-");
        }

        /// <summary>
        /// Detects Phosphor weight suffix in icon name.
        /// "star-fill" → (Fill, "star"); "heart-bold" → (Bold, "heart").
        /// Returns Regular and null stripped if no suffix found.
        /// </summary>
        private static SusIconWeight DetectWeightSuffix(string name, out string stripped)
        {
            stripped = null;
            if (name.EndsWith("-fill", System.StringComparison.OrdinalIgnoreCase))
            {
                stripped = name.Substring(0, name.Length - "-fill".Length);
                return SusIconWeight.Fill;
            }
            if (name.EndsWith("-bold", System.StringComparison.OrdinalIgnoreCase))
            {
                stripped = name.Substring(0, name.Length - "-bold".Length);
                return SusIconWeight.Bold;
            }
            if (name.EndsWith("-thin", System.StringComparison.OrdinalIgnoreCase))
            {
                stripped = name.Substring(0, name.Length - "-thin".Length);
                return SusIconWeight.Thin;
            }
            if (name.EndsWith("-light", System.StringComparison.OrdinalIgnoreCase))
            {
                stripped = name.Substring(0, name.Length - "-light".Length);
                return SusIconWeight.Light;
            }
            if (name.EndsWith("-duotone", System.StringComparison.OrdinalIgnoreCase))
            {
                stripped = name.Substring(0, name.Length - "-duotone".Length);
                return SusIconWeight.Duotone;
            }
            if (name.EndsWith("-regular", System.StringComparison.OrdinalIgnoreCase))
            {
                stripped = name.Substring(0, name.Length - "-regular".Length);
                return SusIconWeight.Regular;
            }
            return SusIconWeight.Regular; // no suffix → keep caller's weight
        }

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cache.Clear();
            s_knownAliases = null;
            s_categories = null;
            foreach (var p in s_providers)
                p.Invalidate();
        }
#endif
    }
}

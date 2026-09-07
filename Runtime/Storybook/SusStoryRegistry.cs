using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sharq.Core.Storybook
{
    /// <summary>
    /// The catalogue of stories, built by reflection over the LOADED assemblies that opted in with
    /// <see cref="SusStoryAssemblyAttribute"/> (plan §4.1). Zone A is drawn from this and from
    /// nothing else: a package with no story assembly has no tab, a package whose assembly is
    /// loaded but declared nothing gets a tab with the empty plate.
    ///
    /// Why an opt-in attribute and not a full sweep: <c>GetTypes()</c> over every assembly of a
    /// game project is a measurable start-up cost paid by everyone, while the set of assemblies
    /// that can hold stories is known to the packages themselves.
    ///
    /// IL2CPP / managed stripping (§9 risk 3): nothing here resolves a type from a STRING. Types
    /// arrive as <see cref="Type"/> objects taken from the attribute scan, and instances are made
    /// with <c>Activator.CreateInstance(Type)</c>, so a <c>link.xml</c> that preserves the story
    /// assembly is enough — no name-based lookup can be broken by a rename.
    /// </summary>
    public static class SusStoryRegistry
    {
        /// <summary>Package tab order the shell prefers; anything else follows alphabetically.</summary>
        static readonly string[] PackageOrder = { "core", "router", "kit", "game", "skin" };

        /// <summary>Group order inside a tab; anything else follows alphabetically.</summary>
        static readonly string[] GroupOrder =
        {
            "primitives", "atoms", "molecules", "organisms", "fields", "overlay", "data",
            "services", "slots", "hud", "screens", "world", "showcase", "devtools",
        };

        static readonly Dictionary<string, SusStoryPackageStamp> Declared =
            new(StringComparer.OrdinalIgnoreCase);

        static List<SusStoryEntry> _stories;
        static List<SusStoryPackage> _packages;
        static List<string> _ids;

        /// <summary>
        /// Resolves package id + version for an assembly. A host that can see the package manager
        /// (the Editor) sets this to the live <c>PackageInfo</c> lookup; in a player build it stays
        /// null and the fallback order is: assembly attribute, then the assembly informational
        /// version, then empty. A resolved fact always beats a literal, because a literal goes
        /// stale on the next version bump.
        /// </summary>
        public static Func<Assembly, SusStoryPackageStamp> PackageStampResolver { get; set; }

        /// <summary>Raised after the catalogue was rebuilt.</summary>
        public static event Action Changed;

        /// <summary>Every story found, ordered package, group, order, name.</summary>
        public static IReadOnlyList<SusStoryEntry> Stories
        {
            get { EnsureBuilt(); return _stories; }
        }

        /// <summary>Package tabs in display order, including packages that declared no stories.</summary>
        public static IReadOnlyList<SusStoryPackage> Packages
        {
            get { EnsureBuilt(); return _packages; }
        }

        /// <summary>
        /// Ids of every registered story, in display order. Name kept from the pre-engine shell
        /// (plan D11): 85 driver call sites in <c>sus-dev</c> read this list.
        /// </summary>
        public static IReadOnlyList<string> LastRegisteredStoryIds
        {
            get { EnsureBuilt(); return _ids; }
        }

        /// <summary>Story by exact id, or null.</summary>
        public static SusStoryEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureBuilt();
            for (int i = 0; i < _stories.Count; i++)
                if (string.Equals(_stories[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return _stories[i];
            return null;
        }

        /// <summary>Package tab by key, or null.</summary>
        public static SusStoryPackage FindPackage(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureBuilt();
            for (int i = 0; i < _packages.Count; i++)
                if (string.Equals(_packages[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return _packages[i];
            return null;
        }

        /// <summary>
        /// Announces a package tab that no loaded assembly declares — the escape hatch for a
        /// project that wants the tab (and its "no stories" plate) before the package ships any
        /// story. Takes effect on the next rebuild.
        /// </summary>
        public static void DeclarePackage(string key, string packageId = null, string version = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Declared[key] = new SusStoryPackageStamp(packageId, version);
            Invalidate();
        }

        /// <summary>Drops every declared package (test hygiene).</summary>
        public static void ClearDeclaredPackages()
        {
            Declared.Clear();
            Invalidate();
        }

        /// <summary>Throws the cache away; the next read rescans.</summary>
        public static void Invalidate()
        {
            _stories = null;
            _packages = null;
            _ids = null;
        }

        /// <summary>Rescans loaded assemblies now and raises <see cref="Changed"/>.</summary>
        public static void Refresh()
        {
            Invalidate();
            EnsureBuilt();
            Changed?.Invoke();
        }

        // Discovery ----------------------------------------------------------

        static void EnsureBuilt()
        {
            if (_stories != null) return;
            Build(AppDomain.CurrentDomain.GetAssemblies());
        }

        /// <summary>
        /// Builds the catalogue from an explicit assembly list. Public so a test can hand in one
        /// assembly instead of the whole domain — the discovery rule is then checked without
        /// depending on what else the editor happens to have loaded.
        /// </summary>
        public static void BuildFrom(IEnumerable<Assembly> assemblies)
        {
            Build(assemblies ?? Array.Empty<Assembly>());
            Changed?.Invoke();
        }

        static void Build(IEnumerable<Assembly> assemblies)
        {
            var stories = new List<SusStoryEntry>();
            var stamps = new Dictionary<string, SusStoryPackageStamp>(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in Declared)
            {
                known.Add(kv.Key);
                stamps[kv.Key] = kv.Value;
            }

            foreach (var asm in assemblies)
            {
                if (asm == null) continue;

                SusStoryAssemblyAttribute[] marks;
                try
                {
                    marks = (SusStoryAssemblyAttribute[])asm
                        .GetCustomAttributes(typeof(SusStoryAssemblyAttribute), false);
                }
                catch (Exception)
                {
                    continue;   // dynamic / unloadable assembly is not a reason to lose the rest
                }
                if (marks == null || marks.Length == 0) continue;

                var stamp = ResolveStamp(asm, marks);

                foreach (var m in marks)
                {
                    if (string.IsNullOrWhiteSpace(m.Package)) continue;
                    known.Add(m.Package);
                    if (!stamps.TryGetValue(m.Package, out var have) || have.IsEmpty)
                        stamps[m.Package] = stamp;
                }

                foreach (var type in SafeGetTypes(asm))
                    CollectFromType(type, asm, stories, stamps, known, stamp);
            }

            _stories = Order(stories);
            _ids = _stories.Select(s => s.Id).ToList();
            _packages = GroupIntoPackages(_stories, stamps, known);
        }

        static void CollectFromType(
            Type type,
            Assembly asm,
            List<SusStoryEntry> stories,
            Dictionary<string, SusStoryPackageStamp> stamps,
            HashSet<string> known,
            SusStoryPackageStamp stamp)
        {
            if (type == null || type.IsAbstract || type.IsGenericTypeDefinition) return;

            var attr = (SusStoryAttribute)Attribute.GetCustomAttribute(type, typeof(SusStoryAttribute));
            if (attr != null)
            {
                if (!typeof(ISusStory).IsAssignableFrom(type))
                {
                    SusLog.Warn("[storybook] '" + type.FullName + "' carries [SusStory] but does not implement ISusStory - skipped.");
                }
                else if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    SusLog.Warn("[storybook] '" + type.FullName + "' has no public parameterless constructor - skipped.");
                }
                else if (TryParseId(attr.Id, out var pkg, out var group, out var slug))
                {
                    var story = (ISusStory)Activator.CreateInstance(type);
                    stories.Add(new SusStoryEntry(
                        NormalizeId(pkg, group, slug), pkg, group, slug,
                        string.IsNullOrWhiteSpace(attr.Name) ? Humanize(slug) : attr.Name,
                        attr.Purpose, attr.Order, type, asm,
                        story.Create, story.Configure) { Weight = attr.Weight });   // card T-3038
                    Remember(pkg, stamp, stamps, known);
                }
                else
                {
                    SusLog.Warn("[storybook] '" + type.FullName + "': id '" + attr.Id +
                                "' is not <package>/<group>/<slug> - skipped.");
                }
            }

            if (!typeof(ISusStoryProvider).IsAssignableFrom(type)) return;
            if (type.GetConstructor(Type.EmptyTypes) == null) return;

            IEnumerable<SusStoryDefinition> defs;
            try
            {
                defs = ((ISusStoryProvider)Activator.CreateInstance(type)).Enumerate();
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] provider '" + type.FullName + "' threw while enumerating: " + e.Message);
                return;
            }
            if (defs == null) return;

            foreach (var def in defs)
            {
                if (def?.Create == null) continue;
                if (!TryParseId(def.Id, out var p, out var g, out var s))
                {
                    SusLog.Warn("[storybook] provider '" + type.FullName + "': id '" + def.Id +
                                "' is not <package>/<group>/<slug> - skipped.");
                    continue;
                }
                stories.Add(new SusStoryEntry(
                    NormalizeId(p, g, s), p, g, s,
                    string.IsNullOrWhiteSpace(def.Name) ? Humanize(s) : def.Name,
                    def.Purpose, def.Order, type, asm, def.Create, def.Configure)
                    { Weight = def.Weight });   // card T-3038
                Remember(p, stamp, stamps, known);
            }
        }

        static void Remember(
            string pkg, SusStoryPackageStamp stamp,
            Dictionary<string, SusStoryPackageStamp> stamps, HashSet<string> known)
        {
            known.Add(pkg);
            if (!stamps.TryGetValue(pkg, out var have) || have.IsEmpty)
                stamps[pkg] = stamp;
        }

        static SusStoryPackageStamp ResolveStamp(Assembly asm, SusStoryAssemblyAttribute[] marks)
        {
            var resolver = PackageStampResolver ?? EditorStamp;
            if (resolver != null)
            {
                try
                {
                    var live = resolver(asm);
                    if (!live.IsEmpty) return live;
                }
                catch (Exception e)
                {
                    SusLog.Warn("[storybook] package stamp resolver threw for '" +
                                asm.GetName().Name + "': " + e.Message);
                }
            }

            string id = null, version = null;
            foreach (var m in marks)
            {
                if (id == null && !string.IsNullOrWhiteSpace(m.PackageId)) id = m.PackageId;
                if (version == null && !string.IsNullOrWhiteSpace(m.Version)) version = m.Version;
            }

            if (version == null)
            {
                var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    asm, typeof(AssemblyInformationalVersionAttribute));
                if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                    version = info.InformationalVersion;
            }

            return new SusStoryPackageStamp(id, version);
        }

        /// <summary>
        /// Default resolver in the Editor: asks the package manager which package owns the
        /// assembly. Null outside the Editor, where the package manager does not exist — the
        /// fallback chain in <c>ResolveStamp</c> takes over there.
        /// </summary>
        static readonly Func<Assembly, SusStoryPackageStamp> EditorStamp =
#if UNITY_EDITOR
            asm =>
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(asm);
                return info == null
                    ? default
                    : new SusStoryPackageStamp(info.name, info.version);
            };
#else
            null;
#endif

        static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // One broken type must not cost the whole package its tab.
                return e.Types == null ? Enumerable.Empty<Type>() : e.Types.Where(t => t != null);
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] cannot read types of '" + asm.GetName().Name + "': " + e.Message);
                return Enumerable.Empty<Type>();
            }
        }

        // Shaping ------------------------------------------------------------

        static List<SusStoryEntry> Order(List<SusStoryEntry> stories)
        {
            return stories
                .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())                     // duplicate id: first wins, stably
                .OrderBy(s => IndexIn(PackageOrder, s.Package))
                .ThenBy(s => s.Package, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => IndexIn(GroupOrder, s.Group))
                .ThenBy(s => s.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Order)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static List<SusStoryPackage> GroupIntoPackages(
            List<SusStoryEntry> stories,
            Dictionary<string, SusStoryPackageStamp> stamps,
            HashSet<string> known)
        {
            var byPackage = stories
                .GroupBy(s => s.Package, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var key in byPackage.Keys) known.Add(key);

            var result = new List<SusStoryPackage>();
            foreach (var key in known
                         .OrderBy(k => IndexIn(PackageOrder, k))
                         .ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var groups = new List<SusStoryGroup>();
                if (byPackage.TryGetValue(key, out var list))
                {
                    foreach (var g in list
                                 .GroupBy(s => s.Group, StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(g => IndexIn(GroupOrder, g.Key))
                                 .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        groups.Add(new SusStoryGroup(g.Key, g.ToList()));
                    }
                }

                stamps.TryGetValue(key, out var stamp);
                result.Add(new SusStoryPackage(
                    key,
                    string.IsNullOrEmpty(stamp.PackageId) ? "com.sharq-it.sus." + key : stamp.PackageId,
                    stamp.Version ?? string.Empty,
                    groups));
            }
            return result;
        }

        static int IndexIn(string[] order, string value)
        {
            for (int i = 0; i < order.Length; i++)
                if (string.Equals(order[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            return order.Length;
        }

        // Ids ----------------------------------------------------------------

        /// <summary>
        /// Splits <c>kit/atoms/select</c>. Exactly three non-empty segments; anything else is a
        /// finding, not a silent skip (the caller logs it).
        /// </summary>
        public static bool TryParseId(string id, out string package, out string group, out string slug)
        {
            package = group = slug = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            var parts = id.Trim().Trim('/').Split('/');
            if (parts.Length != 3) return false;
            for (int i = 0; i < 3; i++)
                if (string.IsNullOrWhiteSpace(parts[i])) return false;
            package = parts[0].Trim();
            group = parts[1].Trim();
            slug = parts[2].Trim();
            return true;
        }

        static string NormalizeId(string pkg, string group, string slug) => pkg + "/" + group + "/" + slug;

        static string Humanize(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return string.Empty;
            var parts = slug.Split('-', '_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }
    }
}

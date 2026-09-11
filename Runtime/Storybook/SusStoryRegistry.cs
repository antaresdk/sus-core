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

        static List<SusStoryEntry> _all;
        static List<SusStoryPackage> _allPackages;
        static List<string> _allIds;

        static List<SusStoryEntry> _stories;
        static List<SusStoryPackage> _packages;
        static List<string> _ids;

        static Func<string, bool> _fixtureVisibility;
        static bool _fixturesByAddress;

        /// <summary>
        /// Resolves package id + version for an assembly. A host that can see the package manager
        /// (the Editor) sets this to the live <c>PackageInfo</c> lookup; in a player build it stays
        /// null and the fallback order is: assembly attribute, then the assembly informational
        /// version, then empty. A resolved fact always beats a literal, because a literal goes
        /// stale on the next version bump.
        /// </summary>
        public static Func<Assembly, SusStoryPackageStamp> PackageStampResolver { get; set; }

        /// <summary>
        /// Whether an engine FIXTURE package is listed - asked per package key, injected exactly
        /// like <see cref="PackageStampResolver"/> above (plan §4.1b, decision D22, card T-3410).
        /// Null (the default) means "no fixture is listed".
        ///
        /// Why an injection and not a bool read from EditorPrefs here: this assembly is Runtime, it
        /// ships in the player, and it cannot reference UnityEditor. The switch itself is an editor
        /// concern; the registry only needs the ANSWER, so the answer arrives as a function the
        /// editor entry point installs. The shell also accepts the answer from the address, so a
        /// link can open the bench on a machine where the toggle is off.
        ///
        /// "Listed" means tabs, tree rows, search hits and sweep records. It does NOT mean
        /// addressable: <see cref="Find"/> and <see cref="FindPackage"/> ignore this switch
        /// entirely, because the EditMode suite of the engine cites fixture addresses 86 times and
        /// a direct link must never depend on a preference.
        /// </summary>
        public static Func<string, bool> FixtureVisibility
        {
            get => _fixtureVisibility;
            set
            {
                _fixtureVisibility = value;
                InvalidateVisibility();
            }
        }

        /// <summary>
        /// Drops the cached PRODUCT selection without rescanning assemblies. Call it when the
        /// answer <see cref="FixtureVisibility"/> gives has changed but the function object has
        /// not (an editor toggle that reads a preference inside its own lambda).
        /// </summary>
        public static void InvalidateVisibility()
        {
            _stories = null;
            _packages = null;
            _ids = null;
        }

        /// <summary>
        /// The address asked for the benches (card T-3410): the shell sets this when the route
        /// carries <c>?fixtures=1</c>, so a link shared with a colleague opens the bench on a
        /// machine whose editor toggle is off.
        ///
        /// It only ever goes UP from the address - a later click that drops the key from the
        /// query does not silently switch the listing back, because "the tabs vanished halfway
        /// through" is a worse surprise than "they stayed". The editor toggle owns the way back.
        /// </summary>
        public static bool FixturesRequestedByAddress
        {
            get => _fixturesByAddress;
            set
            {
                if (_fixturesByAddress == value) return;
                _fixturesByAddress = value;
                InvalidateVisibility();
            }
        }

        /// <summary>True when a package of this key and kind is shown in the listings.</summary>
        public static bool IsListed(SusStoryPackageKind kind, string packageKey)
        {
            if (kind != SusStoryPackageKind.Fixture) return true;
            if (_fixturesByAddress) return true;
            var ask = _fixtureVisibility;
            if (ask == null) return false;
            try
            {
                return ask(packageKey);
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] fixture visibility resolver threw for '" +
                            (packageKey ?? "?") + "': " + e.Message);
                return false;
            }
        }

        /// <summary>Raised after the catalogue was rebuilt.</summary>
        public static event Action Changed;

        /// <summary>
        /// Every PRODUCT story, ordered package, group, order, name (plan §4.1b, decision D23,
        /// card T-3409). This is the default selection on purpose: zone A, the sweep, the frame
        /// conveyor and the catalogue all mean "what the buyer gets", and the one consumer that
        /// meant "everything loaded" wrote an engine fixture into the sweep report for a month.
        /// Everything, fixtures included, is <see cref="AllStories"/>.
        /// </summary>
        public static IReadOnlyList<SusStoryEntry> Stories
        {
            get { EnsureVisible(); return _stories; }
        }

        /// <summary>
        /// PRODUCT package tabs in display order, including packages that declared no stories.
        /// A fixture package is absent unless <see cref="FixtureVisibility"/> lists it - absent
        /// without an empty plate, too (D25): a bench that is not shown must not leave a hole
        /// shaped like itself.
        /// </summary>
        public static IReadOnlyList<SusStoryPackage> Packages
        {
            get { EnsureVisible(); return _packages; }
        }

        /// <summary>
        /// Ids of every registered PRODUCT story, in display order. Name kept from the pre-engine
        /// shell (plan D11): 85 driver call sites in <c>sus-dev</c> read this list.
        /// </summary>
        public static IReadOnlyList<string> LastRegisteredStoryIds
        {
            get { EnsureVisible(); return _ids; }
        }

        /// <summary>Every story the scan found, fixtures included (D23).</summary>
        public static IReadOnlyList<SusStoryEntry> AllStories
        {
            get { EnsureBuilt(); return _all; }
        }

        /// <summary>Every package the scan found, fixtures included (D23).</summary>
        public static IReadOnlyList<SusStoryPackage> AllPackages
        {
            get { EnsureBuilt(); return _allPackages; }
        }

        /// <summary>Ids of every story the scan found, fixtures included (D23).</summary>
        public static IReadOnlyList<string> AllIds
        {
            get { EnsureBuilt(); return _allIds; }
        }

        /// <summary>
        /// Story by exact id, or null. Searches EVERYTHING, fixtures included (D22): the direct
        /// address is the half of the contract that must never depend on a toggle.
        /// </summary>
        public static SusStoryEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureBuilt();
            for (int i = 0; i < _all.Count; i++)
                if (string.Equals(_all[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return _all[i];
            return null;
        }

        /// <summary>Package by key, or null. Searches everything, fixtures included (D22).</summary>
        public static SusStoryPackage FindPackage(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureBuilt();
            for (int i = 0; i < _allPackages.Count; i++)
                if (string.Equals(_allPackages[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return _allPackages[i];
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
            _all = null;
            _allPackages = null;
            _allIds = null;
            InvalidateVisibility();
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
            if (_all != null) return;
            Build(AppDomain.CurrentDomain.GetAssemblies());
        }

        /// <summary>Builds if needed, then makes sure the PRODUCT selection is up to date.</summary>
        static void EnsureVisible()
        {
            EnsureBuilt();
            if (_stories != null) return;

            var stories = new List<SusStoryEntry>(_all.Count);
            for (int i = 0; i < _all.Count; i++)
                if (IsListed(_all[i].Kind, _all[i].Package)) stories.Add(_all[i]);

            var packages = new List<SusStoryPackage>(_allPackages.Count);
            for (int i = 0; i < _allPackages.Count; i++)
                if (IsListed(_allPackages[i].Kind, _allPackages[i].Key)) packages.Add(_allPackages[i]);

            _stories = stories;
            _packages = packages;
            _ids = stories.Select(s => s.Id).ToList();
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
            var kinds = new Dictionary<string, SusStoryPackageKind>(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in Declared)
            {
                known.Add(kv.Key);
                stamps[kv.Key] = kv.Value;
                NoteKind(kv.Key, SusStoryPackageKind.Product, kinds);
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
                var kind = AssemblyKind(marks);

                foreach (var m in marks)
                {
                    if (string.IsNullOrWhiteSpace(m.Package)) continue;
                    known.Add(m.Package);
                    if (!stamps.TryGetValue(m.Package, out var have) || have.IsEmpty)
                        stamps[m.Package] = stamp;
                    NoteKind(m.Package, m.Kind, kinds);
                }

                foreach (var type in SafeGetTypes(asm))
                    CollectFromType(type, asm, stories, stamps, kinds, known, stamp, kind);
            }

            _all = Order(stories);
            _allIds = _all.Select(s => s.Id).ToList();
            _allPackages = GroupIntoPackages(_all, stamps, kinds, known);
            InvalidateVisibility();
        }

        /// <summary>
        /// The kind one assembly speaks with (card T-3409). An assembly that carries several marks
        /// and says <c>Product</c> in any of them is a product assembly: a bench must be able to
        /// declare itself, and must not be able to un-declare somebody else.
        /// </summary>
        static SusStoryPackageKind AssemblyKind(SusStoryAssemblyAttribute[] marks)
        {
            if (marks == null || marks.Length == 0) return SusStoryPackageKind.Product;
            foreach (var m in marks)
                if (m.Kind != SusStoryPackageKind.Fixture) return SusStoryPackageKind.Product;
            return SusStoryPackageKind.Fixture;
        }

        /// <summary>
        /// Records what a package KEY is, with product winning over fixture for the same reason:
        /// two assemblies can contribute to one key, and the shipping half decides the tab.
        /// </summary>
        static void NoteKind(
            string key, SusStoryPackageKind kind, Dictionary<string, SusStoryPackageKind> kinds)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (kinds.TryGetValue(key, out var have) && have == SusStoryPackageKind.Product) return;
            kinds[key] = kind;
        }

        static void CollectFromType(
            Type type,
            Assembly asm,
            List<SusStoryEntry> stories,
            Dictionary<string, SusStoryPackageStamp> stamps,
            Dictionary<string, SusStoryPackageKind> kinds,
            HashSet<string> known,
            SusStoryPackageStamp stamp,
            SusStoryPackageKind kind)
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
                    var id = NormalizeId(pkg, group, slug);
                    stories.Add(new SusStoryEntry(
                        id, pkg, group, slug,
                        string.IsNullOrWhiteSpace(attr.Name) ? Humanize(slug) : attr.Name,
                        attr.Purpose, attr.Order, type, asm,
                        story.Create, story.Configure)
                    {
                        Weight = attr.Weight,                                       // card T-3038
                        ComponentType = ValidComponent(attr.Component, id),         // card T-3137
                        NoComponentReason = Trim(attr.NoComponent),
                        AxisProp = AxisName(attr.Axis),                             // card T-3379
                        AxisValues = AxisSet(attr.Axis, attr.AxisValues, id),
                        Kind = kind,                                                // card T-3409
                    });
                    Remember(pkg, stamp, kind, stamps, kinds, known);
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
                var defId = NormalizeId(p, g, s);
                stories.Add(new SusStoryEntry(
                    defId, p, g, s,
                    string.IsNullOrWhiteSpace(def.Name) ? Humanize(s) : def.Name,
                    def.Purpose, def.Order, type, asm, def.Create, def.Configure)
                {
                    Weight = def.Weight,                                        // card T-3038
                    ComponentType = ValidComponent(def.Component, defId),       // card T-3137
                    NoComponentReason = Trim(def.NoComponent),
                    AxisProp = AxisName(def.Axis),                              // card T-3379
                    AxisValues = AxisSet(def.Axis, def.AxisValues, defId),
                    Kind = kind,                                                // card T-3409
                });
                Remember(p, stamp, kind, stamps, kinds, known);
            }
        }

        static string Trim(string s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();

        /// <summary>
        /// The prop a story hung its closed axis on (card T-3379), defaulted to
        /// <see cref="SusStoryAxis.DefaultPropName"/> so the entry never carries a null name.
        /// </summary>
        static string AxisName(string declared) =>
            string.IsNullOrWhiteSpace(declared) ? SusStoryAxis.DefaultPropName : declared.Trim();

        /// <summary>
        /// The axis values a story declared, cleaned by <see cref="SusStoryAxis.Normalize"/>
        /// (card T-3379).
        ///
        /// The one warning here is for the half-declaration: naming <c>Axis</c> and then giving
        /// no values is not a closed axis, it is a story that looks like it declared one. Silence
        /// there would read as "the engine ignored my axis"; the message says which half is
        /// missing. The opposite half-declaration (values without a name) is legal and common -
        /// it means the default prop.
        /// </summary>
        static IReadOnlyList<string> AxisSet(string declaredAxis, string[] declaredValues, string storyId)
        {
            var clean = SusStoryAxis.Normalize(declaredValues);
            if (clean.Count == 0 && !string.IsNullOrWhiteSpace(declaredAxis))
                SusLog.Warn("[storybook] story '" + storyId + "' names Axis = '" + declaredAxis.Trim() +
                            "' but lists no AxisValues - an axis without its values is not a closed " +
                            "axis, and the matrix will still show one row. Either list the values or " +
                            "drop the Axis.");
            return clean;
        }

        /// <summary>
        /// The declared <c>Component</c>, or null with a warning when it is not a component
        /// (card T-3137, plan §4.1a). The whole point of D19 is that the link is a TYPE and not a
        /// word, so a type that is not a <see cref="SusComponent"/> — a service, a controller, an
        /// abstract base — must not enter the registry as if it were the story's catalogue face:
        /// it would move the guess from the blurb into the attribute instead of removing it.
        /// </summary>
        static Type ValidComponent(Type declared, string storyId)
        {
            if (declared == null) return null;
            if (!typeof(SusComponent).IsAssignableFrom(declared))
            {
                SusLog.Warn("[storybook] story '" + storyId + "': Component = typeof(" +
                            declared.Name + ") is not a SusComponent - link dropped.");
                return null;
            }
            if (declared.IsAbstract)
            {
                SusLog.Warn("[storybook] story '" + storyId + "': Component = typeof(" +
                            declared.Name + ") is abstract, the catalogue has no such entry - link dropped.");
                return null;
            }
            return declared;
        }

        static void Remember(
            string pkg, SusStoryPackageStamp stamp, SusStoryPackageKind kind,
            Dictionary<string, SusStoryPackageStamp> stamps,
            Dictionary<string, SusStoryPackageKind> kinds, HashSet<string> known)
        {
            known.Add(pkg);
            if (!stamps.TryGetValue(pkg, out var have) || have.IsEmpty)
                stamps[pkg] = stamp;
            NoteKind(pkg, kind, kinds);
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
                .Select(g =>
                {
                    // The id is the FULL address (D17), so two stories collide only when they
                    // claim the very same package/group/slug — and then one of them is LOST.
                    // Losing it silently is what the tail-keyed map did to four stories for
                    // months (card T-3137), so the collapse says out loud which types collided.
                    if (g.Count() > 1)
                        SusLog.Warn("[storybook] address '" + g.Key + "' is claimed by " +
                                    string.Join(", ", g.Select(s => s.DeclaringType?.FullName ?? "?")) +
                                    " - the first wins, the rest are not shown.");
                    return g.First();
                })
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
            Dictionary<string, SusStoryPackageKind> kinds,
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
                if (!kinds.TryGetValue(key, out var kind)) kind = SusStoryPackageKind.Product;
                result.Add(new SusStoryPackage(
                    key,
                    string.IsNullOrEmpty(stamp.PackageId) ? "com.sharq-it.sus." + key : stamp.PackageId,
                    stamp.Version ?? string.Empty,
                    groups,
                    kind));
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

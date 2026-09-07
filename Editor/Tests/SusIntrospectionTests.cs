using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Sharq.Core.Editor.Tests
{
    /// <summary>
    /// Contract of the component-introspection API (T-3031, plan
    /// ARCH-20260907-STORYBOOK-ENGINE §4.2): what <c>DescribeProps</c>,
    /// <c>DescribeAllowed</c> and <c>DescribeEvents</c> promise to a consumer that does not know
    /// the component's type — the whole storybook engine is built on exactly these answers.
    /// EditMode: a component's Build() runs in its constructor, so no panel is needed.
    /// </summary>
    public class SusIntrospectionTests
    {
        static SusPropInfo Prop(IReadOnlyList<SusPropInfo> props, string name)
        {
            for (int i = 0; i < props.Count; i++)
                if (props[i].Name == name) return props[i];
            Assert.Fail("prop not described: " + name);
            return null;
        }

        static SusEventInfo Event(IReadOnlyList<SusEventInfo> events, string name)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Name == name) return events[i];
            Assert.Fail("event not described: " + name);
            return null;
        }

        // ── DescribeProps ───────────────────────────────────────────────

        [Test]
        public void DescribeProps_FindsEveryPropShape_ByFieldType()
        {
            var c = new SusIntrospectionFixture();
            var props = c.DescribeProps();

            // 16 declared Prop<T> fields; Func/Action/List fields are NOT props.
            Assert.AreEqual(16, props.Count, "declared Prop<T> fields");

            Assert.AreEqual(typeof(bool), Prop(props, "Disabled").ValueType);
            Assert.AreEqual(typeof(string), Prop(props, "Text").ValueType);
            Assert.AreEqual(typeof(int), Prop(props, "Progress").ValueType);
            Assert.AreEqual(typeof(float), Prop(props, "Value").ValueType);
            Assert.AreEqual(typeof(Color), Prop(props, "Accent").ValueType);
            Assert.AreEqual(typeof(IntrospectionMode), Prop(props, "Mode").ValueType);
            Assert.AreEqual(typeof(List<string>), Prop(props, "Items").ValueType);
            Assert.AreEqual(typeof(IntrospectionModel), Prop(props, "Model").ValueType);
        }

        [Test]
        public void DescribeProps_ReportsCurrentValue_WithoutTrackingOrCountingIt()
        {
            var c = new SusIntrospectionFixture();

            var before = Prop(c.DescribeProps(), "DeadProp").ReadCount;
            // Describing three more times must not look like consumption.
            c.DescribeProps();
            c.DescribeProps();
            c.DescribeProps();
            var after = Prop(c.DescribeProps(), "DeadProp").ReadCount;

            Assert.AreEqual(0, before);
            Assert.AreEqual(0, after, "describing a component must not count as reading it");
            Assert.AreEqual("hello", Prop(c.DescribeProps(), "Text").Value);
        }

        [Test]
        public void DescribeProps_MarksDeclaredButUnreadPropAsDead()
        {
            var c = new SusIntrospectionFixture();

            Assert.IsTrue(Prop(c.DescribeProps(), "DeadProp").Dead,
                "nobody reads DeadProp — that is the whole point of the flag");

            var _ = c.DeadProp.Value;   // one real consumer read

            var after = Prop(c.DescribeProps(), "DeadProp");
            Assert.IsFalse(after.Dead);
            Assert.AreEqual(1, after.ReadCount);
        }

        [Test]
        public void DescribeProps_PropWithWatcher_IsNeverDead()
        {
            var c = new SusIntrospectionFixture();
            // Size is clamped by UseAllowed, i.e. observed.
            var size = Prop(c.DescribeProps(), "Size");
            Assert.IsTrue(size.HasObservers);
            Assert.IsFalse(size.Dead);
        }

        [Test]
        public void DescribeProps_GroupsByNameAndType()
        {
            var props = new SusIntrospectionFixture().DescribeProps();

            Assert.AreEqual(SusPropGroup.State, Prop(props, "Disabled").Group);
            Assert.AreEqual(SusPropGroup.Behavior, Prop(props, "Clearable").Group);
            Assert.AreEqual(SusPropGroup.Content, Prop(props, "Text").Group);
            Assert.AreEqual(SusPropGroup.Content, Prop(props, "Items").Group);
            Assert.AreEqual(SusPropGroup.Axis, Prop(props, "Size").Group, "has an allowed set");
            Assert.AreEqual(SusPropGroup.Axis, Prop(props, "Variant").Group, "axis by name");
            Assert.AreEqual(SusPropGroup.Axis, Prop(props, "Mode").Group, "enum");
            Assert.AreEqual(SusPropGroup.Axis, Prop(props, "Accent").Group, "colour");
            Assert.AreEqual(SusPropGroup.Content, Prop(props, "Progress").Group);
            Assert.AreEqual(SusPropGroup.Behavior, Prop(props, "DebounceMs").Group);
            Assert.AreEqual(SusPropGroup.Data, Prop(props, "Model").Group);
        }

        [Test]
        public void DescribeProps_IconPropIsRecognisedByName()
        {
            var props = new SusIntrospectionFixture().DescribeProps();
            Assert.IsTrue(Prop(props, "Icon").LooksLikeIcon);
            Assert.IsFalse(Prop(props, "Text").LooksLikeIcon);
        }

        [Test]
        public void DescribeProps_CarriesRangeAttribute_BothForms()
        {
            var props = new SusIntrospectionFixture().DescribeProps();

            var value = Prop(props, "Value").Range;
            Assert.NotNull(value, "[SusRange(min,max,step)]");
            Assert.AreEqual(0d, value.Min);
            Assert.AreEqual(10d, value.Max);
            Assert.AreEqual(0.5d, value.Step);
            Assert.IsNull(value.Unit);

            var progress = Prop(props, "Progress").Range;
            Assert.NotNull(progress, "[SusRange(min,max,unit)]");
            Assert.AreEqual(100d, progress.Max);
            Assert.AreEqual("%", progress.Unit);

            Assert.IsNull(Prop(props, "DebounceMs").Range, "no attribute means no range");
        }

        [Test]
        public void DescribeProps_CarriesDependsOn_AndEvaluatesIt()
        {
            var c = new SusIntrospectionFixture();
            var icon = Prop(c.DescribeProps(), "Icon");

            Assert.AreEqual(1, icon.DependsOn.Count);
            Assert.AreEqual("ContentMode", icon.DependsOn[0].Prop);
            Assert.AreEqual("icon", icon.DependsOn[0].Value);
            Assert.AreEqual("ContentMode = icon", c.DescribeDependency(icon));

            Assert.IsFalse(c.IsDependencySatisfied(icon), "ContentMode is 'text'");
            c.ContentMode.Value = "icon";
            Assert.IsTrue(c.IsDependencySatisfied(icon));

            var text = Prop(c.DescribeProps(), "Text");
            Assert.AreEqual(0, text.DependsOn.Count);
            Assert.IsTrue(c.IsDependencySatisfied(text));
            Assert.IsNull(c.DescribeDependency(text));
        }

        [Test]
        public void DescribeProps_TrySetValue_ConvertsFromGenericControls()
        {
            var c = new SusIntrospectionFixture();
            var props = c.DescribeProps();

            Assert.IsTrue(Prop(props, "Progress").TrySetValue(42d), "slider gives a double");
            Assert.AreEqual(42, c.Progress.Value);

            Assert.IsTrue(Prop(props, "Mode").TrySetValue("Roomy"), "dropdown gives a string");
            Assert.AreEqual(IntrospectionMode.Roomy, c.Mode.Value);

            Assert.IsTrue(Prop(props, "Disabled").TrySetValue(true));
            Assert.IsTrue(c.Disabled.Value);

            Assert.IsFalse(Prop(props, "Progress").TrySetValue("not-a-number"),
                "a typo must be refused, not thrown");
            Assert.AreEqual(42, c.Progress.Value, "refused write leaves the value alone");
        }

        [Test]
        public void DescribeProps_SubscribeChanged_SeesStageDrivenChanges()
        {
            var c = new SusIntrospectionFixture();
            int hits = 0;
            var sub = Prop(c.DescribeProps(), "Text").SubscribeChanged(() => hits++);

            c.Text.Value = "one";
            c.Text.Value = "two";
            Assert.AreEqual(2, hits);

            sub.Dispose();
            c.Text.Value = "three";
            Assert.AreEqual(2, hits, "disposed subscription must detach");
        }

        // ── DescribeAllowed ─────────────────────────────────────────────

        [Test]
        public void DescribeAllowed_ExposesTheSetUseAllowedUsedToSwallow()
        {
            var c = new SusIntrospectionFixture();
            var allowed = c.DescribeAllowed();

            Assert.AreEqual(2, allowed.Count, "two UseAllowed calls");
            Assert.IsTrue(allowed.ContainsKey("Size"), "keyed by FIELD name, not by propName label");

            var size = allowed["Size"];
            Assert.AreEqual(typeof(string), size.ValueType);
            CollectionAssert.AreEqual(new[] { "sm", "md", "lg" }, size.Values);
            Assert.AreEqual("md", size.Fallback);
            Assert.AreEqual("sm", size.Aliases["small"]);
            Assert.IsFalse(size.IsDynamic);
        }

        [Test]
        public void DescribeAllowed_DynamicSetIsResolvedOnEveryCall()
        {
            var c = new SusIntrospectionFixture();

            CollectionAssert.AreEqual(new[] { "10", "25", "50" }, c.DescribeAllowed()["PageSize"].Values);
            Assert.IsTrue(c.DescribeAllowed()["PageSize"].IsDynamic);

            c.PageSizes = new List<int> { 5, 15 };
            CollectionAssert.AreEqual(new[] { "5", "15" }, c.DescribeAllowed()["PageSize"].Values,
                "a snapshot taken at registration time would lie here");
        }

        [Test]
        public void DescribeAllowed_IsAttachedToTheMatchingPropInfo()
        {
            var props = new SusIntrospectionFixture().DescribeProps();
            Assert.NotNull(Prop(props, "Size").Allowed);
            Assert.AreEqual("Size", Prop(props, "Size").Allowed.PropName);
            Assert.IsNull(Prop(props, "Variant").Allowed, "no UseAllowed call for Variant");
        }

        [Test]
        public void DescribeAllowed_StillClampsExactlyAsBefore()
        {
            // Recording the set must not change the behaviour it was recorded from.
            var c = new SusIntrospectionFixture();
            c.Size.Value = "large";                 // alias
            Assert.AreEqual("lg", c.Size.Value);
            c.Size.Value = "nonsense";
            Assert.AreEqual("md", c.Size.Value, "fallback");
            c.PageSize.Value = 33;
            Assert.AreEqual(10, c.PageSize.Value, "int fallback");
        }

        // ── DescribeEvents ──────────────────────────────────────────────

        [Test]
        public void DescribeEvents_FindsOnStarDelegateFields_AndIgnoresTheRest()
        {
            var events = new SusIntrospectionFixture().DescribeEvents();

            Assert.AreEqual(3, events.Count);
            Assert.IsNull(Event(events, "OnPing").ArgType, "parameterless Action");
            Assert.AreEqual(typeof(string), Event(events, "OnTextChanged").ArgType);
            Assert.AreEqual(typeof(int), Event(events, "OnCount").ArgType);

            foreach (var e in events)
            {
                Assert.AreNotEqual("NotAnEvent", e.Name, "Func<int> is not an event");
                Assert.AreNotEqual("Handler", e.Name, "a delegate not named On* is not an event");
            }
        }

        [Test]
        public void DescribeEvents_BusNameMirrorsTheEventBusBridge()
        {
            var events = new SusIntrospectionFixture().DescribeEvents();
            Assert.AreEqual("ping", Event(events, "OnPing").BusName);
            Assert.AreEqual("textChanged", Event(events, "OnTextChanged").BusName);
        }

        [Test]
        public void SubscribeEvent_AttachesWithoutKnowingThePayloadType()
        {
            var c = new SusIntrospectionFixture();
            object seen = null;
            int pings = 0;

            var subText = c.SubscribeEvent("OnTextChanged", v => seen = v);
            var subPing = c.SubscribeEvent("ping", _ => pings++);   // by bus name
            Assert.NotNull(subText);
            Assert.NotNull(subPing);

            c.OnTextChanged?.Invoke("fired");
            c.OnPing?.Invoke();
            Assert.AreEqual("fired", seen);
            Assert.AreEqual(1, pings);

            subText.Dispose();
            subPing.Dispose();
            seen = null;
            c.OnTextChanged?.Invoke("again");
            c.OnPing?.Invoke();
            Assert.IsNull(seen, "disposed handler must be detached");
            Assert.AreEqual(1, pings);
        }

        [Test]
        public void SubscribeEvent_UnknownNameReturnsNull_NotThrow()
        {
            var c = new SusIntrospectionFixture();
            Assert.IsNull(c.SubscribeEvent("nope", _ => { }));
        }

        [Test]
        public void DescribeEvents_ReportsExistingSubscribers()
        {
            var c = new SusIntrospectionFixture();
            Assert.IsFalse(Event(c.DescribeEvents(), "OnPing").HasSubscribers);
            c.OnPing += () => { };
            Assert.IsTrue(Event(c.DescribeEvents(), "OnPing").HasSubscribers);
        }

        // ── Fixture 2: the empty component ──────────────────────────────

        [Test]
        public void EmptyComponent_AnswersEmpty_NotNull()
        {
            var c = new SusIntrospectionEmptyFixture();
            Assert.IsNotNull(c.DescribeProps());
            Assert.AreEqual(0, c.DescribeProps().Count);
            Assert.IsNotNull(c.DescribeAllowed());
            Assert.AreEqual(0, c.DescribeAllowed().Count);
            Assert.IsNotNull(c.DescribeEvents());
            Assert.AreEqual(0, c.DescribeEvents().Count);
        }

        // ── Fixture 3: inheritance ──────────────────────────────────────

        [Test]
        public void DerivedComponent_InheritsBaseApi_AndAddsItsOwn()
        {
            var c = new SusIntrospectionDerivedFixture();
            var props = c.DescribeProps();
            var events = c.DescribeEvents();

            Assert.AreEqual(17, props.Count, "16 base props + Extra");
            Assert.AreEqual(4, events.Count, "3 base events + OnToggled");

            Assert.AreEqual(typeof(SusIntrospectionDerivedFixture), Prop(props, "Extra").DeclaringType);
            Assert.AreEqual(typeof(SusIntrospectionFixture), Prop(props, "Size").DeclaringType);

            var allowed = c.DescribeAllowed();
            Assert.AreEqual(3, allowed.Count, "base Size + base PageSize + Extra");
            CollectionAssert.AreEqual(new[] { "x", "y" }, allowed["Extra"].Values);
            Assert.AreEqual(SusPropGroup.Axis, Prop(props, "Extra").Group, "an allowed set is an axis");
        }

        // ── Grouping heuristic on its own ───────────────────────────────

        [Test]
        public void Classify_AllowedSetOutranksNameAndType()
        {
            Assert.AreEqual(SusPropGroup.Axis,
                SusPropGrouping.Classify("Whatever", typeof(string), hasAllowedSet: true));
            Assert.AreEqual(SusPropGroup.Content,
                SusPropGrouping.Classify("Whatever", typeof(string), hasAllowedSet: false));
        }

        [Test]
        public void Classify_HandlesUnknownNameAndNullType()
        {
            Assert.AreEqual(SusPropGroup.Other, SusPropGrouping.Classify(null, typeof(string), false));
            Assert.AreEqual(SusPropGroup.Other, SusPropGrouping.Classify("X", null, false));
            Assert.AreEqual(SusPropGroup.Data, SusPropGrouping.Classify("Payload", typeof(object), false));
        }
    }
}

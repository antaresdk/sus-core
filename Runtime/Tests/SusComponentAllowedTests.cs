using System.Collections.Generic;
using NUnit.Framework;
using Sharq.Core;

namespace Sharq.Core.Runtime.Tests
{
    public class SusComponentAllowedTests
    {
        static readonly string[] VuetifySizes =
            { "x-small", "small", "default", "large", "x-large" };

        static readonly IReadOnlyDictionary<string, string> VuetifyAliases =
            new Dictionary<string, string>
            {
                ["xs"] = "x-small",
                ["sm"] = "small",
                ["md"] = "default",
                ["lg"] = "large",
                ["xl"] = "x-large",
            };

        [Test]
        public void CoerceAllowed_String_MapsAliasAndFallback()
        {
            Assert.AreEqual("large", SusComponent.CoerceAllowed("lg", VuetifySizes, "default", VuetifyAliases));
            Assert.AreEqual("small", SusComponent.CoerceAllowed("sm", VuetifySizes, "default", VuetifyAliases));
            Assert.AreEqual("default", SusComponent.CoerceAllowed("nope", VuetifySizes, "default", VuetifyAliases));
            Assert.AreEqual("large", SusComponent.CoerceAllowed("LARGE", VuetifySizes, "default", VuetifyAliases));
        }

        [Test]
        public void CoerceAllowed_Int_FallsBackToFirstOption()
        {
            var opts = new[] { 10, 25, 50, 100, -1 };
            Assert.AreEqual(10, SusComponent.CoerceAllowed(5, opts, opts[0]));
            Assert.AreEqual(25, SusComponent.CoerceAllowed(25, opts, opts[0]));
            Assert.AreEqual(-1, SusComponent.CoerceAllowed(-1, opts, opts[0]));
        }
    }
}

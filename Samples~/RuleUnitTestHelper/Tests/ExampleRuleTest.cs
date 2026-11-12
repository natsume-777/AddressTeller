using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using AddressTeller;
using AddressTeller.Testing;

namespace AddressTellerSamples
{
    [TestFixture]
    public class ExampleRuleTest
    {
        // An inline rule that mirrors BasicRules/ExampleRules.cs.
        // Replace this with your own AddressRuleBase subclass.
        private sealed class LocalExampleRules : AddressRuleBase
        {
            public override int Order => 0;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("MyGroup")
                    .Where(Match.InFolder("Assets/Demo/Characters").And(Match.OfType<GameObject>()))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("character")
                    .Label("humanoid");

                rules.Group("MyGroup")
                    .Where(Match.InFolder("Assets/Demo/Items").And(Match.OfType<GameObject>()))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("item");
            }
        }

        // A rule that misuses the builder by calling Where() twice on the same group.
        // Configure() has no explicit throw of its own — the exception is thrown by the builder
        // object returned by Group(), and Configure() simply lets it propagate.
        private sealed class WhereCalledTwiceRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("MyGroup").Where(ctx => true);
                group.Where(ctx => false);
            }
        }

        // A rule that misuses the builder by calling Address() twice on the same group. Same shape as
        // WhereCalledTwiceRule above, but for the "one address per rule" constraint.
        private sealed class AddressCalledTwiceRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                var group = rules.Group("MyGroup").Address("first");
                group.Address("second");
            }
        }

        // A rule that targets the project's default Addressables group instead of a named group.
        // The default group's real name is only known inside a Unity project, so the outcome reports
        // UsesDefaultGroup instead of a group name.
        private sealed class GroupDefaultRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault()
                    .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Config/"))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        // A broad rule that loses to LocalExampleRules (higher Order value = lower priority) for the assets
        // both match. Used below to show how rules interact.
        private sealed class CatchAllDemoRules : AddressRuleBase
        {
            public override int Order => 100;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("Fallback")
                    .Where(ctx => ctx.Path.StartsWith("Assets/Demo/"))
                    .Address(ctx => "demo/" + ctx.FileNameWithoutExtension)
                    .Label("demo");

                // A label-only rule: its labels are written whichever address rule wins.
                rules.AnyGroup()
                    .Where(ctx => ctx.Extension == ".prefab")
                    .Label("prefab");
            }
        }

        // The expectation table: one row per asset, listing what the rule should decide for it.
        // Properties left unset are not checked. A row with only a path and a type expects that no rule matches.
        private static IEnumerable<TestCaseData> ExpectationTable()
        {
            yield return Row(new RuleExpectation("Assets/Demo/Characters/Hero.prefab", typeof(GameObject))
            {
                Group = "MyGroup",
                Address = "Hero",
                Labels = new[] { "character", "humanoid" },
            });
            yield return Row(new RuleExpectation("Assets/Demo/Items/Sword.prefab", typeof(GameObject))
            {
                Group = "MyGroup",
                Address = "Sword",
                Labels = new[] { "item" },
            });
            // A texture in the characters folder: the rule only accepts GameObjects.
            yield return Row(new RuleExpectation("Assets/Demo/Characters/HeroTex.png", typeof(Texture2D)));
        }

        // SetName makes each row show up in the Test Runner under its asset path.
        private static TestCaseData Row(RuleExpectation expectation)
            => new TestCaseData(expectation).SetName(expectation.Path);

        private LocalExampleRules _rule;

        [SetUp]
        public void SetUp() => _rule = new LocalExampleRules();

        [TestCaseSource(nameof(ExpectationTable))]
        public void Rule_DecidesWhatTheTableExpects(RuleExpectation expectation)
        {
            // Verify returns a message per failing row instead of asserting, so the package does not
            // depend on NUnit. An empty list means the row passed.
            var failures = RuleTestHelper.Verify(_rule, new[] { expectation });

            CollectionAssert.IsEmpty(failures);
        }

        [Test]
        public void Evaluate_ReportsTheDecisionInDetail()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Characters/Hero.prefab", typeof(GameObject));

            var outcome = RuleTestHelper.Evaluate(_rule, ctx);

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.AreEqual("MyGroup", outcome.GroupName);
            Assert.AreEqual("Hero", outcome.Address);
            CollectionAssert.AreEquivalent(new[] { "character", "humanoid" }, outcome.Labels);
        }

        [Test]
        public void Collect_Returns_TwoEntries()
        {
            var entries = RuleTestHelper.Collect(_rule);
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void For_WithExplicitGuid_UsesProvidedGuid()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Characters/Hero.prefab", typeof(GameObject), "explicit-guid");
            Assert.AreEqual("explicit-guid", ctx.Guid);
        }

        [Test]
        public void Collect_WhenWhereCalledTwiceOnSameGroup_PropagatesInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => RuleTestHelper.Collect(new WhereCalledTwiceRule()));
        }

        [Test]
        public void Collect_WhenAddressCalledTwiceOnSameGroup_PropagatesInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => RuleTestHelper.Collect(new AddressCalledTwiceRule()));
        }

        [Test]
        public void ConfigAsset_MatchesGroupDefaultRule_ReportsUsesDefaultGroup()
        {
            var ctx = RuleTestHelper.For("Assets/Demo/Config/Settings.asset", typeof(ScriptableObject));

            var outcome = RuleTestHelper.Evaluate(new GroupDefaultRule(), ctx);

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.IsTrue(outcome.UsesDefaultGroup);
            Assert.AreEqual("Settings", outcome.Address);
        }

        [Test]
        public void ConfigAsset_GroupDefaultRule_CanBeExpectedInATable()
        {
            var failures = RuleTestHelper.Verify(new GroupDefaultRule(), new[]
            {
                new RuleExpectation("Assets/Demo/Config/Settings.asset", typeof(ScriptableObject))
                {
                    UsesDefaultGroup = true,
                    Address = "Settings",
                },
            });

            CollectionAssert.IsEmpty(failures);
        }

        [Test]
        public void SeveralRules_LowerOrderWins_AndOnlyTheWinnersLabelsAreKept()
        {
            // Pass every rule class whose interaction you want to test; they are evaluated together,
            // the way AddressTeller evaluates all of your project's rules for one asset.
            var rules = new AddressRuleBase[] { _rule, new CatchAllDemoRules() };

            var failures = RuleInspector.Verify(rules, new[]
            {
                // LocalExampleRules (Order 0) beats the catch-all (Order 100). "demo" is the loser's label
                // and is not written; "prefab" comes from the label-only rule and always is.
                new RuleExpectation("Assets/Demo/Characters/Hero.prefab", typeof(GameObject))
                {
                    Group = "MyGroup",
                    Address = "Hero",
                    Labels = new[] { "character", "humanoid", "prefab" },
                },
                // Only the catch-all matches here.
                new RuleExpectation("Assets/Demo/Audio/Theme.wav", typeof(AudioClip))
                {
                    Group = "Fallback",
                    Address = "demo/Theme",
                    Labels = new[] { "demo" },
                },
            });

            CollectionAssert.IsEmpty(failures);
        }
    }
}

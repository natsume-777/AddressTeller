using NUnit.Framework;
using System;
using System.Linq;

namespace AddressTeller.Editor.Tests
{
    public class AddressTellerProjectSettingsRuleOverviewTests
    {
        // テスト用スタブ（このアセンブリ内にのみ存在。RuleCollector.CollectRules() の対象外）

        private sealed class TwoGroupRule : AddressRuleBase
        {
            public override int Order => 5;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("Textures")
                    .Where(ctx => ctx.Path.EndsWith(".png"), "PNG under UI/")
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("ui")
                    .Label("texture");

                rules.Group("Atlas")
                    .Label("atlas");
            }
        }

        private sealed class ThrowingRule : AddressRuleBase
        {
            public override int Order => 5;

            public override void Configure(IAddressRuleBuilder rules)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class NoEntryRule : AddressRuleBase
        {
            public override int Order => 1;

            public override void Configure(IAddressRuleBuilder rules) { }
        }

        private sealed class AnotherOrder1Rule : AddressRuleBase
        {
            public override int Order => 1;

            public override void Configure(IAddressRuleBuilder rules) { }
        }

        [Test]
        public void BuildRuleOverviewCache_ExtractsEntryMetadata()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule() };

            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            Assert.AreEqual(1, cache.Rules.Count);
            var overview = cache.Rules[0];
            Assert.AreEqual(typeof(TwoGroupRule), overview.RuleType);
            Assert.AreEqual(5, overview.Order);
            Assert.IsNull(overview.ConfigureError);
            Assert.AreEqual(2, overview.Entries.Count);

            var textures = overview.Entries[0];
            Assert.AreEqual("Textures", textures.GroupName);
            Assert.AreEqual("PNG under UI/", textures.Description);
            Assert.IsTrue(textures.HasAddress);
            Assert.AreEqual(2, textures.LabelCount);

            var atlas = overview.Entries[1];
            Assert.AreEqual("Atlas", atlas.GroupName);
            Assert.IsNull(atlas.Description);
            Assert.IsFalse(atlas.HasAddress);
            Assert.AreEqual(1, atlas.LabelCount);
        }

        [Test]
        public void BuildRuleOverviewCache_ConfigureThrows_RecordsErrorWithoutBlockingOthers()
        {
            var rules = new AddressRuleBase[] { new ThrowingRule(), new NoEntryRule() };

            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            Assert.AreEqual(2, cache.Rules.Count);

            var throwing = cache.Rules.Single(r => r.RuleType == typeof(ThrowingRule));
            Assert.IsNotNull(throwing.ConfigureError);
            StringAssert.Contains("boom", throwing.ConfigureError);
            Assert.AreEqual(0, throwing.Entries.Count);

            var noEntry = cache.Rules.Single(r => r.RuleType == typeof(NoEntryRule));
            Assert.IsNull(noEntry.ConfigureError);
            Assert.AreEqual(0, noEntry.Entries.Count);
        }

        [Test]
        public void BuildRuleOverviewCache_DuplicateOrders_AreGrouped()
        {
            var rules = new AddressRuleBase[] { new NoEntryRule(), new AnotherOrder1Rule(), new TwoGroupRule() };

            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            Assert.AreEqual(1, cache.DuplicateOrders.Count);
            var dup = cache.DuplicateOrders[0];
            Assert.AreEqual(1, dup.Order);
            Assert.AreEqual(2, dup.RuleClassNames.Count);
            CollectionAssert.AreEqual(
                new[] { nameof(AnotherOrder1Rule), nameof(NoEntryRule) },
                dup.RuleClassNames);
        }

        [Test]
        public void BuildRuleOverviewCache_NoDuplicateOrders_ReturnsEmpty()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule() };

            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            Assert.AreEqual(0, cache.DuplicateOrders.Count);
        }

        [Test]
        public void BuildRuleOverviewCache_OrdersResultByOrderThenClassName()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule(), new AnotherOrder1Rule(), new NoEntryRule() };

            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            CollectionAssert.AreEqual(
                new[] { typeof(AnotherOrder1Rule), typeof(NoEntryRule), typeof(TwoGroupRule) },
                cache.Rules.Select(r => r.RuleType).ToArray());
        }

        // --- CollectManagedGroups ---
        // AddressTellerSettings.DisabledRuleClassNames は ProjectSettings/AddressTellerSettings.asset に
        // 永続化されるため、テスト前後で状態を復元する。

        private System.Collections.Generic.List<string> _originalDisabled;

        [SetUp]
        public void SetUp()
        {
            _originalDisabled = AddressTellerSettings.DisabledRuleClassNames.ToList();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var name in AddressTellerSettings.DisabledRuleClassNames.ToList())
                if (!_originalDisabled.Contains(name))
                    AddressTellerSettings.SetRuleEnabled(name, true);

            foreach (var name in _originalDisabled)
                if (!AddressTellerSettings.DisabledRuleClassNames.Contains(name))
                    AddressTellerSettings.SetRuleEnabled(name, false);
        }

        [Test]
        public void CollectManagedGroups_CollectsGroupNamesFromEnabledRules()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache);

            CollectionAssert.AreEqual(new[] { "Atlas", "Textures" }, managedGroups);
        }

        [Test]
        public void CollectManagedGroups_DisabledRule_ExcludesItsGroups()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);
            AddressTellerSettings.SetRuleEnabled(typeof(TwoGroupRule).FullName, false);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache);

            Assert.IsEmpty(managedGroups);
        }

        [Test]
        public void CollectManagedGroups_ConfigureThrows_ContributesNoGroups()
        {
            var rules = new AddressRuleBase[] { new ThrowingRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache);

            Assert.IsEmpty(managedGroups);
        }

        [Test]
        public void CollectManagedGroups_DuplicateGroupNamesAcrossRules_Deduplicated()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule(), new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache);

            CollectionAssert.AreEqual(new[] { "Atlas", "Textures" }, managedGroups);
        }
    }
}

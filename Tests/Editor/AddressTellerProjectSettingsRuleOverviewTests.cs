using NUnit.Framework;
using System;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

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

        // AnyGroup() のみを使うルール（GroupName は null になる）。
        private sealed class AnyGroupOnlyRule : AddressRuleBase
        {
            public override int Order => 2;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup().Label("common");
            }
        }

        // GroupDefault() と通常の Group() を両方使うルール。
        private sealed class DefaultGroupAndNamedGroupRule : AddressRuleBase
        {
            public override int Order => 3;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault().Address(ctx => ctx.FileNameWithoutExtension);
                rules.Group("Named").Address(ctx => ctx.FileNameWithoutExtension);
            }
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
        public void BuildRuleOverviewCache_IsPure_ScriptIsAlwaysNull()
        {
            // BuildRuleOverviewCache は AssetDatabase 等の Unity API に依存しない純粋関数であるべき。
            // MonoScript の解決は呼び出し元の RefreshRuleOverviewCache が別途行うため、
            // ここでは常に Script == null になることを確認する。
            var rules = new AddressRuleBase[] { new TwoGroupRule() };

            var cache1 = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);
            var cache2 = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            Assert.IsNull(cache1.Rules[0].Script);
            Assert.IsNull(cache2.Rules[0].Script);
        }

        [Test]
        public void RefreshRuleOverviewCache_ResolvesScript_WithoutThrowing()
        {
            // RefreshRuleOverviewCache は RuleCollector.CollectRules()（このアセンブリ内のテスト専用ルールは
            // 対象外）を経由するため、ここでは呼び出し自体が例外を投げず、複数回呼んでも安定して完了することのみ検証する
            // （テストアセンブリ内のルールに対応する MonoScript は見つからないことがあるため、Script の
            // null/非 null 自体は問わない）。
            RuleOverviewCache cache1 = default;
            RuleOverviewCache cache2 = default;

            Assert.DoesNotThrow(() => cache1 = AddressTellerProjectSettings.RefreshRuleOverviewCache());
            Assert.DoesNotThrow(() => cache2 = AddressTellerProjectSettings.RefreshRuleOverviewCache());

            Assert.AreEqual(cache1.Rules.Count, cache2.Rules.Count);
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
        // AddressTellerSettings.DisabledRuleClassNames は ProjectSettings/AddressTellerSettings.json に
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
            // TwoGroupRule の "Atlas" は Address() を呼んでいない（Label() のみ）ため、
            // Address() を宣言している "Textures" だけが対象になる。
            var rules = new AddressRuleBase[] { new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            CollectionAssert.AreEqual(new[] { "Textures" }, managedGroups);
        }

        [Test]
        public void CollectManagedGroups_DisabledRule_ExcludesItsGroups()
        {
            var rules = new AddressRuleBase[] { new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);
            AddressTellerSettings.SetRuleEnabled(typeof(TwoGroupRule).FullName, false);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            Assert.IsEmpty(managedGroups);
        }

        [Test]
        public void CollectManagedGroups_ConfigureThrows_ContributesNoGroups()
        {
            var rules = new AddressRuleBase[] { new ThrowingRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            Assert.IsEmpty(managedGroups);
        }

        [Test]
        public void CollectManagedGroups_DuplicateGroupNamesAcrossRules_Deduplicated()
        {
            // TwoGroupRule の "Atlas" は Address() を呼んでいないため対象外（"Textures" のみが対象）。
            var rules = new AddressRuleBase[] { new TwoGroupRule(), new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            CollectionAssert.AreEqual(new[] { "Textures" }, managedGroups);
        }

        [Test]
        public void CollectManagedGroups_AnyGroupRule_DoesNotInsertNull()
        {
            // AnyGroup() のエントリは GroupName が null になる。RuleEvaluationPipeline.BuildSetup の
            // OwnedGroups がこれを除外するのと同じく、CollectManagedGroups の結果にも
            // null が含まれてはならない（"Managed Groups (N)" の水増し・空行表示を防ぐ）。
            // TwoGroupRule の "Atlas" は Address() を呼んでいないため対象外（"Textures" のみが対象）。
            var rules = new AddressRuleBase[] { new AnyGroupOnlyRule(), new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            Assert.IsFalse(managedGroups.Any(g => g == null));
            CollectionAssert.AreEqual(new[] { "Textures" }, managedGroups);
        }

        [Test]
        public void CollectManagedGroups_GroupWithoutAddress_IsExcluded()
        {
            // Group() だけ宣言して Address() を呼んでいないエントリは、削除の所有権判定
            // （RuleEvaluationPipeline.BuildSetup の OwnedGroups）と同じ条件で除外される。
            var rules = new AddressRuleBase[] { new TwoGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            CollectionAssert.DoesNotContain(managedGroups, "Atlas");
        }

        [Test]
        public void CollectManagedGroups_GroupDefaultSentinel_ResolvedToActualDefaultGroupName()
        {
            // GroupDefault() は Configure() の生出力ではセンチネル文字列のままだが、
            // RuleEvaluationPipeline.BuildSetup は settings.DefaultGroup が解決できる限りこれを実グループ名に
            // 正規化し、そのグループを OwnedGroups（削除・自動作成の対象）に含める。CollectManagedGroups は
            // 「何が削除されうるか」を利用者に示す唯一の画面であるため、この解決を BuildSetup と同じ条件で
            // 再現し、"(Default Group)" のような未解決の表示名ではなく実名を一覧に含めなければならない。
            var settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/AddressableAssetsData", nameof(CollectManagedGroups_GroupDefaultSentinel_ResolvedToActualDefaultGroupName));
            try
            {
                var defaultGroupName = settings.DefaultGroup.Name;
                var rules = new AddressRuleBase[] { new DefaultGroupAndNamedGroupRule() };
                var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

                var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, settings);

                CollectionAssert.AreEquivalent(new[] { "Named", defaultGroupName }, managedGroups);
            }
            finally
            {
                // settings.DefaultGroup へのアクセスで自動作成された既定グループも合わせて破棄する。
                // System と UnityEngine を両方 using しているため Object は裸で書かず明示的に修飾する。
                foreach (var group in settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(settings, true);
            }
        }

        [Test]
        public void CollectManagedGroups_GroupDefaultSentinel_SettingsNull_ExcludedAsUnresolved()
        {
            // settings が渡されない（Addressables 未初期化等で解決不能な）場合は、BuildSetup の
            // DefaultGroupUnavailable 相当としてセンチネル由来のエントリを除外する。誤って
            // "(Default Group)" のような未解決の表示名を一覧に含めてはならない。
            var rules = new AddressRuleBase[] { new DefaultGroupAndNamedGroupRule() };
            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache, null);

            CollectionAssert.AreEqual(new[] { "Named" }, managedGroups);
        }
    }
}

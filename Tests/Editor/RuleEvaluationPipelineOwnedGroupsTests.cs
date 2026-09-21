using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// RuleEvaluationPipeline.BuildSetup が OwnedGroups（削除の所有権判定に使うグループ集合）を
    /// 「いずれかのルールが Address() を宣言したグループ」だけに限定して構築することを検証する。
    /// ラベル加算はこの集合を参照しないため対象外（AddressTellerApplierApplyTests /
    /// AddressTellerApplierPredictTests の LabelsOnly 系テストで別途検証する）。
    /// </summary>
    public class RuleEvaluationPipelineOwnedGroupsTests
    {
        private const string ConfigFolder = "Assets/AddressableAssetsData";
        private AddressableAssetSettings _settings;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(ConfigFolder, nameof(RuleEvaluationPipelineOwnedGroupsTests));
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            if (_settings != null)
                Object.DestroyImmediate(_settings, true);
        }

        /// <summary>Address() まで宣言した通常のルール。OwnedGroups に "OwnedGroup" を含めるはず。</summary>
        private sealed class AddressGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("OwnedGroup").Where(ctx => false).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>Group() は宣言するが Address() をまだ呼んでいないルール（構築途中や Label 専用の用途を模す）。</summary>
        private sealed class GroupOnlyNoAddressRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("UnfinishedGroup").Where(ctx => false).Label("tag");
            }
        }

        /// <summary>AnyGroup() だけを使うラベル専用ルール。GroupName を一切持たない。</summary>
        private sealed class AnyGroupOnlyRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup().Where(ctx => true).Label("shared");
            }
        }

        [Test]
        public void BuildSetup_RuleDeclaresAddress_GroupIsInOwnedGroups()
        {
            var setup = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new AddressGroupRule() });

            Assert.IsTrue(setup.OwnedGroups.Contains("OwnedGroup"));
        }

        [Test]
        public void BuildSetup_GroupWithoutAddress_GroupIsNotInOwnedGroups()
        {
            // Group() だけ宣言して Address() を呼んでいないエントリは削除の権限を発生させない。
            var setup = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new GroupOnlyNoAddressRule() });

            Assert.IsFalse(setup.OwnedGroups.Contains("UnfinishedGroup"));
        }

        [Test]
        public void BuildSetup_AnyGroupOnly_OwnedGroupsIsEmpty()
        {
            // Group() ルールが1件も無い（AnyGroup() だけの）構成では OwnedGroups は空集合になる。
            var setup = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new AnyGroupOnlyRule() });

            Assert.AreEqual(0, setup.OwnedGroups.Count);
        }

        [Test]
        public void RemoveEntryForDeletedAsset_GroupWithoutAddress_EntryIsNotRemoved()
        {
            // OwnedGroups に含まれないグループのエントリは、削除追従（資産削除時のクリーンアップ）の対象にならない。
            var group = _settings.CreateGroup("UnfinishedGroup", false, false, false, null);
            try
            {
                _settings.CreateOrMoveEntry("guid-unfinished", group);

                var setup = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new GroupOnlyNoAddressRule() });
                var removed = AddressTellerApplier.RemoveEntryForDeletedAsset("guid-unfinished", _settings, setup.OwnedGroups);

                Assert.IsNull(removed);
                Assert.IsNotNull(_settings.FindAssetEntry("guid-unfinished"));
            }
            finally
            {
                Object.DestroyImmediate(group, true);
            }
        }

        [Test]
        public void RemoveEntryForDeletedAsset_GroupWithAddress_EntryIsRemoved()
        {
            // Address() を宣言しているグループのエントリは、従来どおり削除追従の対象になる。
            var group = _settings.CreateGroup("OwnedGroup", false, false, false, null);
            try
            {
                _settings.CreateOrMoveEntry("guid-owned", group);

                var setup = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new AddressGroupRule() });
                var removed = AddressTellerApplier.RemoveEntryForDeletedAsset("guid-owned", _settings, setup.OwnedGroups);

                Assert.IsNotNull(removed);
                Assert.IsNull(_settings.FindAssetEntry("guid-owned"));
            }
            finally
            {
                Object.DestroyImmediate(group, true);
            }
        }
    }
}

using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerApplier.Apply の Skipped 時クリーンアップ挙動(コードレビュー#5対応)を、
    /// ディスクに保存しない一時的な AddressableAssetSettings 上で検証する。
    /// </summary>
    public class AddressTellerApplierApplyTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _managedGroup;
        private AddressableAssetGroup _otherGroup;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerApplyTestSettings", false, false);
            _managedGroup = _settings.CreateGroup("ManagedGroup", false, false, false, null);
            _otherGroup = _settings.CreateGroup("OtherGroup", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;

            UnityEngine.Object.DestroyImmediate(_managedGroup, true);
            UnityEngine.Object.DestroyImmediate(_otherGroup, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private static AssetContext Ctx(string guid) =>
            new AssetContext(guid, "Assets/Foo.prefab", typeof(GameObject));

        private static AddressResolution EmptyResolution(params RuleEvaluationError[] errors) =>
            new AddressResolution(Array.Empty<AddressCandidate>(), new HashSet<string>(), errors);

        private HashSet<string> ExistingGroupNames() =>
            new HashSet<string> { _managedGroup.Name, _otherGroup.Name };

        [Test]
        public void Skipped_AssetInManagedGroup_RemovesEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-managed"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            Assert.IsNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void Skipped_AssetInUnmanagedGroup_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-other"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            Assert.IsNotNull(_settings.FindAssetEntry("guid-other"));
        }

        [Test]
        public void Skipped_CleanupDisabled_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-managed"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            Assert.IsNotNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void Skipped_WithRuleErrors_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };
            var resolution = EmptyResolution(new RuleEvaluationError("MyRule", "boom"));

            var result = AddressTellerApplier.Apply(Ctx("guid-managed"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            Assert.IsNotNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void RemoveEntryForDeletedAsset_AssetInManagedGroup_RemovesEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            AddressTellerApplier.RemoveEntryForDeletedAsset("guid-managed", _settings, managedGroups);

            Assert.IsNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void RemoveEntryForDeletedAsset_AssetInUnmanagedGroup_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            AddressTellerApplier.RemoveEntryForDeletedAsset("guid-other", _settings, managedGroups);

            Assert.IsNotNull(_settings.FindAssetEntry("guid-other"));
        }

        [Test]
        public void RemoveEntryForDeletedAsset_CleanupDisabled_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            AddressTellerApplier.RemoveEntryForDeletedAsset("guid-managed", _settings, managedGroups);

            Assert.IsNotNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void RemoveEntryForDeletedAsset_NoEntryForGuid_DoesNothing()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            Assert.DoesNotThrow(() =>
                AddressTellerApplier.RemoveEntryForDeletedAsset("guid-unknown", _settings, managedGroups));
        }
    }
}

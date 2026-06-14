using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerApplier.Predict (dry-run) の判定分岐を、
    /// ディスクに保存しない一時的な AddressableAssetSettings 上で検証する。
    /// Validate / Apply と1対1で対応する分岐を網羅する。
    /// </summary>
    public class AddressTellerApplierPredictTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _managedGroup;
        private AddressableAssetGroup _otherGroup;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerPredictTestSettings", false, false);
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

        private static AssetContext Ctx(string guid = "guid-1") =>
            new AssetContext(guid, "Assets/Foo.prefab", typeof(GameObject));

        private static AddressResolution EmptyResolution(params RuleEvaluationError[] errors) =>
            new AddressResolution(Array.Empty<AddressCandidate>(), new HashSet<string>(), errors);

        private static AddressResolution Resolution(IReadOnlyCollection<string> labels, params AddressCandidate[] candidates) =>
            new AddressResolution(candidates, labels);

        private HashSet<string> ExistingGroupNames() =>
            new HashSet<string> { _managedGroup.Name, _otherGroup.Name };

        [Test]
        public void Ok_NewEntry_PredictsAddOrUpdateWithSortedLabels()
        {
            var resolution = Resolution(
                new[] { "zeta", "alpha" },
                new AddressCandidate(_managedGroup.Name, "Characters/Player"));
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-new"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            Assert.AreEqual(ValidationStatus.Ok, prediction.Validation.Status);
            Assert.AreEqual("Characters/Player", prediction.PredictedEntry.Address);
            Assert.AreEqual(_managedGroup.Name, prediction.PredictedEntry.GroupName);
            CollectionAssert.AreEqual(new[] { "alpha", "zeta" }, prediction.PredictedEntry.Labels);
        }

        [Test]
        public void Ok_ExistingEntryWithLabels_PredictsUnionOfExistingAndResolutionLabels()
        {
            var entry = _settings.CreateOrMoveEntry("guid-existing", _managedGroup);
            entry.SetAddress("OldAddress");
            entry.SetLabel("existingLabel", true);

            var resolution = Resolution(
                new[] { "newLabel" },
                new AddressCandidate(_managedGroup.Name, "NewAddress"));
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-existing"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            Assert.AreEqual("NewAddress", prediction.PredictedEntry.Address);
            CollectionAssert.AreEqual(new[] { "existingLabel", "newLabel" }, prediction.PredictedEntry.Labels);
        }

        [Test]
        public void Skipped_AssetInManagedGroup_CleanupEnabled_PredictsRemove()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.Remove, prediction.Action);
            Assert.AreEqual(ValidationStatus.Skipped, prediction.Validation.Status);
            Assert.AreEqual(_managedGroup.Name, prediction.RemovedFromGroup);
        }

        [Test]
        public void Skipped_AssetInManagedGroup_CleanupDisabled_PredictsNoOp()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void Skipped_AssetInUnmanagedGroup_PredictsNoOp()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-other"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void Skipped_WithRuleErrors_DoesNotPredictRemove()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };
            var resolution = EmptyResolution(new RuleEvaluationError("MyRule", "boom"));

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void Conflict_TwoCandidates_PredictsNoOp()
        {
            var resolution = Resolution(
                new HashSet<string>(),
                new AddressCandidate(_managedGroup.Name, "addr1"),
                new AddressCandidate(_otherGroup.Name, "addr2"));
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-conflict"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.ConflictingAddress, prediction.Validation.Status);
        }

        [Test]
        public void GroupNotFound_PredictsNoOp()
        {
            var resolution = Resolution(
                new HashSet<string>(),
                new AddressCandidate("MissingGroup", "addr"));
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-missing-group"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.GroupNotFound, prediction.Validation.Status);
        }
    }
}

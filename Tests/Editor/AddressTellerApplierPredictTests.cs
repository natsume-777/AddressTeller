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

        private static AddressResolution LabelsOnlyResolution(params string[] labels) =>
            new AddressResolution(Array.Empty<AddressCandidate>(), new HashSet<string>(labels));

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
        public void LabelsOnly_NoExistingEntry_PredictsNoOp()
        {
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-new"), LabelsOnlyResolution("tag"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.LabelsOnly, prediction.Validation.Status);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void LabelsOnly_ExistingEntryInManagedGroup_CleanupEnabled_PredictsAddOrUpdateNotRemove()
        {
            // LabelsOnly はラベルのみルールがマッチしているため CleanupStaleEntries が ON でも Remove を予測してはならない。
            AddressTellerSettings.CleanupStaleEntries = true;
            var entry = _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            entry.SetAddress("ExistingAddress");
            entry.SetLabel("existingLabel", true);
            // guid が実アセットへ解決できないため readOnly=true で作られてしまう。ReadOnly ガードとは
            // 無関係な既存挙動を検証したいので明示的に外し、前提を固定する。
            entry.ReadOnly = false;
            Assert.IsFalse(entry.ReadOnly, "このテストの前提: 既存エントリは ReadOnly ではない。");
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            Assert.AreEqual(ValidationStatus.LabelsOnly, prediction.Validation.Status);
            Assert.AreEqual("ExistingAddress", prediction.PredictedEntry.Address);
            Assert.AreEqual(_managedGroup.Name, prediction.PredictedEntry.GroupName);
            CollectionAssert.AreEqual(new[] { "existingLabel", "newLabel" }, prediction.PredictedEntry.Labels);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void LabelsOnly_ExistingEntryInUnownedGroup_PredictsAddOrUpdate()
        {
            // Apply 側と対称に、ラベル加算の予測は所有権を問わない。既存エントリがあれば AddOrUpdate。
            var entry = _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            entry.SetAddress("ExistingAddress");
            entry.SetLabel("existingLabel", true);
            // guid が実アセットへ解決できないため readOnly=true で作られてしまう。上と同じ理由で外す。
            entry.ReadOnly = false;
            Assert.IsFalse(entry.ReadOnly, "このテストの前提: 既存エントリは ReadOnly ではない。");
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-other"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            Assert.AreEqual(ValidationStatus.LabelsOnly, prediction.Validation.Status);
            Assert.AreEqual("ExistingAddress", prediction.PredictedEntry.Address);
            Assert.AreEqual(_otherGroup.Name, prediction.PredictedEntry.GroupName);
            CollectionAssert.AreEqual(new[] { "existingLabel", "newLabel" }, prediction.PredictedEntry.Labels);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void LabelsOnly_ExistingEntryIsReadOnly_PredictsNoOp()
        {
            var entry = _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            entry.SetAddress("ExistingAddress");
            entry.SetLabel("existingLabel", true);
            entry.ReadOnly = true;
            Assert.IsTrue(entry.ReadOnly, "このテストの前提: 既存エントリは ReadOnly。");
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-other"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.LabelsOnly, prediction.Validation.Status);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void LabelsOnly_ExistingEntryInReadOnlyGroup_PredictsNoOp()
        {
            var readOnlyGroup = _settings.CreateGroup("ReadOnlyGroup", false, true, false, null);
            try
            {
                var entry = _settings.CreateOrMoveEntry("guid-readonly-group", readOnlyGroup);
                entry.SetAddress("ExistingAddress");
                entry.SetLabel("existingLabel", true);
                // guid が実アセットへ解決できないため、エントリ自体も readOnly=true で作られてしまう。
                // 「エントリは非 ReadOnly、グループだけ ReadOnly」を検証したいので、エントリ側だけ
                // 明示的に外し、両方の前提を固定する。
                entry.ReadOnly = false;
                Assert.IsFalse(entry.ReadOnly, "このテストの前提: 既存エントリ自体は ReadOnly ではない。");
                Assert.IsTrue(readOnlyGroup.ReadOnly, "このテストの前提: グループは ReadOnly。");
                var managedGroups = new HashSet<string> { _managedGroup.Name };

                var prediction = AddressTellerApplier.Predict(Ctx("guid-readonly-group"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), managedGroups);

                Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
                Assert.AreEqual(ValidationStatus.LabelsOnly, prediction.Validation.Status);
                Assert.IsNull(prediction.RemovedFromGroup);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readOnlyGroup, true);
            }
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
        public void Skipped_AssetInManagedGroup_CleanupDisabled_PredictsUnmatchedEntryKeptNotice()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };
            var ctx = Ctx("guid-managed");

            var prediction = AddressTellerApplier.Predict(ctx, EmptyResolution(), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.IsNull(prediction.RemovedFromGroup);
            // CleanupStaleEntries が ON なら削除される対象だったことを、IsOk=true の通知として報告する
            // （書き込みも削除も行わない）。
            Assert.AreEqual(ValidationStatus.UnmatchedEntryKept, prediction.Validation.Status);
            Assert.IsTrue(prediction.Validation.IsOk);
            Assert.IsFalse(prediction.Validation.IsBlocking);
            Assert.AreSame(ctx, prediction.Validation.Context);
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
            var resolution = EmptyResolution(new RuleEvaluationError("MyRule", "boom", order: 0, canProduceAddress: true));

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void Skipped_WithConfigureFailures_DoesNotPredictRemove()
        {
            // Apply の Skipped_WithConfigureFailures_KeepsEntry と対称。managedGroups が信頼できない実行では
            // Predict も Remove を予測してはならない。
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups, hasConfigureFailures: true);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.IsNull(prediction.RemovedFromGroup);
        }

        [Test]
        public void Skipped_WithRuleErrors_CleanupDisabled_DoesNotReportUnmatchedEntryKept()
        {
            // ルール例外があった実行では所有権が信頼できないため、CleanupStaleEntries が OFF でも
            // UnmatchedEntryKept 通知（＝ON なら削除される対象だったという判定）自体を行ってはならない。
            AddressTellerSettings.CleanupStaleEntries = false;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };
            var resolution = EmptyResolution(new RuleEvaluationError("MyRule", "boom", order: 0, canProduceAddress: true));

            var prediction = AddressTellerApplier.Predict(Ctx("guid-managed"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.Skipped, prediction.Validation.Status);
        }

        [Test]
        public void SpecificOrderWins_PredictsWinnerGroupAndAddress()
        {
            // 広い(Order=100)+特定(Order=0) の2候補。Order 最小の特定側が予測にも反映される。
            var resolution = Resolution(
                new HashSet<string>(),
                new AddressCandidate(_managedGroup.Name, "broadAddr", order: 100),
                new AddressCandidate(_otherGroup.Name, "specificAddr", order: 0));
            var managedGroups = new HashSet<string> { _managedGroup.Name, _otherGroup.Name };

            var prediction = AddressTellerApplier.Predict(Ctx("guid-priority"), resolution, _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            Assert.AreEqual(ValidationStatus.Ok, prediction.Validation.Status);
            Assert.AreEqual("specificAddr", prediction.PredictedEntry.Address);
            Assert.AreEqual(_otherGroup.Name, prediction.PredictedEntry.GroupName);
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

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AutoCreateMissingGroups（グループ自動作成オプション）の Validate/Apply/Predict 分岐と
    /// AddressTellerGroupFactory.EnsureGroup を、ディスクに保存しない一時的な
    /// AddressableAssetSettings 上で検証する。
    /// </summary>
    public class AddressTellerGroupAutoCreateTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _existingGroup;
        private bool _originalAutoCreateSetting;

        [SetUp]
        public void SetUp()
        {
            _originalAutoCreateSetting = AddressTellerSettings.AutoCreateMissingGroups;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerGroupAutoCreateTestSettings", false, false);
            _existingGroup = _settings.CreateGroup("ExistingGroup", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.AutoCreateMissingGroups = _originalAutoCreateSetting;

            // DefaultGroup の遅延作成（settings.DefaultGroup へのアクセス）や
            // EnsureGroup による新規作成で増えたグループも含めて破棄する。
            foreach (var group in _settings.groups.Where(g => g != null).ToList())
                UnityEngine.Object.DestroyImmediate(group, true);

            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private static AssetContext Ctx(string guid = "guid-1") =>
            new AssetContext(guid, "Assets/Foo.prefab", typeof(GameObject));

        private static AddressResolution Resolution(params AddressCandidate[] candidates) =>
            new AddressResolution(candidates, new HashSet<string>());

        private HashSet<string> ExistingGroupNames() =>
            new HashSet<string> { _existingGroup.Name };

        // --- Validate ---

        [Test]
        public void Validate_AutoCreateOff_MissingGroup_ReturnsGroupNotFound()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, ExistingGroupNames(), autoCreateMissingGroups: false);

            Assert.AreEqual(ValidationStatus.GroupNotFound, result.Status);
            Assert.IsFalse(result.IsOk);
        }

        [Test]
        public void Validate_AutoCreateOn_MissingGroup_ReturnsGroupWillBeCreated_IsOk()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, ExistingGroupNames(), autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, result.Status);
            Assert.IsTrue(result.IsOk);
            // Validate は副作用ゼロ。新規グループは作成されない。
            Assert.IsNull(_settings.FindGroup("Missing"));
        }

        [Test]
        public void Validate_AutoCreateOn_ExistingGroup_ReturnsOk()
        {
            var resolution = Resolution(new AddressCandidate("ExistingGroup", "addr"));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, ExistingGroupNames(), autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
        }

        // --- Predict ---

        [Test]
        public void Predict_AutoCreateOn_MissingGroup_PredictsAddOrUpdate_WithoutCreatingGroup()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));
            var existingGroupNames = new HashSet<string>(ExistingGroupNames());
            var managedGroups = new HashSet<string> { "Missing" };

            var prediction = AddressTellerApplier.Predict(Ctx(), resolution, _settings, existingGroupNames, managedGroups, autoCreateMissingGroups: true);

            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, prediction.Validation.Status);
            Assert.IsTrue(prediction.Validation.IsOk);
            Assert.AreEqual("Missing", prediction.PredictedEntry.GroupName);
            // Predict は副作用ゼロ。新規グループは作成されない。
            Assert.IsNull(_settings.FindGroup("Missing"));
        }

        [Test]
        public void Predict_AutoCreateOff_MissingGroup_PredictsNoOp()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));
            var existingGroupNames = new HashSet<string>(ExistingGroupNames());
            var managedGroups = new HashSet<string> { "Missing" };

            var prediction = AddressTellerApplier.Predict(Ctx(), resolution, _settings, existingGroupNames, managedGroups, autoCreateMissingGroups: false);

            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.GroupNotFound, prediction.Validation.Status);
        }

        // --- Apply ---

        [Test]
        public void Apply_AutoCreateOff_MissingGroup_ReturnsGroupNotFound_NoEntryWritten()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));

            var result = AddressTellerApplier.Apply(Ctx(), resolution, _settings, ExistingGroupNames(), autoCreateMissingGroups: false);

            Assert.AreEqual(ValidationStatus.GroupNotFound, result.Status);
            Assert.IsNull(_settings.FindAssetEntry("guid-1"));
            Assert.IsNull(_settings.FindGroup("Missing"));
        }

        [Test]
        public void Apply_AutoCreateOn_MissingGroup_CreatesGroupFromDefaultAndWritesEntry()
        {
            var resolution = Resolution(new AddressCandidate("NewGroup", "addr"));

            var result = AddressTellerApplier.Apply(Ctx(), resolution, _settings, ExistingGroupNames(), autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, result.Status);
            Assert.IsTrue(result.IsOk);

            var createdGroup = _settings.FindGroup("NewGroup");
            Assert.IsNotNull(createdGroup);

            // DefaultGroup のスキーマ構成が複製されていること。
            var defaultGroup = _settings.DefaultGroup;
            CollectionAssert.AreEquivalent(
                defaultGroup.Schemas.Select(s => s.GetType()),
                createdGroup.Schemas.Select(s => s.GetType()));

            var entry = _settings.FindAssetEntry("guid-1");
            Assert.IsNotNull(entry);
            Assert.AreEqual("addr", entry.address);
            Assert.AreEqual("NewGroup", entry.parentGroup.Name);
        }

        [Test]
        public void Apply_AutoCreateOn_ExistingGroup_BehavesAsNormalApply()
        {
            var resolution = Resolution(new AddressCandidate("ExistingGroup", "addr"));

            var result = AddressTellerApplier.Apply(Ctx(), resolution, _settings, ExistingGroupNames(), autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.Ok, result.Status);

            var entry = _settings.FindAssetEntry("guid-1");
            Assert.IsNotNull(entry);
            Assert.AreEqual("ExistingGroup", entry.parentGroup.Name);

            // 既存グループが再利用されていること（新規作成されていない）。
            Assert.AreSame(_existingGroup, entry.parentGroup);
        }

        // --- AddressTellerGroupFactory.EnsureGroup ---

        [Test]
        public void EnsureGroup_ExistingGroup_ReturnsExisting()
        {
            var ok = AddressTellerGroupFactory.EnsureGroup(_settings, "ExistingGroup", out var group, out var failureReason);

            Assert.IsTrue(ok);
            Assert.AreSame(_existingGroup, group);
            Assert.IsNull(failureReason);
        }

        [Test]
        public void EnsureGroup_MissingGroup_CreatesFromDefaultGroup()
        {
            var ok = AddressTellerGroupFactory.EnsureGroup(_settings, "BrandNewGroup", out var group, out var failureReason);

            Assert.IsTrue(ok);
            Assert.IsNotNull(group);
            Assert.AreEqual("BrandNewGroup", group.Name);
            Assert.IsNull(failureReason);

            var defaultGroup = _settings.DefaultGroup;
            CollectionAssert.AreEquivalent(
                defaultGroup.Schemas.Select(s => s.GetType()),
                group.Schemas.Select(s => s.GetType()));
        }

        // --- ValidateAll issues に対する中止判定（H1）---
        //
        // AddressTellerApplyFlow.Run / AddressTellerMenu.ApplyWithValidateCLI は
        // ValidateAll() の戻り値を `validateIssues.Any(i => !i.IsOk)` で中止判定する。
        // ValidateAll() の issues には GroupWillBeCreated（IsOk=true、AutoCreateMissingGroups
        // による作成予定の提示）が情報提供として混在するため、IsOk=true の要素だけでは
        // 中止してはならない。ここでは ValidationResult のリストに対する判定そのものを検証する。

        [Test]
        public void ValidateIssues_OnlyGroupWillBeCreated_DoesNotTriggerAbort()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));
            var result = AddressTellerApplier.Validate(Ctx(), resolution, ExistingGroupNames(), autoCreateMissingGroups: true);

            var issues = new List<ValidationResult> { result };

            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, result.Status);
            Assert.IsFalse(issues.Any(i => !i.IsOk),
                "GroupWillBeCreated（IsOk=true）のみの場合、ApplyFlow/Menu の中止判定は false（中止しない）になるべき。");
        }

        [Test]
        public void ValidateIssues_ContainsRealError_TriggersAbort()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));
            var result = AddressTellerApplier.Validate(Ctx(), resolution, ExistingGroupNames(), autoCreateMissingGroups: false);

            var issues = new List<ValidationResult> { result };

            Assert.AreEqual(ValidationStatus.GroupNotFound, result.Status);
            Assert.IsTrue(issues.Any(i => !i.IsOk),
                "GroupNotFound（IsOk=false）が含まれる場合、ApplyFlow/Menu の中止判定は true（中止する）になるべき。");
        }

        [Test]
        public void ApplyAutoCreate_RealErrorMixedWithGroupWillBeCreated_StillTriggersAbort()
        {
            // 1件は AutoCreate により GroupWillBeCreated（IsOk=true）、もう1件は別アセットで
            // GroupNotFound（IsOk=false、AutoCreate無効相当）となるケースを混在させる。
            var willBeCreated = AddressTellerApplier.Validate(
                Ctx("guid-1"), Resolution(new AddressCandidate("Missing", "addr")), ExistingGroupNames(), autoCreateMissingGroups: true);
            var notFound = AddressTellerApplier.Validate(
                Ctx("guid-2"), Resolution(new AddressCandidate("StillMissing", "addr")), ExistingGroupNames(), autoCreateMissingGroups: false);

            var issues = new List<ValidationResult> { willBeCreated, notFound };

            Assert.IsTrue(issues.Any(i => !i.IsOk), "IsOk=false の要素が1件でも含まれれば中止判定は true になるべき。");
        }
    }
}

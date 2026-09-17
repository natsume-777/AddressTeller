using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AutoCreateMissingGroups（グループ自動作成オプション）の Validate/Apply/Predict 分岐と
    /// AddressTellerGroupFactory.EnsureGroup を、ディスクに保存しない一時的な
    /// AddressableAssetSettings 上で検証する。
    /// </summary>
    public class AddressTellerGroupAutoCreateTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/GroupAutoCreate";

        // AddressTellerApplier.Apply が実際にエントリを新規作成する経路（settings.CreateOrMoveEntry）に
        // 到達するテストでは、GUID がプロジェクト内のどのアセットにも対応しないと、Addressables 本体が
        // パス無効と判定して readOnly のプレースホルダエントリを作ってしまい、AddressTellerApplier の
        // 拒否判定（EntryRejectedByAddressables）に引っかかる。Validate() のみで完結する（Apply の
        // エントリ作成に到達しない）テストでは実アセットは不要なため、Ctx() の既定の偽 GUID のままでよい。
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _existingGroup;
        private bool _originalAutoCreateSetting;

        [SetUp]
        public void SetUp()
        {
            _originalAutoCreateSetting = AddressTellerSettings.AutoCreateMissingGroups;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerGroupAutoCreateTestSettings", false, false);
            _existingGroup = _settings.CreateGroup("ExistingGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(StubFolder))
                AssetDatabase.CreateFolder(TestRootFolder, "GroupAutoCreate");
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

            // SetUp で作成した一時アセット・フォルダはまとめて消す（他のテストクラスと同じ流儀）。
            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);
        }

        private static AssetContext Ctx(string guid = "guid-1") =>
            new AssetContext(guid, "Assets/Foo.prefab", typeof(GameObject));

        /// <summary>指定パスに最小限の Prefab アセットを作成し、その GUID から AssetContext を組み立てる。</summary>
        private static AssetContext RealCtx(string assetPath)
        {
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetPath));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, assetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            return new AssetContext(AssetDatabase.AssetPathToGUID(assetPath), assetPath, typeof(GameObject));
        }

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
            // settings.CreateOrMoveEntry に実際に到達する（エントリを新規作成する）ため、
            // guid がプロジェクト内の実アセットに対応している必要がある(readOnly プレースホルダ回避)。
            var ctx = RealCtx(StubFolder + "/Stub1.prefab");
            var resolution = Resolution(new AddressCandidate("NewGroup", "addr"));

            var result = AddressTellerApplier.Apply(ctx, resolution, _settings, ExistingGroupNames(), autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, result.Status);
            Assert.IsTrue(result.IsOk);

            var createdGroup = _settings.FindGroup("NewGroup");
            Assert.IsNotNull(createdGroup);

            // DefaultGroup のスキーマ構成が複製されていること。
            var defaultGroup = _settings.DefaultGroup;
            CollectionAssert.AreEquivalent(
                defaultGroup.Schemas.Select(s => s.GetType()),
                createdGroup.Schemas.Select(s => s.GetType()));

            var entry = _settings.FindAssetEntry(ctx.Guid);
            Assert.IsNotNull(entry);
            Assert.AreEqual("addr", entry.address);
            Assert.AreEqual("NewGroup", entry.parentGroup.Name);
        }

        [Test]
        public void Apply_AutoCreateOn_ExistingGroup_BehavesAsNormalApply()
        {
            // settings.CreateOrMoveEntry に実際に到達する（エントリを新規作成する）ため、
            // guid がプロジェクト内の実アセットに対応している必要がある(readOnly プレースホルダ回避)。
            var ctx = RealCtx(StubFolder + "/Stub2.prefab");
            var resolution = Resolution(new AddressCandidate("ExistingGroup", "addr"));

            var result = AddressTellerApplier.Apply(ctx, resolution, _settings, ExistingGroupNames(), autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.Ok, result.Status);

            var entry = _settings.FindAssetEntry(ctx.Guid);
            Assert.IsNotNull(entry);
            Assert.AreEqual("ExistingGroup", entry.parentGroup.Name);

            // 既存グループが再利用されていること（新規作成されていない）。
            Assert.AreSame(_existingGroup, entry.parentGroup);
        }

        [Test]
        public void Apply_AutoCreateOn_SameMissingGroupTwice_SecondAssetReusesCreatedGroup_WithoutDuplicateWarning()
        {
            // 同じ existingGroupNames インスタンスを2件のアセットの Apply 呼び出しに使い回すことで、
            // 1件目でグループが作成された後、2件目では GroupWillBeCreated ではなく Ok になり、
            // EnsureGroup が再度呼ばれないこと（= 新規グループが1個だけ作成されること）を検証する。
            // settings.CreateOrMoveEntry に実際に到達するため、両方とも実アセットの guid を使う。
            var ctx1 = RealCtx(StubFolder + "/Stub3.prefab");
            var ctx2 = RealCtx(StubFolder + "/Stub4.prefab");
            var existingGroupNames = ExistingGroupNames();
            var resolution1 = Resolution(new AddressCandidate("NewGroup", "addr1"));
            var resolution2 = Resolution(new AddressCandidate("NewGroup", "addr2"));

            var result1 = AddressTellerApplier.Apply(ctx1, resolution1, _settings, existingGroupNames, autoCreateMissingGroups: true);
            var result2 = AddressTellerApplier.Apply(ctx2, resolution2, _settings, existingGroupNames, autoCreateMissingGroups: true);

            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, result1.Status);
            Assert.AreEqual(ValidationStatus.Ok, result2.Status,
                "existingGroupNames に作成済みグループが反映されていれば、2件目は GroupWillBeCreated ではなく Ok になるはず。");

            // NewGroup という名前のグループはちょうど1個だけ作成されていること。
            var newGroups = _settings.groups.Where(g => g != null && g.Name == "NewGroup").ToList();
            Assert.AreEqual(1, newGroups.Count);

            var entry1 = _settings.FindAssetEntry(ctx1.Guid);
            var entry2 = _settings.FindAssetEntry(ctx2.Guid);
            Assert.AreSame(entry1.parentGroup, entry2.parentGroup, "2件目のアセットも1件目で作成されたグループへ書き込まれるべき。");
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

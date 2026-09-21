using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerApplier.Apply の Skipped 時クリーンアップ挙動を、
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

            if (AssetDatabase.IsValidFolder(PriorityTestFolder))
                AssetDatabase.DeleteAsset(PriorityTestFolder);
        }

        private static AssetContext Ctx(string guid) =>
            new AssetContext(guid, "Assets/Foo.prefab", typeof(GameObject));

        // --- Order 優先順位テスト用の実アセット生成ヘルパー ---
        //
        // AddressTellerApplier.Apply が実際に settings.CreateOrMoveEntry まで到達するテスト（Ok を期待するもの）は、
        // guid が AssetDatabase.GUIDToAssetPath で実パスへ解決できないと Addressables 本体が readOnly の
        // プレースホルダエントリを作ってしまい EntryRejectedByAddressables になる
        // （下の Apply_GuidDoesNotResolveToAnAsset_... テストが検証している挙動そのもの）。
        // このファイルの他の Skipped/LabelsOnly 系テストは settings.CreateOrMoveEntry を直接呼んで
        // エントリを事前投入しているだけで、この経路（Apply 内部での新規書き込み）を通らないため
        // 架空の guid でも問題にならない。Order 優先順位テストは実際に書き込み成功(Ok)を検証する必要があるため、
        // AddressTellerGroupDefaultTests と同様に実アセットを作成し、その本物の guid を使う。
        private const string PriorityTestFolder = "Assets/_AddressTellerApplierPriorityTestTemp";

        private static string CreateRealPrefabAndGetGuid(string fileName)
        {
            if (!AssetDatabase.IsValidFolder(PriorityTestFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerApplierPriorityTestTemp");

            var path = $"{PriorityTestFolder}/{fileName}.prefab";
            var go = new GameObject(fileName);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            return AssetDatabase.AssetPathToGUID(path);
        }

        private static AddressResolution EmptyResolution(params RuleEvaluationError[] errors) =>
            new AddressResolution(Array.Empty<AddressCandidate>(), new HashSet<string>(), errors);

        private static AddressResolution LabelsOnlyResolution(params string[] labels) =>
            new AddressResolution(Array.Empty<AddressCandidate>(), new HashSet<string>(labels));

        private static AddressResolution OneCandidateResolution(string groupName, string address) =>
            new AddressResolution(new[] { new AddressCandidate(groupName, address) }, new HashSet<string>());

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
        public void Skipped_WithConfigureFailures_KeepsEntry()
        {
            // 他のルールの Configure() が例外を送出した実行では、managedGroups が
            // 「失敗したルールが本来担当していたグループを別のルールがたまたま宣言していただけ」の
            // 可能性があり信頼できないため、Skipped でも削除してはならない。
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-managed"), EmptyResolution(), _settings, ExistingGroupNames(), managedGroups, hasConfigureFailures: true);

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            Assert.IsNotNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void LabelsOnly_NoExistingEntry_ReturnsLabelsOnlyStatus_CreatesNoEntry()
        {
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-new"), LabelsOnlyResolution("tag"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.LabelsOnly, result.Status);
            Assert.IsNull(_settings.FindAssetEntry("guid-new"));
        }

        [Test]
        public void LabelsOnly_ExistingEntryInManagedGroup_CleanupEnabled_NotRemoved_LabelsAdded()
        {
            // LabelsOnly はラベルのみルールがマッチしているため CleanupStaleEntries が ON でも削除されてはならない。
            AddressTellerSettings.CleanupStaleEntries = true;
            var entry = _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            entry.SetAddress("ExistingAddress");
            entry.SetLabel("existingLabel", true);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-managed"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.LabelsOnly, result.Status);
            var updated = _settings.FindAssetEntry("guid-managed");
            Assert.IsNotNull(updated);
            Assert.AreEqual("ExistingAddress", updated.address);
            CollectionAssert.AreEquivalent(new[] { "existingLabel", "newLabel" }, updated.labels);
        }

        [Test]
        public void LabelsOnly_ExistingEntryInUnownedGroup_LabelsAdded()
        {
            // ラベル加算は削除と異なり所有権を問わない。AddressTeller が Address() を宣言していない
            // グループ(ユーザーが手動登録したエントリ等)であっても、既存エントリがあればラベルを加える。
            var entry = _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            entry.SetAddress("ExistingAddress");
            entry.SetLabel("existingLabel", true);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(Ctx("guid-other"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.LabelsOnly, result.Status);
            var updated = _settings.FindAssetEntry("guid-other");
            Assert.IsNotNull(updated);
            Assert.AreEqual("ExistingAddress", updated.address);
            CollectionAssert.AreEquivalent(new[] { "existingLabel", "newLabel" }, updated.labels);
        }

        [Test]
        public void LabelsOnly_OwnedGroupsEmpty_ExistingEntry_LabelsAdded()
        {
            // AnyGroup() だけの構成（Address() を宣言するルールが1つもない）では ownedGroups が空集合になる。
            // それでも既存エントリへのラベル加算は行われる（ラベル加算は所有権を問わないため）。
            var entry = _settings.CreateOrMoveEntry("guid-other", _otherGroup);
            entry.SetAddress("ExistingAddress");
            entry.SetLabel("existingLabel", true);

            var result = AddressTellerApplier.Apply(Ctx("guid-other"), LabelsOnlyResolution("newLabel"), _settings, ExistingGroupNames(), new HashSet<string>());

            Assert.AreEqual(ValidationStatus.LabelsOnly, result.Status);
            var updated = _settings.FindAssetEntry("guid-other");
            Assert.IsNotNull(updated);
            CollectionAssert.AreEquivalent(new[] { "existingLabel", "newLabel" }, updated.labels);
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

            var result = AddressTellerApplier.RemoveEntryForDeletedAsset("guid-other", _settings, managedGroups);

            Assert.IsNull(result);
            Assert.IsNotNull(_settings.FindAssetEntry("guid-other"));
        }

        [Test]
        public void RemoveEntryForDeletedAsset_CleanupDisabled_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = false;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.RemoveEntryForDeletedAsset("guid-managed", _settings, managedGroups);

            Assert.IsNull(result);
            Assert.IsNotNull(_settings.FindAssetEntry("guid-managed"));
        }

        [Test]
        public void RemoveEntryForDeletedAsset_WithConfigureFailures_KeepsEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.RemoveEntryForDeletedAsset("guid-managed", _settings, managedGroups, hasConfigureFailures: true);

            Assert.IsNull(result);
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

        [Test]
        public void RemoveEntryForDeletedAsset_RemovesEntry_ClearedEntryLabelsAreOrdinalSorted()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var entry = _settings.CreateOrMoveEntry("guid-managed", _managedGroup);
            entry.SetAddress("SomeAddress");
            // 意図的に Ordinal 昇順でない順で付与し、ClearedEntry.Labels が並べ替えられることを検証する。
            entry.SetLabel("zebra", true);
            entry.SetLabel("apple", true);
            entry.SetLabel("Mango", true);
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.RemoveEntryForDeletedAsset("guid-managed", _settings, managedGroups);

            Assert.IsTrue(result.HasValue);
            CollectionAssert.AreEqual(new[] { "Mango", "apple", "zebra" }, result.Value.Labels);
        }

        [Test]
        public void Apply_CandidateGroupMissingFromSettings_ReturnsGroupNotFound_DoesNotThrow()
        {
            // existingGroupNames は呼び出し側が別途構築するコレクションであり、_settings.groups と食い違いうる
            // (テスト用の意図的なミスマッチ)。Validate は existingGroupNames だけを見て Ok を返すため、
            // Apply は settings.FindGroup(candidate.GroupName) まで進むが、そのグループは実際には
            // _settings に存在しないため null が返る。これは Addressables が拒否したのではなくグループが
            // 実在しないという状態そのものなので、EntryRejectedByAddressables ではなく GroupNotFound を返し、
            // かつ group が null のままクラッシュしないことを検証する。
            var existingGroupNames = new HashSet<string> { "GroupNotInSettings" };
            var managedGroups = new HashSet<string> { "GroupNotInSettings" };

            ValidationResult result = null;
            Assert.DoesNotThrow(() =>
                result = AddressTellerApplier.Apply(
                    Ctx("guid-missing-group"), OneCandidateResolution("GroupNotInSettings", "SomeAddress"),
                    _settings, existingGroupNames, managedGroups));

            Assert.AreEqual(ValidationStatus.GroupNotFound, result.Status);
            Assert.IsNull(_settings.FindAssetEntry("guid-missing-group"));
        }

        // --- Order による優先順位判定 ---

        [Test]
        public void SpecificOrderWins_EntryMovesToWinnerGroupNotLoserGroup()
        {
            // 広い(Order=100, ManagedGroup)+特定(Order=0, OtherGroup) の2候補。
            // Order 最小の特定側が勝つため、エントリは OtherGroup に作られ、ManagedGroup には作られない。
            var guid = CreateRealPrefabAndGetGuid("PriorityWinnerAsset");
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_managedGroup.Name, "broadAddr", order: 100),
                    new AddressCandidate(_otherGroup.Name, "specificAddr", order: 0),
                },
                new HashSet<string>());

            var result = AddressTellerApplier.Apply(Ctx(guid), resolution, _settings, ExistingGroupNames());

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
            var entry = _settings.FindAssetEntry(guid);
            Assert.IsNotNull(entry);
            Assert.AreEqual(_otherGroup.Name, entry.parentGroup.Name);
            Assert.AreEqual("specificAddr", entry.address);
        }

        [Test]
        public void LabelsAccumulateFromAllMatchingRules_RegardlessOfOrder()
        {
            // ラベルは Order による優先順位の影響を受けず、マッチした全ルールから蓄積される。
            var guid = CreateRealPrefabAndGetGuid("PriorityLabelsAsset");
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_managedGroup.Name, "broadAddr", order: 100),
                    new AddressCandidate(_otherGroup.Name, "specificAddr", order: 0),
                },
                new HashSet<string> { "fromBroad", "fromSpecific" });

            var result = AddressTellerApplier.Apply(Ctx(guid), resolution, _settings, ExistingGroupNames());

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
            var entry = _settings.FindAssetEntry(guid);
            Assert.IsNotNull(entry);
            CollectionAssert.AreEquivalent(new[] { "fromBroad", "fromSpecific" }, entry.labels);
        }

        [Test]
        public void ValidatePredictApply_AgreeOnWinnerWhenPriorityResolves()
        {
            // Validate / Predict / Apply が同一の resolution に対して同じ勝者を選ぶことを確認する。
            var guid = CreateRealPrefabAndGetGuid("PriorityConsistencyAsset");
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_managedGroup.Name, "broadAddr", order: 100),
                    new AddressCandidate(_otherGroup.Name, "specificAddr", order: 0),
                },
                new HashSet<string>());
            var existingGroupNames = ExistingGroupNames();

            var validateResult = AddressTellerApplier.Validate(Ctx(guid), resolution, existingGroupNames);
            var prediction = AddressTellerApplier.Predict(Ctx(guid), resolution, _settings, existingGroupNames, new HashSet<string>());
            var applyResult = AddressTellerApplier.Apply(Ctx(guid), resolution, _settings, existingGroupNames);

            Assert.AreEqual(ValidationStatus.Ok, validateResult.Status);
            Assert.AreEqual(ValidationStatus.Ok, prediction.Validation.Status);
            Assert.AreEqual(ValidationStatus.Ok, applyResult.Status);
            Assert.AreEqual("specificAddr", prediction.PredictedEntry.Address);
            Assert.AreEqual(_otherGroup.Name, prediction.PredictedEntry.GroupName);

            var actualEntry = _settings.FindAssetEntry(guid);
            Assert.AreEqual(prediction.PredictedEntry.Address, actualEntry.address);
            Assert.AreEqual(prediction.PredictedEntry.GroupName, actualEntry.parentGroup.Name);
        }

        [Test]
        public void ValidatePredictApply_AgreeOnConflictWhenTied()
        {
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_managedGroup.Name, "addr1", order: 0),
                    new AddressCandidate(_otherGroup.Name, "addr2", order: 0),
                },
                new HashSet<string>());
            var existingGroupNames = ExistingGroupNames();

            var validateResult = AddressTellerApplier.Validate(Ctx("guid-tie"), resolution, existingGroupNames);
            var prediction = AddressTellerApplier.Predict(Ctx("guid-tie"), resolution, _settings, existingGroupNames, new HashSet<string>());
            var applyResult = AddressTellerApplier.Apply(Ctx("guid-tie"), resolution, _settings, existingGroupNames);

            Assert.AreEqual(ValidationStatus.ConflictingAddress, validateResult.Status);
            Assert.AreEqual(ValidationStatus.ConflictingAddress, prediction.Validation.Status);
            Assert.AreEqual(PredictedAction.NoOp, prediction.Action);
            Assert.AreEqual(ValidationStatus.ConflictingAddress, applyResult.Status);
            Assert.IsNull(_settings.FindAssetEntry("guid-tie"));
        }

        [Test]
        public void Apply_GuidDoesNotResolveToAnAsset_RemovesLeftoverReadOnlyEntry_ReturnsEntryRejectedByAddressables()
        {
            // "guid-unresolvable" はプロジェクト内のどのアセットにも対応しないため、
            // AssetDatabase.GUIDToAssetPath は空文字を返す。この場合、Addressables 本体の
            // CreateAndAddEntryToGroup は例外を投げず、address=guid の readOnly エントリを作成して
            // グループに追加してしまう(AddressTellerApplier.cs の当該コメント参照)。
            // AddressTeller はこれを拒否扱いにし、作られてしまったエントリを取り除く必要がある。
            var managedGroups = new HashSet<string> { _managedGroup.Name };

            var result = AddressTellerApplier.Apply(
                Ctx("guid-unresolvable"), OneCandidateResolution(_managedGroup.Name, "SomeAddress"),
                _settings, ExistingGroupNames(), managedGroups);

            Assert.AreEqual(ValidationStatus.EntryRejectedByAddressables, result.Status);
            Assert.IsNull(_settings.FindAssetEntry("guid-unresolvable"),
                "The read-only placeholder entry Addressables created should have been removed, not left behind.");
        }
    }
}

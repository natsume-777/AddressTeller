using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerApplier.Apply / Predict が、ラベル台帳（AddressTellerLabelLedger）と
    /// <see cref="AddressTellerSettings.RemoveStaleManagedLabels"/> に従って、AddressTeller が過去に書いた
    /// 不要なラベルだけを外す（または OFF のとき通知にとどめる）ことを、ディスクに保存しない一時的な
    /// AddressableAssetSettings 上で検証する。台帳の読み書き先は AddressTellerAddressablesPollutionGuard が
    /// 一時ファイルへ切り替え済みで、各テストの後に AddressTellerLabelLedgerIsolation が空へ戻す。
    /// </summary>
    public class AddressTellerStaleManagedLabelApplierTests
    {
        private const string TestFolder = "Assets/_AddressTellerStaleLabelApplierTestTemp";

        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _group;
        private bool _originalRemoveStale;

        [SetUp]
        public void SetUp()
        {
            LabelLedgerTestUtil.Reset();
            _originalRemoveStale = AddressTellerSettings.RemoveStaleManagedLabels;
            AddressTellerSettings.RemoveStaleManagedLabels = false;

            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerStaleLabelApplierTestSettings", false, false);
            _group = _settings.CreateGroup("ManagedGroup", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = _originalRemoveStale;
            LabelLedgerTestUtil.Reset();

            Object.DestroyImmediate(_group, true);
            Object.DestroyImmediate(_settings, true);

            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);
        }

        private static AssetContext Ctx(string guid, string path = "Assets/Foo.prefab") =>
            new AssetContext(guid, path, typeof(GameObject));

        // Apply が新規書き込みまで到達するには guid が実アセットへ解決できる必要がある
        // （AddressTellerApplierApplyTests.CreateRealPrefabAndGetGuid 付近のコメント参照）。
        private static string CreateRealPrefabAndGetGuid(string fileName)
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerStaleLabelApplierTestTemp");

            var path = $"{TestFolder}/{fileName}.prefab";
            var go = new GameObject(fileName);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return AssetDatabase.AssetPathToGUID(path);
        }

        private HashSet<string> GroupNames() => new HashSet<string> { _group.Name };

        private AddressResolution Winner(string[] candidateLabels, params string[] labelOnly) =>
            new AddressResolution(
                new[] { new AddressCandidate(_group.Name, "addr", labels: candidateLabels) },
                new HashSet<string>(labelOnly));

        private AddressableAssetEntry EntryWithLabels(string guid, params string[] labels)
        {
            var entry = _settings.CreateOrMoveEntry(guid, _group);
            entry.SetAddress("addr");
            foreach (var label in labels)
                entry.SetLabel(label, true);
            return entry;
        }

        // --- 除去 OFF（既定）: 何も外さない ---

        [Test]
        public void Apply_RemoveOff_LedgerLabelNotInFinalSet_IsKept()
        {
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("Off");
            var entry = EntryWithLabels(guid, "stale");

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "stale", "fresh" }, entry.labels);
        }

        // --- 除去 ON ---

        [Test]
        public void Apply_RemoveOn_RemovesOnlyLedgerLabelsMissingFromFinalSet()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale", "keep" });
            var guid = CreateRealPrefabAndGetGuid("On");
            var entry = EntryWithLabels(guid, "stale", "keep");

            LogAssert.Expect(LogType.Warning, new Regex("Removing stale managed label.*label='stale'"));

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "keep", "fresh" }), _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "keep", "fresh" }, entry.labels);
        }

        [Test]
        public void Apply_RemoveOn_LabelNotInLedger_IsNeverTouched()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("NotInLedger");
            var entry = EntryWithLabels(guid, "stale", "manual");

            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "manual", "fresh" }, entry.labels);
        }

        [Test]
        public void Apply_RemoveOn_EmptyLedger_RemovesNothing()
        {
            // 台帳が無い（ファイル不在）状態は空の台帳として扱う。何を除去すべきか判定できないので、何も外さない。
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            var guid = CreateRealPrefabAndGetGuid("EmptyLedger");
            var entry = EntryWithLabels(guid, "old");

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "old", "fresh" }, entry.labels);
        }

        [Test]
        public void Apply_RemoveOn_LabelOnlyLabelCountsAsFinal()
        {
            // ラベル専用ルール由来のラベルは勝敗と無関係に最終集合に入るので、台帳にあっても外さない。
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "always" });
            var guid = CreateRealPrefabAndGetGuid("LabelOnlyFinal");
            var entry = EntryWithLabels(guid, "always");

            AddressTellerApplier.Apply(Ctx(guid), Winner(new string[0], "always"), _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "always" }, entry.labels);
        }

        [Test]
        public void Apply_RemoveOn_LosingCandidateLabelIsNotFinal_AndGetsRemoved()
        {
            // 負けた候補のラベルは最終集合に入らない。以前の実行で付いていて台帳にあるなら外れる。
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "fromBroad" });
            var guid = CreateRealPrefabAndGetGuid("Loser");
            var entry = EntryWithLabels(guid, "fromBroad");
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_group.Name, "broadAddr", order: 100, labels: new[] { "fromBroad" }),
                    new AddressCandidate(_group.Name, "specificAddr", order: 0, labels: new[] { "fromSpecific" }),
                },
                new HashSet<string>());

            LogAssert.Expect(LogType.Warning, new Regex("label='fromBroad'"));

            AddressTellerApplier.Apply(Ctx(guid), resolution, _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "fromSpecific" }, entry.labels);
        }

        // --- 書き込み対象でないエントリには触れない ---

        [Test]
        public void Apply_RemoveOn_Conflict_LeavesExistingLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-conflict", "stale");
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_group.Name, "a", order: 0, labels: new[] { "x" }),
                    new AddressCandidate(_group.Name, "b", order: 0, labels: new[] { "y" }),
                },
                new HashSet<string>());

            var result = AddressTellerApplier.Apply(Ctx("guid-conflict"), resolution, _settings, GroupNames());

            Assert.AreEqual(ValidationStatus.ConflictingAddress, result.Status);
            CollectionAssert.AreEquivalent(new[] { "stale" }, entry.labels);
        }

        [Test]
        public void Apply_RemoveOn_BlockedByRuleError_LeavesExistingLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-blocked", "stale");
            var resolution = new AddressResolution(
                new[] { new AddressCandidate(_group.Name, "a", order: 5, labels: new[] { "x" }) },
                new HashSet<string>(),
                new[] { new RuleEvaluationError("HighPriorityRule", "boom", order: 0, canProduceAddress: true) });

            var result = AddressTellerApplier.Apply(Ctx("guid-blocked"), resolution, _settings, GroupNames());

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, result.Status);
            CollectionAssert.AreEquivalent(new[] { "stale" }, entry.labels);
        }

        [Test]
        public void Apply_RemoveOn_Skipped_LeavesExistingLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-skipped", "stale");
            var resolution = new AddressResolution(new AddressCandidate[0], new HashSet<string>());

            var result = AddressTellerApplier.Apply(Ctx("guid-skipped"), resolution, _settings, GroupNames());

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            CollectionAssert.AreEquivalent(new[] { "stale" }, entry.labels);
        }

        // --- LabelsOnly 経路 ---

        [Test]
        public void LabelsOnly_RemoveOn_RemovesStaleLedgerLabel_AndRecordsWrittenLabel()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-labels-only", "stale", "manual");
            entry.ReadOnly = false;

            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));

            var result = AddressTellerApplier.Apply(
                Ctx("guid-labels-only"), new AddressResolution(new AddressCandidate[0], new HashSet<string> { "tag" }), _settings, GroupNames());

            Assert.AreEqual(ValidationStatus.LabelsOnly, result.Status);
            CollectionAssert.AreEquivalent(new[] { "manual", "tag" }, entry.labels);
            Assert.IsTrue(AddressTellerLabelLedger.IsManaged("tag"));
        }

        [Test]
        public void LabelsOnly_RemoveOff_KeepsStaleLedgerLabel_AndRecordsWrittenLabel()
        {
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-labels-only-off", "stale");
            entry.ReadOnly = false;

            AddressTellerApplier.Apply(
                Ctx("guid-labels-only-off"), new AddressResolution(new AddressCandidate[0], new HashSet<string> { "tag" }), _settings, GroupNames());

            CollectionAssert.AreEquivalent(new[] { "stale", "tag" }, entry.labels);
            Assert.IsTrue(AddressTellerLabelLedger.IsManaged("tag"));
        }

        // --- 台帳への記録 ---

        [Test]
        public void Apply_RecordsEveryWrittenLabelInLedger()
        {
            var guid = CreateRealPrefabAndGetGuid("Records");

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }, "always"), _settings, GroupNames());

            CollectionAssert.AreEqual(new[] { "always", "fresh" }, AddressTellerLabelLedger.GetAll());
            StringAssert.Contains(AddressTellerLabelLedger.MarkerValue, System.IO.File.ReadAllText(AddressTellerLabelLedger.AbsoluteFilePath));
        }

        [Test]
        public void Apply_LabelsAlreadyInLedger_DoesNotRewriteLedgerFile()
        {
            var guid = CreateRealPrefabAndGetGuid("NoRewrite");
            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames());

            var path = AddressTellerLabelLedger.AbsoluteFilePath;
            var oldTime = new System.DateTime(2000, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            System.IO.File.SetLastWriteTimeUtc(path, oldTime);

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames());

            Assert.AreEqual(oldTime, System.IO.File.GetLastWriteTimeUtc(path), "差分が無いときは台帳ファイルへ書き込まない。");
        }

        [Test]
        public void Apply_ConflictOrSkipped_DoesNotRecordLabels()
        {
            var resolution = new AddressResolution(
                new[]
                {
                    new AddressCandidate(_group.Name, "a", order: 0, labels: new[] { "x" }),
                    new AddressCandidate(_group.Name, "b", order: 0, labels: new[] { "y" }),
                },
                new HashSet<string> { "z" });

            AddressTellerApplier.Apply(Ctx("guid-no-write"), resolution, _settings, GroupNames());

            CollectionAssert.IsEmpty(AddressTellerLabelLedger.GetAll());
            Assert.IsFalse(System.IO.File.Exists(AddressTellerLabelLedger.AbsoluteFilePath));
        }

        // --- Predict ---

        [Test]
        public void Predict_RemoveOff_ReportsNotice_AndKeepsLabelInPrediction()
        {
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("PredictOff");
            EntryWithLabels(guid, "stale");
            var ctx = Ctx(guid);

            var prediction = AddressTellerApplier.Predict(ctx, Winner(new[] { "fresh" }), _settings, GroupNames(), new HashSet<string>());

            Assert.IsNotNull(prediction.Notice);
            Assert.AreEqual(ValidationStatus.StaleManagedLabelKept, prediction.Notice.Status);
            Assert.IsTrue(prediction.Notice.IsOk);
            Assert.IsFalse(prediction.Notice.IsBlocking);
            Assert.AreSame(ctx, prediction.Notice.Context);
            StringAssert.Contains("stale", prediction.Notice.Message);
            Assert.AreEqual(ValidationStatus.Ok, prediction.Validation.Status);
            CollectionAssert.AreEqual(new[] { "fresh", "stale" }, prediction.PredictedEntry.Labels);
        }

        [Test]
        public void Predict_RemoveOn_NoNotice_AndPredictsRemoval()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("PredictOn");
            EntryWithLabels(guid, "stale", "manual");

            var prediction = AddressTellerApplier.Predict(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames(), new HashSet<string>());

            Assert.IsNull(prediction.Notice, "ON のときは実際に外すので通知は出さない（差分として現れる）。");
            CollectionAssert.AreEqual(new[] { "fresh", "manual" }, prediction.PredictedEntry.Labels);
        }

        [Test]
        public void Predict_NothingStale_NoNotice()
        {
            AddressTellerLabelLedger.Record(new[] { "fresh" });
            var guid = CreateRealPrefabAndGetGuid("PredictClean");
            EntryWithLabels(guid, "fresh", "manual");

            var prediction = AddressTellerApplier.Predict(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames(), new HashSet<string>());

            Assert.IsNull(prediction.Notice);
        }

        [Test]
        public void Predict_NewEntry_NoNotice()
        {
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("PredictNew");

            var prediction = AddressTellerApplier.Predict(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames(), new HashSet<string>());

            Assert.IsNull(prediction.Notice);
            CollectionAssert.AreEqual(new[] { "fresh" }, prediction.PredictedEntry.Labels);
        }

        [Test]
        public void PredictAndApply_RemoveOn_AgreeOnFinalLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale", "keep" });
            var guid = CreateRealPrefabAndGetGuid("PredictApplyAgree");
            var entry = EntryWithLabels(guid, "stale", "keep", "manual");
            var resolution = Winner(new[] { "keep", "fresh" }, "always");

            var prediction = AddressTellerApplier.Predict(Ctx(guid), resolution, _settings, GroupNames(), new HashSet<string>());

            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));
            AddressTellerApplier.Apply(Ctx(guid), resolution, _settings, GroupNames());

            CollectionAssert.AreEquivalent(prediction.PredictedEntry.Labels, entry.labels);
        }

        [Test]
        public void PredictAndApply_LabelsOnlyRemoveOn_AgreeOnFinalLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-labels-only-agree", "stale", "manual");
            entry.ReadOnly = false;
            var resolution = new AddressResolution(new AddressCandidate[0], new HashSet<string> { "tag" });

            var prediction = AddressTellerApplier.Predict(Ctx("guid-labels-only-agree"), resolution, _settings, GroupNames(), new HashSet<string>());

            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));
            AddressTellerApplier.Apply(Ctx("guid-labels-only-agree"), resolution, _settings, GroupNames());

            CollectionAssert.AreEquivalent(prediction.PredictedEntry.Labels, entry.labels);
        }

        [Test]
        public void Predict_LabelsOnlyRemoveOff_ReportsNotice()
        {
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var entry = EntryWithLabels("guid-labels-only-notice", "stale");
            entry.ReadOnly = false;

            var prediction = AddressTellerApplier.Predict(
                Ctx("guid-labels-only-notice"),
                new AddressResolution(new AddressCandidate[0], new HashSet<string> { "tag" }),
                _settings, GroupNames(), new HashSet<string>());

            Assert.IsNotNull(prediction.Notice);
            Assert.AreEqual(ValidationStatus.StaleManagedLabelKept, prediction.Notice.Status);
        }

        // --- Snapshot Restore で復元できる ---

        [Test]
        public void SnapshotRestore_BringsBackLabelRemovedByApply()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("Restore");
            var entry = EntryWithLabels(guid, "stale");
            var snapshot = AddressTellerSnapshotService.Capture(_settings);

            LogAssert.Expect(LogType.Warning, new Regex("label='stale'"));
            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames());
            Assert.IsFalse(entry.labels.Contains("stale"), "前提: Apply が stale を外している。");

            AddressTellerSnapshotService.Restore(snapshot, _settings, SnapshotRestoreMode.Exact);

            CollectionAssert.AreEquivalent(new[] { "stale" }, _settings.FindAssetEntry(guid).labels);
        }

        // --- 除去を止める条件: ルール例外・呼び出し側の停止指示 ---

        private AddressResolution WinnerWithLabelOnlyRuleError(string[] candidateLabels) =>
            new AddressResolution(
                new[] { new AddressCandidate(_group.Name, "addr", labels: candidateLabels) },
                new HashSet<string>(),
                new[] { new RuleEvaluationError("LabelOnlyRule", "boom", order: 0, canProduceAddress: false) });

        [Test]
        public void Apply_RemoveOn_AnyRuleErrorOnTheAsset_KeepsLabels_ButStillAddsFinalLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("RuleError");
            var entry = EntryWithLabels(guid, "stale");

            var result = AddressTellerApplier.Apply(Ctx(guid), WinnerWithLabelOnlyRuleError(new[] { "fresh" }), _settings, GroupNames());

            Assert.AreEqual(ValidationStatus.Ok, result.Status, "ラベル専用ルールの例外は勝者を止めない。");
            CollectionAssert.AreEquivalent(new[] { "stale", "fresh" }, entry.labels);
        }

        [Test]
        public void Predict_AnyRuleErrorOnTheAsset_KeepsLabels_AndReportsNoNotice()
        {
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("PredictRuleError");
            EntryWithLabels(guid, "stale");

            var prediction = AddressTellerApplier.Predict(
                Ctx(guid), WinnerWithLabelOnlyRuleError(new[] { "fresh" }), _settings, GroupNames(), new HashSet<string>());

            Assert.IsNull(prediction.Notice);
            CollectionAssert.AreEqual(new[] { "fresh", "stale" }, prediction.PredictedEntry.Labels);
        }

        [Test]
        public void Apply_RemoveOn_SuspendLabelRemoval_KeepsLabels()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("Suspended");
            var entry = EntryWithLabels(guid, "stale");

            AddressTellerApplier.Apply(Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames(), suspendLabelRemoval: true);

            CollectionAssert.AreEquivalent(new[] { "stale", "fresh" }, entry.labels);
        }

        [Test]
        public void Predict_SuspendStaleCleanup_KeepsLabels_AndReportsNoNotice()
        {
            AddressTellerSettings.RemoveStaleManagedLabels = true;
            AddressTellerLabelLedger.Record(new[] { "stale" });
            var guid = CreateRealPrefabAndGetGuid("PredictSuspended");
            EntryWithLabels(guid, "stale");

            var prediction = AddressTellerApplier.Predict(
                Ctx(guid), Winner(new[] { "fresh" }), _settings, GroupNames(), new HashSet<string>(), suspendStaleCleanup: true);

            CollectionAssert.AreEqual(new[] { "fresh", "stale" }, prediction.PredictedEntry.Labels);
            Assert.IsNull(prediction.Notice);
        }

        // --- FindStaleManagedLabels ---

        [Test]
        public void FindStaleManagedLabels_ReturnsOrdinalSortedLedgerLabelsMissingFromFinal()
        {
            AddressTellerLabelLedger.Record(new[] { "b", "a", "keep" });

            var stale = AddressTellerApplier.FindStaleManagedLabels(
                new HashSet<string> { "b", "a", "keep", "manual" }, new[] { "keep" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, stale);
        }

        [Test]
        public void FindStaleManagedLabels_NothingStale_ReturnsNull()
        {
            AddressTellerLabelLedger.Record(new[] { "a" });

            Assert.IsNull(AddressTellerApplier.FindStaleManagedLabels(new HashSet<string> { "a", "manual" }, new[] { "a" }));
        }
    }
}

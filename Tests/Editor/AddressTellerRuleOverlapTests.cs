using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// ルールクラス間の重なり（RuleOverlap / DryRunResult.Overlaps）と、厳格モード
    /// （AddressTellerSettings.StrictRuleOverlaps → ValidationStatus.UndeclaredRuleOverlap）の検証。
    /// 集計そのもの（RuleOverlapCollector）と宣言（OverriddenBy）は AssetDatabase なしの純粋なテスト、
    /// ValidateAll / BuildPredictedSnapshot / ApplyAll の各経路は実アセットとメモリ上の
    /// AddressableAssetSettings で確認する。
    /// </summary>
    public class AddressTellerRuleOverlapTests
    {
        // ---------------- 純粋なテスト: 集計 ----------------

        private static AddressCandidate Candidate(string sourceClass, int order, int ruleIndex = 0, params string[] overriddenBy)
            => new AddressCandidate("G", "addr", sourceClass, null, ruleIndex, order, null, overriddenBy.Length == 0 ? null : overriddenBy);

        private static AddressResolution Resolution(params AddressCandidate[] candidates)
            => new AddressResolution(candidates, new HashSet<string>());

        [Test]
        public void Collector_SingleCandidate_IsNotAnOverlap()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/A.prefab", Resolution(Candidate("Broad", 100)));

            Assert.AreEqual(0, collector.Build().Count);
        }

        [Test]
        public void Collector_WinnerAndLoser_CountsOnePairPerAsset()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/B.prefab", Resolution(Candidate("Broad", 100), Candidate("Narrow", 0)));
            collector.Add("Assets/A.prefab", Resolution(Candidate("Narrow", 0), Candidate("Broad", 100)));

            var overlaps = collector.Build();
            Assert.AreEqual(1, overlaps.Count);
            Assert.AreEqual("Broad", overlaps[0].OverriddenRule);
            Assert.AreEqual("Narrow", overlaps[0].OverridingRule);
            Assert.AreEqual(2, overlaps[0].Count);
            Assert.IsFalse(overlaps[0].IsDeclared);
            CollectionAssert.AreEqual(new[] { "Assets/A.prefab", "Assets/B.prefab" }, overlaps[0].ExamplePaths);
        }

        [Test]
        public void Collector_TiedAtLowestOrder_IsNotAnOverlap()
        {
            var collector = new RuleOverlapCollector();

            // 最小 Order が2件で同点（Conflict）。高 Order の3件目が負けていても、勝者が決まらないので数えない。
            collector.Add("Assets/A.prefab", Resolution(Candidate("TieA", 10), Candidate("TieB", 10), Candidate("Broad", 100)));

            Assert.AreEqual(0, collector.Build().Count);
        }

        [Test]
        public void Collector_WinnerBlockedByRuleError_IsNotAnOverlap()
        {
            var collector = new RuleOverlapCollector();
            var resolution = new AddressResolution(
                new[] { Candidate("Mid", 50), Candidate("Broad", 100) },
                new HashSet<string>(),
                new[] { new RuleEvaluationError("Thrower", "boom", 0, canProduceAddress: true) });

            collector.Add("Assets/A.prefab", resolution);

            Assert.AreEqual(0, collector.Build().Count, "勝者より優先度が高いアドレス産出ルールが例外を出したアセットは、勝者が確定しない。");
        }

        [Test]
        public void Collector_ErrorFromLowerPriorityRule_DoesNotHideTheOverlap()
        {
            var collector = new RuleOverlapCollector();
            var resolution = new AddressResolution(
                new[] { Candidate("Narrow", 0), Candidate("Mid", 50) },
                new HashSet<string>(),
                new[] { new RuleEvaluationError("LowThrower", "boom", 200, canProduceAddress: true) });

            collector.Add("Assets/A.prefab", resolution);

            Assert.AreEqual(1, collector.Build().Count, "勝者より低い優先度のルールの例外は、勝者の書き込みも重なりの集計も止めない。");
        }

        [Test]
        public void Collector_Declaration_SplitsTheSamePairByWhetherItWasDeclared()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/Declared.prefab", Resolution(Candidate("Narrow", 0), Candidate("Broad", 100, 0, "Narrow")));
            collector.Add("Assets/Undeclared.prefab", Resolution(Candidate("Narrow", 0), Candidate("Broad", 100, 1)));

            var overlaps = collector.Build();
            Assert.AreEqual(2, overlaps.Count);
            Assert.IsFalse(overlaps[0].IsDeclared, "宣言なしが先。");
            CollectionAssert.AreEqual(new[] { "Assets/Undeclared.prefab" }, overlaps[0].ExamplePaths);
            Assert.IsTrue(overlaps[1].IsDeclared);
            CollectionAssert.AreEqual(new[] { "Assets/Declared.prefab" }, overlaps[1].ExamplePaths);
        }

        [Test]
        public void Collector_DeclaringAnotherRule_DoesNotDeclareThisPair()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/A.prefab", Resolution(Candidate("Narrow", 0), Candidate("Broad", 100, 0, "SomethingElse")));

            Assert.IsFalse(collector.Build().Single().IsDeclared);
        }

        [Test]
        public void Collector_TwoEntriesOfOneClassLosingToTheSameWinner_CountTheAssetOnce()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/A.prefab", Resolution(Candidate("Narrow", 0), Candidate("Broad", 100, 0), Candidate("Broad", 100, 1)));

            Assert.AreEqual(1, collector.Build().Single().Count);
        }

        [Test]
        public void Collector_DeclaringAndUndeclaringEntriesOfOneClassLosingToTheSameWinner_CountTheAssetOncePerKind()
        {
            var collector = new RuleOverlapCollector();

            // 同じクラスの2つの Group() の連鎖が、同じアセットで同じ勝者に負ける。一方だけが宣言している。
            collector.Add("Assets/A.prefab", Resolution(Candidate("Narrow", 0), Candidate("Broad", 100, 0, "Narrow"), Candidate("Broad", 100, 1)));

            var overlaps = collector.Build();
            Assert.AreEqual(2, overlaps.Count);
            Assert.IsFalse(overlaps[0].IsDeclared);
            Assert.AreEqual(1, overlaps[0].Count);
            Assert.IsTrue(overlaps[1].IsDeclared);
            Assert.AreEqual(1, overlaps[1].Count);
        }

        [Test]
        public void Collector_TwoLosersOfDifferentClasses_ProduceOnePairEach()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/A.prefab", Resolution(Candidate("Narrow", 0), Candidate("Mid", 50), Candidate("Broad", 100)));

            var overlaps = collector.Build();
            Assert.AreEqual(2, overlaps.Count);
            CollectionAssert.AreEqual(new[] { "Broad", "Mid" }, overlaps.Select(o => o.OverriddenRule));
            Assert.IsTrue(overlaps.All(o => o.OverridingRule == "Narrow" && o.Count == 1));
        }

        [Test]
        public void Collector_ExamplePaths_KeepTheSmallestOrdinalPathsWhateverTheScanOrder()
        {
            var paths = new[] { "Assets/z.prefab", "Assets/B.prefab", "Assets/a.prefab", "Assets/C.prefab", "Assets/A.prefab", "Assets/B.prefab" };
            var expected = paths.Distinct().OrderBy(p => p, StringComparer.Ordinal).Take(RuleOverlapCollector.MaxExamplePaths).ToArray();

            foreach (var ordering in new[] { paths, paths.Reverse().ToArray() })
            {
                var collector = new RuleOverlapCollector();
                foreach (var path in ordering)
                    collector.Add(path, Resolution(Candidate("Narrow", 0), Candidate("Broad", 100)));

                var overlap = collector.Build().Single();
                CollectionAssert.AreEqual(expected, overlap.ExamplePaths);
                Assert.AreEqual(paths.Length, overlap.Count, "例を絞っても、件数は全アセット分。");
            }
        }

        [Test]
        public void Collector_ToList_IsOrderedByOverriddenThenOverridingRuleOrdinal()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/1.prefab", Resolution(Candidate("Zed", 0), Candidate("beta", 100)));
            collector.Add("Assets/2.prefab", Resolution(Candidate("Alpha", 0), Candidate("beta", 100)));
            collector.Add("Assets/3.prefab", Resolution(Candidate("Alpha", 0), Candidate("Gamma", 100)));

            // Ordinal なので大文字が小文字より先: Gamma < beta。
            CollectionAssert.AreEqual(
                new[] { ("Gamma", "Alpha"), ("beta", "Alpha"), ("beta", "Zed") },
                collector.Build().Select(o => (o.OverriddenRule, o.OverridingRule)));
        }

        [Test]
        public void Collector_CandidateWithoutSourceClass_UsesAPlaceholderName()
        {
            var collector = new RuleOverlapCollector();

            collector.Add("Assets/A.prefab", Resolution(Candidate(null, 0), Candidate("Broad", 100)));

            Assert.AreEqual(RuleOverlapCollector.UnnamedRule, collector.Build().Single().OverridingRule);
        }

        // ---------------- 純粋なテスト: OverriddenBy の宣言 ----------------

        private sealed class NarrowRule : AddressRuleBase
        {
            public override int Order => 0;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension.StartsWith("Narrow", StringComparison.Ordinal)))
                    .Address(ctx => "narrow/" + ctx.FileNameWithoutExtension);
            }
        }

        private sealed class MidRule : AddressRuleBase
        {
            public override int Order => 50;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension.StartsWith("Narrow", StringComparison.Ordinal)))
                    .Address(ctx => "mid/" + ctx.FileNameWithoutExtension);
            }
        }

        private sealed class BroadRule : AddressRuleBase
        {
            public override int Order => 100;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder))
                    .Address(ctx => "broad/" + ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>BroadRule と同じだが、NarrowRule に上書きされることを宣言している。</summary>
        private sealed class DeclaringBroadRule : AddressRuleBase
        {
            public override int Order => 100;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder))
                    .Address(ctx => "declaring/" + ctx.FileNameWithoutExtension)
                    .OverriddenBy<NarrowRule>();
            }
        }

        [Test]
        public void OverriddenBy_RecordsTheRuleClassNameOnTheEntry_AndIgnoresRepeats()
        {
            var builder = new AddressRuleBuilderImpl("MyRule");
            builder.Group("G").Where(ctx => true).Address("a")
                .OverriddenBy<NarrowRule>().OverriddenBy<BroadRule>().OverriddenBy<NarrowRule>();

            CollectionAssert.AreEqual(new[] { nameof(NarrowRule), nameof(BroadRule) }, builder.Entries[0].OverriddenBy);
        }

        [Test]
        public void OverriddenBy_IsPerGroupChain()
        {
            var builder = new AddressRuleBuilderImpl("MyRule");
            builder.Group("G").Where(ctx => true).Address("a").OverriddenBy<NarrowRule>();
            builder.Group("G").Where(ctx => true).Address("b");
            builder.AnyGroup().Where(ctx => true).Label("x");

            var entries = builder.Entries;
            Assert.AreEqual(1, entries[0].OverriddenBy.Count);
            Assert.AreEqual(0, entries[1].OverriddenBy.Count);
            Assert.AreEqual(0, entries[2].OverriddenBy.Count);
        }

        [Test]
        public void OverriddenBy_SurvivesTheSentinelAndFolderGuidRebuilds()
        {
            var builder = new AddressRuleBuilderImpl("MyRule");
            builder.GroupDefault().Where(ctx => true).Address("a").OverriddenBy<NarrowRule>();
            var entry = builder.Entries[0];

            CollectionAssert.AreEqual(new[] { nameof(NarrowRule) }, entry.WithGroupName("Resolved").OverriddenBy);
            CollectionAssert.AreEqual(new[] { nameof(NarrowRule) }, entry.WithDeclaredFolders(new[] { "Assets/A" }, null, null).OverriddenBy);
        }

        [Test]
        public void Evaluate_AndExplain_CarryTheDeclarationToTheCandidate()
        {
            var builder = new AddressRuleBuilderImpl("MyRule");
            builder.Group("G").Where(ctx => true).Address("a").OverriddenBy<NarrowRule>();
            var entries = builder.Entries;
            var context = new AssetContext("guid", "Assets/A.prefab", typeof(GameObject));

            var evaluated = RuleEvaluator.Evaluate(context, entries);
            var explained = RuleEvaluator.Explain(context, entries);

            CollectionAssert.AreEqual(new[] { nameof(NarrowRule) }, evaluated.AddressCandidates.Single().OverriddenBy);
            CollectionAssert.AreEqual(new[] { nameof(NarrowRule) }, explained.Resolution.AddressCandidates.Single().OverriddenBy);
        }

        // ---------------- 実アセット: 各経路 ----------------

        private const string TempRoot = "Assets/_AddressTellerTestTemp";
        private const string TestRootFolder = TempRoot + "/RuleOverlap";
        private const string RealFolder = TestRootFolder + "/Real";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempRuleOverlapConfig";

        private const string NarrowOnePath = RealFolder + "/NarrowOne.prefab";
        private const string NarrowTwoPath = RealFolder + "/NarrowTwo.prefab";
        private const string PlainPath = RealFolder + "/Plain.prefab";

        /// <summary>NarrowOne だけで Predicate が例外を送出する、最優先（Order 0）のアドレス産出ルール。</summary>
        private sealed class ThrowingFirstRule : AddressRuleBase
        {
            public override int Order => 0;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension == "NarrowOne"
                        ? throw new InvalidOperationException("boom")
                        : false))
                    .Address(ctx => "first/" + ctx.FileNameWithoutExtension);
            }
        }

        // TieARule と TieBRule は同じ Order（同点）。
        private sealed class TieARule : AddressRuleBase
        {
            public override int Order => 3;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension == "NarrowOne"))
                    .Address(ctx => "tieA/" + ctx.FileNameWithoutExtension);
            }
        }

        private sealed class TieBRule : AddressRuleBase
        {
            public override int Order => 3;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension == "NarrowOne"))
                    .Address(ctx => "tieB/" + ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>BroadRule と重ならない、ラベル専用ルール（重なりに数えない）。</summary>
        private sealed class LabelOnlyRule : AddressRuleBase
        {
            public override int Order => 0;

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup().Where(Match.InFolder(RealFolder)).Label("everything");
            }
        }

        /// <summary>最初の Report の後、指定した件数のアセットを処理したところで走査を打ち切る。</summary>
        private sealed class CancelAfterReporter : IProgressReporter
        {
            private readonly int _processed;

            public CancelAfterReporter(int processed) => _processed = processed;

            public bool Report(int current, int total, string description) => current < _processed;
        }

        private AddressableAssetSettings _settings;
        private bool _originalCleanup;
        private bool _originalStrict;

        [SetUp]
        public void SetUp()
        {
            _originalCleanup = AddressTellerSettings.CleanupStaleEntries;
            _originalStrict = AddressTellerSettings.StrictRuleOverlaps;
            AddressTellerSettings.CleanupStaleEntries = false;
            AddressTellerSettings.StrictRuleOverlaps = false;
            DeclaredFolderInspector.ResetCache();

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerRuleOverlapTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            EnsureFolder("Assets", "_AddressTellerTestTemp");
            EnsureFolder(TempRoot, "RuleOverlap");
            EnsureFolder(TestRootFolder, "Real");

            foreach (var path in new[] { NarrowOnePath, NarrowTwoPath, PlainPath })
                CreatePrefab(path);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanup;
            AddressTellerSettings.StrictRuleOverlaps = _originalStrict;
            DeclaredFolderInspector.ResetCache();

            // 他のテストが同じ親を使うため親ごとは消さず、自分のサブフォルダだけを消す。
            // 親が空になっていれば親も消す（テスト後に Assets 直下へ残さない）。
            AssetDatabase.DeleteAsset(TestRootFolder);
            if (AssetDatabase.IsValidFolder(TempRoot)
                && AssetDatabase.FindAssets(string.Empty, new[] { TempRoot }).Length == 0)
                AssetDatabase.DeleteAsset(TempRoot);
            if (_settings != null)
            {
                foreach (var group in _settings.groups.Where(g => g != null).ToList())
                    Object.DestroyImmediate(group, true);
                Object.DestroyImmediate(_settings, true);
            }
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static void CreatePrefab(string path)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static readonly string[] AllPaths = { RealFolder, NarrowOnePath, NarrowTwoPath, PlainPath };

        private static AddressRuleBase[] Rules(params AddressRuleBase[] rules) => rules;

        private static ValidationResult[] OfStatus(IEnumerable<ValidationResult> issues, ValidationStatus status)
            => issues.Where(i => i.Status == status).ToArray();

        [Test]
        public void BuildPredictedSnapshot_ListsTheOverlapWithCountAndExamples_WhateverTheSetting()
        {
            foreach (var strict in new[] { false, true })
            {
                AddressTellerSettings.StrictRuleOverlaps = strict;

                var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new BroadRule(), new NarrowRule()));

                var overlap = result.Overlaps.Single();
                Assert.AreEqual(nameof(BroadRule), overlap.OverriddenRule);
                Assert.AreEqual(nameof(NarrowRule), overlap.OverridingRule);
                Assert.AreEqual(2, overlap.Count, "Plain は広いルールだけにマッチするので、重なりに数えない。");
                CollectionAssert.AreEqual(new[] { NarrowOnePath, NarrowTwoPath }, overlap.ExamplePaths);
                Assert.IsFalse(overlap.IsDeclared);
            }
        }

        [Test]
        public void BuildPredictedSnapshot_StrictOff_ReportsNoIssue()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new BroadRule(), new NarrowRule()));

            Assert.AreEqual(0, OfStatus(result.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
            Assert.AreEqual(1, result.Overlaps.Count);
        }

        [Test]
        public void BuildPredictedSnapshot_DeclaredOverlap_IsListedAsDeclared_AndNotReportedWhenStrict()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new DeclaringBroadRule(), new NarrowRule()));

            Assert.IsTrue(result.Overlaps.Single().IsDeclared);
            Assert.AreEqual(0, OfStatus(result.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void ValidateAll_StrictOn_UndeclaredOverlap_IsABlockingIssueWithoutContext()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules(new BroadRule(), new NarrowRule()));

            var overlap = OfStatus(issues, ValidationStatus.UndeclaredRuleOverlap).Single();
            Assert.IsFalse(overlap.IsOk);
            Assert.IsTrue(overlap.IsBlocking);
            Assert.IsNull(overlap.Context);
            StringAssert.Contains(nameof(BroadRule), overlap.Message);
            StringAssert.Contains(nameof(NarrowRule), overlap.Message);
            StringAssert.Contains("2 assets", overlap.Message);
            StringAssert.Contains(NarrowOnePath, overlap.Message);
            StringAssert.Contains($"OverriddenBy<{nameof(NarrowRule)}>()", overlap.Message);
        }

        [Test]
        public void ValidateAll_StrictOn_DeclaredOverlap_ReportsNothing()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules(new DeclaringBroadRule(), new NarrowRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void ValidateAll_StrictOff_ReportsNothing()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules(new BroadRule(), new NarrowRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void ValidateAll_StrictOn_ReportsOneIssuePerPair_InOrdinalOrder()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            // Broad と Mid の両方が Narrow に負ける。Mid も Broad に勝つが、Narrow が最小なので勝者は Narrow だけ。
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new NarrowRule(), new MidRule(), new BroadRule()));

            var overlaps = OfStatus(issues, ValidationStatus.UndeclaredRuleOverlap);
            Assert.AreEqual(2, overlaps.Length);
            StringAssert.StartsWith($"{nameof(BroadRule)} is overridden by {nameof(NarrowRule)}", overlaps[0].Message);
            StringAssert.StartsWith($"{nameof(MidRule)} is overridden by {nameof(NarrowRule)}", overlaps[1].Message);
        }

        [Test]
        public void Overlap_Tie_IsAConflictNotAnOverlap()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { NarrowOnePath },
                Rules(new TieARule(), new TieBRule(), new BroadRule()));

            Assert.AreEqual(1, OfStatus(result.Issues, ValidationStatus.ConflictingAddress).Length);
            Assert.AreEqual(0, result.Overlaps.Count);
            Assert.AreEqual(0, OfStatus(result.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void Overlap_WinnerBlockedByHigherPriorityRuleError_IsNotCounted()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { NarrowOnePath },
                Rules(new ThrowingFirstRule(), new MidRule(), new BroadRule()));

            Assert.AreEqual(1, OfStatus(result.Issues, ValidationStatus.BlockedByRuleError).Length);
            Assert.AreEqual(0, result.Overlaps.Count, "勝者が確定しない（書き込みも止まる）アセットは、重なりとして数えない。");
            Assert.AreEqual(0, OfStatus(result.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void Overlap_LabelOnlyRule_IsNotCounted()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new BroadRule(), new LabelOnlyRule()));

            Assert.AreEqual(0, result.Overlaps.Count);
            Assert.AreEqual(0, OfStatus(result.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void Overlap_SubsetOfPaths_CountsOnlyTheExaminedAssets()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var withoutOverlap = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { PlainPath }, Rules(new BroadRule(), new NarrowRule()));
            var withOne = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { NarrowTwoPath, PlainPath }, Rules(new BroadRule(), new NarrowRule()));

            Assert.AreEqual(0, withoutOverlap.Overlaps.Count);
            Assert.AreEqual(0, OfStatus(withoutOverlap.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
            Assert.AreEqual(1, withOne.Overlaps.Single().Count);
            StringAssert.Contains("1 asset ", OfStatus(withOne.Issues, ValidationStatus.UndeclaredRuleOverlap).Single().Message);
        }

        [Test]
        public void Overlap_SingleRule_HasNoOverlap()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            // 範囲を絞ったルールのプレビューと同じ構成（ルールが1つだけ）。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new BroadRule()));

            Assert.AreEqual(0, result.Overlaps.Count);
        }

        [Test]
        public void BuildPredictedRunState_CancelledImmediately_HasNoOverlaps()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var state = RuleEvaluationPipeline.BuildPredictedRunState(_settings, AllPaths, Rules(new BroadRule(), new NarrowRule()),
                new CancelAfterReporter(0), isFullScan: true);

            Assert.IsTrue(state.Cancelled);
            Assert.AreEqual(0, state.Overlaps.Count);
            Assert.AreEqual(0, OfStatus(state.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void BuildPredictedRunState_CancelledPartway_ReportsOnlyWhatWasExamined()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            // NarrowOne を処理した後で打ち切る。残り（NarrowTwo）は数えない。
            var state = RuleEvaluationPipeline.BuildPredictedRunState(_settings, new[] { NarrowOnePath, NarrowTwoPath, PlainPath },
                Rules(new BroadRule(), new NarrowRule()), new CancelAfterReporter(1), isFullScan: true);

            Assert.IsTrue(state.Cancelled);
            var overlap = state.Overlaps.Single();
            Assert.AreEqual(1, overlap.Count);
            CollectionAssert.AreEqual(new[] { NarrowOnePath }, overlap.ExamplePaths);
            Assert.AreEqual(1, OfStatus(state.Issues, ValidationStatus.UndeclaredRuleOverlap).Length,
                "打ち切るまでに見つかった重なりは事実として正しいので、報告する。");
        }

        // ApplyAll（Postprocessor が呼ぶ増分適用を含む）は検証の経路ではないので、厳格モードでも報告しない。

        [Test]
        public void ApplyAll_StrictOn_DoesNotReportOverlaps_AndStillWrites()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            var issues = AddressTellerService.ApplyAll(AllPaths, _settings, NullProgressReporter.Instance, Rules(new BroadRule(), new NarrowRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.UndeclaredRuleOverlap).Length);
            var entry = _settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(NarrowOnePath));
            Assert.IsNotNull(entry);
            Assert.AreEqual("narrow/NarrowOne", entry.address, "厳格モードでも、勝者と書き込まれる内容は変わらない。");
        }

        [Test]
        public void ApplyAllIncremental_StrictOn_DoesNotReportOverlaps()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            // AddressTellerPostprocessor が呼ぶのと同じ増分適用。
            var issues = AddressTellerService.ApplyAllIncremental(AllPaths, _settings, Rules(new BroadRule(), new NarrowRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.UndeclaredRuleOverlap).Length);
        }

        [Test]
        public void StrictMode_DoesNotChangeWhatIsWritten()
        {
            var strictOff = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new BroadRule(), new NarrowRule()));
            AddressTellerSettings.StrictRuleOverlaps = true;
            var strictOn = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new BroadRule(), new NarrowRule()));

            CollectionAssert.AreEqual(
                strictOff.After.Entries.OrderBy(e => e.Guid, StringComparer.Ordinal).Select(e => (e.Guid, e.Address)),
                strictOn.After.Entries.OrderBy(e => e.Guid, StringComparer.Ordinal).Select(e => (e.Guid, e.Address)));
        }

        // ---------------- 設定 ----------------

        [Test]
        public void StrictRuleOverlaps_DefaultsToOff_AndUsesTheDocumentedJsonKey()
        {
            var data = new AddressTellerSettingsAsset.Data();

            Assert.IsFalse(data._strictRuleOverlaps);
            StringAssert.Contains("\"_strictRuleOverlaps\":false", JsonUtility.ToJson(data));
        }

        [Test]
        public void StrictRuleOverlaps_IsKeptWhenAnotherSettingIsChanged()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;

            // Mutate は Data を複製してから書き換える。複製が新しいフィールドを落としていると、ここで false に戻る。
            AddressTellerSettings.CleanupStaleEntries = !AddressTellerSettings.CleanupStaleEntries;

            Assert.IsTrue(AddressTellerSettings.StrictRuleOverlaps);
        }

        // ---------------- レポート・CLI ----------------

        private static DryRunResult DryRunWithOverlaps(IReadOnlyList<ValidationResult> issues, params RuleOverlap[] overlaps)
            => new DryRunResult(new SnapshotDiff(), issues, overlaps: overlaps);

        private static RuleOverlap Overlap(bool declared, int count = 2)
            => new RuleOverlap("Broad", "Narrow", count, new[] { "Assets/A.prefab", "Assets/B.prefab" }, declared);

        private static ValidationResult UndeclaredIssue()
            => new ValidationResult(null, ValidationStatus.UndeclaredRuleOverlap, "Broad is overridden by Narrow");

        private static AddressTellerCliArgs ParseArgs(params string[] args)
        {
            Assert.IsTrue(AddressTellerCliArgs.TryParse(args, out var parsed, out var error), error);
            return parsed;
        }

        [Test]
        public void Report_Build_CopiesOverlapsInOrder_AndKeepsSchemaVersionAndExitCode()
        {
            var result = DryRunWithOverlaps(new List<ValidationResult>(), Overlap(declared: false), new RuleOverlap("Broad", "Other", 1, new[] { "Assets/C.prefab" }, true));

            var report = AddressTellerReportBuilder.Build(result);

            Assert.AreEqual(2, report.Overlaps.Count);
            Assert.AreEqual("Broad", report.Overlaps[0].OverriddenRule);
            Assert.AreEqual("Narrow", report.Overlaps[0].OverridingRule);
            Assert.AreEqual(2, report.Overlaps[0].Count);
            Assert.IsFalse(report.Overlaps[0].Declared);
            CollectionAssert.AreEqual(new[] { "Assets/A.prefab", "Assets/B.prefab" }, report.Overlaps[0].ExamplePaths);
            Assert.IsTrue(report.Overlaps[1].Declared);
            Assert.AreEqual(1, report.SchemaVersion);
            Assert.AreEqual(0, report.Summary.ExitCode, "一覧そのものは exit code に影響しない。");

            var roundTripped = AddressTellerReport.FromJson(report.ToJson());
            Assert.AreEqual(2, roundTripped.Overlaps.Count);
            Assert.AreEqual("Narrow", roundTripped.Overlaps[0].OverridingRule);
            CollectionAssert.AreEqual(new[] { "Assets/A.prefab", "Assets/B.prefab" }, roundTripped.Overlaps[0].ExamplePaths);
        }

        [Test]
        public void Report_Build_NoOverlaps_LeavesTheListEmpty()
        {
            var report = AddressTellerReportBuilder.Build(new DryRunResult(new SnapshotDiff(), new List<ValidationResult>()));

            Assert.AreEqual(0, report.Overlaps.Count);
        }

        [Test]
        public void CheckCli_UndeclaredOverlapIssue_ExitsTwo_AndJUnitFailsThatStatus()
        {
            var path = Path.Combine(Path.GetTempPath(), "AddressTellerOverlap_" + Guid.NewGuid().ToString("N") + ".xml");
            var originalExit = AddressTellerMenu.s_exitCli;
            try
            {
                int? exitCode = null;
                AddressTellerMenu.s_exitCli = code => exitCode = code;
                LogAssert.Expect(LogType.Log, new Regex("Check completed"));
                LogAssert.Expect(LogType.Error, new Regex("UndeclaredRuleOverlap: Broad is overridden by Narrow"));

                AddressTellerMenu.ReportCheckResult(
                    DryRunWithOverlaps(new List<ValidationResult> { UndeclaredIssue() }, Overlap(declared: false)),
                    ParseArgs("-addressTellerReport", path), null);

                Assert.AreEqual(2, exitCode);
                var junit = File.ReadAllText(path);
                StringAssert.Contains("<testcase name=\"UndeclaredRuleOverlap\" classname=\"AddressTeller.Validation\">\n    <failure", junit);
            }
            finally
            {
                AddressTellerMenu.s_exitCli = originalExit;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void CheckCli_OverlapsWithoutIssue_DoNotChangeTheExitCode()
        {
            var originalExit = AddressTellerMenu.s_exitCli;
            try
            {
                int? exitCode = null;
                AddressTellerMenu.s_exitCli = code => exitCode = code;
                LogAssert.Expect(LogType.Log, new Regex("Check completed"));

                AddressTellerMenu.ReportCheckResult(DryRunWithOverlaps(new List<ValidationResult>(), Overlap(declared: false)), ParseArgs(), null);

                Assert.AreEqual(0, exitCode, "Issues に UndeclaredRuleOverlap が無ければ、重なりの一覧（Overlaps）だけでは CheckCLI は失敗しない。");
            }
            finally
            {
                AddressTellerMenu.s_exitCli = originalExit;
            }
        }

        [Test]
        public void ApplyAllCli_ExcludesTheUndeclaredOverlapIssue_KeepsTheListAndWarnsOnce()
        {
            var other = new ValidationResult(null, ValidationStatus.RuleMatchedNothing, "nothing");
            var dryRun = DryRunWithOverlaps(new List<ValidationResult> { other, UndeclaredIssue() }, Overlap(declared: false));
            LogAssert.Expect(LogType.Warning, new Regex("Strict rule overlaps is on, but ApplyAllCLI does not check rule overlaps \\(1 undeclared"));

            var stripped = AddressTellerMenu.ExcludeUndeclaredRuleOverlapIssues(dryRun);

            CollectionAssert.AreEqual(new[] { other }, stripped.Issues);
            Assert.AreEqual(1, stripped.Overlaps.Count, "重なりの一覧は残す。");
            Assert.IsEmpty(AddressTellerReportBuilder.BuildApplyFailingStatusNames(stripped.Issues),
                "exit code（ApplyAll の結果だけで決まる）と JUnit の failure が食い違わない。");
        }

        [Test]
        public void ApplyAllCli_DryRun_StrictOn_LeavesOutTheUndeclaredIssue_KeepsTheList_AndNamesThePairInTheWarning()
        {
            AddressTellerSettings.StrictRuleOverlaps = true;
            LogAssert.Expect(LogType.Warning, new Regex("ApplyAllCLI does not check rule overlaps \\(1 undeclared overlap\\(s\\) not enforced \\(e\\.g\\. BroadRule -> NarrowRule\\)"));

            // ApplyAllCLI が実際に使う dry-run の組み立て（配線そのもの）を通す。
            var dryRun = AddressTellerMenu.BuildApplyAllCliDryRun(_settings, AllPaths, Rules(new BroadRule(), new NarrowRule()));

            Assert.AreEqual(0, OfStatus(dryRun.Issues, ValidationStatus.UndeclaredRuleOverlap).Length);
            Assert.AreEqual(1, dryRun.Overlaps.Count, "重なりの一覧は残す。");
        }

        [Test]
        public void ApplyAllCli_NoUndeclaredOverlapIssue_ReturnsTheDryRunUnchangedWithoutWarning()
        {
            var dryRun = DryRunWithOverlaps(new List<ValidationResult>(), Overlap(declared: true));

            var result = AddressTellerMenu.ExcludeUndeclaredRuleOverlapIssues(dryRun);

            Assert.AreSame(dryRun.Issues, result.Issues);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ApplyAllMenu_WithChanges_LogsTheOverlapIssueAndTheNotEnforcedWarning()
        {
            var dryRun = DryRunWithOverlaps(new List<ValidationResult> { UndeclaredIssue() }, Overlap(declared: false));
            LogAssert.Expect(LogType.Error, new Regex("UndeclaredRuleOverlap: Broad is overridden by Narrow"));
            LogAssert.Expect(LogType.Warning, new Regex("Apply All does not validate, so it does not enforce Strict rule overlaps"));

            AddressTellerApplyFlow.ReportDryRunNotices(dryRun, allIssuesLogged: false);
        }

        [Test]
        public void ApplyAllMenu_NoChangesPath_AddsOnlyTheWarning_BecauseAllIssuesAreLoggedAlready()
        {
            var dryRun = DryRunWithOverlaps(new List<ValidationResult> { UndeclaredIssue() }, Overlap(declared: false));
            LogAssert.Expect(LogType.Warning, new Regex("Apply All does not validate, so it does not enforce Strict rule overlaps"));

            // 個別ログは LogAll 側（差分なしの経路）が済ませているので、ここで Error を出すと二重になる。
            AddressTellerApplyFlow.ReportDryRunNotices(dryRun, allIssuesLogged: true);
        }

        [Test]
        public void ApplyAllMenu_NoOverlapIssue_LogsNothing()
        {
            AddressTellerApplyFlow.ReportDryRunNotices(DryRunWithOverlaps(new List<ValidationResult>(), Overlap(declared: false)), allIssuesLogged: false);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HasNoChanges_OnlyAnUndeclaredOverlapIssue_IsTreatedAsNoChanges()
        {
            Assert.IsTrue(AddressTellerApplyFlow.HasNoChanges(DryRunWithOverlaps(new List<ValidationResult> { UndeclaredIssue() })),
                "Apply All は重なりを強制しないので、書き込む物が無いのに確認ダイアログを出さない。");

            var other = new ValidationResult(null, ValidationStatus.RuleConfigureFailed, "x");
            Assert.IsFalse(AddressTellerApplyFlow.HasNoChanges(DryRunWithOverlaps(new List<ValidationResult> { UndeclaredIssue(), other })));
        }

        [Test]
        public void ExitWithReport_KeepsOverlapsInTheReport_WhenAHeldBackRemovalRebuildsTheDryRun()
        {
            var path = Path.Combine(Path.GetTempPath(), "AddressTellerOverlapExit_" + Guid.NewGuid().ToString("N") + ".json");
            var originalExit = AddressTellerMenu.s_exitCli;
            try
            {
                int? exitCode = null;
                AddressTellerMenu.s_exitCli = code => exitCode = code;
                var dryRun = DryRunWithOverlaps(new List<ValidationResult>(), Overlap(declared: true));
                var execution = new List<ValidationResult> { new ValidationResult(null, ValidationStatus.StaleRemovalLimitExceeded, "limit") };

                AddressTellerMenu.ExitWithReport(dryRun, execution, ParseArgs("-addressTellerReport", path), _settings);

                Assert.AreEqual(2, exitCode);
                var report = AddressTellerReport.FromJson(File.ReadAllText(path));
                Assert.AreEqual(1, report.Overlaps.Count, "StaleRemovalLimitExceeded で dry-run を作り直しても、重なりの一覧は落とさない。");
            }
            finally
            {
                AddressTellerMenu.s_exitCli = originalExit;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ResultWindowRows_DescribeOverlap_SaysWhetherItWasDeclared()
        {
            var undeclared = AddressTellerResultWindowRows.DescribeOverlap(Overlap(declared: false));
            var declared = AddressTellerResultWindowRows.DescribeOverlap(Overlap(declared: true));

            StringAssert.Contains("Broad is overridden by Narrow on 2 assets [", undeclared);
            StringAssert.Contains("NOT declared", undeclared);
            StringAssert.Contains("Assets/A.prefab, Assets/B.prefab", undeclared);
            StringAssert.Contains("[declared]", declared);
            StringAssert.DoesNotContain("NOT declared", declared);
        }

        [Test]
        public void ResultWindowRows_DescribeOverlap_UsesTheSingularForOneAsset()
        {
            var text = AddressTellerResultWindowRows.DescribeOverlap(Overlap(declared: false, count: 1));

            StringAssert.Contains("on 1 asset [", text);
        }
    }
}

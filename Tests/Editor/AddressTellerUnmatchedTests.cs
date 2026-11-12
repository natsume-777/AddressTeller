using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 宣言フォルダ配下でどのルールにもマッチしなかったアセット（DryRunResult.UnmatchedInDeclaredFolders）と、
    /// フルスキャンで1件にもマッチしなかったアドレス産出ルールの通知（ValidationStatus.RuleMatchedNothing）を、
    /// 実際の AssetDatabase とメモリ上の AddressableAssetSettings で検証する。
    /// exit code・レポート・CLI 引数は AddressTellerReportBuilderTests / AddressTellerCliArgsTests が担当する。
    /// </summary>
    public class AddressTellerUnmatchedTests
    {
        private const string TempRoot = "Assets/_AddressTellerTestTemp";
        private const string TestRootFolder = TempRoot + "/Unmatched";
        private const string RealFolder = TestRootFolder + "/Real";
        private const string SubFolder = RealFolder + "/Sub";
        private const string OtherFolder = TestRootFolder + "/Other";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempUnmatchedConfig";

        private const string MatchedPath = RealFolder + "/MatchA.prefab";
        private const string LabelledPath = RealFolder + "/Labelled.prefab";
        private const string BoomPath = RealFolder + "/Boom.prefab";
        private const string OrphanPath = SubFolder + "/Orphan.prefab";
        private const string ElsewherePath = OtherFolder + "/Elsewhere.prefab";

        /// <summary>RealFolder を宣言し、名前が "Match" で始まるアセットにアドレスを付けるルール。</summary>
        private sealed class MatchPrefixRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension.StartsWith("Match", StringComparison.Ordinal)))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>RealFolder を宣言し、Labelled だけにラベルを付ける（アドレスは付けない）ルール。</summary>
        private sealed class LabelOnlyRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup()
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension == "Labelled"))
                    .Label("tagged");
            }
        }

        /// <summary>RealFolder を宣言し、Boom という名前のアセットでだけ Predicate が例外を送出するルール。</summary>
        private sealed class ThrowingRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension == "Boom"
                        ? throw new InvalidOperationException("boom")
                        : false))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>RealFolder を宣言するが、どのアセットにもマッチしないルール。</summary>
        private sealed class NeverMatchRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => false, "never"))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>RealFolder のうち SubFolder を Not で除く条件のルール。名前が "Match" で始まるアセットにアドレスを付ける。</summary>
        private sealed class ExcludeSubRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(Match.InFolder(SubFolder).Not())
                        .And(ctx => ctx.FileNameWithoutExtension.StartsWith("Match", StringComparison.Ordinal)))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>ExcludeSubRule の GroupDefault() 版。</summary>
        private sealed class ExcludeSubGroupDefaultRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault()
                    .Where(Match.InFolder(RealFolder).And(Match.InFolder(SubFolder).Not())
                        .And(ctx => ctx.FileNameWithoutExtension.StartsWith("Match", StringComparison.Ordinal)))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>RealFolder のうち SubFolder を Not で除く条件のラベル専用ルール（Real 直下は全部マッチする）。</summary>
        private sealed class ExcludeSubAnyGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup()
                    .Where(Match.InFolder(RealFolder).And(Match.InFolder(SubFolder).Not()))
                    .Label("tagged");
            }
        }

        /// <summary>フォルダを宣言しない生ラムダのルール。何にもマッチしない。</summary>
        private sealed class RawLambdaNeverRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(ctx => false).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>RealFolder を recursive: false で宣言し、名前が "Match" で始まるアセットにアドレスを付けるルール。</summary>
        private sealed class NonRecursiveMatchRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder, recursive: false).And(ctx => ctx.FileNameWithoutExtension.StartsWith("Match", StringComparison.Ordinal)))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>実在しないフォルダを宣言し、何にもマッチしないルール（DeclaredFolderMissing が原因を示す）。</summary>
        private sealed class MissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(TestRootFolder + "/DoesNotExist")).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>同じクラスの2エントリ。1つ目はマッチし、2つ目は何にもマッチしない。</summary>
        private sealed class TwoEntryRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => ctx.FileNameWithoutExtension.StartsWith("Match", StringComparison.Ordinal)), "matches")
                    .Address(ctx => ctx.FileNameWithoutExtension);
                rules.Group("StubGroup")
                    .Where(Match.InFolder(RealFolder).And(ctx => false, "never"))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>最初の Report 呼び出しで false を返し、走査をキャンセルする。</summary>
        private sealed class CancelImmediatelyReporter : IProgressReporter
        {
            public bool Report(int current, int total, string description) => false;
        }

        /// <summary>何にもマッチしないラベル専用ルール（RuleMatchedNothing の対象外）。</summary>
        private sealed class LabelOnlyNeverRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup().Where(ctx => false).Label("never");
            }
        }

        private AddressableAssetSettings _settings;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = false;
            DeclaredFolderInspector.ResetCache();

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerUnmatchedTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            EnsureFolder("Assets", "_AddressTellerTestTemp");
            EnsureFolder(TempRoot, "Unmatched");
            EnsureFolder(TestRootFolder, "Real");
            EnsureFolder(RealFolder, "Sub");
            EnsureFolder(TestRootFolder, "Other");

            foreach (var path in new[] { MatchedPath, LabelledPath, BoomPath, OrphanPath, ElsewherePath })
                CreatePrefab(path);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
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
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static readonly string[] AllPaths =
            { RealFolder, SubFolder, OtherFolder, MatchedPath, LabelledPath, BoomPath, OrphanPath, ElsewherePath };

        private static AddressRuleBase[] Rules(params AddressRuleBase[] rules) => rules;

        private static ValidationResult[] OfStatus(System.Collections.Generic.IEnumerable<ValidationResult> issues, ValidationStatus status)
            => issues.Where(i => i.Status == status).ToArray();

        // --- 宣言フォルダ配下の未登録アセット ---

        [Test]
        public void BuildPredictedSnapshot_ListsOnlyUnmatchedAssetsUnderDeclaredFolders()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths,
                Rules(new MatchPrefixRule(), new LabelOnlyRule(), new ThrowingRule()));

            // MatchA: アドレスルールにマッチ / Labelled: ラベル専用ルールにマッチ / Boom: ルール例外 /
            // Sub と Real と Other: フォルダ / Elsewhere: 宣言フォルダの外。残るのは入れ子の Orphan だけ。
            CollectionAssert.AreEqual(new[] { OrphanPath }, result.UnmatchedInDeclaredFolders);
        }

        [Test]
        public void BuildPredictedSnapshot_FolderExcludedByNot_IsNotListedAsUnmatched()
        {
            // Sub/Orphan はルールが Not で除いたフォルダの中身。このルールが取らないだけなので未登録に載せない。
            // Real 直下で名前が Match で始まらない Boom / Labelled は、範囲内で取られていないので載る。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new ExcludeSubRule()));

            CollectionAssert.AreEqual(new[] { BoomPath, LabelledPath }, result.UnmatchedInDeclaredFolders);
        }

        [Test]
        public void BuildPredictedSnapshot_FolderExcludedByNot_IsListedWhenAnotherRuleCoversIt()
        {
            // 除外は他のルールの範囲を削らない。MatchPrefixRule は Real 配下全体を範囲にするので Orphan は載る。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths,
                Rules(new ExcludeSubRule(), new MatchPrefixRule()));

            CollectionAssert.Contains(result.UnmatchedInDeclaredFolders, OrphanPath);
        }

        [Test]
        public void BuildPredictedSnapshot_GroupDefaultRuleWithNotExclusion_KeepsTheExclusion()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new ExcludeSubGroupDefaultRule()));

            CollectionAssert.DoesNotContain(result.UnmatchedInDeclaredFolders, OrphanPath);
            CollectionAssert.Contains(result.UnmatchedInDeclaredFolders, BoomPath);
        }

        [Test]
        public void BuildPredictedSnapshot_AnyGroupRuleWithNotExclusion_KeepsTheExclusion()
        {
            // ラベル専用ルールは Real 直下を全部取る。Sub/Orphan は除外されるので、一覧は空になる。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new ExcludeSubAnyGroupRule()));

            Assert.AreEqual(0, result.UnmatchedInDeclaredFolders.Count);
        }

        [Test]
        public void BuildPredictedSnapshot_UnmatchedIsSortedOrdinal()
        {
            var zebra = RealFolder + "/Zebra.prefab";
            var apple = RealFolder + "/Apple.prefab";
            CreatePrefab(zebra);
            CreatePrefab(apple);

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { zebra, OrphanPath, apple },
                Rules(new MatchPrefixRule()));

            CollectionAssert.AreEqual(new[] { apple, zebra, OrphanPath }.OrderBy(p => p, StringComparer.Ordinal), result.UnmatchedInDeclaredFolders);
        }

        [Test]
        public void BuildPredictedSnapshot_RuleWithoutDeclaredFolder_ListsNothing()
        {
            // 生ラムダの Where は宣言を持たないので、どのアセットも「宣言フォルダ配下」にならない。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new RawLambdaNeverRule()));

            Assert.AreEqual(0, result.UnmatchedInDeclaredFolders.Count);
        }

        [Test]
        public void BuildPredictedSnapshot_AssetOutsideDeclaredFolders_IsNotListed()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { ElsewherePath }, Rules(new MatchPrefixRule()));

            Assert.AreEqual(0, result.UnmatchedInDeclaredFolders.Count);
        }

        [Test]
        public void BuildPredictedSnapshot_NoRules_ListsNothing()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules());

            Assert.AreEqual(0, result.UnmatchedInDeclaredFolders.Count);
        }

        [Test]
        public void BuildPredictedSnapshot_NonRecursiveDeclaration_ListsOnlyDirectChildren()
        {
            // recursive: false の宣言は RealFolder 直下だけが対象。入れ子の Sub/Orphan は「宣言フォルダ配下」ではない。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths, Rules(new NonRecursiveMatchRule()));

            CollectionAssert.AreEqual(new[] { BoomPath, LabelledPath }, result.UnmatchedInDeclaredFolders);
        }

        [Test]
        public void BuildPredictedSnapshot_SameFolderDeclaredRecursivelyByAnotherRule_RecursiveWins()
        {
            // 別のルールが同じフォルダを再帰で宣言していれば、その配下（Sub/Orphan）も対象になる。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths,
                Rules(new NonRecursiveMatchRule(), new MatchPrefixRule()));

            CollectionAssert.Contains(result.UnmatchedInDeclaredFolders, OrphanPath);
        }

        [Test]
        public void NonRecursiveFolders_AreDeclaredByInFolderAndUnionedByAndAndAll()
        {
            Assert.AreEqual(0, Match.InFolder("Assets/A").NonRecursiveFolders.Count);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, Match.InFolder("Assets/A", recursive: false).NonRecursiveFolders);

            var viaAnd = Match.InFolder("Assets/A", recursive: false).And(Match.InFolder("Assets/B"));
            CollectionAssert.AreEqual(new[] { "Assets/A", "Assets/B" }, viaAnd.Folders);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, viaAnd.NonRecursiveFolders);

            var viaAll = Match.All(Match.InFolder("Assets/B"), Match.InFolder("Assets/A", recursive: false));
            CollectionAssert.AreEqual(new[] { "Assets/A" }, viaAll.NonRecursiveFolders);

            var viaRawLambda = Match.InFolder("Assets/A", recursive: false).And(ctx => true);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, viaRawLambda.NonRecursiveFolders);
        }

        [Test]
        public void ResolveGuids_NonRecursiveGuidDeclaration_ReachesEntryNonRecursiveFolders()
        {
            var builder = new AddressRuleBuilderImpl("Rule");
            builder.Group("G").Where(Match.InFolderByGuid("guid-direct", recursive: false)).Address(ctx => "a");
            builder.Group("G").Where(Match.InFolderByGuid("guid-deep")).Address(ctx => "b");

            var resolved = DeclaredFolderInspector.ResolveGuids(builder.Entries, new System.Collections.Generic.List<ValidationResult>(),
                guid => guid == "guid-direct" ? "Assets/Direct" : "Assets/Deep");

            CollectionAssert.AreEqual(new[] { "Assets/Direct" }, resolved[0].NonRecursiveFolders);
            Assert.AreEqual(0, resolved[1].NonRecursiveFolders.Count);
            CollectionAssert.AreEqual(new[] { "Assets/Deep" }, resolved[1].DeclaredFolders);
        }

        // --- 一致 0 件ルールの通知 ---

        [Test]
        public void ValidateAll_AddressRuleThatMatchesNothing_ReportsRuleMatchedNothing()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new MatchPrefixRule(), new NeverMatchRule()));

            var notices = OfStatus(issues, ValidationStatus.RuleMatchedNothing);
            Assert.AreEqual(1, notices.Length);
            Assert.IsTrue(notices[0].IsOk);
            Assert.IsFalse(notices[0].IsBlocking);
            Assert.IsNull(notices[0].Context);
            StringAssert.Contains(nameof(NeverMatchRule), notices[0].Message);
        }

        [Test]
        public void ValidateAll_MatchingRuleAndLabelOnlyRule_AreNotReported()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new MatchPrefixRule(), new LabelOnlyRule(), new LabelOnlyNeverRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.RuleMatchedNothing).Length,
                "マッチしたルールも、ラベル専用ルール（何にもマッチしないものを含む）も対象外。");
        }

        [Test]
        public void ValidateAll_RuleThatThrew_IsNotReportedAsMatchingNothing()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new MatchPrefixRule(), new ThrowingRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.RuleMatchedNothing).Length,
                "例外を出したルールは評価が完了していないので、「マッチしなかった」とは言えない。");
            Assert.AreEqual(1, OfStatus(issues, ValidationStatus.RuleError).Length);
        }

        [Test]
        public void ValidateAll_RuleWithMissingDeclaredFolder_IsNotReportedAsMatchingNothing()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new MatchPrefixRule(), new MissingFolderRule()));

            Assert.AreEqual(1, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.RuleMatchedNothing).Length,
                "宣言フォルダが無いせいで何にもマッチしないルールは、DeclaredFolderMissing が原因を示すので重ねて報告しない。");
        }

        [Test]
        public void ValidateAll_TwoEntriesOfOneRuleClass_ReportsOnlyTheEntryThatMatchedNothing()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules(new TwoEntryRule()));

            var notices = OfStatus(issues, ValidationStatus.RuleMatchedNothing);
            Assert.AreEqual(1, notices.Length);
            StringAssert.Contains("never", notices[0].Message);
            StringAssert.DoesNotContain("matches", notices[0].Message);
        }

        [Test]
        public void BuildPredictedRunState_CancelledFullScan_DoesNotReportRuleMatchedNothing()
        {
            var state = RuleEvaluationPipeline.BuildPredictedRunState(_settings, AllPaths, Rules(new NeverMatchRule()),
                new CancelImmediatelyReporter(), isFullScan: true);

            Assert.IsTrue(state.Cancelled);
            Assert.AreEqual(0, OfStatus(state.Issues, ValidationStatus.RuleMatchedNothing).Length,
                "途中で打ち切った走査では、「1件もマッチしなかった」と確定できない。");
        }

        [Test]
        public void ValidateAll_RuleWithoutDeclaredFolder_IsStillReportedWhenItMatchesNothing()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new MatchPrefixRule(), new RawLambdaNeverRule()));

            var notices = OfStatus(issues, ValidationStatus.RuleMatchedNothing);
            Assert.AreEqual(1, notices.Length);
            StringAssert.Contains(nameof(RawLambdaNeverRule), notices[0].Message);
        }

        [Test]
        public void BuildPredictedSnapshot_SubsetOfPaths_DoesNotReportRuleMatchedNothing()
        {
            // 一部のパスだけの dry-run は、全体を走査していないので「どこにもマッチしない」とは言えない。
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { ElsewherePath },
                Rules(new MatchPrefixRule(), new NeverMatchRule()));

            Assert.AreEqual(0, OfStatus(result.Issues, ValidationStatus.RuleMatchedNothing).Length);
        }

        [Test]
        public void BuildPredictedSnapshot_FullScan_ReportsRuleMatchedNothing()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, AllPaths,
                Rules(new MatchPrefixRule(), new NeverMatchRule()), isFullScan: true);

            var notices = OfStatus(result.Issues, ValidationStatus.RuleMatchedNothing);
            Assert.AreEqual(1, notices.Length);
            StringAssert.Contains(nameof(NeverMatchRule), notices[0].Message);
        }

        [Test]
        public void ApplyAll_DoesNotReportRuleMatchedNothing()
        {
            var issues = AddressTellerService.ApplyAll(AllPaths, _settings, NullProgressReporter.Instance,
                Rules(new MatchPrefixRule(), new NeverMatchRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.RuleMatchedNothing).Length);
        }
    }
}

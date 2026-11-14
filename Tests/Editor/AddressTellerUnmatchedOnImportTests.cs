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

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 宣言フォルダの配下に置いたのにどのルールにもマッチしなかったアセットを、import 時の増分適用
    /// （<see cref="AddressTellerService.ApplyAllIncremental(System.Collections.Generic.IEnumerable{string}, AddressableAssetSettings, System.Collections.Generic.List{string})"/>）と
    /// Validate メニューで拾うことを検証する。フルスキャンの Unmatched と同じ基準（<see cref="DeclaredFolderScope"/>）を使う。
    /// </summary>
    public class AddressTellerUnmatchedOnImportTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerUnmatchedImportTestTemp";
        private const string DeclaredFolder = TestRootFolder + "/Declared";
        private const string RawFolder = DeclaredFolder + "/Raw";
        private const string SubFolder = DeclaredFolder + "/Sub";
        private const string OutsideFolder = TestRootFolder + "/Outside";
        private const string FakeConfigFolder = "Assets/_AddressTellerUnmatchedImportTestConfig";

        private const string MatchedPath = DeclaredFolder + "/Matched.prefab";
        private const string OrphanPath = DeclaredFolder + "/Orphan.txt";
        private const string RawOrphanPath = RawFolder + "/RawOrphan.txt";
        private const string DeepOrphanPath = SubFolder + "/DeepOrphan.txt";
        private const string OutsideOrphanPath = OutsideFolder + "/Outside.txt";

        /// <summary>Declared 配下の prefab だけを取る。.txt は宣言フォルダの配下にあるのにマッチしない。</summary>
        private sealed class DeclaredPrefabRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(DeclaredFolder).And(Match.WithExtension("prefab")))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>Declared の直下だけを範囲に宣言する。Sub 配下は範囲外。</summary>
        private sealed class DirectChildrenRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(DeclaredFolder, recursive: false).And(Match.WithExtension("prefab")))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>Raw を Not で除外する。除外した中身は範囲に入らない。</summary>
        private sealed class ExcludingRawRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(DeclaredFolder)
                        .And(Match.WithExtension("prefab"))
                        .And(Match.InFolder(RawFolder).Not()))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>説明付きの Where（宣言が保たれること）。</summary>
        private sealed class DescribedRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(DeclaredFolder).And(Match.WithExtension("prefab")), "prefabs in Declared")
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>どの資産でも例外を投げる。例外があった資産は「マッチしなかった」と言い切れない。</summary>
        private sealed class ThrowingRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(ctx => throw new InvalidOperationException("boom")).Address(ctx => "x");
            }
        }

        /// <summary>Declared 配下の .txt にラベルだけを付ける（アドレスは出さない）。</summary>
        private sealed class LabelOnlyRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.AnyGroup().Where(Match.InFolder(DeclaredFolder).And(Match.WithExtension("txt"))).Label("l");
            }
        }

        private static AddressRuleBase[] Rules(params AddressRuleBase[] rules) => rules;

        private AddressableAssetSettings _settings;
        private bool _originalCleanup;
        private Action<IReadOnlyList<ValidationResult>, string, IReadOnlyList<string>> _originalShowResultWindow;

        [SetUp]
        public void SetUp()
        {
            _originalCleanup = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = false;
            _originalShowResultWindow = AddressTellerMenu.s_showResultWindow;
            DeclaredFolderInspector.ResetCache();
            AddressTellerPostprocessor.s_warnedUnmatchedPaths.Clear();

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerUnmatchedImportTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);
            AssetDatabase.CreateFolder("Assets", "_AddressTellerUnmatchedImportTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "Declared");
            AssetDatabase.CreateFolder(DeclaredFolder, "Raw");
            AssetDatabase.CreateFolder(DeclaredFolder, "Sub");
            AssetDatabase.CreateFolder(TestRootFolder, "Outside");

            var go = new GameObject("Matched");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, MatchedPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            foreach (var path in new[] { OrphanPath, RawOrphanPath, DeepOrphanPath, OutsideOrphanPath })
            {
                File.WriteAllText(path, "x");
                AssetDatabase.ImportAsset(path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanup;
            AddressTellerMenu.s_showResultWindow = _originalShowResultWindow;
            DeclaredFolderInspector.ResetCache();
            AddressTellerPostprocessor.s_warnedUnmatchedPaths.Clear();

            AssetDatabase.DeleteAsset(TestRootFolder);

            foreach (var group in _settings.groups.Where(g => g != null).ToList())
                UnityEngine.Object.DestroyImmediate(group, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private List<string> Incremental(AddressRuleBase[] rules, params string[] paths)
        {
            var unmatched = new List<string>();
            AddressTellerService.ApplyAllIncremental(paths, _settings, rules, unmatched);
            return unmatched;
        }

        // --- 増分適用 ---

        [Test]
        public void Incremental_SkippedAssetUnderDeclaredFolder_IsCollected()
        {
            var unmatched = Incremental(Rules(new DeclaredPrefabRule()), OrphanPath);

            CollectionAssert.AreEqual(new[] { OrphanPath }, unmatched);
        }

        [Test]
        public void Incremental_RegisteredAsset_IsNotCollected()
        {
            var unmatched = Incremental(Rules(new DeclaredPrefabRule()), MatchedPath);

            Assert.AreEqual(0, unmatched.Count);
            Assert.IsNotNull(_settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(MatchedPath)), "テストの前提: 登録された。");
        }

        [Test]
        public void Incremental_AssetOutsideDeclaredFolders_IsNotCollected()
        {
            var unmatched = Incremental(Rules(new DeclaredPrefabRule()), OutsideOrphanPath);

            Assert.AreEqual(0, unmatched.Count);
        }

        [Test]
        public void Incremental_OnlyTheSkippedOnesOfAMixedImport_AreCollected()
        {
            var unmatched = Incremental(Rules(new DeclaredPrefabRule()), MatchedPath, OrphanPath, OutsideOrphanPath);

            CollectionAssert.AreEqual(new[] { OrphanPath }, unmatched);
        }

        [Test]
        public void Incremental_NonRecursiveFolder_CountsOnlyDirectChildren()
        {
            var unmatched = Incremental(Rules(new DirectChildrenRule()), OrphanPath, DeepOrphanPath);

            CollectionAssert.AreEqual(new[] { OrphanPath }, unmatched, "直下だけを宣言したフォルダの子孫は範囲外。");
        }

        [Test]
        public void Incremental_FolderExcludedWithNot_IsNotCollected()
        {
            var unmatched = Incremental(Rules(new ExcludingRawRule()), OrphanPath, RawOrphanPath);

            CollectionAssert.AreEqual(new[] { OrphanPath }, unmatched, "Not で除外したフォルダの中身は未登録に数えない。");
        }

        [Test]
        public void Incremental_ConditionWithDescription_KeepsTheDeclarationSoTheAssetIsCollected()
        {
            var unmatched = Incremental(Rules(new DescribedRule()), OrphanPath);

            CollectionAssert.AreEqual(new[] { OrphanPath }, unmatched);
        }

        [Test]
        public void Incremental_AssetForWhichARuleThrew_IsNotCollected()
        {
            // 例外で評価が完了していないので、「どのルールにもマッチしなかった」とは言えない。
            var unmatched = Incremental(Rules(new DeclaredPrefabRule(), new ThrowingRule()), OrphanPath);

            Assert.AreEqual(0, unmatched.Count);
        }

        [Test]
        public void Incremental_AssetMatchedOnlyByALabelOnlyRule_IsNotCollected()
        {
            var unmatched = Incremental(Rules(new DeclaredPrefabRule(), new LabelOnlyRule()), OrphanPath);

            Assert.AreEqual(0, unmatched.Count);
        }

        [Test]
        public void Incremental_FolderAssets_AreNotCollected()
        {
            var unmatched = Incremental(Rules(new DeclaredPrefabRule()), SubFolder);

            Assert.AreEqual(0, unmatched.Count);
        }

        [Test]
        public void Incremental_ReimportOfTheSameAsset_ReportsItAgain()
        {
            // 増分適用の結果としては毎回返る（再 import で黙るのは Postprocessor の TakeNewUnmatched の役割）。
            var rules = Rules(new DeclaredPrefabRule());

            CollectionAssert.AreEqual(new[] { OrphanPath }, Incremental(rules, OrphanPath));
            CollectionAssert.AreEqual(new[] { OrphanPath }, Incremental(rules, OrphanPath));
        }

        [Test]
        public void Incremental_WithoutACollector_BehavesAsBefore()
        {
            var issues = AddressTellerService.ApplyAllIncremental(new[] { MatchedPath, OrphanPath }, _settings, Rules(new DeclaredPrefabRule()));

            Assert.AreEqual(0, issues.Count(i => !i.IsOk));
        }

        [Test]
        public void Incremental_AgreesWithTheFullScanOnWhatIsUnmatched()
        {
            var rules = Rules(new ExcludingRawRule());
            var all = new[] { MatchedPath, OrphanPath, RawOrphanPath, DeepOrphanPath, OutsideOrphanPath };

            var incremental = Incremental(rules, all);
            var full = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, all, rules);

            // 範囲を絞った dry-run でも Unmatched は計算される。同じ基準なら同じ集合になる。
            CollectionAssert.AreEquivalent(full.UnmatchedInDeclaredFolders, incremental);
        }

        [Test]
        public void Incremental_AgreesWithTheFullScan_WhenAnOwnedEntryExistsAndCleanupIsOff()
        {
            // 所有グループに既存エントリがあり cleanup OFF だと、Predict は Skipped ではなく UnmatchedEntryKept を返す。
            // 基準は status ではなく評価結果なので、増分もフルスキャンも同じく未登録として数える。
            var guid = AssetDatabase.AssetPathToGUID(OrphanPath);
            _settings.CreateOrMoveEntry(guid, _settings.FindGroup("StubGroup")).SetAddress("Orphan");
            var rules = Rules(new DeclaredPrefabRule());
            var all = new[] { MatchedPath, OrphanPath };

            var incremental = Incremental(rules, all);
            var predicted = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, all, rules);
            var validateUnmatched = new List<string>();
            AddressTellerService.ValidateAllCollectingUnmatched(_settings, rules, validateUnmatched);

            Assert.IsTrue(predicted.Issues.Any(i => i.Status == ValidationStatus.UnmatchedEntryKept), "テストの前提: Predict は UnmatchedEntryKept を返す。");
            CollectionAssert.AreEqual(new[] { OrphanPath }, incremental);
            CollectionAssert.AreEqual(new[] { OrphanPath }, predicted.UnmatchedInDeclaredFolders);
            CollectionAssert.Contains(validateUnmatched, OrphanPath);
        }

        // --- パス単位の一度だけ（Postprocessor） ---

        [Test]
        public void TakeNewUnmatched_SamePathTheSecondTime_IsNotReturned()
        {
            var first = AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());
            var second = AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());

            CollectionAssert.AreEqual(new[] { OrphanPath }, first);
            Assert.AreEqual(0, second.Count, "再 import では黙る。");
        }

        [Test]
        public void TakeNewUnmatched_OnlyNewlyUnmatchedPathsAreReturned()
        {
            AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());

            var second = AddressTellerPostprocessor.TakeNewUnmatched(
                new[] { OrphanPath, DeepOrphanPath }, new[] { OrphanPath, DeepOrphanPath }, Array.Empty<string>());

            CollectionAssert.AreEqual(new[] { DeepOrphanPath }, second);
        }

        [Test]
        public void TakeNewUnmatched_RegisteredThenUnmatchedAgain_IsReportedAgain()
        {
            AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());

            // 登録された（今回は未登録に含まれない）ので記憶から外れる。
            AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, Array.Empty<string>(), Array.Empty<string>());
            Assert.AreEqual(0, AddressTellerPostprocessor.s_warnedUnmatchedPaths.Count);

            var again = AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());
            CollectionAssert.AreEqual(new[] { OrphanPath }, again);
        }

        [Test]
        public void TakeNewUnmatched_DeletedOrMovedFromPaths_AreForgotten_AndFoldersTakeTheirChildren()
        {
            AddressTellerPostprocessor.TakeNewUnmatched(
                new[] { OrphanPath, DeepOrphanPath, OutsideOrphanPath },
                new[] { OrphanPath, DeepOrphanPath, OutsideOrphanPath }, Array.Empty<string>());

            AddressTellerPostprocessor.TakeNewUnmatched(Array.Empty<string>(), Array.Empty<string>(), new[] { OrphanPath, SubFolder });

            CollectionAssert.AreEquivalent(new[] { OutsideOrphanPath }, AddressTellerPostprocessor.s_warnedUnmatchedPaths,
                "削除・移動元（フォルダなら配下も）は記憶から外れ、集合は増え続けない。");
            var again = AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());
            CollectionAssert.AreEqual(new[] { OrphanPath }, again);
        }

        [Test]
        public void TakeNewUnmatched_PathNotEvaluatedThisTime_StaysRemembered()
        {
            AddressTellerPostprocessor.TakeNewUnmatched(new[] { OrphanPath }, new[] { OrphanPath }, Array.Empty<string>());

            AddressTellerPostprocessor.TakeNewUnmatched(new[] { OutsideOrphanPath }, Array.Empty<string>(), Array.Empty<string>());

            CollectionAssert.AreEquivalent(new[] { OrphanPath }, AddressTellerPostprocessor.s_warnedUnmatchedPaths);
        }

        // --- ログ ---

        [Test]
        public void Message_IsNullWhenNothingIsUnmatched()
        {
            Assert.IsNull(AddressTellerIssueLogger.BuildUnmatchedOnImportMessage(new List<string>()));
            Assert.IsNull(AddressTellerIssueLogger.BuildUnmatchedOnImportMessage(null));
        }

        [Test]
        public void Message_ListsCountAndTheFirstFewPathsInOrdinalOrder()
        {
            var message = AddressTellerIssueLogger.BuildUnmatchedOnImportMessage(
                new List<string> { "Assets/D/e.txt", "Assets/D/b.txt", "Assets/D/a.txt", "Assets/D/d.txt", "Assets/D/c.txt" });

            StringAssert.Contains("5 imported assets", message);
            StringAssert.Contains("(Assets/D/a.txt, Assets/D/b.txt, Assets/D/c.txt, and 2 more)", message);
            StringAssert.DoesNotContain("Assets/D/d.txt", message);
            StringAssert.Contains("Tools/AddressTeller/Show Unmatched Assets", message);
        }

        [Test]
        public void Message_SingleAsset_HasNoTail()
        {
            var message = AddressTellerIssueLogger.BuildUnmatchedOnImportMessage(new List<string> { "Assets/D/a.txt" });

            StringAssert.Contains("1 imported asset ", message);
            StringAssert.Contains("(Assets/D/a.txt)", message);
            StringAssert.DoesNotContain("more", message);
        }

        [Test]
        public void Log_EmitsASingleInfoLine_AndNothingWhenEmpty()
        {
            AddressTellerIssueLogger.LogUnmatchedOnImport(new List<string>());

            LogAssert.Expect(LogType.Log, new Regex(@"^\[AddressTeller\] Unmatched: 2 imported assets .*Assets/D/a\.txt, Assets/D/b\.txt\)"));
            AddressTellerIssueLogger.LogUnmatchedOnImport(new List<string> { "Assets/D/b.txt", "Assets/D/a.txt" });
        }

        // --- Validate メニュー ---

        [Test]
        public void MenuValidate_UnmatchedOnly_LogsACountLineAtInfoAndDoesNotOpenTheResultWindow()
        {
            var shown = false;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) => shown = true;

            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Validate completed: no issues.")));
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Unmatched: 3 assets under folders that rules declare")));

            AddressTellerMenu.Validate(_settings, Rules(new DeclaredPrefabRule()));

            Assert.IsFalse(shown, "未登録だけでは結果ウィンドウを開かない（報告であり、失敗ではない）。");
        }


        // --- Show Unmatched Assets メニュー ---

        [Test]
        public void ShowUnmatchedAssets_PassesTheWholeListToTheResultWindow_WithoutIssues()
        {
            IReadOnlyList<ValidationResult> shownIssues = null;
            IReadOnlyList<string> shownUnmatched = null;
            string shownTitle = null;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) =>
            {
                shownIssues = issues;
                shownTitle = title;
                shownUnmatched = unmatched;
            };

            AddressTellerMenu.ShowUnmatchedAssets(_settings, Rules(new DeclaredPrefabRule()));

            Assert.AreEqual("AddressTeller - Unmatched Assets", shownTitle);
            Assert.AreEqual(0, shownIssues.Count, "Issues タブが先に出ないよう、issues は渡さない。");
            CollectionAssert.AreEqual(new[] { OrphanPath, RawOrphanPath, DeepOrphanPath }, shownUnmatched);
        }

        [Test]
        public void ShowUnmatchedAssets_NothingUnmatched_LogsInfoAndOpensNoWindow()
        {
            foreach (var path in new[] { OrphanPath, RawOrphanPath, DeepOrphanPath })
                AssetDatabase.DeleteAsset(path);

            var shown = false;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) => shown = true;
            LogAssert.Expect(LogType.Log, "[AddressTeller] No unmatched assets.");

            AddressTellerMenu.ShowUnmatchedAssets(_settings, Rules(new DeclaredPrefabRule()));

            Assert.IsFalse(shown);
        }

        [Test]
        public void ShowUnmatchedAssets_WhenAFolderIsMissing_WarnsTheListIsIncompleteInsteadOfSayingZero()
        {
            var shown = false;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) => shown = true;
            LogAssert.Expect(LogType.Warning, new Regex("The list of unmatched assets is incomplete.*Tools/AddressTeller/Validate"));

            AddressTellerMenu.ShowUnmatchedAssets(_settings, Rules(new MissingFolderRule()));

            Assert.IsFalse(shown);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ShowUnmatchedAssets_WhenARuleFailedToConfigure_WarnsTheListIsIncomplete()
        {
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) => { };
            LogAssert.Expect(LogType.Error, new Regex("boom"));
            LogAssert.Expect(LogType.Warning, new Regex("The list of unmatched assets is incomplete"));

            AddressTellerMenu.ShowUnmatchedAssets(_settings, Rules(new ConfigureThrowsRule()));
        }

        private sealed class MissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup")
                    .Where(Match.InFolder(TestRootFolder + "/DoesNotExist").And(Match.WithExtension("prefab")))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private sealed class ConfigureThrowsRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules) => throw new InvalidOperationException("boom");
        }
        [Test]
        public void MenuValidate_WithIssuesAndUnmatched_PassesBothToTheResultWindow()
        {
            IReadOnlyList<ValidationResult> shownIssues = null;
            IReadOnlyList<string> shownUnmatched = null;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) =>
            {
                shownIssues = issues;
                shownUnmatched = unmatched;
            };

            LogAssert.Expect(LogType.Error, new Regex("GroupNotFound"));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Validate completed: 1 issue(s) found.")));
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Unmatched:")));

            AddressTellerMenu.Validate(_settings, Rules(new MissingGroupRule()));

            Assert.AreEqual(1, shownIssues.Count);
            Assert.IsTrue(shownUnmatched.Contains(OrphanPath));
        }

        [Test]
        public void MenuValidate_NothingUnmatched_DoesNotShowTheResultWindowOrLogUnmatched()
        {
            // 宣言フォルダ配下に未登録のアセットが無い状態にする。
            foreach (var path in new[] { OrphanPath, RawOrphanPath, DeepOrphanPath })
                AssetDatabase.DeleteAsset(path);

            var shown = false;
            AddressTellerMenu.s_showResultWindow = (issues, title, unmatched) => shown = true;
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Validate completed: no issues.")));

            AddressTellerMenu.Validate(_settings, Rules(new DeclaredPrefabRule()));

            Assert.IsFalse(shown);
        }

        /// <summary>存在しないグループへ割り当てる（GroupNotFound）。Declared 配下の prefab が対象。</summary>
        private sealed class MissingGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("NoSuchGroup")
                    .Where(Match.InFolder(DeclaredFolder).And(Match.WithExtension("prefab")))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        [Test]
        public void ValidateAll_PublicSignatureIsUnchanged_AndIgnoresUnmatched()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, Rules(new DeclaredPrefabRule()));

            Assert.AreEqual(0, issues.Count, "公開の ValidateAll は従来どおり issues だけを返す（Unmatched は issue ではない）。");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// DeclaredFolderInspector の判定ロジックを、AssetDatabase を使わず delegate で差し替えて検証する
    /// （GetSubFolders の列挙結果を Ordinal で比較する、という設計どおりに動くことの確認）。
    /// </summary>
    public class DeclaredFolderInspectorTests
    {
        /// <summary>テスト用の仮想フォルダツリー。親パス → 直下のサブフォルダのフルパス。</summary>
        private static Func<string, string[]> Tree(params string[] folders)
        {
            var children = new Dictionary<string, List<string>>();
            foreach (var folder in folders)
            {
                var slash = folder.LastIndexOf('/');
                var parent = folder.Substring(0, slash);
                if (!children.TryGetValue(parent, out var list))
                    children[parent] = list = new List<string>();
                list.Add(folder);
            }

            return parent => children.TryGetValue(parent, out var list) ? list.ToArray() : new string[0];
        }

        private static readonly Func<string, bool> NoFolderFallback = _ => false;

        private static AddressRuleEntry Entry(string sourceClass, params string[] declaredFolders)
            => new AddressRuleEntry("G", _ => true, _ => "a", new Func<AssetContext, string>[0],
                sourceClass: sourceClass, declaredFolders: declaredFolders);

        [SetUp]
        public void SetUp() => DeclaredFolderInspector.ResetCache();

        [TearDown]
        public void TearDown() => DeclaredFolderInspector.ResetCache();

        // --- Check ---

        [Test]
        public void Check_ExactPath_Exists()
        {
            var check = DeclaredFolderInspector.Check("Assets/Art/Icons", Tree("Assets/Art", "Assets/Art/Icons"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.Exists, check.State);
            Assert.AreEqual("Assets/Art/Icons", check.ActualPath);
        }

        [Test]
        public void Check_RootOnly_Exists()
        {
            Assert.AreEqual(DeclaredFolderState.Exists, DeclaredFolderInspector.Check("Assets", Tree(), NoFolderFallback).State);
            Assert.AreEqual(DeclaredFolderState.Exists, DeclaredFolderInspector.Check("Packages", Tree(), NoFolderFallback).State);
        }

        [Test]
        public void Check_MissingLeaf_IsMissing()
        {
            var check = DeclaredFolderInspector.Check("Assets/Art/Nope", Tree("Assets/Art"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.Missing, check.State);
            Assert.IsNull(check.ActualPath);
        }

        [Test]
        public void Check_MissingMiddleSegment_IsMissing()
        {
            var check = DeclaredFolderInspector.Check("Assets/Nope/Icons", Tree("Assets/Art", "Assets/Art/Icons"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.Missing, check.State);
        }

        [Test]
        public void Check_UnknownRoot_IsMissing()
        {
            Assert.AreEqual(DeclaredFolderState.Missing, DeclaredFolderInspector.Check("Library/Foo", Tree(), NoFolderFallback).State);
        }

        [Test]
        public void Check_LeafDiffersInCase_IsCaseMismatchWithActualPath()
        {
            var check = DeclaredFolderInspector.Check("Assets/Art/icons", Tree("Assets/Art", "Assets/Art/Icons"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.CaseMismatch, check.State);
            Assert.AreEqual("Assets/Art/Icons", check.ActualPath);
        }

        [Test]
        public void Check_MiddleSegmentDiffersInCase_IsCaseMismatchAndContinuesWalking()
        {
            var check = DeclaredFolderInspector.Check("Assets/art/Icons", Tree("Assets/Art", "Assets/Art/Icons"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.CaseMismatch, check.State);
            Assert.AreEqual("Assets/Art/Icons", check.ActualPath);
        }

        [Test]
        public void Check_RootDiffersInCase_IsCaseMismatch()
        {
            var check = DeclaredFolderInspector.Check("assets/Art", Tree("Assets/Art"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.CaseMismatch, check.State);
            Assert.AreEqual("Assets/Art", check.ActualPath);
        }

        [Test]
        public void Check_BothExactAndDifferentCaseSiblingsExist_PrefersExact()
        {
            // 大小文字を区別するファイルシステム上で名前だけが違う兄弟が並ぶ場合。完全一致があればそれを採る。
            var check = DeclaredFolderInspector.Check("Assets/icons", Tree("Assets/Icons", "Assets/icons"), NoFolderFallback);

            Assert.AreEqual(DeclaredFolderState.Exists, check.State);
            Assert.AreEqual("Assets/icons", check.ActualPath);
        }

        [Test]
        public void Check_WalkFailsButIsValidFolder_TreatedAsExistsWithoutCaseCheck()
        {
            // GetSubFolders が列挙しない種類のフォルダ（パッケージ内など）を Missing と誤判定しないための安全網。
            var check = DeclaredFolderInspector.Check("Packages/com.example.pkg/Runtime", Tree(), p => p == "Packages/com.example.pkg/Runtime");

            Assert.AreEqual(DeclaredFolderState.Exists, check.State);
        }

        // --- Inspect ---

        [Test]
        public void Inspect_AllFoldersExist_ReturnsNothing()
        {
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/Art") }, useCache: false, Tree("Assets/Art"), NoFolderFallback);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Inspect_NoDeclarations_ReturnsNothingWithoutEnumeratingFolders()
        {
            var calls = 0;
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA") }, useCache: false, p => { calls++; return new string[0]; }, NoFolderFallback);

            Assert.AreEqual(0, results.Count);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Inspect_MissingFolder_ReportsBlockingDeclaredFolderMissingWithNullContext()
        {
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/Nope") }, useCache: false, Tree(), NoFolderFallback);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, results[0].Status);
            Assert.IsFalse(results[0].IsOk);
            Assert.IsTrue(results[0].IsBlocking);
            Assert.IsNull(results[0].Context);
            StringAssert.Contains("Assets/Nope", results[0].Message);
            StringAssert.Contains("RuleA", results[0].Message);
        }

        [Test]
        public void Inspect_CaseMismatch_ReportsNonBlockingNoticeWithActualName()
        {
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/art") }, useCache: false, Tree("Assets/Art"), NoFolderFallback);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderCaseMismatch, results[0].Status);
            Assert.IsTrue(results[0].IsOk);
            Assert.IsFalse(results[0].IsBlocking);
            Assert.IsNull(results[0].Context);
            StringAssert.Contains("Assets/Art", results[0].Message);
        }

        [Test]
        public void Inspect_SameFolderDeclaredByTwoRules_ReportedOnceNamingBoth()
        {
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/Nope"), Entry("RuleB", "Assets/Nope") }, useCache: false, Tree(), NoFolderFallback);

            Assert.AreEqual(1, results.Count);
            StringAssert.Contains("RuleA", results[0].Message);
            StringAssert.Contains("RuleB", results[0].Message);
        }

        [Test]
        public void Inspect_ManyDeclaringRules_ListsFirstThreeAndCountsRest()
        {
            var entries = Enumerable.Range(1, 5).Select(i => Entry("Rule" + i, "Assets/Nope")).ToArray();

            var results = DeclaredFolderInspector.Inspect(entries, useCache: false, Tree(), NoFolderFallback);

            StringAssert.Contains("Rule1", results[0].Message);
            StringAssert.Contains("Rule3", results[0].Message);
            StringAssert.DoesNotContain("Rule4", results[0].Message);
            StringAssert.Contains("and 2 more", results[0].Message);
        }

        [Test]
        public void Inspect_OrderFollowsDeclarationOrder()
        {
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/Zeta", "Assets/Alpha") }, useCache: false, Tree(), NoFolderFallback);

            Assert.AreEqual(2, results.Count);
            StringAssert.Contains("Assets/Zeta", results[0].Message);
            StringAssert.Contains("Assets/Alpha", results[1].Message);
        }

        [Test]
        public void Inspect_SharedParentEnumeratedOncePerRun()
        {
            var calls = new List<string>();
            var tree = Tree("Assets/Art", "Assets/Art/A", "Assets/Art/B");

            DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/Art/A", "Assets/Art/B") }, useCache: false,
                p => { calls.Add(p); return tree(p); }, NoFolderFallback);

            Assert.AreEqual(1, calls.Count(p => p == "Assets/Art"));
        }

        // --- キャッシュ ---

        [Test]
        public void Inspect_WithCache_SecondRunDoesNotCheckAgain()
        {
            var calls = 0;
            Func<string, string[]> counting = p => { calls++; return Tree("Assets/Art")(p); };
            var entries = new[] { Entry("RuleA", "Assets/Art") };

            DeclaredFolderInspector.Inspect(entries, useCache: true, counting, NoFolderFallback);
            var afterFirst = calls;
            DeclaredFolderInspector.Inspect(entries, useCache: true, counting, NoFolderFallback);

            Assert.Greater(afterFirst, 0);
            Assert.AreEqual(afterFirst, calls, "キャッシュ済みのフォルダは検査し直さない。");
        }

        [Test]
        public void Inspect_WithCache_StillReportsCachedMissingFolder()
        {
            var entries = new[] { Entry("RuleA", "Assets/Nope") };

            DeclaredFolderInspector.Inspect(entries, useCache: true, Tree(), NoFolderFallback);
            var second = DeclaredFolderInspector.Inspect(entries, useCache: true, Tree(), NoFolderFallback);

            Assert.AreEqual(1, second.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, second[0].Status);
        }

        [Test]
        public void Inspect_WithoutCache_AlwaysChecksAgainAndRefreshesCache()
        {
            var entries = new[] { Entry("RuleA", "Assets/Art") };
            DeclaredFolderInspector.Inspect(entries, useCache: true, Tree(), NoFolderFallback); // Missing をキャッシュ

            var fresh = DeclaredFolderInspector.Inspect(entries, useCache: false, Tree("Assets/Art"), NoFolderFallback);
            var cached = DeclaredFolderInspector.Inspect(entries, useCache: true, Tree(), NoFolderFallback);

            Assert.AreEqual(0, fresh.Count, "キャッシュを使わない呼び出しは常に最新の状態を検査する。");
            Assert.AreEqual(0, cached.Count, "キャッシュなしの検査結果でキャッシュが更新される。");
        }

        [Test]
        public void InvalidateCache_DeclaredFolderItselfChanged_ChecksAgain()
        {
            var entries = new[] { Entry("RuleA", "Assets/Art") };
            DeclaredFolderInspector.Inspect(entries, useCache: true, Tree(), NoFolderFallback); // Missing

            DeclaredFolderInspector.InvalidateCache(new[] { "Assets/Art" });
            var results = DeclaredFolderInspector.Inspect(entries, useCache: true, Tree("Assets/Art"), NoFolderFallback);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void InvalidateCache_AncestorOfDeclaredFolderChanged_ChecksAgain()
        {
            var entries = new[] { Entry("RuleA", "Assets/Art/Icons") };
            DeclaredFolderInspector.Inspect(entries, useCache: true, Tree("Assets/Art", "Assets/Art/Icons"), NoFolderFallback); // Exists

            DeclaredFolderInspector.InvalidateCache(new[] { "Assets/Art" }); // 親ごと移動・削除された
            var results = DeclaredFolderInspector.Inspect(entries, useCache: true, Tree(), NoFolderFallback);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, results[0].Status);
        }

        [Test]
        public void InvalidateCache_ChangedPathIsCaseInsensitive()
        {
            var entries = new[] { Entry("RuleA", "Assets/Art") };
            DeclaredFolderInspector.Inspect(entries, useCache: true, Tree("Assets/Art"), NoFolderFallback); // Exists

            DeclaredFolderInspector.InvalidateCache(new[] { "Assets/ART" }); // 大小文字だけ改名された
            var results = DeclaredFolderInspector.Inspect(entries, useCache: true, Tree("Assets/ART"), NoFolderFallback);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderCaseMismatch, results[0].Status);
        }

        [Test]
        public void InvalidateCache_UnrelatedPathOrFileInsideFolder_KeepsCache()
        {
            var calls = 0;
            Func<string, string[]> counting = p => { calls++; return Tree("Assets/Art")(p); };
            var entries = new[] { Entry("RuleA", "Assets/Art") };
            DeclaredFolderInspector.Inspect(entries, useCache: true, counting, NoFolderFallback);
            var afterFirst = calls;

            DeclaredFolderInspector.InvalidateCache(new[] { "Assets/Other/Foo.prefab", "Assets/Art/Icon.png", "Assets/ArtExtra" });
            DeclaredFolderInspector.Inspect(entries, useCache: true, counting, NoFolderFallback);

            Assert.AreEqual(afterFirst, calls, "宣言フォルダ内のファイルや、名前が前方一致するだけの別フォルダの変更では無効化しない。");
        }

        [Test]
        public void InvalidateCache_EmptyCache_DoesNotEnumerateChangedPaths()
        {
            IEnumerable<string> Throwing()
            {
                throw new InvalidOperationException("列挙してはいけない");
                #pragma warning disable CS0162
                yield break;
                #pragma warning restore CS0162
            }

            Assert.DoesNotThrow(() => DeclaredFolderInspector.InvalidateCache(Throwing()));
        }

        // --- GUID 解決 ---

        private static AddressRuleEntry GuidEntry(string sourceClass, string guid)
        {
            var condition = Match.InFolderByGuid(guid);
            var builder = new AddressRuleBuilderImpl(sourceClass);
            builder.Group("G").Where(condition).Address("a");
            return builder.Entries[0];
        }

        [Test]
        public void ResolveGuids_NoGuidDeclarations_ReturnsSameEntries()
        {
            var entries = new[] { Entry("RuleA", "Assets/A") };
            var failures = new List<ValidationResult>();

            var resolved = DeclaredFolderInspector.ResolveGuids(entries, failures, _ => null);

            Assert.AreSame(entries, resolved);
            Assert.AreEqual(0, failures.Count);
        }

        [Test]
        public void ResolveGuids_ResolvableGuid_AddsResolvedPathToDeclaredFolders()
        {
            var entry = GuidEntry("RuleA", "guid-art");
            var failures = new List<ValidationResult>();

            var resolved = DeclaredFolderInspector.ResolveGuids(
                new[] { entry }, failures, g => g == "guid-art" ? "Assets/Art" : null);

            Assert.AreEqual(0, failures.Count);
            Assert.AreEqual(new[] { "Assets/Art" }, resolved[0].DeclaredFolders.ToArray());
            Assert.AreSame(entry.Predicate, resolved[0].Predicate, "述語は差し替えない（評価のたびに解決器から引く）。");
        }

        [Test]
        public void ResolveGuids_RenamedFolder_ResolvesToCurrentPathOnNextRun()
        {
            var entry = GuidEntry("RuleA", "guid-art");
            var path = "Assets/Art";
            var failures = new List<ValidationResult>();

            var first = DeclaredFolderInspector.ResolveGuids(new[] { entry }, failures, _ => path);
            path = "Assets/Graphics";
            var second = DeclaredFolderInspector.ResolveGuids(new[] { entry }, failures, _ => path);

            Assert.AreEqual(new[] { "Assets/Art" }, first[0].DeclaredFolders.ToArray());
            Assert.AreEqual(new[] { "Assets/Graphics" }, second[0].DeclaredFolders.ToArray());
            Assert.AreEqual(0, failures.Count);
        }

        [Test]
        public void ResolveGuids_ResolvedFolderIsAlsoInspectedForExistence()
        {
            var entry = GuidEntry("RuleA", "guid-art");
            var failures = new List<ValidationResult>();
            var resolved = DeclaredFolderInspector.ResolveGuids(new[] { entry }, failures, _ => "Assets/Art");

            var results = DeclaredFolderInspector.Inspect(resolved, useCache: false, Tree("Assets/Art"), NoFolderFallback);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ResolveGuids_UnknownGuid_ReportsMissing()
        {
            var entry = GuidEntry("RuleA", "guid-gone");
            var failures = new List<ValidationResult>();

            var resolved = DeclaredFolderInspector.ResolveGuids(new[] { entry }, failures, _ => null);

            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, failures[0].Status);
            Assert.IsNull(failures[0].Context);
            StringAssert.Contains("guid-gone", failures[0].Message);
            StringAssert.Contains("RuleA", failures[0].Message);
            Assert.AreEqual(0, resolved[0].DeclaredFolders.Count);
        }

        [Test]
        public void ResolveGuids_SameUnknownGuidInTwoRules_ReportedOnce()
        {
            var failures = new List<ValidationResult>();

            DeclaredFolderInspector.ResolveGuids(
                new[] { GuidEntry("RuleA", "guid-gone"), GuidEntry("RuleB", "guid-gone") }, failures, _ => null);

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("RuleA", failures[0].Message);
            StringAssert.Contains("RuleB", failures[0].Message);
        }

        [Test]
        public void Inspect_MessageDoesNotDependOnCleanupSettingOrRoute()
        {
            var results = DeclaredFolderInspector.Inspect(
                new[] { Entry("RuleA", "Assets/Nope") }, useCache: false, Tree(), NoFolderFallback);

            StringAssert.DoesNotContain("for this run", results[0].Message);
            StringAssert.Contains("Cleanup of unmatched entries is suspended", results[0].Message);
        }
    }
}

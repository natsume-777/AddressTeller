using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 条件の合成（Or / Not / Match.Any / Match.WithExtension）の判定・説明文・入力の扱いと、
    /// 宣言フォルダ（Folders）との合成規則（実在検査の対象と、未登録アセット列挙の範囲）を検証する。
    /// Core の振る舞いだけを見るので AssetDatabase は使わない（フォルダ検査は delegate を差し替える）。
    /// </summary>
    public class ConditionCompositionTests
    {
        private static AssetContext Ctx(string path) => new AssetContext("guid", path, typeof(string));

        private static AssetCondition Named(string name, bool result) => new AssetCondition(_ => result, name);

        // --- Or ---

        [Test]
        public void Or_MatchesWhenEitherOperandMatches()
        {
            var or = Named("a", false).Or(Named("b", true));
            Assert.IsTrue(or.Test(Ctx("Assets/x.png")));
            Assert.IsTrue(Named("a", true).Or(Named("b", false)).Test(Ctx("Assets/x.png")));
            Assert.IsFalse(Named("a", false).Or(Named("b", false)).Test(Ctx("Assets/x.png")));
        }

        [Test]
        public void Or_ShortCircuits_SecondOperandNotEvaluatedAfterFirstMatches()
        {
            var secondCalls = 0;
            var or = Named("a", true).Or(new AssetCondition(_ => { secondCalls++; return true; }, "b"));

            or.Test(Ctx("Assets/x.png"));

            Assert.AreEqual(0, secondCalls);
        }

        [Test]
        public void Or_Description_IsParenthesized()
        {
            Assert.AreEqual("(a OR b)", Named("a", true).Or(Named("b", true)).Description);
        }

        [Test]
        public void Or_OperandWithoutDescription_ProducesNoDescription()
        {
            // 片側を落とした説明は条件を偽って伝えてしまうので、説明ごと持たない。
            var raw = new AssetCondition(_ => true);
            Assert.IsNull(Named("a", true).Or(raw).Description);
            Assert.IsNull(raw.Or(Named("a", true)).Description);
        }

        [Test]
        public void Or_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Named("a", true).Or(null));
        }

        // --- Not ---

        [Test]
        public void Not_InvertsTheResult()
        {
            Assert.IsFalse(Named("a", true).Not().Test(Ctx("Assets/x.png")));
            Assert.IsTrue(Named("a", false).Not().Test(Ctx("Assets/x.png")));
        }

        [Test]
        public void Not_Description_AndConditionWithoutDescription()
        {
            Assert.AreEqual("NOT a", Named("a", true).Not().Description);
            Assert.IsNull(new AssetCondition(_ => true).Not().Description);
        }

        // --- 説明文の入れ子 ---

        [Test]
        public void Description_OrInsideAnd_KeepsItsParentheses()
        {
            Assert.AreEqual("a AND (b OR c)", Named("a", true).And(Named("b", true).Or(Named("c", true))).Description);
        }

        [Test]
        public void Description_AndInsideOr_IsWrappedSoPrecedenceIsExplicit()
        {
            var and = Named("a", true).And(Named("b", true));
            Assert.AreEqual("((a AND b) OR c)", and.Or(Named("c", true)).Description);
            Assert.AreEqual("(c OR (a AND b))", Named("c", true).Or(and).Description);
        }

        [Test]
        public void Description_NotOfAndOrOr_IsParenthesized()
        {
            Assert.AreEqual("NOT (a AND b)", Named("a", true).And(Named("b", true)).Not().Description);
            Assert.AreEqual("NOT (a OR b)", Named("a", true).Or(Named("b", true)).Not().Description);
        }

        [Test]
        public void Description_NotInsideAnd_BindsTightlyToItsOperand()
        {
            Assert.AreEqual("NOT a AND b", Named("a", true).Not().And(Named("b", true)).Description);
            Assert.AreEqual("NOT NOT a", Named("a", true).Not().Not().Description);
        }

        [Test]
        public void Description_AndWithRawOperand_KeepsTheDescribedSideOnlyOnItsOwn()
        {
            // And 単体の説明は従来どおり、説明のある側だけ。
            Assert.AreEqual("a", Named("a", true).And(_ => true).Description);
            Assert.AreEqual("a", new AssetCondition(_ => true).And(Named("a", true)).Description);
        }

        [Test]
        public void Description_AndWithRawOperandInsideOrOrNot_HasNoDescription()
        {
            // 片側を落とした説明を Or / Not で包むと条件を偽って伝える（NOT a は「a でない」と読めてしまう）ので、説明なしにする。
            var andWithRaw = Named("a", true).And(_ => true);

            Assert.IsNull(andWithRaw.Not().Description);
            Assert.IsNull(andWithRaw.Or(Named("c", true)).Description);
            Assert.IsNull(Named("c", true).Or(andWithRaw).Description);
            Assert.IsNull(Match.Any(Named("c", true), andWithRaw, Named("d", true)).Description);
            Assert.IsNull(Match.InFolder("Assets/A").And(_ => true).Not().Description);
        }

        [Test]
        public void Description_PartialnessPropagatesThroughAnd()
        {
            var partial = Named("a", true).And(_ => true);

            Assert.IsNull(partial.And(Named("b", true)).Not().Description);
            Assert.IsNull(Named("b", true).And(partial).Or(Named("c", true)).Description);
            // 両側が説明ありの And は完全なので、従来どおり合成できる。
            Assert.AreEqual("NOT (a AND b)", Named("a", true).And(Named("b", true)).Not().Description);
        }

        // --- Match.Any ---

        [Test]
        public void Any_MatchesWhenAnyOperandMatches_AndDescriptionIsFlat()
        {
            var any = Match.Any(Named("a", false), Named("b", false), Named("c", true));

            Assert.IsTrue(any.Test(Ctx("Assets/x.png")));
            Assert.AreEqual("(a OR b OR c)", any.Description);
            Assert.IsFalse(Match.Any(Named("a", false), Named("b", false)).Test(Ctx("Assets/x.png")));
        }

        [Test]
        public void Any_SingleCondition_ReturnsItAsIs()
        {
            var only = Named("a", true);
            Assert.AreSame(only, Match.Any(only));
        }

        [Test]
        public void Any_NoConditions_Throws()
        {
            Assert.Throws<ArgumentException>(() => Match.Any());
            Assert.Throws<ArgumentException>(() => Match.Any(null));
            Assert.Throws<ArgumentException>(() => Match.Any(new AssetCondition[0]));
        }

        [Test]
        public void Any_NullElement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Match.Any(Named("a", true), null));
            Assert.Throws<ArgumentNullException>(() => Match.Any(null, Named("a", true)));
        }

        [Test]
        public void Any_ChangingTheArrayAfterwards_DoesNotChangeTheCondition()
        {
            var operands = new[] { Named("a", false), Named("b", false) };
            var any = Match.Any(operands);

            operands[1] = Named("z", true);

            Assert.IsFalse(any.Test(Ctx("Assets/x.png")));
        }

        // --- Match.WithExtension ---

        [TestCase("png")]
        [TestCase(".png")]
        [TestCase(".PNG")]
        [TestCase(" .Png ")]
        public void WithExtension_AcceptsLeadingDotLetterCaseAndWhiteSpace(string spelling)
        {
            var condition = Match.WithExtension(spelling);

            Assert.IsTrue(condition.Test(Ctx("Assets/a.png")));
            Assert.IsTrue(condition.Test(Ctx("Assets/a.PNG")));
            Assert.IsFalse(condition.Test(Ctx("Assets/a.jpg")));
            Assert.AreEqual("WithExtension(.png)", condition.Description);
        }

        [Test]
        public void WithExtension_MultipleExtensions_MatchAnyAndDeduplicate()
        {
            var condition = Match.WithExtension("png", ".JPG", ".png");

            Assert.IsTrue(condition.Test(Ctx("Assets/a.png")));
            Assert.IsTrue(condition.Test(Ctx("Assets/a.jpg")));
            Assert.IsFalse(condition.Test(Ctx("Assets/a.tga")));
            Assert.AreEqual("WithExtension(.png, .jpg)", condition.Description);
        }

        [Test]
        public void WithExtension_FileWithoutExtensionAndFolder_DoNotMatch()
        {
            var condition = Match.WithExtension("png");

            Assert.IsFalse(condition.Test(Ctx("Assets/README")));
            Assert.IsFalse(condition.Test(new AssetContext("g", "Assets/Icons", typeof(string), isFolder: true)));
        }

        [Test]
        public void WithExtension_FolderWhoseNameHasAnExtension_DoesNotMatch()
        {
            // "Foo.bundle" のようなフォルダ名の ".bundle" は拡張子ではない。ファイルなら同じパスでもマッチする。
            var condition = Match.WithExtension("bundle");

            Assert.IsFalse(condition.Test(new AssetContext("g", "Assets/Plugins/Foo.bundle", typeof(string), isFolder: true)));
            Assert.IsTrue(condition.Test(new AssetContext("g", "Assets/Plugins/Foo.bundle", typeof(string), isFolder: false)));
        }

        [Test]
        public void WithExtension_OnlyTheLastExtensionCounts()
        {
            Assert.IsTrue(Match.WithExtension("gz").Test(Ctx("Assets/a.tar.gz")));
            Assert.IsFalse(Match.WithExtension("tar").Test(Ctx("Assets/a.tar.gz")));
        }

        [Test]
        public void WithExtension_DeclaresNoFolders()
        {
            Assert.AreEqual(0, Match.WithExtension("png").Folders.Count);
        }

        [Test]
        public void WithExtension_NoExtensions_Throws()
        {
            Assert.Throws<ArgumentException>(() => Match.WithExtension());
            Assert.Throws<ArgumentException>(() => Match.WithExtension(null));
            Assert.Throws<ArgumentException>(() => Match.WithExtension(new string[0]));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(".")]
        [TestCase(".tar.gz")]
        [TestCase("tar.gz")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        public void WithExtension_InvalidElement_Throws(string bad)
        {
            Assert.Throws<ArgumentException>(() => Match.WithExtension("png", bad));
        }

        // --- Folders との合成: 宣言の引き継ぎ ---

        [Test]
        public void Or_FoldersAreTheUnion()
        {
            var or = Match.InFolder("Assets/A").Or(Match.InFolder("Assets/B"));

            CollectionAssert.AreEqual(new[] { "Assets/A", "Assets/B" }, or.Folders);
            Assert.AreEqual(0, or.NegatedFolders.Count);
        }

        [Test]
        public void Any_FoldersAreTheUnion_ForInFolderAndGlobPrefix()
        {
            var any = Match.Any(Match.InFolder("Assets/A"), Match.Glob("Assets/B/*.png"), Match.OfType<string>());

            CollectionAssert.AreEqual(new[] { "Assets/A", "Assets/B" }, any.Folders);
        }

        [Test]
        public void Or_NonRecursive_OnlyWhenEveryDeclaringOperandIsNonRecursive()
        {
            var bothDirect = Match.InFolder("Assets/A", recursive: false).Or(Match.InFolder("Assets/A", recursive: false));
            CollectionAssert.AreEqual(new[] { "Assets/A" }, bothDirect.NonRecursiveFolders);

            // 片方が再帰なら、そちらが範囲になる（Or なのでどちらかがマッチしうる）。
            var mixed = Match.InFolder("Assets/A", recursive: false).Or(Match.InFolder("Assets/A"));
            Assert.AreEqual(0, mixed.NonRecursiveFolders.Count);

            // 別のフォルダを宣言しているだけの片方は、A の再帰・非再帰に影響しない。
            var other = Match.InFolder("Assets/A", recursive: false).Or(Match.InFolder("Assets/B"));
            CollectionAssert.AreEqual(new[] { "Assets/A" }, other.NonRecursiveFolders);
        }

        [Test]
        public void And_NonRecursive_StillTheUnion()
        {
            var and = Match.InFolder("Assets/A", recursive: false).And(Match.InFolder("Assets/A"));

            CollectionAssert.AreEqual(new[] { "Assets/A" }, and.NonRecursiveFolders);
        }

        [Test]
        public void Not_KeepsFolders_ButMovesThemToNegated()
        {
            var not = Match.InFolder("Assets/A").Not();

            CollectionAssert.AreEqual(new[] { "Assets/A" }, not.Folders);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, not.NegatedFolders);
        }

        [Test]
        public void Not_Twice_RestoresTheScope()
        {
            var twice = Match.InFolder("Assets/A", recursive: false).Not().Not();

            CollectionAssert.AreEqual(new[] { "Assets/A" }, twice.Folders);
            Assert.AreEqual(0, twice.NegatedFolders.Count);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, twice.NonRecursiveFolders);
        }

        [Test]
        public void Not_OfGlobPrefix_IsNegated()
        {
            var not = Match.Glob("Assets/Art/*.png").Not();

            CollectionAssert.AreEqual(new[] { "Assets/Art" }, not.Folders);
            CollectionAssert.AreEqual(new[] { "Assets/Art" }, not.NegatedFolders);
        }

        [Test]
        public void Not_OfOr_NegatesEveryFolder()
        {
            var not = Match.InFolder("Assets/A").Or(Match.InFolder("Assets/B")).Not();

            CollectionAssert.AreEqual(new[] { "Assets/A", "Assets/B" }, not.NegatedFolders);
        }

        [Test]
        public void And_PositiveAndNegatedFolders_OnlyTheNegatedOnlyStayNegated()
        {
            var combined = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub").Not());

            CollectionAssert.AreEqual(new[] { "Assets/A", "Assets/A/Sub" }, combined.Folders);
            CollectionAssert.AreEqual(new[] { "Assets/A/Sub" }, combined.NegatedFolders);
        }

        [Test]
        public void And_SameFolderPositiveAndNegated_PositiveWins()
        {
            var combined = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A").Not());

            Assert.AreEqual(0, combined.NegatedFolders.Count);
        }

        [Test]
        public void Or_NegatedOperandDoesNotCountAsNonRecursiveScope()
        {
            // A の直下だけを除外する Not と、A を再帰で宣言する条件。A は範囲（再帰）として残る。
            var combined = Match.InFolder("Assets/A", recursive: false).Not().Or(Match.InFolder("Assets/A"));

            Assert.AreEqual(0, combined.NegatedFolders.Count);
            Assert.AreEqual(0, combined.NonRecursiveFolders.Count);
        }

        [Test]
        public void RawLambdaOperands_ContributeNothing()
        {
            var combined = Match.InFolder("Assets/A").Or(new AssetCondition(_ => true)).Not();

            CollectionAssert.AreEqual(new[] { "Assets/A" }, combined.Folders);
            Assert.AreEqual(0, new AssetCondition(_ => true).Not().Folders.Count);
        }

        // --- Folders との合成: 実在検査（Folders 全体が対象） ---

        private static AddressRuleEntry EntryOf(AssetCondition condition, string rule = "RuleA")
        {
            var builder = new AddressRuleBuilderImpl(rule);
            builder.Group("G").Where(condition).Address(_ => "a");
            return builder.Entries[0];
        }

        [Test]
        public void Inspect_MissingFolderInsideNot_IsStillReported()
        {
            DeclaredFolderInspector.ResetCache();
            try
            {
                var entry = EntryOf(Match.InFolder("Assets/Gone").Not());

                var results = DeclaredFolderInspector.Inspect(new[] { entry }, useCache: false, _ => new string[0], _ => false);

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, results[0].Status);
                StringAssert.Contains("Assets/Gone", results[0].Message);
            }
            finally
            {
                DeclaredFolderInspector.ResetCache();
            }
        }

        [Test]
        public void Inspect_MissingFolderInsideOr_IsReportedOncePerFolder()
        {
            DeclaredFolderInspector.ResetCache();
            try
            {
                var entry = EntryOf(Match.InFolder("Assets/GoneA").Or(Match.InFolder("Assets/GoneB")));

                var results = DeclaredFolderInspector.Inspect(new[] { entry }, useCache: false, _ => new string[0], _ => false);

                Assert.AreEqual(2, results.Count);
                Assert.IsTrue(results.All(r => r.Status == ValidationStatus.DeclaredFolderMissing));
            }
            finally
            {
                DeclaredFolderInspector.ResetCache();
            }
        }

        // --- Folders との合成: 未登録アセット列挙の範囲（DeclaredFolderScope） ---

        private static bool InScope(string path, params AssetCondition[] conditions)
            => new DeclaredFolderScope(conditions.Select(c => EntryOf(c)).ToArray()).Contains(path);

        [Test]
        public void Scope_NotDoesNotInheritTheDeclaration()
        {
            // 除外したフォルダの中身を「未登録」に並べない。
            Assert.IsFalse(InScope("Assets/A/x.png", Match.InFolder("Assets/A").Not()));
        }

        [Test]
        public void Scope_Or_CoversBothFolders()
        {
            var or = Match.InFolder("Assets/A").Or(Match.InFolder("Assets/B"));

            Assert.IsTrue(InScope("Assets/A/x.png", or));
            Assert.IsTrue(InScope("Assets/B/deep/y.png", or));
            Assert.IsFalse(InScope("Assets/C/z.png", or));
        }

        [Test]
        public void Scope_AndWithDirectNotOfInFolder_ExcludesThatFolderFromTheScope()
        {
            var condition = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub").Not());

            Assert.IsTrue(InScope("Assets/A/x.png", condition));
            Assert.IsTrue(InScope("Assets/A/Other/y.png", condition));
            Assert.IsFalse(InScope("Assets/A/Sub/x.png", condition));
            Assert.IsFalse(InScope("Assets/A/Sub/Deep/x.png", condition));
            Assert.IsFalse(InScope("Assets/B/x.png", condition));
        }

        [Test]
        public void Scope_Exclusion_IgnoresLetterCaseAndDoesNotMatchSiblingWithSamePrefix()
        {
            var condition = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub").Not());

            Assert.IsFalse(InScope("Assets/a/sub/x.png", condition));
            // "SubX" は "Sub" の配下ではない。
            Assert.IsTrue(InScope("Assets/A/SubX/x.png", condition));
        }

        [Test]
        public void Scope_Exclusion_OfNonRecursiveInFolder_ExcludesOnlyDirectChildren()
        {
            var condition = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub", recursive: false).Not());

            Assert.IsFalse(InScope("Assets/A/Sub/x.png", condition));
            Assert.IsTrue(InScope("Assets/A/Sub/Deep/x.png", condition));
        }

        [Test]
        public void Scope_Exclusion_OfGlob_ExcludesOnlyWhatTheGlobMatches()
        {
            // Glob の固定プレフィックス（Assets/A）全体ではなく、パターンに当たるパスだけが除外。
            var condition = Match.InFolder("Assets/A").And(Match.Glob("Assets/A/*.png").Not());

            Assert.IsFalse(InScope("Assets/A/x.png", condition));
            Assert.IsTrue(InScope("Assets/A/x.prefab", condition));
        }

        [Test]
        public void Scope_Exclusion_OfInFolderByGuid_UsesTheResolvedFolder()
        {
            var previous = FolderGuidResolver.Resolver;
            FolderGuidResolver.Resolver = guid => guid == "g1" ? "Assets/A/Sub" : null;
            try
            {
                var condition = Match.InFolder("Assets/A").And(Match.InFolderByGuid("g1").Not());

                Assert.IsFalse(InScope("Assets/A/Sub/x.png", condition));
                Assert.IsTrue(InScope("Assets/A/x.png", condition));
            }
            finally
            {
                FolderGuidResolver.Resolver = previous;
            }
        }

        [Test]
        public void Scope_Exclusion_AccumulatesAcrossAnd()
        {
            var condition = Match.InFolder("Assets/A")
                .And(Match.InFolder("Assets/A/S1").Not())
                .And(Match.InFolder("Assets/A/S2").Not());

            Assert.IsFalse(InScope("Assets/A/S1/x.png", condition));
            Assert.IsFalse(InScope("Assets/A/S2/x.png", condition));
            Assert.IsTrue(InScope("Assets/A/S3/x.png", condition));
        }

        [Test]
        public void Scope_Exclusion_AppliesToTheWholeAndEvenWhenWrittenFirst()
        {
            var condition = Match.InFolder("Assets/A/Sub").Not().And(Match.InFolder("Assets/A"));

            Assert.IsFalse(InScope("Assets/A/Sub/x.png", condition));
            Assert.IsTrue(InScope("Assets/A/x.png", condition));
        }

        [Test]
        public void Scope_Exclusion_DoesNotCutAnotherEntrysScope()
        {
            var excluding = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub").Not());
            var covering = Match.InFolder("Assets/A/Sub");

            Assert.IsTrue(InScope("Assets/A/Sub/x.png", excluding, covering));
        }

        [Test]
        public void Scope_Exclusion_IsDroppedByOr_SoTheScopeIsNeverNarrowerThanTheRule()
        {
            // Or では除外が片方のオペランドだけのものなので引き継がない（列挙が増える側に倒す）。
            var or = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub").Not()).Or(Match.InFolder("Assets/B"));

            Assert.IsTrue(InScope("Assets/A/Sub/x.png", or));
            Assert.IsTrue(InScope("Assets/B/x.png", or));
        }

        [Test]
        public void Scope_Not_OfACompoundCondition_RecordsNoExclusion()
        {
            // 直接の否定ではない Not（Or / And の否定）は範囲を削らない。範囲に加わりもしない。
            var notOr = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Sub").Or(Match.InFolder("Assets/A/Other")).Not());

            Assert.IsTrue(InScope("Assets/A/Sub/x.png", notOr));
            Assert.IsTrue(InScope("Assets/A/x.png", notOr));
        }

        [Test]
        public void Scope_ExclusionOnlyEntry_ContributesNothing()
        {
            Assert.IsFalse(InScope("Assets/A/x.png", Match.InFolder("Assets/A/Sub").Not().And(Match.OfType<string>())));
        }

        [Test]
        public void Scope_NotOfNot_IsTheScopeAgain()
        {
            Assert.IsTrue(InScope("Assets/A/x.png", Match.InFolder("Assets/A").Not().Not()));
        }

        [Test]
        public void Scope_NotOfOr_ContributesNothing()
        {
            var not = Match.InFolder("Assets/A").Or(Match.InFolder("Assets/B")).Not();

            Assert.IsFalse(InScope("Assets/A/x.png", not));
            Assert.IsFalse(InScope("Assets/B/x.png", not));
        }

        [Test]
        public void Scope_OrOfDirectOnlyFolders_ListsOnlyDirectChildren()
        {
            var or = Match.InFolder("Assets/A", recursive: false).Or(Match.InFolder("Assets/B", recursive: false));

            Assert.IsTrue(InScope("Assets/A/x.png", or));
            Assert.IsFalse(InScope("Assets/A/Sub/x.png", or));
        }

        [Test]
        public void Scope_OrOfDirectAndRecursiveSameFolder_RecursiveWins()
        {
            var or = Match.InFolder("Assets/A", recursive: false).Or(Match.InFolder("Assets/A"));

            Assert.IsTrue(InScope("Assets/A/Sub/x.png", or));
        }

        [Test]
        public void Scope_NegatedFolderInOneRule_DoesNotRemoveAnotherRulesScope()
        {
            Assert.IsTrue(InScope("Assets/A/x.png", Match.InFolder("Assets/A").Not(), Match.InFolder("Assets/A")));
        }

        // --- GUID 宣言との合成 ---

        private static IReadOnlyList<AddressRuleEntry> Resolve(AddressRuleEntry entry, Func<string, string> resolve, List<ValidationResult> failures = null)
            => DeclaredFolderInspector.ResolveGuids(new[] { entry }, failures ?? new List<ValidationResult>(), resolve);

        [Test]
        public void ResolveGuids_InsideNot_IsDeclaredButNegated()
        {
            var entry = EntryOf(Match.InFolderByGuid("g1").Not());

            var resolved = Resolve(entry, _ => "Assets/Excluded")[0];

            CollectionAssert.AreEqual(new[] { "Assets/Excluded" }, resolved.DeclaredFolders);
            CollectionAssert.AreEqual(new[] { "Assets/Excluded" }, resolved.NegatedFolders);
            Assert.IsFalse(new DeclaredFolderScope(new[] { resolved }).Contains("Assets/Excluded/x.png"));
        }

        [Test]
        public void ResolveGuids_InsideOr_JoinsTheScope()
        {
            var entry = EntryOf(Match.InFolderByGuid("g1").Or(Match.InFolderByGuid("g2")));

            var resolved = Resolve(entry, guid => guid == "g1" ? "Assets/One" : "Assets/Two")[0];

            CollectionAssert.AreEqual(new[] { "Assets/One", "Assets/Two" }, resolved.DeclaredFolders);
            Assert.AreEqual(0, resolved.NegatedFolders.Count);
        }

        [Test]
        public void ResolveGuids_NegatedGuidResolvingToAPositiveFolder_StaysInScope()
        {
            var entry = EntryOf(Match.InFolder("Assets/A").And(Match.InFolderByGuid("g1").Not()));

            var resolved = Resolve(entry, _ => "Assets/A")[0];

            Assert.AreEqual(0, resolved.NegatedFolders.Count);
            Assert.IsTrue(new DeclaredFolderScope(new[] { resolved }).Contains("Assets/A/x.png"));
        }

        [Test]
        public void ResolveGuids_UnresolvableGuidInsideNot_IsStillReportedMissing()
        {
            var entry = EntryOf(Match.InFolderByGuid("gone").Not());
            var failures = new List<ValidationResult>();

            Resolve(entry, _ => null, failures);

            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, failures[0].Status);
        }

        [Test]
        public void ResolveGuids_NegatedStateSurvivesNotTwice()
        {
            var entry = EntryOf(Match.InFolderByGuid("g1").Not().Not());

            var resolved = Resolve(entry, _ => "Assets/Scope")[0];

            Assert.AreEqual(0, resolved.NegatedFolders.Count);
            Assert.IsTrue(new DeclaredFolderScope(new[] { resolved }).Contains("Assets/Scope/x.png"));
        }
    }
}

using System;
using System.Linq;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// <c>Where(AssetCondition, string)</c>。<c>Where(Match.InFolder(...), "text")</c> が暗黙変換で
    /// <c>Where(Func, string)</c> に解決されて宣言（フォルダ・除外）が黙って落ちることがないこと、
    /// 説明は引数で上書きされること、Where は1回限りであることを両ビルダーで検証する。
    /// </summary>
    public class WhereConditionDescriptionTests
    {
        private static AddressRuleEntry GroupEntry(Func<IAddressRuleGroupBuilder, IAddressRuleGroupBuilder> configure)
        {
            var builder = new AddressRuleBuilderImpl("RuleA");
            configure(builder.Group("G")).Address("a");
            return builder.Entries[0];
        }

        private static AddressRuleEntry AnyGroupEntry(Func<ILabelRuleBuilder, ILabelRuleBuilder> configure)
        {
            var builder = new AddressRuleBuilderImpl("RuleA");
            configure(builder.AnyGroup()).Label("l");
            return builder.Entries[0];
        }

        [Test]
        public void Group_ConditionWithDescription_KeepsFoldersAndUsesTheGivenDescription()
        {
            var entry = GroupEntry(g => g.Where(Match.InFolder("Assets/UI"), "UI assets"));

            CollectionAssert.AreEqual(new[] { "Assets/UI" }, entry.DeclaredFolders);
            Assert.AreEqual("UI assets", entry.Description);
            Assert.IsTrue(entry.Predicate(new AssetContext("g", "Assets/UI/a.png", typeof(UnityEngine.Texture2D))));
            Assert.IsFalse(entry.Predicate(new AssetContext("g", "Assets/Other/a.png", typeof(UnityEngine.Texture2D))));
        }

        [Test]
        public void AnyGroup_ConditionWithDescription_KeepsFoldersAndUsesTheGivenDescription()
        {
            var entry = AnyGroupEntry(g => g.Where(Match.InFolder("Assets/UI"), "UI assets"));

            CollectionAssert.AreEqual(new[] { "Assets/UI" }, entry.DeclaredFolders);
            Assert.AreEqual("UI assets", entry.Description);
        }

        [Test]
        public void Group_ConditionWithDescription_KeepsEveryKindOfDeclaration()
        {
            var condition = Match.InFolder("Assets/A", recursive: false)
                .And(Match.InFolder("Assets/A/Raw").Not())
                .And(Match.InFolderByGuid("guid-1"));

            var withDescription = GroupEntry(g => g.Where(condition, "text"));
            var withoutDescription = GroupEntry(g => g.Where(condition));

            CollectionAssert.AreEqual(withoutDescription.DeclaredFolders, withDescription.DeclaredFolders);
            CollectionAssert.AreEqual(withoutDescription.NonRecursiveFolders, withDescription.NonRecursiveFolders);
            CollectionAssert.AreEqual(withoutDescription.NegatedFolders, withDescription.NegatedFolders);
            Assert.AreEqual(withoutDescription.ScopeExclusions.Count, withDescription.ScopeExclusions.Count);
            Assert.AreEqual(1, withDescription.ScopeExclusions.Count, "テストの前提: 除外が宣言されている。");
            Assert.AreEqual(withoutDescription.FolderGuids.Count, withDescription.FolderGuids.Count);
            Assert.AreEqual(1, withDescription.FolderGuids.Count, "テストの前提: GUID 宣言がある。");
            CollectionAssert.AreEqual(new[] { "Assets/A" }, withDescription.NonRecursiveFolders);
        }

        [Test]
        public void AnyGroup_ConditionWithDescription_KeepsEveryKindOfDeclaration()
        {
            var condition = Match.InFolder("Assets/A", recursive: false).And(Match.InFolder("Assets/A/Raw").Not());

            var entry = AnyGroupEntry(g => g.Where(condition, "text"));

            CollectionAssert.AreEqual(new[] { "Assets/A" }, entry.NonRecursiveFolders);
            CollectionAssert.AreEqual(new[] { "Assets/A/Raw" }, entry.NegatedFolders);
            Assert.AreEqual(1, entry.ScopeExclusions.Count);
        }

        [Test]
        public void ConditionWithDescription_OverridesTheConditionsOwnDescription()
        {
            var condition = Match.InFolder("Assets/UI");
            Assert.AreEqual("InFolder(Assets/UI)", condition.Description, "テストの前提: 条件は自分の説明を持つ。");

            Assert.AreEqual("mine", GroupEntry(g => g.Where(condition, "mine")).Description);
            Assert.AreEqual("InFolder(Assets/UI)", GroupEntry(g => g.Where(condition)).Description, "説明なしの Where は従来どおり条件の説明を使う。");
            Assert.AreEqual("mine", AnyGroupEntry(g => g.Where(condition, "mine")).Description);
        }

        [Test]
        public void ConditionWithNullDescription_MeansNoDescription()
        {
            Assert.IsNull(GroupEntry(g => g.Where(Match.InFolder("Assets/UI"), null)).Description);
        }

        [Test]
        public void ConditionWithDescription_DoesNotChangeTheConditionItself()
        {
            var condition = Match.InFolder("Assets/UI").And(Match.WithExtension("png"));
            var before = condition.Description;

            GroupEntry(g => g.Where(condition, "mine"));

            Assert.AreEqual(before, condition.Description);
            Assert.AreEqual($"(({before}) OR InFolder(Assets/B))", condition.Or(Match.InFolder("Assets/B")).Description,
                "説明は規則ごとの表示用で、条件の合成には影響しない。");
        }

        [Test]
        public void Group_ConditionWithDescription_WhereIsOnceOnly()
        {
            var builder = new AddressRuleBuilderImpl("RuleA");
            var group = builder.Group("G").Where(Match.InFolder("Assets/UI"), "a");

            Assert.Throws<InvalidOperationException>(() => group.Where(Match.InFolder("Assets/UI"), "b"));
            Assert.Throws<InvalidOperationException>(() => group.Where(Match.InFolder("Assets/UI")));
            Assert.Throws<InvalidOperationException>(() => group.Where(_ => true));
            Assert.Throws<InvalidOperationException>(() => group.Where(_ => true, "c"));
        }

        [Test]
        public void Group_WhereAfterAnyOtherWhere_ConditionWithDescriptionThrows()
        {
            var builder = new AddressRuleBuilderImpl("RuleA");

            Assert.Throws<InvalidOperationException>(() => builder.Group("G1").Where(_ => true).Where(Match.InFolder("Assets/UI"), "x"));
            Assert.Throws<InvalidOperationException>(() => builder.Group("G2").Where(_ => true, "d").Where(Match.InFolder("Assets/UI"), "x"));
            Assert.Throws<InvalidOperationException>(() => builder.Group("G3").Where(Match.InFolder("Assets/UI")).Where(Match.InFolder("Assets/UI"), "x"));
        }

        [Test]
        public void AnyGroup_ConditionWithDescription_WhereIsOnceOnly()
        {
            var builder = new AddressRuleBuilderImpl("RuleA");
            var any = builder.AnyGroup().Where(Match.InFolder("Assets/UI"), "a");

            Assert.Throws<InvalidOperationException>(() => any.Where(Match.InFolder("Assets/UI"), "b"));
            Assert.Throws<InvalidOperationException>(() => any.Where(Match.InFolder("Assets/UI")));
            Assert.Throws<InvalidOperationException>(() => any.Where(_ => true, "c"));
            Assert.Throws<InvalidOperationException>(() => builder.AnyGroup().Where(_ => true).Where(Match.InFolder("Assets/UI"), "x"));
        }

        [Test]
        public void NullCondition_Throws()
        {
            var builder = new AddressRuleBuilderImpl("RuleA");

            Assert.Throws<ArgumentNullException>(() => builder.Group("G").Where((AssetCondition)null, "x"));
            Assert.Throws<ArgumentNullException>(() => builder.AnyGroup().Where((AssetCondition)null, "x"));
        }

        [Test]
        public void ConditionWithDescription_MissingFolderIsStillReported()
        {
            DeclaredFolderInspector.ResetCache();
            try
            {
                var entry = GroupEntry(g => g.Where(Match.InFolder("Assets/Gone"), "gone"));

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
        public void ConditionWithDescription_StillDefinesTheUnmatchedScope()
        {
            var entry = GroupEntry(g => g.Where(
                Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Raw").Not()), "a without raw"));

            var scope = new DeclaredFolderScope(new[] { entry });

            Assert.IsTrue(scope.Contains("Assets/A/x.png"));
            Assert.IsFalse(scope.Contains("Assets/A/Raw/x.png"), "除外も保たれる。");
            Assert.IsFalse(scope.Contains("Assets/B/x.png"));
        }

        // --- And(AssetCondition, string) / AssetCondition(AssetCondition, string) ---

        [Test]
        public void And_ConditionWithDescription_KeepsTheNotExclusionAndOtherDeclarations()
        {
            var condition = Match.InFolder("Assets/A").And(Match.InFolder("Assets/A/Raw").Not(), "without raw");

            Assert.AreEqual("InFolder(Assets/A) AND without raw", condition.Description);
            CollectionAssert.AreEqual(new[] { "Assets/A", "Assets/A/Raw" }, condition.Folders);
            CollectionAssert.AreEqual(new[] { "Assets/A/Raw" }, condition.NegatedFolders);
            Assert.AreEqual(1, condition.ScopeExclusions.Count);

            var scope = new DeclaredFolderScope(new[] { GroupEntry(g => g.Where(condition)) });
            Assert.IsTrue(scope.Contains("Assets/A/x.png"));
            Assert.IsFalse(scope.Contains("Assets/A/Raw/x.png"), "除外が保たれるので、Raw の中身は未登録に数えない。");
        }

        [Test]
        public void And_ConditionWithDescription_KeepsGuidAndNonRecursiveDeclarations()
        {
            var condition = Match.InFolder("Assets/A", recursive: false).And(Match.InFolderByGuid("guid-1"), "text");

            Assert.AreEqual(1, condition.FolderGuids.Count);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, condition.NonRecursiveFolders);
        }

        [Test]
        public void And_ConditionWithDescription_SameDeclarationsAsTheOneArgumentAnd()
        {
            var other = Match.InFolder("Assets/B").Not();
            var joined = Match.InFolder("Assets/A").And(other);
            var described = Match.InFolder("Assets/A").And(other, "text");

            CollectionAssert.AreEqual(joined.Folders, described.Folders);
            CollectionAssert.AreEqual(joined.NegatedFolders, described.NegatedFolders);
            Assert.AreEqual(joined.ScopeExclusions.Count, described.ScopeExclusions.Count);
            Assert.AreEqual("InFolder(Assets/A) AND NOT InFolder(Assets/B)", joined.Description, "1 引数の And は従来どおり説明を連結する。");
            Assert.IsTrue(described.Test(new AssetContext("g", "Assets/A/x.png", typeof(UnityEngine.Texture2D))));
            Assert.IsFalse(described.Test(new AssetContext("g", "Assets/B/x.png", typeof(UnityEngine.Texture2D))));
        }

        [Test]
        public void And_ConditionWithNullDescription_MeansNoDescription()
        {
            // other の説明が無くなるので、残るのは this 側の説明だけ（述語版と同じ規則）。
            Assert.AreEqual("InFolder(Assets/A)", Match.InFolder("Assets/A").And(Match.InFolder("Assets/B"), null).Description);
        }

        [Test]
        public void And_ConditionWithDescription_MeansTheSameAsThePredicateOverload()
        {
            var viaCondition = Match.InFolder("Assets/A").And(Match.InFolder("Assets/B"), "text");
            var viaPredicate = Match.InFolder("Assets/A").And(ctx => ctx.IsInFolder("Assets/B"), "text");

            Assert.AreEqual("InFolder(Assets/A) AND text", viaCondition.Description);
            Assert.AreEqual(viaPredicate.Description, viaCondition.Description);
        }

        [Test]
        public void And_LambdaWithDescription_StillUsesThePredicateOverload()
        {
            var condition = Match.InFolder("Assets/A").And(ctx => ctx.FileName.EndsWith(".png"), "png");

            Assert.AreEqual("InFolder(Assets/A) AND png", condition.Description);
            CollectionAssert.AreEqual(new[] { "Assets/A" }, condition.Folders);
        }

        [Test]
        public void And_NullConditionWithDescription_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Match.InFolder("Assets/A").And((AssetCondition)null, "x"));
        }

        [Test]
        public void Constructor_FromCondition_KeepsEveryDeclarationAndReplacesTheDescription()
        {
            var source = Match.InFolder("Assets/A", recursive: false)
                .And(Match.InFolder("Assets/A/Raw").Not())
                .And(Match.InFolderByGuid("guid-1"));

            var copy = new AssetCondition(source, "text");

            Assert.AreEqual("text", copy.Description);
            CollectionAssert.AreEqual(source.Folders, copy.Folders);
            CollectionAssert.AreEqual(source.NonRecursiveFolders, copy.NonRecursiveFolders);
            CollectionAssert.AreEqual(source.NegatedFolders, copy.NegatedFolders);
            Assert.AreEqual(source.ScopeExclusions.Count, copy.ScopeExclusions.Count);
            Assert.AreEqual(1, copy.FolderGuids.Count);
            Assert.AreSame(source.Predicate, copy.Predicate);
        }

        [Test]
        public void Constructor_FromDirectFolderCondition_StillRecordsTheExclusionWhenNegated()
        {
            var copy = new AssetCondition(Match.InFolder("Assets/A/Raw"), "raw");

            Assert.AreEqual(1, copy.Not().ScopeExclusions.Count, "直接のフォルダ条件であること（PathTest）も引き継ぐ。");
        }

        [Test]
        public void Constructor_FromCondition_NullSourceThrows_AndLambdaStillUsesThePredicateConstructor()
        {
            Assert.Throws<ArgumentNullException>(() => new AssetCondition((AssetCondition)null, "x"));

            var fromLambda = new AssetCondition(_ => true, "d");
            Assert.AreEqual("d", fromLambda.Description);
            Assert.AreEqual(0, fromLambda.Folders.Count);
        }

        [Test]
        public void Constructor_FromCondition_CompoundDescriptionIsWrappedByOr()
        {
            var copy = new AssetCondition(Match.InFolder("Assets/A"), "x AND y");

            Assert.AreEqual("((x AND y) OR InFolder(Assets/B))", copy.Or(Match.InFolder("Assets/B")).Description);
        }

        [Test]
        public void ConditionPassedAsRawPredicate_DropsDeclarations_WhichIsWhyTheOverloadExists()
        {
            // Func として渡すと宣言が落ちる（暗黙変換）。説明付きの条件は Where(AssetCondition, string) を使う。
            Func<AssetContext, bool> predicate = Match.InFolder("Assets/UI");
            var entry = GroupEntry(g => g.Where(predicate, "text"));

            Assert.AreEqual(0, entry.DeclaredFolders.Count);
            Assert.AreEqual("text", entry.Description);
        }
    }
}

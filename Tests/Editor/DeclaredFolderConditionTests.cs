using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 条件が依存するフォルダの宣言（AssetCondition.Folders / AddressRuleEntry.DeclaredFolders）が、
    /// Match.* の生成・And/Match.All の合成・ビルダー経由でエントリへ引き継がれるまでの Core 側の振る舞いを検証する。
    /// </summary>
    public class DeclaredFolderConditionTests
    {
        private static AssetContext MakeCtx(string path) => new AssetContext("guid1", path, typeof(GameObject));

        // --- Match.InFolder ---

        [Test]
        public void InFolder_DeclaresItsFolder()
        {
            Assert.AreEqual(new[] { "Assets/Art" }, Match.InFolder("Assets/Art").Folders.ToArray());
        }

        [Test]
        public void InFolder_NonRecursive_DeclaresItsFolder()
        {
            Assert.AreEqual(new[] { "Assets/Art" }, Match.InFolder("Assets/Art", recursive: false).Folders.ToArray());
        }

        [Test]
        public void InFolder_NormalizesBackslashAndTrailingSlash()
        {
            Assert.AreEqual(new[] { "Assets/Art/Icons" }, Match.InFolder("Assets\\Art\\Icons/").Folders.ToArray());
        }

        [Test]
        public void InFolder_NullOrEmptyFolder_DeclaresNothing()
        {
            Assert.AreEqual(0, Match.InFolder(null).Folders.Count);
            Assert.AreEqual(0, Match.InFolder("").Folders.Count);
        }

        // --- Match.Glob ---

        [TestCase("Assets/Art/*.png", "Assets/Art")]
        [TestCase("Assets/Art/**/*.png", "Assets/Art")]
        [TestCase("Assets/Art/Icons/icon?.png", "Assets/Art/Icons")]
        [TestCase("Assets/Art*/x.png", "Assets")]
        [TestCase("Assets/Art/Icons/exact.png", "Assets/Art/Icons")]
        public void ExtractGlobFolderPrefix_ReturnsWholeSegmentsBeforeFirstWildcard(string pattern, string expected)
        {
            Assert.AreEqual(expected, Match.ExtractGlobFolderPrefix(pattern));
        }

        [TestCase("**/foo")]
        [TestCase("*.prefab")]
        [TestCase("?/foo")]
        [TestCase("Assets*/foo")]
        [TestCase("")]
        [TestCase(null)]
        public void ExtractGlobFolderPrefix_NoFixedPrefix_ReturnsNull(string pattern)
        {
            Assert.IsNull(Match.ExtractGlobFolderPrefix(pattern));
        }

        [Test]
        public void Glob_DeclaresItsFixedPrefix()
        {
            Assert.AreEqual(new[] { "Assets/Art" }, Match.Glob("Assets/Art/**/*.png").Folders.ToArray());
        }

        [Test]
        public void Glob_LeadingWildcard_DeclaresNothing()
        {
            Assert.AreEqual(0, Match.Glob("**/foo/*.png").Folders.Count);
        }

        // --- 合成 ---

        [Test]
        public void And_AssetCondition_FoldersAreUnion()
        {
            var combined = Match.InFolder("Assets/A").And(Match.InFolder("Assets/B"));

            CollectionAssert.AreEquivalent(new[] { "Assets/A", "Assets/B" }, combined.Folders.ToArray());
        }

        [Test]
        public void And_SameFolderTwice_DeclaredOnce()
        {
            var combined = Match.InFolder("Assets/A").And(Match.Glob("Assets/A/*.png"));

            Assert.AreEqual(new[] { "Assets/A" }, combined.Folders.ToArray());
        }

        [Test]
        public void And_RawPredicate_KeepsOwnFoldersOnly()
        {
            var combined = Match.InFolder("Assets/A").And(ctx => ctx.Extension == ".png");

            Assert.AreEqual(new[] { "Assets/A" }, combined.Folders.ToArray());
        }

        [Test]
        public void Match_All_FoldersAreUnionOfAllConditions()
        {
            var combined = Match.All(Match.InFolder("Assets/A"), Match.OfType<GameObject>(), Match.Glob("Assets/B/*.png"));

            CollectionAssert.AreEquivalent(new[] { "Assets/A", "Assets/B" }, combined.Folders.ToArray());
        }

        [Test]
        public void Match_All_NoConditions_DeclaresNothing()
        {
            Assert.AreEqual(0, Match.All().Folders.Count);
        }

        [Test]
        public void Constructor_DropsNullEmptyAndDuplicateFolders()
        {
            var condition = new AssetCondition(ctx => true, null, new[] { "Assets/A", null, "", "Assets/A/", "Assets/B" });

            Assert.AreEqual(new[] { "Assets/A", "Assets/B" }, condition.Folders.ToArray());
        }

        [Test]
        public void Constructor_NoFolders_IsEmptyNotNull()
        {
            Assert.IsNotNull(new AssetCondition(ctx => true).Folders);
            Assert.AreEqual(0, new AssetCondition(ctx => true).Folders.Count);
        }

        // --- Match.InFolderByGuid ---

        [Test]
        public void InFolderByGuid_EmptyGuid_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => Match.InFolderByGuid(null));
            Assert.Throws<System.ArgumentException>(() => Match.InFolderByGuid(""));
        }

        /// <summary>FolderGuidResolver.Resolver を一時的に差し替えて action を実行し、必ず元に戻す。</summary>
        private static void WithResolver(System.Func<string, string> resolver, System.Action action)
        {
            var original = FolderGuidResolver.Resolver;
            FolderGuidResolver.Resolver = resolver;
            try
            {
                action();
            }
            finally
            {
                FolderGuidResolver.Resolver = original;
            }
        }

        [Test]
        public void InFolderByGuid_Unresolvable_MatchesNothingAndDeclaresNoPath()
        {
            WithResolver(_ => null, () =>
            {
                var condition = Match.InFolderByGuid("abc123");

                Assert.IsFalse(condition.Test(MakeCtx("Assets/Art/Icon.png")));
                Assert.AreEqual(0, condition.Folders.Count);
                Assert.AreEqual(1, condition.FolderGuids.Count);
            });
        }

        [Test]
        public void InFolderByGuid_NoResolverInjected_NeverMatches()
        {
            // 解決器が無い環境（Core 単体）では何も解決できず、条件は常に不一致（例外にもならない）。
            WithResolver(null, () =>
                Assert.IsFalse(Match.InFolderByGuid("abc123").Test(MakeCtx("Assets/Art/Icon.png"))));
        }

        [Test]
        public void InFolderByGuid_Resolvable_MatchesRecursively()
        {
            WithResolver(g => g == "abc123" ? "Assets/Art" : null, () =>
            {
                var condition = Match.InFolderByGuid("abc123");

                Assert.IsTrue(condition.Test(MakeCtx("Assets/Art/Sub/Icon.png")));
                Assert.IsFalse(condition.Test(MakeCtx("Assets/Other/Icon.png")));
            });
        }

        [Test]
        public void InFolderByGuid_NonRecursive_MatchesDirectChildrenOnly()
        {
            WithResolver(_ => "Assets/Art", () =>
            {
                var condition = Match.InFolderByGuid("abc123", recursive: false);

                Assert.IsTrue(condition.Test(MakeCtx("Assets/Art/Icon.png")));
                Assert.IsFalse(condition.Test(MakeCtx("Assets/Art/Sub/Icon.png")));
            });
        }

        [Test]
        public void InFolderByGuid_ResolvedOnEveryEvaluation_FollowsMovedFolder()
        {
            // 述語は解決結果を保持せず評価のたびに引くので、同じ条件でも、フォルダが移動・改名された後は
            // 新しいパスで判定され、評価の順序や過去の評価結果に依存しない。
            var path = "Assets/Old";
            WithResolver(_ => path, () =>
            {
                var condition = Match.InFolderByGuid("abc123");

                Assert.IsTrue(condition.Test(MakeCtx("Assets/Old/Icon.png")));
                path = "Assets/New";
                Assert.IsFalse(condition.Test(MakeCtx("Assets/Old/Icon.png")));
                Assert.IsTrue(condition.Test(MakeCtx("Assets/New/Icon.png")));
                path = null;
                Assert.IsFalse(condition.Test(MakeCtx("Assets/New/Icon.png")));
            });
        }

        [Test]
        public void InFolderByGuid_TwoConditionsWithDifferentGuids_DoNotInfluenceEachOther()
        {
            WithResolver(g => g == "a" ? "Assets/A" : "Assets/B", () =>
            {
                var a = Match.InFolderByGuid("a");
                var b = Match.InFolderByGuid("b");

                Assert.IsTrue(b.Test(MakeCtx("Assets/B/x.png")));
                Assert.IsTrue(a.Test(MakeCtx("Assets/A/x.png")));
                Assert.IsFalse(a.Test(MakeCtx("Assets/B/x.png")));
                Assert.IsFalse(b.Test(MakeCtx("Assets/A/x.png")));
            });
        }

        [Test]
        public void InFolderByGuid_ViaRuleInspectorCollect_SeesSameResultAsEvaluation()
        {
            WithResolver(_ => "Assets/Art", () =>
            {
                var entries = AddressTeller.Testing.RuleInspector.Collect(new GuidDeclaringRule());

                Assert.IsTrue(entries[0].Predicate(MakeCtx("Assets/Art/Icon.png")));
                Assert.IsFalse(entries[0].Predicate(MakeCtx("Assets/Other/Icon.png")));
            });
        }

        private sealed class GuidDeclaringRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("G").Where(Match.InFolderByGuid("abc123")).Address("x");
            }
        }

        [Test]
        public void And_PropagatesFolderGuidDeclarations()
        {
            WithResolver(_ => "Assets/B", () =>
            {
                var combined = Match.InFolder("Assets/A").And(Match.InFolderByGuid("abc123"));

                Assert.AreEqual(1, combined.FolderGuids.Count);
                Assert.IsFalse(combined.Test(MakeCtx("Assets/B/x.png")), "InFolder(Assets/A) も満たす必要がある。");
            });
        }

        // --- ビルダー → エントリ ---

        [Test]
        public void Builder_WhereWithCondition_CarriesFoldersToEntry()
        {
            var builder = new AddressRuleBuilderImpl("TestRule");
            builder.Group("G").Where(Match.InFolder("Assets/A").And(Match.Glob("Assets/B/*.png"))).Address("x");

            CollectionAssert.AreEquivalent(new[] { "Assets/A", "Assets/B" }, builder.Entries[0].DeclaredFolders.ToArray());
        }

        [Test]
        public void Builder_AnyGroupWhereWithCondition_CarriesFoldersToEntry()
        {
            var builder = new AddressRuleBuilderImpl("TestRule");
            builder.AnyGroup().Where(Match.InFolder("Assets/A")).Label("l");

            Assert.AreEqual(new[] { "Assets/A" }, builder.Entries[0].DeclaredFolders.ToArray());
        }

        [Test]
        public void Builder_WhereWithRawLambda_DeclaresNothing()
        {
            var builder = new AddressRuleBuilderImpl("TestRule");
            builder.Group("G").Where(ctx => ctx.IsInFolder("Assets/A")).Address("x");

            Assert.AreEqual(0, builder.Entries[0].DeclaredFolders.Count);
        }

        [Test]
        public void Builder_WhereWithRawLambdaAndDescription_DeclaresNothing()
        {
            var builder = new AddressRuleBuilderImpl("TestRule");
            builder.Group("G").Where(ctx => ctx.IsInFolder("Assets/A"), "in A").Address("x");

            Assert.AreEqual(0, builder.Entries[0].DeclaredFolders.Count);
        }

        [Test]
        public void Builder_NoWhere_DeclaresNothing()
        {
            var builder = new AddressRuleBuilderImpl("TestRule");
            builder.Group("G").Address("x");

            Assert.AreEqual(0, builder.Entries[0].DeclaredFolders.Count);
        }

        [Test]
        public void Builder_WhereWithFolderGuidCondition_CarriesGuidDeclarationToEntry()
        {
            var builder = new AddressRuleBuilderImpl("TestRule");
            builder.Group("G").Where(Match.InFolderByGuid("abc123")).Address("x");

            Assert.AreEqual(1, builder.Entries[0].FolderGuids.Count);
            Assert.AreEqual("abc123", builder.Entries[0].FolderGuids[0].Guid);
        }

        [Test]
        public void PublicConstructor_AcceptsDeclaredFolders()
        {
            var entry = new AddressRuleEntry("G", _ => true, _ => "a", new System.Func<AssetContext, string>[0],
                declaredFolders: new[] { "Assets/A/" });

            Assert.AreEqual(new[] { "Assets/A" }, entry.DeclaredFolders.ToArray());
        }

        [Test]
        public void PublicConstructor_WithoutDeclaredFolders_IsEmpty()
        {
            var entry = new AddressRuleEntry("G", _ => true, _ => "a", new System.Func<AssetContext, string>[0]);

            Assert.AreEqual(0, entry.DeclaredFolders.Count);
        }
    }
}

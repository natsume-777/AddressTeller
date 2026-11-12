using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using AddressTeller.Testing;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// <see cref="RuleInspector.Evaluate"/> / <see cref="RuleInspector.Verify"/> のテスト共通の部品。
    /// 優先度（Order）・ラベル専用・同点・空アドレス・例外・GroupDefault を一通り含むルール集合を提供する。
    /// </summary>
    internal static class RuleInspectorTestRules
    {
        internal sealed class TestRule : AddressRuleBase
        {
            private readonly int _order;
            private readonly Action<IAddressRuleBuilder> _configure;

            public TestRule(int order, Action<IAddressRuleBuilder> configure)
            {
                _order = order;
                _configure = configure;
            }

            public override int Order => _order;

            public override void Configure(IAddressRuleBuilder rules) => _configure(rules);
        }

        private static bool Under(AssetContext ctx, string folder) => ctx.Path.StartsWith(folder, StringComparison.Ordinal);

        public static List<AddressRuleBase> Build() => new List<AddressRuleBase>
        {
            // 広い低優先ルール
            new TestRule(100, r => r.Group("Broad")
                .Where(ctx => Under(ctx, "Assets/Content/"), "Content")
                .Address(Naming.FileNameWithoutExtension())
                .Label("broad")),
            // 狭い高優先ルール
            new TestRule(0, r => r.Group("Narrow")
                .Where(ctx => Under(ctx, "Assets/Content/Hero/"), "Hero")
                .Address(ctx => "hero/" + ctx.FileNameWithoutExtension)
                .Label("narrow")),
            // ラベル専用ルール（勝敗と無関係に付く）
            new TestRule(50, r =>
            {
                r.AnyGroup().Where(ctx => ctx.Extension == ".prefab", "Prefabs").Label("prefab");
                r.AnyGroup().Where(ctx => Under(ctx, "Assets/Tags/"), "Tags").Label("tag");
            }),
            // 同点（どちらも Order=5）
            new TestRule(5, r => r.Group("TieA").Where(ctx => Under(ctx, "Assets/Tie/"), "TieA").Address("a")),
            new TestRule(5, r => r.Group("TieB").Where(ctx => Under(ctx, "Assets/Tie/"), "TieB").Address("b")),
            // 空アドレス
            new TestRule(7, r => r.Group("Broad").Where(ctx => Under(ctx, "Assets/Empty/"), "Empty").Address(ctx => "")),
            // GroupDefault
            new TestRule(9, r => r.GroupDefault()
                .Where(ctx => Under(ctx, "Assets/Config/"), "Config")
                .Address(Naming.FileNameWithoutExtension())),
            // 勝者になりうる高優先ルールが例外を送出し、低優先の候補だけが残る
            new TestRule(3, r => r.Group("Narrow")
                .Where(ctx => Under(ctx, "Assets/Throw/") ? throw new InvalidOperationException("boom") : false, "Thrower")
                .Address("never")),
            new TestRule(50, r => r.Group("Low").Where(ctx => Under(ctx, "Assets/Throw/"), "Low").Address("low")),
            // ラベル専用ルールの例外は勝者を止めない
            new TestRule(20, r => r.AnyGroup()
                .Where(ctx => Under(ctx, "Assets/LabelThrow/") ? throw new InvalidOperationException("label boom") : false, "LabelThrower")
                .Label("x")),
            new TestRule(30, r => r.Group("Low").Where(ctx => Under(ctx, "Assets/LabelThrow/"), "LabelThrowTarget").Address("lt")),
        };

        public static AssetContext Ctx(string path, Type type = null, bool isFolder = false, string guid = null)
            => new AssetContext(guid ?? "g-" + path, path, type ?? typeof(ScriptableObject), isFolder);
    }

    public class RuleInspectorEvaluateTests
    {
        private static List<AddressRuleBase> Rules() => RuleInspectorTestRules.Build();

        private static RuleOutcome Evaluate(string path, Type type = null, bool isFolder = false)
            => RuleInspector.Evaluate(Rules(), RuleInspectorTestRules.Ctx(path, type, isFolder));

        // --- Evaluate ---

        [Test]
        public void Evaluate_NarrowHighPriorityWins_OnlyWinnerAndLabelOnlyLabelsAreReported()
        {
            var outcome = Evaluate("Assets/Content/Hero/Hero.prefab", typeof(GameObject));

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.AreEqual("Narrow", outcome.GroupName);
            Assert.IsFalse(outcome.UsesDefaultGroup);
            Assert.AreEqual("hero/Hero", outcome.Address);
            // 負けた広いルールの "broad" は含まれない。ラベル専用ルールの "prefab" は含まれる。
            CollectionAssert.AreEqual(new[] { "narrow", "prefab" }, outcome.Labels);
            Assert.AreEqual("TestRule > \"Hero\"", outcome.WinningRule);
            Assert.IsEmpty(outcome.Messages);
        }

        [Test]
        public void Evaluate_OnlyBroadRuleMatches_BroadRuleWins()
        {
            var outcome = Evaluate("Assets/Content/Props/Crate.asset");

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.AreEqual("Broad", outcome.GroupName);
            Assert.AreEqual("Crate", outcome.Address);
            CollectionAssert.AreEqual(new[] { "broad" }, outcome.Labels);
        }

        [Test]
        public void Evaluate_NoRuleMatches_IsNone()
        {
            var outcome = Evaluate("Assets/Other/Thing.asset");

            Assert.AreEqual(RuleOutcomeKind.None, outcome.Kind);
            Assert.IsNull(outcome.GroupName);
            Assert.IsNull(outcome.Address);
            Assert.IsNull(outcome.WinningRule);
            Assert.IsEmpty(outcome.Labels);
            Assert.IsEmpty(outcome.Messages);
        }

        [Test]
        public void Evaluate_OnlyLabelRulesMatch_IsLabelsOnlyWithSortedLabels()
        {
            var outcome = Evaluate("Assets/Tags/Thing.prefab", typeof(GameObject));

            Assert.AreEqual(RuleOutcomeKind.LabelsOnly, outcome.Kind);
            Assert.IsNull(outcome.Address);
            CollectionAssert.AreEqual(new[] { "prefab", "tag" }, outcome.Labels);
        }

        [Test]
        public void Evaluate_TiedLowestOrder_IsConflictWithNoLabelsAndTiedRulesInMessages()
        {
            var outcome = Evaluate("Assets/Tie/X.asset");

            Assert.AreEqual(RuleOutcomeKind.Conflict, outcome.Kind);
            Assert.IsNull(outcome.Address);
            Assert.IsEmpty(outcome.Labels);
            Assert.AreEqual(2, outcome.Messages.Count);
            StringAssert.Contains("TieA", outcome.Messages[0]);
            StringAssert.Contains("TieB", outcome.Messages[1]);
        }

        [Test]
        public void Evaluate_EmptyAddress_IsInvalidAddressAndNamesTheRule()
        {
            var outcome = Evaluate("Assets/Empty/X.asset");

            Assert.AreEqual(RuleOutcomeKind.InvalidAddress, outcome.Kind);
            Assert.IsNull(outcome.Address);
            Assert.IsEmpty(outcome.Labels);
            Assert.AreEqual("TestRule > \"Empty\"", outcome.WinningRule);
        }

        [Test]
        public void Evaluate_GroupDefault_ReportsUsesDefaultGroupWithoutRevealingASentinel()
        {
            var outcome = Evaluate("Assets/Config/Settings.asset");

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.IsTrue(outcome.UsesDefaultGroup);
            Assert.IsNull(outcome.GroupName);
            Assert.AreEqual("Settings", outcome.Address);
        }

        [Test]
        public void Evaluate_HigherPriorityRuleThrows_IsBlockedByRuleErrorAndReportsTheException()
        {
            var outcome = Evaluate("Assets/Throw/X.asset");

            Assert.AreEqual(RuleOutcomeKind.BlockedByRuleError, outcome.Kind);
            Assert.IsNull(outcome.Address);
            Assert.IsEmpty(outcome.Labels);
            Assert.AreEqual(1, outcome.Messages.Count);
            StringAssert.Contains("boom", outcome.Messages[0]);
        }

        [Test]
        public void Evaluate_LabelOnlyRuleThrows_DoesNotBlockTheWinnerButIsReported()
        {
            var outcome = Evaluate("Assets/LabelThrow/X.asset");

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.AreEqual("lt", outcome.Address);
            Assert.AreEqual(1, outcome.Messages.Count);
            StringAssert.Contains("label boom", outcome.Messages[0]);
        }

        [Test]
        public void Evaluate_FolderContext_IsSkippedUnlessTheRuleOptsIn()
        {
            var outcome = Evaluate("Assets/Content/Props", typeof(UnityEditor.DefaultAsset), isFolder: true);

            Assert.AreEqual(RuleOutcomeKind.None, outcome.Kind);
        }

        [Test]
        public void Evaluate_FolderContext_IsSeenByRuleThatCalledIncludeFolders()
        {
            var rules = new List<AddressRuleBase>
            {
                new RuleInspectorTestRules.TestRule(0, r => r.Group("Folders")
                    .Where(ctx => ctx.IsFolder, "AnyFolder")
                    .IncludeFolders()
                    .Address(ctx => ctx.FileName)),
            };

            var outcome = RuleInspector.Evaluate(rules, RuleInspectorTestRules.Ctx("Assets/Content/Props", typeof(UnityEditor.DefaultAsset), true));

            Assert.AreEqual(RuleOutcomeKind.Address, outcome.Kind);
            Assert.AreEqual("Props", outcome.Address);
        }

        [Test]
        public void Evaluate_RulesAreEvaluatedAsGiven_SameOrderTieStillConflicts()
        {
            // 同じ Order は登録順に関わらず同点（競合）になる。
            var rules = Rules();
            rules.Reverse();

            var outcome = RuleInspector.Evaluate(rules, RuleInspectorTestRules.Ctx("Assets/Tie/X.asset"));

            Assert.AreEqual(RuleOutcomeKind.Conflict, outcome.Kind);
        }

        [Test]
        public void Evaluate_ConfigureThrows_PropagatesToTheCaller()
        {
            var rules = new List<AddressRuleBase>
            {
                new RuleInspectorTestRules.TestRule(0, r => throw new InvalidOperationException("configure boom")),
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => RuleInspector.Evaluate(rules, RuleInspectorTestRules.Ctx("Assets/A.asset")));
            Assert.AreEqual("configure boom", ex.Message);
        }

        [Test]
        public void Evaluate_NullArguments_Throw()
        {
            var ctx = RuleInspectorTestRules.Ctx("Assets/A.asset");
            Assert.Throws<ArgumentNullException>(() => RuleInspector.Evaluate(null, ctx));
            Assert.Throws<ArgumentNullException>(() => RuleInspector.Evaluate(Rules(), null));
            Assert.Throws<ArgumentException>(() => RuleInspector.Evaluate(new AddressRuleBase[] { null }, ctx));
        }

        // --- Verify（期待値表） ---

        private static IEnumerable<TestCaseData> PassingTable()
        {
            yield return Row(new RuleExpectation("Assets/Content/Hero/Hero.prefab", typeof(GameObject))
                { Group = "Narrow", Address = "hero/Hero", Labels = new[] { "prefab", "narrow" } });
            yield return Row(new RuleExpectation("Assets/Content/Props/Crate.prefab", typeof(GameObject))
                { Group = "Broad", Address = "Crate", Labels = new[] { "broad", "prefab" } });
            yield return Row(new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject))
                { Address = "Crate" });
            yield return Row(new RuleExpectation("Assets/Other/Thing.asset", typeof(ScriptableObject)));
            yield return Row(new RuleExpectation("Assets/Tags/Thing.prefab", typeof(GameObject))
                { Labels = new[] { "tag", "prefab" } });
            yield return Row(new RuleExpectation("Assets/Tie/X.asset", typeof(ScriptableObject))
                { Kind = RuleOutcomeKind.Conflict });
            yield return Row(new RuleExpectation("Assets/Empty/X.asset", typeof(ScriptableObject))
                { Kind = RuleOutcomeKind.InvalidAddress });
            yield return Row(new RuleExpectation("Assets/Config/Settings.asset", typeof(ScriptableObject))
                { UsesDefaultGroup = true, Address = "Settings", Labels = new string[0] });
            yield return Row(new RuleExpectation("Assets/Content/Props", typeof(UnityEditor.DefaultAsset))
                { IsFolder = true });
        }

        private static TestCaseData Row(RuleExpectation expectation)
            => new TestCaseData(expectation).SetName("Verify_Passes_" + expectation.Path);

        [TestCaseSource(nameof(PassingTable))]
        public void Verify_ExpectationRowMatchingTheRules_ReturnsNoFailures(RuleExpectation expectation)
        {
            var failures = RuleInspector.Verify(Rules(), new[] { expectation });

            CollectionAssert.IsEmpty(failures);
        }

        [Test]
        public void Verify_WholeTable_ReturnsNoFailures()
        {
            var table = PassingTable().Select(c => (RuleExpectation)c.Arguments[0]).ToList();

            CollectionAssert.IsEmpty(RuleInspector.Verify(Rules(), table));
        }

        [Test]
        public void Verify_WrongAddress_ReportsTheRowWithExpectedAndActual()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject)) { Address = "Wrong" },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.StartsWith("row 1: Assets/Content/Props/Crate.asset:", failures[0]);
            StringAssert.Contains("expected address 'Wrong' but was 'Crate'", failures[0]);
        }

        [Test]
        public void Verify_WrongGroupAndLabels_ReportsEachDifference()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject))
                    { Group = "Narrow", Labels = new[] { "narrow" } },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("expected group 'Narrow' but was 'Broad'", failures[0]);
            StringAssert.Contains("expected labels [narrow] but was [broad]", failures[0]);
        }

        [Test]
        public void Verify_ExpectedEmptyLabelsButLabelsExist_Fails()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject))
                    { Address = "Crate", Labels = new string[0] },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("expected labels [] but was [broad]", failures[0]);
        }

        [Test]
        public void Verify_UnsetKindIsInferred_ExpectingNoneForAnAssetThatMatches_Fails()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject)),
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("expected None but was Address", failures[0]);
        }

        [Test]
        public void Verify_ExpectedDefaultGroupButNamedGroupWon_Fails()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject)) { UsesDefaultGroup = true },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("expected the default group but was 'Broad'", failures[0]);
        }

        [Test]
        public void Verify_SeveralFailingRows_ReportsOneMessagePerFailingRowOnly()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject)) { Address = "Crate" },
                new RuleExpectation("Assets/Other/A.asset", typeof(ScriptableObject)) { Address = "A" },
                new RuleExpectation("Assets/Other/B.asset", typeof(ScriptableObject)) { Kind = RuleOutcomeKind.Conflict },
            });

            Assert.AreEqual(2, failures.Count);
            StringAssert.StartsWith("row 2: Assets/Other/A.asset:", failures[0]);
            StringAssert.StartsWith("row 3: Assets/Other/B.asset:", failures[1]);
        }

        [Test]
        public void Verify_ConflictMessage_IncludesTiedRulesFromTheOutcome()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Tie/X.asset", typeof(ScriptableObject)) { Address = "a" },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("TieA", failures[0]);
            StringAssert.Contains("TieB", failures[0]);
        }

        [Test]
        public void Verify_RuleThrewEvenIfRowOtherwiseMatches_Fails()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/LabelThrow/X.asset", typeof(ScriptableObject)) { Address = "lt" },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("a rule threw during evaluation", failures[0]);
            StringAssert.Contains("label boom", failures[0]);
        }

        [Test]
        public void Verify_BlockedRowExpectingBlockedByRuleError_PassesDespiteTheBlockingThrow()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Throw/X.asset", typeof(ScriptableObject)) { Kind = RuleOutcomeKind.BlockedByRuleError },
            });

            CollectionAssert.IsEmpty(failures);
        }

        [Test]
        public void Verify_BlockedRowWithAnUnrelatedThrow_StillFails()
        {
            // 勝者を止める例外とは別に、ラベル専用ルールも例外を送出する行。後者は期待されていないので失敗のまま。
            var rules = Rules();
            rules.Add(new RuleInspectorTestRules.TestRule(20, r => r.AnyGroup()
                .Where(ctx => ctx.Path.StartsWith("Assets/Throw/", StringComparison.Ordinal) ? throw new InvalidOperationException("extra boom") : false, "ExtraThrower")
                .Label("y")));

            var failures = RuleInspector.Verify(rules, new[]
            {
                new RuleExpectation("Assets/Throw/X.asset", typeof(ScriptableObject)) { Kind = RuleOutcomeKind.BlockedByRuleError },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("a rule threw during evaluation", failures[0]);
            StringAssert.Contains("extra boom", failures[0]);
        }

        [Test]
        public void Verify_BlockedRowExpectedAsSomethingElse_FailsOnKind()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Throw/X.asset", typeof(ScriptableObject)) { Kind = RuleOutcomeKind.None },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("expected None but was BlockedByRuleError", failures[0]);
            StringAssert.Contains("a rule threw during evaluation", failures[0]);
        }

        [Test]
        public void Verify_LabelsOnlyWithoutKind_AcceptsAddressAndLabelsOnlyButNotNone()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Tags/Thing.prefab", typeof(GameObject)) { Labels = new[] { "prefab", "tag" } },
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject)) { Labels = new[] { "broad" } },
                new RuleExpectation("Assets/Other/Thing.asset", typeof(ScriptableObject)) { Labels = new[] { "tag" } },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.StartsWith("row 3:", failures[0]);
            StringAssert.Contains("expected a match (Address or LabelsOnly) but was None", failures[0]);
        }

        [Test]
        public void Verify_LabelsWithExplicitLabelsOnlyKind_RejectsAnAddressOutcome()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject))
                    { Kind = RuleOutcomeKind.LabelsOnly, Labels = new[] { "broad" } },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("expected LabelsOnly but was Address", failures[0]);
        }

        [Test]
        public void Verify_UsesDefaultGroupFalse_RejectsTheDefaultGroup()
        {
            var failures = RuleInspector.Verify(Rules(), new[]
            {
                new RuleExpectation("Assets/Config/Settings.asset", typeof(ScriptableObject)) { Address = "Settings", UsesDefaultGroup = false },
                new RuleExpectation("Assets/Content/Props/Crate.asset", typeof(ScriptableObject)) { Address = "Crate", UsesDefaultGroup = false },
            });

            Assert.AreEqual(1, failures.Count);
            StringAssert.StartsWith("row 1:", failures[0]);
            StringAssert.Contains("expected a named group but the default group was used", failures[0]);
        }

        private static IEnumerable<TestCaseData> ContradictoryRows()
        {
            yield return new TestCaseData(new RuleExpectation("Assets/Config/Settings.asset", typeof(ScriptableObject))
                { Group = "G", UsesDefaultGroup = true }).SetName("Contradictory_GroupAndUsesDefaultGroup");
            yield return new TestCaseData(new RuleExpectation("Assets/Tie/X.asset", typeof(ScriptableObject))
                { Kind = RuleOutcomeKind.Conflict, Address = "a" }).SetName("Contradictory_ConflictWithAddress");
            yield return new TestCaseData(new RuleExpectation("Assets/Throw/X.asset", typeof(ScriptableObject))
                { Kind = RuleOutcomeKind.BlockedByRuleError, Group = "Low" }).SetName("Contradictory_BlockedWithGroup");
            yield return new TestCaseData(new RuleExpectation("Assets/Other/Thing.asset", typeof(ScriptableObject))
                { Kind = RuleOutcomeKind.None, Address = "x" }).SetName("Contradictory_NoneWithAddress");
            yield return new TestCaseData(new RuleExpectation("Assets/Tie/X.asset", typeof(ScriptableObject))
                { Kind = RuleOutcomeKind.Conflict, Labels = new[] { "l" } }).SetName("Contradictory_ConflictWithLabels");
        }

        [TestCaseSource(nameof(ContradictoryRows))]
        public void Verify_ContradictoryExpectation_IsReportedWithoutEvaluating(RuleExpectation expectation)
        {
            var failures = RuleInspector.Verify(Rules(), new[] { expectation });

            Assert.AreEqual(1, failures.Count);
            StringAssert.StartsWith("row 1: " + expectation.Path + ": contradictory expectation:", failures[0]);
        }

        [Test]
        public void ToString_OfExpectationAndOutcome_IsReadable()
        {
            Assert.AreEqual("Assets/A.asset", new RuleExpectation("Assets/A.asset", typeof(GameObject)).ToString());
            Assert.AreEqual("Assets/A.asset (Conflict)",
                new RuleExpectation("Assets/A.asset", typeof(GameObject)) { Kind = RuleOutcomeKind.Conflict }.ToString());

            var outcome = Evaluate("Assets/Content/Hero/Hero.prefab", typeof(GameObject));
            Assert.AreEqual("Address Narrow -> \"hero/Hero\" [narrow, prefab]", outcome.ToString());
            Assert.AreEqual("None", Evaluate("Assets/Other/Thing.asset").ToString());
        }

        [Test]
        public void Verify_ConfigureRunsOncePerRuleForTheWholeTable()
        {
            var configureCalls = 0;
            var rules = new List<AddressRuleBase>
            {
                new RuleInspectorTestRules.TestRule(0, r =>
                {
                    configureCalls++;
                    r.Group("G").Where(ctx => true).Address("a");
                }),
            };

            RuleInspector.Verify(rules, new[]
            {
                new RuleExpectation("Assets/A.asset", typeof(ScriptableObject)),
                new RuleExpectation("Assets/B.asset", typeof(ScriptableObject)),
                new RuleExpectation("Assets/C.asset", typeof(ScriptableObject)),
            });

            Assert.AreEqual(1, configureCalls);
        }

        [Test]
        public void Verify_ConfigureThrows_PropagatesToTheCaller()
        {
            var rules = new List<AddressRuleBase>
            {
                new RuleInspectorTestRules.TestRule(0, r => throw new InvalidOperationException("configure boom")),
            };

            Assert.Throws<InvalidOperationException>(() => RuleInspector.Verify(rules, new[]
            {
                new RuleExpectation("Assets/A.asset", typeof(ScriptableObject)),
            }));
        }

        [Test]
        public void Verify_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => RuleInspector.Verify(null, new RuleExpectation[0]));
            Assert.Throws<ArgumentNullException>(() => RuleInspector.Verify(Rules(), null));
            Assert.Throws<ArgumentException>(() => RuleInspector.Verify(Rules(), new RuleExpectation[] { null }));
        }

        [Test]
        public void Verify_EmptyTable_ReturnsNoFailures()
        {
            CollectionAssert.IsEmpty(RuleInspector.Verify(Rules(), new RuleExpectation[0]));
        }

        [Test]
        public void RuleExpectation_Constructor_ValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new RuleExpectation("", typeof(GameObject)));
            Assert.Throws<ArgumentException>(() => new RuleExpectation(null, typeof(GameObject)));
            Assert.Throws<ArgumentNullException>(() => new RuleExpectation("Assets/A.asset", null));
        }
    }

    /// <summary>
    /// <see cref="RuleInspector.Evaluate"/> の結論が、dry-run（<see cref="AddressTellerApplier.Predict"/>）と
    /// 食い違わないことを固定する回帰テスト。両者は勝者決定と最終ラベル合成を同じ Core の実装で行うが、
    /// 一方だけが将来別の判定を持ち込んでも検知できるよう、同じルール・同じアセットで突き合わせる。
    /// </summary>
    public class RuleInspectorDryRunConsistencyTests : LabelLedgerIsolatedFixture
    {
        private const string DefaultGroupName = "DefaultGroupForTest";

        private AddressableAssetSettings _settings;
        private readonly List<AddressableAssetGroup> _groups = new List<AddressableAssetGroup>();

        [SetUp]
        public void SetUp()
        {
            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerRuleInspectorTestSettings", false, false);
            foreach (var name in new[] { "Broad", "Narrow", "Low", "TieA", "TieB", DefaultGroupName })
                _groups.Add(_settings.CreateGroup(name, false, false, false, null));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var group in _groups)
                UnityEngine.Object.DestroyImmediate(group, true);
            _groups.Clear();
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private static readonly string[] Paths =
        {
            "Assets/Content/Hero/Hero.prefab",
            "Assets/Content/Props/Crate.prefab",
            "Assets/Content/Props/Crate.asset",
            "Assets/Other/Thing.asset",
            "Assets/Tags/Thing.prefab",
            "Assets/Tie/X.asset",
            "Assets/Empty/X.asset",
            "Assets/Config/Settings.asset",
            "Assets/Throw/X.asset",
            "Assets/LabelThrow/X.asset",
        };

        private static RuleOutcomeKind KindOf(ValidationStatus status)
        {
            switch (status)
            {
                case ValidationStatus.Ok: return RuleOutcomeKind.Address;
                case ValidationStatus.Skipped: return RuleOutcomeKind.None;
                case ValidationStatus.LabelsOnly: return RuleOutcomeKind.LabelsOnly;
                case ValidationStatus.ConflictingAddress: return RuleOutcomeKind.Conflict;
                case ValidationStatus.InvalidAddress: return RuleOutcomeKind.InvalidAddress;
                case ValidationStatus.BlockedByRuleError: return RuleOutcomeKind.BlockedByRuleError;
                default:
                    Assert.Fail($"Unexpected validation status in the dry run: {status}");
                    return RuleOutcomeKind.None;
            }
        }

        // 突き合わせの範囲について: dry-run 側は BuildPredictedRunState を通さず、Core の RuleEvaluator と
        // AddressTellerApplier.Predict を直接呼ぶ。BuildPredictedRunState は実アセットの AssetDatabase 解決
        // （BuildContext）を要するため、このテストからは使えない。したがって、BuildSetup / BuildPredictedRunState
        // 固有の前処理（AssetFilter、Configure() の失敗集約、宣言フォルダの検査、グループ実在確認）で
        // 両者がずれても、このテストでは検知できない。GroupDefault() のセンチネル解決だけは BuildSetup と
        // 同じ置換をここで再現している。
        private ApplyPrediction Predict(IEnumerable<AddressRuleBase> rules, AssetContext context)
        {
            var entries = rules
                .SelectMany(rule => RuleInspector.Collect(rule))
                .Select(e => RuleInspector.IsUnresolvedDefaultGroup(e.GroupName) ? e.WithGroupName(DefaultGroupName) : e)
                .ToList();
            var resolution = RuleEvaluator.Evaluate(context, entries);
            var groupNames = new HashSet<string>(_groups.Select(g => g.Name));
            return AddressTellerApplier.Predict(context, resolution, _settings, groupNames, groupNames);
        }

        [Test]
        public void Evaluate_LabelsOnly_AgreesWithDryRunPredictionForAnExistingEntry()
        {
            // ラベル専用ルールだけがマッチするアセットに既存エントリがあるとき、dry-run は既存ラベルとの和集合を予測する。
            // Evaluate のラベルが、その和集合から既存ラベルを除いたものと一致することを確かめる。
            const string path = "Assets/Tags/Thing.prefab";
            var rules = RuleInspectorTestRules.Build();
            var context = RuleInspectorTestRules.Ctx(path, typeof(GameObject));

            var entry = _settings.CreateOrMoveEntry(context.Guid, _groups[0]);
            entry.SetAddress("Existing");
            entry.SetLabel("preexisting", true);
            // guid が実アセットへ解決できず readOnly=true で作られるため、前提を固定するため外す。
            entry.ReadOnly = false;

            var prediction = Predict(rules, context);
            var outcome = RuleInspector.Evaluate(rules, context);

            Assert.AreEqual(RuleOutcomeKind.LabelsOnly, outcome.Kind);
            Assert.AreEqual(KindOf(prediction.Validation.Status), outcome.Kind);
            Assert.AreEqual(PredictedAction.AddOrUpdate, prediction.Action);
            var expected = outcome.Labels.Concat(new[] { "preexisting" }).OrderBy(l => l, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(expected, prediction.PredictedEntry.Labels);
        }

        [TestCaseSource(nameof(Paths))]
        public void Evaluate_AgreesWithDryRunPrediction(string path)
        {
            var rules = RuleInspectorTestRules.Build();
            var context = RuleInspectorTestRules.Ctx(path);

            var prediction = Predict(rules, context);

            var outcome = RuleInspector.Evaluate(rules, context);

            Assert.AreEqual(KindOf(prediction.Validation.Status), outcome.Kind, $"{path}: {prediction.Validation.Message}");
            if (outcome.Kind != RuleOutcomeKind.Address)
                return;

            Assert.AreEqual(prediction.PredictedEntry.Address, outcome.Address, path);
            var predictedGroup = outcome.UsesDefaultGroup ? DefaultGroupName : outcome.GroupName;
            Assert.AreEqual(prediction.PredictedEntry.GroupName, predictedGroup, path);
            CollectionAssert.AreEqual(prediction.PredictedEntry.Labels, outcome.Labels, path);
        }
    }
}

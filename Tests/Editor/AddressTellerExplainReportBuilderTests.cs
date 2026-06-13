using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerExplainReportBuilder.Build / AddressTellerExplainReport.ToJson/FromJson の単体テスト。
    /// AssetExplanation 等は Addressables / AssetDatabase に依存しないため直接構築する。
    /// </summary>
    public class AddressTellerExplainReportBuilderTests
    {
        private static AssetContext Ctx(string path)
            => new AssetContext("guid1", path, typeof(UnityEngine.Object));

        [Test]
        public void Build_ToJson_FromJson_RoundTrip()
        {
            var ctx = Ctx("Assets/Foo.prefab");

            var details = new List<RuleEvaluationDetail>
            {
                new RuleEvaluationDetail(
                    "StubRule > \"matched\"", "StubGroup", "matched",
                    RuleMatchOutcome.Matched, "Foo", new[] { "alpha", "beta" }, null),
                new RuleEvaluationDetail(
                    "StubRule > \"errored\"", "StubGroup", "errored",
                    RuleMatchOutcome.Errored, null, null, "boom"),
                new RuleEvaluationDetail(
                    "StubRule > \"notmatched\"", "OtherGroup", "notmatched",
                    RuleMatchOutcome.NotMatched, null, null, null),
            };

            var resolution = new AddressResolution(
                new[] { new AddressCandidate("StubGroup", "Foo", "StubRule", "matched", 0) },
                new HashSet<string> { "alpha", "beta" });

            var explanation = new RuleExplanation(ctx, details, resolution);
            var validation = new ValidationResult(ctx, ValidationStatus.Ok, "ok");

            var excludedCtx = Ctx("Assets/Editor/Excluded.prefab");
            var excluded = new AssetExplanation(excludedCtx.Path, isExcluded: true, explanation: null, validation: null);
            var included = new AssetExplanation(ctx.Path, isExcluded: false, explanation: explanation, validation: validation);

            var report = AddressTellerExplainReportBuilder.Build(new[] { included, excluded });

            var json = report.ToJson();
            var restored = AddressTellerExplainReport.FromJson(json);

            Assert.AreEqual(2, restored.Assets.Count);

            // Path の Ordinal 順: "Assets/Editor/..." < "Assets/Foo.prefab"
            Assert.AreEqual("Assets/Editor/Excluded.prefab", restored.Assets[0].Path);
            Assert.IsTrue(restored.Assets[0].IsExcluded);
            Assert.AreEqual(string.Empty, restored.Assets[0].ValidationStatus);
            Assert.AreEqual(0, restored.Assets[0].Rules.Count);

            var asset = restored.Assets[1];
            Assert.AreEqual("Assets/Foo.prefab", asset.Path);
            Assert.IsFalse(asset.IsExcluded);
            Assert.AreEqual("Ok", asset.ValidationStatus);
            Assert.AreEqual("ok", asset.ValidationMessage);
            Assert.AreEqual(3, asset.Rules.Count);

            var matched = asset.Rules[0];
            Assert.AreEqual("StubRule > \"matched\"", matched.RuleSource);
            Assert.AreEqual("StubGroup", matched.GroupName);
            Assert.AreEqual("matched", matched.Description);
            Assert.AreEqual("Matched", matched.Outcome);
            Assert.AreEqual("Foo", matched.ProducedAddress);
            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, matched.ProducedLabels);
            Assert.AreEqual(string.Empty, matched.ErrorMessage);

            var errored = asset.Rules[1];
            Assert.AreEqual("Errored", errored.Outcome);
            Assert.AreEqual(string.Empty, errored.ProducedAddress);
            Assert.AreEqual("boom", errored.ErrorMessage);

            var notMatched = asset.Rules[2];
            Assert.AreEqual("NotMatched", notMatched.Outcome);
            Assert.AreEqual(0, notMatched.ProducedLabels.Count);
        }

        [Test]
        public void Build_EmptyList_ReturnsEmptyAssets()
        {
            var report = AddressTellerExplainReportBuilder.Build(Array.Empty<AssetExplanation>());

            var restored = AddressTellerExplainReport.FromJson(report.ToJson());

            Assert.AreEqual(0, restored.Assets.Count);
        }
    }
}

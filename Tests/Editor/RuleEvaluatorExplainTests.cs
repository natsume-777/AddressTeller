using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class RuleEvaluatorExplainTests
    {
        private static AssetContext Ctx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        private static AddressRuleEntry Entry(
            string group,
            System.Func<AssetContext, bool> where = null,
            System.Func<AssetContext, string> address = null,
            string[] labels = null,
            string sourceClass = null,
            string description = null,
            int ruleIndex = 0)
        {
            var labelSelectors = new List<System.Func<AssetContext, string>>();
            if (labels != null)
                foreach (var l in labels)
                {
                    var captured = l;
                    labelSelectors.Add(_ => captured);
                }

            return new AddressRuleEntry(
                group,
                where ?? (_ => true),
                address,
                labelSelectors,
                sourceClass,
                description,
                ruleIndex
            );
        }

        [Test]
        public void SingleMatch_DetailsHaveOneMatchedAndProducedAddress()
        {
            var entries = new[]
            {
                Entry("Characters", address: ctx => ctx.FileNameWithoutExtension),
                Entry("Others", where: _ => false),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Chars/Player.prefab"), entries);

            Assert.AreEqual(2, explanation.Details.Count);
            Assert.AreEqual(RuleMatchOutcome.Matched, explanation.Details[0].Outcome);
            Assert.AreEqual("Player", explanation.Details[0].ProducedAddress);
            Assert.AreEqual(RuleMatchOutcome.NotMatched, explanation.Details[1].Outcome);
        }

        [Test]
        public void NotMatchedRule_DescriptionIsCarriedToDetail()
        {
            var entries = new[]
            {
                Entry("G1", where: _ => false, description: "拡張子がpngのもの"),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual(RuleMatchOutcome.NotMatched, explanation.Details[0].Outcome);
            Assert.AreEqual("拡張子がpngのもの", explanation.Details[0].Description);
        }

        [Test]
        public void NotMatchedRule_WithoutDescription_DescriptionIsNull()
        {
            var entries = new[]
            {
                Entry("G1", where: _ => false),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual(RuleMatchOutcome.NotMatched, explanation.Details[0].Outcome);
            Assert.IsNull(explanation.Details[0].Description);
        }

        [Test]
        public void PredicateThrows_DetailIsErroredWithMessage_NotFallenBackToNotMatched()
        {
            var entries = new[]
            {
                Entry("G1", where: _ => throw new System.FormatException("boom")),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual(RuleMatchOutcome.Errored, explanation.Details[0].Outcome);
            StringAssert.Contains("boom", explanation.Details[0].ErrorMessage);
        }

        [Test]
        public void MultipleMatches_DetailsHaveAllMatchedAndResolutionHasConflict()
        {
            var entries = new[]
            {
                Entry("G1", address: _ => "addr1"),
                Entry("G2", address: _ => "addr2"),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            Assert.IsTrue(explanation.Details.All(d => d.Outcome == RuleMatchOutcome.Matched));
            Assert.AreEqual(2, explanation.Resolution.AddressCandidates.Count);
        }

        [Test]
        public void LabelOnlyRule_Matched_ProducedLabelsContainsLabel()
        {
            var entries = new[]
            {
                Entry("G", labels: new[] { "lbl" }),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual(RuleMatchOutcome.Matched, explanation.Details[0].Outcome);
            CollectionAssert.Contains(explanation.Details[0].ProducedLabels, "lbl");
            CollectionAssert.Contains(explanation.Resolution.Labels, "lbl");
        }

        [Test]
        public void Details_OrderMatchesEntriesOrder()
        {
            var entries = new[]
            {
                Entry("First", address: _ => "addr-first"),
                Entry("Second", where: _ => false),
                Entry("Third", address: _ => "addr-third"),
            };
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual("First", explanation.Details[0].GroupName);
            Assert.AreEqual("Second", explanation.Details[1].GroupName);
            Assert.AreEqual("Third", explanation.Details[2].GroupName);
        }

        [Test]
        public void Explain_ResolutionMatchesEvaluateResult()
        {
            var entries = new[]
            {
                Entry("G1", address: _ => "addr1", labels: new[] { "a" }),
                Entry("G2", where: _ => false, address: _ => "addr2", labels: new[] { "skipped" }),
                Entry("G3", where: _ => throw new System.InvalidOperationException("oops")),
                Entry("G4", labels: new[] { "b" }),
            };

            var evaluated = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), entries);
            var explanation = RuleEvaluator.Explain(Ctx("Assets/Foo.prefab"), entries);

            CollectionAssert.AreEqual(
                evaluated.AddressCandidates.Select(c => (c.GroupName, c.Address)),
                explanation.Resolution.AddressCandidates.Select(c => (c.GroupName, c.Address)));
            CollectionAssert.AreEquivalent(evaluated.Labels, explanation.Resolution.Labels);
            Assert.AreEqual(evaluated.Errors.Count, explanation.Resolution.Errors.Count);
            for (var i = 0; i < evaluated.Errors.Count; i++)
                Assert.AreEqual(evaluated.Errors[i].Message, explanation.Resolution.Errors[i].Message);
        }
    }
}

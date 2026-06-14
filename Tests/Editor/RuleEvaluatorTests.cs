using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class RuleEvaluatorTests
    {
        private static AssetContext Ctx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        private static AddressRuleEntry Entry(
            string group,
            System.Func<AssetContext, bool> where = null,
            System.Func<AssetContext, string> address = null,
            string[] labels = null)
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
                labelSelectors
            );
        }

        [Test]
        public void NoMatch_ReturnEmptyCandidatesAndLabels()
        {
            var entry = Entry("G", where: _ => false, address: _ => "addr");
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), new[] { entry });

            Assert.AreEqual(0, result.AddressCandidates.Count);
            Assert.AreEqual(0, result.Labels.Count);
        }

        [Test]
        public void SingleMatch_ProducesOneCandidate()
        {
            var entry = Entry("Characters", address: ctx => ctx.FileNameWithoutExtension);
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Chars/Player.prefab"), new[] { entry });

            Assert.AreEqual(1, result.AddressCandidates.Count);
            Assert.AreEqual("Characters", result.AddressCandidates[0].GroupName);
            Assert.AreEqual("Player", result.AddressCandidates[0].Address);
        }

        [Test]
        public void MultipleMatchingRules_AccumulateAllCandidates()
        {
            var entries = new[]
            {
                Entry("G1", address: _ => "addr1"),
                Entry("G2", address: _ => "addr2"),
            };
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual(2, result.AddressCandidates.Count);
        }

        [Test]
        public void Labels_AccumulateAcrossAllMatchingRules()
        {
            var entries = new[]
            {
                Entry("G1", address: _ => "addr", labels: new[] { "a", "b" }),
                Entry("G2", address: _ => "addr2", labels: new[] { "b", "c" }),
            };
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), entries);

            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, result.Labels);
        }

        [Test]
        public void LabelOnly_NoCandidateButLabelsCollected()
        {
            var entry = Entry("G", labels: new[] { "lbl" });
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), new[] { entry });

            Assert.AreEqual(0, result.AddressCandidates.Count);
            CollectionAssert.Contains(result.Labels, "lbl");
        }

        [Test]
        public void EvaluationOrder_FollowsEntriesOrder()
        {
            var entries = new[]
            {
                Entry("First", address: _ => "addr-first"),
                Entry("Second", address: _ => "addr-second"),
            };
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual("First", result.AddressCandidates[0].GroupName);
            Assert.AreEqual("Second", result.AddressCandidates[1].GroupName);
        }

        [Test]
        public void SkippedRule_DoesNotContributeLabels()
        {
            var entries = new[]
            {
                Entry("G1", where: _ => false, labels: new[] { "skipped" }),
                Entry("G2", labels: new[] { "included" }),
            };
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), entries);

            CollectionAssert.DoesNotContain(result.Labels, "skipped");
            CollectionAssert.Contains(result.Labels, "included");
        }

        [Test]
        public void PredicateThrows_RecordsErrorAndContinuesOtherEntries()
        {
            var entries = new[]
            {
                Entry("G1", where: _ => throw new System.FormatException("boom"), address: _ => "addr1"),
                Entry("G2", address: _ => "addr2"),
            };
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), entries);

            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("boom", result.Errors[0].Message);
            Assert.AreEqual(1, result.AddressCandidates.Count);
            Assert.AreEqual("G2", result.AddressCandidates[0].GroupName);
        }

        [Test]
        public void AddressSelectorThrows_RecordsErrorWithoutCandidate()
        {
            var entry = Entry("G1", address: _ => throw new System.InvalidOperationException("bad address"));
            var result = RuleEvaluator.Evaluate(Ctx("Assets/Foo.prefab"), new[] { entry });

            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("bad address", result.Errors[0].Message);
            Assert.AreEqual(0, result.AddressCandidates.Count);
        }
    }
}

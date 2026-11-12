using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// WinnerSelector（勝者決定と最終ラベル集合の合成）。Addressables に依存しない Core の純粋ロジックを検証する。
    /// </summary>
    public class WinnerSelectorTests
    {
        private static AddressResolution Resolution(
            AddressCandidate[] candidates,
            string[] labelOnly = null,
            RuleEvaluationError[] errors = null) =>
            new AddressResolution(candidates, new HashSet<string>(labelOnly ?? Array.Empty<string>()), errors);

        [Test]
        public void Select_NoCandidates_ReturnsNoCandidates()
        {
            var selection = WinnerSelector.Select(Resolution(Array.Empty<AddressCandidate>(), new[] { "x" }));

            Assert.AreEqual(WinnerSelectionOutcome.NoCandidates, selection.Outcome);
        }

        [Test]
        public void Select_LowestOrderWins()
        {
            var selection = WinnerSelector.Select(Resolution(new[]
            {
                new AddressCandidate("G1", "broad", order: 100),
                new AddressCandidate("G2", "narrow", order: 0),
                new AddressCandidate("G3", "mid", order: 50),
            }));

            Assert.AreEqual(WinnerSelectionOutcome.Won, selection.Outcome);
            Assert.AreEqual("narrow", selection.Winner.Address);
        }

        [Test]
        public void Select_TieAtLowestOrder_ReturnsOnlyTiedCandidatesInEvaluationOrder()
        {
            var selection = WinnerSelector.Select(Resolution(new[]
            {
                new AddressCandidate("G1", "a", order: 0),
                new AddressCandidate("G2", "loser", order: 10),
                new AddressCandidate("G3", "b", order: 0),
            }));

            Assert.AreEqual(WinnerSelectionOutcome.Tied, selection.Outcome);
            CollectionAssert.AreEqual(new[] { "a", "b" }, new[] { selection.TiedCandidates[0].Address, selection.TiedCandidates[1].Address });
            Assert.AreEqual(2, selection.TiedCandidates.Count);
        }

        [Test]
        public void Select_ErrorFromEqualOrHigherPriorityAddressRule_Blocks()
        {
            var candidates = new[] { new AddressCandidate("G1", "addr", order: 5) };

            var sameOrder = WinnerSelector.Select(Resolution(candidates,
                errors: new[] { new RuleEvaluationError("R", "boom", order: 5, canProduceAddress: true) }));
            var higher = WinnerSelector.Select(Resolution(candidates,
                errors: new[] { new RuleEvaluationError("R", "boom", order: 0, canProduceAddress: true) }));

            Assert.AreEqual(WinnerSelectionOutcome.Blocked, sameOrder.Outcome);
            Assert.AreEqual(WinnerSelectionOutcome.Blocked, higher.Outcome);
            Assert.AreEqual(1, higher.BlockingErrors.Count);
        }

        [Test]
        public void Select_ErrorFromLowerPriorityOrLabelOnlyRule_DoesNotBlock()
        {
            var candidates = new[] { new AddressCandidate("G1", "addr", order: 5) };

            var lower = WinnerSelector.Select(Resolution(candidates,
                errors: new[] { new RuleEvaluationError("R", "boom", order: 9, canProduceAddress: true) }));
            var labelOnly = WinnerSelector.Select(Resolution(candidates,
                errors: new[] { new RuleEvaluationError("R", "boom", order: 0, canProduceAddress: false) }));

            Assert.AreEqual(WinnerSelectionOutcome.Won, lower.Outcome);
            Assert.AreEqual(WinnerSelectionOutcome.Won, labelOnly.Outcome);
        }

        [Test]
        public void ComposeFinalLabels_UnionOfWinnerAndLabelOnly_ExcludesLoserLabels()
        {
            var winner = new AddressCandidate("G2", "narrow", order: 0, labels: new[] { "narrowLabel", "shared" });
            var resolution = Resolution(
                new[] { new AddressCandidate("G1", "broad", order: 100, labels: new[] { "broadLabel" }), winner },
                labelOnly: new[] { "always", "shared" });

            var labels = WinnerSelector.ComposeFinalLabels(resolution, winner);

            CollectionAssert.AreEquivalent(new[] { "narrowLabel", "shared", "always" }, labels);
        }

        [Test]
        public void ComposeFinalLabels_NoWinner_ReturnsLabelOnlyLabels()
        {
            var resolution = Resolution(
                new[] { new AddressCandidate("G1", "a", order: 0, labels: new[] { "candidateLabel" }) },
                labelOnly: new[] { "always" });

            var labels = WinnerSelector.ComposeFinalLabels(resolution, null);

            CollectionAssert.AreEquivalent(new[] { "always" }, labels);
        }

        [Test]
        public void ComposeFinalLabels_WinnerWithoutLabels_ReturnsLabelOnlyLabels()
        {
            var winner = new AddressCandidate("G1", "a", order: 0);
            var resolution = Resolution(new[] { winner }, labelOnly: new[] { "always" });

            CollectionAssert.AreEquivalent(new[] { "always" }, WinnerSelector.ComposeFinalLabels(resolution, winner));
        }

        [Test]
        public void ComposeFinalLabels_NoLabelOnlyLabels_ReturnsWinnerLabels()
        {
            var winner = new AddressCandidate("G1", "a", order: 0, labels: new[] { "w" });
            var resolution = Resolution(new[] { winner });

            CollectionAssert.AreEquivalent(new[] { "w" }, WinnerSelector.ComposeFinalLabels(resolution, winner));
        }
    }
}

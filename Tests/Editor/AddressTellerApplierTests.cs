using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class AddressTellerApplierTests
    {
        private static AssetContext Ctx(string path = "Assets/Foo.prefab") =>
            new AssetContext("guid1", path, typeof(GameObject));

        private static AddressResolution Resolution(params AddressCandidate[] candidates) =>
            new AddressResolution(candidates, new HashSet<string>());

        private static AddressResolution ResolutionWithLabels(AddressCandidate candidate, params string[] labels) =>
            new AddressResolution(new[] { candidate }, new HashSet<string>(labels));

        private static AddressResolution ResolutionWithErrors(IReadOnlyList<AddressCandidate> candidates, params RuleEvaluationError[] errors) =>
            new AddressResolution(candidates, new HashSet<string>(), errors);

        [Test]
        public void NoCandidates_ReturnsSkipped()
        {
            var result = AddressTellerApplier.Validate(Ctx(), Resolution(), new[] { "G" });

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
        }

        [Test]
        public void TwoCandidates_ReturnsConflict()
        {
            var resolution = Resolution(
                new AddressCandidate("G1", "addr1"),
                new AddressCandidate("G2", "addr2"));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1", "G2" });

            Assert.AreEqual(ValidationStatus.ConflictingAddress, result.Status);
            Assert.AreEqual(2, result.ConflictingCandidates.Count);
        }

        [Test]
        public void GroupNotFound_ReturnsGroupNotFound()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr"));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "Existing" });

            Assert.AreEqual(ValidationStatus.GroupNotFound, result.Status);
            StringAssert.Contains("Missing", result.Message);
        }

        [Test]
        public void ValidCandidate_ReturnsOk()
        {
            var resolution = Resolution(new AddressCandidate("Characters", "Player"));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "Characters" });

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
        }

        [Test]
        public void Conflict_MessageContainsPath()
        {
            var resolution = Resolution(
                new AddressCandidate("G1", "addr1"),
                new AddressCandidate("G2", "addr2"));

            var result = AddressTellerApplier.Validate(Ctx("Assets/Game/Player.prefab"), resolution, new[] { "G1", "G2" });

            StringAssert.Contains("Assets/Game/Player.prefab", result.Message);
        }

        [Test]
        public void Conflict_MessageUsesOriginalRuleIndex()
        {
            // 5 ルール中、定義順 1 番目(index=1)と 4 番目(index=4)が衝突したケースを想定
            var resolution = Resolution(
                new AddressCandidate("G1", "addr1", sourceClass: "MyRule", ruleIndex: 1),
                new AddressCandidate("G2", "addr2", sourceClass: "MyRule", ruleIndex: 4));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1", "G2" });

            StringAssert.Contains("MyRule[1]", result.Message);
            StringAssert.Contains("MyRule[4]", result.Message);
        }

        [Test]
        public void NullAddress_ReturnsInvalidAddress()
        {
            var resolution = Resolution(new AddressCandidate("G1", null));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" });

            Assert.AreEqual(ValidationStatus.InvalidAddress, result.Status);
        }

        [Test]
        public void EmptyAddress_ReturnsInvalidAddress()
        {
            var resolution = Resolution(new AddressCandidate("G1", ""));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" });

            Assert.AreEqual(ValidationStatus.InvalidAddress, result.Status);
        }

        // --- Order による優先順位判定 ---

        [Test]
        public void SpecificOrderWins_BroadRuleOrderIsIgnored()
        {
            // 広いルール(Order=100, グループ不在)を先頭(index 0)に、特定ルール(Order=0, グループ実在)を
            // 2番目に置く。Order 最小の特定ルールが勝つため、広いルールの不在グループは無視され Ok になる。
            // もし誤って index 0 を採用する実装だった場合は GroupNotFound になり、このテストは失敗する。
            var resolution = Resolution(
                new AddressCandidate("BroadGroupMissing", "broadAddr", order: 100),
                new AddressCandidate("Specific", "specificAddr", order: 0));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "Specific" });

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
        }

        [Test]
        public void ThreeCandidates_NotAllTied_LowestOrderWins()
        {
            // Order = 10, 5, 20 の3候補。最小の 5 が単独のため採用され、そのグループ(G5)だけが
            // existingGroupNames に含まれていれば Ok になる。
            var resolution = Resolution(
                new AddressCandidate("G10", "addr10", order: 10),
                new AddressCandidate("G5", "addr5", order: 5),
                new AddressCandidate("G20", "addr20", order: 20));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G5" });

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
        }

        [Test]
        public void TwoCandidates_SameNonZeroOrder_ReturnsConflict()
        {
            var resolution = Resolution(
                new AddressCandidate("G1", "addr1", order: 5),
                new AddressCandidate("G2", "addr2", order: 5));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1", "G2" });

            Assert.AreEqual(ValidationStatus.ConflictingAddress, result.Status);
            Assert.AreEqual(2, result.ConflictingCandidates.Count);
        }

        [Test]
        public void Conflict_MessageOnlyListsTiedCandidates_NotHigherOrderLosers()
        {
            // G1/G2 は Order=0 で同点競合。G3 は Order=10 の同点でない敗者なので、
            // 衝突メッセージにも ConflictingCandidates にも出てはならない。
            var resolution = Resolution(
                new AddressCandidate("G1", "addr1", sourceClass: "RuleA", order: 0),
                new AddressCandidate("G2", "addr2", sourceClass: "RuleB", order: 0),
                new AddressCandidate("G3", "addr3", sourceClass: "RuleC", order: 10));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1", "G2", "G3" });

            Assert.AreEqual(ValidationStatus.ConflictingAddress, result.Status);
            Assert.AreEqual(2, result.ConflictingCandidates.Count);
            StringAssert.Contains("RuleA", result.Message);
            StringAssert.Contains("RuleB", result.Message);
            StringAssert.DoesNotContain("RuleC", result.Message);
        }

        // --- Validate(..., out AddressCandidate winner) の内部オーバーロード契約 ---
        //
        // Apply/Predict はこのオーバーロードが返す winner をそのまま使い、自分では選び直さない
        // （AddressTellerApplier.cs 参照）。この契約自体をここで固定する。

        [Test]
        public void ValidateOutWinner_UniqueWinner_ReturnsWinningCandidate()
        {
            var resolution = Resolution(
                new AddressCandidate("Broad", "broadAddr", order: 100),
                new AddressCandidate("Specific", "specificAddr", order: 0));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "Specific" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
            Assert.AreEqual("Specific", winner.GroupName);
            Assert.AreEqual("specificAddr", winner.Address);
        }

        [Test]
        public void ValidateOutWinner_GroupWillBeCreated_ReturnsWinningCandidate()
        {
            var resolution = Resolution(new AddressCandidate("Missing", "addr", order: 0));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new string[0], autoCreateMissingGroups: true, out var winner);

            Assert.AreEqual(ValidationStatus.GroupWillBeCreated, result.Status);
            Assert.AreEqual("Missing", winner.GroupName);
            Assert.AreEqual("addr", winner.Address);
        }

        [Test]
        public void ValidateOutWinner_Conflict_WinnerIsDefault()
        {
            // 同点競合時は勝者を一意に選べないため、winner は default(AddressCandidate) のまま返る
            // （呼び出し側はこの場合 result.IsOk が false であることを見て winner を読まない前提）。
            var resolution = Resolution(
                new AddressCandidate("G1", "addr1", order: 0),
                new AddressCandidate("G2", "addr2", order: 0));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1", "G2" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.ConflictingAddress, result.Status);
            Assert.IsNull(winner.GroupName);
            Assert.IsNull(winner.Address);
        }

        [Test]
        public void ValidateOutWinner_Skipped_WinnerIsDefault()
        {
            var result = AddressTellerApplier.Validate(Ctx(), Resolution(), new[] { "G" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.Skipped, result.Status);
            Assert.IsNull(winner.GroupName);
            Assert.IsNull(winner.Address);
        }

        // --- BlockedByRuleError ---
        //
        // 優先度の高い(Orderが小さい/同点な)アドレス産出ルールが例外を投げた場合、たまたま残った
        // 低優先ルールの勝者を黙って書き込まず、BlockedByRuleError で書き込みを見送る。

        [Test]
        public void HigherPriorityAddressRuleErrors_BlocksLowerPriorityWinner()
        {
            // Order=0 のルールが例外を投げ候補を出さなかったため、Order=5 の候補だけが残り勝者に見えるが、
            // Order=0 のエラーが勝者以下なのでブロックされる。
            var resolution = ResolutionWithErrors(
                new[] { new AddressCandidate("G5", "addr5", order: 5) },
                new RuleEvaluationError("HighPriorityRule", "boom", order: 0, canProduceAddress: true));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G5" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, result.Status);
            Assert.IsTrue(result.IsBlocking);
            Assert.IsFalse(result.IsOk);
            Assert.IsNull(winner.GroupName);
            StringAssert.Contains("HighPriorityRule", result.Message);
        }

        [Test]
        public void ErrorAtSameOrderAsWinner_BlocksWinner()
        {
            // 同点(Order 一致)も「勝者以下」に含まれるためブロックされる。
            var resolution = ResolutionWithErrors(
                new[] { new AddressCandidate("G1", "addr1", order: 3) },
                new RuleEvaluationError("SameOrderRule", "boom", order: 3, canProduceAddress: true));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, result.Status);
        }

        [Test]
        public void ErrorAtLowerPriorityThanWinner_DoesNotBlock()
        {
            // Order がより大きい(優先度が低い)ルールの例外は、勝者の書き込みに影響しない。
            var resolution = ResolutionWithErrors(
                new[] { new AddressCandidate("G1", "addr1", order: 0) },
                new RuleEvaluationError("LowPriorityRule", "boom", order: 10, canProduceAddress: true));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
            Assert.AreEqual("addr1", winner.Address);
        }

        [Test]
        public void LabelOnlyRuleErrors_AtOrLowerOrderThanWinner_DoesNotBlock()
        {
            // ラベル専用ルール(CanProduceAddress=false)の例外は、Order が勝者以下でも書き込みを止めない。
            var resolution = ResolutionWithErrors(
                new[] { new AddressCandidate("G1", "addr1", order: 5) },
                new RuleEvaluationError("LabelOnlyRule", "boom", order: 0, canProduceAddress: false));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.Ok, result.Status);
            Assert.AreEqual("addr1", winner.Address);
        }

        [Test]
        public void BlockedByRuleError_WithAutoCreateMissingGroups_StillBlocks()
        {
            // GroupWillBeCreated になるはずだった場合でも、より優先度の高いルールの例外があれば
            // グループ作成予定にすらせずブロックする（Apply 側で実際にグループを作らせないため）。
            var resolution = ResolutionWithErrors(
                new[] { new AddressCandidate("Missing", "addr", order: 5) },
                new RuleEvaluationError("HighPriorityRule", "boom", order: 0, canProduceAddress: true));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new string[0], autoCreateMissingGroups: true, out var winner);

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, result.Status);
        }

        [Test]
        public void SameRuleThrowsAfterProducingWinningCandidate_BlocksItself()
        {
            // AddressSelector は成功して勝者候補を出したが、同じルールチェーン上の後続 LabelSelector が例外を
            // 投げたケース。RuleEvaluator.Evaluate の仕様上、そのルールは AddressCandidates と Errors の両方に
            // 同時に現れる（RuleEvaluatorTests.LabelSelectorThrows_AfterAddressProduced_CandidateAndErrorBothRecorded
            // 参照）。この場合、勝者と例外を出したルールが同一であっても Order は一致する（同点扱い）ため
            // ブロックされる——「自分自身の評価が完走しなかった」ことに変わりはないため。
            var entry = new AddressRuleEntry(
                "G1", _ => true, _ => "addr1",
                new List<System.Func<AssetContext, string>> { _ => throw new System.InvalidOperationException("label boom") },
                sourceClass: "SelfBlockingRule", description: null, ruleIndex: 0, includesFolders: false, order: 2);

            var resolution = RuleEvaluator.Evaluate(Ctx(), new[] { entry });

            Assert.AreEqual(1, resolution.AddressCandidates.Count);
            Assert.AreEqual(1, resolution.Errors.Count);

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, result.Status);
            StringAssert.Contains("SelfBlockingRule", result.Message);
        }

        [Test]
        public void BlockedByRuleError_MessageDoesNotRepeatExceptionBody_OnlyNamesTheBlockingRules()
        {
            // 例外本文（Message）はこの結果では再掲しない——同じ例外がこの直前に RuleError として issues に
            // 積まれ、本文はそちらに載る（AddressTellerService/RuleEvaluationPipeline.AddRuleErrors 参照）。
            // ここではどのルールがブロックの原因になったかだけを示す。
            var resolution = ResolutionWithErrors(
                new[] { new AddressCandidate("G1", "addr1", order: 3) },
                new RuleEvaluationError("BlockingRule", "a very specific exception message that should not repeat", order: 3, canProduceAddress: true));

            var result = AddressTellerApplier.Validate(Ctx(), resolution, new[] { "G1" }, autoCreateMissingGroups: false, out var winner);

            Assert.AreEqual(ValidationStatus.BlockedByRuleError, result.Status);
            StringAssert.Contains("BlockingRule", result.Message);
            StringAssert.DoesNotContain("a very specific exception message that should not repeat", result.Message);
        }
    }
}

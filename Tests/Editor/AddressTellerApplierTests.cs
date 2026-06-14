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
    }
}

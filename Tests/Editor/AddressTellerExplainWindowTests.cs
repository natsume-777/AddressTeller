using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class AddressTellerExplainWindowTests
    {
        private static AssetContext Ctx() =>
            new AssetContext("guid1", "Assets/Foo.prefab", typeof(GameObject));

        private static AddressResolution Resolution(
            IReadOnlyList<AddressCandidate> candidates = null,
            IReadOnlyCollection<string> labels = null,
            IReadOnlyList<RuleEvaluationError> errors = null) =>
            new AddressResolution(
                candidates ?? Array.Empty<AddressCandidate>(),
                labels ?? Array.Empty<string>(),
                errors);

        [Test]
        public void RuleErrorAndSkipped_ConclusionMentionsRuleErrors_NotSkipped()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.Skipped, null);
            var resolution = Resolution(errors: new[]
            {
                new RuleEvaluationError("SomeRule", "boom"),
            });

            var (text, color) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("1件のルールがエラー", text);
            StringAssert.Contains("SomeRule", text);
            Assert.AreEqual(Color.red, color);
        }

        [Test]
        public void NoRuleErrors_Skipped_ConclusionIsUnchanged()
        {
            var validation = new ValidationResult(Ctx(), ValidationStatus.Skipped, null);
            var resolution = Resolution();

            var (text, color) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("マッチするルールなし", text);
            Assert.AreEqual(Color.gray, color);
        }

        [Test]
        public void RuleErrors_ButStatusOk_ConclusionShowsOkNotRuleError()
        {
            // 他のルールがマッチしてアドレスが確定している場合は、Ok 表示を優先する
            // （M-1としてスコープ外、RuleError優先表示はSkipped時のみ）。
            var validation = new ValidationResult(Ctx(), ValidationStatus.Ok, null);
            var candidates = new[] { new AddressCandidate("Group", "addr") };
            var resolution = Resolution(candidates: candidates, errors: new[]
            {
                new RuleEvaluationError("SomeRule", "boom"),
            });

            var (text, color) = AddressTellerExplainWindow.DescribeConclusion(validation, resolution);

            StringAssert.Contains("addr", text);
            Assert.AreEqual(Color.green, color);
        }
    }
}

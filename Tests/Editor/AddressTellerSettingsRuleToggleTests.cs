using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings のルールクラス単位 On/Off 切り替え（IsRuleEnabled/SetRuleEnabled）を検証する。
    /// ProjectSettings/AddressTellerSettings.asset への永続化は行われるため、テスト前後で状態を復元する。
    /// </summary>
    public class AddressTellerSettingsRuleToggleTests
    {
        private const string DummyClassName = "AddressTeller.Editor.Tests.DummyRuleForToggleTest";

        private List<string> _originalDisabled;

        [SetUp]
        public void SetUp()
        {
            _originalDisabled = AddressTellerSettings.DisabledRuleClassNames.ToList();
        }

        [TearDown]
        public void TearDown()
        {
            // テストで追加・削除した分を元の状態に戻す。
            foreach (var name in AddressTellerSettings.DisabledRuleClassNames.ToList())
                if (!_originalDisabled.Contains(name))
                    AddressTellerSettings.SetRuleEnabled(name, true);

            foreach (var name in _originalDisabled)
                if (!AddressTellerSettings.DisabledRuleClassNames.Contains(name))
                    AddressTellerSettings.SetRuleEnabled(name, false);
        }

        [Test]
        public void IsRuleEnabled_NotInDisabledList_ReturnsTrueByDefault()
        {
            Assert.IsTrue(AddressTellerSettings.IsRuleEnabled(DummyClassName));
        }

        [Test]
        public void SetRuleEnabled_False_MakesIsRuleEnabledFalse()
        {
            AddressTellerSettings.SetRuleEnabled(DummyClassName, false);

            Assert.IsFalse(AddressTellerSettings.IsRuleEnabled(DummyClassName));
            Assert.Contains(DummyClassName, AddressTellerSettings.DisabledRuleClassNames.ToList());
        }

        [Test]
        public void SetRuleEnabled_FalseThenTrue_ReturnsToEnabled()
        {
            AddressTellerSettings.SetRuleEnabled(DummyClassName, false);
            AddressTellerSettings.SetRuleEnabled(DummyClassName, true);

            Assert.IsTrue(AddressTellerSettings.IsRuleEnabled(DummyClassName));
            CollectionAssert.DoesNotContain(AddressTellerSettings.DisabledRuleClassNames.ToList(), DummyClassName);
        }

        [Test]
        public void DisabledRuleClassNames_ReturnsDefensiveCopy_NotSameInstanceAcrossCalls()
        {
            // 要素が空の場合は Array.Empty<T> 相当の共有インスタンスが返ることがあるが、
            // 空配列は変更のしようがなく安全性には影響しないため、ここでは要素を1件以上持たせた上で検証する。
            // 内部リストの実体を露出していると、毎回同じインスタンスが返る（AreSame になる）はず。
            // 防御的コピーであれば、呼び出しのたびに別インスタンスが返る。
            AddressTellerSettings.SetRuleEnabled(DummyClassName, false);

            var first = AddressTellerSettings.DisabledRuleClassNames;
            var second = AddressTellerSettings.DisabledRuleClassNames;

            Assert.AreNotSame(first, second);
        }
    }
}

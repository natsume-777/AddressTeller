using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings.AutoSnapshotRetention の最小値クランプ（Mathf.Max(1, value)）を検証する。
    /// Project Settings の IntegerField はセッター呼び出し後にこの値を読み戻して表示に反映するため、
    /// クランプがセッター側で正しく行われることが UI 表示の正しさの前提になる。
    /// </summary>
    public class AddressTellerSettingsAutoSnapshotRetentionTests
    {
        private int _original;

        [SetUp]
        public void SetUp()
        {
            _original = AddressTellerSettings.AutoSnapshotRetention;
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.AutoSnapshotRetention = _original;
        }

        [TestCase(0, 1)]
        [TestCase(-5, 1)]
        [TestCase(1, 1)]
        [TestCase(10, 10)]
        public void AutoSnapshotRetention_ClampsToMinimumOfOne(int input, int expected)
        {
            AddressTellerSettings.AutoSnapshotRetention = input;

            Assert.AreEqual(expected, AddressTellerSettings.AutoSnapshotRetention);
        }
    }
}

using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings.PostprocessOrder の既定値フォールバックとカスタム値の読み書き、
    /// AddressTellerPostprocessor.GetPostprocessOrder() への反映を検証する。
    /// ProjectSettings/AddressTellerSettings.asset への永続化は行われるため、テスト前後で状態を復元する。
    /// </summary>
    public class AddressTellerSettingsPostprocessOrderTests
    {
        private int _original;

        [SetUp]
        public void SetUp()
        {
            _original = AddressTellerSettings.PostprocessOrder;
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.PostprocessOrder = _original;
        }

        [Test]
        public void PostprocessOrder_Default_IsOneThousand()
        {
            // 0（未設定）を明示的に書き込み、既定値へフォールバックすることを確認する。
            AddressTellerSettings.PostprocessOrder = 0;

            Assert.AreEqual(1000, AddressTellerSettings.PostprocessOrder);
            Assert.AreEqual(1000, AddressTellerSettings.DefaultPostprocessOrder);
        }

        [Test]
        public void PostprocessOrder_CustomValue_IsReadBack()
        {
            AddressTellerSettings.PostprocessOrder = 500;

            Assert.AreEqual(500, AddressTellerSettings.PostprocessOrder);
        }

        [Test]
        public void GetPostprocessOrder_ReflectsSettingsValue()
        {
            AddressTellerSettings.PostprocessOrder = 250;

            var postprocessor = new AddressTellerPostprocessor();
            Assert.AreEqual(250, postprocessor.GetPostprocessOrder());
        }
    }
}

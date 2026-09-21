using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings.PostprocessOrder の既定値フォールバックとカスタム値の読み書き、
    /// AddressTellerPostprocessor.GetPostprocessOrder() への反映を検証する。
    /// ProjectSettings/AddressTellerSettings.json への永続化は行われるため、テスト前後で状態を復元する。
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
        public void PostprocessOrder_ExplicitZero_IsReadBackAsZero()
        {
            // センチネル(0=未設定)は廃止されているため、明示的に0を設定した場合はそのまま0が返る
            // （キー不在時に既定値1000へフォールバックするケースは AddressTellerSettingsPersistenceTests 側で検証）。
            AddressTellerSettings.PostprocessOrder = 0;

            Assert.AreEqual(0, AddressTellerSettings.PostprocessOrder);
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

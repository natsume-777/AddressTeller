using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.TestTools;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressableAssetSettings が存在しない環境でメニューが安全に失敗することを確認する。
    /// </summary>
    public class AddressTellerMenuTests
    {
        private bool _hadSettings;

        [SetUp]
        public void SetUp()
        {
            _hadSettings = AddressableAssetSettingsDefaultObject.Settings != null;
        }

        [Test]
        public void ApplyAll_WhenSettingsNull_LogsError()
        {
            if (_hadSettings) Assert.Ignore("Addressable Settings が存在するため、このテストはスキップします。");

            LogAssert.Expect(LogType.Error, new Regex("AddressableAssetSettings"));
            AddressTellerMenu.ApplyAll();
        }

        [Test]
        public void Validate_WhenSettingsNull_LogsError()
        {
            if (_hadSettings) Assert.Ignore("Addressable Settings が存在するため、このテストはスキップします。");

            LogAssert.Expect(LogType.Error, new Regex("AddressableAssetSettings"));
            AddressTellerMenu.Validate();
        }
    }
}

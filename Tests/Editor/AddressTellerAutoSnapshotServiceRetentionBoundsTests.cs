using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 設定ファイルから下限未満の AutoSnapshotRetention を読み込んだ状態のまま
    /// <see cref="AddressTellerAutoSnapshotService.CaptureAndSave"/> を呼んだ場合の挙動を検証する。
    /// 正規化（<see cref="AddressTellerSettingsAsset.NormalizeAutoSnapshotRetention"/>）が働かなければ、
    /// Rotate(0) は保存直後のファイル自身まで削除してしまう
    /// （<see cref="AddressTellerAutoSnapshotServiceTests"/> の他の Rotate 系テストとは別に、
    /// 設定ファイル経由の下限未満という入力に絞って確認する）。
    /// </summary>
    public class AddressTellerAutoSnapshotServiceRetentionBoundsTests
    {
        private string _originalSettingsOverride;
        private string _settingsTempPath;
        private string _originalSnapshotFolder;
        private int _originalRetention;
        private string _tempSnapshotRoot;
        private AddressableAssetSettings _addressablesSettings;

        [SetUp]
        public void SetUp()
        {
            _originalSettingsOverride = AddressTellerSettingsAsset.FilePathOverride;
            _settingsTempPath = Path.Combine(Path.GetTempPath(), $"AddressTellerAutoSnapshotServiceRetentionBoundsTests_{Guid.NewGuid():N}.json");
            AddressTellerSettingsAsset.FilePathOverride = _settingsTempPath;
            AddressTellerSettingsAsset.ResetInMemoryState();

            _originalSnapshotFolder = AddressTellerSettings.SnapshotFolder;
            _originalRetention = AddressTellerSettings.AutoSnapshotRetention;

            _tempSnapshotRoot = Path.Combine(Path.GetTempPath(), "AddressTellerAutoSnapshotServiceRetentionBoundsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempSnapshotRoot);
            AddressTellerSettings.SnapshotFolder = _tempSnapshotRoot;

            _addressablesSettings = AddressableAssetSettings.Create(
                "Assets/_AddressTellerAutoSnapshotServiceRetentionBoundsTestConfig", "AddressTellerAutoSnapshotServiceRetentionBoundsTestSettings", false, false);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettingsAsset.FilePathOverride = _originalSettingsOverride;
            AddressTellerSettingsAsset.ResetInMemoryState();
            if (_settingsTempPath != null && File.Exists(_settingsTempPath))
                File.Delete(_settingsTempPath);

            AddressTellerSettings.SnapshotFolder = _originalSnapshotFolder;
            AddressTellerSettings.AutoSnapshotRetention = _originalRetention;

            UnityEngine.Object.DestroyImmediate(_addressablesSettings, true);

            if (Directory.Exists(_tempSnapshotRoot))
                Directory.Delete(_tempSnapshotRoot, true);
        }

        [Test]
        public void CaptureAndSave_AfterLoadingRetentionZero_KeepsTheJustSavedFile()
        {
            // _snapshotFolder を含めず JSON を書くと、JsonUtility.FromJson がそのフィールドを既定値
            // "AddressTellerSnapshots" のまま残し、EnsureLoaded 後の CaptureAndSave がプロジェクト直下の
            // AddressTellerSnapshots/Auto（本番相当のパス）に書き込んでしまう——このテストが検証したいのは
            // retention の正規化であって SnapshotFolder ではないため、一時フォルダも明示的に含めて書く。
            File.WriteAllText(_settingsTempPath,
                $"{{\"_marker\": \"{AddressTellerSettingsAsset.MarkerValue}\", " +
                $"\"_snapshotFolder\": \"{_tempSnapshotRoot.Replace("\\", "\\\\")}\", " +
                "\"_autoSnapshotRetention\": 0}");

            LogAssert.Expect(LogType.Warning, new Regex("AutoSnapshotRetention"));
            Assert.IsTrue(AddressTellerSettings.EnsureLoaded());
            Assert.AreEqual(1, AddressTellerSettings.AutoSnapshotRetention);
            Assert.AreEqual(_tempSnapshotRoot, AddressTellerSettings.SnapshotFolder);

            var path = AddressTellerAutoSnapshotService.CaptureAndSave(_addressablesSettings);

            Assert.IsNotNull(path);
            // 保存先が一時フォルダ配下であることを確認し、本番相当のパスへ書いていないことを保証する。
            StringAssert.StartsWith(Path.GetFullPath(_tempSnapshotRoot), Path.GetFullPath(path));
            Assert.IsTrue(File.Exists(path),
                "正規化前の値(0)のまま Rotate されていれば、保存直後のこのファイル自身も削除されてしまう。");
        }
    }
}

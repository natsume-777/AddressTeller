using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// 公開サービス入口（<see cref="AddressTellerService.ApplyAll(System.Collections.Generic.IEnumerable{string}, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, IProgressReporter, System.Collections.Generic.IReadOnlyList{AddressRuleBase})"/> 等の中核オーバーロード）と
    /// <see cref="AddressTellerSnapshotService.BuildPredictedSnapshot(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, System.Collections.Generic.IEnumerable{string}, System.Collections.Generic.IReadOnlyList{AddressRuleBase})"/>
    /// が設定ゲートを通しているかを検証する。<see cref="AddressTellerSettingsAsset.FilePathOverride"/>（テスト用シーム）で
    /// 一時ファイルへ差し替えるため、本番の設定ファイルには一切触れない。
    /// これらのメソッドはログを出さないゲート（<see cref="AddressTellerSettingsAsset.EnsureLoaded"/>）を使う契約
    /// （<see cref="AddressTellerSettings.EnsureLoaded"/> の remarks 参照）のため、ここでは「ログが出ないこと」も
    /// 合わせて検証する。削除系 API（<see cref="AddressTellerService.RemoveEntriesForDeletedAssets(System.Collections.Generic.IEnumerable{string}, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, System.Collections.Generic.IReadOnlyList{AddressRuleBase})"/>）
    /// のみ例外的にログを出す契約のため、そちらは逆にログが出ることを検証する。
    /// </summary>
    public class AddressTellerServiceSettingsGateTests
    {
        private string _originalOverride;
        private string _tempPath;

        [SetUp]
        public void SetUp()
        {
            // AddressTellerAddressablesPollutionGuard（[SetUpFixture]）が退避済みの値を必ず覚えておき、
            // TearDown で元へ戻す（AddressTellerSettingsPersistenceTests と同じ考え方）。
            _originalOverride = AddressTellerSettingsAsset.FilePathOverride;
            _tempPath = Path.Combine(Path.GetTempPath(), $"AddressTellerServiceSettingsGateTests_{Guid.NewGuid():N}.json");
            AddressTellerSettingsAsset.FilePathOverride = _tempPath;
            AddressTellerSettingsAsset.ResetInMemoryState();

            // マーカーなし＝壊れたファイルとして扱われ、EnsureLoaded は常に失敗する。
            File.WriteAllText(_tempPath, "{}");
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettingsAsset.FilePathOverride = _originalOverride;
            AddressTellerSettingsAsset.ResetInMemoryState();

            if (_tempPath != null && File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        [Test]
        public void ApplyAll_SettingsFileUnreadable_ReturnsSettingsUnavailableOnly_NoLog()
        {
            var results = AddressTellerService.ApplyAll(
                Array.Empty<string>(), null, NullProgressReporter.Instance, Array.Empty<AddressRuleBase>());

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.SettingsUnavailable, results[0].Status);
            Assert.IsFalse(results[0].IsOk);
            Assert.IsTrue(results[0].IsBlocking);
            Assert.IsNull(results[0].Context);

            // サービス層はログを出さない契約（ログは呼び出し元の責務）。
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ValidateAll_SettingsFileUnreadable_ReturnsSettingsUnavailableOnly_NoLog()
        {
            var results = AddressTellerService.ValidateAll(
                null, NullProgressReporter.Instance, Array.Empty<AddressRuleBase>());

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.SettingsUnavailable, results[0].Status);
            Assert.IsFalse(results[0].IsOk);
            Assert.IsTrue(results[0].IsBlocking);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_SettingsFileUnreadable_ReturnsEmptyList_LogsError()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("does not look like an AddressTeller settings file"));

            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(
                Array.Empty<string>(), null, Array.Empty<AddressRuleBase>());

            Assert.AreEqual(0, cleared.Count);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BuildPredictedSnapshot_SettingsFileUnreadable_ReturnsSettingsUnavailableOnly_NoLog()
        {
            var settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/_AddressTellerServiceSettingsGateTestsConfig", "AddressTellerServiceSettingsGateTestsSettings");
            try
            {
                var result = AddressTellerSnapshotService.BuildPredictedSnapshot(
                    settings, Array.Empty<string>(), Array.Empty<AddressRuleBase>());

                Assert.AreEqual(1, result.Issues.Count);
                Assert.AreEqual(ValidationStatus.SettingsUnavailable, result.Issues[0].Status);
                Assert.IsTrue(result.Issues[0].IsBlocking);
                Assert.IsTrue(result.Diff.IsEmpty);
                Assert.IsNotNull(result.After);
                Assert.AreEqual(0, result.After.Entries.Count);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                foreach (var group in settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(settings, true);
            }
        }

        // --- ラッパーオーバーロード経由（rules 未指定）でも、ゲート失敗時はゲート前にルール収集を
        // 行わないことを確認する。RuleCollector.CollectEnabledRules()/CollectRules() は
        // AddressTellerSettingsAsset.Current の無効化ルール一覧を読むため、ゲートより前に呼ぶと
        // 古いメモリ上の値と組み合わさる恐れがある（今回の修正内容）。ここでは rules を渡さない
        // ラッパーオーバーロードを直接呼び、中核オーバーロードと同じ単一の SettingsUnavailable 結果に
        // 到達すること・ログが出ない（削除系のみログが出る）ことを確認することで、ゲート前のルール収集を
        // 経由していないことを間接的に確認する。

        [Test]
        public void ApplyAll_WrapperOverload_SettingsFileUnreadable_ReturnsSettingsUnavailableOnly_NoLog()
        {
            var results = AddressTellerService.ApplyAll((AddressableAssetSettings)null);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.SettingsUnavailable, results[0].Status);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ValidateAll_WrapperOverload_SettingsFileUnreadable_ReturnsSettingsUnavailableOnly_NoLog()
        {
            var results = AddressTellerService.ValidateAll((AddressableAssetSettings)null);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationStatus.SettingsUnavailable, results[0].Status);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_WrapperOverload_SettingsFileUnreadable_ReturnsEmptyList_LogsError()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("does not look like an AddressTeller settings file"));

            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(Array.Empty<string>(), null);

            Assert.AreEqual(0, cleared.Count);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BuildPredictedSnapshot_WrapperOverload_SettingsFileUnreadable_ReturnsSettingsUnavailableOnly_NoLog()
        {
            var settings = AddressTellerTestSettingsFactory.CreateInMemory(
                "Assets/_AddressTellerServiceSettingsGateTestsWrapperConfig", "AddressTellerServiceSettingsGateTestsWrapperSettings");
            try
            {
                var result = AddressTellerSnapshotService.BuildPredictedSnapshot(settings, Array.Empty<string>());

                Assert.AreEqual(1, result.Issues.Count);
                Assert.AreEqual(ValidationStatus.SettingsUnavailable, result.Issues[0].Status);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                foreach (var group in settings.groups.Where(g => g != null).ToList())
                    UnityEngine.Object.DestroyImmediate(group, true);
                UnityEngine.Object.DestroyImmediate(settings, true);
            }
        }
    }
}

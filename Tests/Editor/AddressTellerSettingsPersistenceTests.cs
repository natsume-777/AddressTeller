using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettings.SaveToDisk / ReloadFromDisk の挙動を検証する。
    /// ProjectSettings/AddressTellerSettings.asset を実際に読み書きするため、TearDown では
    /// テストガイドラインの規約どおり「メモリを元値へ戻す → SaveToDisk() で確定する」の順で復元する
    /// （バイト列を直接書き戻すだけではメモリが古いまま残り、次の保存で再汚染されるため）。
    /// 永続化ファイルは YAML なので、内容の確認は JSON ではなく生テキストの部分一致で行う。
    /// 元値の退避・復元は AddressTellerSettings.PostprocessOrder のようなプロパティ getter/setter
    /// ではなく、AddressTellerSettingsAsset の生フィールドを直接使う。PostprocessOrder の getter は
    /// 未設定センチネルの 0 を既定値 1000 に読み替えるため、getter で控えて setter で書き戻すと、
    /// 実際には _postprocessOrder: 0（未設定）だったプロジェクトのファイルが _postprocessOrder: 1000
    /// （明示設定）へ書き換わってしまう（このテストが同梱される実プロジェクトでも起こりうる）。
    /// </summary>
    public class AddressTellerSettingsPersistenceTests
    {
        private string _path;
        private string _backupPath;
        private int _originalPostprocessOrderField;
        private List<string> _originalDisabledRuleClassNames;

        [SetUp]
        public void SetUp()
        {
            _path = AddressTellerSettingsAsset.GetAbsoluteFilePath();
            // 接尾辞はこのテストクラス専用にする。AddressTellerSettingsAssetReloadTests も同じ _path から
            // 退避ファイルを作るため、単純に ".bak" では2クラスが同じパスを取り合ってしまう
            // （一方が退避したまま他方が File.Move の宛先とみなすと IOException になる）。
            _backupPath = _path + ".persistence.bak";
            _originalPostprocessOrderField = AddressTellerSettingsAsset.instance._postprocessOrder;
            _originalDisabledRuleClassNames = new List<string>(AddressTellerSettingsAsset.instance._disabledRuleClassNames);
        }

        [TearDown]
        public void TearDown()
        {
            // テストが _path を一時的に退避している場合（File.Move）、メモリの復元より先にディスクの
            // 実体を元の場所へ戻しておく。テスト実行のキャンセル・ドメインリロード・Editor クラッシュが
            // 退避直後に起きて本メソッド自体が呼ばれなかった場合でも、_path ではなく _backupPath として
            // 内容がディスク上に残るため、File.Delete で退避していた場合と違って開発者の設定ファイルが
            // 消失したまま残ることがない。この復元は SetUp の成否に関わらず必ず試みる
            // （_backupPath は SetUp の早い段階、instance へ触れるより前に確定するため、
            // SetUp が後段で例外を出していてもここには到達できる）。
            if (_backupPath != null && File.Exists(_backupPath))
            {
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(_backupPath, _path);
            }

            // SetUp が GetAbsoluteFilePath()/instance 参照等で例外を出して早期終了した場合、
            // _originalDisabledRuleClassNames は null のままであり、メモリを復元するための元値も無い。
            // ここで戻らずに書き戻すと、未初期化の既定値（_originalPostprocessOrderField: 0 等、
            // これは「未設定」センチネルと同じ値）を SaveToDisk() 経由で実ファイルへ書き込んでしまう。
            if (_originalDisabledRuleClassNames == null) return;

            var asset = AddressTellerSettingsAsset.instance;
            asset._postprocessOrder = _originalPostprocessOrderField;
            asset._disabledRuleClassNames.Clear();
            asset._disabledRuleClassNames.AddRange(_originalDisabledRuleClassNames);
            AddressTellerSettings.SaveToDisk();
        }

        /// <summary>元値と衝突しない値を返す（フォールバック対象の 0 も避ける）。</summary>
        private static int DistinctFrom(int baseline, int candidate)
        {
            if (candidate == baseline || candidate == 0) return candidate + 1;
            return candidate;
        }

        [Test]
        public void SaveToDisk_AfterFileDeletedAndSameValueReassigned_RecreatesFileWithCurrentValue()
        {
            var x = DistinctFrom(_originalPostprocessOrderField, 777);
            AddressTellerSettings.PostprocessOrder = x; // 通常の setter 経由。ここでファイルへ書き込まれる。

            // File.Delete ではなく File.Move で退避する。中断（テストキャンセル・ドメインリロード・
            // Editor クラッシュ）がこの直後に起きても、実プロジェクトの設定ファイルの内容が
            // _backupPath としてディスク上に残る（TearDown 参照）。
            File.Move(_path, _backupPath);
            Assert.IsFalse(File.Exists(_path));

            // 同値の再代入は setter の早期 return により何も起きない（詰みの再現）。
            // ここから SaveToDisk() までの間、AddressTellerSettingsAsset.instance には一切アクセスしない。
            // instance へのアクセスはメモリ上にキャッシュ済みの参照をそのまま返すだけで、ファイルの
            // 有無に応じて自動的に破棄→再読込が走ることはない（それが起きるのは ReloadFromDisk() が
            // 明示的に DestroyImmediate を呼んだときだけであり、このテストでは呼ばない）。
            AddressTellerSettings.PostprocessOrder = x;
            Assert.IsFalse(File.Exists(_path), "同値の再代入では書き込まれないことの前提確認");

            var result = AddressTellerSettings.SaveToDisk();

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(_path));

            var diskText = File.ReadAllText(_path);
            StringAssert.Contains($"_postprocessOrder: {x}", diskText);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveToDisk_CalledTwiceConsecutively_ProducesIdenticalFileContent()
        {
            AddressTellerSettings.PostprocessOrder = DistinctFrom(_originalPostprocessOrderField, 123);

            Assert.IsTrue(AddressTellerSettings.SaveToDisk());
            var first = File.ReadAllText(_path);

            Assert.IsTrue(AddressTellerSettings.SaveToDisk());
            var second = File.ReadAllText(_path);

            Assert.AreEqual(first, second);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveToDisk_AfterSaving_ReserializedTempFileMatchesDiskText()
        {
            AddressTellerSettings.PostprocessOrder = DistinctFrom(_originalPostprocessOrderField, 321);

            Assert.IsTrue(AddressTellerSettings.SaveToDisk());

            var diskText = File.ReadAllText(_path);
            var reserializedText = AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText();
            Assert.AreEqual(diskText, reserializedText);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReloadFromDisk_ReplacesInstanceIdentityAndPropertiesReflectNewInstance()
        {
            var x = DistinctFrom(_originalPostprocessOrderField, 42);
            AddressTellerSettings.PostprocessOrder = x;
            Assert.IsTrue(AddressTellerSettings.SaveToDisk());

            var instanceBeforeReload = AddressTellerSettingsAsset.instance;

            // ファイルを介さず、メモリ上のシングルトンだけを直接書き換える。
            AddressTellerSettingsAsset.instance._postprocessOrder = DistinctFrom(x, 999);

            var result = AddressTellerSettings.ReloadFromDisk();

            Assert.IsTrue(result);
            Assert.AreNotSame(instanceBeforeReload, AddressTellerSettingsAsset.instance,
                "ReloadFromDisk() は破棄→再取得によりインスタンスの参照を差し替える設計であるため、" +
                "呼び出し前後で参照が変わらないのは回帰。");
            Assert.AreEqual(x, AddressTellerSettings.PostprocessOrder,
                "差し替え後の新しいインスタンス経由でプロパティが正しく値を返すこと。");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReloadFromDisk_FileMissing_ReturnsFalseAndLeavesMemoryAndInstanceUnchanged()
        {
            var x = DistinctFrom(_originalPostprocessOrderField, 55);
            AddressTellerSettings.PostprocessOrder = x;
            Assert.IsTrue(AddressTellerSettings.SaveToDisk());

            // File.Delete ではなく File.Move で退避する（理由は TearDown のコメントを参照）。
            File.Move(_path, _backupPath);
            Assert.IsFalse(File.Exists(_path));

            var instanceBefore = AddressTellerSettingsAsset.instance;

            var result = AddressTellerSettings.ReloadFromDisk();

            Assert.IsFalse(result);
            Assert.AreSame(instanceBefore, AddressTellerSettingsAsset.instance,
                "ファイルが存在しない場合は早期 return するため、instance の参照は差し替わらないこと。");
            Assert.AreEqual(x, AddressTellerSettings.PostprocessOrder);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReloadFromDisk_DisabledRuleClassNamesListField_RoundTripsThroughSaveAndReload()
        {
            // 実在のルールクラスである必要はない。SetRuleEnabled/IsRuleEnabled/DisabledRuleClassNames は
            // クラス名の文字列をそのまま保持・比較するだけで、実在性は検証しない。
            // int 以外の型（List<string>）でもシリアライズ往復後に内容が保持されることを確認する
            // （将来 _disabledRuleClassNames に FormerlySerializedAs を追加する等の変更に対する回帰検知）。
            const string ruleClassName =
                "AddressTeller.Editor.Tests.AddressTellerSettingsPersistenceTests+RoundTripDummyRuleName";

            AddressTellerSettings.SetRuleEnabled(ruleClassName, false);
            Assert.IsFalse(AddressTellerSettings.IsRuleEnabled(ruleClassName));
            Assert.IsTrue(AddressTellerSettings.SaveToDisk());

            // ファイルを介さず、メモリ上のリストだけを直接書き換える（Reload で復元されることを確認するため）。
            AddressTellerSettingsAsset.instance._disabledRuleClassNames.Remove(ruleClassName);
            Assert.IsTrue(AddressTellerSettings.IsRuleEnabled(ruleClassName), "前提: メモリ上では一旦復帰していること。");

            var result = AddressTellerSettings.ReloadFromDisk();

            Assert.IsTrue(result);
            Assert.IsFalse(AddressTellerSettings.IsRuleEnabled(ruleClassName),
                "List<string> フィールドの内容がシリアライズ往復後も保持されていること。");

            LogAssert.NoUnexpectedReceived();
        }

        // --- SaveToDisk()/ReloadFromDisk() による AddressTellerSettingsLoadDiagnostics
        //     ドメインスコープキャッシュの無効化 ---
        // AddressTellerSettingsLoadDiagnosticsTests.cs は実ファイルを経由しない制約のクラスのため、
        // 実際に SaveToDisk()/ReloadFromDisk() を呼んでキャッシュが無効化されることの確認はここに置く。
        // 「直したはずの不一致が古い診断結果のせいで -addressTellerFailOnSettingsMismatch 指定時に誤って
        // 中断させる」偽陽性を防ぐための無効化なので、この2件は今回追加した中で最も回帰リスクが高い。

        private static readonly FieldInfo DomainCacheField = typeof(AddressTellerSettingsLoadDiagnostics).GetField(
            "s_diagnosisThisDomain", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void SaveToDisk_DomainCacheWasPopulated_InvalidatesItRegardlessOfOutcome()
        {
            AddressTellerSettings.PostprocessOrder = DistinctFrom(_originalPostprocessOrderField, 654);

            // Diagnose() を経由せず、キャッシュへ直接マーカー値を書き込む（この呼び出し自体はまだ
            // ファイルに触れない）。
            DomainCacheField.SetValue(null, (SettingsLoadDiagnosis?)SettingsLoadDiagnosis.Mismatch(
                "cache-probe-marker (SaveToDisk invalidation test)"));
            try
            {
                Assert.IsTrue(AddressTellerSettings.SaveToDisk());

                Assert.IsNull(DomainCacheField.GetValue(null),
                    "SaveToDisk() はキャッシュを無条件に無効化するはずで、成功時も例外ではない。");
            }
            finally
            {
                // アサーションが失敗して早期終了した場合でも、他のテストへマーカー値が漏れないようにする。
                AddressTellerSettingsLoadDiagnostics.InvalidateDomainCache();
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReloadFromDisk_DomainCacheWasPopulated_InvalidatesItRegardlessOfOutcome()
        {
            AddressTellerSettings.PostprocessOrder = DistinctFrom(_originalPostprocessOrderField, 456);
            Assert.IsTrue(AddressTellerSettings.SaveToDisk());

            DomainCacheField.SetValue(null, (SettingsLoadDiagnosis?)SettingsLoadDiagnosis.Mismatch(
                "cache-probe-marker (ReloadFromDisk invalidation test)"));
            try
            {
                Assert.IsTrue(AddressTellerSettings.ReloadFromDisk());

                Assert.IsNull(DomainCacheField.GetValue(null),
                    "ReloadFromDisk() はキャッシュを無条件に無効化するはずで、成功時も例外ではない。");
            }
            finally
            {
                AddressTellerSettingsLoadDiagnostics.InvalidateDomainCache();
            }

            LogAssert.NoUnexpectedReceived();
        }
    }
}

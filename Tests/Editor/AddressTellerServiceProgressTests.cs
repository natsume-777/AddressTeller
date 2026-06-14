using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerService.ApplyAll/ValidateAll の IProgressReporter 連携オーバーロードを検証する。
    /// 実在しないダミーパスを渡すことで AssetFilter.ShouldExclude 以降の処理（書き込み・ルール評価）を
    /// 発生させず、progress.Report の呼び出しタイミング・回数・キャンセル時の中断のみを観察する。
    /// </summary>
    public class AddressTellerServiceProgressTests
    {
        // ApplyAll/ValidateAll は内部で settings.ConfigFolder（AssetDatabase.GetAssetPath 依存）を参照するため、
        // ConfigFolder のキャッシュのみを設定した非永続 settings を使う。他のテストクラスと衝突しないよう専用サブフォルダを使う。
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp/ServiceProgress";

        private AddressableAssetSettings _settings;

        [SetUp]
        public void SetUp()
        {
            // AssetDatabase.CreateFolder は1階層ずつ作成する必要があるため、親→子の順で確認・作成する。
            if (!AssetDatabase.IsValidFolder("Assets/_AddressTellerTestTemp"))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets/_AddressTellerTestTemp", "ServiceProgress");

            // isPersisted=true でディスク上に .asset を作成すると本番の Addressables 設定に副作用が
            // 残るため、非永続 settings に ConfigFolder のキャッシュのみを設定するヘルパーを使う。
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(TestRootFolder, "AddressTellerServiceProgressTestSettings");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRootFolder);
        }

        /// <summary>
        /// Report の呼び出し回数・引数を記録し、指定回数目で false を返すスパイ。
        /// </summary>
        private sealed class SpyProgressReporter : IProgressReporter
        {
            public readonly List<(int Current, int Total, string Description)> Calls = new List<(int, int, string)>();

            private readonly int _cancelAfterCallCount;

            /// <param name="cancelAfterCallCount">この回数だけ true を返した後、それ以降は false を返す。0以下なら常に true。</param>
            public SpyProgressReporter(int cancelAfterCallCount = int.MaxValue)
            {
                _cancelAfterCallCount = cancelAfterCallCount;
            }

            public bool Report(int current, int total, string description)
            {
                Calls.Add((current, total, description));
                return Calls.Count <= _cancelAfterCallCount;
            }
        }

        private static List<string> DummyPaths(int count)
        {
            var paths = new List<string>();
            for (var i = 0; i < count; i++)
                paths.Add($"{TestRootFolder}/NonExistent_{i}.prefab");
            return paths;
        }

        [Test]
        public void ApplyAll_WithPaths_ReportsEachPathAndCompletion()
        {
            var paths = DummyPaths(3);
            var reporter = new SpyProgressReporter();

            AddressTellerService.ApplyAll(paths, _settings, reporter);

            // 3件のアセット分 + 完了通知1回 = 4回
            Assert.AreEqual(4, reporter.Calls.Count);
            Assert.AreEqual((0, 3, paths[0]), reporter.Calls[0]);
            Assert.AreEqual((1, 3, paths[1]), reporter.Calls[1]);
            Assert.AreEqual((2, 3, paths[2]), reporter.Calls[2]);
            // 完了通知は description が空文字
            Assert.AreEqual((3, 3, string.Empty), reporter.Calls[3]);
        }

        [Test]
        public void ApplyAll_WithPaths_CancelledPartway_StopsProcessingRemainingPaths()
        {
            var paths = DummyPaths(5);
            // 3回目の Report 呼び出し（3件目, current=2）で false を返してキャンセルする。
            var reporter = new SpyProgressReporter(cancelAfterCallCount: 2);

            AddressTellerService.ApplyAll(paths, _settings, reporter);

            // 完了通知（progress.Report(total, total, "")）は break 後に呼ばれないため、
            // 1〜3件目の3回のみ。4件目・5件目（paths[3], paths[4]）は報告されない。
            Assert.AreEqual(3, reporter.Calls.Count);
            Assert.AreEqual((0, 5, paths[0]), reporter.Calls[0]);
            Assert.AreEqual((1, 5, paths[1]), reporter.Calls[1]);
            Assert.AreEqual((2, 5, paths[2]), reporter.Calls[2]);
        }

        [Test]
        public void ApplyAll_WithNullProgressReporter_FallsBackToNullProgressReporter()
        {
            var paths = DummyPaths(2);

            // progress に null を渡しても NullProgressReporter にフォールバックし、例外なく完了する。
            Assert.DoesNotThrow(() => AddressTellerService.ApplyAll(paths, _settings, null));
        }

        [Test]
        public void ApplyAll_NullProgressReporterInstance_ReturnsSameResultAsWithoutProgress()
        {
            var paths = DummyPaths(3);

            var withoutProgress = AddressTellerService.ApplyAll(paths, _settings);
            var withNullReporter = AddressTellerService.ApplyAll(paths, _settings, NullProgressReporter.Instance);

            Assert.AreEqual(withoutProgress.Count, withNullReporter.Count);
        }

        [Test]
        public void ApplyAll_EmptyPaths_ReportsCompletionOnly()
        {
            var paths = new List<string>();
            var reporter = new SpyProgressReporter();

            AddressTellerService.ApplyAll(paths, _settings, reporter);

            // total=0 の完了通知のみ1回。
            Assert.AreEqual(1, reporter.Calls.Count);
            Assert.AreEqual((0, 0, string.Empty), reporter.Calls[0]);
        }

        [Test]
        public void ValidateAll_NullProgressReporterInstance_ReturnsSameResultAsWithoutProgress()
        {
            var withoutProgress = AddressTellerService.ValidateAll(_settings);
            var withNullReporter = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance);

            Assert.AreEqual(withoutProgress.Count, withNullReporter.Count);
        }

        [Test]
        public void ValidateAll_CancelledImmediately_ReturnsEmptyIssues()
        {
            // 最初の Report で false を返すため、対象が1件以上あれば1件も評価されずに中断する。
            // （対象が0件の場合は完了通知のみが1回呼ばれる。いずれも issues は空。）
            var reporter = new SpyProgressReporter(cancelAfterCallCount: 0);

            var issues = AddressTellerService.ValidateAll(_settings, reporter);

            Assert.AreEqual(0, issues.Count);
            Assert.AreEqual(1, reporter.Calls.Count);
        }
    }
}

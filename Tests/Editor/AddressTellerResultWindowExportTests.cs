using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerResultWindow.ExportDistributionToPath（Export CSV/Markdown ボタンの実処理）の
    /// 書き込み失敗時の通知を検証する。EditorWindow.Show() を伴う実ウィンドウ表示は行わず、
    /// ScriptableObject.CreateInstance で直接インスタンス化する。
    /// </summary>
    public class AddressTellerResultWindowExportTests
    {
        private Action<string, string> _originalNotify;
        private AddressTellerResultWindow _window;

        [SetUp]
        public void SetUp()
        {
            _originalNotify = AddressTellerResultWindow.s_notifyExportFailed;
            _window = ScriptableObject.CreateInstance<AddressTellerResultWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerResultWindow.s_notifyExportFailed = _originalNotify;
            UnityEngine.Object.DestroyImmediate(_window);
        }

        private void SetDistribution(BundleDistribution distribution, DistributionSummary summary)
        {
            var distributionField = typeof(AddressTellerResultWindow).GetField("_distribution", BindingFlags.NonPublic | BindingFlags.Instance);
            var summaryField = typeof(AddressTellerResultWindow).GetField("_distributionSummary", BindingFlags.NonPublic | BindingFlags.Instance);
            distributionField.SetValue(_window, distribution);
            summaryField.SetValue(_window, summary);
        }

        [Test]
        public void ExportDistributionToPath_WriteFails_NotifiesFailure()
        {
            SetDistribution(
                new BundleDistribution(new System.Collections.Generic.List<LogicalBundle>()),
                new DistributionSummary(0, 0, null));

            // 既存のディレクトリと同名のパスをファイル出力先として渡し、書き込みを確実に失敗させる。
            var blockingDir = Path.Combine(Path.GetTempPath(), "AddressTellerResultWindowExportTests_" + Guid.NewGuid());
            Directory.CreateDirectory(blockingDir);
            var path = blockingDir;

            try
            {
                string notifiedTitle = null;
                string notifiedMessage = null;
                AddressTellerResultWindow.s_notifyExportFailed = (title, message) =>
                {
                    notifiedTitle = title;
                    notifiedMessage = message;
                };

                LogAssert.Expect(LogType.Error, new Regex("Failed to write bundle distribution"));

                _window.ExportDistributionToPath(path, "csv");

                Assert.IsNotNull(notifiedTitle, "書き込みに失敗した場合、失敗通知（ダイアログ相当）が呼ばれるべき。");
                StringAssert.Contains(path, notifiedMessage);
            }
            finally
            {
                if (Directory.Exists(blockingDir))
                    Directory.Delete(blockingDir, true);
            }
        }

        [Test]
        public void ExportDistributionToPath_WriteSucceeds_DoesNotNotify()
        {
            SetDistribution(
                new BundleDistribution(new System.Collections.Generic.List<LogicalBundle>()),
                new DistributionSummary(0, 0, null));

            var tempDir = Path.Combine(Path.GetTempPath(), "AddressTellerResultWindowExportTests_" + Guid.NewGuid());
            var path = Path.Combine(tempDir, "distribution.csv");

            try
            {
                var notified = false;
                AddressTellerResultWindow.s_notifyExportFailed = (_, __) => notified = true;

                _window.ExportDistributionToPath(path, "csv");

                Assert.IsFalse(notified, "書き込みに成功した場合、失敗通知は呼ばれないべき。");
                Assert.IsTrue(File.Exists(path));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}

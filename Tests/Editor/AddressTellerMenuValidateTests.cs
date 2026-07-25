using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerMenu.Validate() の issue 検出時の挙動（IsOk に応じたログレベルの振り分け・
    /// 結果ウィンドウ表示の呼び出し）を検証する。
    /// 結果ウィンドウの表示自体は <see cref="AddressTellerMenu.s_showResultWindow"/> を差し替えて検証し、
    /// 実 EditorWindow は開かない（ユーザーが開いていた既存の結果ウィンドウを巻き添えで閉じないため）。
    /// </summary>
    public class AddressTellerMenuValidateTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/MenuValidate";
        private const string StubAssetPath = StubFolder + "/StubAsset.prefab";

        // settings.ConfigFolder はスキャン対象から除外されるため、テストアセット配下と衝突しない別パスにする。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempMenuValidateConfig";

        /// <summary>StubFolder 配下のアセットに、存在しないグループ "MissingGroup" を割り当てるルール（GroupNotFound を発生させる）。</summary>
        private sealed class MissingGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("MissingGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>StubFolder 配下のアセットに、存在しないグループ "AutoCreatedGroup" を割り当てるルール。
        /// AutoCreateMissingGroups を ON にすると GroupWillBeCreated（IsOk=true）のみを発生させる。</summary>
        private sealed class AutoCreateGroupRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("AutoCreatedGroup")
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private AddressableAssetSettings _settings;
        private bool _originalAutoCreate;
        private Action<IReadOnlyList<ValidationResult>, string> _originalShowResultWindow;

        [SetUp]
        public void SetUp()
        {
            // GroupNotFound（IsOk=false）を発生させるテストのため、他テストの残留設定に左右されないよう明示的に OFF にする。
            _originalAutoCreate = AddressTellerSettings.AutoCreateMissingGroups;
            AddressTellerSettings.AutoCreateMissingGroups = false;

            _originalShowResultWindow = AddressTellerMenu.s_showResultWindow;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerMenuValidateTestSettings");

            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);

            AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "MenuValidate");

            var go = new GameObject("StubAsset");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, StubAssetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.AutoCreateMissingGroups = _originalAutoCreate;
            AddressTellerMenu.s_showResultWindow = _originalShowResultWindow;

            AssetDatabase.DeleteAsset(TestRootFolder);

            foreach (var group in _settings.groups.Where(g => g != null).ToList())
                UnityEngine.Object.DestroyImmediate(group, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        [Test]
        public void Validate_RealIssue_LogsErrorPerIssueAndSummary_AndShowsResultWindow()
        {
            var rules = new AddressRuleBase[] { new MissingGroupRule() };

            IReadOnlyList<ValidationResult> shownIssues = null;
            string shownTitle = null;
            AddressTellerMenu.s_showResultWindow = (issues, title) =>
            {
                shownIssues = issues;
                shownTitle = title;
            };

            LogAssert.Expect(LogType.Error, new Regex("GroupNotFound"));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AddressTeller] Validate completed: 1 issue(s) found.")));

            AddressTellerMenu.Validate(_settings, rules);

            // Apply All / Apply with Validate の中止経路と対称に、IsOk=false の issue が見つかった場合は結果ウィンドウ表示を呼ぶ。
            Assert.IsNotNull(shownIssues, "Validate() で issue（IsOk=false）が見つかった場合、結果ウィンドウ表示が呼ばれるべき。");
            Assert.AreEqual(1, shownIssues.Count);
            Assert.AreEqual("AddressTeller - Validate", shownTitle);
        }

        [Test]
        public void Validate_NoIssues_LogsCompletionOnly()
        {
            // ルールを何も渡さない（=対象アセットは何にもマッチしない）ため issues は0件になる。
            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Validate completed: no issues.")));

            var wasShown = false;
            AddressTellerMenu.s_showResultWindow = (issues, title) => wasShown = true;

            AddressTellerMenu.Validate(_settings, System.Array.Empty<AddressRuleBase>());

            Assert.IsFalse(wasShown, "issues が0件の場合、結果ウィンドウ表示は呼ばれてはいけない。");
        }

        [Test]
        public void Validate_OnlyGroupWillBeCreatedNotices_LogsAsInfoAndDoesNotShowResultWindow()
        {
            // AutoCreateMissingGroups を ON にすると、存在しないグループへの割り当ては
            // GroupWillBeCreated（IsOk=true、エラーではない通知）のみになる。
            AddressTellerSettings.AutoCreateMissingGroups = true;
            var rules = new AddressRuleBase[] { new AutoCreateGroupRule() };

            var wasShown = false;
            AddressTellerMenu.s_showResultWindow = (issues, title) => wasShown = true;

            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape("[AddressTeller] Validate completed: 1 notice(s) (no issues).")));

            AddressTellerMenu.Validate(_settings, rules);

            // GroupWillBeCreated のみ（errorCount == 0）の場合、フォーカスを奪う結果ウィンドウは開かない。
            Assert.IsFalse(wasShown, "IsOk=false の issue が無い場合、結果ウィンドウ表示は呼ばれてはいけない。");
        }

        [Test]
        public void Validate_NullSettings_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AddressTellerMenu.Validate(null, Array.Empty<AddressRuleBase>()));
        }
    }
}

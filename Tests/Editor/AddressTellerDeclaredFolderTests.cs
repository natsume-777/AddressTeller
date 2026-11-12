using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// ルールが宣言したフォルダの検査（DeclaredFolderMissing / DeclaredFolderCaseMismatch）を、実際の
    /// AssetDatabase とメモリ上の AddressableAssetSettings で ApplyAll / ValidateAll / BuildPredictedSnapshot /
    /// RemoveEntriesForDeletedAssets / 増分適用の各経路から検証する。
    /// 判定ロジック単体は DeclaredFolderInspectorTests、宣言の組み立ては DeclaredFolderConditionTests が担当する。
    /// </summary>
    public class AddressTellerDeclaredFolderTests
    {
        private const string TempRoot = "Assets/_AddressTellerTestTemp";
        private const string TestRootFolder = TempRoot + "/DeclaredFolder";
        private const string RealFolder = TestRootFolder + "/Real";
        private const string OtherFolder = TestRootFolder + "/Other";
        private const string MissingFolder = TestRootFolder + "/DoesNotExist";
        private const string LaterFolder = TestRootFolder + "/Later";
        private const string StubAssetPath = RealFolder + "/StubAsset.prefab";
        private const string StaleAssetPath = OtherFolder + "/StaleAsset.prefab";
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempDeclaredFolderConfig";

        /// <summary>GuidRule が宣言する GUID。テストごとに差し替える。</summary>
        private static string s_guidToDeclare;

        private sealed class RealFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(RealFolder)).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private sealed class MissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(MissingFolder)).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private sealed class LaterFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(LaterFolder)).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>RealFolder を、最後のセグメントの大小文字だけ変えて宣言するルール。</summary>
        private sealed class WrongCaseFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolder(TestRootFolder + "/real")).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        /// <summary>存在しないフォルダを生ラムダで書いたルール。宣言は無いので何も報告されない。</summary>
        private sealed class RawLambdaMissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(ctx => ctx.IsInFolder(MissingFolder)).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private sealed class GlobMissingFolderRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.Glob(MissingFolder + "/**/*.prefab")).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private sealed class GlobLeadingWildcardRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.Glob("**/DoesNotExist/*.prefab")).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private sealed class GuidRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("StubGroup").Where(Match.InFolderByGuid(s_guidToDeclare)).Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        private AddressableAssetSettings _settings;
        private bool _originalCleanupSetting;

        [SetUp]
        public void SetUp()
        {
            _originalCleanupSetting = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = false;
            DeclaredFolderInspector.ResetCache();
            AddressTellerPostprocessor.s_lastLoggedDeclaredFolderSignature = null;

            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerDeclaredFolderTestSettings");
            _settings.CreateGroup("StubGroup", false, false, false, null);

            if (!AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder(TempRoot, "DeclaredFolder");
            if (!AssetDatabase.IsValidFolder(RealFolder))
                AssetDatabase.CreateFolder(TestRootFolder, "Real");
            if (!AssetDatabase.IsValidFolder(OtherFolder))
                AssetDatabase.CreateFolder(TestRootFolder, "Other");

            CreatePrefab(StubAssetPath);
            CreatePrefab(StaleAssetPath);
        }

        [TearDown]
        public void TearDown()
        {
            AddressTellerSettings.CleanupStaleEntries = _originalCleanupSetting;
            DeclaredFolderInspector.ResetCache();
            AddressTellerPostprocessor.s_lastLoggedDeclaredFolderSignature = null;

            AssetDatabase.DeleteAsset(TestRootFolder);
            if (_settings != null)
            {
                foreach (var group in _settings.groups.Where(g => g != null).ToList())
                    Object.DestroyImmediate(group, true);
                Object.DestroyImmediate(_settings, true);
            }
        }

        private static void CreatePrefab(string path)
        {
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static string GuidOf(string path) => AssetDatabase.AssetPathToGUID(path);

        /// <summary>StaleAssetPath のエントリを、どのルールにもマッチしない所有グループ内エントリとして作る。</summary>
        private string AddStaleEntry()
        {
            var guid = GuidOf(StaleAssetPath);
            var entry = _settings.CreateOrMoveEntry(guid, _settings.FindGroup("StubGroup"));
            entry.SetAddress("StaleAsset");
            return guid;
        }

        private static AddressRuleBase[] Rules(params AddressRuleBase[] rules) => rules;

        private static ValidationResult[] OfStatus(System.Collections.Generic.IEnumerable<ValidationResult> issues, ValidationStatus status)
            => issues.Where(i => i.Status == status).ToArray();

        // --- 欠落 ---

        [Test]
        public void ApplyAll_DeclaredFolderMissing_ReportsBlockingIssueAndStillAppliesOtherRules()
        {
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new RealFolderRule(), new MissingFolderRule()));

            var missing = OfStatus(issues, ValidationStatus.DeclaredFolderMissing);
            Assert.AreEqual(1, missing.Length);
            Assert.IsTrue(missing[0].IsBlocking);
            Assert.IsNull(missing[0].Context);
            StringAssert.Contains(MissingFolder, missing[0].Message);
            StringAssert.Contains(nameof(MissingFolderRule), missing[0].Message);
            Assert.IsNotNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)), "欠落の報告は、ほかのルールの書き込みを止めない。");
        }

        [Test]
        public void ApplyAll_AllDeclaredFoldersExist_ReportsNothing()
        {
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new RealFolderRule()));

            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void ApplyAll_GlobWithMissingFixedPrefix_ReportsMissing()
        {
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new GlobMissingFolderRule()));

            Assert.AreEqual(1, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
        }

        [Test]
        public void ApplyAll_GlobWithLeadingWildcard_ReportsNothing()
        {
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new GlobLeadingWildcardRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length,
                "先頭がワイルドカードの Glob は宣言を持たないので、欠落を報告できない。");
        }

        [Test]
        public void ApplyAll_RawLambdaWhere_DeclaresNothingAndReportsNothing()
        {
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new RawLambdaMissingFolderRule()));

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.DeclaredFolderCaseMismatch).Length);
        }

        [Test]
        public void ValidateAll_DeclaredFolderMissing_Reports()
        {
            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance,
                Rules(new RealFolderRule(), new MissingFolderRule()));

            Assert.AreEqual(1, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
        }

        [Test]
        public void BuildPredictedSnapshot_DeclaredFolderMissing_Reports()
        {
            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StubAssetPath },
                Rules(new RealFolderRule(), new MissingFolderRule()));

            Assert.AreEqual(1, OfStatus(result.Issues, ValidationStatus.DeclaredFolderMissing).Length);
        }

        // --- 削除の停止 ---

        [Test]
        public void ApplyAll_DeclaredFolderMissing_SuspendsStaleCleanup()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("Skipping stale entry cleanup for this run because 1 declared folder(s) do not exist")));

            AddressTellerService.ApplyAll(new[] { StaleAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new RealFolderRule(), new MissingFolderRule()));

            Assert.IsNotNull(_settings.FindAssetEntry(guid), "宣言フォルダが欠けているランでは、無マッチに見えるエントリも削除しない。");
        }

        [Test]
        public void ApplyAll_NoMissingFolder_StillRemovesStaleEntry()
        {
            // 上のテストの対照。欠落が無ければ、同じ構成で無マッチのエントリは従来どおり削除される。
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            LogAssert.Expect(LogType.Warning, new Regex("Removing stale entry"));

            AddressTellerService.ApplyAll(new[] { StaleAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new RealFolderRule()));

            Assert.IsNull(_settings.FindAssetEntry(guid));
        }

        [Test]
        public void BuildPredictedSnapshot_DeclaredFolderMissing_PredictsNoRemoval()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StaleAssetPath },
                Rules(new RealFolderRule(), new MissingFolderRule()));

            Assert.IsFalse(result.Diff.Removed.Any(e => e.Guid == guid), "Apply が削除しないので、予測も削除を含めない。");
        }

        [Test]
        public void BuildPredictedSnapshot_NoMissingFolder_PredictsRemoval()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            var result = AddressTellerSnapshotService.BuildPredictedSnapshot(_settings, new[] { StaleAssetPath },
                Rules(new RealFolderRule()));

            Assert.IsTrue(result.Diff.Removed.Any(e => e.Guid == guid));
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_DeclaredFolderMissing_DoesNotRemove()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("Skipping stale entry cleanup for this run because 1 declared folder(s) do not exist")));

            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(new[] { guid }, _settings,
                Rules(new RealFolderRule(), new MissingFolderRule()));

            Assert.AreEqual(0, cleared.Count);
            Assert.IsNotNull(_settings.FindAssetEntry(guid));
        }

        [Test]
        public void RemoveEntriesForDeletedAssets_NoMissingFolder_RemovesOwnedEntry()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            LogAssert.Expect(LogType.Warning, new Regex("Removing stale entry"));

            var cleared = AddressTellerService.RemoveEntriesForDeletedAssets(new[] { guid }, _settings,
                Rules(new RealFolderRule()));

            Assert.AreEqual(1, cleared.Count);
        }

        // --- 大小文字 ---

        [Test]
        public void ApplyAll_DeclaredFolderCaseMismatch_ReportsNonBlockingNoticeAndRuleStillApplies()
        {
            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new WrongCaseFolderRule()));

            var mismatches = OfStatus(issues, ValidationStatus.DeclaredFolderCaseMismatch);
            Assert.AreEqual(1, mismatches.Length);
            Assert.IsTrue(mismatches[0].IsOk);
            Assert.IsFalse(mismatches[0].IsBlocking);
            Assert.IsNull(mismatches[0].Context);
            StringAssert.Contains(RealFolder, mismatches[0].Message);
            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
            Assert.IsNotNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)), "フォルダ照合は大小文字を区別しないので、ルールは効く。");
        }

        [Test]
        public void ApplyAll_DeclaredFolderCaseMismatch_DoesNotSuspendStaleCleanup()
        {
            AddressTellerSettings.CleanupStaleEntries = true;
            var guid = AddStaleEntry();

            LogAssert.Expect(LogType.Warning, new Regex("Removing stale entry"));

            AddressTellerService.ApplyAll(new[] { StaleAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new WrongCaseFolderRule()));

            Assert.IsNull(_settings.FindAssetEntry(guid), "大小文字だけの不一致は通知のみで、削除を止めない。");
        }

        // --- GUID ---

        [Test]
        public void ApplyAll_InFolderByGuid_ResolvableGuid_RuleAppliesAndReportsNothing()
        {
            s_guidToDeclare = GuidOf(RealFolder);

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new GuidRule()));

            Assert.AreEqual(0, issues.Count);
            Assert.IsNotNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)));
        }

        [Test]
        public void ApplyAll_InFolderByGuid_UnknownGuid_ReportsMissingAndMatchesNothing()
        {
            s_guidToDeclare = "00000000000000000000000000000000";

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new GuidRule()));

            var missing = OfStatus(issues, ValidationStatus.DeclaredFolderMissing);
            Assert.AreEqual(1, missing.Length);
            StringAssert.Contains(s_guidToDeclare, missing[0].Message);
            Assert.IsNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)));
        }

        [Test]
        public void ApplyAll_InFolderByGuid_GuidOfAFile_ReportsMissing()
        {
            s_guidToDeclare = GuidOf(StubAssetPath);

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance,
                Rules(new GuidRule()));

            Assert.AreEqual(1, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
        }

        [Test]
        public void ApplyAll_InFolderByGuid_FolderRenamed_ResolvesToNewPathOnNextRun()
        {
            s_guidToDeclare = GuidOf(RealFolder);
            var rules = Rules(new GuidRule());
            AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.AreEqual(string.Empty, AssetDatabase.RenameAsset(RealFolder, "Renamed"), "テストの前提: フォルダの改名に成功する。");
            var renamedAssetPath = TestRootFolder + "/Renamed/StubAsset.prefab";

            var issues = AddressTellerService.ApplyAll(new[] { renamedAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.AreEqual(0, issues.Count, "GUID は改名後の現在のパスへ解決し直され、欠落扱いにならない。");
            Assert.IsNotNull(_settings.FindAssetEntry(GuidOf(renamedAssetPath)), "改名後のパスでもルールがマッチする。");
        }

        // --- 増分経路のキャッシュ / Postprocessor が取りこぼす import でもキャッシュが古くならない ---
        //
        // 注意: AssetDatabase でフォルダを作成・削除・改名すると、実際の AddressTellerPostprocessor が
        // そのパスでキャッシュを無効化する（本番どおりの正しい動作）。そのため「キャッシュが残っている」状態は
        // AssetDatabase の操作では作れない。実体とは食い違う古い検査結果を SeedCache で直接キャッシュへ書き込み、
        // 「無効化されない限りキャッシュを使う」「無効化されたら検査し直す」を検証する。

        /// <summary>
        /// folder の検査結果を、実体とは無関係にキャッシュへ書き込む。exists=true なら Exists、false なら Missing。
        /// </summary>
        private static void SeedCache(string folder, bool exists)
        {
            var entry = new AddressRuleEntry("StubGroup", _ => true, _ => "a", new System.Func<AssetContext, string>[0],
                declaredFolders: new[] { folder });
            System.Func<string, string[]> subFolders = parent =>
            {
                if (!exists || !folder.StartsWith(parent + "/", System.StringComparison.Ordinal)) return new string[0];
                var next = folder.IndexOf('/', parent.Length + 1);
                return new[] { next < 0 ? folder : folder.Substring(0, next) };
            };
            DeclaredFolderInspector.Inspect(new[] { entry }, useCache: false, subFolders, _ => false);
        }

        /// <summary>
        /// OnPostprocessAllAssets を、SuppressForTests を立てて呼ぶ。本体の処理は走らず（先頭の early return）、
        /// 先頭で行うキャッシュ無効化だけが実行される。
        /// </summary>
        private static void InvokePostprocessorWhileSuppressed(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var method = typeof(AddressTellerPostprocessor).GetMethod("OnPostprocessAllAssets",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method);

            var original = AddressTellerPostprocessor.SuppressForTests;
            AddressTellerPostprocessor.SuppressForTests = true;
            try
            {
                method.Invoke(null, new object[] { imported, deleted, moved, movedFrom });
            }
            finally
            {
                AddressTellerPostprocessor.SuppressForTests = original;
            }
        }

        private int MissingCountIncremental(AddressRuleBase[] rules)
            => OfStatus(AddressTellerService.ApplyAllIncremental(new[] { StubAssetPath }, _settings, rules), ValidationStatus.DeclaredFolderMissing).Length;

        [Test]
        public void Postprocessor_FolderDeletedInImportThatSkipsApply_NextIncrementalSeesMissing()
        {
            var rules = Rules(new LaterFolderRule()); // LaterFolder は実際には存在しない
            SeedCache(LaterFolder, exists: true);      // 削除前に Exists と検査した、古い結果
            Assert.AreEqual(0, MissingCountIncremental(rules), "テストの前提: 無効化しなければ古い Exists が使われる。");

            // Apply を行わずに戻る import（ここでは SuppressForTests）でも、先頭の無効化は実行される。
            InvokePostprocessorWhileSuppressed(new string[0], new[] { LaterFolder }, new string[0], new string[0]);

            Assert.AreEqual(1, MissingCountIncremental(rules));
        }

        [Test]
        public void Postprocessor_FolderRenamedInImportThatSkipsApply_MovedFromInvalidatesCache()
        {
            var rules = Rules(new LaterFolderRule());
            SeedCache(LaterFolder, exists: true);
            Assert.AreEqual(0, MissingCountIncremental(rules), "テストの前提: 無効化しなければ古い Exists が使われる。");

            InvokePostprocessorWhileSuppressed(new string[0], new string[0], new[] { TestRootFolder + "/Later2" }, new[] { LaterFolder });

            Assert.AreEqual(1, MissingCountIncremental(rules), "移動元のパスでも無効化される。");
        }

        [Test]
        public void Postprocessor_UnrelatedImport_KeepsCache()
        {
            var rules = Rules(new LaterFolderRule());
            SeedCache(LaterFolder, exists: true);

            InvokePostprocessorWhileSuppressed(new[] { StubAssetPath }, new string[0], new string[0], new string[0]);

            Assert.AreEqual(0, MissingCountIncremental(rules), "無関係な import ではキャッシュを捨てない。");
        }

        [Test]
        public void ApplyAllIncremental_CachesCheckUntilInvalidated()
        {
            var rules = Rules(new LaterFolderRule());
            SeedCache(LaterFolder, exists: true);

            Assert.AreEqual(0, MissingCountIncremental(rules), "無効化されるまでは、キャッシュ済みの検査結果を使う。");

            DeclaredFolderInspector.InvalidateCache(new[] { LaterFolder });

            Assert.AreEqual(1, MissingCountIncremental(rules), "宣言フォルダが変更パスに含まれたら検査し直す。");
        }

        [Test]
        public void ApplyAll_PublicOverload_AlwaysChecksAgainEvenWhenIncrementalCacheIsStale()
        {
            var rules = Rules(new LaterFolderRule());
            AssetDatabase.CreateFolder(TestRootFolder, "Later");
            SeedCache(LaterFolder, exists: false); // 実体は存在するのに Missing とキャッシュされた古い結果

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length,
                "公開の ApplyAll（Menu/CLI 経路）はキャッシュを読まず、常に最新の状態を検査する。");
        }

        [Test]
        public void ValidateAll_AlwaysChecksAgainEvenWhenIncrementalCacheIsStale()
        {
            var rules = Rules(new LaterFolderRule());
            AssetDatabase.CreateFolder(TestRootFolder, "Later");
            SeedCache(LaterFolder, exists: false);

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, rules);

            Assert.AreEqual(0, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
        }

        // --- Explain（5 引数版） ---

        [Test]
        public void Explain_DeclaredFolderMissing_ReturnsItInDeclaredFolderIssues()
        {
            RuleExplainService.Explain(new[] { StubAssetPath }, _settings, Rules(new RealFolderRule(), new MissingFolderRule()),
                out var configureFailures, out var declaredFolderIssues);

            Assert.AreEqual(0, configureFailures.Count);
            Assert.AreEqual(1, declaredFolderIssues.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderMissing, declaredFolderIssues[0].Status);
        }

        [Test]
        public void Explain_DeclaredFolderCaseMismatch_ReturnsItInDeclaredFolderIssues()
        {
            RuleExplainService.Explain(new[] { StubAssetPath }, _settings, Rules(new WrongCaseFolderRule()),
                out _, out var declaredFolderIssues);

            Assert.AreEqual(1, declaredFolderIssues.Count);
            Assert.AreEqual(ValidationStatus.DeclaredFolderCaseMismatch, declaredFolderIssues[0].Status);
        }

        // --- 増分経路のログ頻度 ---

        private static ValidationResult[] FolderIssue(ValidationStatus status, string message)
            => new[] { new ValidationResult(null, status, message) };

        [Test]
        public void ShouldLogDeclaredFolderIssues_SameContent_LogsOnlyOnce()
        {
            var issues = FolderIssue(ValidationStatus.DeclaredFolderMissing, "Folder 'A' does not exist.");

            Assert.IsTrue(AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(issues));
            Assert.IsFalse(AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(
                FolderIssue(ValidationStatus.DeclaredFolderMissing, "Folder 'A' does not exist.")));
        }

        [Test]
        public void ShouldLogDeclaredFolderIssues_ContentChanged_LogsAgain()
        {
            AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(FolderIssue(ValidationStatus.DeclaredFolderMissing, "Folder 'A' does not exist."));

            Assert.IsTrue(AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(
                FolderIssue(ValidationStatus.DeclaredFolderMissing, "Folder 'B' does not exist.")));
            Assert.IsTrue(AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(
                FolderIssue(ValidationStatus.DeclaredFolderCaseMismatch, "Folder 'B' does not exist.")),
                "Status だけが変わった場合も再ログする。");
        }

        [Test]
        public void ShouldLogDeclaredFolderIssues_ResolvedThenRecurs_LogsAgain()
        {
            var issues = FolderIssue(ValidationStatus.DeclaredFolderMissing, "Folder 'A' does not exist.");
            AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(issues);

            Assert.IsFalse(AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(new ValidationResult[0]), "解消時はログしない。");
            Assert.IsTrue(AddressTellerPostprocessor.ShouldLogDeclaredFolderIssues(issues), "解消後に同じ内容で再発したら再ログする。");
        }

        [Test]
        public void ApplyAllIncremental_MissingFolderOnly_DoesNotLogCleanupSkipWarningItself()
        {
            // 増分経路では、欠落だけが理由の「stale 掃除を止めた」警告を ApplyAll の内部では出さない
            // （import のたびに繰り返されないよう、Postprocessor が内容変化時にだけ出す）。
            // LogAssert の期待を置かないので、警告が出れば Unity が失敗にする。
            AddressTellerSettings.CleanupStaleEntries = true;
            AddStaleEntry();

            var issues = AddressTellerService.ApplyAllIncremental(new[] { StaleAssetPath }, _settings,
                Rules(new RealFolderRule(), new MissingFolderRule()));

            Assert.AreEqual(1, OfStatus(issues, ValidationStatus.DeclaredFolderMissing).Length);
        }
    }
}

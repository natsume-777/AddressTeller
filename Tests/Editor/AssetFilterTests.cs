using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    public class AssetFilterTests
    {
        private static AssetContext Ctx(string path) =>
            new AssetContext("guid1", path, typeof(GameObject));

        private static AssetContext FolderCtx(string path) =>
            new AssetContext("guid1", path, typeof(DefaultAsset), isFolder: true);

        [TestCase("Assets/Game/Player.prefab", false)]
        [TestCase("Assets/Game/Player.cs", true)]
        [TestCase("Assets/Game/lib.dll", true)]
        [TestCase("Assets/Game/Player.prefab.meta", true)]
        public void ExcludedExtension(string path, bool expected)
        {
            Assert.AreEqual(expected, AssetFilter.ShouldExclude(Ctx(path)));
        }

        [Test]
        public void EditorFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExclude(Ctx("Assets/Game/Editor/Foo.asset")));
        }

        [Test]
        public void NormalAsset_IsNotExcluded()
        {
            Assert.IsFalse(AssetFilter.ShouldExclude(Ctx("Assets/Game/Foo.prefab")));
        }

        [Test]
        public void AddressablesConfigFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExclude(
                Ctx("Assets/AddressableAssetsData/Settings.asset"),
                "Assets/AddressableAssetsData"));
        }

        [Test]
        public void AddressablesConfigFolder_NullMeansNoCheck()
        {
            Assert.IsFalse(AssetFilter.ShouldExclude(
                Ctx("Assets/AddressableAssetsData/Settings.asset"),
                null));
        }

        [Test]
        public void AddressablesConfigFolder_AdjacentFolderWithSamePrefix_IsExcluded()
        {
            // Addressables 本体（AddressableAssetUtility.IsPathValidForEntry）は境界なしの前方一致で
            // ConfigFolder を判定しており、隣接フォルダ（例: AddressableAssetsData_Backup）も区別せず
            // 除外する。AddressTeller はこれに意図的に揃えている（詳細は AssetFilter.IsPathValidForAddressablesEntry
            // の XML doc を参照）。
            Assert.IsTrue(AssetFilter.ShouldExclude(
                Ctx("Assets/AddressableAssetsData_Backup/Hero.prefab"),
                "Assets/AddressableAssetsData"));
        }

        // ShouldExcludeByPath は AssetContext 構築前にパス文字列のみで判定する早期除外用。
        // ShouldExclude(context, ...) のパス部分の判定結果と一致することを確認する。

        [TestCase("Assets/Game/Player.prefab", false)]
        [TestCase("Assets/Game/Player.cs", true)]
        [TestCase("Assets/Game/lib.dll", true)]
        [TestCase("Assets/Game/Player.prefab.meta", true)]
        public void ShouldExcludeByPath_ExcludedExtension_MatchesShouldExclude(string path, bool expected)
        {
            Assert.AreEqual(expected, AssetFilter.ShouldExcludeByPath(path));
            Assert.AreEqual(expected, AssetFilter.ShouldExclude(Ctx(path)));
        }

        [Test]
        public void ShouldExcludeByPath_EditorFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath("Assets/Game/Editor/Foo.asset"));
        }

        [Test]
        public void ShouldExcludeByPath_NormalAsset_IsNotExcluded()
        {
            Assert.IsFalse(AssetFilter.ShouldExcludeByPath("Assets/Game/Foo.prefab"));
        }

        [Test]
        public void ShouldExcludeByPath_AddressablesConfigFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData/Settings.asset",
                "Assets/AddressableAssetsData"));
        }

        [Test]
        public void ShouldExcludeByPath_AddressablesConfigFolder_NullMeansNoCheck()
        {
            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData/Settings.asset",
                null));
        }

        [Test]
        public void ShouldExcludeByPath_AdjacentFolderWithSamePrefix_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData_Backup/Hero.prefab",
                "Assets/AddressableAssetsData"));
        }

        [Test]
        public void ShouldExcludeByPath_BackslashConfigFolder_DoesNotMatchForwardSlashPath()
        {
            // AssetFilter は configFolder を渡されたとおりに使い、自分では正規化しない契約
            // （正規化は読み取り箇所である RuleEvaluationPipeline.BuildSetup / AddressTellerPostprocessor が
            // 1回だけ行う。詳細は各箇所のコメントを参照）。呼び出し側がバックスラッシュ区切りの
            // configFolder（settings.ConfigFolder が Windows で返しうる形）をそのまま渡すと、
            // フォワードスラッシュの実パスとは前方一致せず、ConfigFolder 配下として除外されない。
            // この契約を明文化しておくための回帰テスト。
            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(
                "Assets/AddressableAssetsData/Settings.asset",
                @"Assets\AddressableAssetsData"));
        }

        [TestCase(@"Assets\Game\Player.cs")]
        [TestCase(@"Assets\Game\Editor\Foo.asset")]
        [TestCase(@"Assets\AddressableAssetsData\Settings.asset")]
        [TestCase(@"Assets\Game\Player.prefab")]
        public void ShouldExcludeByPath_BackslashPath_MatchesForwardSlashEquivalent(string backslashPath)
        {
            var forwardPath = backslashPath.Replace('\\', '/');

            Assert.AreEqual(
                AssetFilter.ShouldExcludeByPath(forwardPath, "Assets/AddressableAssetsData"),
                AssetFilter.ShouldExcludeByPath(backslashPath, "Assets/AddressableAssetsData"));

            Assert.AreEqual(
                AssetFilter.ShouldExclude(Ctx(forwardPath), "Assets/AddressableAssetsData"),
                AssetFilter.ShouldExcludeByPath(backslashPath, "Assets/AddressableAssetsData"));
        }

        [Test]
        public void ShouldExcludeByPath_NullPath_ReturnsTrueWithoutThrowing()
        {
            // path.Replace('\\','/') は path が null だと NRE になるため、安全側（除外扱い）にフォールバックする。
            Assert.DoesNotThrow(() => AssetFilter.ShouldExcludeByPath(null));
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(null));
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath(null, "Assets/AddressableAssetsData"));
        }

        [Test]
        public void FolderNamedEditor_IsExcluded()
        {
            // "Editor" という名前のフォルダ自身は、IsPathValidForAddressablesEntry が拡張子なしパスとして
            // 判定するため ShouldExcludeByPath の時点で既に除外される（実際にフォルダかどうかは問わない。
            // 詳細は FolderNamedEditor_NoExtensionPath_IsExcludedRegardlessOfIsFolder を参照）。
            Assert.IsTrue(AssetFilter.ShouldExcludeByPath("Assets/Game/Editor"));
            Assert.IsTrue(AssetFilter.ShouldExclude(FolderCtx("Assets/Game/Editor")));
        }

        [Test]
        public void AssetsRootFolder_IsExcluded()
        {
            Assert.IsTrue(AssetFilter.ShouldExclude(FolderCtx("Assets")));
        }

        [Test]
        public void OrdinaryFolder_IsNotExcludedByDefault()
        {
            // フォルダの一律除外はしない。ルールが IncludeFolders() で opt-in すれば評価対象になる
            // (evaluator 側でのゲートは AssetFilter の責務外)。
            Assert.IsFalse(AssetFilter.ShouldExclude(FolderCtx("Assets/Game/Characters")));
        }

        [Test]
        public void FolderNamedEditor_NoExtensionPath_IsExcludedRegardlessOfIsFolder()
        {
            // IsPathValidForAddressablesEntry はパス文字列だけで「拡張子なし = フォルダ」とみなす
            // (Addressables 本体の AddressableAssetUtility.IsPathValidForEntry と同じ判定方式)。
            // そのため拡張子なしのファイル (IsFolder == false) が偶然 "Editor" という名前だった場合も、
            // 実際にフォルダかどうかを問わず除外される。AssetContext.IsFolder は
            // IncludeFolders() のオプトイン判定専用であり、この除外判定には関与しない。
            var fileCtx = new AssetContext("guid1", "Assets/Game/Editor", typeof(TextAsset), isFolder: false);

            Assert.IsTrue(AssetFilter.ShouldExclude(fileCtx));
        }

        [Test]
        public void ShouldExclude_TypeOnlyExclusion_NotCaughtByPathCheck()
        {
            // AddressableAssetSettings 型は ExcludedAddressablesTypes による除外であり、
            // パス自体は通常のアセットパスなので ShouldExcludeByPath では除外されない。
            var ctx = new AssetContext("guid1", "Assets/AddressableAssetsData/AddressableAssetSettings.asset", typeof(AddressableAssetSettings));

            Assert.IsFalse(AssetFilter.ShouldExcludeByPath(ctx.Path));
            Assert.IsTrue(AssetFilter.ShouldExclude(ctx));
        }

        // IsPathValidForAddressablesEntry の代表パス表。Addressables 本体（Groups ウィンドウのドラッグ、
        // Inspector の Addressable チェック）が受け付けないパスは、ここでも valid=false になっていなければならない。
        // ConfigFolder 判定は境界なしの前方一致（IsPathValidForAddressablesEntry_ConfigFolder_MatchesAddressables 等
        // で別途検証済み）だが、ここでは configFolder=null で ConfigFolder 以外のパス判定のみを確認する。
        [TestCase("Assets/Foo.asset", true)]
        [TestCase("Assets", false)]
        [TestCase("Assets/Foo/Editor", false)]
        [TestCase("Assets/Foo/Editor/Bar.asset", false)]
        [TestCase("Assets/Foo.asmdef", false)]
        [TestCase("Assets/Foo.preset", false)]
        [TestCase("Assets/Foo.cs", false)]
        [TestCase("Packages/com.example.pkg/package.json", false)]
        [TestCase("Packages/com.example.pkg/Runtime/Foo.asset", true)]
        [TestCase("ProjectSettings/TagManager.asset", false)]
        [TestCase("Library/BuildPlayer.prefs", false)]
        [TestCase("Assets/EditorArt/Editor", true)] // 手動トレース済み: 直前の "/EditorArt" が偽陽性として先にマッチし、
                                                      // 末尾の本物の "Editor" フォルダは "/Editor/"（末尾スラッシュ付き）の
                                                      // 再チェックでは検出されない。Addressables 本体もこの取りこぼしを
                                                      // 共有しており（意図的な忠実移植）、下の突き合わせテストで実測確認する。
        [TestCase("Assets/LICENSE", true)]
        [TestCase("Packages/com.example.pkg", false)]
        [TestCase("Packages/com.example.pkg/Sub/package.json", true)]
        [TestCase("packages/com.example.pkg/Runtime/Foo.asset", true)]
        [TestCase("Assets/Foo.PRESET", true)] // 拡張子比較が大文字小文字を区別するようになったため、除外されない。
        [TestCase("Assets/Foo.CS", true)] // 同上。
        public void IsPathValidForAddressablesEntry_RepresentativePaths(string path, bool expectedValid)
        {
            Assert.AreEqual(expectedValid, AssetFilter.IsPathValidForAddressablesEntry(path, null));
        }

        [Test]
        public void IsPathValidForAddressablesEntry_ConfigFolder_UsesUnboundedMatch()
        {
            // ConfigFolder 配下はもちろん、境界のない前方一致フォルダ（隣接フォルダ）も区別せず除外する。
            // 移植元（Addressables 本体の素の StartsWith）に意図的に揃えている。
            Assert.IsFalse(AssetFilter.IsPathValidForAddressablesEntry(
                "Assets/AddressableAssetsData/Settings.asset", "Assets/AddressableAssetsData"));
            Assert.IsFalse(AssetFilter.IsPathValidForAddressablesEntry(
                "Assets/AddressableAssetsData_Backup/Hero.prefab", "Assets/AddressableAssetsData"));
        }

        [Test]
        public void IsPathValidForAddressablesEntry_ConfigFolderItself_IsExcluded()
        {
            // ConfigFolder 自身（末尾 "/" 抜きの完全一致）も、その配下と同じ理由で除外する。
            Assert.IsFalse(AssetFilter.IsPathValidForAddressablesEntry(
                "Assets/AddressableAssetsData", "Assets/AddressableAssetsData"));
        }

        // Addressables 本体の internal メソッドと結果を突き合わせ、Addressables のアップデートで
        // 除外条件がずれた場合に検知できるようにする。メソッドが見つからない場合は Inconclusive で skip する。
        // ConfigFolder の判定は境界なしの前方一致で本体に揃えたため、ここでは configFolder を使わない
        // パスのみを対象にする（ConfigFolder 自体の突き合わせは
        // IsPathValidForAddressablesEntry_ConfigFolder_MatchesAddressables で別途行う。実プロジェクトの
        // AddressableAssetSettingsDefaultObject.Settings.ConfigFolder に依存するため分離している）。
        [TestCase("Assets/Foo.asset")]
        [TestCase("Assets")]
        [TestCase("Assets/Foo/Editor")]
        [TestCase("Assets/Foo/Editor/Bar.asset")]
        [TestCase("Assets/Foo.asmdef")]
        [TestCase("Assets/Foo.preset")]
        [TestCase("Assets/Foo.cs")]
        [TestCase("Packages/com.example.pkg/package.json")]
        [TestCase("Packages/com.example.pkg/Runtime/Foo.asset")]
        [TestCase("ProjectSettings/TagManager.asset")]
        [TestCase("Library/BuildPlayer.prefs")]
        [TestCase("Assets/EditorArt/Editor")]
        [TestCase("Assets/LICENSE")]
        [TestCase("Packages/com.example.pkg")]
        [TestCase("Packages/com.example.pkg/Sub/package.json")]
        [TestCase("packages/com.example.pkg/Runtime/Foo.asset")]
        [TestCase("Assets/Foo.PRESET")]
        [TestCase("Assets/Foo.CS")]
        public void IsPathValidForAddressablesEntry_MatchesAddressablesInternalImplementation(string path)
        {
            var method = typeof(AddressableAssetSettings).Assembly
                .GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetUtility")
                ?.GetMethod("IsPathValidForEntry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                Assert.Inconclusive("AddressableAssetUtility.IsPathValidForEntry not found via reflection; " +
                    "the Addressables version in this project may have changed its internal layout.");
                return;
            }

            var addressablesResult = (bool)method.Invoke(null, new object[] { path });
            var atResult = AssetFilter.IsPathValidForAddressablesEntry(path, null);

            Assert.AreEqual(addressablesResult, atResult,
                $"AddressTeller's ported judgement diverged from Addressables' internal IsPathValidForEntry for '{path}'.");
        }

        // ConfigFolder の判定を実プロジェクトの AddressableAssetSettingsDefaultObject.Settings.ConfigFolder に
        // 対して突き合わせる。ConfigFolder 自体・隣接フォルダの両方とも境界なしの前方一致で判定するようになった
        // ため、この2ケースだけは上の代表パス表と分離して実プロジェクト設定ありきで検証する。
        // 実プロジェクトに Addressables 設定が無い環境（AddressableAssetSettingsDefaultObject.Settings が null）
        // では検証しようがないため Inconclusive で skip する。
        [Test]
        public void IsPathValidForAddressablesEntry_ConfigFolder_MatchesAddressables()
        {
            var method = typeof(AddressableAssetSettings).Assembly
                .GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetUtility")
                ?.GetMethod("IsPathValidForEntry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                Assert.Inconclusive("AddressableAssetUtility.IsPathValidForEntry not found via reflection; " +
                    "the Addressables version in this project may have changed its internal layout.");
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Assert.Inconclusive("No AddressableAssetSettings is configured in this project; " +
                    "cannot verify ConfigFolder parity against the real settings object.");
                return;
            }

            // settings.ConfigFolder はそのまま(移植元と同じ生の区切り文字)で使う。移植元の
            // IsPathValidForEntry は受け取ったパスを OS のパス区切りへ変換してから ConfigFolder と比較するため、
            // ここで作るパスも settings.ConfigFolder と同じ区切り文字で連結しておけば、どちらの区切り文字の
            // 環境でも正しく前方一致する。
            var rawConfigFolder = settings.ConfigFolder;
            var underConfigFolderPath = rawConfigFolder + System.IO.Path.DirectorySeparatorChar + "AddressTellerConfigFolderProbe.asset";
            var adjacentFolderPath = rawConfigFolder + "_Backup" + System.IO.Path.DirectorySeparatorChar + "AddressTellerConfigFolderProbe.asset";

            // AddressTeller 側はフォワードスラッシュ正規化済みのパス・ConfigFolder を受け取る契約
            // (RuleEvaluationPipeline.BuildSetup が settings.ConfigFolder を正規化してから渡す)。
            var atConfigFolder = rawConfigFolder.Replace('\\', '/');

            foreach (var path in new[] { underConfigFolderPath, adjacentFolderPath })
            {
                var addressablesResult = (bool)method.Invoke(null, new object[] { path });
                var atResult = AssetFilter.IsPathValidForAddressablesEntry(path.Replace('\\', '/'), atConfigFolder);

                Assert.AreEqual(addressablesResult, atResult,
                    $"AddressTeller's ConfigFolder judgement diverged from Addressables' internal IsPathValidForEntry for '{path}'.");
            }
        }
    }
}

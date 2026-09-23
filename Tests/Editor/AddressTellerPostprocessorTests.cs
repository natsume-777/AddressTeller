using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerPostprocessor の純粋ロジック部分（ConfigFolder 早期リターン判定・削除済みアセットの
    /// GUID 解決フィルタ）を検証する。OnPostprocessAllAssets 自体は Unity のインポートフックからのみ
    /// 呼ばれる private static メソッドであり、実際のインポート/削除操作をテストコードから発生させるのは
    /// 実質的に不可能（EditMode テストから安定して postprocessor を再現よくトリガーできない）なため、
    /// その内部で使われている実質的なロジックを internal static メソッドとして抽出したうえで直接検証する。
    /// </summary>
    public class AddressTellerPostprocessorTests
    {
        private const string ConfigFolder = "Assets/AddressableAssetsData";

        // --- ShouldSkip ---

        [Test]
        public void ShouldSkip_NoChangedPaths_ReturnsTrue()
        {
            Assert.IsTrue(AddressTellerPostprocessor.ShouldSkip(ConfigFolder, new List<string>()));
        }

        [Test]
        public void ShouldSkip_AllChangedPathsInsideConfigFolder_ReturnsTrue()
        {
            var changed = new List<string>
            {
                $"{ConfigFolder}/AddressableAssetSettings.asset",
                $"{ConfigFolder}/AssetGroups/Default Local Group.asset",
            };

            Assert.IsTrue(AddressTellerPostprocessor.ShouldSkip(ConfigFolder, changed),
                "変更が全て ConfigFolder 配下の場合、処理はスキップされるべき。");
        }

        [Test]
        public void ShouldSkip_SomeChangedPathsOutsideConfigFolder_ReturnsFalse()
        {
            var changed = new List<string>
            {
                $"{ConfigFolder}/AddressableAssetSettings.asset",
                "Assets/Prefabs/Foo.prefab",
            };

            Assert.IsFalse(AddressTellerPostprocessor.ShouldSkip(ConfigFolder, changed),
                "ConfigFolder 外の変更が1件でも含まれる場合はスキップされないべき。");
        }

        [Test]
        public void ShouldSkip_AllChangedPathsOutsideConfigFolder_ReturnsFalse()
        {
            var changed = new List<string> { "Assets/Prefabs/Foo.prefab" };

            Assert.IsFalse(AddressTellerPostprocessor.ShouldSkip(ConfigFolder, changed));
        }

        [Test]
        public void ShouldSkip_AdjacentFolderSharingPrefix_ReturnsFalse()
        {
            // "Assets/AddressableAssetsDataOther/..." は文字列プレフィックスとしては ConfigFolder と
            // 一致するが、"/" 境界を跨がないため配下ではない（隣接フォルダの誤除外を避ける境界テスト）。
            var changed = new List<string> { $"{ConfigFolder}Other/Foo.asset" };

            Assert.IsFalse(AddressTellerPostprocessor.ShouldSkip(ConfigFolder, changed));
        }

        [Test]
        public void ShouldSkip_ConfigFolderPassedWithTrailingSlash_StillMatchesNestedPaths()
        {
            var changed = new List<string> { $"{ConfigFolder}/Nested/Foo.asset" };

            // configFolder 自体に末尾スラッシュが付いていても TrimEnd で正規化されるべき。
            Assert.IsTrue(AddressTellerPostprocessor.ShouldSkip(ConfigFolder + "/", changed));
        }

        [Test]
        public void ShouldSkip_BackslashConfigFolder_DoesNotMatchForwardSlashPaths()
        {
            // ShouldSkip 自身は configFolder を渡されたとおりに使い、正規化しない契約
            // （正規化は呼び出し元の OnPostprocessAllAssets が settings.ConfigFolder を読み取る箇所で
            // 1回だけ行う。settings.ConfigFolder は Windows ではバックスラッシュ区切りで返ることがあるため、
            // 正規化を怠ると変更パス（常にフォワードスラッシュ）と前方一致しなくなる）。
            // OnPostprocessAllAssets 自体は直接テストできないため、この契約をここで固定しておく。
            var changed = new List<string> { $"{ConfigFolder}/Nested/Foo.asset" };

            Assert.IsFalse(AddressTellerPostprocessor.ShouldSkip(ConfigFolder.Replace('/', '\\'), changed));
        }

        [Test]
        public void ShouldSkip_NullConfigFolder_ReturnsFalseWithoutThrowing()
        {
            // configFolder は呼び出し元で settings.ConfigFolder?.Replace(...) を経由するため null になりうる
            // （settings.ConfigFolder 自体が null を返す異常系）。ConfigFolder 配下かどうかを判定できない以上、
            // 安全側（スキップしない）に倒すべきで、TrimEnd で NullReferenceException も投げてはならない。
            var changed = new List<string> { "Assets/Prefabs/Foo.prefab" };

            Assert.DoesNotThrow(() => AddressTellerPostprocessor.ShouldSkip(null, changed));
            Assert.IsFalse(AddressTellerPostprocessor.ShouldSkip(null, changed));
        }

        // --- ShouldLogDuplicateAssetEntries ---

        [TearDown]
        public void TearDown()
        {
            // static な記憶をテスト間で持ち越さない。
            AddressTellerPostprocessor.s_lastLoggedDuplicateSignature = null;
        }

        private static ValidationResult DuplicateEntry(string message) =>
            new(null, ValidationStatus.DuplicateAssetEntry, message);

        [Test]
        public void ShouldLogDuplicateAssetEntries_Empty_ReturnsFalse()
        {
            AddressTellerPostprocessor.s_lastLoggedDuplicateSignature = "leftover-from-previous-import";

            var shouldLog = AddressTellerPostprocessor.ShouldLogDuplicateAssetEntries(new List<ValidationResult>());

            Assert.IsFalse(shouldLog);
            Assert.IsNull(AddressTellerPostprocessor.s_lastLoggedDuplicateSignature,
                "重複が解消されたら、次に同じ内容の重複が再発したときに改めてログできるよう記憶をリセットするべき。");
        }

        [Test]
        public void ShouldLogDuplicateAssetEntries_FirstOccurrence_ReturnsTrue()
        {
            var duplicates = new List<ValidationResult> { DuplicateEntry("guid-a duplicate") };

            var shouldLog = AddressTellerPostprocessor.ShouldLogDuplicateAssetEntries(duplicates);

            Assert.IsTrue(shouldLog);
        }

        [Test]
        public void ShouldLogDuplicateAssetEntries_SameContentAsLastLog_ReturnsFalse()
        {
            var duplicates = new List<ValidationResult> { DuplicateEntry("guid-a duplicate") };
            AddressTellerPostprocessor.ShouldLogDuplicateAssetEntries(duplicates); // 1回目（ログする想定）

            // 同じ内容（同じ Message 集合）でもう一度 import が走った状態を模す（削除を伴う import で
            // ApplyAll/RemoveEntriesForDeletedAssets の両方が同じ状態を検出するケースにも相当する）。
            var shouldLogAgain = AddressTellerPostprocessor.ShouldLogDuplicateAssetEntries(
                new List<ValidationResult> { DuplicateEntry("guid-a duplicate") });

            Assert.IsFalse(shouldLogAgain, "内容が変わらない間は再ログしないべき。");
        }

        [Test]
        public void ShouldLogDuplicateAssetEntries_ContentChanged_ReturnsTrueAgain()
        {
            AddressTellerPostprocessor.ShouldLogDuplicateAssetEntries(
                new List<ValidationResult> { DuplicateEntry("guid-a duplicate") });

            var shouldLog = AddressTellerPostprocessor.ShouldLogDuplicateAssetEntries(
                new List<ValidationResult> { DuplicateEntry("guid-b duplicate") });

            Assert.IsTrue(shouldLog, "重複の内容（対象 guid 等）が変われば、改めてログするべき。");
        }

        // --- ResolveDeletedGuids ---

        [Test]
        public void ResolveDeletedGuids_AllResolve_ReturnsAllGuidsInOrder()
        {
            var deleted = new List<string> { "Assets/A.prefab", "Assets/B.prefab" };

            var guids = AddressTellerPostprocessor.ResolveDeletedGuids(deleted, p => p + "-guid").ToList();

            CollectionAssert.AreEqual(new[] { "Assets/A.prefab-guid", "Assets/B.prefab-guid" }, guids);
        }

        [Test]
        public void ResolveDeletedGuids_SomeUnresolvable_FiltersOutEmptyGuid()
        {
            var deleted = new List<string> { "Assets/A.prefab", "Assets/Unresolvable.prefab", "Assets/B.prefab" };

            var guids = AddressTellerPostprocessor.ResolveDeletedGuids(
                deleted,
                p => p == "Assets/Unresolvable.prefab" ? string.Empty : p + "-guid").ToList();

            CollectionAssert.AreEqual(new[] { "Assets/A.prefab-guid", "Assets/B.prefab-guid" }, guids,
                "GUID解決に失敗した（空文字を返した）パスは除外されるべき。");
        }

        [Test]
        public void ResolveDeletedGuids_NullResolvedGuid_IsFilteredOut()
        {
            var deleted = new List<string> { "Assets/A.prefab" };

            var guids = AddressTellerPostprocessor.ResolveDeletedGuids(deleted, p => null).ToList();

            Assert.AreEqual(0, guids.Count, "GUID解決が null を返した場合も除外されるべき。");
        }

        [Test]
        public void ResolveDeletedGuids_EmptyInput_ReturnsEmpty()
        {
            var guids = AddressTellerPostprocessor.ResolveDeletedGuids(new List<string>(), p => p).ToList();

            Assert.AreEqual(0, guids.Count);
        }
    }
}

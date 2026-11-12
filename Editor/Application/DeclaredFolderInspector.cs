using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary><see cref="DeclaredFolderInspector.Check"/> が返す、宣言フォルダの実在状態。</summary>
    internal enum DeclaredFolderState
    {
        /// <summary>宣言と完全に同じ大小文字で実在する。</summary>
        Exists,

        /// <summary>大小文字だけが異なる名前で実在する（<see cref="DeclaredFolderCheck.ActualPath"/> が実際の名前）。</summary>
        CaseMismatch,

        /// <summary>実在しない。</summary>
        Missing,
    }

    internal readonly struct DeclaredFolderCheck
    {
        public DeclaredFolderState State { get; }

        /// <summary>State が Exists / CaseMismatch のときの実際のフォルダパス。Missing では null。</summary>
        public string ActualPath { get; }

        public DeclaredFolderCheck(DeclaredFolderState state, string actualPath)
        {
            State = state;
            ActualPath = actualPath;
        }
    }

    /// <summary>
    /// ルールの条件が依存すると宣言したフォルダ（<see cref="AssetCondition.Folders"/> /
    /// <see cref="AddressRuleEntry.DeclaredFolders"/>）の GUID 解決と実在検査を行う。
    /// 実在・大小文字の判定は <c>AssetDatabase.AssetPathToGUID</c> の大小文字感度に頼らず、
    /// 親フォルダの <c>AssetDatabase.GetSubFolders</c> の列挙結果を Ordinal で比較して行う。
    /// 判定ロジックは AssetDatabase を直接呼ばず delegate を受け取るため、単体でテストできる。
    /// </summary>
    internal static class DeclaredFolderInspector
    {
        /// <summary>プロジェクトのアセットフォルダの根。宣言パスの第1セグメントはこのどちらかでなければならない。</summary>
        private static readonly string[] Roots = { "Assets", "Packages" };

        /// <summary>
        /// 宣言パス（Ordinal）→ 検査結果。増分適用（Postprocessor 経由）が import のたびに同じフォルダを
        /// 検査し直さないための、ドメインリロードまで生きるキャッシュ。読むのは増分経路だけ
        /// （フルスキャンは常に検査し直し、結果でキャッシュを更新する）。フォルダの作成・削除・移動は
        /// Postprocessor が <see cref="InvalidateCache"/> で反映する。
        /// </summary>
        private static readonly Dictionary<string, DeclaredFolderCheck> s_cache =
            new Dictionary<string, DeclaredFolderCheck>(StringComparer.Ordinal);

        /// <summary>キャッシュを空にする（テスト用。本体では <see cref="InvalidateCache"/> を使う）。</summary>
        internal static void ResetCache() => s_cache.Clear();

        /// <summary>
        /// GUID で宣言されたフォルダ（<see cref="Match.InFolderByGuid"/>）を現在のパスへ解決する。
        /// 解決結果のキャッシュ（<see cref="AssetDatabaseFolderGuidResolver"/>）は、このランの最初に捨てて引き直す。
        /// </summary>
        public static IReadOnlyList<AddressRuleEntry> ResolveGuids(IReadOnlyList<AddressRuleEntry> entries, List<ValidationResult> failures)
        {
            AssetDatabaseFolderGuidResolver.ClearCache();
            return ResolveGuids(entries, failures, AssetDatabaseFolderGuidResolver.ResolveCached);
        }

        /// <summary>
        /// 解決できた GUID の現在のパスを <see cref="AddressRuleEntry.DeclaredFolders"/> に加えたエントリへ差し替えて
        /// 返す（GroupDefault() のセンチネル解決と同じ構造）。条件の述語自体は評価のたびに
        /// <see cref="FolderGuidResolver"/> から GUID を引くので、ここでは述語を触らない。フォルダに引けない GUID は
        /// <see cref="ValidationStatus.DeclaredFolderMissing"/> を <paramref name="failures"/> へ GUID ごとに1件積む。
        /// GUID 宣言が1件も無ければ <paramref name="entries"/> をそのまま返す。
        /// </summary>
        /// <param name="resolve">guid → フォルダの現在のパス。フォルダに引けなければ null または空。</param>
        internal static IReadOnlyList<AddressRuleEntry> ResolveGuids(
            IReadOnlyList<AddressRuleEntry> entries,
            List<ValidationResult> failures,
            Func<string, string> resolve)
        {
            if (!entries.Any(e => e.FolderGuids.Count > 0)) return entries;

            var result = new List<AddressRuleEntry>(entries.Count);
            var failedGuids = new List<string>();
            var failedSources = new Dictionary<string, List<AddressRuleEntry>>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                if (entry.FolderGuids.Count == 0)
                {
                    result.Add(entry);
                    continue;
                }

                var declared = new List<string>(entry.DeclaredFolders);
                foreach (var declaration in entry.FolderGuids)
                {
                    var path = resolve(declaration.Guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        AddSource(failedGuids, failedSources, declaration.Guid, entry);
                        continue;
                    }

                    declared.Add(AssetCondition.NormalizeFolder(path));
                }

                result.Add(entry.WithDeclaredFolders(declared));
            }

            foreach (var guid in failedGuids)
            {
                failures.Add(new ValidationResult(
                    null,
                    ValidationStatus.DeclaredFolderMissing,
                    $"Folder with GUID '{guid}' declared by {FormatSources(failedSources[guid])} does not resolve to a folder in the project. Cleanup of unmatched entries is suspended while this is present."));
            }

            return result;
        }

        /// <summary>宣言フォルダを検査する。</summary>
        public static List<ValidationResult> Inspect(IReadOnlyList<AddressRuleEntry> entries, bool useCache)
            => Inspect(entries, useCache, AssetDatabase.GetSubFolders, AssetDatabase.IsValidFolder);

        /// <summary>
        /// <paramref name="entries"/> の宣言フォルダを重複を除いて検査し、実在しなければ
        /// <see cref="ValidationStatus.DeclaredFolderMissing"/>、大小文字だけ違えば
        /// <see cref="ValidationStatus.DeclaredFolderCaseMismatch"/> を、フォルダごとに1件（宣言したルールを
        /// メッセージに列挙）返す。並びはエントリ内の宣言の登場順。
        /// </summary>
        /// <param name="useCache">
        /// true のとき、キャッシュ済みのフォルダは検査し直さない（増分適用用）。false でも結果はキャッシュへ書き込む。
        /// </param>
        internal static List<ValidationResult> Inspect(
            IReadOnlyList<AddressRuleEntry> entries,
            bool useCache,
            Func<string, string[]> getSubFolders,
            Func<string, bool> isValidFolder)
        {
            var results = new List<ValidationResult>();

            var folders = new List<string>();
            // 宣言したルールの識別名は、Missing / CaseMismatch を報告するフォルダについてだけ組み立てる
            // （キャッシュヒットで何も出さない増分適用で、import のたびに文字列を作らないため）。
            var sources = new Dictionary<string, List<AddressRuleEntry>>(StringComparer.Ordinal);
            foreach (var entry in entries)
                foreach (var folder in entry.DeclaredFolders)
                    AddSource(folders, sources, folder, entry);

            if (folders.Count == 0) return results;

            // 同じ親の列挙を宣言ごとにやり直さないよう、このランの間だけ GetSubFolders の結果を使い回す。
            var subFolderMemo = new Dictionary<string, string[]>(StringComparer.Ordinal);
            string[] MemoizedSubFolders(string parent)
            {
                if (!subFolderMemo.TryGetValue(parent, out var subs))
                {
                    subs = getSubFolders(parent);
                    subFolderMemo[parent] = subs;
                }
                return subs;
            }

            foreach (var folder in folders)
            {
                DeclaredFolderCheck check;
                if (!useCache || !s_cache.TryGetValue(folder, out check))
                {
                    check = Check(folder, MemoizedSubFolders, isValidFolder);
                    s_cache[folder] = check;
                }

                switch (check.State)
                {
                    case DeclaredFolderState.Missing:
                        results.Add(new ValidationResult(
                            null,
                            ValidationStatus.DeclaredFolderMissing,
                            $"Folder '{folder}' declared by {FormatSources(sources[folder])} does not exist. Cleanup of unmatched entries is suspended while this is present."));
                        break;
                    case DeclaredFolderState.CaseMismatch:
                        results.Add(new ValidationResult(
                            null,
                            ValidationStatus.DeclaredFolderCaseMismatch,
                            $"Folder '{folder}' declared by {FormatSources(sources[folder])} exists as '{check.ActualPath}', which differs only in letter case. The rule still matches (folder matching ignores case); correct the declaration to the actual name."));
                        break;
                }
            }

            return results;
        }

        /// <summary>
        /// 宣言パス（"/" 区切り・末尾 "/" 無し）を第1セグメントから順に、各階層の親フォルダの
        /// <paramref name="getSubFolders"/> の列挙結果と Ordinal で比較して辿る。完全一致で辿り切れれば Exists、
        /// どこかで大小文字無視でのみ一致すれば CaseMismatch、辿れなければ Missing。
        /// </summary>
        /// <param name="isValidFolder">
        /// 辿れなかった場合の最終確認。<c>GetSubFolders</c> が列挙しない種類のフォルダ（パッケージ内など）で
        /// 実在するのに Missing と誤判定し、blocking な通知と削除停止を起こさないための安全網。
        /// この確認で見つかった場合は大小文字の検査はできないため Exists として扱う。
        /// </param>
        internal static DeclaredFolderCheck Check(string folderPath, Func<string, string[]> getSubFolders, Func<string, bool> isValidFolder)
        {
            var segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return new DeclaredFolderCheck(DeclaredFolderState.Missing, null);

            string root = null;
            foreach (var candidate in Roots)
            {
                if (string.Equals(candidate, segments[0], StringComparison.OrdinalIgnoreCase))
                {
                    root = candidate;
                    break;
                }
            }

            if (root == null) return Fallback(folderPath, isValidFolder);

            var caseDiffers = !string.Equals(root, segments[0], StringComparison.Ordinal);
            var actual = root;

            for (var i = 1; i < segments.Length; i++)
            {
                var expected = actual + "/" + segments[i];
                string exact = null;
                string ignoreCase = null;

                var subs = getSubFolders(actual) ?? Array.Empty<string>();
                foreach (var sub in subs)
                {
                    var normalized = sub.Replace('\\', '/');
                    if (string.Equals(normalized, expected, StringComparison.Ordinal))
                    {
                        exact = normalized;
                        break;
                    }

                    if (ignoreCase == null && string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase))
                        ignoreCase = normalized;
                }

                if (exact != null)
                {
                    actual = exact;
                }
                else if (ignoreCase != null)
                {
                    actual = ignoreCase;
                    caseDiffers = true;
                }
                else
                {
                    return Fallback(folderPath, isValidFolder);
                }
            }

            return new DeclaredFolderCheck(caseDiffers ? DeclaredFolderState.CaseMismatch : DeclaredFolderState.Exists, actual);
        }

        private static DeclaredFolderCheck Fallback(string folderPath, Func<string, bool> isValidFolder)
            => isValidFolder(folderPath)
                ? new DeclaredFolderCheck(DeclaredFolderState.Exists, folderPath)
                : new DeclaredFolderCheck(DeclaredFolderState.Missing, null);

        /// <summary>
        /// 変更されたアセットパス（import / delete / move の移動先・移動元）に、キャッシュ済みの宣言フォルダ自身
        /// またはその祖先が含まれていれば、そのキャッシュを捨てる。フォルダの作成・削除・改名・親ごとの移動は
        /// いずれも import / delete / move のいずれかのパスに現れるため、これで検査結果が実態から外れない。
        /// キャッシュが空なら <paramref name="changedPaths"/> を列挙しない。
        /// </summary>
        public static void InvalidateCache(IEnumerable<string> changedPaths)
        {
            if (s_cache.Count == 0) return;

            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in changedPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                changed.Add(AssetCondition.NormalizeFolder(path));
            }

            if (changed.Count == 0) return;

            List<string> stale = null;
            foreach (var key in s_cache.Keys)
            {
                // key 自身と、各祖先（"A/B/C" なら "A/B/C"、"A/B"、"A"）のいずれかが変更されたか。
                var candidate = key;
                while (true)
                {
                    if (changed.Contains(candidate))
                    {
                        (stale ??= new List<string>()).Add(key);
                        break;
                    }

                    var slash = candidate.LastIndexOf('/');
                    if (slash <= 0) break;
                    candidate = candidate.Substring(0, slash);
                }
            }

            if (stale == null) return;
            foreach (var key in stale)
                s_cache.Remove(key);
        }

        /// <summary>key の登場順を保ちつつ、key ごとに宣言したエントリを集める。</summary>
        private static void AddSource(List<string> keyOrder, Dictionary<string, List<AddressRuleEntry>> sourcesByKey, string key, AddressRuleEntry entry)
        {
            if (!sourcesByKey.TryGetValue(key, out var sources))
            {
                sources = new List<AddressRuleEntry>();
                sourcesByKey[key] = sources;
                keyOrder.Add(key);
            }

            sources.Add(entry);
        }

        private const int MaxListedSources = 3;

        /// <summary>宣言したエントリのルール識別名を重複なしで先頭から並べ、<see cref="MaxListedSources"/> 件を超える分は件数にまとめる。</summary>
        private static string FormatSources(List<AddressRuleEntry> entries)
        {
            var names = new List<string>();
            foreach (var entry in entries)
            {
                var name = AddressRuleEntry.DescribeSource(entry.SourceClass, entry.Description, entry.RuleIndex);
                if (!names.Contains(name)) names.Add(name);
            }

            if (names.Count <= MaxListedSources) return string.Join(", ", names);
            return $"{string.Join(", ", names.Take(MaxListedSources))} and {names.Count - MaxListedSources} more";
        }
    }
}

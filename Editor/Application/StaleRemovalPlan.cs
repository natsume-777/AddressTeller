using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 1回のランで行う stale エントリ削除（無マッチ判定に基づくもの・無効パスの掃除）を、実行前にまとめて
    /// 保持する計画。ApplyAll は削除をその場で行わず、ラン末尾でこの計画を確定してから一括で実行する——
    /// 削除件数が <see cref="AddressTellerSettings.MaxStaleRemovalsPerRun"/> を超える場合に全件を見送れるようにするため
    /// （一部だけ削除済みの状態で超過に気付くことがないようにする）。
    /// </summary>
    internal sealed class StaleRemovalPlan
    {
        /// <summary>削除理由。実行時のログ文言を切り替える。</summary>
        private enum Reason
        {
            NoMatchingRule,
            InvalidPath,
        }

        private readonly struct Target
        {
            public AddressableAssetEntry Entry { get; }
            public Reason Reason { get; }

            public Target(AddressableAssetEntry entry, Reason reason)
            {
                Entry = entry;
                Reason = reason;
            }
        }

        /// <summary>メッセージに含める先頭パスの最大件数。</summary>
        private const int MessagePathCount = 5;

        private readonly List<Target> _targets = new();

        // 同じエントリを2経路（無マッチ判定と無効パス掃除）で拾っても1件として数えるための guid 集合。
        private readonly HashSet<string> _guids = new(StringComparer.Ordinal);

        /// <summary>削除予定の件数。</summary>
        public int Count => _targets.Count;

        /// <summary>どのルールにもマッチしなくなったアセットのエントリを削除予定に加える。</summary>
        public void AddUnmatched(AddressableAssetEntry entry) => Add(entry, Reason.NoMatchingRule);

        /// <summary>パスが Addressables のエントリとして構造的に無効になったエントリを削除予定に加える。</summary>
        public void AddInvalidPath(AddressableAssetEntry entry) => Add(entry, Reason.InvalidPath);

        private void Add(AddressableAssetEntry entry, Reason reason)
        {
            if (entry == null) return;
            if (!_guids.Add(entry.guid)) return;
            _targets.Add(new Target(entry, reason));
        }

        /// <summary>
        /// 計画した全エントリを削除する。削除ごとに個別の Warning ログを出す。
        /// 上限判定は呼び出し側の責務（このメソッドは判定しない）。
        /// </summary>
        /// <returns>削除したエントリの一覧（計画が空なら空リスト）。</returns>
        public List<ClearedEntry> Execute(AddressableAssetSettings settings)
        {
            var cleared = new List<ClearedEntry>(_targets.Count);
            foreach (var target in _targets)
            {
                var removed = RemoveLogged(settings, target.Entry, target.Reason);
                if (removed.HasValue) cleared.Add(removed.Value);
            }
            return cleared;
        }

        /// <summary>
        /// 資産削除への追従など、計画を介さず1件だけ削除する経路用。<see cref="Execute"/> と同じログを出す。
        /// </summary>
        public static ClearedEntry? RemoveNoMatchingRuleEntry(AddressableAssetSettings settings, AddressableAssetEntry entry)
            => RemoveLogged(settings, entry, Reason.NoMatchingRule);

        /// <returns>削除したエントリ。<c>RemoveAssetEntry</c> が false（実際には削除されなかった）なら null。</returns>
        private static ClearedEntry? RemoveLogged(AddressableAssetSettings settings, AddressableAssetEntry entry, Reason reason)
        {
            var guid = entry.guid;
            var groupName = entry.parentGroup?.Name ?? "";
            var labels = entry.labels.OrderBy(l => l, StringComparer.Ordinal).ToList();
            var cleared = new ClearedEntry(guid, entry.address, groupName, labels);

            if (reason == Reason.InvalidPath)
                UnityEngine.Debug.LogWarning($"[AddressTeller] Removing stale entry: guid={guid}, group='{groupName}', address='{entry.address}', path='{entry.AssetPath}', labels=[{string.Join(", ", labels)}] (path is not valid for an Addressables entry).");
            else
                UnityEngine.Debug.LogWarning($"[AddressTeller] Removing stale entry: guid={guid}, group='{groupName}', address='{entry.address}', path='{entry.AssetPath}' (no longer matched by any rule).");

            return settings.RemoveAssetEntry(guid) ? cleared : (ClearedEntry?)null;
        }

        /// <summary>
        /// 削除予定が上限を超えたことを報告する <see cref="ValidationStatus.StaleRemovalLimitExceeded"/> を組み立てる。
        /// 先頭数件のパスは Ordinal 順（同じ入力なら同じメッセージになるように）。パスが引けないエントリは guid で示す。
        /// </summary>
        public ValidationResult BuildLimitExceededResult(int limit)
        {
            var samples = _targets
                .Select(t => string.IsNullOrEmpty(t.Entry.AssetPath) ? $"(guid {t.Entry.guid})" : t.Entry.AssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Take(MessagePathCount)
                .ToList();
            var more = _targets.Count > samples.Count ? ", ..." : "";

            return new ValidationResult(
                null,
                ValidationStatus.StaleRemovalLimitExceeded,
                $"Skipped removing {_targets.Count} stale entries: more than the limit of {limit} (Max stale removals per run). " +
                $"No entries were removed. First: {string.Join(", ", samples)}{more}. " +
                "Review the removals with Apply All from the menu, then raise or clear the limit if they are expected.");
        }
    }
}

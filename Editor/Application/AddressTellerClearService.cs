using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor
{
    /// <summary>クリア対象のスコープ。</summary>
    public enum ClearScope
    {
        /// <summary>全 Addressable エントリを対象とする。</summary>
        All,

        /// <summary>AddressTeller が管理するグループ（いずれかのルールが GroupName として参照しているグループ）に属するエントリのみを対象とする。</summary>
        Managed,
    }

    /// <summary>クリアによって削除された1エントリの情報。ログ出力・スナップショット説明等に使う。</summary>
    public readonly struct ClearedEntry
    {
        public string Guid { get; }
        public string Address { get; }
        public string GroupName { get; }
        public IReadOnlyList<string> Labels { get; }

        public ClearedEntry(string guid, string address, string groupName, IReadOnlyList<string> labels)
        {
            Guid = guid;
            Address = address;
            GroupName = groupName;
            Labels = labels;
        }
    }

    /// <summary>
    /// Addressables の全エントリ（または AddressTeller 管理下のエントリ）を一括削除する。
    /// 既定の安全側運用（資産単位の所有権判定・デフォルト OFF）から意図的に逸脱した、
    /// 公開前パッケージの初期セットアップ用途向けの割り切り機能。
    /// 呼び出し側で削除前にスナップショットを取得し、戻り値を Warning 以上で個別ログすることを前提とする。
    /// </summary>
    public static class AddressTellerClearService
    {
        /// <summary>
        /// <paramref name="settings"/> から対象エントリを削除する。
        /// <paramref name="scope"/> が <see cref="ClearScope.Managed"/> の場合、<paramref name="managedGroups"/> は必須
        /// （null の場合は <see cref="ArgumentNullException"/>）。
        /// 削除前の各エントリの情報を <see cref="ClearedEntry"/> として GroupName→Guid の Ordinal 昇順で返す。
        /// このメソッド自体はログを出力しない（呼び出し側の責務）。
        /// </summary>
        public static IReadOnlyList<ClearedEntry> Clear(
            AddressableAssetSettings settings,
            ClearScope scope,
            IReadOnlyCollection<string> managedGroups = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (scope == ClearScope.Managed && managedGroups == null)
                throw new ArgumentNullException(nameof(managedGroups), "managedGroups must be provided when scope is ClearScope.Managed.");

            var targets = new List<(string Guid, string Address, string GroupName, IReadOnlyList<string> Labels)>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (scope == ClearScope.Managed && !managedGroups.Contains(group.Name)) continue;

                foreach (var entry in group.entries)
                {
                    targets.Add((
                        entry.guid,
                        entry.address,
                        group.Name,
                        entry.labels.OrderBy(l => l, StringComparer.Ordinal).ToList()));
                }
            }

            var ordered = targets
                .OrderBy(t => t.GroupName, StringComparer.Ordinal)
                .ThenBy(t => t.Guid, StringComparer.Ordinal)
                .ToList();

            var cleared = new List<ClearedEntry>(ordered.Count);
            foreach (var target in ordered)
            {
                cleared.Add(new ClearedEntry(target.Guid, target.Address, target.GroupName, target.Labels));
                settings.RemoveAssetEntry(target.Guid);
            }

            return cleared;
        }
    }
}

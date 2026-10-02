using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 同一アセット（GUID）が2つ以上の Addressables グループに同時にエントリを持つ状態を検出する。
    /// VCS のマージ等で混入しうる。Addressables 2.8.1 では、グループアセットの import 時
    /// （AddressableAssetSettings.OnPostprocessAllAssets → AddressableAssetGroup.DedupeEnteries）に、
    /// import されたグループ側のエントリのうち FindAssetEntry が別グループのエントリを返すものが外される
    /// ことがある（FindAssetEntry は settings.groups の並びで先に見つかった方を返し、キャッシュもある）。
    /// ただしこれは import されたグループに対してだけ走るため、常に解消されるわけではなく、この検出器が
    /// 発火する時点で重複は残っている。問題の核は、既存の重複エントリのうちどれがそのアセットのエントリか
    /// （アドレス・ラベル・手動編集を含む）が一意に決まらず、どれが生き残る/参照されるかがルールと無関係に
    /// グループの並び順等で決まってしまうこと。
    /// また、この状態で guid をキーにした Dictionary を組み立てようとすると例外になる
    /// （RuleEvaluationPipeline.BuildPredictedRunState の afterMap 構築等）ため、それより前にここで検出して
    /// 評価そのものを止める。
    /// 検出専用（書き込みは一切行わない）。
    /// </summary>
    internal static class DuplicateAssetEntryDetector
    {
        /// <summary>
        /// settings 内の全エントリを guid でグルーピングし、2つ以上のグループにまたがって存在する guid ごとに
        /// 1件の <see cref="ValidationStatus.DuplicateAssetEntry"/> を返す（0件なら空リスト）。
        /// settings.groups の null 要素・guid が空のエントリは除外する。戻り値は guid の Ordinal 昇順で
        /// 決定的に並べる。
        /// この呼び出しは import のたびに走る Postprocessor 経由でも呼ばれるため、圧倒的多数を占める
        /// 「重複していない」guid については素性を1件記録するだけに留め、実際に2件目が見つかった guid に
        /// ついてのみ List を確保する（全 guid ぶんの List を毎回アロケートしない）。
        /// </summary>
        public static IReadOnlyList<ValidationResult> Detect(AddressableAssetSettings settings)
        {
            var firstLocationByGuid = new Dictionary<string, (string GroupName, string Address)>(StringComparer.Ordinal);
            Dictionary<string, List<(string GroupName, string Address)>> duplicatesByGuid = null;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.guid)) continue;

                    var location = (group.Name, entry.address);

                    if (duplicatesByGuid != null && duplicatesByGuid.TryGetValue(entry.guid, out var locations))
                    {
                        locations.Add(location);
                        continue;
                    }

                    if (firstLocationByGuid.TryGetValue(entry.guid, out var firstLocation))
                    {
                        // ちょうど2件目が見つかった瞬間。ここで初めて List を確保し、1件目・2件目をまとめる。
                        duplicatesByGuid ??= new Dictionary<string, List<(string GroupName, string Address)>>(StringComparer.Ordinal);
                        duplicatesByGuid[entry.guid] = new List<(string, string)> { firstLocation, location };
                        continue;
                    }

                    firstLocationByGuid[entry.guid] = location;
                }
            }

            if (duplicatesByGuid == null) return Array.Empty<ValidationResult>();

            var results = new List<ValidationResult>(duplicatesByGuid.Count);
            foreach (var guid in duplicatesByGuid.Keys.OrderBy(g => g, StringComparer.Ordinal))
                results.Add(BuildResult(guid, duplicatesByGuid[guid]));

            return results;
        }

        /// <summary>1件の重複 guid につき、関与する全グループ・アドレスを列挙した ValidationResult を組み立てる。</summary>
        private static ValidationResult BuildResult(string guid, IReadOnlyList<(string GroupName, string Address)> locations)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var displayPath = string.IsNullOrEmpty(path) ? "(path could not be resolved)" : path;

            var sorted = locations
                .OrderBy(l => l.GroupName, StringComparer.Ordinal)
                .ThenBy(l => l.Address, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.Append($"Asset guid={guid} ({displayPath}) has an entry in {sorted.Count} groups at once, so it is ambiguous which entry is this asset's entry, and which one survives or gets looked up is decided independently of your rules (for example by the order of the groups):");
            foreach (var location in sorted)
                sb.Append($"\n  group '{location.GroupName}' address='{location.Address}'");
            sb.Append("\nNote each entry's address and labels first (labels not assigned by a rule will not come back on their own), then remove the entries for this asset from every group listed above in the Addressables Groups window, then add the asset back to the group it should belong to (or run AddressTeller again to let your rules recreate it). See the Troubleshooting section of Documentation~/operations.md in the AddressTeller package.");

            return new ValidationResult(null, ValidationStatus.DuplicateAssetEntry, sb.ToString());
        }

        /// <summary>
        /// <see cref="Detect"/> を呼び、重複があれば各件を Error でログしたうえで <paramref name="context"/>
        /// が中止された旨のサマリ行も1件ログして true を返す（呼び出し元は何も書き込まず中止すること）。
        /// UndoLastApply・Save Snapshot・ClearAll/ClearCLI・Apply All/Apply with Validate の自動スナップショット・
        /// Snapshot Manager の Restore など、この状態を検出したら書き込みを一切行わず中止する入口が共通で使う
        /// （メッセージの文言をここに集約し、入口ごとの表記ゆれを避ける）。重複が無ければ何もログせず false を返す。
        /// </summary>
        public static bool LogAndReturnTrueIfDuplicates(AddressableAssetSettings settings, string context)
        {
            var duplicateAssetEntries = Detect(settings);
            if (duplicateAssetEntries.Count == 0) return false;

            foreach (var duplicate in duplicateAssetEntries)
                Debug.LogError($"[AddressTeller] {duplicate.Message}");
            Debug.LogError($"[AddressTeller] {context} aborted: the same asset has an entry in two or more Addressables groups at once. See the Console for details.");

            return true;
        }
    }
}

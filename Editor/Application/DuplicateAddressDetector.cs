using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 別々のアセットが同じアドレス文字列になっていないかを検出する。
    /// <see cref="AddressTellerApplier"/> の衝突判定（1アセットに複数ルールがアドレスを返した場合）とは別物で、
    /// こちらは「予測される最終状態でアドレスが重複しているアセットの組」を対象にする
    /// （<see cref="ValidationStatus.DuplicateAddress"/> の XML doc 参照）。
    /// ValidateAll のフルスキャン経路と BuildPredictedSnapshot の両方から共有される、書き込みを行わない検出ロジック。
    /// </summary>
    internal static class DuplicateAddressDetector
    {
        /// <summary>
        /// <paramref name="afterAddresses"/>（guid → このランの Apply 後に想定されるアドレス）をアドレスで
        /// グルーピングし、2件以上のアセットが同じアドレスを持つ組ごとに1件の
        /// <see cref="ValidationStatus.DuplicateAddress"/> を返す。空アドレス（null/空文字）は対象外。
        /// <paramref name="writtenGuids"/> は、このランで AddressTeller が実際にアドレスを書き込む
        /// （書き込む予定の）guid の集合。重複組の中に1件でも含まれていれば
        /// <see cref="ValidationResult.HasWritableDuplicate"/> が true になり Error 扱い、
        /// 1件も含まれていなければ Warning 扱いになる。
        /// 戻り値はアドレスの Ordinal 昇順で決定的に並べる。
        /// </summary>
        public static IReadOnlyList<ValidationResult> Detect(
            IEnumerable<KeyValuePair<string, string>> afterAddresses,
            IReadOnlyCollection<string> writtenGuids)
        {
            var guidsByAddress = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var kvp in afterAddresses)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;

                if (!guidsByAddress.TryGetValue(kvp.Value, out var guids))
                {
                    guids = new List<string>();
                    guidsByAddress[kvp.Value] = guids;
                }

                guids.Add(kvp.Key);
            }

            var results = new List<ValidationResult>();

            foreach (var address in guidsByAddress.Keys.OrderBy(a => a, StringComparer.Ordinal))
            {
                var guids = guidsByAddress[address];
                if (guids.Count < 2) continue;

                var hasWritableDuplicate = guids.Any(writtenGuids.Contains);
                results.Add(BuildResult(address, guids, hasWritableDuplicate));
            }

            return results;
        }

        /// <summary>1件の重複アドレスにつき、関与する全アセットのパスを含む ValidationResult を組み立てる。</summary>
        private static ValidationResult BuildResult(string address, IReadOnlyList<string> guids, bool hasWritableDuplicate)
        {
            // パスは重複が見つかった組だけを対象に解決する（毎アセットループ内ではなく後段のここだけで呼ぶ）。
            var assets = guids
                .Select(guid => (Guid: guid, Path: AssetDatabase.GUIDToAssetPath(guid)))
                .OrderBy(a => a.Path, StringComparer.Ordinal)
                .ThenBy(a => a.Guid, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.Append($"Address \"{address}\" is used by {assets.Count} assets:");
            foreach (var asset in assets)
            {
                var displayPath = string.IsNullOrEmpty(asset.Path) ? "(path could not be resolved)" : asset.Path;
                sb.Append($"\n  {displayPath} (guid={asset.Guid})");
            }

            return new ValidationResult(null, ValidationStatus.DuplicateAddress, sb.ToString(), hasWritableDuplicate: hasWritableDuplicate);
        }
    }
}

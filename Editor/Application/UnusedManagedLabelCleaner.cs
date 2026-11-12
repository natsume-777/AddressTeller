using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Addressables のラベル定義（<c>AddressableAssetSettings.RemoveLabel</c> の対象）のうち、AddressTeller が書いた
    /// ことがあり（ラベル台帳にある）、いまどのエントリにも使われていないものを見つけて削除する。
    /// ラベルの除去（<see cref="AddressTellerSettings.RemoveStaleManagedLabels"/>）はエントリからラベルを外すだけで
    /// 定義は残すため、定義の掃除は別操作として切り出している。台帳に無い定義（利用者が自分で作ったもの）は、
    /// 誰も使っていなくても対象にしない。
    /// </summary>
    internal static class UnusedManagedLabelCleaner
    {
        /// <summary>
        /// 台帳にあり、Addressables に定義があり、どのエントリにも付いていないラベルを Ordinal 順で返す
        /// （書き込みは行わない）。
        /// </summary>
        public static List<string> FindUnused(AddressableAssetSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var managed = AddressTellerLabelLedger.GetAll();
            if (managed.Length == 0) return new List<string>();

            var defined = new HashSet<string>(settings.GetLabels(), StringComparer.Ordinal);

            // 全エントリを1回走査して、使われているラベル名の集合を作る。
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                    foreach (var label in entry.labels)
                        used.Add(label);
            }

            // managed は Ordinal 順なので、その順序を保って絞り込む。
            return managed.Where(l => defined.Contains(l) && !used.Contains(l)).ToList();
        }

        /// <summary>
        /// <paramref name="labels"/> の定義を Addressables から削除し、台帳からも外す（定義が無くなったラベルは
        /// もう AddressTeller が管理しているものではないため）。削除ごとに Warning を出す。
        /// </summary>
        public static void Remove(AddressableAssetSettings settings, IReadOnlyList<string> labels)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (labels == null) throw new ArgumentNullException(nameof(labels));

            foreach (var label in labels)
            {
                Debug.LogWarning($"[AddressTeller] Removing unused label definition '{label}' (written by AddressTeller earlier, no entry uses it).");
                settings.RemoveLabel(label);
            }

            AddressTellerLabelLedger.Forget(labels);
        }
    }
}

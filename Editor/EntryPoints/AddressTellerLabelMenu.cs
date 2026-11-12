using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>ラベル定義の掃除メニュー（Tools/AddressTeller/Remove Unused Managed Labels...）。</summary>
    internal static class AddressTellerLabelMenu
    {
        /// <summary>
        /// AddressTeller が書いたことがあり（ラベル台帳にある）、いまどのエントリにも使われていないラベルの定義を、
        /// 確認ダイアログを経て Addressables から削除する。台帳に無い定義には触れない。
        /// </summary>
        [MenuItem("Tools/AddressTeller/Remove Unused Managed Labels...")]
        public static void RemoveUnusedManagedLabels()
        {
            if (!AddressTellerSettings.EnsureLoaded()) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressTeller] AddressableAssetSettings not found. Please initialize Addressables.");
                return;
            }

            Run(settings, ConfirmWithDialog);
        }

        /// <summary>
        /// メニューのコア処理。確認を <paramref name="confirm"/> に委ねる（EditMode テストが実モーダルを開かずに
        /// 検証できるようにするための注入点）。<paramref name="confirm"/> が false を返したら何も削除しない。
        /// </summary>
        /// <returns>削除したラベル名（Ordinal 順）。対象が無い、または確認で中止した場合は空。</returns>
        internal static IReadOnlyList<string> Run(AddressableAssetSettings settings, Func<IReadOnlyList<string>, bool> confirm)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (confirm == null) throw new ArgumentNullException(nameof(confirm));

            var unused = UnusedManagedLabelCleaner.FindUnused(settings);
            if (unused.Count == 0)
            {
                Debug.Log("[AddressTeller] No unused labels written by AddressTeller were found.");
                return Array.Empty<string>();
            }

            if (!confirm(unused)) return Array.Empty<string>();

            UnusedManagedLabelCleaner.Remove(settings, unused);
            Debug.Log($"[AddressTeller] Removed {unused.Count} unused label definition(s) written by AddressTeller.");
            return unused;
        }

        private static bool ConfirmWithDialog(IReadOnlyList<string> labels)
            => EditorUtility.DisplayDialog(
                "AddressTeller - Remove Unused Managed Labels",
                $"This removes {labels.Count} label definition(s) from Addressables. AddressTeller wrote them earlier and no entry uses them now:\n\n" +
                string.Join("\n", labels) +
                "\n\nLabel definitions you created yourself are not touched. Proceed?",
                "Remove",
                "Cancel");
    }
}

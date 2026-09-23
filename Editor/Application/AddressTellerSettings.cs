using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// AddressTeller settings persisted to ProjectSettings/AddressTellerSettings.json.
    /// Shared across the project and tracked in version control; these values can be toggled from the
    /// Project Settings UI.
    /// Assigning a property the value it already has is a no-op and does not write the file (each setter
    /// short-circuits on equality).
    /// </summary>
    public static class AddressTellerSettings
    {
        /// <summary>
        /// When true (default), ApplyAll removes entries for assets that no longer match any rule, from
        /// groups AddressTeller owns. See the "Deletions Are Determined by Per-Asset Ownership" section
        /// in design-decisions.md for what counts as owned and what this means for manually registered
        /// entries.
        /// Deletion is per-entry (<c>RemoveAssetEntry</c>), so both the address and any labels the entry
        /// held are lost. This does not affect labels on entries that remain matched: because labels
        /// accumulate from all rules by design, it is impossible to identify after the fact which rule
        /// assigned which label, so ApplyAll only ever adds labels to a surviving entry and never removes
        /// any label from a surviving entry.
        /// </summary>
        public static bool CleanupStaleEntries
        {
            get => AddressTellerSettingsAsset.Current._cleanupStaleEntries;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                if (data._cleanupStaleEntries == value) return;
                data._cleanupStaleEntries = value;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>
        /// When false, the automatic ApplyAll normally triggered on import by AssetPostprocessor is not
        /// performed. This does not affect manual execution from Tools/AddressTeller/Apply All.
        /// Default: true.
        /// </summary>
        public static bool PostprocessEnabled
        {
            get => AddressTellerSettingsAsset.Current._postprocessEnabled;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                if (data._postprocessEnabled == value) return;
                data._postprocessEnabled = value;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>
        /// Default value of <see cref="SnapshotFolder"/>. Also used as the fallback when the range check
        /// finds that a relative path resolves outside the project root.
        /// </summary>
        public const string DefaultSnapshotFolder = "AddressTellerSnapshots";

        /// <summary>
        /// Folder where snapshots are saved, relative to the project root (the parent directory of
        /// Assets). Defaults to "AddressTellerSnapshots" (outside Assets, not imported by Unity).
        /// Specifying "Assets/..." makes it visible in the Project window as well.
        /// </summary>
        public static string SnapshotFolder
        {
            get => AddressTellerSettingsAsset.Current._snapshotFolder;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                if (data._snapshotFolder == value) return;
                data._snapshotFolder = value;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>
        /// Resolves SnapshotFolder to an absolute path rooted at the project root.
        /// If SnapshotFolder is a relative path, verifies that it has not resolved outside the project
        /// root (the parent directory of Application.dataPath) via path traversal (e.g. a value like
        /// "../../shared"); if it is out of range, logs a warning and falls back to
        /// <see cref="DefaultSnapshotFolder"/> (to prevent snapshot saves and Rotate() deletions from
        /// happening outside the project). If SnapshotFolder is specified as an absolute path, it is
        /// treated as an intentional external location (e.g. a temp folder used in tests) and is used
        /// as-is, skipping the range check.
        /// </summary>
        public static string GetSnapshotFolderAbsolutePath()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var configured = SnapshotFolder;

            if (Path.IsPathRooted(configured))
                return Path.GetFullPath(configured);

            var resolved = Path.GetFullPath(Path.Combine(projectRoot, configured));
            if (IsWithinProjectRoot(resolved, projectRoot))
                return resolved;

            Debug.LogWarning($"[AddressTeller] SnapshotFolder '{configured}' resolves outside the project root ('{resolved}'). Falling back to the default value '{DefaultSnapshotFolder}'.");
            return Path.GetFullPath(Path.Combine(projectRoot, DefaultSnapshotFolder));
        }

        /// <summary>
        /// <paramref name="resolvedPath"/> が <paramref name="projectRoot"/> 自身、またはその配下かどうかを
        /// "/" 境界込みで判定する（<paramref name="projectRoot"/> の文字列プレフィックスに一致するだけの
        /// 別フォルダを誤って範囲内と判定しないため）。
        /// </summary>
        private static bool IsWithinProjectRoot(string resolvedPath, string projectRoot)
        {
            var normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return resolvedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || resolvedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// When true (default), an automatic snapshot of the current Addressables state is saved (under
        /// SnapshotFolder/Auto) immediately before running Tools/AddressTeller/Apply All or Apply with
        /// Validate. Old snapshots beyond the most recent <see cref="AutoSnapshotRetention"/> are removed
        /// automatically. This only applies to those two menu items — the automatic apply on import
        /// (Postprocessor) and the CLI (ApplyAllCLI/ApplyWithValidateCLI) are excluded, to avoid extra
        /// build time and disk I/O. If saving the snapshot itself fails, Apply is aborted rather than
        /// proceeding with a destructive operation with no Undo Last Apply backing (see the apply flow
        /// used by <c>Tools/AddressTeller/Apply All</c>).
        /// </summary>
        public static bool AutoSnapshotBeforeApplyAll
        {
            get => AddressTellerSettingsAsset.Current._autoSnapshotBeforeApplyAll;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                if (data._autoSnapshotBeforeApplyAll == value) return;
                data._autoSnapshotBeforeApplyAll = value;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>
        /// Number of automatic snapshots (under SnapshotFolder/Auto) to retain. Older files beyond this
        /// count are removed when a new one is saved. Minimum value is 1. Default: 10.
        /// </summary>
        public static int AutoSnapshotRetention
        {
            get => AddressTellerSettingsAsset.Current._autoSnapshotRetention;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                var clamped = AddressTellerSettingsAsset.NormalizeAutoSnapshotRetention(value, warnIfChanged: false);
                if (data._autoSnapshotRetention == clamped) return;
                data._autoSnapshotRetention = clamped;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>
        /// When true, if a rule references a group that does not exist in Addressables at Apply time, it
        /// is created automatically by copying DefaultGroup's schema configuration. When false (default),
        /// this is treated as <see cref="ValidationStatus.GroupNotFound"/> as before, and no write is
        /// performed.
        /// On Validate/Predict (dry-run), even when this is on, the group is not actually created; it is
        /// only reported as <see cref="ValidationStatus.GroupWillBeCreated"/>.
        /// </summary>
        public static bool AutoCreateMissingGroups
        {
            get => AddressTellerSettingsAsset.Current._autoCreateMissingGroups;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                if (data._autoCreateMissingGroups == value) return;
                data._autoCreateMissingGroups = value;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>
        /// Full names (<see cref="System.Type.FullName"/>) of disabled rule classes.
        /// Rules listed here are excluded from evaluation by Apply/Validate/snapshot prediction/Explain.
        /// Returns a defensive copy rather than the internal list itself, so changes made by the caller
        /// are not reflected in the setting.
        /// </summary>
        public static IReadOnlyList<string> DisabledRuleClassNames
            => AddressTellerSettingsAsset.Current._disabledRuleClassNames.ToArray();

        /// <summary>
        /// Returns whether the given rule class is enabled. Defaults to true (enabled) if it is not
        /// listed in <see cref="DisabledRuleClassNames"/>.
        /// </summary>
        public static bool IsRuleEnabled(string ruleClassFullName)
            => !AddressTellerSettingsAsset.Current._disabledRuleClassNames.Contains(ruleClassFullName);

        /// <summary>
        /// Default value of <see cref="PostprocessOrder"/>. Set to a comparatively large value so that
        /// other packages' Postprocessors are expected to run first in AssetPostprocessor order.
        /// </summary>
        public const int DefaultPostprocessOrder = 1000;

        /// <summary>
        /// Value returned by <see cref="AddressTellerPostprocessor.GetPostprocessOrder"/>.
        /// Controls AssetPostprocessor execution order; smaller values run earlier.
        /// </summary>
        public static int PostprocessOrder
        {
            get => AddressTellerSettingsAsset.Current._postprocessOrder;
            set
            {
                var data = AddressTellerSettingsAsset.Current;
                if (data._postprocessOrder == value) return;
                data._postprocessOrder = value;
                AddressTellerSettingsAsset.SaveChanges();
            }
        }

        /// <summary>Enables or disables the given rule class.</summary>
        public static void SetRuleEnabled(string ruleClassFullName, bool enabled)
        {
            var data = AddressTellerSettingsAsset.Current;
            var list = data._disabledRuleClassNames;

            if (enabled)
            {
                if (!list.Remove(ruleClassFullName)) return;
            }
            else
            {
                if (list.Contains(ruleClassFullName)) return;
                list.Add(ruleClassFullName);
            }

            AddressTellerSettingsAsset.SaveChanges();
        }

        /// <summary>
        /// バッチ（Apply All / Validate / Preview / Explain / 各 CLI コマンド / Postprocessor の1回の
        /// OnPostprocessAllAssets / Project Settings ページの activate）の入口で呼ぶ共通ゲート。
        /// 設定ファイルが前回の読み込みから変化していれば読み直し、ファイルはあるが読めない・
        /// AddressTeller の設定ファイルとして認識できない場合は Error を1本ログして false を返す
        /// （呼び出し元はこれを見て Apply/Validate/Preview/Explain/CLI の実行を中止すること）。
        /// ファイルがまだ存在しない場合は無言で true を返す（初回起動として正常）。
        /// </summary>
        internal static bool EnsureLoaded()
        {
            if (AddressTellerSettingsAsset.EnsureLoaded(out var error))
                return true;

            Debug.LogError($"[AddressTeller] {error}");
            return false;
        }
    }
}

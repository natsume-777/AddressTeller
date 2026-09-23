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
    /// Assigning a property the value it already has is a no-op and does not write the file. Every setter
    /// (and <see cref="SetRuleEnabled"/>) writes through <see cref="AddressTellerSettingsAsset.Mutate"/>,
    /// which reloads the file first if it changed on disk (e.g. after a git pull), applies the change to a
    /// copy, and only replaces the in-memory value once the write to disk has actually succeeded. If the
    /// settings file cannot currently be loaded, every setter throws <see cref="InvalidOperationException"/>
    /// instead of writing on top of it.
    /// </summary>
    public static class AddressTellerSettings
    {
        /// <summary>
        /// When true, ApplyAll removes entries for assets that no longer match any rule, from
        /// groups AddressTeller owns. See the "Deletions Are Determined by Per-Asset Ownership" section
        /// in design-decisions.md for what counts as owned and what this means for manually registered
        /// entries.
        /// Deletion is per-entry (<c>RemoveAssetEntry</c>), so both the address and any labels the entry
        /// held are lost. This does not affect labels on entries that remain matched: because labels
        /// accumulate from all rules by design, it is impossible to identify after the fact which rule
        /// assigned which label, so ApplyAll only ever adds labels to a surviving entry and never removes
        /// any label from a surviving entry.
        /// Default: false. When off, an entry that would have been removed is instead reported once per
        /// asset as <see cref="ValidationStatus.UnmatchedEntryKept"/> (a non-blocking notice) so the change
        /// is visible before you opt in.
        /// </summary>
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static bool CleanupStaleEntries
        {
            get => AddressTellerSettingsAsset.Current._cleanupStaleEntries;
            set => AddressTellerSettingsAsset.Mutate(data => data._cleanupStaleEntries = value);
        }

        /// <summary>
        /// When false (default), the automatic ApplyAll normally triggered on import by AssetPostprocessor
        /// is not performed. This does not affect manual execution from Tools/AddressTeller/Apply All.
        /// </summary>
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static bool PostprocessEnabled
        {
            get => AddressTellerSettingsAsset.Current._postprocessEnabled;
            set => AddressTellerSettingsAsset.Mutate(data => data._postprocessEnabled = value);
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
        /// Setting this to an empty or whitespace-only string normalizes it to
        /// <see cref="DefaultSnapshotFolder"/> before saving, rather than saving the empty value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static string SnapshotFolder
        {
            get => AddressTellerSettingsAsset.Current._snapshotFolder;
            set
            {
                var normalized = AddressTellerSettingsAsset.NormalizeSnapshotFolder(value, warnIfChanged: false);
                AddressTellerSettingsAsset.Mutate(data => data._snapshotFolder = normalized);
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
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static bool AutoSnapshotBeforeApplyAll
        {
            get => AddressTellerSettingsAsset.Current._autoSnapshotBeforeApplyAll;
            set => AddressTellerSettingsAsset.Mutate(data => data._autoSnapshotBeforeApplyAll = value);
        }

        /// <summary>
        /// Number of automatic snapshots (under SnapshotFolder/Auto) to retain. Older files beyond this
        /// count are removed when a new one is saved. Minimum value is 1. Default: 10.
        /// </summary>
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static int AutoSnapshotRetention
        {
            get => AddressTellerSettingsAsset.Current._autoSnapshotRetention;
            set
            {
                var clamped = AddressTellerSettingsAsset.NormalizeAutoSnapshotRetention(value, warnIfChanged: false);
                AddressTellerSettingsAsset.Mutate(data => data._autoSnapshotRetention = clamped);
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
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static bool AutoCreateMissingGroups
        {
            get => AddressTellerSettingsAsset.Current._autoCreateMissingGroups;
            set => AddressTellerSettingsAsset.Mutate(data => data._autoCreateMissingGroups = value);
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
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static int PostprocessOrder
        {
            get => AddressTellerSettingsAsset.Current._postprocessOrder;
            set => AddressTellerSettingsAsset.Mutate(data => data._postprocessOrder = value);
        }

        /// <summary>Enables or disables the given rule class.</summary>
        /// <exception cref="InvalidOperationException">The settings file could not be loaded (it exists but cannot be read, or is not recognized as an AddressTeller settings file).</exception>
        public static void SetRuleEnabled(string ruleClassFullName, bool enabled)
        {
            AddressTellerSettingsAsset.Mutate(data =>
            {
                var list = data._disabledRuleClassNames;
                if (enabled)
                    list.Remove(ruleClassFullName);
                else if (!list.Contains(ruleClassFullName))
                    list.Add(ruleClassFullName);
            });
        }

        /// <summary>
        /// バッチ（Apply All / Validate / Preview / Explain / 各 CLI コマンド / Postprocessor の1回の
        /// OnPostprocessAllAssets / Project Settings ページの activate）の入口で呼ぶ共通ゲート。
        /// 読み込み段（<see cref="AddressTellerSettingsAsset.EnsureLoaded"/>）を先に行い、失敗した場合のみ
        /// <paramref name="logPolicy"/> に従ってログ段を行う。ファイルが壊れたままの間、この読み込み段自体は
        /// 呼ぶたびに毎回実際にファイルを読み直してパースを試みる（<see cref="AddressTellerSettingsAsset.EnsureLoaded"/>
        /// の remarks 参照）——OncePerDistinctFailure が抑制するのはあくまで Console への出力だけで、
        /// I/O 自体は抑制しない。
        /// 戻り値（<see cref="SettingsGateResult"/>）は <c>bool</c> へ暗黙変換されるため、
        /// <c>if (!EnsureLoaded()) return;</c> と書ける。ゲート失敗の詳細（<see cref="SettingsGateResult.Error"/>）
        /// やファイルの有無（<see cref="SettingsGateResult.FileExists"/>）が必要な呼び出し元は戻り値を
        /// そのまま参照すること。
        /// このメソッドは失敗を Console へログする副作用を持つため、プログラムから消費される公開 API
        /// （<c>AddressTellerService</c> 等のサービス層の入口）は、ログを出さない
        /// <see cref="AddressTellerSettingsAsset.EnsureLoaded"/> を直接使い、失敗を戻り値（例:
        /// <c>ValidationStatus.SettingsUnavailable</c>）として返してログの要否は呼び出し元に委ねること。
        /// </summary>
        internal static SettingsGateResult EnsureLoaded(SettingsGateLogPolicy logPolicy = SettingsGateLogPolicy.Always)
        {
            var result = AddressTellerSettingsAsset.EnsureLoaded();
            if (result.Success)
            {
                // 直前まで抑制のために覚えていた失敗はもう最新ではない。次に同じ失敗が起きたら
                // 改めて1回はログできるよう、成功のたびに重複排除キーを捨てる。
                s_lastLoggedFailure = null;
                return result;
            }

            switch (logPolicy)
            {
                case SettingsGateLogPolicy.Always:
                    Debug.LogError($"[AddressTeller] {result.Error}");
                    break;

                case SettingsGateLogPolicy.OncePerDistinctFailure:
                    // 同じ失敗の間は1回だけログする。判定は (ファイルの更新日時, サイズ, エラー文) の組。
                    // 読み込み段が既に stat 済みの値（result.FileLastWriteUtc/FileLength）をそのまま使い、
                    // ここで再度ファイルを stat しない（失敗時は FileExists=true が保証されているため、
                    // これらの値は常に有効）。
                    var key = (result.FileLastWriteUtc, result.FileLength, result.Error);
                    if (s_lastLoggedFailure != key)
                    {
                        Debug.LogError($"[AddressTeller] {result.Error}");
                        s_lastLoggedFailure = key;
                    }
                    break;
            }

            return result;
        }

        /// <summary>
        /// <see cref="SettingsGateLogPolicy.OncePerDistinctFailure"/> で直近にログした失敗の識別子。
        /// null は「まだこのポリシーで失敗をログしていない、または直近の呼び出しが成功していた」ことを表す。
        /// </summary>
        private static (DateTime lastWriteUtc, long length, string error)? s_lastLoggedFailure;

        /// <summary>
        /// テスト専用: <see cref="s_lastLoggedFailure"/> をリセットする。OncePerDistinctFailure の
        /// 重複排除状態はテスト間で共有される static であり、後続のテストが前のテストの失敗を
        /// 引き継いで「2回目だから出ない」と誤判定しないよう、各テストの SetUp/TearDown で呼ぶこと。
        /// </summary>
        internal static void ResetOncePerDistinctFailureLogForTests() => s_lastLoggedFailure = null;
    }

    /// <summary>
    /// <see cref="AddressTellerSettings.EnsureLoaded"/> のログ段の方針。
    /// </summary>
    internal enum SettingsGateLogPolicy
    {
        /// <summary>失敗のたびに毎回ログする。メニュー・CLI など、利用者が明示的に起こした操作向け。</summary>
        Always,

        /// <summary>
        /// 同じ失敗（ファイルの更新日時・サイズ・エラー文が同じ）が続く間は最初の1回だけログする。
        /// Postprocessor（1 import ごとに毎回呼ばれる）向け。ファイルが直っていない限りログを連発しない。
        /// </summary>
        OncePerDistinctFailure,
    }
}

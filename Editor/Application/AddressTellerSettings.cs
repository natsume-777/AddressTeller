using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// AddressTeller settings persisted to ProjectSettings/AddressTellerSettings.asset.
    /// Shared across the project and tracked in version control; these values can be toggled from the
    /// Project Settings UI.
    /// Assigning a property the value it already has is a no-op and does not write the file (each setter
    /// short-circuits on equality). If the file and the in-memory values have drifted apart, use
    /// <see cref="SaveToDisk"/> to force a write, or <see cref="ReloadFromDisk"/> to load the file's
    /// values back into memory.
    /// </summary>
    public static class AddressTellerSettings
    {
        /// <summary>
        /// When true (default), ApplyAll removes entries for assets that no longer match any rule, from
        /// groups managed by AddressTeller (any group referenced as a rule's GroupName).
        /// Deletion is per-entry (<c>RemoveAssetEntry</c>), so both the address and any labels the entry
        /// held are lost. This does not affect labels on entries that remain matched: because labels
        /// accumulate from all rules by design, it is impossible to identify after the fact which rule
        /// assigned which label, so ApplyAll only ever adds labels to a surviving entry and never removes
        /// any label from a surviving entry.
        /// </summary>
        public static bool CleanupStaleEntries
        {
            get => AddressTellerSettingsAsset.instance._cleanupStaleEntries;
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                if (asset._cleanupStaleEntries == value) return;
                asset._cleanupStaleEntries = value;
                asset.SaveChanges();
            }
        }

        /// <summary>
        /// When false, the automatic ApplyAll normally triggered on import by AssetPostprocessor is not
        /// performed. This does not affect manual execution from Tools/AddressTeller/Apply All.
        /// Default: true.
        /// </summary>
        public static bool PostprocessEnabled
        {
            get => AddressTellerSettingsAsset.instance._postprocessEnabled;
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                if (asset._postprocessEnabled == value) return;
                asset._postprocessEnabled = value;
                asset.SaveChanges();
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
            get => AddressTellerSettingsAsset.instance._snapshotFolder;
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                if (asset._snapshotFolder == value) return;
                asset._snapshotFolder = value;
                asset.SaveChanges();
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
            get => AddressTellerSettingsAsset.instance._autoSnapshotBeforeApplyAll;
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                if (asset._autoSnapshotBeforeApplyAll == value) return;
                asset._autoSnapshotBeforeApplyAll = value;
                asset.SaveChanges();
            }
        }

        /// <summary>
        /// Number of automatic snapshots (under SnapshotFolder/Auto) to retain. Older files beyond this
        /// count are removed when a new one is saved. Minimum value is 1. Default: 10.
        /// </summary>
        public static int AutoSnapshotRetention
        {
            get => AddressTellerSettingsAsset.instance._autoSnapshotRetention;
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                var clamped = Mathf.Max(1, value);
                if (asset._autoSnapshotRetention == clamped) return;
                asset._autoSnapshotRetention = clamped;
                asset.SaveChanges();
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
            get => AddressTellerSettingsAsset.instance._autoCreateMissingGroups;
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                if (asset._autoCreateMissingGroups == value) return;
                asset._autoCreateMissingGroups = value;
                asset.SaveChanges();
            }
        }

        /// <summary>
        /// Full names (<see cref="System.Type.FullName"/>) of disabled rule classes.
        /// Rules listed here are excluded from evaluation by Apply/Validate/snapshot prediction/Explain.
        /// Returns a defensive copy rather than the internal list itself, so changes made by the caller
        /// are not reflected in the setting.
        /// </summary>
        public static IReadOnlyList<string> DisabledRuleClassNames
            => AddressTellerSettingsAsset.instance._disabledRuleClassNames.ToArray();

        /// <summary>
        /// Returns whether the given rule class is enabled. Defaults to true (enabled) if it is not
        /// listed in <see cref="DisabledRuleClassNames"/>.
        /// </summary>
        public static bool IsRuleEnabled(string ruleClassFullName)
            => !AddressTellerSettingsAsset.instance._disabledRuleClassNames.Contains(ruleClassFullName);

        /// <summary>
        /// Default value of <see cref="PostprocessOrder"/>. Set to a comparatively large value so that
        /// other packages' Postprocessors are expected to run first in AssetPostprocessor order.
        /// </summary>
        public const int DefaultPostprocessOrder = 1000;

        /// <summary>
        /// Value returned by <see cref="AddressTellerPostprocessor.GetPostprocessOrder"/>.
        /// Controls AssetPostprocessor execution order; smaller values run earlier.
        /// If the field is unset (0) on an existing asset, falls back to <see cref="DefaultPostprocessOrder"/>.
        /// Explicitly setting 0 is likewise read back as DefaultPostprocessOrder.
        /// </summary>
        public static int PostprocessOrder
        {
            get
            {
                var value = AddressTellerSettingsAsset.instance._postprocessOrder;
                return value == 0 ? DefaultPostprocessOrder : value;
            }
            set
            {
                var asset = AddressTellerSettingsAsset.instance;
                if (asset._postprocessOrder == value) return;
                asset._postprocessOrder = value;
                asset.SaveChanges();
            }
        }

        /// <summary>
        /// Writes the current in-memory settings to ProjectSettings/AddressTellerSettings.asset, even when
        /// nothing has changed, and verifies the write.
        /// </summary>
        /// <remarks>
        /// Property setters already persist on change, so this is only needed when the file and the
        /// in-memory values have drifted apart — for example after the file failed to load, or after it
        /// was edited outside the Editor.
        /// The in-memory values win: any change made to the file while the Editor was running is
        /// overwritten. Call <see cref="ReloadFromDisk"/> first if the file is the side you want to keep.
        /// Returns true if, after writing, the file's contents match a fresh re-serialization of the same
        /// in-memory settings (this compares serialized text, not deserialized values — it confirms the
        /// write was not lost, truncated, or partially applied; it does not confirm Unity itself can still
        /// parse the file back). Returns false, and logs an error, if writing the file, re-serializing the
        /// settings for the comparison, or reading either file fails (for example, no read/write
        /// permission), or if that comparison does not match.
        /// </remarks>
        public static bool SaveToDisk()
        {
            var asset = AddressTellerSettingsAsset.instance;

            string diskText;
            string reserializedText;
            try
            {
                asset.SaveChanges();
                diskText = File.ReadAllText(AddressTellerSettingsAsset.GetAbsoluteFilePath());
                reserializedText = AddressTellerSettingsAsset.SaveCurrentInstanceToTempFileAndReadText();
            }
            catch (Exception ex)
            {
                Debug.LogError("[AddressTeller] SaveToDisk: writing or verifying " +
                    $"ProjectSettings/AddressTellerSettings.asset failed ({ex.GetType().Name}: {ex.Message}).");
                return false;
            }

            if (reserializedText == null)
            {
                Debug.LogError("[AddressTeller] SaveToDisk: could not re-serialize the current settings to " +
                    "a temporary file for verification. This does not necessarily mean the write to " +
                    "ProjectSettings/AddressTellerSettings.asset itself failed.");
                return false;
            }

            if (reserializedText == diskText) return true;

            Debug.LogError("[AddressTeller] SaveToDisk: the file content read back after writing does not " +
                "match a fresh re-serialization of the in-memory settings. " +
                "ProjectSettings/AddressTellerSettings.asset may not reflect the current settings.");
            return false;
        }

        /// <summary>
        /// Reloads ProjectSettings/AddressTellerSettings.asset from disk into memory.
        /// </summary>
        /// <remarks>
        /// Discards the current in-memory settings object and forces Unity to recreate it, which reads the
        /// file fresh. Any unsaved in-memory changes are lost. Does not write to disk.
        /// This changes the identity of the internal settings object; code that has cached a reference to
        /// it directly (rather than looking it up again after calling this method) would hold a stale,
        /// destroyed reference — the public API here always looks the object up on each call, so this only
        /// matters for code that reaches into internal implementation details.
        /// Because this discards and recreates a Unity object, call it from the main thread, outside of an
        /// asset import callback or a serialization callback (e.g. <c>ISerializationCallbackReceiver</c>)
        /// — destroying an object from those contexts is not supported by Unity.
        /// If the file exists but is corrupted or otherwise unreadable, the result is the same as what
        /// happens when the Editor itself starts up and reads that same file — which may mean the settings
        /// reset to their default values, and Unity's own deserializer may log a parse error to the
        /// Console while doing so (that log comes from Unity, not from this method — this method never
        /// logs anything on its own, in either the success or failure case). This method cannot
        /// distinguish a reset-to-defaults outcome from a normal successful reload, so it still returns
        /// true in that case.
        /// Returns false, without changing memory, only when the file does not exist or is not accessible
        /// (e.g. on first run in a project that has never saved this asset, or if the process lacks read
        /// permission).
        /// </remarks>
        public static bool ReloadFromDisk()
        {
            var path = AddressTellerSettingsAsset.GetAbsoluteFilePath();

            // File.Exists は「存在するが読めない」を確実に判別できるとは限らないため、実際に開けるかどうか
            // で判定する。ファイルが存在しない場合もこの catch に落ちるが、この段階ではログは出さない
            // （ログが出うるのは、この後 Unity 自身が壊れたファイルを読む場合のみ。XML doc 参照）。
            // また、開けないファイルをそのまま下の破棄→再取得の経路（Unity 自身の再読み込み処理）へ渡すと、
            // Editor のメインスレッドが長時間ブロックされる事象を実測で観測した（原因は未特定）。
            // ここで事前に弾くことで、その経路へ入ること自体を避けている。
            try
            {
                using (File.OpenRead(path)) { }
            }
            catch
            {
                return false;
            }

            // メモリ上の唯一のインスタンスを破棄してから instance に再アクセスすることで、
            // ScriptableSingleton にディスクから読み直させる（Unity の内部読み込み経路に委ねる）。
            // 同じファイルを2個目のオブジェクトとして読む方式は ScriptableSingleton のコンストラクタが
            // 既存インスタンスの存在を検知して Debug.LogError を出す実装と衝突するため採用しない。
            UnityEngine.Object.DestroyImmediate(AddressTellerSettingsAsset.instance);
            _ = AddressTellerSettingsAsset.instance;

            return true;
        }

        /// <summary>Enables or disables the given rule class.</summary>
        public static void SetRuleEnabled(string ruleClassFullName, bool enabled)
        {
            var asset = AddressTellerSettingsAsset.instance;
            var list = asset._disabledRuleClassNames;

            if (enabled)
            {
                if (!list.Remove(ruleClassFullName)) return;
            }
            else
            {
                if (list.Contains(ruleClassFullName)) return;
                list.Add(ruleClassFullName);
            }

            asset.SaveChanges();
        }
    }
}

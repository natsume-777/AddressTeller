using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// ProjectSettings/AddressTellerSettings.asset に永続化される AddressTeller の設定値。
    /// プロジェクト共有・バージョン管理対象であり、Project Settings UI からこれらの値を切り替えられる。
    /// </summary>
    public static class AddressTellerSettings
    {
        /// <summary>
        /// true の場合、ApplyAll 実行時にどのルールにもマッチしなくなったアセットの
        /// エントリを、AddressTeller が管理するグループ（いずれかのルールの GroupName）から削除する。
        /// 削除されるのは Addressables のエントリ（アドレス）のみで、過去にルールが付与した
        /// ラベルはプロジェクト全体で共有されるため剥がされない。
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
        /// false の場合、AssetPostprocessor によるインポート時の自動 ApplyAll を行わない。
        /// Tools/AddressTeller/Apply All からの手動実行には影響しない。
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
        /// <see cref="SnapshotFolder"/> の既定値。範囲チェックで相対パスがプロジェクトルート外に
        /// 解決された場合のフォールバック先としても使う。
        /// </summary>
        public const string DefaultSnapshotFolder = "AddressTellerSnapshots";

        /// <summary>
        /// スナップショットの保存先フォルダ。プロジェクトルート（Assets の親ディレクトリ）からの相対パス。
        /// 既定値は "AddressTellerSnapshots"（Assets 外、Unity にインポートされない）。
        /// "Assets/..." を指定すると Project ウィンドウにも表示される。
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
        /// SnapshotFolder をプロジェクトルートからの絶対パスに解決する。
        /// SnapshotFolder が相対パスの場合、"../../shared" のような値によるパストラバーサルで
        /// プロジェクトルート（Application.dataPath の親ディレクトリ）の外に解決されていないかを検証し、
        /// 範囲外であれば警告ログを出して <see cref="DefaultSnapshotFolder"/> にフォールバックする
        /// （スナップショットの保存・Rotate() による削除がプロジェクト外で行われるのを防ぐ）。
        /// SnapshotFolder が絶対パスとして指定された場合は、テスト用の一時フォルダ指定など意図的な
        /// 外部指定とみなし、範囲チェックの対象外としてそのまま使用する。
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
        /// true の場合、Tools/AddressTeller/Apply All と Apply with Validate の実行直前に、
        /// 現在の Addressables の状態を自動スナップショットとして保存する（SnapshotFolder/Auto 以下）。
        /// 直近 <see cref="AutoSnapshotRetention"/> 件を超える古いものは自動的に削除される。
        /// 対象は上記2つのメニューのみ。import 時の自動適用（Postprocessor）と CLI
        /// （ApplyAllCLI/ApplyWithValidateCLI）はビルド時間とディスク I/O を避けるため対象外。
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
        /// 自動スナップショット（SnapshotFolder/Auto 以下）の保持件数。これを超える古いファイルは
        /// 新規保存時に削除される。最小値は 1。
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
        /// true の場合、Apply 実行時にルールが参照するグループが Addressables に存在しなければ、
        /// <see cref="AddressTellerGroupFactory.EnsureGroup"/> により DefaultGroup のスキーマ構成を
        /// 複製して自動的に作成する。false（既定）の場合は従来通り <see cref="ValidationStatus.GroupNotFound"/>
        /// として扱われ、書き込みは行われない。
        /// Validate/Predict（dry-run）では ON でも実際にグループを作成せず、
        /// <see cref="ValidationStatus.GroupWillBeCreated"/> として作成予定を提示するのみ。
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
        /// 無効化されているルールクラスの完全名（<see cref="System.Type.FullName"/>）一覧。
        /// ここに含まれるルールは Apply/Validate/スナップショット予測/Explain で評価対象から除外される。
        /// </summary>
        public static IReadOnlyList<string> DisabledRuleClassNames
            => AddressTellerSettingsAsset.instance._disabledRuleClassNames;

        /// <summary>
        /// 指定したルールクラスが有効かどうかを返す。<see cref="DisabledRuleClassNames"/> に
        /// 含まれていない場合は既定で true（有効）。
        /// </summary>
        public static bool IsRuleEnabled(string ruleClassFullName)
            => !AddressTellerSettingsAsset.instance._disabledRuleClassNames.Contains(ruleClassFullName);

        /// <summary>
        /// <see cref="PostprocessOrder"/> の既定値。AssetPostprocessor の実行順序としては
        /// 後段寄りの大きな値とし、他パッケージの Postprocessor が先に実行されることを期待する。
        /// </summary>
        public const int DefaultPostprocessOrder = 1000;

        /// <summary>
        /// <see cref="AddressTellerPostprocessor.GetPostprocessOrder"/> が返す値。
        /// AssetPostprocessor の実行順序を制御し、値が小さいほど早く実行される。
        /// 既存アセットでフィールドが未設定（0）の場合は <see cref="DefaultPostprocessOrder"/> にフォールバックする。
        /// 0 を明示的に設定した場合も同様に DefaultPostprocessOrder として読まれる。
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

        /// <summary>指定したルールクラスの有効・無効を切り替える。</summary>
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

    /// <summary>
    /// AddressTellerSettings の実体。ProjectSettings/AddressTellerSettings.asset に
    /// シリアライズされ、プロジェクトを共有する開発者間でバージョン管理される。
    /// </summary>
    [FilePath("ProjectSettings/AddressTellerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AddressTellerSettingsAsset : ScriptableSingleton<AddressTellerSettingsAsset>
    {
        [SerializeField] internal bool _cleanupStaleEntries = true;
        [SerializeField] internal bool _postprocessEnabled = true;
        [SerializeField] internal string _snapshotFolder = AddressTellerSettings.DefaultSnapshotFolder;
        [SerializeField] internal bool _autoSnapshotBeforeApplyAll = true;
        [SerializeField] internal int _autoSnapshotRetention = 10;
        [SerializeField] internal bool _autoCreateMissingGroups = false;
        [SerializeField] internal int _postprocessOrder = AddressTellerSettings.DefaultPostprocessOrder;
        [SerializeField] internal List<string> _disabledRuleClassNames = new();

        /// <summary>変更内容を ProjectSettings/AddressTellerSettings.asset へ書き出す。</summary>
        internal void SaveChanges() => Save(true);
    }
}

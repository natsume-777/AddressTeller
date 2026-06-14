using System;
using System.Collections.Generic;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// Check（dry-run）/ Apply の結果を CI 向けに構造化したレポート。
    /// JsonUtility でシリアライズするため public フィールドで構成する。
    /// </summary>
    [Serializable]
    public sealed class AddressTellerReport
    {
        public AddressTellerReportSummary Summary = new();
        public List<AddressTellerReportDrift> Drift = new();
        public List<AddressTellerReportIssue> Issues = new();

        /// <summary>
        /// 論理バンドル分布サマリ。dry-run の <see cref="DryRunResult.After"/> が無い場合（テスト構築等）は null。
        /// </summary>
        public BundleDistributionReport BundleDistribution;

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static AddressTellerReport FromJson(string json) => JsonUtility.FromJson<AddressTellerReport>(json);
    }

    /// <summary>レポート全体の集計値。</summary>
    [Serializable]
    public sealed class AddressTellerReportSummary
    {
        public int Added;
        public int Removed;
        public int Changed;
        public int Issues;
        public int ExitCode;
    }

    /// <summary>1アセット分のドリフト（Apply 適用前に計算した予測差分）。</summary>
    [Serializable]
    public sealed class AddressTellerReportDrift
    {
        public string Guid;
        public string Path;

        /// <summary>"Added" / "Removed" / "Changed"。JsonUtility が enum を扱えないため文字列で持つ。</summary>
        public string ChangeType;

        public AddressTellerReportEntry Before = new();
        public AddressTellerReportEntry After = new();
    }

    /// <summary>ドリフトの Before/After 1件分の Address/Group/Labels。</summary>
    [Serializable]
    public sealed class AddressTellerReportEntry
    {
        public string Address;
        public string GroupName;
        public List<string> Labels = new();
    }

    /// <summary>Validation で検出された問題1件。</summary>
    [Serializable]
    public sealed class AddressTellerReportIssue
    {
        public string Path;

        /// <summary><see cref="ValidationStatus"/> の名前（例: "ConflictingAddress"）。</summary>
        public string Status;

        public string Message;
    }

    /// <summary>
    /// 論理バンドル分布サマリ。Predict 結果と各グループの BundleMode から算出した論理推定値であり、
    /// 実 Addressables ビルドのバンドル数を保証しない（<see cref="Disclaimer"/> 参照）。
    /// </summary>
    [Serializable]
    public sealed class BundleDistributionReport
    {
        public LogicalBundleDto[] Bundles = Array.Empty<LogicalBundleDto>();

        /// <summary>Unknown を除いた論理バンドル数の合計。</summary>
        public int TotalLogicalBundleCount;

        /// <summary>BundleMode が判定できない（Unknown）グループの数。</summary>
        public int UnknownGroupCount;

        /// <summary>この分布が論理推定であることの注記。固定文言。</summary>
        public string Disclaimer = "";
    }

    /// <summary>論理バンドル1件分の DTO（<see cref="LogicalBundle"/> のシリアライズ用）。</summary>
    [Serializable]
    public sealed class LogicalBundleDto
    {
        public string GroupName;

        /// <summary><see cref="BundleModeKind"/> の名前。</summary>
        public string Mode;

        public string SplitKey;
        public int AssetCount;
    }
}

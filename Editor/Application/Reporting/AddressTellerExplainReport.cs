using System;
using System.Collections.Generic;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Explain（<see cref="RuleExplainService.Explain"/>）の結果を CI/外部ツール向けに構造化したレポート。
    /// JsonUtility でシリアライズするため public フィールドで構成する。
    /// 生成元（<see cref="AddressTellerExplainReportBuilder"/>）が internal であり利用者からの到達手段が無いため、
    /// この型自体も internal とする（外部公開する場合は CLI/エクスポート経路の新設とあわせて再検討する）。
    /// </summary>
    [Serializable]
    internal sealed class AddressTellerExplainReport
    {
        public List<AddressTellerExplainAsset> Assets = new();

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static AddressTellerExplainReport FromJson(string json) => JsonUtility.FromJson<AddressTellerExplainReport>(json);
    }

    /// <summary>1アセット分の Explain 結果。</summary>
    [Serializable]
    internal sealed class AddressTellerExplainAsset
    {
        public string Path;

        /// <summary>AssetFilter.ShouldExclude により評価対象外と判定された場合 true。</summary>
        public bool IsExcluded;

        /// <summary><see cref="ValidationStatus"/> の名前（例: "ConflictingAddress"）。除外時は空文字。</summary>
        public string ValidationStatus;

        /// <summary>除外時は空文字。</summary>
        public string ValidationMessage;

        public List<AddressTellerExplainRule> Rules = new();
    }

    /// <summary>1ルール×1アセットの評価詳細。</summary>
    [Serializable]
    internal sealed class AddressTellerExplainRule
    {
        public string RuleSource;
        public string GroupName;
        public string Description;

        /// <summary><see cref="RuleMatchOutcome"/> の名前（例: "Matched"）。JsonUtility が enum を扱えないため文字列で持つ。</summary>
        public string Outcome;

        /// <summary>Matched かつ AddressSelector が指定されている場合のアドレス。それ以外は空文字。</summary>
        public string ProducedAddress;

        public List<string> ProducedLabels = new();

        /// <summary>Errored の場合の例外メッセージ。それ以外は空文字。</summary>
        public string ErrorMessage;
    }
}

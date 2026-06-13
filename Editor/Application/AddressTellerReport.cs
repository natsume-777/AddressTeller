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

    /// <summary>1アセット分のドリフト（Apply 適用後との差分）。</summary>
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
}

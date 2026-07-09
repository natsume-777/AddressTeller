using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace AddressTeller.Editor
{
    /// <summary>Diff 行の種別。</summary>
    internal enum DiffRowKind
    {
        Added,
        Removed,
        Changed,
    }

    /// <summary>
    /// <see cref="SnapshotDiff"/> の1エントリを表示用に変換した行データ。
    /// <see cref="DiffRowKind.Changed"/> のみ Before/After 両方を保持し、
    /// Added は Before 側、Removed は After 側が空になる。
    /// </summary>
    internal readonly struct DiffRow
    {
        public DiffRowKind Kind { get; }
        public string Guid { get; }
        public string AssetPath { get; }
        public string BeforeAddress { get; }
        public string AfterAddress { get; }
        public string BeforeGroup { get; }
        public string AfterGroup { get; }
        public string LabelsSummary { get; }

        public DiffRow(DiffRowKind kind, string guid, string assetPath,
            string beforeAddress, string afterAddress,
            string beforeGroup, string afterGroup, string labelsSummary)
        {
            Kind = kind;
            Guid = guid;
            AssetPath = assetPath;
            BeforeAddress = beforeAddress;
            AfterAddress = afterAddress;
            BeforeGroup = beforeGroup;
            AfterGroup = afterGroup;
            LabelsSummary = labelsSummary;
        }
    }

    /// <summary><see cref="ValidationResult"/> の1件を表示用に変換した行データ。</summary>
    internal readonly struct IssueRow
    {
        public ValidationStatus Status { get; }
        public string AssetPath { get; }
        public string Guid { get; }
        public string Message { get; }

        /// <summary><see cref="ValidationStatus.ConflictingAddress"/> のときのみ2件以上。それ以外は空。</summary>
        public IReadOnlyList<AddressCandidate> ConflictingCandidates { get; }

        public IssueRow(ValidationStatus status, string assetPath, string guid, string message,
            IReadOnlyList<AddressCandidate> conflictingCandidates)
        {
            Status = status;
            AssetPath = assetPath;
            Guid = guid;
            Message = message;
            ConflictingCandidates = conflictingCandidates;
        }
    }

    /// <summary>
    /// <see cref="DryRunResult"/> / <see cref="ValidationResult"/> を結果ウィンドウ表示用の行データへ変換する。
    /// AssetDatabase 呼び出しを含むため、Editor 専用 API への依存がある。
    /// </summary>
    internal static class AddressTellerResultWindowRows
    {
        /// <summary>
        /// <see cref="SnapshotDiff"/> を <see cref="DiffRow"/> のリストへ変換する。
        /// 並び順は Kind → AssetPath（序数比較）の決定的ソート。
        /// </summary>
        public static List<DiffRow> BuildDiffRows(SnapshotDiff diff)
        {
            if (diff == null)
                return new List<DiffRow>();

            var rows = new List<DiffRow>();

            foreach (var entry in diff.Added)
            {
                rows.Add(new DiffRow(
                    DiffRowKind.Added,
                    entry.Guid,
                    ResolveAssetPath(entry.Guid),
                    beforeAddress: string.Empty,
                    afterAddress: entry.Address,
                    beforeGroup: string.Empty,
                    afterGroup: entry.GroupName,
                    labelsSummary: SummarizeLabels(entry.Labels)));
            }

            foreach (var entry in diff.Removed)
            {
                rows.Add(new DiffRow(
                    DiffRowKind.Removed,
                    entry.Guid,
                    ResolveAssetPath(entry.Guid),
                    beforeAddress: entry.Address,
                    afterAddress: string.Empty,
                    beforeGroup: entry.GroupName,
                    afterGroup: string.Empty,
                    labelsSummary: SummarizeLabels(entry.Labels)));
            }

            foreach (var (before, after) in diff.Changed)
            {
                rows.Add(new DiffRow(
                    DiffRowKind.Changed,
                    after.Guid,
                    ResolveAssetPath(after.Guid),
                    beforeAddress: before.Address,
                    afterAddress: after.Address,
                    beforeGroup: before.GroupName,
                    afterGroup: after.GroupName,
                    labelsSummary: SummarizeLabels(after.Labels)));
            }

            return rows
                .OrderBy(r => r.Kind)
                .ThenBy(r => r.AssetPath, StringComparer.Ordinal)
                .ThenBy(r => r.Guid, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// <see cref="ValidationResult"/> の一覧を <see cref="IssueRow"/> のリストへ変換する。
        /// 並び順は Status → AssetPath（序数比較）の決定的ソート。
        /// </summary>
        public static List<IssueRow> BuildIssueRows(IReadOnlyList<ValidationResult> issues)
        {
            return issues
                .Select(issue =>
                {
                    var candidates = issue.ConflictingCandidates ?? (IReadOnlyList<AddressCandidate>)Array.Empty<AddressCandidate>();

                    // ConflictingAddress の Message は候補ごとの内訳を含む複数行文字列だが、
                    // 候補が2件以上ある場合はその内訳を子行（TreeView）側で表示するため、
                    // 親行には概要（1行目）のみを表示し重複・行高オーバーフローを避ける。
                    var message = candidates.Count >= 2
                        ? issue.Message.Split('\n')[0]
                        : issue.Message;

                    // Context は設計上 nullable（AddressTellerReportBuilder.BuildReport と同様に防御的にガードする）。
                    return new IssueRow(issue.Status, issue.Context?.Path ?? string.Empty, issue.Context?.Guid ?? string.Empty, message, candidates);
                })
                .OrderBy(r => r.Status)
                .ThenBy(r => r.AssetPath, StringComparer.Ordinal)
                .ThenBy(r => r.Guid, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>GUID をアセットパスへ解決する。解決できない場合は GUID をそのまま返す。</summary>
        private static string ResolveAssetPath(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? guid : path;
        }

        private static string SummarizeLabels(List<string> labels) =>
            string.Join(", ", labels ?? (IEnumerable<string>)Array.Empty<string>());
    }
}

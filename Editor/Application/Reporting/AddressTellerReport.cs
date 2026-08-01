using System;
using System.Collections.Generic;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Report structuring the result of Check (dry-run) / Apply for CI consumption.
    /// Composed of public fields since it is serialized with JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class AddressTellerReport
    {
        /// <summary>Aggregate counts for this report.</summary>
        public AddressTellerReportSummary Summary = new();

        /// <summary>Per-asset drift entries (the predicted diff computed before Apply is performed).</summary>
        public List<AddressTellerReportDrift> Drift = new();

        /// <summary>Validation problems detected while building this report.</summary>
        public List<AddressTellerReportIssue> Issues = new();

        /// <summary>
        /// Logical bundle distribution summary. Left <c>null</c> — never assigned a new value — when the
        /// dry-run has no <see cref="DryRunResult.After"/> (e.g. when constructed in a test), when no
        /// <see cref="UnityEditor.AddressableAssets.Settings.AddressableAssetSettings"/> was supplied to the
        /// builder, or when the bundle-distribution calculation itself throws (caught internally and logged
        /// as a warning). Because this field's type has no way to serialize a null reference under
        /// <see cref="JsonUtility"/>, the JSON output always contains a <c>BundleDistribution</c> object even
        /// while the C# field itself is still <c>null</c>; in the "not calculated" cases above the serialized
        /// object appears with every field at its C# default (<c>Bundles: []</c>, counts at <c>0</c>,
        /// <c>Disclaimer: ""</c>) rather than being omitted or written as JSON <c>null</c>.
        /// </summary>
        public BundleDistributionReport BundleDistribution;

        /// <summary>Currently supported report schema version. See <see cref="SchemaVersion"/>.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// Schema version of this report, mirroring <see cref="AddressTellerSnapshot.SchemaVersion"/>.
        /// Set to <see cref="CurrentSchemaVersion"/> by the internal report builder used by
        /// <c>CheckCLI</c>/<c>ApplyAllCLI</c>/<c>ApplyWithValidateCLI</c>.
        /// </summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>Serializes this report to pretty-printed JSON via <see cref="JsonUtility"/>.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>
        /// Deserializes a report previously produced by <see cref="ToJson"/>. Returns <c>null</c> when the
        /// deserialized <see cref="SchemaVersion"/> is greater than <see cref="CurrentSchemaVersion"/> (a
        /// schema this version of the package does not know how to interpret) — in that case, a warning
        /// is logged that includes only the offending version numbers, not the source file path (the caller
        /// is best positioned to add that context, e.g. when reading a specific report file).
        /// Only the <see cref="SchemaVersion"/> rejection policy mirrors
        /// <see cref="AddressTellerSnapshotService.LoadFromFile"/>'s rejection of snapshots from a newer
        /// schema; unlike <c>LoadFromFile</c>, this method does not validate the JSON's content and does
        /// not catch exceptions from the underlying <see cref="JsonUtility"/> call — a malformed
        /// <paramref name="json"/> string propagates as whatever exception <c>JsonUtility</c> throws.
        /// </summary>
        public static AddressTellerReport FromJson(string json)
        {
            var report = JsonUtility.FromJson<AddressTellerReport>(json);
            if (report == null) return null;

            if (report.SchemaVersion > CurrentSchemaVersion)
            {
                Debug.LogWarning($"[AddressTeller] Report schema version ({report.SchemaVersion}) is not supported (current: {CurrentSchemaVersion}).");
                return null;
            }

            return report;
        }
    }

    /// <summary>Aggregate counts for the whole report.</summary>
    [Serializable]
    public sealed class AddressTellerReportSummary
    {
        /// <summary>Number of entries added by the predicted diff.</summary>
        public int Added;

        /// <summary>Number of entries removed by the predicted diff.</summary>
        public int Removed;

        /// <summary>Number of entries changed by the predicted diff.</summary>
        public int Changed;

        /// <summary>Number of validation problems detected.</summary>
        public int Issues;

        /// <summary>Process exit code this report corresponds to.</summary>
        public int ExitCode;
    }

    /// <summary>Drift for a single asset (the predicted diff computed before Apply is performed).</summary>
    [Serializable]
    public sealed class AddressTellerReportDrift
    {
        /// <summary>GUID of the asset this drift entry is about.</summary>
        public string Guid;

        /// <summary>Asset path at the time the report was built.</summary>
        public string Path;

        /// <summary>"Added" / "Removed" / "Changed". Stored as a string since JsonUtility cannot handle enums.</summary>
        public string ChangeType;

        /// <summary>State before the predicted change. Fields are empty/default for an "Added" entry.</summary>
        public AddressTellerReportEntry Before = new();

        /// <summary>State after the predicted change. Fields are empty/default for a "Removed" entry.</summary>
        public AddressTellerReportEntry After = new();
    }

    /// <summary>Address/Group/Labels for a single Before/After side of a drift entry.</summary>
    [Serializable]
    public sealed class AddressTellerReportEntry
    {
        /// <summary>Address assigned to the asset.</summary>
        public string Address;

        /// <summary>Name of the group the asset belongs to.</summary>
        public string GroupName;

        /// <summary>Labels assigned to the asset.</summary>
        public List<string> Labels = new();
    }

    /// <summary>A single problem detected by validation.</summary>
    [Serializable]
    public sealed class AddressTellerReportIssue
    {
        /// <summary>Asset path the issue is about.</summary>
        public string Path;

        /// <summary>Name of the <see cref="ValidationStatus"/> value (e.g. "ConflictingAddress").</summary>
        public string Status;

        /// <summary>Human-readable description of the issue.</summary>
        public string Message;
    }

    /// <summary>
    /// Logical bundle distribution summary. This is a logical estimate computed from the Predict result
    /// and each group's BundleMode; it does not guarantee the actual bundle count from a real Addressables
    /// build (see <see cref="Disclaimer"/>).
    /// </summary>
    [Serializable]
    public sealed class BundleDistributionReport
    {
        /// <summary>Logical bundles computed from the dry-run result, one per group/split combination.</summary>
        public BundleDistributionReportEntry[] Bundles = Array.Empty<BundleDistributionReportEntry>();

        /// <summary>Total number of logical bundles, excluding Unknown.</summary>
        public int TotalLogicalBundleCount;

        /// <summary>Number of groups whose BundleMode could not be determined (Unknown).</summary>
        public int UnknownGroupCount;

        /// <summary>Fixed text noting that this distribution is a logical estimate.</summary>
        public string Disclaimer = "";
    }

    /// <summary>
    /// Serialized (JSON report) form of a single logical bundle. This is the shape written to
    /// <see cref="AddressTellerReport"/> output; the domain model it is derived from is
    /// <see cref="LogicalBundle"/>.
    /// </summary>
    [Serializable]
    public sealed class BundleDistributionReportEntry
    {
        /// <summary>Name of the Addressables group this logical bundle belongs to.</summary>
        public string GroupName;

        /// <summary>Name of the <see cref="BundleModeKind"/> value.</summary>
        public string Mode;

        /// <summary>
        /// Serialized form of <see cref="LogicalBundle.SplitKey"/> (e.g. "all", an asset id, or a label
        /// key). May also be the fixed strings <see cref="BundleDistributionCalculator.NoLabelsSplitKey"/>
        /// or <see cref="BundleDistributionCalculator.UnknownSplitKey"/>.
        /// </summary>
        public string SplitKey;

        /// <summary>Number of assets placed in this logical bundle.</summary>
        public int AssetCount;
    }
}

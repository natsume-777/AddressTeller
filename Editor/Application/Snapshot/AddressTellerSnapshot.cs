using System;
using System.Collections.Generic;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Snapshot of the Addressables address/label/group assignment state.
    /// Composed of public fields since it is serialized with JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class AddressTellerSnapshot
    {
        /// <summary>Per-asset entries captured at snapshot time.</summary>
        public List<SnapshotEntry> Entries = new();

        /// <summary>Capture timestamp (UTC, ISO 8601 format). Empty string for older-format JSON.</summary>
        public string CapturedAtIso = "";

        /// <summary>Optional free-form comment supplied by the user.</summary>
        public string Comment = "";

        /// <summary>Unity version at capture time (<see cref="Application.unityVersion"/>).</summary>
        public string UnityVersion = "";

        /// <summary>AddressTeller package version at capture time. Empty string if it could not be determined.</summary>
        public string PackageVersion = "";

        /// <summary>
        /// Schema version of this snapshot. Set to <see cref="AddressTellerSnapshotService.CurrentSchemaVersion"/>
        /// by <see cref="AddressTellerSnapshotService.Capture"/>.
        /// Reads back as 0 when loading older-format JSON that lacks this field, or immediately after
        /// initialization.
        /// </summary>
        public int SchemaVersion = 0;

        /// <summary>Serializes this snapshot to pretty-printed JSON via <see cref="JsonUtility"/>.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>Deserializes a snapshot previously produced by <see cref="ToJson"/>.</summary>
        public static AddressTellerSnapshot FromJson(string json) => JsonUtility.FromJson<AddressTellerSnapshot>(json);
    }

    /// <summary>A single asset's entry within a snapshot.</summary>
    [Serializable]
    public sealed class SnapshotEntry
    {
        /// <summary>GUID of the asset this entry is about.</summary>
        public string Guid;

        /// <summary>Address assigned to the asset.</summary>
        public string Address;

        /// <summary>Name of the group the asset belongs to.</summary>
        public string GroupName;

        /// <summary>Labels assigned to the asset.</summary>
        public List<string> Labels = new();
    }
}

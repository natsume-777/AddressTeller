using System.Linq;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSettingsTextDiff.Compare の単体テスト。
    /// ここで埋め込む旧形式テキストは実測で確認済みのもの（このバージョンより前の AddressTeller が
    /// 書き出す形式を、このバージョンで読み込むと全フィールドが既定値化する — その実測に使ったファイル
    /// そのものの本文）で、ファイル I/O を一切経由しない（ProjectSettings 配下の実ファイルには一切触れない）。
    /// </summary>
    public class AddressTellerSettingsTextDiffTests
    {
        // 旧形式（このバージョンより前の AddressTeller が書き出す形式）、非既定値。
        // m_Script: {fileID: 0} かつ m_EditorClassIdentifier が旧識別子（コロン区切り）。
        private const string OldFormatNonDefault = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 53
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 0}
  m_Name:
  m_EditorClassIdentifier: AddressTeller.Editor:AddressTeller.Editor:AddressTellerSettingsAsset
  _cleanupStaleEntries: 0
  _postprocessEnabled: 1
  _snapshotFolder: ProbeSnapshots
  _autoSnapshotBeforeApplyAll: 1
  _autoSnapshotRetention: 3
  _autoCreateMissingGroups: 1
  _postprocessOrder: 1234
  _disabledRuleClassNames:
  - Probe.Rule
";

        // 旧形式、ただし全フィールドが既定値（新形式のデフォルトと同じ値）。
        // 旧形式そのものだけでは差分が出ないことを固定する回帰テスト用。
        private const string OldFormatDefault = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 53
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 0}
  m_Name:
  m_EditorClassIdentifier: AddressTeller.Editor:AddressTeller.Editor:AddressTellerSettingsAsset
  _cleanupStaleEntries: 1
  _postprocessEnabled: 1
  _snapshotFolder: AddressTellerSnapshots
  _autoSnapshotBeforeApplyAll: 1
  _autoSnapshotRetention: 10
  _autoCreateMissingGroups: 0
  _postprocessOrder: 1000
  _disabledRuleClassNames: []
";

        // 現行の正常な形式（新識別子）、既定値。
        private const string CurrentFormatDefault = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 53
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: ca9673d549a3412b8f0dd5755bb580cd, type: 3}
  m_Name:
  m_EditorClassIdentifier: AddressTeller.Editor::AddressTeller.Editor.AddressTellerSettingsAsset
  _cleanupStaleEntries: 1
  _postprocessEnabled: 1
  _snapshotFolder: AddressTellerSnapshots
  _autoSnapshotBeforeApplyAll: 1
  _autoSnapshotRetention: 10
  _autoCreateMissingGroups: 0
  _postprocessOrder: 1000
  _disabledRuleClassNames: []
";

        [Test]
        public void OldFormatNonDefaultOnDisk_VsCurrentDefaultInMemory_DetectsAllDifferingFieldsAndTypeIdentifierMismatch()
        {
            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(OldFormatNonDefault, CurrentFormatDefault);

            Assert.IsFalse(typeIdentifierMatches,
                "m_Script/m_EditorClassIdentifier が旧形式と新形式で異なるため、型識別子は不一致と判定されるべき。");

            var fieldNames = diffs.Select(d => d.FieldName).ToList();
            CollectionAssert.AreEqual(
                new[]
                {
                    "_autoCreateMissingGroups",
                    "_autoSnapshotRetention",
                    "_cleanupStaleEntries",
                    "_disabledRuleClassNames",
                    "_postprocessOrder",
                    "_snapshotFolder",
                },
                fieldNames,
                "差分フィールドはフィールド名の Ordinal 昇順で決定的に並ぶべき。");

            var postprocessOrderDiff = diffs.Single(d => d.FieldName == "_postprocessOrder");
            Assert.AreEqual("1234", postprocessOrderDiff.DiskValue);
            Assert.AreEqual("1000", postprocessOrderDiff.MemoryValue);

            // 同値のフィールド（_postprocessEnabled, _autoSnapshotBeforeApplyAll）は差分に含まれないこと。
            Assert.IsFalse(fieldNames.Contains("_postprocessEnabled"));
            Assert.IsFalse(fieldNames.Contains("_autoSnapshotBeforeApplyAll"));
        }

        [Test]
        public void OldFormatAllDefaultOnDisk_VsCurrentDefaultInMemory_NoFieldDiffsDespiteTypeIdentifierMismatch()
        {
            // 誤検知しないことの回帰テスト: 旧形式というだけでは差分は出ない。値が実際に食い違っているときだけ出る。
            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(OldFormatDefault, CurrentFormatDefault);

            Assert.IsFalse(typeIdentifierMatches);
            CollectionAssert.IsEmpty(diffs);
        }

        [Test]
        public void CurrentFormatOnBothSides_IdenticalValues_NoDiffsAndTypeIdentifierMatches()
        {
            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(CurrentFormatDefault, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches);
            CollectionAssert.IsEmpty(diffs);
        }

        [Test]
        public void CurrentFormatOnBothSides_OneValueDiffers_DetectsOnlyThatFieldWithTypeIdentifierMatching()
        {
            var diskText = CurrentFormatDefault.Replace("_postprocessOrder: 1000", "_postprocessOrder: 2000");

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches, "m_Script は両側とも新形式で一致するため型識別子は一致するべき。");
            Assert.AreEqual(1, diffs.Count);
            Assert.AreEqual("_postprocessOrder", diffs[0].FieldName);
            Assert.AreEqual("2000", diffs[0].DiskValue);
            Assert.AreEqual("1000", diffs[0].MemoryValue);
        }

        [Test]
        public void ScriptGuidMatchesButEditorClassIdentifierDiffers_TypeIdentifierStillMatches()
        {
            // 型識別の判定は m_Script のみで行う（m_EditorClassIdentifier は見ない）。
            // m_Script（実際に Unity が型解決に使う GUID 参照）が一致していれば、Unity はこの型を正しく
            // 解決できているはずであり、この状態を「読めなかった」側（variant A）に誤分類してはならない。
            // m_EditorClassIdentifier 自体は値比較からも除外されているため、それだけが食い違っても diffs には現れない。
            var diskText = CurrentFormatDefault.Replace(
                "m_EditorClassIdentifier: AddressTeller.Editor::AddressTeller.Editor.AddressTellerSettingsAsset",
                "m_EditorClassIdentifier: SomeOther.Namespace::SomeOther.Namespace.SomeOtherType");

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches,
                "m_Script が一致していれば、m_EditorClassIdentifier だけが食い違っても型識別子は一致と判定すべき。");
            CollectionAssert.IsEmpty(diffs, "m_EditorClassIdentifier は値比較の対象外のため、これだけの相違では diffs に現れない。");
        }

        [Test]
        public void DisabledRuleClassNames_BlockListFormOnOneSideOnly_ExtractsAsDiffOnThatFieldAlone()
        {
            var diskText = CurrentFormatDefault.Replace(
                "_disabledRuleClassNames: []",
                "_disabledRuleClassNames:\n  - MyNamespace.RuleA\n  - MyNamespace.RuleB");

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches);
            Assert.AreEqual(1, diffs.Count);
            Assert.AreEqual("_disabledRuleClassNames", diffs[0].FieldName);
            Assert.AreEqual("- MyNamespace.RuleA\n- MyNamespace.RuleB", diffs[0].DiskValue);
            Assert.AreEqual("[]", diffs[0].MemoryValue);
        }

        [Test]
        public void DisabledRuleClassNames_SameBlockListOnBothSides_NoDiff()
        {
            var blockList = "_disabledRuleClassNames:\n  - MyNamespace.RuleA";
            var diskText = CurrentFormatDefault.Replace("_disabledRuleClassNames: []", blockList);
            var memoryText = CurrentFormatDefault.Replace("_disabledRuleClassNames: []", blockList);

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, memoryText);

            Assert.IsTrue(typeIdentifierMatches);
            CollectionAssert.IsEmpty(diffs);
        }

        [Test]
        public void UnknownFieldOnlyOnDiskSide_IsIgnored()
        {
            // メモリ側テキストに現れない鍵は比較対象から自動的に除外される（候補鍵はメモリ側から導出するため）。
            var diskText = CurrentFormatDefault.Replace(
                "_postprocessOrder: 1000",
                "_postprocessOrder: 1000\n  _futureFieldNotYetKnown: 5");

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches);
            CollectionAssert.IsEmpty(diffs);
        }

        [Test]
        public void FieldMissingFromDiskSide_IsIgnoredRatherThanReportedAsDiff()
        {
            // ディスク側にだけ存在しない鍵（バージョン間でフィールドが増減した場合を想定）は、
            // 両辺に存在する鍵だけを比較する契約により無視される。
            var diskText = CurrentFormatDefault.Replace("  _autoCreateMissingGroups: 0\n", "");

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches);
            CollectionAssert.IsEmpty(diffs);
        }

        [Test]
        public void CrlfLineEndingsOnOneSide_DoesNotProduceSpuriousDiff()
        {
            var diskTextCrlf = CurrentFormatDefault.Replace("\n", "\r\n");

            var (diffs, typeIdentifierMatches) = AddressTellerSettingsTextDiff.Compare(diskTextCrlf, CurrentFormatDefault);

            Assert.IsTrue(typeIdentifierMatches);
            CollectionAssert.IsEmpty(diffs);
        }
    }
}

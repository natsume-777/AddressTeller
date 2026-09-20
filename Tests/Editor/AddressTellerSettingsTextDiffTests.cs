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
            var comparison = AddressTellerSettingsTextDiff.Compare(OldFormatNonDefault, CurrentFormatDefault);

            Assert.IsFalse(comparison.TypeIdentifierMatches,
                "m_Script/m_EditorClassIdentifier が旧形式と新形式で異なるため、型識別子は不一致と判定されるべき。");

            var fieldNames = comparison.Diffs.Select(d => d.FieldName).ToList();
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

            var postprocessOrderDiff = comparison.Diffs.Single(d => d.FieldName == "_postprocessOrder");
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
            var comparison = AddressTellerSettingsTextDiff.Compare(OldFormatDefault, CurrentFormatDefault);

            Assert.IsFalse(comparison.TypeIdentifierMatches);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        [Test]
        public void CurrentFormatOnBothSides_IdenticalValues_NoDiffsAndTypeIdentifierMatches()
        {
            var comparison = AddressTellerSettingsTextDiff.Compare(CurrentFormatDefault, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        [Test]
        public void CurrentFormatOnBothSides_OneValueDiffers_DetectsOnlyThatFieldWithTypeIdentifierMatching()
        {
            var diskText = CurrentFormatDefault.Replace("_postprocessOrder: 1000", "_postprocessOrder: 2000");

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches, "m_Script は両側とも新形式で一致するため型識別子は一致するべき。");
            Assert.AreEqual(1, comparison.Diffs.Count);
            Assert.AreEqual("_postprocessOrder", comparison.Diffs[0].FieldName);
            Assert.AreEqual("2000", comparison.Diffs[0].DiskValue);
            Assert.AreEqual("1000", comparison.Diffs[0].MemoryValue);
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

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches,
                "m_Script が一致していれば、m_EditorClassIdentifier だけが食い違っても型識別子は一致と判定すべき。");
            CollectionAssert.IsEmpty(comparison.Diffs, "m_EditorClassIdentifier は値比較の対象外のため、これだけの相違では diffs に現れない。");
        }

        [Test]
        public void DisabledRuleClassNames_BlockListFormOnOneSideOnly_ExtractsAsDiffOnThatFieldAlone()
        {
            var diskText = CurrentFormatDefault.Replace(
                "_disabledRuleClassNames: []",
                "_disabledRuleClassNames:\n  - MyNamespace.RuleA\n  - MyNamespace.RuleB");

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches);
            Assert.AreEqual(1, comparison.Diffs.Count);
            Assert.AreEqual("_disabledRuleClassNames", comparison.Diffs[0].FieldName);
            Assert.AreEqual("- MyNamespace.RuleA\n- MyNamespace.RuleB", comparison.Diffs[0].DiskValue);
            Assert.AreEqual("[]", comparison.Diffs[0].MemoryValue);
        }

        [Test]
        public void DisabledRuleClassNames_SameBlockListOnBothSides_NoDiff()
        {
            var blockList = "_disabledRuleClassNames:\n  - MyNamespace.RuleA";
            var diskText = CurrentFormatDefault.Replace("_disabledRuleClassNames: []", blockList);
            var memoryText = CurrentFormatDefault.Replace("_disabledRuleClassNames: []", blockList);

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, memoryText);

            Assert.IsTrue(comparison.TypeIdentifierMatches);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        [Test]
        public void UnknownFieldOnlyOnDiskSide_IsIgnored()
        {
            // メモリ側テキストに現れない鍵は比較対象から自動的に除外される（候補鍵はメモリ側から導出するため）。
            var diskText = CurrentFormatDefault.Replace(
                "_postprocessOrder: 1000",
                "_postprocessOrder: 1000\n  _futureFieldNotYetKnown: 5");

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        [Test]
        public void FieldMissingFromDiskSide_IsIgnoredRatherThanReportedAsDiff()
        {
            // ディスク側にだけ存在しない鍵（バージョン間でフィールドが増減した場合を想定）は、
            // 両辺に存在する鍵だけを比較する契約により無視される。
            var diskText = CurrentFormatDefault.Replace("  _autoCreateMissingGroups: 0\n", "");

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        [Test]
        public void CrlfLineEndingsOnOneSide_DoesNotProduceSpuriousDiff()
        {
            var diskTextCrlf = CurrentFormatDefault.Replace("\n", "\r\n");

            var comparison = AddressTellerSettingsTextDiff.Compare(diskTextCrlf, CurrentFormatDefault);

            Assert.IsTrue(comparison.TypeIdentifierMatches);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        // --- SettingsTextComparison.MemoryTextRecognized / ComparableFieldCount ---
        // AddressTellerSettingsLoadDiagnostics.Diagnose() が FileUnparsable / DiagnosticUnavailable を
        // 判定する基準そのものを、Compare() 単体のレベルで固定する。

        [Test]
        public void CurrentFormatBothSides_GoldenComparableFieldCount_IsEightNonHeaderFields()
        {
            // ゴールデン: 現行スキーマの非ヘッダフィールド数（誤検知の境界を固定する）。
            // 8 は実測で確認済み（628件greenの時点で確定）。この値が変わるのは AddressTellerSettingsAsset の
            // [SerializeField] フィールドが増減したときだけであり、その場合は本ファイルの
            // CurrentFormatDefault 等のフィクスチャも同時に更新すること（この2つ目のゴールデン
            // （OldFormatNonDefaultOnDisk_ComparableFieldCount_MatchesCurrentFieldCount）も同じ8を使うため、
            // 意図はここ1箇所にまとめている）。同じ理由で CHANGELOG.md/.ja.md の `Before upgrading` /
            // `アップデート前に` で始まるチェックリストの項目数、および Documentation~/operations.md/.ja.md
            // の `## Project Settings` 節（Project Settings 箇条書き・`Auto Safety Snapshot` 箇条書き・
            // ルール有効/無効の段落の3箇所に分散しているが合計で全8フィールドを説明している）も、この8との
            // 対応を保つこと（詳細は AddressTellerSettingsAsset.cs のフィールド宣言直上のコメント参照）。
            var comparison = AddressTellerSettingsTextDiff.Compare(CurrentFormatDefault, CurrentFormatDefault);

            Assert.AreEqual(8, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void OldFormatNonDefaultOnDisk_ComparableFieldCount_MatchesCurrentFieldCount()
        {
            // 回帰: 旧形式ファイルは全フィールド名が現行と完全に同名のため、ComparableFieldCount は
            // 0 にならない——つまり Diagnose() は FileUnparsable ではなく、従来どおり variant A の
            // Mismatch に分類される。8 の由来は上のテストのコメント参照。
            var comparison = AddressTellerSettingsTextDiff.Compare(OldFormatNonDefault, CurrentFormatDefault);

            Assert.AreEqual(8, comparison.ComparableFieldCount);
        }

        [Test]
        public void DiskTextEmpty_ComparableFieldCountIsZero_ButMemoryTextRecognized()
        {
            var comparison = AddressTellerSettingsTextDiff.Compare(string.Empty, CurrentFormatDefault);

            Assert.AreEqual(0, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void DiskTextIndentedWithThreeSpaces_NotRecognizedAsTopLevelFields_ComparableFieldCountIsZero()
        {
            // トップレベル鍵の検出は2スペースインデント固定。3スペースインデントは検出されない
            // （ネストした値・リスト継続行と区別できないため）。
            var diskText = string.Join("\n", CurrentFormatDefault.Split('\n').Select(line =>
                line.StartsWith("  ") ? " " + line : line));

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.AreEqual(0, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void DiskTextWithNoIndentation_ComparableFieldCountIsZero()
        {
            var diskText = string.Join("\n", CurrentFormatDefault.Split('\n').Select(line => line.TrimStart()));

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.AreEqual(0, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void DiskTextNotYaml_ComparableFieldCountIsZero()
        {
            var diskText = "this is not a YAML settings file at all, just some unrelated binary-ish garbage";

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.AreEqual(0, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void DiskTextHeaderKeysOnly_ComparableFieldCountIsZero()
        {
            var diskText = @"%YAML 1.1
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
";

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.AreEqual(0, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void DiskTextNonHeaderKeysDoNotOverlapCurrentSchema_ComparableFieldCountIsZero()
        {
            // ディスク側に非ヘッダ鍵は抽出できるが、現行スキーマの鍵と1つも名前が重ならない
            // （全く別の型のアセットを読んだ場合を想定）。「抽出鍵ゼロ」より広い判定基準であることの固定。
            var diskText = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 53
  m_Script: {fileID: 11500000, guid: 0000000000000000000000000000000, type: 3}
  m_Name:
  _someCompletelyUnrelatedFieldName: 42
  _anotherUnrelatedField: hello
";

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.AreEqual(0, comparison.ComparableFieldCount);
            Assert.IsTrue(comparison.MemoryTextRecognized);
        }

        [Test]
        public void MemoryTextEmpty_MemoryTextRecognizedIsFalse_EvenWhenDiskTextIsAlsoUnparsable()
        {
            // メモリ側優先の固定: ディスク側も解釈不能な場合でも、判定は MemoryTextRecognized 側が優先される
            // （AddressTellerSettingsLoadDiagnostics.Diagnose がこの順序に依存している）。
            var comparison = AddressTellerSettingsTextDiff.Compare(string.Empty, string.Empty);

            Assert.IsFalse(comparison.MemoryTextRecognized);
            Assert.AreEqual(0, comparison.ComparableFieldCount);
        }

        [Test]
        public void MemoryTextNotYaml_MemoryTextRecognizedIsFalse()
        {
            var comparison = AddressTellerSettingsTextDiff.Compare(CurrentFormatDefault, "not yaml at all");

            Assert.IsFalse(comparison.MemoryTextRecognized);
        }

        [Test]
        public void DiskTextWithLeadingUtf8Bom_DoesNotProduceSpuriousFileUnparsableClassification()
        {
            // BOM (U+FEFF) を char キャストで組み立てる。文字列リテラルへ直接埋め込むと、ツール経由の
            // エスケープシーケンス解釈により意図と異なるバイト列が書き込まれる事故が起きやすいため。
            var diskText = ((char)0xFEFF) + CurrentFormatDefault;

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            // BOM は先頭行 "%YAML 1.1" の識別を壊しうるが、トップレベル鍵の抽出（2スペースインデント行の
            // 正規表現マッチ）自体には影響しない行から始まるため、フィールドは通常どおり認識される。
            Assert.AreEqual(8, comparison.ComparableFieldCount);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }

        [Test]
        public void DiskTextWithLeadingBlankLine_DoesNotProduceSpuriousFileUnparsableClassification()
        {
            var diskText = "\n" + CurrentFormatDefault;

            var comparison = AddressTellerSettingsTextDiff.Compare(diskText, CurrentFormatDefault);

            Assert.AreEqual(8, comparison.ComparableFieldCount);
            CollectionAssert.IsEmpty(comparison.Diffs);
        }
    }
}

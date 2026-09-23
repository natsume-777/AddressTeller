using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerReportWriter の単体テスト。
    /// JSON/JUnit のシリアライズ・エスケープ・ファイル書き込みを検証する。
    /// Addressables / AssetDatabase には依存しない（DTOのみを扱う）。
    /// </summary>
    public class AddressTellerReportWriterTests
    {
        private static AddressTellerReport EmptyReport()
        {
            var report = new AddressTellerReport();
            report.Summary.ExitCode = 0;
            return report;
        }

        private static AddressTellerReport ReportWithDriftAndIssues()
        {
            var report = new AddressTellerReport();
            report.Drift.Add(new AddressTellerReportDrift
            {
                Guid = "guid-1",
                Path = "Assets/Foo.prefab",
                ChangeType = "Added",
                Before = new AddressTellerReportEntry(),
                After = new AddressTellerReportEntry
                {
                    Address = "foo",
                    GroupName = "Default",
                    Labels = { "label1", "label2" },
                },
            });
            report.Drift.Add(new AddressTellerReportDrift
            {
                Guid = "guid-2",
                Path = "Assets/Bar.prefab",
                ChangeType = "Removed",
                Before = new AddressTellerReportEntry
                {
                    Address = "bar",
                    GroupName = "Default",
                },
                After = new AddressTellerReportEntry(),
            });

            report.Issues.Add(new AddressTellerReportIssue
            {
                Path = "Assets/Conflict.prefab",
                Status = "ConflictingAddress",
                Message = "2件のルールが衝突しました",
            });
            report.Issues.Add(new AddressTellerReportIssue
            {
                Path = "Assets/Group.prefab",
                Status = "GroupNotFound",
                Message = "グループが存在しません",
            });

            report.Summary.Added = 1;
            report.Summary.Removed = 1;
            report.Summary.Issues = 2;
            report.Summary.ExitCode = 2;

            return report;
        }

        /// <summary>
        /// <see cref="ReportWithDriftAndIssues"/> に加え、<see cref="AddressTellerReport.BundleDistribution"/>
        /// も埋めたレポート。<c>Bundles</c> が空配列のままだと配列要素のキー（<c>GroupName</c>/<c>Mode</c>/
        /// <c>SplitKey</c>/<c>AssetCount</c>）がJSON上に現れず、それらのキーの存在を検証するテストが
        /// 成立しないため、要素を1件明示的に設定する必要がある。
        /// </summary>
        private static AddressTellerReport ReportWithAllSections()
        {
            var report = ReportWithDriftAndIssues();
            report.BundleDistribution = new BundleDistributionReport
            {
                Bundles = new[]
                {
                    new BundleDistributionReportEntry
                    {
                        GroupName = "Characters",
                        Mode = "PackTogether",
                        SplitKey = "all",
                        AssetCount = 3,
                    },
                },
                TotalLogicalBundleCount = 1,
                UnknownGroupCount = 0,
                Disclaimer = "This distribution is a logical estimate.",
            };

            return report;
        }

        [Test]
        public void DefaultSchemaVersion_IsOne()
        {
            // AddressTellerSnapshot.SchemaVersion（未設定時は0）とは異なり、レポートは常に組み立て済みの
            // 構造化データであるため、素朴に構築した時点での既定値は1にしている。
            Assert.AreEqual(1, EmptyReport().SchemaVersion);
        }

        [Test]
        public void ToJson_RoundTrip_PreservesMainFields()
        {
            var report = ReportWithDriftAndIssues();

            var json = AddressTellerReportWriter.ToJson(report);
            var restored = AddressTellerReport.FromJson(json);

            Assert.AreEqual(report.Summary.Added, restored.Summary.Added);
            Assert.AreEqual(report.Summary.Removed, restored.Summary.Removed);
            Assert.AreEqual(report.Summary.Issues, restored.Summary.Issues);
            Assert.AreEqual(report.Summary.ExitCode, restored.Summary.ExitCode);

            Assert.AreEqual(report.Drift.Count, restored.Drift.Count);
            Assert.AreEqual(report.Drift[0].Path, restored.Drift[0].Path);
            Assert.AreEqual(report.Drift[0].ChangeType, restored.Drift[0].ChangeType);
            Assert.AreEqual(report.Drift[0].After.Address, restored.Drift[0].After.Address);
            CollectionAssert.AreEqual(report.Drift[0].After.Labels, restored.Drift[0].After.Labels);

            Assert.AreEqual(report.Issues.Count, restored.Issues.Count);
            Assert.AreEqual(report.Issues[0].Status, restored.Issues[0].Status);
            Assert.AreEqual(report.Issues[0].Message, restored.Issues[0].Message);
        }

        [Test]
        public void FromJson_FutureSchemaVersion_ReturnsNull()
        {
            // AddressTellerSnapshotService.LoadFromFile の SchemaVersion 拒否（未来バージョンは拒否）と対称の
            // 挙動を AddressTellerReport.FromJson 側でも確認する。
            var report = ReportWithDriftAndIssues();
            report.SchemaVersion = AddressTellerReport.CurrentSchemaVersion + 1;
            var json = AddressTellerReportWriter.ToJson(report);

            LogAssert.Expect(LogType.Warning, new Regex("Report schema version"));
            var restored = AddressTellerReport.FromJson(json);

            Assert.IsNull(restored);
        }

        [Test]
        public void FromJson_CurrentSchemaVersion_ReturnsReport()
        {
            var report = ReportWithDriftAndIssues();
            var json = AddressTellerReportWriter.ToJson(report);

            var restored = AddressTellerReport.FromJson(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(AddressTellerReport.CurrentSchemaVersion, restored.SchemaVersion);
        }

        [Test]
        public void FromJson_MissingSchemaVersionKey_ReadsAsCurrentSchemaVersion()
        {
            // "SchemaVersion" キー自体を含まない JSON（このフィールドが存在する前の旧形式レポート、または
            // 手動編集でキーを削除した入力を模する）。JsonUtility.FromJson<T> は対象の既定コンストラクタで
            // インスタンスを構築してから JSON に存在するキーだけを上書きする（未知/欠落キーはフィールド
            // 初期化子の値のまま）挙動をする、というのがこのテストの前提。AddressTellerSnapshot.SchemaVersion
            // の初期化子は 0（CLR既定値と同じ）でこの区別がつかないため、初期化子が非既定値
            // （AddressTellerReport.CurrentSchemaVersion = 1）であるこちらで実測結果として固定する。
            const string json = "{\"Summary\":{\"Added\":0,\"Removed\":0,\"Changed\":0,\"Issues\":0,\"ExitCode\":0},\"Drift\":[],\"Issues\":[]}";

            var restored = AddressTellerReport.FromJson(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(AddressTellerReport.CurrentSchemaVersion, restored.SchemaVersion);
        }

        // 以下は Documentation~/compatibility.md で「互換性契約」として文書化した JSON のキー名を固定するゴールデンテスト。
        // ToJson_RoundTrip_PreservesMainFields はC#オブジェクトを経由した値の再現性しか見ておらず、
        // フィールド名を一括リネームしても検出できないため、生成された JSON 文字列に対して直接キー名の有無を検証する。
        //
        // StringAssert.Contains によるキー名だけの部分一致（例: "\"GroupName\""）には、同じJSON内に
        // 同名キーが複数箇所に存在する場合（GroupName・Path が AddressTellerReportEntry と
        // BundleDistributionReportEntry / Drift と Issues の双方に存在する）や、フィールド名の文字列が
        // 別フィールドの値と偶然一致する場合（"ChangeType": "Added" の値 "Added" が Summary.Added という
        // キー名と同じ文字列）に、片方だけリネームしても検知できないという弱点がある。そのため以下では
        // キー名の直後にコロンと実際の値まで含めた文字列で照合し、値そのものを固有の識別子として使うことで
        // この曖昧さを排除する（JsonUtility はクラスのフィールド宣言順でシリアライズするため、末尾フィールド
        // 以外は直後にカンマが続くことも合わせて固定する）。
        //
        // トレードオフ: 上記のカンマ固定は、末尾以外のフィールドの宣言順を入れ替えただけ（キー名自体は不変で
        // 互換性ポリシー上は非破壊）の場合でも、末尾になったフィールドの直後にカンマが付かなくなる等の理由で
        // このテストを失敗させうる。意図的に許容している弱点であり、失敗時は差分がキー名の変更かカンマ位置
        // だけの変化かを個別に確認すること。

        [Test]
        public void ToJson_ContainsExpectedTopLevelKeys()
        {
            var report = ReportWithAllSections();

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"Summary\": {", json);
            StringAssert.Contains("\"Drift\": [", json);
            StringAssert.Contains("\"Issues\": [", json);
            StringAssert.Contains("\"BundleDistribution\": {", json);
            StringAssert.Contains("\"SchemaVersion\": ", json);
        }

        [Test]
        public void ToJson_SummaryKeys_ContainsAllFields()
        {
            // ReportWithAllSections（= ReportWithDriftAndIssues 由来）の Summary は
            // Added=1, Removed=1, Changed=0（未設定）, Issues=2, ExitCode=2。
            // Added/Removed はJSON中に "ChangeType": "Added" / "Removed" という値としても出現するため、
            // キー名だけの部分一致では区別できない。コロンと値まで含めて照合する。
            var report = ReportWithAllSections();

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"Added\": 1,", json);
            StringAssert.Contains("\"Removed\": 1,", json);
            StringAssert.Contains("\"Changed\": 0,", json);
            StringAssert.Contains("\"Issues\": 2,", json);
            StringAssert.Contains("\"ExitCode\": 2", json);
        }

        [Test]
        public void ToJson_DriftEntryKeys_ContainsAllFields()
        {
            // GroupName は AddressTellerReportEntry（Drift[].Before/After）と BundleDistributionReportEntry
            // （Bundles[]）の両方に存在するキー名のため、Drift 側の実際の値 "Default" を含めて照合する
            // （Bundles[0].GroupName の値 "Characters" とは異なる文字列であり、片方だけリネームされても
            // 検知できる）。Path も同様に Drift[] と Issues[] の双方に存在するため、Drift[0].Path の値
            // "Assets/Foo.prefab" で照合する。
            var report = ReportWithAllSections();

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"Guid\": \"guid-1\",", json);
            StringAssert.Contains("\"Path\": \"Assets/Foo.prefab\",", json);
            StringAssert.Contains("\"ChangeType\": \"Added\",", json);
            StringAssert.Contains("\"Before\": {", json);
            StringAssert.Contains("\"After\": {", json);
            StringAssert.Contains("\"Address\": \"foo\",", json);
            StringAssert.Contains("\"GroupName\": \"Default\",", json);
            StringAssert.Contains("\"Labels\": [", json);
        }

        [Test]
        public void ToJson_IssueEntryKeys_ContainsAllFields()
        {
            // Path は Issues[] だけでなく Drift[] にも存在するキー名のため、Issues[0].Path の実際の値
            // "Assets/Conflict.prefab" を含めて照合する（Drift 側は ToJson_DriftEntryKeys_ContainsAllFields
            // が別の値 "Assets/Foo.prefab" で固定済み）。
            var report = ReportWithAllSections();

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"Path\": \"Assets/Conflict.prefab\",", json);
            StringAssert.Contains("\"Status\": \"ConflictingAddress\",", json);
            StringAssert.Contains("\"Message\": \"2件のルールが衝突しました\"", json);
        }

        [Test]
        public void ToJson_BundleDistributionKeys_ContainsAllFields()
        {
            var report = ReportWithAllSections();

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"Bundles\": [", json);
            StringAssert.Contains("\"TotalLogicalBundleCount\": 1,", json);
            StringAssert.Contains("\"UnknownGroupCount\": 0,", json);
            StringAssert.Contains("\"Disclaimer\": \"This distribution is a logical estimate.\"", json);
        }

        [Test]
        public void ToJson_BundleDistributionEntryKeys_ContainsAllFields()
        {
            // GroupName は Drift[].Before/After（AddressTellerReportEntry）とも共有されるキー名のため、
            // Bundles[0].GroupName の実際の値 "Characters" を含めて照合する（Drift 側の値 "Default" とは
            // 異なる文字列であり、片方だけリネームされても検知できる）。
            var report = ReportWithAllSections();

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"GroupName\": \"Characters\",", json);
            StringAssert.Contains("\"Mode\": \"PackTogether\",", json);
            StringAssert.Contains("\"SplitKey\": \"all\",", json);
            StringAssert.Contains("\"AssetCount\": 3", json);
        }

        [Test]
        public void ToJson_BundleDistribution_Null_SerializesAsDefaultInstance()
        {
            // JsonUtility は UnityEngine.Object を継承しない [Serializable] クラス型のnull参照フィールドを
            // 省略せず、既定値インスタンスとしてシリアライズする仕様であることを固定する。
            // 未算出（BundleDistributionを設定しない）状態でも "BundleDistribution" キー自体は常に出力され、
            // 中身はBundles空配列・カウント0・Disclaimer空文字になる。
            var report = EmptyReport();
            Assert.IsNull(report.BundleDistribution);

            var json = AddressTellerReportWriter.ToJson(report);

            StringAssert.Contains("\"BundleDistribution\"", json);
            StringAssert.Contains("\"Bundles\": []", json);
            StringAssert.Contains("\"TotalLogicalBundleCount\": 0", json);
            StringAssert.Contains("\"UnknownGroupCount\": 0", json);
            StringAssert.Contains("\"Disclaimer\": \"\"", json);
        }

        [Test]
        public void ToJUnitXml_EmptyReport_IsValidXmlWithoutFailures()
        {
            var report = EmptyReport();

            var xml = AddressTellerReportWriter.ToJUnitXml(report);
            var doc = XDocument.Parse(xml);

            var testsuite = doc.Root;
            Assert.AreEqual("testsuite", testsuite.Name.LocalName);

            // drift testcase 1件のみ、failureなし
            Assert.AreEqual("1", testsuite.Attribute("tests").Value);
            Assert.AreEqual("0", testsuite.Attribute("failures").Value);

            var testcases = testsuite.Elements("testcase").ToList();
            Assert.AreEqual(1, testcases.Count);
            Assert.AreEqual("drift", testcases[0].Attribute("name").Value);
            Assert.IsNull(testcases[0].Element("failure"));
        }

        [Test]
        public void ToJUnitXml_WithDriftAndIssues_HasFailuresAndCorrectCounts()
        {
            var report = ReportWithDriftAndIssues();

            var xml = AddressTellerReportWriter.ToJUnitXml(report);
            var doc = XDocument.Parse(xml);

            var testsuite = doc.Root;

            // testcase: drift + ConflictingAddress + GroupNotFound = 3
            Assert.AreEqual("3", testsuite.Attribute("tests").Value);
            // 全てfailureあり
            Assert.AreEqual("3", testsuite.Attribute("failures").Value);

            var testcases = testsuite.Elements("testcase").ToList();
            Assert.AreEqual(3, testcases.Count);

            var driftCase = testcases.First(tc => tc.Attribute("name").Value == "drift");
            Assert.AreEqual("AddressTeller.Drift", driftCase.Attribute("classname").Value);
            var driftFailure = driftCase.Element("failure");
            Assert.IsNotNull(driftFailure);
            StringAssert.Contains("Assets/Foo.prefab", driftFailure.Value);
            StringAssert.Contains("Assets/Bar.prefab", driftFailure.Value);

            var conflictCase = testcases.First(tc => tc.Attribute("name").Value == "ConflictingAddress");
            Assert.AreEqual("AddressTeller.Validation", conflictCase.Attribute("classname").Value);
            var conflictFailure = conflictCase.Element("failure");
            Assert.IsNotNull(conflictFailure);
            StringAssert.Contains("Assets/Conflict.prefab", conflictFailure.Value);

            var groupCase = testcases.First(tc => tc.Attribute("name").Value == "GroupNotFound");
            Assert.IsNotNull(groupCase.Element("failure"));
        }

        [Test]
        public void ToJUnitXml_TreatDriftAsFailureFalse_DriftTestCaseHasNoFailure()
        {
            // ApplyAllCLI / ApplyWithValidateCLI 向け: Apply は既に成功しているため、drift 自体を
            // JUnit の <failure> として報告してはならない（同じランで exit code は 0 なのに、
            // この report を取り込む CI ジョブだけが赤くなるのを防ぐ）。
            var report = ReportWithDriftAndIssues();

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: false);
            var doc = XDocument.Parse(xml);

            var testsuite = doc.Root;
            // testcase 数（drift + ConflictingAddress + GroupNotFound = 3）は変わらないが、
            // drift 分の failure が無くなるため failures は 2 になる。
            Assert.AreEqual("3", testsuite.Attribute("tests").Value);
            Assert.AreEqual("2", testsuite.Attribute("failures").Value);

            var testcases = testsuite.Elements("testcase").ToList();
            var driftCase = testcases.First(tc => tc.Attribute("name").Value == "drift");
            Assert.IsNull(driftCase.Element("failure"), "drift は failure なしで testcase のみ出力されるべき。");

            // Issues 側（Apply 実行結果ではなく dry-run の issues）は従来どおり failure を維持する。
            var conflictCase = testcases.First(tc => tc.Attribute("name").Value == "ConflictingAddress");
            Assert.IsNotNull(conflictCase.Element("failure"));
        }

        [Test]
        public void ToJUnitXml_TreatDriftAsFailureDefault_MatchesTrue()
        {
            // 既定値（省略時）は true（CheckCLI 相当の挙動）であることを固定する。
            var report = ReportWithDriftAndIssues();

            var explicitTrue = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true);
            var omitted = AddressTellerReportWriter.ToJUnitXml(report);

            Assert.AreEqual(explicitTrue, omitted);
        }

        // 以下は internal オーバーロード（failingStatusNames による failure 対象の絞り込み）を検証する。
        // JUnit の testcase 構成（1 Status = 1 testcase）自体は公開オーバーロードと変わらず、
        // failure の有無だけが failingStatusNames に応じて変わることを確認する。

        // failure 対象の Status 名集合そのものは、本番で使われている
        // AddressTellerReportBuilder.BuildCheckCliFailingStatusNames / BuildApplyFailingStatusNames を
        // そのまま呼ぶ（基準を再実装せず、本番と同じ関数で検証する）。

        /// <summary><paramref name="issues"/> の Status 名をそのまま <see cref="AddressTellerReportIssue"/> に写したレポートを作る。</summary>
        private static AddressTellerReport ReportFromIssues(IReadOnlyList<ValidationResult> issues)
        {
            var report = new AddressTellerReport();
            foreach (var issue in issues)
            {
                report.Issues.Add(new AddressTellerReportIssue
                {
                    Path = "Assets/Dummy.prefab",
                    Status = issue.Status.ToString(),
                    Message = issue.Message,
                });
            }
            return report;
        }

        /// <summary><paramref name="xml"/> から name 属性が <paramref name="statusName"/> の Validation testcase を取り出す。</summary>
        private static XElement FindValidationTestCase(string xml, string statusName) =>
            XDocument.Parse(xml).Root.Elements("testcase").First(tc => tc.Attribute("name").Value == statusName);

        [Test]
        public void ToJUnitXml_FailingStatusNames_LimitsFailureToSpecifiedStatuses()
        {
            // ReportWithDriftAndIssues は ConflictingAddress / GroupNotFound の2 Statusを持つ。
            // 集合に ConflictingAddress のみを含めると、GroupNotFound の testcase は残るが failure が外れる。
            var report = ReportWithDriftAndIssues();

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, failingStatusNames: new[] { "ConflictingAddress" });
            var doc = XDocument.Parse(xml);
            var testsuite = doc.Root;

            // testcase 数（drift + ConflictingAddress + GroupNotFound = 3）は不変。
            Assert.AreEqual("3", testsuite.Attribute("tests").Value);
            // failures は drift(1) + ConflictingAddress(1) = 2 に減る（GroupNotFoundの分だけ減少）。
            Assert.AreEqual("2", testsuite.Attribute("failures").Value);

            Assert.IsNotNull(FindValidationTestCase(xml, "ConflictingAddress").Element("failure"));
            Assert.IsNull(FindValidationTestCase(xml, "GroupNotFound").Element("failure"), "集合に無い Status は testcase を残したまま failure だけ外れるべき。");
        }

        [Test]
        public void ToJUnitXml_FailingStatusNamesNull_MatchesAllStatusesFailing()
        {
            // failingStatusNames が null の場合、全 Status に無条件で failure が付く公開オーバーロードと同じ挙動。
            var report = ReportWithDriftAndIssues();

            var withNullSet = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, failingStatusNames: null);
            var publicOverload = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true);

            Assert.AreEqual(publicOverload, withNullSet);
        }

        [Test]
        public void ToJUnitXml_CheckCliCriteria_NoticeOnlyDuplicateAddress_HasNoFailure()
        {
            // 通知専用（管理外同士の重複、HasWritableDuplicate=false → IsOk=true）のみの場合、
            // CheckCLI 基準（!IsOk）では failure 対象に含まれない。
            var issues = new List<ValidationResult>
            {
                new ValidationResult(null, ValidationStatus.DuplicateAddress, "notice", hasWritableDuplicate: false),
            };
            var report = ReportFromIssues(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, AddressTellerReportBuilder.BuildCheckCliFailingStatusNames(issues));

            Assert.IsNull(FindValidationTestCase(xml, "DuplicateAddress").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_CheckCliCriteria_WritableDuplicateAddress_HasFailure()
        {
            // 書き込み対象を含む重複（HasWritableDuplicate=true → IsOk=false）は CheckCLI 基準で failure 対象。
            var issues = new List<ValidationResult>
            {
                new ValidationResult(null, ValidationStatus.DuplicateAddress, "writable", hasWritableDuplicate: true),
            };
            var report = ReportFromIssues(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, AddressTellerReportBuilder.BuildCheckCliFailingStatusNames(issues));

            Assert.IsNotNull(FindValidationTestCase(xml, "DuplicateAddress").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_CheckCliCriteria_MixedDuplicateAddress_HasFailure()
        {
            // 通知専用と書き込み対象を含む重複が同じ Status 内に混在する場合、1件でも !IsOk があれば
            // その Status 全体（testcase 単位）が failure 対象になる。
            var issues = new List<ValidationResult>
            {
                new ValidationResult(null, ValidationStatus.DuplicateAddress, "notice", hasWritableDuplicate: false),
                new ValidationResult(null, ValidationStatus.DuplicateAddress, "writable", hasWritableDuplicate: true),
            };
            var report = ReportFromIssues(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, AddressTellerReportBuilder.BuildCheckCliFailingStatusNames(issues));

            Assert.IsNotNull(FindValidationTestCase(xml, "DuplicateAddress").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_ApplyCriteria_WritableDuplicateAddress_HasNoFailure()
        {
            // Apply 系の基準（AddressTellerApplyFlow.IsBlocking）は DuplicateAddress を常に除外する
            // ——書き込み対象を含み IsOk=false であっても、Apply を中止しない報告専用ステータスのため。
            var issues = new List<ValidationResult>
            {
                new ValidationResult(null, ValidationStatus.DuplicateAddress, "writable", hasWritableDuplicate: true),
            };
            var report = ReportFromIssues(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: false, AddressTellerReportBuilder.BuildApplyFailingStatusNames(issues));

            Assert.IsNull(FindValidationTestCase(xml, "DuplicateAddress").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_ApplyCriteria_ConflictingAndGroupNotFound_HaveFailure()
        {
            // DuplicateAddress 以外の !IsOk ステータスは Apply 系の基準でも通常どおり failure 対象になる。
            var issues = new List<ValidationResult>
            {
                new ValidationResult(null, ValidationStatus.ConflictingAddress, "conflict"),
                new ValidationResult(null, ValidationStatus.GroupNotFound, "missing"),
            };
            var report = ReportFromIssues(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: false, AddressTellerReportBuilder.BuildApplyFailingStatusNames(issues));

            Assert.IsNotNull(FindValidationTestCase(xml, "ConflictingAddress").Element("failure"));
            Assert.IsNotNull(FindValidationTestCase(xml, "GroupNotFound").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_FailingStatusNamesEmpty_NoValidationTestCaseHasFailure()
        {
            // 空集合は「failure対象のStatusがひとつも無い」という意味であり、null（全Status対象）とは異なる。
            var report = ReportWithDriftAndIssues();

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, failingStatusNames: Array.Empty<string>());
            var doc = XDocument.Parse(xml);
            var testsuite = doc.Root;

            // testcase数（drift + ConflictingAddress + GroupNotFound = 3）は不変。
            Assert.AreEqual("3", testsuite.Attribute("tests").Value);
            // failures は drift(1)のみ。Validation側の2 Statusはどちらも空集合に含まれないためfailureなし。
            Assert.AreEqual("1", testsuite.Attribute("failures").Value);

            Assert.IsNull(FindValidationTestCase(xml, "ConflictingAddress").Element("failure"));
            Assert.IsNull(FindValidationTestCase(xml, "GroupNotFound").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_FailingStatusNames_ContainsUnknownStatus_IsIgnoredWithoutError()
        {
            // 集合に、このレポートに実在しないStatus名が混じっていても例外を投げず、
            // 実在するtestcaseのfailure判定にも影響しない。
            var report = ReportWithDriftAndIssues();

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true,
                failingStatusNames: new[] { "ConflictingAddress", "StatusThatDoesNotExistInThisReport" });
            var doc = XDocument.Parse(xml);

            Assert.AreEqual("3", doc.Root.Attribute("tests").Value);
            Assert.IsNotNull(FindValidationTestCase(xml, "ConflictingAddress").Element("failure"));
            Assert.IsNull(FindValidationTestCase(xml, "GroupNotFound").Element("failure"));
        }

        [Test]
        public void ToJUnitXml_FailingStatusNames_DriftFailureGovernedOnlyByTreatDriftAsFailure()
        {
            // failingStatusNamesはValidation系testcaseにのみ作用する。drift testcaseのfailure有無は
            // treatDriftAsFailureだけで決まり、failingStatusNamesに"drift"を含めても除いても変わらない。
            var report = ReportWithDriftAndIssues();

            // Validation側を全滅させる空集合でも、treatDriftAsFailure:trueならdriftはfailureを持つ。
            var xmlDriftTrue = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, failingStatusNames: Array.Empty<string>());
            Assert.IsNotNull(FindValidationTestCase(xmlDriftTrue, "drift").Element("failure"));

            // "drift"という名前自体を含む集合を渡しても、treatDriftAsFailure:falseならdriftはfailureを持たない。
            var xmlDriftFalse = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: false,
                failingStatusNames: new[] { "ConflictingAddress", "GroupNotFound", "drift" });
            Assert.IsNull(FindValidationTestCase(xmlDriftFalse, "drift").Element("failure"));
        }

        // 以下は「validation系testcaseにfailureが1つ以上ある」⇔「exit code 2」という不変条件が、
        // CheckCLI基準・Apply基準の両方で成り立つことを、テスト側で基準を再実装せず本番の関数
        // （AddressTellerReportBuilder.BuildCheckCliFailingStatusNames/BuildApplyFailingStatusNames、
        // DetermineExitCode/DetermineApplyExitCode）を直接呼んで検証する。

        /// <summary>xmlの中にfailure付きのtestcaseが1件でもあるかどうか。</summary>
        private static bool AnyTestCaseHasFailure(string xml) =>
            XDocument.Parse(xml).Root.Elements("testcase").Any(tc => tc.Element("failure") != null);

        private static IEnumerable<TestCaseData> InvariantIssueSets()
        {
            yield return new TestCaseData(new List<ValidationResult>())
                .SetName("{m}_Empty");
            yield return new TestCaseData(new List<ValidationResult>
            {
                new(null, ValidationStatus.Ok, "ok"),
            }).SetName("{m}_OkOnly");
            yield return new TestCaseData(new List<ValidationResult>
            {
                new(null, ValidationStatus.Skipped, "skipped"),
            }).SetName("{m}_SkippedOnly");
            yield return new TestCaseData(new List<ValidationResult>
            {
                new(null, ValidationStatus.ConflictingAddress, "conflict"),
            }).SetName("{m}_ConflictingAddress");
            yield return new TestCaseData(new List<ValidationResult>
            {
                new(null, ValidationStatus.DuplicateAddress, "notice", hasWritableDuplicate: false),
            }).SetName("{m}_DuplicateAddressNoticeOnly");
            yield return new TestCaseData(new List<ValidationResult>
            {
                new(null, ValidationStatus.DuplicateAddress, "writable", hasWritableDuplicate: true),
            }).SetName("{m}_DuplicateAddressWritable");
            yield return new TestCaseData(new List<ValidationResult>
            {
                new(null, ValidationStatus.Ok, "ok"),
                new(null, ValidationStatus.ConflictingAddress, "conflict"),
            }).SetName("{m}_MixedOkAndConflict");
        }

        [TestCaseSource(nameof(InvariantIssueSets))]
        public void ToJUnitXml_CheckCliBasis_FailurePresence_MatchesExitCodeTwo(List<ValidationResult> issues)
        {
            var report = ReportFromIssues(issues);
            var failingNames = AddressTellerReportBuilder.BuildCheckCliFailingStatusNames(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: true, failingNames);
            var dryRun = new DryRunResult(new SnapshotDiff(), issues);
            var exitCodeIsTwo = AddressTellerReportBuilder.DetermineExitCode(dryRun) == 2;

            Assert.AreEqual(exitCodeIsTwo, AnyTestCaseHasFailure(xml),
                "CheckCLI基準で「exit code 2」と「JUnitにfailureが1つ以上ある」は同値であるべき。");
        }

        [TestCaseSource(nameof(InvariantIssueSets))]
        public void ToJUnitXml_ApplyBasis_FailurePresence_MatchesExitCodeTwo(List<ValidationResult> issues)
        {
            var report = ReportFromIssues(issues);
            var failingNames = AddressTellerReportBuilder.BuildApplyFailingStatusNames(issues);

            var xml = AddressTellerReportWriter.ToJUnitXml(report, treatDriftAsFailure: false, failingNames);
            var exitCodeIsTwo = AddressTellerReportBuilder.DetermineApplyExitCode(issues) == 2;

            Assert.AreEqual(exitCodeIsTwo, AnyTestCaseHasFailure(xml),
                "Apply系基準で「exit code 2」と「JUnitにfailureが1つ以上ある」は同値であるべき。");
        }

        [Test]
        public void WriteToFile_Json_FailingStatusNames_DoesNotAffectOutput()
        {
            // JSONはfailureの概念を持たないため、failingStatusNamesの指定有無・中身に関わらず出力は同一。
            var report = ReportWithDriftAndIssues();
            var tempDir = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_" + Guid.NewGuid());
            var pathWithoutFilter = Path.Combine(tempDir, "without.json");
            var pathWithFilter = Path.Combine(tempDir, "with.json");

            try
            {
                Assert.IsTrue(AddressTellerReportWriter.WriteToFile(pathWithoutFilter, report, ReportFormat.Json));
                Assert.IsTrue(AddressTellerReportWriter.WriteToFile(pathWithFilter, report, ReportFormat.Json,
                    treatDriftAsFailure: true, failingStatusNames: new[] { "ConflictingAddress" }));

                var contentWithout = File.ReadAllText(pathWithoutFilter);
                var contentWith = File.ReadAllText(pathWithFilter);

                Assert.AreEqual(contentWithout, contentWith);
                Assert.AreEqual(AddressTellerReportWriter.ToJson(report), contentWithout);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void WriteToFile_Junit_TreatDriftAsFailureFalse_WritesWithoutDriftFailure()
        {
            var report = ReportWithDriftAndIssues();
            var tempDir = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_" + Guid.NewGuid());
            var path = Path.Combine(tempDir, "report.xml");

            try
            {
                var ok = AddressTellerReportWriter.WriteToFile(path, report, ReportFormat.Junit, treatDriftAsFailure: false);

                Assert.IsTrue(ok);
                var doc = XDocument.Parse(File.ReadAllText(path));
                var driftCase = doc.Root.Elements("testcase").First(tc => tc.Attribute("name").Value == "drift");
                Assert.IsNull(driftCase.Element("failure"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void ToJUnitXml_SpecialCharacters_AreEscapedAndRoundTrip()
        {
            var report = new AddressTellerReport();
            const string specialPath = "Assets/<weird> & \"quoted\" 'name'.prefab";
            const string specialMessage = "競合: <A> & <B> が \"重複\"";

            report.Issues.Add(new AddressTellerReportIssue
            {
                Path = specialPath,
                Status = "ConflictingAddress",
                Message = specialMessage,
            });

            var xml = AddressTellerReportWriter.ToJUnitXml(report);

            // 解析できること自体がエスケープ正当性の確認
            var doc = XDocument.Parse(xml);

            var testcase = doc.Root.Elements("testcase")
                .First(tc => tc.Attribute("name").Value == "ConflictingAddress");
            var failure = testcase.Element("failure");
            Assert.IsNotNull(failure);

            // 要素値に元のパス・メッセージが復元されること
            StringAssert.Contains(specialPath, failure.Value);
            StringAssert.Contains(specialMessage, failure.Value);
        }

        [Test]
        public void WriteToFile_Json_WritesFileSuccessfully()
        {
            var report = ReportWithDriftAndIssues();
            var tempDir = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_" + Guid.NewGuid());
            var path = Path.Combine(tempDir, "report.json");

            try
            {
                var ok = AddressTellerReportWriter.WriteToFile(path, report, ReportFormat.Json);

                Assert.IsTrue(ok);
                Assert.IsTrue(File.Exists(path));

                var restored = AddressTellerReport.FromJson(File.ReadAllText(path));
                Assert.AreEqual(report.Summary.ExitCode, restored.Summary.ExitCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void WriteToFile_Junit_WritesFileSuccessfully()
        {
            var report = ReportWithDriftAndIssues();
            var tempDir = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_" + Guid.NewGuid());
            var path = Path.Combine(tempDir, "report.xml");

            try
            {
                var ok = AddressTellerReportWriter.WriteToFile(path, report, ReportFormat.Junit);

                Assert.IsTrue(ok);
                Assert.IsTrue(File.Exists(path));

                // valid XML であること
                XDocument.Parse(File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void WriteToFile_UnknownFormat_ThrowsArgumentException()
        {
            var report = EmptyReport();
            var path = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_unknown.txt");

            // 定義域外の値（enum が将来拡張されず switch の default に落ちるケースの防御的分岐を検証する）。
            Assert.Throws<ArgumentException>(() =>
                AddressTellerReportWriter.WriteToFile(path, report, (ReportFormat)99));
        }

        [Test]
        public void WriteToFile_NullReport_ThrowsArgumentNullException()
        {
            var path = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_null.json");

            Assert.Throws<ArgumentNullException>(() =>
                AddressTellerReportWriter.WriteToFile(path, null, ReportFormat.Json));
        }

        [Test]
        public void WriteToFile_InvalidPath_ReturnsFalseAndLogsError()
        {
            // CLI の exit code 3（実行環境エラー）はレポート書き込み失敗時にも発生する。
            // 出力先ディレクトリ名が既存のファイルと衝突する（ディレクトリ作成不可な）パスで、
            // その経路（false 復帰 + エラーログ）を確認する。
            var report = EmptyReport();
            var blockingFile = Path.Combine(Path.GetTempPath(), "AddressTellerReportWriterTests_blocking_" + Guid.NewGuid());
            var path = Path.Combine(blockingFile, "report.json");

            try
            {
                File.WriteAllText(blockingFile, "blocking");

                LogAssert.Expect(LogType.Error, new Regex("Failed to write report"));
                var ok = AddressTellerReportWriter.WriteToFile(path, report, ReportFormat.Json);

                Assert.IsFalse(ok);
            }
            finally
            {
                if (File.Exists(blockingFile))
                    File.Delete(blockingFile);
            }
        }
    }
}

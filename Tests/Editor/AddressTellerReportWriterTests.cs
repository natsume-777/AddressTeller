using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Natsume777.AddressTeller.Editor.Tests
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
                var ok = AddressTellerReportWriter.WriteToFile(path, report, "json");

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
                var ok = AddressTellerReportWriter.WriteToFile(path, report, "junit");

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

            Assert.Throws<ArgumentException>(() =>
                AddressTellerReportWriter.WriteToFile(path, report, "yaml"));
        }
    }
}

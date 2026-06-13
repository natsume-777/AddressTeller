using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerReportBuilder.Build / DetermineExitCode の単体テスト。
    /// drift の Path 解決に AssetDatabase.GUIDToAssetPath を使うため、
    /// テスト対象アセットは一時フォルダ Assets/_AddressTellerReportBuilderTestTemp 配下に実体として作成する。
    /// </summary>
    public class AddressTellerReportBuilderTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerReportBuilderTestTemp";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerReportBuilderTestTemp");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRootFolder);
        }

        /// <summary>指定パスに最小限の Prefab アセットを作成し、その GUID を返す。</summary>
        private static string CreatePrefab(string path)
        {
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return AssetDatabase.AssetPathToGUID(path);
        }

        private static AssetContext Ctx(string guid, string path)
            => new(guid, path, typeof(UnityEngine.Object));

        [Test]
        public void NoDiffNoIssues_ExitCodeIsZero()
        {
            var diff = new SnapshotDiff();
            var issues = new List<ValidationResult>();
            var result = new DryRunResult(diff, issues);

            var report = AddressTellerReportBuilder.Build(result);

            Assert.AreEqual(0, report.Summary.ExitCode);
            Assert.AreEqual(0, AddressTellerReportBuilder.DetermineExitCode(result));
            Assert.AreEqual(0, report.Drift.Count);
            Assert.AreEqual(0, report.Issues.Count);
        }

        [Test]
        public void MixedDrift_ExitCodeIsOne_AndOrderedByPath()
        {
            // Path の Ordinal 順では "Added.prefab" < "Changed.prefab" < "Removed.prefab" になる想定。
            var addedGuid = CreatePrefab(TestRootFolder + "/Added.prefab");
            var changedGuid = CreatePrefab(TestRootFolder + "/Changed.prefab");
            var removedGuid = CreatePrefab(TestRootFolder + "/Removed.prefab");

            var diff = new SnapshotDiff();
            diff.Added.Add(new SnapshotEntry
            {
                Guid = addedGuid,
                Address = "Added",
                GroupName = "Default",
                Labels = new List<string> { "alpha" },
            });
            diff.Removed.Add(new SnapshotEntry
            {
                Guid = removedGuid,
                Address = "Removed",
                GroupName = "Default",
                Labels = new List<string>(),
            });
            diff.Changed.Add((
                new SnapshotEntry { Guid = changedGuid, Address = "OldAddress", GroupName = "Default", Labels = new List<string>() },
                new SnapshotEntry { Guid = changedGuid, Address = "NewAddress", GroupName = "Default", Labels = new List<string>() }
            ));

            var result = new DryRunResult(diff, new List<ValidationResult>());

            var report = AddressTellerReportBuilder.Build(result);

            Assert.AreEqual(1, report.Summary.ExitCode);
            Assert.AreEqual(1, AddressTellerReportBuilder.DetermineExitCode(result));
            Assert.AreEqual(1, report.Summary.Added);
            Assert.AreEqual(1, report.Summary.Removed);
            Assert.AreEqual(1, report.Summary.Changed);
            Assert.AreEqual(0, report.Summary.Issues);

            Assert.AreEqual(3, report.Drift.Count);
            CollectionAssert.AreEqual(
                new[] { TestRootFolder + "/Added.prefab", TestRootFolder + "/Changed.prefab", TestRootFolder + "/Removed.prefab" },
                new[] { report.Drift[0].Path, report.Drift[1].Path, report.Drift[2].Path });

            var added = report.Drift[0];
            Assert.AreEqual("Added", added.ChangeType);
            Assert.AreEqual("Added", added.After.Address);
            CollectionAssert.AreEqual(new[] { "alpha" }, added.After.Labels);

            var changed = report.Drift[1];
            Assert.AreEqual("Changed", changed.ChangeType);
            Assert.AreEqual("OldAddress", changed.Before.Address);
            Assert.AreEqual("NewAddress", changed.After.Address);

            var removed = report.Drift[2];
            Assert.AreEqual("Removed", removed.ChangeType);
            Assert.AreEqual("Removed", removed.Before.Address);
        }

        [Test]
        public void IssuesPresent_ExitCodeIsTwo_EvenWithoutDiff()
        {
            var diff = new SnapshotDiff();
            var issues = new List<ValidationResult>
            {
                new(Ctx("guid-b", "Assets/B.prefab"), ValidationStatus.ConflictingAddress, "conflict"),
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.GroupNotFound, "group not found"),
            };
            var result = new DryRunResult(diff, issues);

            var report = AddressTellerReportBuilder.Build(result);

            Assert.AreEqual(2, report.Summary.ExitCode);
            Assert.AreEqual(2, AddressTellerReportBuilder.DetermineExitCode(result));
            Assert.AreEqual(2, report.Summary.Issues);

            Assert.AreEqual(2, report.Issues.Count);
            // Path の Ordinal 順: "Assets/A.prefab" < "Assets/B.prefab"
            Assert.AreEqual("Assets/A.prefab", report.Issues[0].Path);
            Assert.AreEqual("GroupNotFound", report.Issues[0].Status);
            Assert.AreEqual("Assets/B.prefab", report.Issues[1].Path);
            Assert.AreEqual("ConflictingAddress", report.Issues[1].Status);
        }

        [Test]
        public void RemovedOnly_ExitCodeIsOne()
        {
            var removedGuid = CreatePrefab(TestRootFolder + "/RemovedOnly.prefab");

            var diff = new SnapshotDiff();
            diff.Removed.Add(new SnapshotEntry
            {
                Guid = removedGuid,
                Address = "RemovedOnly",
                GroupName = "Default",
                Labels = new List<string>(),
            });

            var result = new DryRunResult(diff, new List<ValidationResult>());

            Assert.AreEqual(1, AddressTellerReportBuilder.DetermineExitCode(result));
        }

        [Test]
        public void OkOrSkippedIssuesOnly_ExitCodeIsNotTwo()
        {
            // Issues に IsOk=true（Ok/Skipped）の ValidationResult のみが混入した場合、
            // exit code 2（Validation エラーあり）と判定してはならない。
            var diff = new SnapshotDiff();
            var issues = new List<ValidationResult>
            {
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.Ok, "ok"),
                new(Ctx("guid-b", "Assets/B.prefab"), ValidationStatus.Skipped, "skipped"),
            };
            var result = new DryRunResult(diff, issues);

            Assert.AreEqual(0, AddressTellerReportBuilder.DetermineExitCode(result));

            var report = AddressTellerReportBuilder.Build(result);
            Assert.AreEqual(0, report.Summary.ExitCode);
        }

        [Test]
        public void SamePathAndStatusIssues_OrderedByMessage()
        {
            // 同一Path・同一Statusのissueが複数ある場合、Messageでタイブレークして決定的に並ぶことを確認する。
            var diff = new SnapshotDiff();
            var issues = new List<ValidationResult>
            {
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.ConflictingAddress, "z-message"),
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.ConflictingAddress, "a-message"),
            };
            var result = new DryRunResult(diff, issues);

            var report = AddressTellerReportBuilder.Build(result);

            Assert.AreEqual(2, report.Issues.Count);
            Assert.AreEqual("a-message", report.Issues[0].Message);
            Assert.AreEqual("z-message", report.Issues[1].Message);
        }

        [Test]
        public void DetermineExitCode_WithExecutionIssues_NoDiffNoIssues_ExecutionErrorEscalatesToTwo()
        {
            // dry-run 時点では差分・問題なし（0判定）でも、Apply 実行後の issues にエラーがあれば 2 に昇格する。
            var diff = new SnapshotDiff();
            var dryRun = new DryRunResult(diff, new List<ValidationResult>());

            var executionIssues = new List<ValidationResult>
            {
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.ConflictingAddress, "conflict"),
            };

            Assert.AreEqual(2, AddressTellerReportBuilder.DetermineExitCode(dryRun, executionIssues));
        }

        [Test]
        public void DetermineExitCode_WithExecutionIssues_AllOk_DoesNotEscalate()
        {
            var diff = new SnapshotDiff();
            var dryRun = new DryRunResult(diff, new List<ValidationResult>());

            var executionIssues = new List<ValidationResult>
            {
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.Ok, "ok"),
            };

            Assert.AreEqual(0, AddressTellerReportBuilder.DetermineExitCode(dryRun, executionIssues));
        }

        [Test]
        public void DetermineExitCode_WithExecutionIssues_DryRunAlreadyTwo_StaysTwo()
        {
            var diff = new SnapshotDiff();
            var dryRunIssues = new List<ValidationResult>
            {
                new(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.ConflictingAddress, "conflict"),
            };
            var dryRun = new DryRunResult(diff, dryRunIssues);

            Assert.AreEqual(2, AddressTellerReportBuilder.DetermineExitCode(dryRun, new List<ValidationResult>()));
        }

        [Test]
        public void ToJson_Succeeds()
        {
            var addedGuid = CreatePrefab(TestRootFolder + "/JsonTarget.prefab");

            var diff = new SnapshotDiff();
            diff.Added.Add(new SnapshotEntry
            {
                Guid = addedGuid,
                Address = "JsonTarget",
                GroupName = "Default",
                Labels = new List<string> { "alpha", "beta" },
            });

            var issues = new List<ValidationResult>
            {
                new(Ctx("guid-c", "Assets/C.prefab"), ValidationStatus.RuleError, "boom"),
            };

            var report = AddressTellerReportBuilder.Build(new DryRunResult(diff, issues));

            var json = report.ToJson();

            Assert.IsFalse(string.IsNullOrEmpty(json));
            StringAssert.Contains("JsonTarget", json);
            StringAssert.Contains("RuleError", json);

            var restored = AddressTellerReport.FromJson(json);
            Assert.AreEqual(report.Summary.ExitCode, restored.Summary.ExitCode);
            Assert.AreEqual(1, restored.Drift.Count);
            Assert.AreEqual(1, restored.Issues.Count);
        }
    }
}

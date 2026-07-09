using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// <see cref="AddressTellerResultWindowRows"/> の行データ変換ロジックの EditMode テスト。
    /// AssetDatabase.GUIDToAssetPath を経由するため、一時アセットを作成して実 GUID を取得する。
    /// </summary>
    public class AddressTellerResultWindowRowsTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string AssetAPath = TestRootFolder + "/AssetA.prefab";
        private const string AssetBPath = TestRootFolder + "/AssetB.prefab";
        private const string AssetCPath = TestRootFolder + "/AssetC.prefab";

        private const string UnresolvableGuid = "00000000000000000000000000000000";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");

            CreatePrefab(AssetAPath);
            CreatePrefab(AssetBPath);
            CreatePrefab(AssetCPath);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRootFolder);
        }

        private static void CreatePrefab(string path)
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
        }

        private static string Guid(string path) => AssetDatabase.AssetPathToGUID(path);

        private static SnapshotEntry Entry(string guid, string address, string groupName, params string[] labels) =>
            new SnapshotEntry { Guid = guid, Address = address, GroupName = groupName, Labels = labels.ToList() };

        [Test]
        public void BuildDiffRows_Added_HasAfterValuesOnly()
        {
            var diff = new SnapshotDiff();
            diff.Added.Add(Entry(Guid(AssetAPath), "AssetA", "GroupA", "alpha"));

            var rows = AddressTellerResultWindowRows.BuildDiffRows(diff);

            Assert.AreEqual(1, rows.Count);
            var row = rows[0];
            Assert.AreEqual(DiffRowKind.Added, row.Kind);
            Assert.AreEqual(AssetAPath, row.AssetPath);
            Assert.AreEqual(string.Empty, row.BeforeAddress);
            Assert.AreEqual("AssetA", row.AfterAddress);
            Assert.AreEqual(string.Empty, row.BeforeGroup);
            Assert.AreEqual("GroupA", row.AfterGroup);
            Assert.AreEqual("alpha", row.LabelsSummary);
        }

        [Test]
        public void BuildDiffRows_Removed_HasBeforeValuesOnly()
        {
            var diff = new SnapshotDiff();
            diff.Removed.Add(Entry(Guid(AssetAPath), "OldAddress", "OldGroup", "old"));

            var rows = AddressTellerResultWindowRows.BuildDiffRows(diff);

            Assert.AreEqual(1, rows.Count);
            var row = rows[0];
            Assert.AreEqual(DiffRowKind.Removed, row.Kind);
            Assert.AreEqual(AssetAPath, row.AssetPath);
            Assert.AreEqual("OldAddress", row.BeforeAddress);
            Assert.AreEqual(string.Empty, row.AfterAddress);
            Assert.AreEqual("OldGroup", row.BeforeGroup);
            Assert.AreEqual(string.Empty, row.AfterGroup);
            Assert.AreEqual("old", row.LabelsSummary);
        }

        [Test]
        public void BuildDiffRows_Changed_HasBeforeAndAfterValues()
        {
            var diff = new SnapshotDiff();
            var before = Entry(Guid(AssetAPath), "OldAddress", "OldGroup", "old");
            var after = Entry(Guid(AssetAPath), "NewAddress", "NewGroup", "new");
            diff.Changed.Add((before, after));

            var rows = AddressTellerResultWindowRows.BuildDiffRows(diff);

            Assert.AreEqual(1, rows.Count);
            var row = rows[0];
            Assert.AreEqual(DiffRowKind.Changed, row.Kind);
            Assert.AreEqual(AssetAPath, row.AssetPath);
            Assert.AreEqual("OldAddress", row.BeforeAddress);
            Assert.AreEqual("NewAddress", row.AfterAddress);
            Assert.AreEqual("OldGroup", row.BeforeGroup);
            Assert.AreEqual("NewGroup", row.AfterGroup);
            Assert.AreEqual("new", row.LabelsSummary);
        }

        [Test]
        public void BuildDiffRows_SortsByKindThenAssetPath_Deterministically()
        {
            var diff = new SnapshotDiff();
            // 意図的に Kind と AssetPath の順序が逆になるように追加する。
            diff.Changed.Add((Entry(Guid(AssetCPath), "a", "g", "l"), Entry(Guid(AssetCPath), "b", "g", "l")));
            diff.Added.Add(Entry(Guid(AssetBPath), "b", "g", "l"));
            diff.Added.Add(Entry(Guid(AssetAPath), "a", "g", "l"));
            diff.Removed.Add(Entry(Guid(AssetCPath), "x", "g", "l"));

            var rows1 = AddressTellerResultWindowRows.BuildDiffRows(diff);
            var rows2 = AddressTellerResultWindowRows.BuildDiffRows(diff);

            // Added(AssetA) -> Added(AssetB) -> Removed(AssetC) -> Changed(AssetC)
            Assert.AreEqual(4, rows1.Count);
            Assert.AreEqual(DiffRowKind.Added, rows1[0].Kind);
            Assert.AreEqual(AssetAPath, rows1[0].AssetPath);
            Assert.AreEqual(DiffRowKind.Added, rows1[1].Kind);
            Assert.AreEqual(AssetBPath, rows1[1].AssetPath);
            Assert.AreEqual(DiffRowKind.Removed, rows1[2].Kind);
            Assert.AreEqual(AssetCPath, rows1[2].AssetPath);
            Assert.AreEqual(DiffRowKind.Changed, rows1[3].Kind);
            Assert.AreEqual(AssetCPath, rows1[3].AssetPath);

            // 同じ入力から複数回呼んでも同じ順序になる。
            for (var i = 0; i < rows1.Count; i++)
            {
                Assert.AreEqual(rows1[i].Kind, rows2[i].Kind);
                Assert.AreEqual(rows1[i].AssetPath, rows2[i].AssetPath);
            }
        }

        [Test]
        public void BuildDiffRows_UnresolvableGuid_FallsBackToGuidAsAssetPath()
        {
            var diff = new SnapshotDiff();
            diff.Added.Add(Entry(UnresolvableGuid, "Address", "Group", "label"));

            var rows = AddressTellerResultWindowRows.BuildDiffRows(diff);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(UnresolvableGuid, rows[0].AssetPath);
        }

        [Test]
        public void BuildIssueRows_ConvertsStatusPathGuidMessage()
        {
            var ctx = new AssetContext(Guid(AssetAPath), AssetAPath, typeof(GameObject));
            var issues = new List<ValidationResult>
            {
                new ValidationResult(ctx, ValidationStatus.GroupNotFound, "Group 'Foo' not found."),
            };

            var rows = AddressTellerResultWindowRows.BuildIssueRows(issues);

            Assert.AreEqual(1, rows.Count);
            var row = rows[0];
            Assert.AreEqual(ValidationStatus.GroupNotFound, row.Status);
            Assert.AreEqual(AssetAPath, row.AssetPath);
            Assert.AreEqual(Guid(AssetAPath), row.Guid);
            Assert.AreEqual("Group 'Foo' not found.", row.Message);
        }

        [Test]
        public void BuildIssueRows_SortsByStatusThenAssetPath_Deterministically()
        {
            var ctxA = new AssetContext(Guid(AssetAPath), AssetAPath, typeof(GameObject));
            var ctxB = new AssetContext(Guid(AssetBPath), AssetBPath, typeof(GameObject));
            var ctxC = new AssetContext(Guid(AssetCPath), AssetCPath, typeof(GameObject));

            // ValidationStatus: Ok=0, Skipped=1, ConflictingAddress=2, GroupNotFound=3, InvalidAddress=4, RuleError=5
            var issues = new List<ValidationResult>
            {
                new ValidationResult(ctxC, ValidationStatus.GroupNotFound, "c"),
                new ValidationResult(ctxB, ValidationStatus.ConflictingAddress, "b"),
                new ValidationResult(ctxA, ValidationStatus.ConflictingAddress, "a"),
            };

            var rows1 = AddressTellerResultWindowRows.BuildIssueRows(issues);
            var rows2 = AddressTellerResultWindowRows.BuildIssueRows(issues);

            Assert.AreEqual(3, rows1.Count);
            Assert.AreEqual(ValidationStatus.ConflictingAddress, rows1[0].Status);
            Assert.AreEqual(AssetAPath, rows1[0].AssetPath);
            Assert.AreEqual(ValidationStatus.ConflictingAddress, rows1[1].Status);
            Assert.AreEqual(AssetBPath, rows1[1].AssetPath);
            Assert.AreEqual(ValidationStatus.GroupNotFound, rows1[2].Status);
            Assert.AreEqual(AssetCPath, rows1[2].AssetPath);

            for (var i = 0; i < rows1.Count; i++)
            {
                Assert.AreEqual(rows1[i].Status, rows2[i].Status);
                Assert.AreEqual(rows1[i].AssetPath, rows2[i].AssetPath);
            }
        }

        [Test]
        public void BuildDiffRows_EmptyDiff_ReturnsEmptyList()
        {
            var diff = new SnapshotDiff();

            var rows = AddressTellerResultWindowRows.BuildDiffRows(diff);

            Assert.AreEqual(0, rows.Count);
        }

        [Test]
        public void BuildDiffRows_NullDiff_ReturnsEmptyList()
        {
            var rows = AddressTellerResultWindowRows.BuildDiffRows(null);

            Assert.AreEqual(0, rows.Count);
        }

        [Test]
        public void BuildDiffRows_NullLabels_SummarizesAsEmptyString()
        {
            var diff = new SnapshotDiff();
            diff.Added.Add(new SnapshotEntry { Guid = Guid(AssetAPath), Address = "AssetA", GroupName = "GroupA", Labels = null });

            var rows = AddressTellerResultWindowRows.BuildDiffRows(diff);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(string.Empty, rows[0].LabelsSummary);
        }

        [Test]
        public void BuildIssueRows_EmptyList_ReturnsEmptyList()
        {
            var rows = AddressTellerResultWindowRows.BuildIssueRows(new List<ValidationResult>());

            Assert.AreEqual(0, rows.Count);
        }

        [Test]
        public void BuildIssueRows_ConflictingAddress_CarriesConflictingCandidates()
        {
            var ctx = new AssetContext(Guid(AssetAPath), AssetAPath, typeof(GameObject));
            var candidates = new List<AddressCandidate>
            {
                new AddressCandidate("GroupA", "AddressA", "RuleA", "descA", 0),
                new AddressCandidate("GroupB", "AddressB", "RuleB", "descB", 1),
            };
            var issues = new List<ValidationResult>
            {
                new ValidationResult(ctx, ValidationStatus.ConflictingAddress, "conflict", candidates),
            };

            var rows = AddressTellerResultWindowRows.BuildIssueRows(issues);

            Assert.AreEqual(1, rows.Count);
            var row = rows[0];
            Assert.AreEqual(ValidationStatus.ConflictingAddress, row.Status);
            Assert.AreEqual(2, row.ConflictingCandidates.Count);
            Assert.AreEqual("AddressA", row.ConflictingCandidates[0].Address);
            Assert.AreEqual("AddressB", row.ConflictingCandidates[1].Address);
        }

        [Test]
        public void BuildIssueRows_ConflictingAddress_MultiLineMessage_TruncatesToFirstLine()
        {
            var ctx = new AssetContext(Guid(AssetAPath), AssetAPath, typeof(GameObject));
            var candidates = new List<AddressCandidate>
            {
                new AddressCandidate("GroupA", "AddressA", "RuleA", "descA", 0),
                new AddressCandidate("GroupB", "AddressB", "RuleB", "descB", 1),
            };
            var message = $"Address conflict for '{AssetAPath}':\n  RuleA [GroupA] -> \"AddressA\"\n  RuleB [GroupB] -> \"AddressB\"";
            var issues = new List<ValidationResult>
            {
                new ValidationResult(ctx, ValidationStatus.ConflictingAddress, message, candidates),
            };

            var rows = AddressTellerResultWindowRows.BuildIssueRows(issues);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual($"Address conflict for '{AssetAPath}':", rows[0].Message);
        }

        [Test]
        public void BuildIssueRows_NullContext_DoesNotThrow_FallsBackToEmptyStrings()
        {
            // Context は設計上 nullable。RuleError 等、AssetContext を確定できない場合に null になり得る。
            var issues = new List<ValidationResult>
            {
                new ValidationResult(null, ValidationStatus.RuleError, "rule threw"),
            };

            List<IssueRow> rows = null;
            Assert.DoesNotThrow(() => rows = AddressTellerResultWindowRows.BuildIssueRows(issues));

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(string.Empty, rows[0].AssetPath);
            Assert.AreEqual(string.Empty, rows[0].Guid);
        }

        [Test]
        public void BuildIssueRows_NonConflictingIssue_HasEmptyConflictingCandidates()
        {
            var ctx = new AssetContext(Guid(AssetAPath), AssetAPath, typeof(GameObject));
            var issues = new List<ValidationResult>
            {
                new ValidationResult(ctx, ValidationStatus.GroupNotFound, "Group 'Foo' not found."),
            };

            var rows = AddressTellerResultWindowRows.BuildIssueRows(issues);

            Assert.AreEqual(1, rows.Count);
            Assert.IsNotNull(rows[0].ConflictingCandidates);
            Assert.AreEqual(0, rows[0].ConflictingCandidates.Count);
        }
    }
}

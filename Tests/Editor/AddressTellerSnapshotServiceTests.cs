using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerSnapshotService の Capture/Restore/Diff を、
    /// ディスクに保存しない一時的な AddressableAssetSettings 上で検証する。
    /// </summary>
    public class AddressTellerSnapshotServiceTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _groupA;
        private AddressableAssetGroup _groupB;

        [SetUp]
        public void SetUp()
        {
            // AddressTellerTestSettingsFactory 経由で生成する（非永続 settings でも ConfigFolder を参照できるようにする）。
            // RuleEvaluationPipeline.BuildSetup 等 settings.ConfigFolder に依存するテスト対象コードを呼ぶテスト
            // （例: UndoLastApply_CountConsistency_ManagedGroupFilteredAndUnmanagedKept）が
            // 「AddressableAssetSettings is not persisted」例外を出さないようにするための対応。
            _settings = AddressTellerTestSettingsFactory.CreateInMemory("Assets/_AddressTellerTestTemp", "AddressTellerSnapshotTestSettings");
            _groupA = _settings.CreateGroup("GroupA", false, false, false, null);
            _groupB = _settings.CreateGroup("GroupB", false, false, false, null);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_groupA, true);
            UnityEngine.Object.DestroyImmediate(_groupB, true);
            UnityEngine.Object.DestroyImmediate(_settings, true);
        }

        private static SnapshotEntry Entry(string guid, string address, string groupName, params string[] labels) =>
            new SnapshotEntry { Guid = guid, Address = address, GroupName = groupName, Labels = labels.ToList() };

        [Test]
        public void Capture_CollectsAddressGroupAndLabels()
        {
            var entry = _settings.CreateOrMoveEntry("guid1", _groupA);
            entry.SetAddress("Foo");
            entry.SetLabel("preload", true);

            var snapshot = AddressTellerSnapshotService.Capture(_settings);

            var captured = snapshot.Entries.Single(e => e.Guid == "guid1");
            Assert.AreEqual("Foo", captured.Address);
            Assert.AreEqual("GroupA", captured.GroupName);
            CollectionAssert.Contains(captured.Labels, "preload");
        }

        [Test]
        public void Capture_WithComment_SetsMetadata()
        {
            var entry = _settings.CreateOrMoveEntry("guid1", _groupA);
            entry.SetAddress("Foo");

            var before = DateTime.UtcNow;
            var snapshot = AddressTellerSnapshotService.Capture(_settings, "test");
            var after = DateTime.UtcNow;

            Assert.AreEqual("test", snapshot.Comment);
            Assert.AreEqual(1, snapshot.SchemaVersion);
            Assert.AreEqual(UnityEngine.Application.unityVersion, snapshot.UnityVersion);

            Assert.IsFalse(string.IsNullOrEmpty(snapshot.CapturedAtIso));
            var capturedAt = DateTime.Parse(snapshot.CapturedAtIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.GreaterOrEqual(capturedAt, before.AddSeconds(-1));
            Assert.LessOrEqual(capturedAt, after.AddSeconds(1));
        }

        [Test]
        public void Capture_WithoutComment_DefaultsToEmptyComment()
        {
            var snapshot = AddressTellerSnapshotService.Capture(_settings);

            Assert.AreEqual("", snapshot.Comment);
            Assert.AreEqual(1, snapshot.SchemaVersion);
        }

        [Test]
        public void LoadFromFile_MissingFile_ReturnsError()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_Missing_{Guid.NewGuid():N}.json");

            var result = AddressTellerSnapshotService.LoadFromFile(path, out var snapshot, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(snapshot);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void LoadFromFile_BrokenJson_ReturnsError()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_Broken_{Guid.NewGuid():N}.json");
            File.WriteAllText(path, "{ this is not valid json");

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var snapshot, out var error);

                Assert.IsFalse(result);
                Assert.IsNull(snapshot);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadFromFile_FutureSchemaVersion_ReturnsError()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_Future_{Guid.NewGuid():N}.json");
            var snapshot = AddressTellerSnapshotService.Capture(_settings, "future");
            snapshot.SchemaVersion = AddressTellerSnapshotService.CurrentSchemaVersion + 1;
            File.WriteAllText(path, snapshot.ToJson());

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var loaded, out var error);

                Assert.IsFalse(result);
                Assert.IsNull(loaded);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadFromFile_EntryWithEmptyGuid_ReturnsError()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_EmptyGuid_{Guid.NewGuid():N}.json");
            var snapshot = AddressTellerSnapshotService.Capture(_settings, "empty-guid");
            snapshot.Entries.Add(new SnapshotEntry { Guid = "", Address = "Foo", GroupName = "Default Local Group" });
            File.WriteAllText(path, snapshot.ToJson());

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var loaded, out var error);

                Assert.IsFalse(result);
                Assert.IsNull(loaded);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadFromFile_DuplicateGuid_ReturnsError()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_DupGuid_{Guid.NewGuid():N}.json");
            var snapshot = AddressTellerSnapshotService.Capture(_settings, "dup-guid");
            var duplicate = new SnapshotEntry { Guid = "duplicate-guid", Address = "Foo", GroupName = "Default Local Group" };
            snapshot.Entries.Add(duplicate);
            snapshot.Entries.Add(duplicate);
            File.WriteAllText(path, snapshot.ToJson());

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var loaded, out var error);

                Assert.IsFalse(result);
                Assert.IsNull(loaded);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadFromFile_EntryWithEmptyGroupName_ReturnsError()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_EmptyGroupName_{Guid.NewGuid():N}.json");
            var snapshot = AddressTellerSnapshotService.Capture(_settings, "empty-groupname");
            snapshot.Entries.Add(new SnapshotEntry { Guid = "guid-empty-group", Address = "Foo", GroupName = "" });
            File.WriteAllText(path, snapshot.ToJson());

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var loaded, out var error);

                Assert.IsFalse(result);
                Assert.IsNull(loaded);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadFromFile_EntryWithNullLabelsInJson_LoadsSuccessfullyWithEmptyLabels()
        {
            // JsonUtility は List<T> フィールドに対して JSON 側で "Labels": null を明示的に指定しても、
            // その値を反映せずフィールド初期化子（= new()）の空リストを保持する（実測で確認済み。
            // 参照型フィールドへの null 反映自体をサポートしていないための挙動）。
            // そのため LoadFromFile 内の entry.Labels == null チェック（Restore() 側の同種チェックと対称に
            // 維持している防御的コード）はこの経路では発火せず、壊れた JSON でも空リストとしてロードに成功する。
            // 手動編集された壊れたスナップショット JSON を模して、直接テキストを組み立てる。
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_NullLabels_{Guid.NewGuid():N}.json");
            var json = "{"
                + "\"Entries\":[{\"Guid\":\"guid-null-labels\",\"Address\":\"Foo\",\"GroupName\":\"GroupA\",\"Labels\":null}],"
                + "\"CapturedAtIso\":\"\",\"Comment\":\"\",\"UnityVersion\":\"\",\"PackageVersion\":\"\",\"SchemaVersion\":1"
                + "}";
            File.WriteAllText(path, json);

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var loaded, out var error);

                Assert.IsTrue(result);
                Assert.IsNull(error);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(1, loaded.Entries.Count);
                Assert.IsNotNull(loaded.Entries[0].Labels);
                Assert.AreEqual(0, loaded.Entries[0].Labels.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadFromFile_ValidJson_ReturnsSnapshot()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressTellerSnapshotTests_Valid_{Guid.NewGuid():N}.json");
            var snapshot = AddressTellerSnapshotService.Capture(_settings, "valid");
            File.WriteAllText(path, snapshot.ToJson());

            try
            {
                var result = AddressTellerSnapshotService.LoadFromFile(path, out var loaded, out var error);

                Assert.IsTrue(result);
                Assert.IsNull(error);
                Assert.AreEqual("valid", loaded.Comment);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Restore_Additive_KeepsLabelsAddedAfterSnapshot()
        {
            var entry = _settings.CreateOrMoveEntry("guid1", _groupA);
            entry.SetAddress("Foo");
            entry.SetLabel("old", true);

            var snapshot = AddressTellerSnapshotService.Capture(_settings);

            entry.SetLabel("new", true);

            AddressTellerSnapshotService.Restore(snapshot, _settings, SnapshotRestoreMode.Additive);

            var restored = _settings.FindAssetEntry("guid1");
            CollectionAssert.Contains(restored.labels, "old");
            CollectionAssert.Contains(restored.labels, "new");
        }

        [Test]
        public void Restore_Exact_RemovesLabelsAddedAfterSnapshot()
        {
            var entry = _settings.CreateOrMoveEntry("guid1", _groupA);
            entry.SetAddress("Foo");
            entry.SetLabel("old", true);

            var snapshot = AddressTellerSnapshotService.Capture(_settings);

            entry.SetLabel("new", true);

            AddressTellerSnapshotService.Restore(snapshot, _settings, SnapshotRestoreMode.Exact);

            var restored = _settings.FindAssetEntry("guid1");
            CollectionAssert.Contains(restored.labels, "old");
            CollectionAssert.DoesNotContain(restored.labels, "new");
        }

        [Test]
        public void Restore_ResolvesMultipleGroupsByName()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(Entry("guid-a", "FooA", "GroupA"));
            snapshot.Entries.Add(Entry("guid-b", "FooB", "GroupB"));

            var issues = AddressTellerSnapshotService.Restore(snapshot, _settings);

            Assert.AreEqual(0, issues.Count);
            Assert.AreEqual("GroupA", _settings.FindAssetEntry("guid-a").parentGroup.Name);
            Assert.AreEqual("GroupB", _settings.FindAssetEntry("guid-b").parentGroup.Name);
        }

        [Test]
        public void Restore_GroupNotFound_ReturnsIssueAndSkipsEntry()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(Entry("guid-x", "Foo", "Missing"));

            var issues = AddressTellerSnapshotService.Restore(snapshot, _settings);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("Missing", issues[0]);
            Assert.IsNull(_settings.FindAssetEntry("guid-x"));
        }

        [Test]
        public void Restore_DuplicateGroupName_ReportsIssueAndUsesFirstGroup_WithoutThrowing()
        {
            // Addressables はグループ名の一意性を保証しないため、settings.groups.ToDictionary(g => g.Name, ...)
            // は重複時に例外を投げてしまう。ここでは意図的に GroupA と同名のグループを作り、
            // Restore が例外を投げず issues に報告した上で処理を継続することを検証する。
            var duplicateGroup = _settings.CreateGroup("GroupA_TemporaryName", false, false, false, null);
            try
            {
                duplicateGroup.Name = "GroupA";

                var snapshot = new AddressTellerSnapshot();
                snapshot.Entries.Add(Entry("guid1", "Foo", "GroupA"));

                IReadOnlyList<string> issues = null;
                Assert.DoesNotThrow(() => issues = AddressTellerSnapshotService.Restore(snapshot, _settings));

                Assert.IsTrue(issues.Any(i => i.Contains("GroupA")), "重複グループ名を検出した場合、issues に報告されるべき。");
                Assert.IsNotNull(_settings.FindAssetEntry("guid1"), "重複があっても、いずれかの GroupA へ復元自体は継続されるべき。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(duplicateGroup, true);
            }
        }

        [Test]
        public void Restore_DuplicateGroupNameNotUsedBySnapshot_DoesNotReportIssue()
        {
            // GroupB と同名の重複グループが settings に存在していても、スナップショットが GroupB を
            // 一切参照していない場合、無関係な重複についてまで issues に報告してはいけない
            // （ノイズ防止。Restore_DuplicateGroupName_ReportsIssueAndUsesFirstGroup_WithoutThrowing の対比）。
            var duplicateGroup = _settings.CreateGroup("GroupB_TemporaryName", false, false, false, null);
            try
            {
                duplicateGroup.Name = "GroupB";

                var snapshot = new AddressTellerSnapshot();
                snapshot.Entries.Add(Entry("guid1", "Foo", "GroupA"));

                var issues = AddressTellerSnapshotService.Restore(snapshot, _settings);

                Assert.AreEqual(0, issues.Count, "スナップショットが参照していないグループの重複は報告してはいけない。");
                Assert.IsNotNull(_settings.FindAssetEntry("guid1"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(duplicateGroup, true);
            }
        }

        [Test]
        public void Restore_NullEntry_ReportsIssueAndContinuesRestoringOtherEntries()
        {
            // JsonUtility はデフォルトコンストラクタ・フィールド初期化子を経由せずにオブジェクトを生成するため、
            // 手動編集された/壊れたスナップショット JSON からは entry 自体が null のまま List に混入しうる。
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(null);
            snapshot.Entries.Add(Entry("guid-ok", "Bar", "GroupA"));

            IReadOnlyList<string> issues = null;
            Assert.DoesNotThrow(() => issues = AddressTellerSnapshotService.Restore(snapshot, _settings));

            Assert.AreEqual(1, issues.Count);
            var restored = _settings.FindAssetEntry("guid-ok");
            Assert.IsNotNull(restored);
            Assert.AreEqual("Bar", restored.address);
        }

        [Test]
        public void Restore_NullGroupName_ReportsIssueAndContinuesRestoringOtherEntries()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(new SnapshotEntry { Guid = "guid-no-group", Address = "Foo", GroupName = null, Labels = new List<string>() });
            snapshot.Entries.Add(Entry("guid-ok", "Bar", "GroupA"));

            var issues = AddressTellerSnapshotService.Restore(snapshot, _settings);

            Assert.AreEqual(1, issues.Count);
            Assert.IsNull(_settings.FindAssetEntry("guid-no-group"));
            Assert.IsNotNull(_settings.FindAssetEntry("guid-ok"));
        }

        [Test]
        public void Restore_NullLabels_ReportsIssueAndContinuesRestoringOtherEntries()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(new SnapshotEntry { Guid = "guid-no-labels", Address = "Foo", GroupName = "GroupA", Labels = null });
            snapshot.Entries.Add(Entry("guid-ok", "Bar", "GroupA"));

            var issues = AddressTellerSnapshotService.Restore(snapshot, _settings);

            Assert.AreEqual(1, issues.Count);
            Assert.IsNull(_settings.FindAssetEntry("guid-no-labels"));
            Assert.IsNotNull(_settings.FindAssetEntry("guid-ok"));
        }

        [Test]
        public void Restore_CreateOrMoveEntryReturnsNull_ReportsIssueAndContinuesRestoringOtherEntries()
        {
            // AddressableAssetSettings.CreateOrMoveEntry は guid が空文字の場合に null を返す
            // （targetParent == null || string.IsNullOrEmpty(guid) の早期リターン）。
            // LoadFromFile 経由の正規スナップショットではここまで空 guid のエントリが混入することはないが、
            // Restore/RestoreExactWithRemoval は任意の AddressTellerSnapshot を受け取れる公開 API のため、
            // CreateOrMoveEntry が null を返すケース自体をここで直接再現し、NRE でループが中断しないことを検証する。
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(Entry("", "Foo", "GroupA"));
            snapshot.Entries.Add(Entry("guid-ok", "Bar", "GroupA"));

            var issues = AddressTellerSnapshotService.Restore(snapshot, _settings);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("Failed to create/move entry", issues[0]);
            // NRE でループが中断されず、後続のエントリは正常に復元されるべき。
            var restored = _settings.FindAssetEntry("guid-ok");
            Assert.IsNotNull(restored);
            Assert.AreEqual("Bar", restored.address);
        }

        [Test]
        public void Restore_Exact_DoesNotRemoveEntriesNotInSnapshot()
        {
            // 汎用 Restore（Exact モードでもラベルの完全一致のみを行い、エントリ削除は行わない）の固定回帰テスト。
            // エントリ削除は Undo Last Apply 専用の RestoreExactWithRemoval にのみ許可する設計。
            _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");

            var snapshot = new AddressTellerSnapshot();

            AddressTellerSnapshotService.Restore(snapshot, _settings, SnapshotRestoreMode.Exact);

            Assert.IsNotNull(_settings.FindAssetEntry("guid1"), "Restore(Exact) はエントリを削除してはいけない。");
        }

        [Test]
        public void RestoreExactWithRemoval_RemovesEntriesInRemovalList()
        {
            _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");

            var snapshot = new AddressTellerSnapshot();

            AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, new[] { "guid1" });

            Assert.IsNull(_settings.FindAssetEntry("guid1"));
        }

        [Test]
        public void RestoreExactWithRemoval_GuidNotInRemovalList_EntryIsKept()
        {
            _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");

            var snapshot = new AddressTellerSnapshot();

            AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, Array.Empty<string>());

            Assert.IsNotNull(_settings.FindAssetEntry("guid1"), "削除対象 GUID に含まれないエントリは保持されるべき。");
        }

        [Test]
        public void RestoreExactWithRemoval_CreateOrMoveEntryReturnsNull_DoesNotThrowAndReportsIssue()
        {
            // RestoreExactWithRemoval は内部で Restore を呼ぶため、CreateOrMoveEntry が null を返すケースの
            // NRE 耐性はここでも同様に効くべき（Restore 側のテストと対で確認する回帰テスト）。
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(Entry("", "Foo", "GroupA"));

            IReadOnlyList<string> issues = null;
            Assert.DoesNotThrow(() =>
                issues = AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, Array.Empty<string>()));

            Assert.AreEqual(1, issues.Count);
        }

        [Test]
        public void RestoreExactWithRemoval_ReaddsEntriesFromSnapshot()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(Entry("guid2", "Bar", "GroupB"));

            AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, Array.Empty<string>());

            var restored = _settings.FindAssetEntry("guid2");
            Assert.IsNotNull(restored);
            Assert.AreEqual("Bar", restored.address);
            Assert.AreEqual("GroupB", restored.parentGroup.Name);
        }

        [Test]
        public void RestoreExactWithRemoval_StripsLabelsNotInSnapshot()
        {
            var entry = _settings.CreateOrMoveEntry("guid1", _groupA);
            entry.SetAddress("Foo");
            entry.SetLabel("old", true);

            var snapshot = AddressTellerSnapshotService.Capture(_settings);

            entry.SetLabel("new", true);

            AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, Array.Empty<string>());

            var restored = _settings.FindAssetEntry("guid1");
            CollectionAssert.Contains(restored.labels, "old");
            CollectionAssert.DoesNotContain(restored.labels, "new");
        }

        [Test]
        public void RestoreExactWithRemoval_IgnoresCleanupStaleEntriesFlag()
        {
            // Undo Last Apply の削除は AddressTellerSettings.CleanupStaleEntries に縛られない設計。
            // 自動クリーンアップが無効なユーザーでも、明示的な Undo 操作では所有権判定のみを尊重して削除する。
            var original = AddressTellerSettings.CleanupStaleEntries;
            AddressTellerSettings.CleanupStaleEntries = false;
            try
            {
                _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");
                var snapshot = new AddressTellerSnapshot();

                AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, new[] { "guid1" });

                Assert.IsNull(_settings.FindAssetEntry("guid1"), "CleanupStaleEntries=false でも Undo は削除を行うべき。");
            }
            finally
            {
                AddressTellerSettings.CleanupStaleEntries = original;
            }
        }

        /// <summary>GroupA のみを Group() 対象とするスタブルール。managedGroups に GroupA だけを含めるために使う。</summary>
        private sealed class ManagedGroupAOnlyRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.Group("GroupA")
                    .Where(ctx => false)
                    .Address(ctx => ctx.FileNameWithoutExtension);
            }
        }

        [Test]
        public void UndoLastApply_CountConsistency_ManagedGroupFilteredAndUnmanagedKept()
        {
            // AddressTellerSnapshotMenu.UndoLastApply と同じ組み立て
            // （Diff.Removed を managedGroups で仕分けてから RestoreExactWithRemoval に渡す）を、
            // メニュー層（EditorUtility.DisplayDialog 依存）を経由せずサービス層で再現して検証する。
            _settings.CreateOrMoveEntry("guid-managed", _groupA).SetAddress("Managed");
            _settings.CreateOrMoveEntry("guid-unmanaged", _groupB).SetAddress("Unmanaged");

            var current = AddressTellerSnapshotService.Capture(_settings);
            var snapshot = new AddressTellerSnapshot(); // Apply 前 = 両エントリとも存在しない状態

            var diff = AddressTellerSnapshotService.Diff(current, snapshot);
            Assert.AreEqual(2, diff.Removed.Count);

            var managedGroups = RuleEvaluationPipeline.BuildSetup(_settings, new AddressRuleBase[] { new ManagedGroupAOnlyRule() }).ManagedGroups;
            var removable = diff.Removed.Where(e => managedGroups.Contains(e.GroupName)).ToList();
            var keptCount = diff.Removed.Count - removable.Count;

            Assert.AreEqual(1, removable.Count, "確認ダイアログに渡す削除件数は managedGroups でフィルタされた件数と一致するべき。");
            Assert.AreEqual(1, keptCount);

            AddressTellerSnapshotService.RestoreExactWithRemoval(snapshot, _settings, removable.Select(e => e.Guid));

            Assert.IsNull(_settings.FindAssetEntry("guid-managed"), "managed グループのエントリは実削除されるべき。");
            Assert.IsNotNull(_settings.FindAssetEntry("guid-unmanaged"), "管理外グループのエントリは実削除の件数一致どおり保持されるべき。");
        }

        [Test]
        public void Diff_DetectsAddedRemovedAndChanged()
        {
            var before = new AddressTellerSnapshot();
            before.Entries.Add(Entry("guid-removed", "OldOnly", "GroupA"));
            before.Entries.Add(Entry("guid-changed", "Before", "GroupA"));

            var after = new AddressTellerSnapshot();
            after.Entries.Add(Entry("guid-changed", "After", "GroupA"));
            after.Entries.Add(Entry("guid-added", "NewOnly", "GroupB"));

            var diff = AddressTellerSnapshotService.Diff(before, after);

            Assert.AreEqual(1, diff.Added.Count);
            Assert.AreEqual("guid-added", diff.Added[0].Guid);

            Assert.AreEqual(1, diff.Removed.Count);
            Assert.AreEqual("guid-removed", diff.Removed[0].Guid);

            Assert.AreEqual(1, diff.Changed.Count);
            Assert.AreEqual("guid-changed", diff.Changed[0].After.Guid);
            Assert.AreEqual("Before", diff.Changed[0].Before.Address);
            Assert.AreEqual("After", diff.Changed[0].After.Address);
        }

        [Test]
        public void Diff_NoChanges_IsEmpty()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(Entry("guid1", "Foo", "GroupA"));

            var diff = AddressTellerSnapshotService.Diff(snapshot, snapshot);

            Assert.IsTrue(diff.IsEmpty);
        }
    }
}

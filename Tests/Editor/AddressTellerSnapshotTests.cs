using NUnit.Framework;
using System.Collections.Generic;

namespace AddressTeller.Editor.Tests
{
    public class AddressTellerSnapshotTests
    {
        [Test]
        public void ToJson_FromJson_RoundTrip()
        {
            var snapshot = new AddressTellerSnapshot();
            snapshot.Entries.Add(new SnapshotEntry
            {
                Guid = "guid1",
                Address = "Characters/Player",
                GroupName = "Characters",
                Labels = new List<string> { "preload", "ui" },
            });

            var json = snapshot.ToJson();
            var restored = AddressTellerSnapshot.FromJson(json);

            Assert.AreEqual(1, restored.Entries.Count);
            Assert.AreEqual("guid1", restored.Entries[0].Guid);
            Assert.AreEqual("Characters/Player", restored.Entries[0].Address);
            Assert.AreEqual("Characters", restored.Entries[0].GroupName);
            CollectionAssert.AreEqual(new[] { "preload", "ui" }, restored.Entries[0].Labels);
        }

        [Test]
        public void ToJson_FromJson_EmptyEntries()
        {
            var snapshot = new AddressTellerSnapshot();

            var restored = AddressTellerSnapshot.FromJson(snapshot.ToJson());

            Assert.AreEqual(0, restored.Entries.Count);
        }

        /// <summary>
        /// メタデータ（CapturedAtIso/Comment/UnityVersion/PackageVersion/SchemaVersion）とエントリ1件を
        /// 埋めたスナップショット。ゴールデンテストで各フィールドの値を固有の識別子として使うため、
        /// フィールドごとに異なる値を設定している。
        /// </summary>
        private static AddressTellerSnapshot SnapshotWithEntryAndMetadata()
        {
            var snapshot = new AddressTellerSnapshot
            {
                CapturedAtIso = "2024-01-01T00:00:00Z",
                Comment = "test comment",
                UnityVersion = "6000.3.0f1",
                PackageVersion = "0.4.0",
                SchemaVersion = 1,
            };
            snapshot.Entries.Add(new SnapshotEntry
            {
                Guid = "guid-1",
                Address = "Characters/Player",
                GroupName = "Characters",
                Labels = new List<string> { "preload", "ui" },
            });

            return snapshot;
        }

        // 以下は Documentation~/compatibility.md §6（Snapshot Files）で「互換性契約」として文書化した
        // JSON のキー名を固定するゴールデンテスト。レポート側（AddressTellerReportWriterTests）と同じ方針で、
        // StringAssert.Contains によるキー名だけの部分一致ではなくコロン＋値まで含めて照合する
        // （JsonUtility はクラスのフィールド宣言順でシリアライズするため、末尾フィールド以外は直後に
        // カンマが続くことも合わせて固定する）。スナップショットのキーはレポート側と異なり同名キーが
        // 複数箇所に出現する構造ではないため、値と偶然一致するリスクは無いが、対称性のため同じ照合方法を用いる。
        //
        // トレードオフ: 上記のカンマ固定は、末尾以外のフィールドの宣言順を入れ替えただけ（キー名自体は不変で
        // 互換性ポリシー上は非破壊）の場合でも、末尾になったフィールドの直後にカンマが付かなくなる等の理由で
        // このテストを失敗させうる。意図的に許容している弱点であり、失敗時は差分がキー名の変更かカンマ位置
        // だけの変化かを個別に確認すること。

        [Test]
        public void ToJson_ContainsExpectedTopLevelKeys()
        {
            var snapshot = SnapshotWithEntryAndMetadata();

            var json = snapshot.ToJson();

            StringAssert.Contains("\"Entries\": [", json);
            StringAssert.Contains("\"CapturedAtIso\": \"2024-01-01T00:00:00Z\",", json);
            StringAssert.Contains("\"Comment\": \"test comment\",", json);
            StringAssert.Contains("\"UnityVersion\": \"6000.3.0f1\",", json);
            StringAssert.Contains("\"PackageVersion\": \"0.4.0\",", json);
            StringAssert.Contains("\"SchemaVersion\": 1", json);
        }

        [Test]
        public void ToJson_EntryKeys_ContainsAllFields()
        {
            var snapshot = SnapshotWithEntryAndMetadata();

            var json = snapshot.ToJson();

            StringAssert.Contains("\"Guid\": \"guid-1\",", json);
            StringAssert.Contains("\"Address\": \"Characters/Player\",", json);
            StringAssert.Contains("\"GroupName\": \"Characters\",", json);
            StringAssert.Contains("\"Labels\": [", json);
        }

        [Test]
        public void FromJson_LegacyFormatWithoutMetadata_FallsBackToDefaults()
        {
            // メタデータフィールドが存在しない旧形式の JSON。
            const string legacyJson = @"{""Entries"":[{""Guid"":""guid1"",""Address"":""Characters/Player"",""GroupName"":""Characters"",""Labels"":[""preload""]}]}";

            var restored = AddressTellerSnapshot.FromJson(legacyJson);

            Assert.AreEqual(1, restored.Entries.Count);
            Assert.AreEqual("guid1", restored.Entries[0].Guid);
            Assert.AreEqual("Characters/Player", restored.Entries[0].Address);
            Assert.AreEqual("Characters", restored.Entries[0].GroupName);
            CollectionAssert.AreEqual(new[] { "preload" }, restored.Entries[0].Labels);

            Assert.AreEqual(0, restored.SchemaVersion);
            Assert.AreEqual("", restored.Comment);
            Assert.AreEqual("", restored.CapturedAtIso);
            Assert.AreEqual("", restored.UnityVersion);
            Assert.AreEqual("", restored.PackageVersion);
        }
    }
}

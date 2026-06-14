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

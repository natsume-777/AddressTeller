using NUnit.Framework;
using System.Collections.Generic;

namespace Natsume777.AddressTeller.Editor.Tests
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
    }
}

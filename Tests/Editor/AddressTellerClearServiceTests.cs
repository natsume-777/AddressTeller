using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// AddressTellerClearService.Clear の単体テスト。
    /// </summary>
    public class AddressTellerClearServiceTests
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _groupA;
        private AddressableAssetGroup _groupB;

        [SetUp]
        public void SetUp()
        {
            _settings = AddressableAssetSettings.Create("Assets/_AddressTellerTestTemp", "AddressTellerClearServiceTestSettings", false, false);
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

        [Test]
        public void Clear_All_RemovesAllEntriesFromAllGroups()
        {
            var entryA = _settings.CreateOrMoveEntry("guid1", _groupA);
            entryA.SetAddress("Foo");
            entryA.SetLabel("preload", true);

            var entryB = _settings.CreateOrMoveEntry("guid2", _groupB);
            entryB.SetAddress("Bar");

            var cleared = AddressTellerClearService.Clear(_settings, ClearScope.All);

            Assert.AreEqual(2, cleared.Count);
            Assert.IsNull(_settings.FindAssetEntry("guid1"));
            Assert.IsNull(_settings.FindAssetEntry("guid2"));
            Assert.AreEqual(0, _groupA.entries.Count);
            Assert.AreEqual(0, _groupB.entries.Count);
        }

        [Test]
        public void Clear_All_ReturnsClearedEntriesWithOriginalInfo()
        {
            var entryA = _settings.CreateOrMoveEntry("guid1", _groupA);
            entryA.SetAddress("Foo");
            entryA.SetLabel("preload", true);
            entryA.SetLabel("ui", true);

            var cleared = AddressTellerClearService.Clear(_settings, ClearScope.All);

            var info = cleared.Single(c => c.Guid == "guid1");
            Assert.AreEqual("Foo", info.Address);
            Assert.AreEqual("GroupA", info.GroupName);
            CollectionAssert.AreEqual(new[] { "preload", "ui" }, info.Labels);
        }

        [Test]
        public void Clear_All_ReturnsEntriesOrderedByGroupThenGuid()
        {
            _settings.CreateOrMoveEntry("guid_b2", _groupB).SetAddress("B2");
            _settings.CreateOrMoveEntry("guid_a2", _groupA).SetAddress("A2");
            _settings.CreateOrMoveEntry("guid_a1", _groupA).SetAddress("A1");
            _settings.CreateOrMoveEntry("guid_b1", _groupB).SetAddress("B1");

            var cleared = AddressTellerClearService.Clear(_settings, ClearScope.All);

            var order = cleared.Select(c => (c.GroupName, c.Guid)).ToList();
            CollectionAssert.AreEqual(
                new[] { ("GroupA", "guid_a1"), ("GroupA", "guid_a2"), ("GroupB", "guid_b1"), ("GroupB", "guid_b2") },
                order);
        }

        [Test]
        public void Clear_Managed_LeavesEntriesOutsideManagedGroupsUntouched()
        {
            _settings.CreateOrMoveEntry("guid1", _groupA).SetAddress("Foo");
            _settings.CreateOrMoveEntry("guid2", _groupB).SetAddress("Bar");

            var managedGroups = new HashSet<string> { "GroupA" };
            var cleared = AddressTellerClearService.Clear(_settings, ClearScope.Managed, managedGroups);

            Assert.AreEqual(1, cleared.Count);
            Assert.AreEqual("guid1", cleared[0].Guid);
            Assert.IsNull(_settings.FindAssetEntry("guid1"));
            Assert.IsNotNull(_settings.FindAssetEntry("guid2"));
        }

        [Test]
        public void Clear_Managed_WithoutManagedGroups_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AddressTellerClearService.Clear(_settings, ClearScope.Managed, null));
        }

        [Test]
        public void Clear_NoEntries_ReturnsEmptyList()
        {
            var cleared = AddressTellerClearService.Clear(_settings, ClearScope.All);

            Assert.IsEmpty(cleared);
        }
    }
}

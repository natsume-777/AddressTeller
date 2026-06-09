using NUnit.Framework;
using System;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class AssetContextTests
    {
        [Test]
        public void Properties_ReturnExpectedValues()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Characters/Player.prefab", typeof(GameObject));

            Assert.AreEqual("abc123", ctx.Guid);
            Assert.AreEqual("Assets/Game/Characters/Player.prefab", ctx.Path);
            Assert.AreEqual(typeof(GameObject), ctx.Type);
            Assert.AreEqual("Player", ctx.FileNameWithoutExtension);
            Assert.AreEqual("Player.prefab", ctx.FileName);
            Assert.AreEqual("Assets/Game/Characters", ctx.Directory);
        }

        [Test]
        public void Path_NormalizesBackslashes()
        {
            var ctx = new AssetContext("abc123", @"Assets\Game\Player.prefab", typeof(GameObject));

            Assert.AreEqual("Assets/Game/Player.prefab", ctx.Path);
            Assert.AreEqual("Assets/Game", ctx.Directory);
        }

        [Test]
        public void Constructor_ThrowsOnEmptyGuid()
        {
            Assert.Throws<ArgumentException>(() =>
                new AssetContext("", "Assets/Game/Player.prefab", typeof(GameObject)));
        }

        [Test]
        public void Constructor_ThrowsOnEmptyPath()
        {
            Assert.Throws<ArgumentException>(() =>
                new AssetContext("abc123", "", typeof(GameObject)));
        }

        [Test]
        public void Constructor_ThrowsOnNullType()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AssetContext("abc123", "Assets/Game/Player.prefab", null));
        }
    }
}

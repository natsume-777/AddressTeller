using NUnit.Framework;
using System;
using UnityEngine;

namespace AddressTeller.Editor.Tests
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

        [Test]
        public void Extension_ReturnsLowercasedExtension()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Player.prefab", typeof(GameObject));

            Assert.AreEqual(".prefab", ctx.Extension);
        }

        [Test]
        public void Extension_UppercaseExtension_IsLowercased()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Icon.PNG", typeof(GameObject));

            Assert.AreEqual(".png", ctx.Extension);
        }

        [Test]
        public void Extension_NoExtension_ReturnsEmptyString()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Player", typeof(GameObject));

            Assert.AreEqual(string.Empty, ctx.Extension);
        }

        [Test]
        public void IsInFolder_AssetInSubfolder_ReturnsTrue()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Characters/Player.prefab", typeof(GameObject));

            Assert.IsTrue(ctx.IsInFolder("Assets/Game"));
        }

        [Test]
        public void IsInFolder_AssetDirectlyInFolder_ReturnsTrue()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Player.prefab", typeof(GameObject));

            Assert.IsTrue(ctx.IsInFolder("Assets/Game"));
        }

        [Test]
        public void IsInFolder_AssetNotInFolder_ReturnsFalse()
        {
            var ctx = new AssetContext("abc123", "Assets/Other/Player.prefab", typeof(GameObject));

            Assert.IsFalse(ctx.IsInFolder("Assets/Game"));
        }

        [Test]
        public void IsInFolder_CaseInsensitive_ReturnsTrue()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Player.prefab", typeof(GameObject));

            Assert.IsTrue(ctx.IsInFolder("assets/GAME"));
        }

        [Test]
        public void IsInFolder_TrailingSlashOnFolder_ReturnsTrue()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Player.prefab", typeof(GameObject));

            Assert.IsTrue(ctx.IsInFolder("Assets/Game/"));
        }

        [Test]
        public void PathSegments_SplitsByForwardSlash()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Characters/Player.prefab", typeof(GameObject));

            Assert.AreEqual(new[] { "Assets", "Game", "Characters", "Player.prefab" }, ctx.PathSegments);
        }

        [Test]
        public void RelativePathFrom_AssetInSubfolder_ReturnsRelativePath()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Sub/Player.prefab", typeof(GameObject));

            Assert.AreEqual("Sub/Player.prefab", ctx.RelativePathFrom("Assets/Game"));
        }

        [Test]
        public void RelativePathFrom_AssetNotInFolder_ReturnsOriginalPath()
        {
            var ctx = new AssetContext("abc123", "Assets/Other/Player.prefab", typeof(GameObject));

            Assert.AreEqual("Assets/Other/Player.prefab", ctx.RelativePathFrom("Assets/Game"));
        }

        [Test]
        public void RelativePathFrom_CaseInsensitive_ReturnsRelativePath()
        {
            var ctx = new AssetContext("abc123", "Assets/Game/Sub/Player.prefab", typeof(GameObject));

            Assert.AreEqual("Sub/Player.prefab", ctx.RelativePathFrom("assets/GAME"));
        }
    }
}

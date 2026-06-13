using NUnit.Framework;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor.Tests
{
    public class MatchTests
    {
        private static AssetContext MakeCtx(string path, System.Type type = null) =>
            new AssetContext("guid1", path, type ?? typeof(GameObject));

        [Test]
        public void InFolder_Recursive_MatchesAssetInSubfolder()
        {
            var condition = Match.InFolder("Assets/Characters");

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Sub/Player.prefab")));
            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Player.prefab")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Other/Player.prefab")));
        }

        [Test]
        public void InFolder_Recursive_DefaultDescription()
        {
            var condition = Match.InFolder("Assets/Characters");

            Assert.AreEqual("InFolder(Assets/Characters)", condition.Description);
        }

        [Test]
        public void InFolder_NonRecursive_MatchesOnlyDirectChildren()
        {
            var condition = Match.InFolder("Assets/Characters", recursive: false);

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Player.prefab")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Characters/Sub/Player.prefab")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Other/Player.prefab")));
        }

        [Test]
        public void InFolder_NonRecursive_CaseInsensitive()
        {
            var condition = Match.InFolder("assets/CHARACTERS", recursive: false);

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Player.prefab")));
        }

        [Test]
        public void InFolder_NonRecursive_Description()
        {
            var condition = Match.InFolder("Assets/Characters", recursive: false);

            Assert.AreEqual("InFolder(Assets/Characters, recursive: false)", condition.Description);
        }

        [Test]
        public void OfType_MatchesAssignableType()
        {
            var condition = Match.OfType<Texture2D>();

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Tex.png", typeof(Texture2D))));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Player.prefab", typeof(GameObject))));
        }

        [Test]
        public void OfType_MatchesBaseType()
        {
            var condition = Match.OfType<Texture>();

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Tex.png", typeof(Texture2D))));
        }

        [Test]
        public void OfType_Description()
        {
            var condition = Match.OfType<Texture2D>();

            Assert.AreEqual("OfType<Texture2D>()", condition.Description);
        }

        [Test]
        public void Glob_SingleStar_DoesNotCrossSlash()
        {
            var condition = Match.Glob("Assets/Characters/*.png");

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Icon.png")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Characters/Sub/Icon.png")));
        }

        [Test]
        public void Glob_DoubleStar_CrossesSlash()
        {
            var condition = Match.Glob("Assets/**/*.png");

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Sub/Icon.png")));
            Assert.IsTrue(condition.Test(MakeCtx("Assets/Icon.png")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Characters/Sub/Icon.prefab")));
        }

        [Test]
        public void Glob_QuestionMark_MatchesSingleCharacter()
        {
            var condition = Match.Glob("Assets/Icon?.png");

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Icon1.png")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Icon12.png")));
            Assert.IsFalse(condition.Test(MakeCtx("Assets/Icon.png")));
        }

        [Test]
        public void Glob_CaseInsensitive()
        {
            var condition = Match.Glob("assets/characters/*.png");

            Assert.IsTrue(condition.Test(MakeCtx("Assets/Characters/Icon.png")));
        }

        [Test]
        public void Glob_Description()
        {
            var condition = Match.Glob("Assets/**/*.png");

            Assert.AreEqual("Glob(Assets/**/*.png)", condition.Description);
        }

        [Test]
        public void All_AllTrue_ReturnsTrue()
        {
            var a = new AssetCondition(ctx => true, "A");
            var b = new AssetCondition(ctx => true, "B");

            var combined = Match.All(a, b);

            Assert.IsTrue(combined.Test(MakeCtx("Assets/Player.prefab")));
        }

        [Test]
        public void All_OneFalse_ReturnsFalse()
        {
            var a = new AssetCondition(ctx => true, "A");
            var b = new AssetCondition(ctx => false, "B");

            var combined = Match.All(a, b);

            Assert.IsFalse(combined.Test(MakeCtx("Assets/Player.prefab")));
        }

        [Test]
        public void All_NoConditions_ReturnsAlwaysTrue()
        {
            var combined = Match.All();

            Assert.IsTrue(combined.Test(MakeCtx("Assets/Player.prefab")));
        }
    }
}

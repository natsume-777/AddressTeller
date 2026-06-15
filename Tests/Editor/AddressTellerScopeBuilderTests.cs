using System.Collections.Generic;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// <see cref="AddressTellerScopeBuilder"/> の純粋関数の単体テスト。
    /// </summary>
    public class AddressTellerScopeBuilderTests
    {
        [Test]
        public void ExpandFolders_FileOnly_ReturnsAsIs()
        {
            var result = AddressTellerScopeBuilder.ExpandFolders(
                new[] { "Assets/Foo.prefab" },
                _ => false,
                _ => new List<string>());

            Assert.AreEqual(new[] { "Assets/Foo.prefab" }, result);
        }

        [Test]
        public void ExpandFolders_Folder_ExpandsRecursivelyViaInjectedFunction()
        {
            var result = AddressTellerScopeBuilder.ExpandFolders(
                new[] { "Assets/FolderA" },
                path => path == "Assets/FolderA",
                folder => new[] { "Assets/FolderA/B.prefab", "Assets/FolderA/Sub/C.prefab" });

            Assert.AreEqual(new[] { "Assets/FolderA/B.prefab", "Assets/FolderA/Sub/C.prefab" }, result);
        }

        [Test]
        public void ExpandFolders_DuplicatePaths_AreDeduplicated()
        {
            var result = AddressTellerScopeBuilder.ExpandFolders(
                new[] { "Assets/FolderA", "Assets/FolderA/B.prefab" },
                path => path == "Assets/FolderA",
                folder => new[] { "Assets/FolderA/B.prefab", "Assets/FolderA/C.prefab" });

            Assert.AreEqual(new[] { "Assets/FolderA/B.prefab", "Assets/FolderA/C.prefab" }, result);
        }

        [Test]
        public void ExpandFolders_ResultIsSortedOrdinal()
        {
            var result = AddressTellerScopeBuilder.ExpandFolders(
                new[] { "Assets/Z.prefab", "Assets/A.prefab", "Assets/M.prefab" },
                _ => false,
                _ => new List<string>());

            Assert.AreEqual(new[] { "Assets/A.prefab", "Assets/M.prefab", "Assets/Z.prefab" }, result);
        }

        [Test]
        public void ExpandFolders_EmptyInput_ReturnsEmpty()
        {
            var result = AddressTellerScopeBuilder.ExpandFolders(
                System.Array.Empty<string>(),
                _ => false,
                _ => new List<string>());

            Assert.IsEmpty(result);
        }

        [Test]
        public void BuildScopeTitle_AppendsScopeLabel()
        {
            var title = AddressTellerScopeBuilder.BuildScopeTitle("AddressTeller Preview", "Rule: FooRule");

            Assert.AreEqual("AddressTeller Preview — Rule: FooRule", title);
        }

        [Test]
        public void BuildScopeTitle_NullOrEmptyScopeLabel_ReturnsBaseTitle()
        {
            Assert.AreEqual("AddressTeller Preview", AddressTellerScopeBuilder.BuildScopeTitle("AddressTeller Preview", null));
            Assert.AreEqual("AddressTeller Preview", AddressTellerScopeBuilder.BuildScopeTitle("AddressTeller Preview", ""));
        }

        [Test]
        public void BuildScopeNotice_RuleFiltered_ReturnsNonNull()
        {
            var notice = AddressTellerScopeBuilder.BuildScopeNotice(isRuleFiltered: true);

            Assert.IsNotNull(notice);
            Assert.IsNotEmpty(notice);
        }

        [Test]
        public void BuildScopeNotice_NotRuleFiltered_ReturnsNull()
        {
            var notice = AddressTellerScopeBuilder.BuildScopeNotice(isRuleFiltered: false);

            Assert.IsNull(notice);
        }
    }
}

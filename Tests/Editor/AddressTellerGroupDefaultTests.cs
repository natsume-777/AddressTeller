using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// IAddressRuleBuilder.GroupDefault() の評価結果（DefaultGroup への正規化・リネーム追従・
    /// 競合検出・表示）と、DefaultGroupUnavailable の報告経路を検証する。
    /// </summary>
    public class AddressTellerGroupDefaultTests
    {
        private const string TestRootFolder = "Assets/_AddressTellerTestTemp";
        private const string StubFolder = TestRootFolder + "/GroupDefault";
        private const string StubAssetPath = StubFolder + "/StubAsset.prefab";

        // settings.ConfigFolder は AssetFilter.ShouldExclude の判定に使われるため、
        // テストアセット自身が除外されないよう StubFolder と衝突しない値を割り当てる。
        private const string FakeConfigFolder = "Assets/_AddressTellerTestTempGroupDefaultConfig";

        /// <summary>StubFolder 配下のアセットに GroupDefault() でアドレスを付与するルール。</summary>
        private sealed class GroupDefaultRule : AddressRuleBase
        {
            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault()
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => ctx.FileNameWithoutExtension)
                    .Label("default-group");
            }
        }

        /// <summary>GroupDefault() と Group("実名で同じグループ") の両方からアドレスを発行する、競合検証用のルール。</summary>
        private sealed class ConflictingDefaultAndNamedRule : AddressRuleBase
        {
            public string NamedGroup { get; set; }

            public override void Configure(IAddressRuleBuilder rules)
            {
                rules.GroupDefault()
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => "from-default");

                rules.Group(NamedGroup)
                    .Where(ctx => ctx.Path.StartsWith(StubFolder + "/", System.StringComparison.Ordinal))
                    .Address(ctx => "from-named");
            }
        }

        private AddressableAssetSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = AddressTellerTestSettingsFactory.CreateInMemory(FakeConfigFolder, "AddressTellerGroupDefaultTestSettings");

            if (AssetDatabase.IsValidFolder(TestRootFolder))
                AssetDatabase.DeleteAsset(TestRootFolder);

            AssetDatabase.CreateFolder("Assets", "_AddressTellerTestTemp");
            AssetDatabase.CreateFolder(TestRootFolder, "GroupDefault");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRootFolder);
            DestroySettings(_settings);
        }

        /// <summary>テスト用の非永続 AddressableAssetSettings を、配下の全グループも含めて破棄する。</summary>
        private static void DestroySettings(AddressableAssetSettings settings)
        {
            if (settings == null) return;

            foreach (var group in settings.groups.Where(g => g != null).ToList())
                Object.DestroyImmediate(group, true);

            Object.DestroyImmediate(settings, true);
        }

        private static string GuidOf(string path) => AssetDatabase.AssetPathToGUID(path);

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

        [Test]
        public void GroupDefault_AppliesToDefaultGroup()
        {
            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new GroupDefaultRule() };

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);
            Assert.IsFalse(issues.Any(i => !i.IsOk), "GroupDefault() は DefaultGroup へ正常に書き込まれるべき。");

            var entry = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entry);
            Assert.AreEqual(_settings.DefaultGroup.Name, entry.parentGroup.Name);
            Assert.AreEqual("StubAsset", entry.address);
            CollectionAssert.Contains(entry.labels, "default-group");
        }

        [Test]
        public void GroupDefault_FollowsDefaultGroupRename()
        {
            CreatePrefab(StubAssetPath);
            var rules = new AddressRuleBase[] { new GroupDefaultRule() };

            // DefaultGroup を取得・確定させた上でリネームする。
            var defaultGroup = _settings.DefaultGroup;
            defaultGroup.Name = "RenamedDefaultGroup";

            var issues = AddressTellerService.ApplyAll(new[] { StubAssetPath }, _settings, NullProgressReporter.Instance, rules);
            Assert.IsFalse(issues.Any(i => !i.IsOk));

            var entry = _settings.FindAssetEntry(GuidOf(StubAssetPath));
            Assert.IsNotNull(entry);
            Assert.AreEqual("RenamedDefaultGroup", entry.parentGroup.Name);
        }

        [Test]
        public void GroupDefault_AndNamedGroup_SameRealGroup_ReportsConflictWithRealName()
        {
            CreatePrefab(StubAssetPath);

            // GroupDefault() と Group("実名") の実名側に DefaultGroup の名前を直接指定し、
            // 両方が同一の実グループを指すようにする。
            var defaultGroupName = _settings.DefaultGroup.Name;
            var rules = new AddressRuleBase[] { new ConflictingDefaultAndNamedRule { NamedGroup = defaultGroupName } };

            var issues = AddressTellerService.ValidateAll(_settings, NullProgressReporter.Instance, rules);
            var issue = issues.SingleOrDefault(i => i.Context?.Guid == GuidOf(StubAssetPath));

            Assert.IsNotNull(issue);
            Assert.AreEqual(ValidationStatus.ConflictingAddress, issue.Status);

            // センチネル文字列が競合候補・メッセージに漏出していないこと。実グループ名のみが使われる。
            Assert.AreEqual(2, issue.ConflictingCandidates.Count);
            foreach (var candidate in issue.ConflictingCandidates)
                Assert.AreEqual(defaultGroupName, candidate.GroupName);
            StringAssert.DoesNotContain(AddressRuleBuilderImpl.DefaultGroupSentinel, issue.Message);
        }

        [Test]
        public void RuleOverview_GroupDefault_DisplaysAsPlaceholder_ButExcludedFromManagedGroups()
        {
            var rules = new AddressRuleBase[] { new GroupDefaultRule() };

            var cache = AddressTellerProjectSettings.BuildRuleOverviewCache(rules);

            var overview = cache.Rules.Single(r => r.RuleType == typeof(GroupDefaultRule));
            var entry = overview.Entries.Single();
            Assert.AreEqual(AddressRuleBuilderImpl.DefaultGroupSentinel, entry.GroupName,
                "builder.Entries 自体はセンチネルを保持する（settings 非依存）。表示変換は DisplayGroupName が担う。");
            Assert.AreEqual("(Default Group)", AddressRuleBuilderImpl.DisplayGroupName(entry.GroupName));

            // BuildRuleOverviewCache は Configure() の生出力のみから構築され、settings に依存しない
            // （RuleEvaluationPipeline.BuildSetup のような実 DefaultGroup への解決を行わない）。
            // そのため CollectManagedGroups は BuildSetup の managedGroups 計算（null / 未解決センチネル
            // を除外）と同じ条件で、GroupDefault() 由来のセンチネルを常に除外する
            // （実グループ名にもプレースホルダ文字列にも解決されず、Managed Groups には一切現れない）。
            var managedGroups = AddressTellerProjectSettings.CollectManagedGroups(cache);
            CollectionAssert.IsEmpty(managedGroups,
                "GroupDefault() のみを参照するルールは、settings 非依存の概要キャッシュでは解決されないため Managed Groups に現れない。");
        }

        // --- DefaultGroupUnavailable ---
        //
        // AddressableAssetSettings.DefaultGroup は通常 null を返さず、未設定時は自動でグループを
        // 作成するため、RuleEvaluationPipeline.BuildSetup の defaultGroupUnavailable=true 経路は
        // 実運用では発生しにくい防御的分岐になる。ここでは、その分岐が発生した場合に
        // AddressTellerApplier.Validate がセンチネルを検出して DefaultGroupUnavailable を返す
        // ことを、エントリのグループ名がセンチネルのまま渡るケースとして直接検証する。

        [Test]
        public void Validate_GroupNameIsSentinel_ReturnsDefaultGroupUnavailable_NotOk()
        {
            var context = new AssetContext("guid-1", StubAssetPath, typeof(GameObject));
            var candidate = new AddressCandidate(AddressRuleBuilderImpl.DefaultGroupSentinel, "addr", "GroupDefaultRule", null, 0);
            var resolution = new AddressResolution(new[] { candidate }, new System.Collections.Generic.HashSet<string>());

            var result = AddressTellerApplier.Validate(context, resolution, _settings.groups.Where(g => g != null).Select(g => g.Name).ToList());

            Assert.AreEqual(ValidationStatus.DefaultGroupUnavailable, result.Status);
            Assert.IsFalse(result.IsOk);
        }

        [Test]
        public void Apply_GroupNameIsSentinel_SkipsWrite_ReturnsDefaultGroupUnavailable()
        {
            CreatePrefab(StubAssetPath);

            var context = new AssetContext(GuidOf(StubAssetPath), StubAssetPath, typeof(GameObject));
            var candidate = new AddressCandidate(AddressRuleBuilderImpl.DefaultGroupSentinel, "addr", "GroupDefaultRule", null, 0);
            var resolution = new AddressResolution(new[] { candidate }, new System.Collections.Generic.HashSet<string>());

            var existingGroupNames = _settings.groups.Where(g => g != null).Select(g => g.Name).ToList();
            var result = AddressTellerApplier.Apply(context, resolution, _settings, existingGroupNames);

            Assert.AreEqual(ValidationStatus.DefaultGroupUnavailable, result.Status);
            Assert.IsFalse(result.IsOk);
            Assert.IsNull(_settings.FindAssetEntry(GuidOf(StubAssetPath)), "書き込みはスキップされるべき。");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AddressTeller.Editor
{
    /// <summary>
    /// 1つの <see cref="AddressRuleEntry"/>（Configure() 内の Group() 1回分）から抽出した表示用メタ情報。
    /// </summary>
    internal readonly struct RuleEntryOverview
    {
        /// <summary>Group() に渡したグループ名。</summary>
        public string GroupName { get; }

        /// <summary>Where に渡した説明文。未指定の場合は null。</summary>
        public string Description { get; }

        /// <summary>Address() が呼ばれていれば true（アドレスを付与する）。</summary>
        public bool HasAddress { get; }

        /// <summary>Label() が呼ばれた回数。</summary>
        public int LabelCount { get; }

        /// <summary>Configure() 内で Group() が呼ばれた順序（0始まり）。Description が null の場合のフォールバック表示に使う。</summary>
        public int RuleIndex { get; }

        public RuleEntryOverview(string groupName, string description, bool hasAddress, int labelCount, int ruleIndex)
        {
            GroupName = groupName;
            Description = description;
            HasAddress = hasAddress;
            LabelCount = labelCount;
            RuleIndex = ruleIndex;
        }
    }

    /// <summary>
    /// AddressRuleBase 1クラス分の概要。Configure() が例外を投げた場合は <see cref="ConfigureError"/> に
    /// メッセージが入り、<see cref="Entries"/> は空になる。
    /// </summary>
    internal readonly struct RuleClassOverview
    {
        /// <summary>ルールクラスの型。</summary>
        public Type RuleType { get; }

        /// <summary>評価順序。</summary>
        public int Order { get; }

        /// <summary>Configure() が成功した場合のグループ一覧。例外時は空。</summary>
        public IReadOnlyList<RuleEntryOverview> Entries { get; }

        /// <summary>Configure() 実行時に発生した例外のメッセージ。発生していない場合は null。</summary>
        public string ConfigureError { get; }

        public RuleClassOverview(Type ruleType, int order, IReadOnlyList<RuleEntryOverview> entries, string configureError)
        {
            RuleType = ruleType;
            Order = order;
            Entries = entries;
            ConfigureError = configureError;
        }
    }

    /// <summary>
    /// 同一 Order 値を持つルールクラス名のグループ。Order 重複警告の表示に使う。
    /// </summary>
    internal readonly struct DuplicateOrderGroup
    {
        public int Order { get; }
        public IReadOnlyList<string> RuleClassNames { get; }

        public DuplicateOrderGroup(int order, IReadOnlyList<string> ruleClassNames)
        {
            Order = order;
            RuleClassNames = ruleClassNames;
        }
    }

    /// <summary>
    /// Project Settings 画面で表示するルール概要一式。<see cref="AddressTellerProjectSettings.BuildRuleOverviewCache"/>
    /// で構築し、構築結果は activateHandler から static キャッシュとして参照する想定（UI 組み立て時に
    /// Configure() を毎回実行しないため）。
    /// </summary>
    internal readonly struct RuleOverviewCache
    {
        /// <summary>Order 昇順・同順位はクラス名昇順のルール概要一覧。</summary>
        public IReadOnlyList<RuleClassOverview> Rules { get; }

        /// <summary>Order が重複しているルールクラスのグループ一覧。</summary>
        public IReadOnlyList<DuplicateOrderGroup> DuplicateOrders { get; }

        public RuleOverviewCache(IReadOnlyList<RuleClassOverview> rules, IReadOnlyList<DuplicateOrderGroup> duplicateOrders)
        {
            Rules = rules;
            DuplicateOrders = duplicateOrders;
        }
    }

    /// <summary>
    /// Project Settings ウィンドウに AddressTeller の設定項目・ルール一覧を表示する。
    /// </summary>
    internal static class AddressTellerProjectSettings
    {
        // ルール概要キャッシュ。ドメインリロードで自動的にリセットされる。
        private static RuleOverviewCache? s_ruleOverviewCache;

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/AddressTeller", SettingsScope.Project)
            {
                label = "AddressTeller",
                activateHandler = BuildUI,
                keywords = new[] { "AddressTeller", "Addressables", "Address", "Label" },
            };
        }

        private static void BuildUI(string searchContext, VisualElement root)
        {
            var overviewCache = GetRuleOverviewCache();

            var scroll = new ScrollView();
            root.Add(scroll);

            var container = new VisualElement();
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            container.style.paddingTop = 4;
            scroll.Add(container);

            // ---- Apply / Validate Behavior ----
            container.Add(MakeSectionLabel("Apply / Validate Behavior"));

            var autoApplyToggle = new Toggle("Auto-apply on import") { value = AddressTellerSettings.PostprocessEnabled };
            autoApplyToggle.RegisterValueChangedCallback(e => AddressTellerSettings.PostprocessEnabled = e.newValue);
            container.Add(autoApplyToggle);
            container.Add(MakeDescription("Runs ApplyAll whenever assets are imported, moved, or deleted."));

            var orderField = new IntegerField("Postprocessor order") { value = AddressTellerSettings.PostprocessOrder };
            orderField.RegisterValueChangedCallback(e => AddressTellerSettings.PostprocessOrder = e.newValue);
            container.Add(orderField);
            container.Add(MakeDescription("Value passed to AssetPostprocessor.GetPostprocessOrder(). Lower values run before other postprocessors. Default is 1000 (runs later)."));

            var cleanupToggle = new Toggle("Remove unmatched entries") { value = AddressTellerSettings.CleanupStaleEntries };
            cleanupToggle.RegisterValueChangedCallback(e => AddressTellerSettings.CleanupStaleEntries = e.newValue);
            container.Add(cleanupToggle);
            container.Add(MakeDescription("Automatically removes assets that no longer match any rule from groups managed by AddressTeller. The entry itself is deleted, so both the address and labels (in Addressables) are lost."));

            var autoCreateToggle = new Toggle("Auto-create missing groups") { value = AddressTellerSettings.AutoCreateMissingGroups };
            autoCreateToggle.RegisterValueChangedCallback(e => AddressTellerSettings.AutoCreateMissingGroups = e.newValue);
            container.Add(autoCreateToggle);
            container.Add(MakeDescription("When a group referenced by a rule does not exist, Apply will create it by duplicating the DefaultGroup schema. Validate/Predict only displays it as a pending creation and does not actually create the group."));

            // ---- Managed Groups Foldout ----
            var managedGroups = CollectManagedGroups(overviewCache);
            var managedFoldout = new Foldout { text = $"Managed Groups ({managedGroups.Count})", value = false };
            if (managedGroups.Count == 0)
            {
                managedFoldout.Add(new Label("(No groups are referenced by enabled rules)") { style = { color = new Color(0.6f, 0.6f, 0.6f) } });
            }
            else
            {
                foreach (var groupName in managedGroups)
                    managedFoldout.Add(new Label(groupName));
            }
            container.Add(managedFoldout);
            container.Add(MakeDescription("Groups referenced by at least one enabled rule via Group(). These are the targets of \"Remove unmatched entries\" and \"Auto-create missing groups\". Entries manually registered in these groups will be removed if no rule matches them."));

            // ---- Registered Rules ----
            container.Add(MakeSpacer());
            container.Add(MakeSectionLabel("Registered Rules"));

            foreach (var duplicate in overviewCache.DuplicateOrders)
            {
                var ruleNames = string.Join(", ", duplicate.RuleClassNames);
                container.Add(new HelpBox(
                    $"Order={duplicate.Order} is duplicated: {ruleNames}. Verify the evaluation order.",
                    HelpBoxMessageType.Warning));
            }

            // ルール一覧の「このルールだけ Validate/Apply」ボタン用に、型からインスタンスを引けるようにしておく。
            // RuleCollector.CollectRules() 自体はキャッシュ済みのため、Dictionary 化のみ。
            var ruleInstancesByType = RuleCollector.CollectRules().ToDictionary(r => r.GetType());
            var addressablesSettings = AddressableAssetSettingsDefaultObject.Settings;

            var overviewRules = overviewCache.Rules;
            if (overviewRules.Count == 0)
            {
                container.Add(new HelpBox(
                    "No class inheriting AddressRuleBase was found.\n" +
                    "Inherit AddressRuleBase and define address/label rules in Configure().",
                    HelpBoxMessageType.Info));
            }

            foreach (var overview in overviewRules)
            {
                var type = overview.RuleType;
                container.Add(BuildRuleRow(overview, type, addressablesSettings, ruleInstancesByType));
            }

            // ---- Operations ----
            container.Add(MakeSpacer());
            container.Add(MakeSectionLabel("Operations"));

            if (addressablesSettings == null)
                container.Add(new HelpBox("AddressableAssetSettings not found. Please initialize Addressables.", HelpBoxMessageType.Warning));

            var opsRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var validateBtn = new Button(AddressTellerMenu.Validate) { text = "Run Validate" };
            validateBtn.SetEnabled(addressablesSettings != null);
            var previewBtn = new Button(AddressTellerMenu.ApplyWithValidate) { text = "Preview (with Validate)" };
            previewBtn.SetEnabled(addressablesSettings != null);
            var applyBtn = new Button(AddressTellerMenu.ApplyAll) { text = "Run Apply" };
            applyBtn.SetEnabled(addressablesSettings != null);
            opsRow.Add(validateBtn);
            opsRow.Add(previewBtn);
            opsRow.Add(applyBtn);
            container.Add(opsRow);

            // ---- Snapshot ----
            container.Add(MakeSpacer());
            container.Add(MakeSectionLabel("Snapshot"));

            var snapshotField = new TextField("Snapshot folder") { value = AddressTellerSettings.SnapshotFolder };
            snapshotField.RegisterValueChangedCallback(e => AddressTellerSettings.SnapshotFolder = e.newValue);
            container.Add(snapshotField);
            container.Add(MakeDescription("Relative path from the project root (parent directory of Assets). Default is \"AddressTellerSnapshots\" (outside Assets, not imported by Unity)."));

            var browseRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd } };
            var browseBtn = new Button(() =>
            {
                PickSnapshotFolder();
                // フォルダ選択後に TextField へ反映する
                snapshotField.SetValueWithoutNotify(AddressTellerSettings.SnapshotFolder);
            }) { text = "Browse...", style = { width = 120 } };
            browseRow.Add(browseBtn);
            container.Add(browseRow);

            var autoSnapshotToggle = new Toggle("Auto-snapshot before Apply") { value = AddressTellerSettings.AutoSnapshotBeforeApplyAll };
            autoSnapshotToggle.RegisterValueChangedCallback(e => AddressTellerSettings.AutoSnapshotBeforeApplyAll = e.newValue);
            container.Add(autoSnapshotToggle);
            container.Add(MakeDescription("Applies only to the Apply All / Apply with Validate menu actions. Auto-apply on import and CLI execution are not covered."));

            var retentionField = new IntegerField("Auto-snapshot retention count") { value = AddressTellerSettings.AutoSnapshotRetention };
            retentionField.RegisterValueChangedCallback(e => AddressTellerSettings.AutoSnapshotRetention = e.newValue);
            container.Add(retentionField);
            container.Add(MakeDescription("Auto snapshots exceeding this count are automatically deleted. Minimum is 1."));
        }

        /// <summary>ルール1件分の行（有効無効トグル + Foldout + ボタン群）を組み立てる。</summary>
        private static VisualElement BuildRuleRow(
            in RuleClassOverview overview,
            Type type,
            AddressableAssetSettings addressablesSettings,
            Dictionary<Type, AddressRuleBase> ruleInstancesByType)
        {
            var wrapper = new VisualElement();

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var enabledToggle = new Toggle { value = AddressTellerSettings.IsRuleEnabled(type.FullName), style = { width = 20 } };
            enabledToggle.RegisterValueChangedCallback(e => AddressTellerSettings.SetRuleEnabled(type.FullName, e.newValue));
            header.Add(enabledToggle);

            var ruleFoldout = new Foldout { text = $"{type.Name}  (Order: {overview.Order})", value = false };

            // 有効/無効に応じてラベルのアルファを下げる
            void SyncFoldoutEnabled(bool enabled)
            {
                ruleFoldout.style.opacity = enabled ? 1f : 0.5f;
            }
            SyncFoldoutEnabled(AddressTellerSettings.IsRuleEnabled(type.FullName));
            enabledToggle.RegisterValueChangedCallback(e => SyncFoldoutEnabled(e.newValue));

            // Foldout の toggle 部分が展開コンテンツを切り替えるので、詳細はその中に入れる。
            BuildRuleOverviewContent(in overview, ruleFoldout);
            header.Add(ruleFoldout);

            // Select / Validate-Apply ボタンは右揃え
            var buttonsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = StyleKeyword.Auto } };

            var script = FindScriptForType(type);
            var selectBtn = new Button(() => Selection.activeObject = script) { text = "Select" };
            selectBtn.SetEnabled(script != null);
            buttonsRow.Add(selectBtn);

            var previewBtn = new Button(() => AddressTellerScopedPreview.RunRulePreview(addressablesSettings, ruleInstancesByType[type]))
            {
                text = "Validate/Apply this rule only"
            };
            previewBtn.SetEnabled(addressablesSettings != null);
            buttonsRow.Add(previewBtn);

            header.Add(buttonsRow);
            wrapper.Add(header);

            return wrapper;
        }

        /// <summary>ルール Foldout の展開コンテンツ（Configure() 結果の概要）を組み立てる。</summary>
        private static void BuildRuleOverviewContent(in RuleClassOverview overview, Foldout foldout)
        {
            if (overview.ConfigureError != null)
            {
                foldout.Add(new HelpBox($"An exception occurred during Configure(): {overview.ConfigureError}", HelpBoxMessageType.Error));
                return;
            }

            if (overview.Entries.Count == 0)
            {
                foldout.Add(new Label("(Group() was not called)"));
                return;
            }

            foreach (var entry in overview.Entries)
            {
                var where = entry.Description ?? $"(condition #{entry.RuleIndex})";
                var address = entry.HasAddress ? "dynamic" : "none";
                var groupName = AddressRuleBuilderImpl.DisplayGroupName(entry.GroupName);
                foldout.Add(new Label(
                    $"Group: \"{groupName}\"  Where: \"{where}\"  Address: {address}  Labels: {entry.LabelCount}")
                { style = { whiteSpace = WhiteSpace.Normal } });
            }
        }

        /// <summary>フォルダ選択ダイアログを開き、選択結果をプロジェクトルートからの相対パスで保存する。</summary>
        private static void PickSnapshotFolder()
        {
            var current = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            var selected = EditorUtility.OpenFolderPanel("Snapshot Folder", current, "");
            if (string.IsNullOrEmpty(selected)) return;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            AddressTellerSettings.SnapshotFolder = Path.GetRelativePath(projectRoot, selected);
        }

        private static Label MakeSectionLabel(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 6,
                    marginBottom = 2,
                }
            };
        }

        private static Label MakeDescription(string text)
        {
            return new Label(text)
            {
                style =
                {
                    color = new Color(0.6f, 0.6f, 0.6f),
                    whiteSpace = WhiteSpace.Normal,
                    marginLeft = 16,
                    marginBottom = 4,
                }
            };
        }

        private static VisualElement MakeSpacer()
        {
            var spacer = new VisualElement { style = { height = 8 } };
            return spacer;
        }

        /// <summary>
        /// 有効なルールクラスが Configure() で参照しているグループ名を、<see cref="RuleEvaluationPipeline.BuildSetup"/>
        /// の managedGroups（<see cref="AddressTellerSettings.CleanupStaleEntries"/> /
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の対象）と同じ条件で集約する。
        /// 表示順序は決定的にするため Ordinal でソートする。
        /// </summary>
        internal static IReadOnlyList<string> CollectManagedGroups(in RuleOverviewCache overviewCache)
        {
            var groups = new HashSet<string>();
            foreach (var rule in overviewCache.Rules)
            {
                if (!AddressTellerSettings.IsRuleEnabled(rule.RuleType.FullName)) continue;
                foreach (var entry in rule.Entries)
                    groups.Add(AddressRuleBuilderImpl.DisplayGroupName(entry.GroupName));
            }
            return groups.OrderBy(g => g, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 現在キャッシュされているルール概要を返す。未構築の場合は <see cref="RefreshRuleOverviewCache"/> で構築する。
        /// </summary>
        internal static RuleOverviewCache GetRuleOverviewCache()
            => s_ruleOverviewCache ??= RefreshRuleOverviewCache();

        /// <summary>
        /// <see cref="RuleCollector.CollectRules()"/> からルール概要キャッシュを再構築し、static キャッシュへ格納する。
        /// </summary>
        internal static RuleOverviewCache RefreshRuleOverviewCache()
        {
            var cache = BuildRuleOverviewCache(RuleCollector.CollectRules());
            s_ruleOverviewCache = cache;
            return cache;
        }

        /// <summary>
        /// 与えられたルール一覧から概要キャッシュを構築する純粋関数。Configure() はルールごとに try/catch し、
        /// 例外が発生したルールも他のルールの処理をブロックせずスキップする（例外時は <see cref="RuleClassOverview.ConfigureError"/>
        /// にメッセージを記録し <see cref="RuleClassOverview.Entries"/> は空）。
        /// </summary>
        internal static RuleOverviewCache BuildRuleOverviewCache(IReadOnlyList<AddressRuleBase> rules)
        {
            var ruleOverviews = new List<RuleClassOverview>(rules.Count);

            foreach (var rule in rules)
            {
                var type = rule.GetType();
                IReadOnlyList<RuleEntryOverview> entryOverviews = Array.Empty<RuleEntryOverview>();
                string configureError = null;

                try
                {
                    var builder = new AddressRuleBuilderImpl(type.Name);
                    rule.Configure(builder);
                    entryOverviews = builder.Entries
                        .Select(e => new RuleEntryOverview(e.GroupName, e.Description, e.AddressSelector != null, e.LabelSelectors.Count, e.RuleIndex))
                        .ToArray();
                }
                catch (Exception ex)
                {
                    configureError = $"{ex.GetType().Name}: {ex.Message}";
                }

                ruleOverviews.Add(new RuleClassOverview(type, rule.Order, entryOverviews, configureError));
            }

            ruleOverviews = ruleOverviews
                .OrderBy(r => r.Order)
                .ThenBy(r => r.RuleType.Name, StringComparer.Ordinal)
                .ToList();

            var duplicateOrders = RuleCollector.FindDuplicateOrders(rules)
                .OrderBy(g => g.Key)
                .Select(g => new DuplicateOrderGroup(
                    g.Key,
                    g.Select(r => r.GetType().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray()))
                .ToArray();

            return new RuleOverviewCache(ruleOverviews, duplicateOrders);
        }

        private static MonoScript FindScriptForType(System.Type type)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                    return script;
            }

            return null;
        }
    }
}

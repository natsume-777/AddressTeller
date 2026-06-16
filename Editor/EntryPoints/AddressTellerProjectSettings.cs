using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

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
    /// で構築し、構築結果は OnGUI から static キャッシュとして参照する想定（OnGUI 内で毎フレーム
    /// Configure() を実行しないため）。
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
        // ルール概要キャッシュ。初回構築・手動更新トリガで AddressTellerProjectSettings 自身が再構築する
        // （UI 組み込みは別Stepで対応）。ドメインリロードで自動的にリセットされる。
        private static RuleOverviewCache? s_ruleOverviewCache;

        // ルール一覧の Foldout 開閉状態。クラスごとに保持する。ドメインリロードでリセットされて問題ない。
        private static readonly Dictionary<Type, bool> s_ruleFoldouts = new Dictionary<Type, bool>();

        // 「管理対象グループ」一覧の Foldout 開閉状態。
        private static bool s_managedGroupsFoldout;

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/AddressTeller", SettingsScope.Project)
            {
                label = "AddressTeller",
                guiHandler = OnGUI,
                keywords = new[] { "AddressTeller", "Addressables", "Address", "Label" },
            };
        }

        private static void OnGUI(string searchContext)
        {
            var overviewCache = GetRuleOverviewCache();

            EditorGUILayout.LabelField("Apply / Validate Behavior", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var postprocessEnabled = EditorGUILayout.ToggleLeft("Auto-apply on import", AddressTellerSettings.PostprocessEnabled);
            DrawDescription("Runs ApplyAll whenever assets are imported, moved, or deleted.");

            EditorGUILayout.Space(4);

            var postprocessOrder = EditorGUILayout.IntField("Postprocessor order", AddressTellerSettings.PostprocessOrder);
            DrawDescription("Value passed to AssetPostprocessor.GetPostprocessOrder(). Lower values run before other postprocessors. Default is 1000 (runs later).");

            EditorGUILayout.Space(4);

            var cleanupStaleEntries = EditorGUILayout.ToggleLeft("Remove unmatched entries", AddressTellerSettings.CleanupStaleEntries);
            DrawDescription("Automatically removes assets that no longer match any rule from groups managed by AddressTeller. The entry itself is deleted, so both the address and labels (in Addressables) are lost.");

            EditorGUILayout.Space(4);

            var autoCreateMissingGroups = EditorGUILayout.ToggleLeft("Auto-create missing groups", AddressTellerSettings.AutoCreateMissingGroups);
            DrawDescription("When a group referenced by a rule does not exist, Apply will create it by duplicating the DefaultGroup schema. Validate/Predict only displays it as a pending creation and does not actually create the group.");

            if (EditorGUI.EndChangeCheck())
            {
                AddressTellerSettings.PostprocessEnabled = postprocessEnabled;
                AddressTellerSettings.PostprocessOrder = postprocessOrder;
                AddressTellerSettings.CleanupStaleEntries = cleanupStaleEntries;
                AddressTellerSettings.AutoCreateMissingGroups = autoCreateMissingGroups;
            }

            EditorGUILayout.Space(4);

            var managedGroups = CollectManagedGroups(overviewCache);
            s_managedGroupsFoldout = EditorGUILayout.Foldout(s_managedGroupsFoldout, $"Managed Groups ({managedGroups.Count})", true);
            if (s_managedGroupsFoldout)
            {
                EditorGUI.indentLevel++;
                if (managedGroups.Count == 0)
                    EditorGUILayout.LabelField("(No groups are referenced by enabled rules)", EditorStyles.wordWrappedMiniLabel);
                else
                    foreach (var groupName in managedGroups)
                        EditorGUILayout.LabelField(groupName, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }
            DrawDescription("Groups referenced by at least one enabled rule via Group(). These are the targets of \"Remove unmatched entries\" and \"Auto-create missing groups\". Entries manually registered in these groups will be removed if no rule matches them.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Registered Rules", EditorStyles.boldLabel);

            foreach (var duplicate in overviewCache.DuplicateOrders)
            {
                var ruleNames = string.Join(", ", duplicate.RuleClassNames);
                EditorGUILayout.HelpBox(
                    $"Order={duplicate.Order} is duplicated: {ruleNames}. Verify the evaluation order.",
                    MessageType.Warning);
            }

            // ルール一覧行の「このルールだけ」プレビューボタンと、末尾の運用アクションで共用する。
            var addressablesSettings = AddressableAssetSettingsDefaultObject.Settings;

            // 「このルールだけ Validate/Apply」ボタン用に、型からルールインスタンスを引けるようにしておく。
            // RuleCollector.CollectRules() 自体はキャッシュ済みのため、毎フレームの再構築コストは Dictionary 化のみ。
            var ruleInstancesByType = RuleCollector.CollectRules().ToDictionary(r => r.GetType());

            var overviewRules = overviewCache.Rules;
            if (overviewRules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No class inheriting AddressRuleBase was found.\n" +
                    "Inherit AddressRuleBase and define address/label rules in Configure().",
                    MessageType.Info);
            }

            foreach (var overview in overviewRules)
            {
                var type = overview.RuleType;
                var enabled = AddressTellerSettings.IsRuleEnabled(type.FullName);

                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                var toggled = EditorGUILayout.ToggleLeft(GUIContent.none, enabled, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck())
                    AddressTellerSettings.SetRuleEnabled(type.FullName, toggled);

                s_ruleFoldouts.TryGetValue(type, out var foldout);
                var newFoldout = EditorGUILayout.Foldout(foldout, GUIContent.none, true);
                if (newFoldout != foldout)
                    s_ruleFoldouts[type] = newFoldout;

                using (new EditorGUI.DisabledScope(!enabled))
                {
                    EditorGUILayout.LabelField($"{type.Name}  (Order: {overview.Order})", EditorStyles.wordWrappedLabel);
                }

                GUILayout.FlexibleSpace();

                var script = FindScriptForType(type);
                using (new EditorGUI.DisabledScope(script == null))
                {
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                        Selection.activeObject = script;
                }

                using (new EditorGUI.DisabledScope(addressablesSettings == null))
                {
                    if (GUILayout.Button("Validate/Apply this rule only", GUILayout.Width(180)))
                        AddressTellerScopedPreview.RunRulePreview(addressablesSettings, ruleInstancesByType[type]);
                }

                EditorGUILayout.EndHorizontal();

                if (newFoldout)
                    DrawRuleOverview(overview);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);

            if (addressablesSettings == null)
                EditorGUILayout.HelpBox("AddressableAssetSettings not found. Please initialize Addressables.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(addressablesSettings == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Run Validate"))
                    AddressTellerMenu.Validate();
                if (GUILayout.Button("Preview (with Validate)"))
                    AddressTellerMenu.ApplyWithValidate();
                if (GUILayout.Button("Run Apply"))
                    AddressTellerMenu.ApplyAll();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Snapshot", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var snapshotFolder = EditorGUILayout.TextField("Snapshot folder", AddressTellerSettings.SnapshotFolder);
            if (EditorGUI.EndChangeCheck())
                AddressTellerSettings.SnapshotFolder = snapshotFolder;

            DrawDescription("Relative path from the project root (parent directory of Assets). Default is \"AddressTellerSnapshots\" (outside Assets, not imported by Unity).");

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Browse...", GUILayout.Width(120)))
                PickSnapshotFolder();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            var autoSnapshotBeforeApplyAll = EditorGUILayout.ToggleLeft("Auto-snapshot before Apply", AddressTellerSettings.AutoSnapshotBeforeApplyAll);
            DrawDescription("Applies only to the Apply All / Apply with Validate menu actions. Auto-apply on import and CLI execution are not covered.");

            var autoSnapshotRetention = EditorGUILayout.IntField("Auto-snapshot retention count", AddressTellerSettings.AutoSnapshotRetention);
            DrawDescription("Auto snapshots exceeding this count are automatically deleted. Minimum is 1.");

            if (EditorGUI.EndChangeCheck())
            {
                AddressTellerSettings.AutoSnapshotBeforeApplyAll = autoSnapshotBeforeApplyAll;
                AddressTellerSettings.AutoSnapshotRetention = autoSnapshotRetention;
            }

            EditorGUILayout.Space();
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
        /// ルール一覧の Foldout 展開時に、Configure() の結果（Group/Where/Address/Label）を1行ずつ表示する。
        /// Configure() が例外を投げている場合はエントリの代わりにエラーを HelpBox で表示する。
        /// </summary>
        private static void DrawRuleOverview(in RuleClassOverview overview)
        {
            EditorGUI.indentLevel++;

            if (overview.ConfigureError != null)
            {
                EditorGUILayout.HelpBox($"An exception occurred during Configure(): {overview.ConfigureError}", MessageType.Error);
            }
            else if (overview.Entries.Count == 0)
            {
                EditorGUILayout.LabelField("(Group() was not called)", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                foreach (var entry in overview.Entries)
                {
                    var where = entry.Description ?? $"(condition #{entry.RuleIndex})";
                    var address = entry.HasAddress ? "dynamic" : "none";
                    var groupName = AddressRuleBuilderImpl.DisplayGroupName(entry.GroupName);
                    EditorGUILayout.LabelField(
                        $"Group: \"{groupName}\"  Where: \"{where}\"  Address: {address}  Labels: {entry.LabelCount}",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUI.indentLevel--;
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

        private static void DrawDescription(string text)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
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

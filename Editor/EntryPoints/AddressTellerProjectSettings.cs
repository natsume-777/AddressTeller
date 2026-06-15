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

            EditorGUILayout.LabelField("適用・検証の挙動", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var postprocessEnabled = EditorGUILayout.ToggleLeft("インポート時に自動適用する", AddressTellerSettings.PostprocessEnabled);
            DrawDescription("アセットのインポート・移動・削除のたびに ApplyAll を実行します。");

            EditorGUILayout.Space(4);

            var postprocessOrder = EditorGUILayout.IntField("Postprocessor の実行順序", AddressTellerSettings.PostprocessOrder);
            DrawDescription("AssetPostprocessor.GetPostprocessOrder() に渡される値です。値が小さいほど他の Postprocessor より先に実行されます。既定値は 1000（後段寄り）です。");

            EditorGUILayout.Space(4);

            var cleanupStaleEntries = EditorGUILayout.ToggleLeft("マッチしなくなったエントリを削除する", AddressTellerSettings.CleanupStaleEntries);
            DrawDescription("どのルールにもマッチしなくなったアセットを、AddressTeller が管理するグループから自動的に削除します。エントリ自体が削除されるため、アドレスと（Addressablesの）ラベルの両方が失われます。");

            EditorGUILayout.Space(4);

            var autoCreateMissingGroups = EditorGUILayout.ToggleLeft("存在しないグループを自動作成する", AddressTellerSettings.AutoCreateMissingGroups);
            DrawDescription("ルールが参照するグループが存在しない場合、Apply 実行時に DefaultGroup のスキーマ構成を複製して自動作成します。Validate/Predict では作成予定として表示するのみで、実際の作成は行いません。");

            if (EditorGUI.EndChangeCheck())
            {
                AddressTellerSettings.PostprocessEnabled = postprocessEnabled;
                AddressTellerSettings.PostprocessOrder = postprocessOrder;
                AddressTellerSettings.CleanupStaleEntries = cleanupStaleEntries;
                AddressTellerSettings.AutoCreateMissingGroups = autoCreateMissingGroups;
            }

            EditorGUILayout.Space(4);

            var managedGroups = CollectManagedGroups(overviewCache);
            s_managedGroupsFoldout = EditorGUILayout.Foldout(s_managedGroupsFoldout, $"管理対象グループ ({managedGroups.Count}件)", true);
            if (s_managedGroupsFoldout)
            {
                EditorGUI.indentLevel++;
                if (managedGroups.Count == 0)
                    EditorGUILayout.LabelField("(有効なルールが参照しているグループはありません)", EditorStyles.wordWrappedMiniLabel);
                else
                    foreach (var groupName in managedGroups)
                        EditorGUILayout.LabelField(groupName, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }
            DrawDescription("有効なルールがいずれかの Group() で参照しているグループです。マッチしなくなったエントリを削除する／存在しないグループを自動作成する の対象になります。これらのグループに手動で登録したエントリは、対応するルールがなければ削除対象になります。");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("登録されているルール", EditorStyles.boldLabel);

            foreach (var duplicate in overviewCache.DuplicateOrders)
            {
                var ruleNames = string.Join(", ", duplicate.RuleClassNames);
                EditorGUILayout.HelpBox(
                    $"Order={duplicate.Order} が重複: {ruleNames}。評価順序を確認してください",
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
                    "AddressRuleBase を継承したクラスが見つかりません。\n" +
                    "AddressRuleBase を継承し、Configure() でアドレス／ラベルのルールを定義してください。",
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
                    if (GUILayout.Button("選択", GUILayout.Width(60)))
                        Selection.activeObject = script;
                }

                using (new EditorGUI.DisabledScope(addressablesSettings == null))
                {
                    if (GUILayout.Button("このルールだけ Validate/Apply", GUILayout.Width(180)))
                        AddressTellerScopedPreview.RunRulePreview(addressablesSettings, ruleInstancesByType[type]);
                }

                EditorGUILayout.EndHorizontal();

                if (newFoldout)
                    DrawRuleOverview(overview);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("運用アクション", EditorStyles.boldLabel);

            if (addressablesSettings == null)
                EditorGUILayout.HelpBox("AddressableAssetSettings が見つかりません。Addressables を初期化してください。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(addressablesSettings == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Validate 実行"))
                    AddressTellerMenu.Validate();
                if (GUILayout.Button("プレビュー（Validate付き）"))
                    AddressTellerMenu.ApplyWithValidate();
                if (GUILayout.Button("Apply 実行"))
                    AddressTellerMenu.ApplyAll();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("スナップショット", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var snapshotFolder = EditorGUILayout.TextField("保存先フォルダ", AddressTellerSettings.SnapshotFolder);
            if (EditorGUI.EndChangeCheck())
                AddressTellerSettings.SnapshotFolder = snapshotFolder;

            DrawDescription("プロジェクトルート（Assets の親ディレクトリ）からの相対パス。既定値は \"AddressTellerSnapshots\"（Assets 外、Unity にインポートされない）。");

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("フォルダを選択...", GUILayout.Width(120)))
                PickSnapshotFolder();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            var autoSnapshotBeforeApplyAll = EditorGUILayout.ToggleLeft("Apply実行前に自動スナップショットを保存する", AddressTellerSettings.AutoSnapshotBeforeApplyAll);
            DrawDescription("対象は Apply All / Apply with Validate メニューのみです。import時の自動適用やCLIでの実行は対象外です。");

            var autoSnapshotRetention = EditorGUILayout.IntField("自動スナップショットの保持件数", AddressTellerSettings.AutoSnapshotRetention);
            DrawDescription("これを超える古い自動スナップショットは自動的に削除されます。最小値は1件です。");

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
                EditorGUILayout.HelpBox($"Configure() の実行中に例外が発生しました: {overview.ConfigureError}", MessageType.Error);
            }
            else if (overview.Entries.Count == 0)
            {
                EditorGUILayout.LabelField("(Group() が呼ばれていません)", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                foreach (var entry in overview.Entries)
                {
                    var where = entry.Description ?? $"(条件 #{entry.RuleIndex})";
                    var address = entry.HasAddress ? "動的" : "なし";
                    var groupName = AddressRuleBuilderImpl.DisplayGroupName(entry.GroupName);
                    EditorGUILayout.LabelField(
                        $"Group: \"{groupName}\"  Where: \"{where}\"  Address: {address}  Labels: {entry.LabelCount}個",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>フォルダ選択ダイアログを開き、選択結果をプロジェクトルートからの相対パスで保存する。</summary>
        private static void PickSnapshotFolder()
        {
            var current = AddressTellerSettings.GetSnapshotFolderAbsolutePath();
            var selected = EditorUtility.OpenFolderPanel("スナップショット保存先フォルダ", current, "");
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

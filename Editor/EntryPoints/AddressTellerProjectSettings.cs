using System.IO;
using UnityEditor;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// Project Settings ウィンドウに AddressTeller の設定項目・ルール一覧を表示する。
    /// </summary>
    internal static class AddressTellerProjectSettings
    {
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
            EditorGUILayout.LabelField("自動適用", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var postprocessEnabled = EditorGUILayout.ToggleLeft("インポート時に自動適用する", AddressTellerSettings.PostprocessEnabled);
            DrawDescription("アセットのインポート・移動・削除のたびに ApplyAll を実行します。");

            EditorGUILayout.Space(4);

            var cleanupStaleEntries = EditorGUILayout.ToggleLeft("マッチしなくなったエントリを削除する", AddressTellerSettings.CleanupStaleEntries);
            DrawDescription("どのルールにもマッチしなくなったアセットを、AddressTeller が管理するグループから自動的に削除します。ラベルは削除されません。");

            if (EditorGUI.EndChangeCheck())
            {
                AddressTellerSettings.PostprocessEnabled = postprocessEnabled;
                AddressTellerSettings.CleanupStaleEntries = cleanupStaleEntries;
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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("登録されているルール", EditorStyles.boldLabel);

            var rules = RuleCollector.CollectRules();
            if (rules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "AddressRuleBase を継承したクラスが見つかりません。\n" +
                    "AddressRuleBase を継承し、Configure() でアドレス／ラベルのルールを定義してください。",
                    MessageType.Info);
                return;
            }

            foreach (var rule in rules)
            {
                var type = rule.GetType();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{type.Name}  (Order: {rule.Order})", EditorStyles.wordWrappedLabel);
                GUILayout.FlexibleSpace();

                var script = FindScriptForType(type);
                using (new EditorGUI.DisabledScope(script == null))
                {
                    if (GUILayout.Button("選択", GUILayout.Width(60)))
                        Selection.activeObject = script;
                }

                EditorGUILayout.EndHorizontal();
            }
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

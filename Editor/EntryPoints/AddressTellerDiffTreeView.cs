using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// <see cref="DiffRow"/> の一覧を表示する TreeView。
    /// フラット行（depth=0）のみで親子関係は持たない。Removed 行は赤字で表示する。
    /// </summary>
    internal sealed class AddressTellerDiffTreeView : TreeView
    {
        private enum ColumnId
        {
            Kind,
            Asset,
            Address,
            Group,
            Labels,
        }

        private IReadOnlyList<DiffRow> _rows = System.Array.Empty<DiffRow>();

        public AddressTellerDiffTreeView(TreeViewState state, MultiColumnHeader header) : base(state, header)
        {
            rowHeight = 20f;
            showAlternatingRowBackgrounds = true;
            useScrollView = true;
            Reload();
        }

        /// <summary>表示する行データを設定し、ツリーを再構築する。</summary>
        public void SetRows(IReadOnlyList<DiffRow> rows)
        {
            _rows = rows ?? System.Array.Empty<DiffRow>();
            Reload();
        }

        public static MultiColumnHeaderState CreateHeaderState()
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("種別"),
                    width = 70,
                    minWidth = 50,
                    autoResize = false,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("アセット"),
                    width = 320,
                    minWidth = 120,
                    autoResize = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("アドレス"),
                    width = 220,
                    minWidth = 100,
                    autoResize = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("グループ"),
                    width = 140,
                    minWidth = 80,
                    autoResize = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("ラベル"),
                    width = 160,
                    minWidth = 80,
                    autoResize = true,
                },
            };

            return new MultiColumnHeaderState(columns);
        }

        protected override TreeViewItem BuildRoot()
        {
            // 行の id は 0 始まりで _rows のインデックスと対応させるため、
            // 隠しルートの id はそれと衝突しない -1 にする。
            return new TreeViewItem { id = -1, depth = -1, displayName = "Root" };
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            var rows = new List<TreeViewItem>(_rows.Count);

            for (var i = 0; i < _rows.Count; i++)
                rows.Add(new TreeViewItem(i, 0, _rows[i].AssetPath));

            root.children = rows.Count == 0
                ? new List<TreeViewItem> { new TreeViewItem(int.MaxValue, 0, "(差分なし)") }
                : rows;

            // 行データが空のとき root.children を空のままにすると TreeView 側で例外になるため、
            // ダミー行を1件入れておく。SelectionChanged/DoubleClick では id 範囲外として無視される。
            SetupParentsAndChildrenFromDepths(root, root.children);
            return root.children;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item.id < 0 || args.item.id >= _rows.Count)
            {
                base.RowGUI(args);
                return;
            }

            var row = _rows[args.item.id];
            var originalColor = GUI.color;

            if (row.Kind == DiffRowKind.Removed)
                GUI.color = Color.red;

            for (var i = 0; i < args.GetNumVisibleColumns(); i++)
            {
                var rect = args.GetCellRect(i);
                var columnId = (ColumnId)args.GetColumn(i);
                var text = GetCellText(row, columnId);
                EditorGUI.LabelField(rect, text);
            }

            GUI.color = originalColor;
        }

        private static string GetCellText(DiffRow row, ColumnId columnId)
        {
            switch (columnId)
            {
                case ColumnId.Kind:
                    return row.Kind.ToString();
                case ColumnId.Asset:
                    return row.AssetPath;
                case ColumnId.Address:
                    return row.Kind == DiffRowKind.Changed
                        ? $"{row.BeforeAddress} → {row.AfterAddress}"
                        : (row.Kind == DiffRowKind.Removed ? row.BeforeAddress : row.AfterAddress);
                case ColumnId.Group:
                    return row.Kind == DiffRowKind.Changed
                        ? $"{row.BeforeGroup} → {row.AfterGroup}"
                        : (row.Kind == DiffRowKind.Removed ? row.BeforeGroup : row.AfterGroup);
                case ColumnId.Labels:
                    return row.LabelsSummary;
                default:
                    return string.Empty;
            }
        }

        protected override void DoubleClickedItem(int id)
        {
            if (id < 0 || id >= _rows.Count) return;

            AddressTellerResultWindowUtility.PingAsset(_rows[id].AssetPath);
        }
    }
}

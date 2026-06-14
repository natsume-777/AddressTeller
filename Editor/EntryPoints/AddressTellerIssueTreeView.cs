using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="IssueRow"/> の一覧を表示する TreeView。
    /// フラット行（depth=0）のみで親子関係は持たない。
    /// 表示対象は <see cref="SetRows"/> に渡す時点でステータスフィルタ済みの行データを想定する。
    /// </summary>
    internal sealed class AddressTellerIssueTreeView : TreeView<int>
    {
        private enum ColumnId
        {
            Status,
            Asset,
            Message,
        }

        private IReadOnlyList<IssueRow> _rows = System.Array.Empty<IssueRow>();

        public AddressTellerIssueTreeView(TreeViewState<int> state, MultiColumnHeader header) : base(state, header)
        {
            rowHeight = 20f;
            showAlternatingRowBackgrounds = true;
            useScrollView = true;
            Reload();
        }

        /// <summary>表示する行データ（フィルタ適用後）を設定し、ツリーを再構築する。</summary>
        public void SetRows(IReadOnlyList<IssueRow> rows)
        {
            _rows = rows ?? System.Array.Empty<IssueRow>();
            Reload();
        }

        public static MultiColumnHeaderState CreateHeaderState()
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("ステータス"),
                    width = 140,
                    minWidth = 100,
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
                    headerContent = new GUIContent("詳細"),
                    width = 360,
                    minWidth = 150,
                    autoResize = true,
                },
            };

            return new MultiColumnHeaderState(columns);
        }

        protected override TreeViewItem<int> BuildRoot()
        {
            // 行の id は 0 始まりで _rows のインデックスと対応させるため、
            // 隠しルートの id はそれと衝突しない -1 にする。
            return new TreeViewItem<int> { id = -1, depth = -1, displayName = "Root" };
        }

        protected override IList<TreeViewItem<int>> BuildRows(TreeViewItem<int> root)
        {
            var rows = new List<TreeViewItem<int>>(_rows.Count);

            for (var i = 0; i < _rows.Count; i++)
                rows.Add(new TreeViewItem<int>(i, 0, _rows[i].AssetPath));

            root.children = rows.Count == 0
                ? new List<TreeViewItem<int>> { new TreeViewItem<int>(int.MaxValue, 0, "(問題なし)") }
                : rows;

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

            for (var i = 0; i < args.GetNumVisibleColumns(); i++)
            {
                var rect = args.GetCellRect(i);
                var columnId = (ColumnId)args.GetColumn(i);
                var text = GetCellText(row, columnId);
                EditorGUI.LabelField(rect, text);
            }
        }

        private static string GetCellText(IssueRow row, ColumnId columnId)
        {
            switch (columnId)
            {
                case ColumnId.Status:
                    return row.Status.ToString();
                case ColumnId.Asset:
                    return row.AssetPath;
                case ColumnId.Message:
                    return row.Message;
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

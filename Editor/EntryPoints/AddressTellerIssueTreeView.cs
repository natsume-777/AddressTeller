using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="IssueRow"/> の一覧を表示する TreeView。
    /// 親行（depth=0）は <see cref="IssueRow"/> 1件に対応する。
    /// <see cref="ValidationStatus.ConflictingAddress"/> で競合候補が2件以上ある行のみ、
    /// 各候補を子行（depth=1）として展開表示する。
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

        /// <summary>子行 id から (親行の _rows インデックス, 候補インデックス) への対応表。</summary>
        private readonly Dictionary<int, (int parentRowIndex, int candidateIndex)> _childRowMap = new();

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
            _childRowMap.Clear();

            // SetupParentsAndChildrenFromDepths は depth の連続した1本の平坦リストから
            // 親子関係を構築するため、親行と子行を depth 順にそのまま並べる。
            var flat = new List<TreeViewItem<int>>(_rows.Count);
            var expandedIds = new List<int>();

            // 親行の id は _rows のインデックス（0 ～ _rows.Count - 1）のまま。
            // 子行の id は _rows.Count 以降の別レンジを割り当て、_childRowMap で
            // (親行インデックス, 候補インデックス) へ逆引きする。
            var nextChildId = _rows.Count;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                flat.Add(new TreeViewItem<int>(i, 0, row.AssetPath));

                var candidates = row.ConflictingCandidates;
                if (candidates == null || candidates.Count < 2)
                    continue;

                for (var c = 0; c < candidates.Count; c++)
                {
                    var childId = nextChildId++;
                    _childRowMap[childId] = (i, c);
                    flat.Add(new TreeViewItem<int>(childId, 1, candidates[c].Address));
                }

                expandedIds.Add(i);
            }

            root.children = flat.Count == 0
                ? new List<TreeViewItem<int>> { new TreeViewItem<int>(int.MaxValue, 0, "(問題なし)") }
                : flat;

            SetupParentsAndChildrenFromDepths(root, root.children);

            // 競合候補を持つ行は初期状態で展開しておく。
            if (expandedIds.Count > 0)
                state.expandedIDs = expandedIds;

            return base.BuildRows(root);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var id = args.item.id;

            if (_childRowMap.TryGetValue(id, out var childRef))
            {
                var parentRow = _rows[childRef.parentRowIndex];
                var candidate = parentRow.ConflictingCandidates[childRef.candidateIndex];

                for (var i = 0; i < args.GetNumVisibleColumns(); i++)
                {
                    var rect = args.GetCellRect(i);
                    var columnId = (ColumnId)args.GetColumn(i);
                    var text = columnId == ColumnId.Message
                        ? $"{candidate.DescribeSource()} → {AddressRuleBuilderImpl.DisplayGroupName(candidate.GroupName)} / {candidate.Address}"
                        : string.Empty;
                    EditorGUI.LabelField(rect, text);
                }

                return;
            }

            if (id < 0 || id >= _rows.Count)
            {
                base.RowGUI(args);
                return;
            }

            var row = _rows[id];

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
            if (_childRowMap.TryGetValue(id, out var childRef))
            {
                AddressTellerResultWindowUtility.PingAsset(_rows[childRef.parentRowIndex].AssetPath);
                return;
            }

            if (id < 0 || id >= _rows.Count) return;

            AddressTellerResultWindowUtility.PingAsset(_rows[id].AssetPath);
        }
    }
}

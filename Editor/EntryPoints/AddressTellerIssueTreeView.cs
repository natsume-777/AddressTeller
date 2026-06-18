using System.Collections.Generic;
using UnityEngine.UIElements;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="IssueRow"/> の一覧を表示する MultiColumnTreeView ラッパー。
    /// 親行（depth=0）は <see cref="IssueRow"/> 1件に対応する。
    /// <see cref="ValidationStatus.ConflictingAddress"/> で競合候補が2件以上ある行のみ、
    /// 各候補を子行として展開表示する。
    /// 表示対象は <see cref="SetRows"/> に渡す時点でステータスフィルタ済みの行データを想定する。
    /// </summary>
    internal sealed class AddressTellerIssueTreeView : MultiColumnTreeView
    {
        // 子行の行データとして候補1件を保持するための内部型
        private readonly struct CandidateRow
        {
            public AddressCandidate Candidate { get; }
            public CandidateRow(AddressCandidate candidate) { Candidate = candidate; }
        }

        // 親行と子行を同じ MultiColumnTreeView で表示するため、
        // セルバインド時にどちらの型かを object で受け取り分岐する。
        // TreeViewItemData は struct なので共通型を object で格納できないが、
        // id をキーに _rowById / _candidateById で引き直す方式を使う。

        private readonly Dictionary<int, IssueRow> _rowById = new();
        private readonly Dictionary<int, CandidateRow> _candidateById = new();
        // 子行 id → 親行 id の逆引きマップ
        private readonly Dictionary<int, int> _parentById = new();

        private IReadOnlyList<IssueRow> _rows = System.Array.Empty<IssueRow>();

        public AddressTellerIssueTreeView()
        {
            itemsChosen += _ => OnItemChosen();

            columns.Add(new Column { name = "status",  title = "Status",  width = 140, minWidth = 100 });
            columns.Add(new Column { name = "asset",   title = "Asset",   width = 320, minWidth = 120, stretchable = true });
            columns.Add(new Column { name = "details", title = "Details", width = 360, minWidth = 150, stretchable = true });

            columns["status"].makeCell  = MakeCell;
            columns["asset"].makeCell   = MakeCell;
            columns["details"].makeCell = MakeCell;

            columns["status"].bindCell  = (e, i) => BindStatusCell(e, i);
            columns["asset"].bindCell   = (e, i) => BindAssetCell(e, i);
            columns["details"].bindCell = (e, i) => BindDetailsCell(e, i);

            showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly;
            style.flexGrow = 1;
        }

        /// <summary>表示する行データ（フィルタ適用後）を設定し、ツリーを再構築する。</summary>
        public void SetRows(IReadOnlyList<IssueRow> rows)
        {
            _rows = rows ?? System.Array.Empty<IssueRow>();
            _rowById.Clear();
            _candidateById.Clear();
            _parentById.Clear();

            var rootItems = new List<TreeViewItemData<int>>(_rows.Count);

            // 親行の id は _rows のインデックス（0 ～ Count-1）を使う。
            // 子行の id は Count 以降の別レンジを割り当て、_candidateById で逆引きする。
            var nextChildId = _rows.Count;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                _rowById[i] = row;

                var candidates = row.ConflictingCandidates;
                if (candidates != null && candidates.Count >= 2)
                {
                    var children = new List<TreeViewItemData<int>>(candidates.Count);
                    for (var c = 0; c < candidates.Count; c++)
                    {
                        var childId = nextChildId++;
                        _candidateById[childId] = new CandidateRow(candidates[c]);
                        _parentById[childId] = i;
                        children.Add(new TreeViewItemData<int>(childId, childId));
                    }
                    rootItems.Add(new TreeViewItemData<int>(i, i, children));
                }
                else
                {
                    rootItems.Add(new TreeViewItemData<int>(i, i));
                }
            }

            SetRootItems(rootItems);
            Rebuild();

            // 競合候補を持つ親行は初期状態で展開しておく
            for (var i = 0; i < _rows.Count; i++)
            {
                var candidates = _rows[i].ConflictingCandidates;
                if (candidates != null && candidates.Count >= 2)
                    ExpandItem(i, false);
            }
        }

        private static VisualElement MakeCell()
        {
            return new Label { style = { paddingLeft = 4, unityTextAlign = UnityEngine.TextAnchor.MiddleLeft, whiteSpace = WhiteSpace.NoWrap } };
        }

        private void BindStatusCell(VisualElement element, int index)
        {
            var label = (Label)element;
            var id = GetIdForIndex(index);

            if (_rowById.TryGetValue(id, out var row))
                label.text = row.Status.ToString();
            else
                label.text = string.Empty;
        }

        private void BindAssetCell(VisualElement element, int index)
        {
            var label = (Label)element;
            var id = GetIdForIndex(index);

            if (_rowById.TryGetValue(id, out var row))
                label.text = row.AssetPath;
            else
                label.text = string.Empty;
        }

        private void BindDetailsCell(VisualElement element, int index)
        {
            var label = (Label)element;
            var id = GetIdForIndex(index);

            if (_rowById.TryGetValue(id, out var row))
            {
                label.text = row.Message;
            }
            else if (_candidateById.TryGetValue(id, out var cand))
            {
                var c = cand.Candidate;
                label.text = $"{c.DescribeSource()} → {AddressRuleBuilderImpl.DisplayGroupName(c.GroupName)} / {c.Address}";
            }
            else
            {
                label.text = string.Empty;
            }
        }

        /// <summary>flat index に対応する item id を取得する（BaseTreeView の公開メソッドを使う）。</summary>
        private new int GetIdForIndex(int index) => base.GetIdForIndex(index);

        private void OnItemChosen()
        {
            var id = GetIdForIndex(selectedIndex);

            if (_rowById.TryGetValue(id, out var row))
            {
                AddressTellerResultWindowUtility.PingAsset(row.AssetPath);
                return;
            }

            // 子行から親行のアセットを ping する（_parentById で逆引き）
            if (_candidateById.ContainsKey(id) && _parentById.TryGetValue(id, out var parentId))
            {
                if (_rowById.TryGetValue(parentId, out var parentRow))
                    AddressTellerResultWindowUtility.PingAsset(parentRow.AssetPath);
            }
        }
    }
}

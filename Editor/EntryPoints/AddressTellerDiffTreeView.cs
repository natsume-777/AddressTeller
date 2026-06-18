using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AddressTeller.Editor
{
    /// <summary>
    /// <see cref="DiffRow"/> の一覧を表示する MultiColumnTreeView ラッパー。
    /// フラット行（子なし）のみで親子関係は持たない。Removed 行は赤字で表示する。
    /// </summary>
    internal sealed class AddressTellerDiffTreeView : MultiColumnTreeView
    {
        private IReadOnlyList<DiffRow> _rows = System.Array.Empty<DiffRow>();

        public AddressTellerDiffTreeView()
        {
            // 行ダブルクリック（決定）で対象アセットを ping する
            onItemsChosen += _ => OnItemChosen();

            columns.Add(new Column { name = "kind",    title = "Type",    width = 70,  minWidth = 50 });
            columns.Add(new Column { name = "asset",   title = "Asset",   width = 320, minWidth = 120, stretchable = true });
            columns.Add(new Column { name = "address", title = "Address", width = 220, minWidth = 100, stretchable = true });
            columns.Add(new Column { name = "group",   title = "Group",   width = 140, minWidth = 80,  stretchable = true });
            columns.Add(new Column { name = "labels",  title = "Labels",  width = 160, minWidth = 80,  stretchable = true });

            columns["kind"].makeCell    = MakeCell;
            columns["asset"].makeCell   = MakeCell;
            columns["address"].makeCell = MakeCell;
            columns["group"].makeCell   = MakeCell;
            columns["labels"].makeCell  = MakeCell;

            columns["kind"].bindCell    = (e, i) => BindCell(e, i, "kind");
            columns["asset"].bindCell   = (e, i) => BindCell(e, i, "asset");
            columns["address"].bindCell = (e, i) => BindCell(e, i, "address");
            columns["group"].bindCell   = (e, i) => BindCell(e, i, "group");
            columns["labels"].bindCell  = (e, i) => BindCell(e, i, "labels");

            showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly;
            style.flexGrow = 1;
        }

        /// <summary>表示する行データを設定し、ツリーを再構築する。</summary>
        public void SetRows(IReadOnlyList<DiffRow> rows)
        {
            _rows = rows ?? System.Array.Empty<DiffRow>();

            // id = _rows インデックスのフラットリストを構築する
            var rootItems = new List<TreeViewItemData<int>>(_rows.Count);
            for (var i = 0; i < _rows.Count; i++)
                rootItems.Add(new TreeViewItemData<int>(i, i));

            SetRootItems(rootItems);
            Rebuild();
        }

        private static VisualElement MakeCell()
        {
            return new Label { style = { paddingLeft = 4, unityTextAlign = TextAnchor.MiddleLeft } };
        }

        private void BindCell(VisualElement element, int index, string columnName)
        {
            var label = (Label)element;
            // id は SetRootItems で割り当てた _rows インデックスと一致する
            var id = GetIdForIndex(index);
            if (id < 0 || id >= _rows.Count)
            {
                label.text = string.Empty;
                label.style.color = StyleKeyword.Null;
                return;
            }
            var row = _rows[id];

            label.text = columnName switch
            {
                "kind"    => row.Kind.ToString(),
                "asset"   => row.AssetPath,
                "address" => row.Kind == DiffRowKind.Changed
                    ? $"{row.BeforeAddress} → {row.AfterAddress}"
                    : (row.Kind == DiffRowKind.Removed ? row.BeforeAddress : row.AfterAddress),
                "group"   => row.Kind == DiffRowKind.Changed
                    ? $"{row.BeforeGroup} → {row.AfterGroup}"
                    : (row.Kind == DiffRowKind.Removed ? row.BeforeGroup : row.AfterGroup),
                "labels"  => row.LabelsSummary,
                _         => string.Empty,
            };

            label.style.color = row.Kind == DiffRowKind.Removed ? Color.red : StyleKeyword.Null;
        }

        private void OnItemChosen()
        {
            if (selectedIndex < 0) return;
            var id = GetIdForIndex(selectedIndex);
            if (id < 0 || id >= _rows.Count) return;
            AddressTellerResultWindowUtility.PingAsset(_rows[id].AssetPath);
        }
    }
}

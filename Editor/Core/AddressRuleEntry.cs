using System;
using System.Collections.Generic;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// Configure() で組み立てられたグループ単位のルール1件。
    /// </summary>
    public sealed class AddressRuleEntry
    {
        public string GroupName { get; }
        public Func<AssetContext, bool> Predicate { get; }

        /// <summary>null の場合はこのルールでアドレスを付与しない。</summary>
        public Func<AssetContext, string> AddressSelector { get; }

        public IReadOnlyList<Func<AssetContext, string>> LabelSelectors { get; }

        public AddressRuleEntry(
            string groupName,
            Func<AssetContext, bool> predicate,
            Func<AssetContext, string> addressSelector,
            IReadOnlyList<Func<AssetContext, string>> labelSelectors)
        {
            GroupName = groupName ?? throw new ArgumentNullException(nameof(groupName));
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            AddressSelector = addressSelector;
            LabelSelectors = labelSelectors ?? Array.Empty<Func<AssetContext, string>>();
        }
    }
}

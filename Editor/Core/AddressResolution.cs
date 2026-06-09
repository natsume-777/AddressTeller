using System.Collections.Generic;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// アドレス候補1件（グループ名 + アドレス文字列）。
    /// </summary>
    public readonly struct AddressCandidate
    {
        public string GroupName { get; }
        public string Address { get; }
        public string SourceClass { get; }
        public string Description { get; }

        public AddressCandidate(string groupName, string address, string sourceClass = null, string description = null)
        {
            GroupName = groupName;
            Address = address;
            SourceClass = sourceClass;
            Description = description;
        }
    }

    /// <summary>
    /// 1アセットに対するルール評価結果。
    /// AddressCandidates が 2 件以上のとき競合。0 件のときこのアセットは対象外。
    /// Labels は全マッチルールから蓄積される。
    /// </summary>
    public sealed class AddressResolution
    {
        /// <summary>アドレスを発行したルールのグループ名＋アドレスのリスト。</summary>
        public IReadOnlyList<AddressCandidate> AddressCandidates { get; }

        /// <summary>全マッチルールから蓄積されたラベルセット。</summary>
        public IReadOnlyCollection<string> Labels { get; }

        public AddressResolution(
            IReadOnlyList<AddressCandidate> candidates,
            IReadOnlyCollection<string> labels)
        {
            AddressCandidates = candidates;
            Labels = labels;
        }
    }
}

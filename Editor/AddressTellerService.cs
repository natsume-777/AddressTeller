using System.Collections.Generic;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// ルールを収集して Addressables へ適用するエディタ側サービス。
    /// 現状はワークスペース構成検証用のスタブ。今後の実装で本体ロジックを追加する。
    /// </summary>
    public static class AddressTellerService
    {
        /// <summary>登録された全ルールを適用する（未実装スタブ）。</summary>
        public static void ApplyAll(IEnumerable<AddressRuleBase> rules)
        {
            // TODO: Addressables への適用ロジックを実装する。
        }
    }
}

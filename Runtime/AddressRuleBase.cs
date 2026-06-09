using System;

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// ルール定義の基底クラス。利用者はこれを継承し、C# でアドレス／ラベル付与ルールを記述する。
    /// </summary>
    public abstract class AddressRuleBase
    {
        /// <summary>評価順序。小さいほど先に評価される。</summary>
        public virtual int Order => 0;

        /// <summary>ルールを組み立てる。</summary>
        public abstract void Configure(IAddressRuleBuilder rules);
    }

    /// <summary>
    /// ルール組み立て用ビルダー。Group() でグループルールを追加する。
    /// </summary>
    public interface IAddressRuleBuilder
    {
        IAddressRuleGroupBuilder Group(string groupName);
    }

    /// <summary>
    /// グループ単位のルール設定。Where / Address / Label をチェーンで記述する。
    /// </summary>
    public interface IAddressRuleGroupBuilder
    {
        IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate);
        IAddressRuleGroupBuilder Address(Func<AssetContext, string> selector);
        IAddressRuleGroupBuilder Address(string address);
        IAddressRuleGroupBuilder Label(Func<AssetContext, string> selector);
        IAddressRuleGroupBuilder Label(string label);
    }
}

namespace Natsume777.AddressTeller
{
    /// <summary>
    /// ルール定義の基底クラス。利用者はこれを継承し、C# でアドレス／ラベル付与ルールを記述する。
    /// 詳細な API は今後の実装で拡張される（現状はワークスペース構成検証用のスタブ）。
    /// </summary>
    public abstract class AddressRuleBase
    {
        /// <summary>評価順序。小さいほど先に評価される。</summary>
        public virtual int Order => 0;

        /// <summary>ルールを組み立てる。</summary>
        public abstract void Configure(IAddressRuleBuilder rules);
    }

    /// <summary>
    /// ルール組み立て用ビルダー。現状はスタブ。
    /// </summary>
    public interface IAddressRuleBuilder
    {
    }
}

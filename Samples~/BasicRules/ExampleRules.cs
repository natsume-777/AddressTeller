using Natsume777.AddressTeller;

namespace AddressTellerSamples
{
    /// <summary>
    /// アドレス／ラベル付与ルールの最小サンプル。
    /// Package Manager の Samples からインポートして利用する。
    /// </summary>
    public sealed class ExampleRules : AddressRuleBase
    {
        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // ここにルールを記述する（API は実装中）。
        }
    }
}

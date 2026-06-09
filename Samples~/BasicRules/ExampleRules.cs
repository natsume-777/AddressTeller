using Natsume777.AddressTeller;
using UnityEngine;

namespace AddressTellerSamples
{
    /// <summary>
    /// アドレス／ラベル付与ルールの最小サンプル。
    /// Package Manager の Samples からインポートして利用する。
    ///
    /// 前提:
    ///   - Addressable Groups に "MyGroup" という名前のグループが存在すること
    ///   - Assets/Demo/Characters/ と Assets/Demo/Items/ にアセットを配置すること
    /// </summary>
    public sealed class ExampleRules : AddressRuleBase
    {
        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // Characters/ 以下の Prefab
            // アドレス: ファイル名（拡張子なし）
            // ラベル: "character" と "humanoid" の2つを付与
            rules.Group("MyGroup")
                .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Characters/")
                           && ctx.Type == typeof(GameObject))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("character")
                .Label("humanoid");

            // Items/ 以下の Prefab
            // ラベル: "item" のみ
            rules.Group("MyGroup")
                .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Items/")
                           && ctx.Type == typeof(GameObject))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("item");
        }
    }
}

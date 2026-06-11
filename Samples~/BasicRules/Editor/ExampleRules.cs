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
    ///
    /// 配置に関する注意:
    ///   AddressRuleBase 等は Editor 専用アセンブリの型なので、
    ///   このスクリプトは "Editor" という名前のフォルダ配下に置く必要がある。
    /// </summary>
    public sealed class ExampleRules : AddressRuleBase
    {
        // 評価順序。複数のルールクラスがある場合、小さい値から先に評価される。
        // 同じ Order 値を持つクラスが複数あると Apply All / Validate 実行時に警告が出る。
        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // 1つの Configure() の中で Group() を複数回呼び、複数のルールエントリを定義できる。

            // Characters/ 以下の Prefab
            rules.Group("MyGroup")
                // Where() は1グループにつき1回のみ呼び出し可能。複数条件は && でまとめる。
                .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Characters/")
                           && ctx.Type == typeof(GameObject))
                // アドレス: ファイル名（拡張子なし）
                .Address(ctx => ctx.FileNameWithoutExtension)
                // Label() は何度でも呼び出せる。マッチした全ルールのラベルが蓄積される。
                .Label("character")
                .Label("humanoid");

            // Items/ 以下の Prefab
            // Address() を呼ばないグループルールも定義できる（その場合アドレスは発行されず、ラベル付与のみ行われる）。
            // ここでは Address() を呼んでいるので、アドレスはファイル名（拡張子なし）になる。
            rules.Group("MyGroup")
                .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Items/")
                           && ctx.Type == typeof(GameObject))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("item");
        }
    }
}

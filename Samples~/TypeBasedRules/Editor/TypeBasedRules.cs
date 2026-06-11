using Natsume777.AddressTeller;
using UnityEngine;

namespace AddressTellerSamples
{
    /// <summary>
    /// アセットの型ごとにグループ・ラベルを振り分けるサンプル。
    /// Package Manager の Samples からインポートして利用する。
    ///
    /// 前提:
    ///   - Addressable Groups に "Prefabs" / "Textures" / "Audio" / "Configs" の
    ///     4つのグループが存在すること
    ///   - Assets/Demo/ 以下に各種アセットを配置すること
    /// </summary>
    public sealed class TypeBasedRules : AddressRuleBase
    {
        private const string RootPath = "Assets/Demo/";

        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // Prefab（GameObject）-> Prefabs グループ、ラベル "prefab"
            rules.Group("Prefabs")
                .Where(ctx => ctx.Path.StartsWith(RootPath) && ctx.Type == typeof(GameObject))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("prefab");

            // テクスチャ -> Textures グループ、ラベル "texture"
            rules.Group("Textures")
                .Where(ctx => ctx.Path.StartsWith(RootPath) && ctx.Type == typeof(Texture2D))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("texture");

            // オーディオクリップ -> Audio グループ、ラベル "audio"
            rules.Group("Audio")
                .Where(ctx => ctx.Path.StartsWith(RootPath) && ctx.Type == typeof(AudioClip))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("audio");

            // ScriptableObject の派生クラス全般 -> Configs グループ、ラベル "config"
            // ctx.Type は厳密な型なので、派生クラスもまとめて対象にする場合は
            // == ではなく IsAssignableFrom で判定する。
            rules.Group("Configs")
                .Where(ctx => ctx.Path.StartsWith(RootPath)
                           && typeof(ScriptableObject).IsAssignableFrom(ctx.Type))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("config");
        }
    }
}

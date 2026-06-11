using Natsume777.AddressTeller;

namespace AddressTellerSamples
{
    /// <summary>
    /// フォルダ階層をそのままアドレス・ラベルに反映するサンプル。
    /// Package Manager の Samples からインポートして利用する。
    ///
    /// 前提:
    ///   - Addressable Groups に "FolderAssets" という名前のグループが存在すること
    ///   - Assets/Demo/ 以下のサブフォルダにアセットを配置すること
    ///     （例: Assets/Demo/Characters/Enemies/Goblin.prefab）
    /// </summary>
    public sealed class FolderBasedRules : AddressRuleBase
    {
        private const string RootPath = "Assets/Demo/";

        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // Assets/Demo/ 以下、サブフォルダに置かれたアセットすべてが対象
            rules.Group("FolderAssets")
                .Where(ctx => ctx.Path.StartsWith(RootPath)
                           && ctx.Path.Substring(RootPath.Length).Contains("/"))
                // アドレス: "Assets/Demo/" を除いた拡張子なしパス
                // 例: "Assets/Demo/Characters/Enemies/Goblin.prefab" -> "Characters/Enemies/Goblin"
                .Address(ctx => ctx.Directory.Substring(RootPath.Length - 1).TrimStart('/')
                             + "/" + ctx.FileNameWithoutExtension)
                // ラベル: Assets/Demo/ 直下のフォルダ名
                // 例: "Assets/Demo/Characters/Enemies/Goblin.prefab" -> "Characters"
                .Label(ctx => ctx.Path.Substring(RootPath.Length).Split('/')[0]);
        }
    }
}

# AddressTeller

Unity Addressables のアドレス・ラベルを **C# コードで** 自動付与するツール。
ScriptableObject / UI ではなく、コードファーストでルールを定義します。

## 要件

- Unity 6000.0 以降
- com.unity.addressables 2.8.1 以降

## インストール

Package Manager の `Add package from git URL...` で次を指定します。

```
https://github.com/natsume-777/AddressTeller.git
```

または `Packages/manifest.json` に直接追記します。

```json
{
  "dependencies": {
    "com.natsume777.addressteller": "https://github.com/natsume-777/AddressTeller.git"
  }
}
```

## クイックスタート

`AddressRuleBase` を継承したクラスを `Editor` フォルダ配下に置くと、リフレクションで自動的に収集されます。
`Configure()` 内で `Group().Where().Address().Label()` をチェーンしてルールを記述します。

```csharp
using AddressTeller;
using UnityEngine;

public sealed class GameAddressRules : AddressRuleBase
{
    public override int Order => 0;

    public override void Configure(IAddressRuleBuilder rules)
    {
        rules.Group("Characters")
            .Where(ctx => ctx.Path.StartsWith("Assets/Game/Characters/")
                       && ctx.Type == typeof(GameObject))
            .Address(ctx => ctx.FileNameWithoutExtension)   // "Player"
            .Label("character");
    }
}
```

`Tools/AddressTeller/Apply All` を実行すると、対象アセットにアドレスとラベルが設定されます。
`Characters` グループは事前に Addressable Groups ウィンドウで作成しておく必要があります（存在しないグループ名はエラーになります）。

## ドキュメント

- [ルールの書き方](Documentation~/writing-rules.md) — `AddressRuleBase` の書き方、`Match`/`Naming` ヘルパー、`AssetContext`、評価ルールの詳細
- [適用と運用](Documentation~/operations.md) — 適用方法、CI 連携、Project Settings、スナップショット、サンプル
- [設計上の決定事項](Documentation~/design-decisions.md) — アドレス・ラベル・グループの扱いをこう決めた理由
- [アーキテクチャ](Documentation~/architecture.md) — レイヤ構成・フォルダ別の責務・ルール評価の流れ
- [コントリビュート](CONTRIBUTING.md)

## 背景

アドレス・ラベルの設計はエンジニアが担うことが多い。にもかかわらず、ScriptableObject + Inspector UI でルールを定義する方式は「UI で表現できることが表現の上限」になりやすく、グループを GUID で参照する保存形式はリネームや削除で壊れ、差分も読みにくい。

AddressTeller はルール定義を C# コードに寄せることで次を狙う。

- **表現力の上限がない** — パス解析・外部データ読み込み・任意の C# ロジックが書ける
- **差分が綺麗** — `.asset` を持たず、コードだけ管理すればよい
- **IDE 支援が効く** — 補完・リファクタリング・ユニットテストが普通の C# として使える

データ駆動にしたい場合も、読み込み方式をコード側で自由に選べる。

## ライセンス

[MIT](LICENSE)

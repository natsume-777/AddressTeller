# AddressTeller

Unity Addressables のアドレス・ラベルを **C# コードで** 自動付与するツール。
ScriptableObject / UI ではなく、コードファーストでルールを定義します。

## 要件

- Unity 6000.0 以降
- com.unity.addressables 2.x

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

## 使い方

`AddressRuleBase` を継承し、ルールを記述します（API は実装中）。

```csharp
public sealed class GameAddressRules : AddressRuleBase
{
    public override int Order => 0;

    public override void Configure(IAddressRuleBuilder rules)
    {
        // ルール定義（実装予定）
    }
}
```

## 背景

アドレス・ラベルの設計はエンジニアが担うことが多い。にもかかわらず、ScriptableObject + Inspector UI でルールを定義する方式は「UI で表現できることが表現の上限」になりやすく、グループを GUID で参照する保存形式はリネームや削除で壊れ、差分も読みにくい。

AddressTeller はルール定義を C# コードに寄せることで次を狙う。

- **表現力の上限がない** — パス解析・外部データ読み込み・任意の C# ロジックが書ける
- **差分が綺麗** — `.asset` を持たず、コードだけ管理すればよい
- **IDE 支援が効く** — 補完・リファクタリング・ユニットテストが普通の C# として使える

データ駆動にしたい場合も、読み込み方式をコード側で自由に選べる。

## ライセンス

[MIT](LICENSE)

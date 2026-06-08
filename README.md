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

詳しい設計背景は [DESIGN.md](https://github.com/natsume-777/AddressTeller) を参照してください。

## ライセンス

[MIT](LICENSE)

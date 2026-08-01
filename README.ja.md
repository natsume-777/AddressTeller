[English](./README.md)

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

Addressables に不慣れな方向けに用語を一言で説明すると、**アドレス**は実行時にアセットをロードする際に使う文字列キー（`Addressables.LoadAssetAsync<GameObject>("Player")`）、**ラベル**はアドレスをまたいでアセットを絞り込み・分類するための自由記述タグ、**グループ**はアセットのバンドル方法（分割・圧縮方針）を決める Addressables 上の入れ物です。AddressTeller をインストールするとパッケージ依存として `com.unity.addressables` も一緒に導入されますが、Addressables 自体の初期化（設定アセットの作成）はプロジェクトごとに一度別途必要です。`Window > Asset Management > Addressables > Groups` を開き、案内が出たら設定を作成してください。

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

`Tools/AddressTeller/Apply All` を実行すると、対象アセットにアドレスとラベルが設定されます。`Apply All` は Addressable エントリ自体の作成・移動も行うため、あらかじめ各アセットを手動で Addressable 化しておく必要はありません。ただし `Characters` グループは事前に存在している必要があります。`Window > Asset Management > Addressables > Groups` で作成しておいてください（存在しないグループ名はエラーになり、既定では自動作成はされません）。

`Where()` の条件が、任せるつもりのないグループのアセットにマッチしない限り、AddressTeller が**エントリを削除したりラベルを足したりする**のは、いずれかのルールが参照しているグループに属するエントリだけです。そのため既存の Addressables 環境に対して、グループ単位で部分的に導入していくこともできます。ただし、ルールに**マッチしたアセットは**現在どのグループに属していても（手動で管理しているグループであっても）無条件にそのルールのグループへ**移動**されるため、`Where()` の範囲は AddressTeller に任せたいアセットに絞ってください。詳しくは [設計上の決定事項](Documentation~/design-decisions.ja.md#削除は資産単位の所有権で判定する) を参照してください。

既定では `AssetPostprocessor` により、アセットのインポート・移動・削除のたびにルール評価が自動的に再実行されます（インポート時自動適用、既定 ON）。そのため、ルールを定義した後は日常的なアセットインポートだけで自動適用が走ることがあります。これには、どのルールにもマッチしなくなったエントリの削除（`CleanupStaleEntries`、こちらも既定 ON）も含まれます。詳しくは [設計上の決定事項](Documentation~/design-decisions.ja.md#削除は資産単位の所有権で判定する) を参照してください。いずれの設定も Project Settings でオフにできます。適用方法一覧やこれらの設定については [適用と運用](Documentation~/operations.ja.md) を参照してください。

Addressables の DefaultGroup に付与したい場合は `Group("名前")` の代わりに `GroupDefault()` を使えます（DefaultGroup のリネームに追従します）。詳しくは [ルールの書き方](Documentation~/writing-rules.ja.md#groupdefault) を参照してください。

ルールクラスを独自の asmdef 内に定義する場合、その asmdef の `references` に `AddressTeller.Core` と `AddressTeller.Editor` の両方を追加してください（公開 API が両方のアセンブリ型を露出しているため）。

## ドキュメント

- [ルールの書き方](Documentation~/writing-rules.ja.md) — `AddressRuleBase` の書き方、`Match`/`Naming` ヘルパー、`AssetContext`、評価ルールの詳細
- [適用と運用](Documentation~/operations.ja.md) — 適用方法、CI 連携、Project Settings、スナップショット、サンプル
- [設計上の決定事項](Documentation~/design-decisions.ja.md) — アドレス・ラベル・グループの扱いをこう決めた理由
- [アーキテクチャ](Documentation~/architecture.ja.md) — レイヤ構成・フォルダ別の責務・ルール評価の流れ
- [互換性ポリシー](Documentation~/compatibility.ja.md) — SemVer の保証対象・対象外（CI に組み込む、バージョンを上げる、といった段階で読めば十分です。まず試してみたいだけなら後回しでかまいません）
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

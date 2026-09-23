[English](./README.md)

# AddressTeller

Unity Addressables のアドレス・ラベルを **C# コードで** 付与するツール。ON にすれば自動で追従させることもできます。
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

上記はいずれも既定ブランチを追跡するため、後から `Update` すると破壊的変更が入る可能性があります。
リリース版に固定するには、URL の末尾にリリースタグを付けてください。バージョン `0.x` の間は
[互換性ポリシー](Documentation~/compatibility.ja.md)はまだ発効していません。マイナーリリースでも破壊的変更が入りうるため、タグ固定を強く推奨します。

```
https://github.com/natsume-777/AddressTeller.git#v<X.Y.Z>
```

利用可能なタグは [GitHub Releases](https://github.com/natsume-777/AddressTeller/releases) で確認してください。

**注:** リリースタグは 0.4.0 以降にのみ存在するため、タグによるバージョン固定は 0.4.0 以降が対象です。

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

なお `ctx.FileNameWithoutExtension` は、別フォルダに同名ファイルがあると同じアドレスになります。`Validate` / `Apply All` はそれをアドレス重複として報告します（[ルールの書き方](Documentation~/writing-rules.ja.md#評価ルールと挙動) を参照）。

`Tools/AddressTeller/Apply All` を実行すると、対象アセットにアドレスとラベルが設定されます。`Apply All` は Addressable エントリ自体の作成・移動も行うため、あらかじめ各アセットを手動で Addressable 化しておく必要はありません。ただし `Characters` グループは事前に存在している必要があります。`Window > Asset Management > Addressables > Groups` で作成しておいてください（存在しないグループ名はエラーになり、既定では自動作成はされません）。書き込みの前に、`Apply All` は変更内容を要約した確認ダイアログを表示し、復元可能な自動セーフティスナップショットを保存します（`Tools/AddressTeller/Undo Last Apply` で復元）。

`Where()` の条件が、任せるつもりのないグループのアセットにマッチしない限り、AddressTeller が**エントリを削除する**のは、いずれかのルールが `Address()` を宣言しているグループだけで、かつそれを有効にした場合に限ります（後述の手順3）——そのグループ内では、手動で登録したエントリでもどのルールにもマッチしなくなれば削除されます。そのため既存の Addressables 環境に対して、グループ単位で部分的に導入していくこともできます。ただし、ルールに**マッチしたアセットは**現在どのグループに属していても（手動で管理しているグループであっても）無条件にそのルールのグループへ**移動**されるため、`Where()` の範囲は AddressTeller に任せたいアセットに絞ってください。詳しくは [設計上の決定事項](Documentation~/design-decisions.ja.md#削除は資産単位の所有権で判定する) を参照してください。

AddressTeller は、それぞれの段階で止めても安全な3ステップで導入することを想定しています。

1. **手動で `Apply All` を実行する**（上記の通り）。ルールを追加・調整するたびに何が変わるか確認してください。この段階では何も自動実行されず、何も削除されません。
2. **`Apply All` の結果に納得できたら「インポート時に自動適用する」を ON にする**（`Project Settings > AddressTeller`）。以降は `AssetPostprocessor` が、アセットのインポート・移動・削除のたびに自動的に走ります。インポート・移動されたアセットについてはルール評価が再実行されるため、手動で `Apply All` を実行しなくても日常的なアセットインポートだけでアドレス・ラベルが最新に保たれます（アセットが削除された際にそのエントリを外すのは Addressables 本体であり、この設定の有無とは無関係です）。この段階では AddressTeller 自身が能動的に削除を行うことはありません。
3. **どのグループを所有しているか把握できたら「マッチしなくなったエントリを削除する」を ON にする**。これにより、どのルールにもマッチしなくなった所有グループ内のエントリ（手動で登録したものを含む）が削除されるようになります（詳しくは [設計上の決定事項](Documentation~/design-decisions.ja.md#削除は資産単位の所有権で判定する) を参照）。ON にした直後にメニューから `Tools/AddressTeller/Apply All` を実行してください——OFF の間に溜まった削除分を、次のインポートで黙って実行させるのではなく、手順1の確認ダイアログとセーフティスナップショットを経由させるためです。OFF の間、`Validate` と Preview 系ウィンドウは「削除されるはずのもの」を書き込みを止めない通知として報告するため、ON にする前に確認できます。

手順2・3の設定はいずれも既定 OFF です。適用方法一覧・CI 連携・これらの設定の詳細については [適用と運用](Documentation~/operations.ja.md) を参照してください。

Addressables の DefaultGroup に付与したい場合は `Group("名前")` の代わりに `GroupDefault()` を使えます（DefaultGroup のリネームに追従します）。詳しくは [ルールの書き方](Documentation~/writing-rules.ja.md#groupdefault) を参照してください。

`Order` は優先順位も兼ねます。広いルールに大きい `Order`、特定のルールに小さい `Order` を与えておけば、両方が同じアセットにマッチしたとき特定側のアドレスが採用されます。例は [ルールの書き方: アドレスの優先順位と競合](Documentation~/writing-rules.ja.md#評価ルールと挙動) を参照してください。

ルールクラスを独自の asmdef 内に定義する場合、その asmdef の `references` に `AddressTeller.Core` を追加してください。ルール記述に使う型（`AddressRuleBase` / `IAddressRuleBuilder` / `Match` / `Naming` / `AssetContext`）はすべてこのアセンブリにあります。同じアセンブリから運用系 API（`AddressTellerService` / `ValidationResult` / スナップショット / レポート）も呼ぶ場合に限り、`AddressTeller.Editor` も追加してください。これらは `AddressTeller.Editor` にありますが、シグネチャに Core の型を露出しているため、`AddressTeller.Editor` を参照する場合は必ず `AddressTeller.Core` の参照も必要になります。

ルールクラスは、asmdef が `nunit.framework` を参照するアセンブリ（EditMode テストアセンブリ等）には置かないでください——AddressTeller のルール収集はそのようなアセンブリを警告なく除外するため、そこに置いたルールは一切実行されません。通常の Editor 用 asmdef に置いてください。

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

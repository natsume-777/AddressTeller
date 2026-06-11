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
using Natsume777.AddressTeller;
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

`Tools/AddressTeller/Apply All` を実行すると、対象アセットに `Characters` グループへのエントリが作成され、アドレスとラベルが設定されます。
`Characters` グループは事前に Addressable Groups ウィンドウで作成しておく必要があります（存在しないグループ名はエラーになります）。

## DSL リファレンス

### AddressRuleBase

```csharp
public abstract class AddressRuleBase
{
    public virtual int Order => 0;          // 評価順序。小さいほど先に評価される
    public abstract void Configure(IAddressRuleBuilder rules);
}
```

`AddressRuleBase` を継承したクラスはアセンブリから自動収集されます（中央登録は不要）。

### Group / Where / Address / Label

```csharp
rules.Group("グループ名")
    .Where(ctx => /* bool */)                 // 1グループにつき1回のみ。複数条件は && でまとめる
    .Address(ctx => /* string */)             // または .Address("固定文字列")
    .Label(ctx => /* string */)               // 複数回呼べる。.Label("固定文字列") も可
    .Label("もう一つのラベル");
```

- `Where(predicate)` / `Where(predicate, description)` は **1グループにつき1回のみ**呼び出せます。2回目を呼ぶと `InvalidOperationException`。
  `description` を指定すると、衝突時のエラーメッセージにその説明文が使われます。
- `Address()` を呼ばなければ、そのグループルールはアドレスを発行しません（ラベル付与だけのルールとして使えます）。
- `Label()` は**何度でも**呼び出せます。マッチした全ルールのラベルが蓄積されます。
- 同じ `Configure()` 内で `Group()` を複数回呼び、複数のルールエントリを定義できます。

### AssetContext

ルールに渡されるアセット1件分の情報です。

| プロパティ | 説明 | 例 |
|---|---|---|
| `Guid` | アセットの GUID | |
| `Path` | `Assets/` 起点のパス（`/` 区切りに正規化済み） | `"Assets/Game/Characters/Player.prefab"` |
| `Type` | アセットの型 | `typeof(GameObject)` |
| `FileNameWithoutExtension` | 拡張子なしファイル名 | `"Player"` |
| `FileName` | 拡張子ありファイル名 | `"Player.prefab"` |
| `Directory` | ディレクトリパス | `"Assets/Game/Characters"` |

## 評価ルールと挙動

- **評価順序**: 全ルールを `Order` の昇順で評価します。同じ `Order` 値を持つルールクラスが複数ある場合、`Apply All` / `Validate` 実行時に警告が出ます。
- **アドレスの競合**: マッチしたルールのうち `Address()` を指定したものが2件以上あると **競合エラー**になり、そのアセットへの書き込みは行われません（Apply・Validate 共通）。1件だけマッチした場合にそのアドレスが採用されます。
- **ラベルの蓄積**: `Label()` はモードに関わらず、マッチした全ルールから蓄積されます（複数ラベルが同時に付与されます）。
- **マッチするルールが0件の場合**: そのアセットは対象外としてスキップされます。`CleanupStaleEntries`（後述）が有効な場合のみ、AddressTeller が管理するグループに残った既存エントリが削除されます。
- **グループが存在しない場合**: `GroupNotFound` エラーになります。グループの自動作成は行いません。事前に Addressable Groups ウィンドウで作成してください。
- **ルール内で例外が発生した場合**: そのルールだけが `RuleError` として個別に報告され、他のルール・他のアセットの処理は継続されます。

## 適用方法

| 方法 | 説明 |
|---|---|
| インポート時自動適用 | `AssetPostprocessor` により、アセットのインポート・移動・削除のたびに自動で `Apply All` 相当が実行されます。Project Settings でオフにできます。 |
| `Tools/AddressTeller/Apply All` | プロジェクト全体に手動でルールを適用します。 |
| `Tools/AddressTeller/Validate` | 書き込みは行わず、競合・グループ未検出などの問題だけを Console に出力します。 |
| `Tools/AddressTeller/Apply with Validate` | 先に Validate を実行し、問題があれば Apply を中止します。 |

### CI 連携

`-executeMethod` で以下を実行できます。問題があれば exit code 1 で終了します。

- `Natsume777.AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `Natsume777.AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI`

## Project Settings

`Project Settings > AddressTeller` に以下の項目があります。

- **インポート時に自動適用する**（既定: ON）— オフにすると `AssetPostprocessor` による自動適用を行いません。手動メニューには影響しません。
- **マッチしなくなったエントリを削除する**（`CleanupStaleEntries`、既定: ON）— `Apply All` 実行時、どのルールにもマッチしなくなったアセットを、AddressTeller が管理するグループ（いずれかのルールが参照しているグループ）から削除します。**ラベルは削除されません**。AddressTeller が管理していないグループに手動で登録したエントリには触れません。
- **スナップショット保存先フォルダ**（後述）

これらの設定値は `ProjectSettings/AddressTellerSettings.asset` に保存されます。プロジェクト単位の設定としてバージョン管理に含めることができ、チームメンバー間で共有されます。

登録されているルールクラス（`AddressRuleBase` 継承クラス）の一覧と、`Order` 値も同じ画面で確認できます。

## スナップショット

`Tools/AddressTeller/Snapshot/` 以下のメニューで、現在の Addressables 状態（グループ・アドレス・ラベル）を JSON として保存・復元・比較できます。

- **Save Snapshot**: 現在の状態を JSON ファイルに保存します。
- **Restore Snapshot (Additive)**: スナップショットの内容を書き戻します。スナップショットにないラベルは残ります。
- **Restore Snapshot (Exact)**: スナップショットの内容に書き戻し、スナップショットにないラベルは剥がして完全一致させます。
- **Compare with Current State / Compare Two Snapshots**: 追加・削除・変更を Console に出力します。

保存先フォルダは Project Settings で変更できます（既定値: プロジェクトルート直下の `AddressTellerSnapshots/`、Assets 外）。

## サンプル

Package Manager の Samples タブから以下をインポートできます（`Samples~/` 配下）。

- **Basic Rules** — 最小構成のルール定義例。
- **Folder-based Rules** — フォルダ階層をそのままアドレス・ラベルに反映する例。
- **Type-based Rules** — アセットの型ごとにグループ・ラベルを振り分ける例。

## 背景

アドレス・ラベルの設計はエンジニアが担うことが多い。にもかかわらず、ScriptableObject + Inspector UI でルールを定義する方式は「UI で表現できることが表現の上限」になりやすく、グループを GUID で参照する保存形式はリネームや削除で壊れ、差分も読みにくい。

AddressTeller はルール定義を C# コードに寄せることで次を狙う。

- **表現力の上限がない** — パス解析・外部データ読み込み・任意の C# ロジックが書ける
- **差分が綺麗** — `.asset` を持たず、コードだけ管理すればよい
- **IDE 支援が効く** — 補完・リファクタリング・ユニットテストが普通の C# として使える

データ駆動にしたい場合も、読み込み方式をコード側で自由に選べる。

## ライセンス

[MIT](LICENSE)

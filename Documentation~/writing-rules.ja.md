[English](./writing-rules.md)

# ルールの書き方

## AddressRuleBase

```csharp
public abstract class AddressRuleBase
{
    public virtual int Order => 0;          // 評価順序。小さいほど先に評価される
    public abstract void Configure(IAddressRuleBuilder rules);
}
```

`AddressRuleBase` を継承したクラスはアセンブリから自動収集されます（中央登録は不要）。`Editor` フォルダ配下に置いてください。

## Group / Where / Address / Label

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

## GroupDefault

`Group("名前")` の代わりに `GroupDefault()` を使うと、Addressables の DefaultGroup にアドレス・ラベルを付与できます。
`Where`/`Address`/`Label` は `Group()` と同様にチェーンできます。

```csharp
rules.GroupDefault()
    .Where(ctx => ctx.IsInFolder("Assets/Game/Misc"))
    .Address(ctx => ctx.FileNameWithoutExtension);
```

- DefaultGroup は評価時に `AddressableAssetSettings.DefaultGroup` から解決されるため、**DefaultGroup をリネームしても追従**します（グループ名をコードに書く必要がありません）。
- `Group("実名")` と `GroupDefault()` が同一の実グループを指している場合も、通常のグループルールと同様に競合検出の対象になります。

## Match / Naming ヘルパー

条件述語とアドレス生成の定型パターンをヘルパークラスで簡潔に記述できます。

### Match 静的クラス

頻出の条件を組み立てる機能。自動生成される説明は Explain ウィンドウで確認でき、ルール検証時のエラーメッセージにも反映されます。

```csharp
using AddressTeller;

rules.Group("Characters")
    .Where(Match.InFolder("Assets/Game/Characters")
               .And(Match.OfType<GameObject>()))
    .Address(Naming.FileNameWithoutExtension())
    .Label("character");
```

主なメソッド:
- `Match.InFolder(string path)` — 指定フォルダ配下のアセットにマッチ
- `Match.OfType<T>()` — 指定の型（GameObject, Sprite など）にマッチ
- `Match.Glob(string pattern)` — ワイルドカード（`*.prefab` など）で照合
- `Match.All()` — 常に真（条件なしルール）
- `condition.And(otherCondition)` — 条件を AND 合成

### Naming 静的クラス

アドレス値を生成する頻出パターン。パス正規化の細部を気にせずに記述できます。

```csharp
.Address(Naming.FileNameWithoutExtension())
.Address(Naming.ParentFolderName())
.Address(Naming.RelativePath("Assets/Game"))
```

主なメソッド:
- `Naming.FileName()` — 拡張子付きファイル名
- `Naming.FileNameWithoutExtension()` — 拡張子なしファイル名
- `Naming.ParentFolderName()` — 親フォルダ名
- `Naming.RelativePath(string root)` — 指定フォルダ起点の相対パス

## AssetContext

ルールに渡されるアセット1件分の情報です。

| プロパティ | 説明 | 例 |
|---|---|---|
| `Guid` | アセットの GUID | |
| `Path` | `Assets/` 起点のパス（`/` 区切りに正規化済み） | `"Assets/Game/Characters/Player.prefab"` |
| `Type` | アセットの型 | `typeof(GameObject)` |
| `FileNameWithoutExtension` | 拡張子なしファイル名 | `"Player"` |
| `FileName` | 拡張子ありファイル名 | `"Player.prefab"` |
| `Directory` | ディレクトリパス | `"Assets/Game/Characters"` |
| `Extension` | ファイル拡張子 | `".prefab"` |
| `IsInFolder(string)` | フォルダ配下判定メソッド | `ctx.IsInFolder("Assets/Game")` → `true` |
| `PathSegments` | パスを `/` で分割した配列 | `["Assets", "Game", "Characters", "Player.prefab"]` |
| `RelativePathFrom(string root)` | 指定フォルダ起点の相対パス | `ctx.RelativePathFrom("Assets/Game")` → `"Characters/Player.prefab"` |

## 評価ルールと挙動

- **評価順序**: 全ルールを `Order` の昇順で評価します。同じ `Order` 値を持つルールクラスが複数ある場合、`Apply All` / `Validate` 実行時に警告が出ます。
- **アドレスの競合**: マッチしたルールのうち `Address()` を指定したものが2件以上あると **競合エラー**になり、そのアセットへの書き込みは行われません（Apply・Validate 共通）。1件だけマッチした場合にそのアドレスが採用されます。なぜこの挙動にしているかは [設計上の決定事項: アドレスは競合時にエラーにする](design-decisions.ja.md#アドレスは競合時にエラーにする) を参照してください。
- **ラベルの蓄積**: `Label()` はモードに関わらず、マッチした全ルールから蓄積されます（複数ラベルが同時に付与されます）。理由は [設計上の決定事項: ラベルは全ルールから蓄積する](design-decisions.ja.md#ラベルは全ルールから蓄積する) を参照してください。
- **マッチするルールが0件の場合**: そのアセットは対象外としてスキップされます。`CleanupStaleEntries`（[適用と運用](operations.ja.md) を参照）が有効な場合のみ、AddressTeller が管理するグループに残った既存エントリが削除されます。
- **グループが存在しない場合**: `GroupNotFound` エラーになります。グループの自動作成は行いません。事前に Addressable Groups ウィンドウで作成してください。詳しくは [設計上の決定事項: 存在しないグループは作らない（既定）](design-decisions.ja.md#存在しないグループは作らない既定) を参照してください。
- **ルール内で例外が発生した場合**: そのルールだけが `RuleError` として個別に報告され、他のルール・他のアセットの処理は継続されます。

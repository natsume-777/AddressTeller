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

ルールクラスを独自の asmdef 内に定義する場合、その asmdef の `references` に `AddressTeller.Core` を追加してください。ルールクラスが触れる型（`AddressRuleBase` / `IAddressRuleBuilder` / `Match` / `AssetCondition` / `Naming` / `AssetContext`、および単体テストで使う `AddressTeller.Testing.RuleInspector`）はすべてこのアセンブリにあるため、ルール定義だけのアセンブリであればこれ以外の参照は不要です（`Rule Unit Test Helper` サンプルの asmdef がまさにこのケースで、`AddressTeller.Core` のみを参照しています）。

同じアセンブリから運用系 API（`AddressTellerService` / `ValidationResult` / `AddressTellerSnapshotService` / レポート系の型など）も呼ぶ場合に限り、`AddressTeller.Editor` も追加してください。これらは `AddressTeller.Editor` にありますが、シグネチャに Core の型を露出している（例: `ValidationResult.Context` は Core の `AddressTeller.AssetContext`）ため、`AddressTeller.Editor` を参照する場合は必ず `AddressTeller.Core` の参照も必要になります。

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

同じルールファイルで `System.Text.RegularExpressions` も使う場合は、2つの `Match` 型を区別するため `using Match = AddressTeller.Match;` を追加してください。

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
| `IsFolder` | このアセットがファイルではなくフォルダかどうか | `"Assets/Game/Characters"` に対して `true` |
| `PathSegments` | パスを `/` で分割した配列 | `["Assets", "Game", "Characters", "Player.prefab"]` |
| `RelativePathFrom(string root)` | 指定フォルダ起点の相対パス | `ctx.RelativePathFrom("Assets/Game")` → `"Characters/Player.prefab"` |

## 評価対象から除外されるアセット

一部のパスはどのルールの `Where()` にも渡されません。ルールが除外したのではなく、Addressables 自身がエントリ登録を拒否するパスを AddressTeller が事前にフィルタしているためです（Groups ウィンドウへのドラッグや Inspector の Addressable チェックボックスと同じパス有効性の判定。ただし Inspector のチェックボックスはこれに加えて、メインアセットの型がエディタアセンブリに属するものも拒否しており、その判定は AddressTeller では再現していません）。これにより、手動では作れないエントリを AddressTeller 経由でだけ作ってしまうことを防いでいます。

`IncludeFolders()` の有無に関わらず除外されるもの:
- `Assets/` 配下でもパッケージ自身のフォルダ配下でもないパス（`ProjectSettings/` や `Library/` 配下のファイル等）、およびパッケージ直下の `package.json` 自体。
- 次のいずれかの拡張子を持つファイル: `.cs`, `.js`, `.boo`, `.exe`, `.dll`, `.meta`, `.preset`, `.asmdef`。
- パスの途中に `Editor` という名前のセグメントを含むもの（例: `Assets/Game/Editor/Foo.asset`）。
- 拡張子がなく、パスがちょうど `Assets` であるか、`Editor` という名前のフォルダ自体であるか、または `Editor` という名前のフォルダの配下にあるもの（実際にフォルダかどうかは問わない。下記の注記を参照）。
- 設定済みの Addressables Config Folder 自体とその配下（`AddressableAssetSettings.asset` 等が置かれているフォルダ）。Addressables 本体と同じ単純な前方一致で判定するため、名前が同じ文字列で**始まっているだけ**の別フォルダ（例: `Assets/AddressableAssetsData_Backup`）も、Config Folder の実際の配下ではなくても除外されます。

これは Addressables 本体が内部で行っているエントリ有効性の判定を移植したもので、パス文字列のみで判定します。そのため、拡張子のないパスは実体が通常のファイルであってもフォルダ扱いになります。例えば、拡張子なしで `Assets/Game/Editor` という名前の実ファイルが存在すれば、それも除外されます。`AssetContext.IsFolder`（下記の `IncludeFolders()` の opt-in 判定で使用）はこれとは別の、実際のフォルダかどうかの判定であり、この除外には関与しません。

## フォルダ（IncludeFolders）

既定では、ルールはフォルダを一切見ません。ルールが `IncludeFolders()` で明示的に opt-in しない限り、フォルダに対して `Where()` は呼ばれません。これにより、ファイルを前提に書かれた既存ルールが、`Match.InFolder(...)` のような前方一致条件で意図せずサブフォルダにマッチしてしまうことを防いでいます。

```csharp
rules.Group("Bundles")
    .Where(ctx => ctx.IsFolder && ctx.FileName == "StreamingContent")
    .IncludeFolders()
    .Address(ctx => ctx.FileName);
```

- `IncludeFolders()` は1ルールにつき1回のみ呼び出せます（`Where()` / `Address()` と同じ制約）。2回目を呼ぶと `InvalidOperationException`。
- opt-in したルールの `Where` / `Address` / `Label` の中では、`ctx.IsFolder` でファイルとフォルダを区別できます。
- Addressables 側でフォルダをエントリ化すると、配下の全アセットが暗黙に含まれます。フォルダのラベルは配下アセットにも継承されます。フォルダと配下のアセットを別々のルールでそれぞれマッチさせると、同じアセット群を指す2つのエントリが別々に管理される状態になるため、意図した挙動か確認してください。
- 拡張子のないファイル（`LICENSE` など）は通常のファイルであり、フォルダではありません。`IncludeFolders()` を宣言していても `IsFolder` は `false` のままです。ただし拡張子なしパスの一部（`Assets` ルート自体、`Editor` という名前のフォルダ自体、`Editor` という名前のフォルダ配下）は、実際にフォルダかどうかを問わず評価対象から除外されます。詳しくは上記の[評価対象から除外されるアセット](#評価対象から除外されるアセット)を参照してください。
- `IncludeFolders()` を宣言していても、[評価対象から除外されるアセット](#評価対象から除外されるアセット)に挙げた除外は変わらず適用されます（いずれにせよ Addressables 側がそれらのパスへのエントリ登録を拒否するため）。

## テスト

`AddressRuleBase` のサブクラスを単体テストするには、公開 API の `RuleInspector`（名前空間 `AddressTeller.Testing`、`AddressTeller.Core` アセンブリへの参照が必要）と「Rule Unit Test Helper」サンプルを使用します。`RuleInspector.Collect()` は `Configure()` で定義されたルールエントリを取り出します（ライブな Addressables プロジェクト不要）。あとはテストでフィクスチャを用意し、ルール動作を検証します。詳しい使い方と NUnit 統合パターンは、Package Manager の Samples タブから「Rule Unit Test Helper」をインポートし、同梱の README を参照してください。

## 評価ルールと挙動

- **評価順序**: 全ルールを `Order` の昇順で評価します。同じ `Order` 値を持つルールクラスが複数ある場合、`Apply All` / `Validate` 実行時に警告が出ます。
- **アドレスの競合**: マッチしたルールのうち `Address()` を指定したものが2件以上あると **競合エラー**になり、そのアセットへの書き込みは行われません（Apply・Validate 共通）。1件だけマッチした場合にそのアドレスが採用されます。なぜこの挙動にしているかは [設計上の決定事項: アドレスは競合時にエラーにする](design-decisions.ja.md#アドレスは競合時にエラーにする) を参照してください。
- **ラベルの蓄積**: `Label()` はモードに関わらず、マッチした全ルールから蓄積されます（複数ラベルが同時に付与されます）。理由は [設計上の決定事項: ラベルは全ルールから蓄積する](design-decisions.ja.md#ラベルは全ルールから蓄積する) を参照してください。
- **マッチするルールが0件の場合**: そのアセットは対象外としてスキップされます。`CleanupStaleEntries`（[適用と運用](operations.ja.md) を参照）が有効な場合のみ、AddressTeller が管理するグループに残った既存エントリが削除されます。この削除はアドレス・ラベルのいずれも生成しない「真に無マッチ」の場合のみ適用されます。ラベルのみルール（`AnyGroup()` や `Address()` を呼ばない `Group()` ルール）がマッチしている場合はエントリは削除されず、そのラベルが更新されます。このラベルのみのケースでは、既存エントリの address / group は変更されません。そのアセットに対して最後にアドレスルールがマッチした時点の値のまま保持され、管理下グループに属する場合のみラベルが更新されます（管理外グループのエントリには一切触れません）。
- **グループが存在しない場合**: `GroupNotFound` エラーになります。グループの自動作成は行いません。事前に Addressable Groups ウィンドウで作成してください。詳しくは [設計上の決定事項: 存在しないグループは作らない（既定）](design-decisions.ja.md#存在しないグループは作らない既定) を参照してください。
- **ルール内で例外が発生した場合**: そのルールだけが `RuleError` として個別に報告され、他のルール・他のアセットの処理は継続されます。

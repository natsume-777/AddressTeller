[English](./compatibility.md)

# 互換性ポリシー

## 適用範囲と発効時期

`0.x` の間は、破壊的変更がマイナーリリースに含まれることがあります。含まれる場合は [CHANGELOG.ja.md](../CHANGELOG.ja.md) で **BREAKING** と明記します。`1.0.0` 以降は、この文書が契約になります。破壊的変更はメジャーリリースに限定され、対象APIを `[Obsolete]` にするリリースを少なくとも1回挟んでから行われます（[廃止プロセス](#廃止プロセス10以降)を参照）。

この文書は列挙主義です。網羅的な説明ではなく、以下に明示的に列挙したものだけが SemVer の保証対象です。列挙されていないものは、現状たまたま安定していても、予告なくマイナー・パッチリリースで変わりえます。[保証対象外のもの](#保証対象外のもの)も参照してください。

このポリシーが発効した（`1.0.0` 以降の）SemVer 適用ルール:

- **メジャー** — [保証対象](#保証対象)に列挙したものへの破壊的変更
- **マイナー** — 後方互換な追加（新規型・新規メンバー・enum値の末尾追加・新規CLIフラグ・安全な既定値を持つ新規設定フィールド等）
- **パッチ** — 以下の契約を一切変更しないバグ修正

## 保証対象

### 1. 公開C# API

対象アセンブリ: `AddressTeller.Core`（namespace `AddressTeller`。ルール検査ヘルパー `AddressTeller.Testing.RuleInspector` もこのアセンブリに属する）と `AddressTeller.Editor`（namespace `AddressTeller.Editor`）。この2アセンブリの public/protected サーフェスが契約です。

正本は承認テストのベースラインファイルです: `Tests/Editor/PublicApiApproval/PublicAPI.AddressTeller.Core.approved.txt` と `PublicAPI.AddressTeller.Editor.approved.txt`。これらのファイルへの変更のうち純粋な追加でないものは、下記の破壊的/非破壊的リストに照らして分類してください。承認ベースラインは機械的な検知手段であり、それ自体が分類基準ではありません（例: 既存メソッドへの既定値付きオプション引数の追加は承認ファイルの既存行を書き換えますが、下記では非破壊的に分類されます）。承認テスト（`PublicApiApprovalTests`）は未承認のサーフェス変更を検知して失敗するため、リリース時点でのこの2ファイルの差分は、その分類の出発点であり、結論そのものではありません。

**破壊的（メジャー）**:
- public/protected な型・メンバーの削除
- public/protected な型・メンバーのリネーム
- メソッド/プロパティのシグネチャ変更（引数の型・順序、戻り値の型、必須引数の追加）
- 可視性の縮小（例: `public` → `internal`、継承可能なクラスからの `protected` メンバー削除）
- 型の namespace またはアセンブリの変更
- これまで unsealed だった public クラスの sealed 化、その他拡張ポイントの除去
- public/protected なフィールド・イベントの型の変更（より抽象的な型への変更を含む）

**非破壊的（マイナー）**:
- public/protected な型・メンバーの新規追加
- 既存メソッドへの、既定値付きオプション引数の追加（既存の呼び出し元から見えるシグネチャは変わらない）
- 引数の型をより汎用的な型へ拡張すること（既存呼び出し元のオーバーロード解決に影響しない場合）

### 2. `-executeMethod` エントリポイント

コマンドラインから完全修飾名で呼び出されるため、リネームをコンパイラが検知できず、実際にCIを実行して初めて壊れていることが分かります。上記の公開API一般則とは独立に保証対象とし、公開APIの承認テストで既に検知される場合であっても、リネームは破壊的変更として扱います。

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI`
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI`
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI`

### 3. コマンドライン引数

`AddressTellerCliArgs.TryParse` がパースします（正本: `Editor/EntryPoints/AddressTellerCliArgs.cs`）。

| フラグ | 値 | 省略時の既定 |
|---|---|---|
| `-addressTellerReport <path>` | 任意のファイルパス | レポートを出力しない |
| `-addressTellerReportFormat <format>` | `json`, `junit` | `-addressTellerReport` の拡張子から推定（`.xml` → `junit`、それ以外 → `json`）。レポートパスも未指定なら値なし |
| `-addressTellerDisableRules <names>` | カンマ区切りのルールクラス完全修飾名 | 空（追加除外なし） |
| `-addressTellerConfirmClear` | 値を取らない存在フラグ | 未指定（`ClearCLI` は意図的な拒否として扱う） |
| `-addressTellerClearScope <scope>` | `all`, `managed` | `managed` |

未知の引数は黙って無視されます。これ自体が契約の一部です。したがって、将来追加されるフラグが、既にそのフラグ名を（無関係な目的で）渡しているCI実行を壊すことはありません。逆に、このパッケージが将来のリリースで未知の引数をエラーとして拒否し始めることも許されません。

**非破壊的**: 新規フラグの追加、既存フラグの値語彙への新しい値の追加（例: `-addressTellerClearScope` の3つ目の値）。

**破壊的**: フラグ名の変更、値語彙からの値の削除、フラグ省略時の既定値の変更。

### 4. Exit Code

`Documentation~/operations.ja.md` より。

`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI`:

| exit code | 意味 |
|---|---|
| 0 | 差分なし・問題なし |
| 1 | ドリフトあり（差分あり、Validation エラーなし） |
| 2 | Validation エラーあり |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`-addressTellerDisableRules` に未知のルールクラス名を指定・レポート書き込み失敗） |

`ClearCLI`:

| exit code | 意味 |
|---|---|
| 0 | クリア完了 |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`scope=managed` で `managedGroups` の信頼性を損なうルール構成エラー・スナップショット保存失敗） |
| 4 | `-addressTellerConfirmClear` が指定されていないため拒否 |

新しい exit code 値の追加（いずれのCLI系統でも）は、既知のコードだけをチェックするCIスクリプトを必ずしも壊さないとしても、**メジャー**変更として扱います。理由は、CIスクリプトは既知コードごとの等値判定で分岐し「それ以外」を予期しない失敗区分として扱う書き方が一般的だからです（例: `case 0/1/2/3: ... ; default: ビルド失敗`）。新しいコードを追加すると、既存の分岐の意味が変わらなくても「それ以外」が捕捉する範囲が変わってしまいます。

### 5. レポート出力（JSON / JUnit XML）

内部型 `AddressTellerReportBuilder`（ここではロジックの所在を示すためだけに名前を挙げています。これ自体は公開APIの一部ではありません。[保証対象外のもの](#保証対象外のもの)参照）が生成し、公開型 `AddressTellerReportWriter` がファイルに書き出します。元となるのは公開型 `AddressTellerReport`（`Editor/Application/Reporting/AddressTellerReport.cs`）です。`JsonUtility` でシリアライズされるため、C# のフィールド名がそのまま（キャメルケース変換なしで）JSON キーになります。

**JSON キー**（インデントはネストを表す）:

```
Summary
  Added, Removed, Changed, Issues, ExitCode         (いずれも System.Int32)
Drift[]
  Guid, Path, ChangeType                            (System.String)
  Before / After
    Address, GroupName                              (System.String)
    Labels[]                                        (System.String)
Issues[]
  Path, Status, Message                             (System.String)
BundleDistribution                                  (常に存在する。詳細は下記の注記を参照)
  Bundles[]
    GroupName, Mode, SplitKey                        (System.String)
    AssetCount                                        (System.Int32)
  TotalLogicalBundleCount, UnknownGroupCount          (System.Int32)
  Disclaimer                                          (System.String)
SchemaVersion                                         (System.Int32)
```

**`BundleDistribution`**: このキーは常に JSON 出力に含まれ、省略されることも JSON の `null` になることもありません。`JsonUtility` は `UnityEngine.Object` を継承しない `[Serializable]` クラスの null 参照を表現する手段を持たないため、`BundleDistribution` が null であっても、全フィールドが C# の既定値（`Bundles: []`、`TotalLogicalBundleCount: 0`、`UnknownGroupCount: 0`、`Disclaimer: ""`）を持つオブジェクトとしてシリアライズされます（省略や `null` にはなりません）。この全既定値の形は、dry-run の `After` が null な場合、`AddressableAssetSettings` が渡されなかった場合、あるいはバンドル分布の算出処理自体が例外を投げた場合（内部でキャッチされ警告ログのみ出力）に現れます。消費者は、この「件数がすべて0・`Bundles[]` が空・`Disclaimer` が空文字」という組み合わせを「算出されなかった」ことを示すものとして扱うべきであり、「バンドル数0」を意味するわけではありません。

**値の語彙**:

- `ChangeType`: 固定文字列 `"Added"` / `"Removed"` / `"Changed"`。
- `Status`: `ValidationStatus` メンバーの名前（例: `"ConflictingAddress"`）。この一覧がどう増えうるかは [Enum](#enum) を参照。
- `Mode`（`Bundles[]` 内）: `BundleModeKind` メンバーの名前（`PackTogether`、`PackSeparately`、`PackTogetherByLabel`、`Unknown`）。
- `SplitKey`（`Bundles[]` 内）: `PackTogether` の場合は固定文字列 `"all"`。`Unknown` の場合は固定文字列 `"(unknown)"`（`BundleDistributionCalculator.UnknownSplitKey`）。`PackTogetherByLabel` の場合は、そのバンドルに属するアセットにラベルが無ければ固定文字列 `"(no labels)"`（`BundleDistributionCalculator.NoLabelsSplitKey`）、そうでなければアセットのラベルを Ordinal でソートして `|` で連結したもの。`PackSeparately` の場合はアセット識別子（この固定語彙には含まれません）。
- `Bundles[]` の順序: `GroupName`（Ordinal）→ `Mode` の基底の数値（文字列比較ではない）→ `SplitKey`（Ordinal）の順でソートされます。`BundleModeKind` に新しいメンバーを、既存メンバーの数値の間に挟まる値で追加するとこの並び順が変わります（[Enum](#enum)参照）。

**`SchemaVersion`**: 現在は `1`（`AddressTellerReportBuilder.CurrentSchemaVersion`）。上記の形状が、JSON を構造的にパースする消費者にとって暗黙に後方非互換になる変更を行うたびにインクリメントします（新規の任意フィールドを読むだけの場合は対象外）。パッケージ自身はレポートを読み戻す際に未知の `SchemaVersion` を拒否しません（現状レポートファイルを再読込するCLI経路が無いため）が、これらのレポートをパースする外部ツールは、自分が理解しているバージョンより大きい値を「この形状を知らない」として扱うべきです。

**JUnit XML**（`AddressTellerReportWriter.ToJUnitXml`）:

- `<testsuite name="AddressTeller" tests="..." failures="...">` — `name` 属性は `"AddressTeller"` に固定。
- drift 全体で1つの `<testcase>`: `name="drift"`、`classname="AddressTeller.Drift"`。
- レポートの issues に含まれる `ValidationStatus` の種類ごとに1つの `<testcase>`: `name="<ステータス名>"`（例: `"ConflictingAddress"`）、`classname="AddressTeller.Validation"`。
- `tests` / `failures` の件数、および入れ子の `<failure>` 要素の有無は、標準的なJUnit消費者の期待に従います（`<failure>` の無い `<testcase>` は成功、有る場合は失敗）。

**消費者側の義務**: 未知のJSONフィールドは無視する（追加で失敗しない）こと。`SchemaVersion` を自分が理解している最大バージョンと比較し、それより大きければ形状を推測せず未対応として扱うこと。

**明示的に対象外**: `Disclaimer` 文字列の正確な文言、およびJUnitの `<failure>` 要素の `message` 属性・本文。いずれも自由記述であり随時変更されうるため、内容ではなく有無のみを検証してください。ただし、`BundleDistribution` が実際に算出された場合、`Disclaimer` は必ず非空になります（`AddressTellerReportBuilder` はその経路で常に固定の非空文字列を代入します）。`Disclaimer` が空文字であることは、上記の「算出されなかった」全既定値の形になっていることを示すシグナルの1つですが、非空である場合の文言そのものには何の保証もありません。

### 6. スナップショットファイル

`AddressTellerSnapshotService.Capture` が組み立てた `AddressTellerSnapshot` を `JsonUtility` でシリアライズしたものです。実際のファイル書き込みは `Capture` 自身ではなく、`AddressTellerAutoSnapshotService`・`AddressTellerClearSnapshotService`・Save Snapshot メニューが行います。読み込みは `AddressTellerSnapshotService.LoadFromFile`（`Editor/Application/Snapshot/AddressTellerSnapshotService.cs`）が行います。こちらも `JsonUtility` でシリアライズされるため、同じくフィールド名そのままのルールが適用されます。

**JSON キー**:

```
Entries[]
  Guid, Address, GroupName                (System.String)
  Labels[]                                (System.String)
CapturedAtIso, Comment, UnityVersion, PackageVersion   (System.String)
SchemaVersion                             (System.Int32)
```

**`SchemaVersion`**: 現在は `1`（`AddressTellerSnapshotService.CurrentSchemaVersion`）。レポート形式と異なり、スナップショットの読み込みはこれを積極的に検証します。`LoadFromFile` は `SchemaVersion` が現在サポートしている値より大きいファイルを拒否します。値が `0` の場合は「このフィールドが存在する前の旧形式JSON、または初期化直後のインスタンス」を意味し、受け入れられます。

**フォルダ構成**: スナップショットは `AddressTellerSettings.SnapshotFolder`（設定変更可能、既定はプロジェクトルート直下の `AddressTellerSnapshots/`）以下に書き出されます。契約上の意味を持つ予約サブフォルダが2つあります。

- `SnapshotFolder/Auto/` — メニューから `Apply All` / `Apply with Validate` を実行する直前に取得される自動セーフティスナップショット。`Tools/AddressTeller/Undo Last Apply` が利用します。ローテーション対象です（`AddressTellerSettings.AutoSnapshotRetention`。実装は `AddressTellerAutoSnapshotService`）。保持件数を超えた古いファイルは自動的に削除されます。
- `SnapshotFolder/Clear/` — `Tools/AddressTeller/Clear All Addresses & Labels...` / `ClearCLI` の実行前に取得される専用スナップショット。Auto 側のローテーション対象外であり、Clear 側自体にもローテーションの仕組みがありません（`AddressTellerClearSnapshotService`）。このフォルダ内のファイルが自動削除されることはありません。

これらのサブフォルダを走査して最新の自動スナップショット・クリア用スナップショットを探すツールは、フォルダ名と、Clear サブフォルダが Auto のローテーションによって間引かれることが無い点に依拠できます。

### 7. Settings Asset

`ProjectSettings/AddressTellerSettings.asset`（`ScriptableSingleton`、`Editor/Application/AddressTellerSettings.cs`）に永続化され、バージョン管理に含めてチームで共有されることを想定しています。

**シリアライズされるフィールド名**（すべて `AddressTellerSettingsAsset` 上の `[SerializeField] internal`）:

| フィールド | 型 | 既定値 |
|---|---|---|
| `_cleanupStaleEntries` | `bool` | `true` |
| `_postprocessEnabled` | `bool` | `true` |
| `_snapshotFolder` | `string` | `"AddressTellerSnapshots"` |
| `_autoSnapshotBeforeApplyAll` | `bool` | `true` |
| `_autoSnapshotRetention` | `int` | `10` |
| `_autoCreateMissingGroups` | `bool` | `false` |
| `_postprocessOrder` | `int` | `1000`（後述） |
| `_disabledRuleClassNames` | `List<string>` | 空 |

これらのフィールドを、旧名を指す `[FormerlySerializedAs]` を付けずにリネームすることは禁止です。付けずにリネームすると、既に `AddressTellerSettings.asset` をコミット済みのすべてのプロジェクトで、該当設定がエラーも警告もなく既定値へ黙って戻ってしまいます。フィールドの既定値の変更は**メジャー**（破壊的）変更です。明示的に設定していないプロジェクトの挙動が変わるためです。

`_postprocessOrder` の `0` は「未設定」を表す予約値です。この設定が追加される前からある既存アセット（フィールドがゼロ値の既定のまま）と、明示的に `0` を設定したプロジェクトのどちらも、読み込み時は `AddressTellerSettings.DefaultPostprocessOrder`（`1000`）として扱われます。これは上記のフィールドリネーム規則の必然的な帰結です。Unityがシリアライズする `int` では「一度も設定されていない」と「明示的に0を設定した」を区別できないため、両方を同じフォールバックにまとめています。

Project Settings の UI 自体（`Project Settings > AddressTeller`、プロバイダーパス `Project/AddressTeller` で登録）は保証対象**外**です。レイアウト・項目順序・説明文は自由に変更されえます。

### 8. メニューパス

`Editor/EntryPoints/` 配下の全 `[MenuItem]` パス:

- `Tools/AddressTeller/Apply All`
- `Tools/AddressTeller/Preview Group...`
- `Tools/AddressTeller/Clear All Addresses & Labels...`
- `Tools/AddressTeller/Validate`
- `Tools/AddressTeller/Apply with Validate`
- `Tools/AddressTeller/Snapshot/Save Snapshot`
- `Tools/AddressTeller/Snapshot/Manage Snapshots...`
- `Tools/AddressTeller/Undo Last Apply`
- `Assets/AddressTeller/Explain`
- `Assets/AddressTeller/Preview (Apply Preview)`

これらのパスのリネーム・削除は破壊的変更です（ドキュメント・利用者の操作記憶・記録済みマクロが文字列としてこれらを参照するため）。新規メニュー項目の追加は非破壊的です。

### 9. ルール記述の挙動

`AddressRuleBase` サブクラスの収集・評価に関する以下の挙動は保証対象です。利用者のルールクラスはこの挙動を前提に書かれるためです。

- **収集方法**: ロード済みの全アセンブリ（`nunit.framework` を参照するアセンブリを除く）からリフレクションで検出します。abstract でない型で、public な引数なしコンストラクタを持つことが条件です。オープンジェネリック型は別途フィルタされているわけではありません。コンストラクタの存在チェック自体は通過しますが、実際のインスタンス化時に `Activator.CreateInstance` が例外を投げるため、コンストラクタが例外を投げるルールクラスと同じ扱い（警告ログを出したうえでスキップ、収集全体は中断しない）になります。
- **Order**: `Order` の昇順で評価されます。同値の場合はルールクラスの完全修飾型名（Ordinal）で決定的にタイブレークします。クラス間で `Order` が重複していても警告が出るだけでエラーにはなりません。
- **競合**: 同一アセットに対して2件以上のマッチしたルールが `Address()` を呼んだ場合は競合（`ValidationStatus.ConflictingAddress`）となり、そのアセットへのアドレス・ラベルとも書き込まれません（アドレスだけでなく、そのアセットへの書き込み自体がスキップされます）。
- **ラベルの蓄積**: マッチした全ルールからの `Label()` 呼び出しがアセットに蓄積されます。マッチしなくなったルールによってラベルが暗黙に削除されることはありません（唯一ラベルを削除するのは `CleanupStaleEntries` によるエントリ全体の削除です）。
- **`Where()` / `Address()` の単回呼び出し制約**: 同一ルールチェーン上でいずれかを2回呼び出すと `InvalidOperationException` を投げます。
- **`GroupDefault()` の解決**: `Configure()` 実行時ではなく評価時に `AddressableAssetSettings.DefaultGroup` から解決されるため、DefaultGroup のリネームに自動的に追従します。

評価挙動を変える新規設定を、明示的に有効化した場合にのみ挙動が変わる形（既定OFFのオプトイン）で追加することは非破壊的です。オプトインしないプロジェクトの挙動は変わらないためです。

## Enum

`ValidationStatus`、`ClearScope`、`ReportFormat`、`DistributionFormat`、`SnapshotRestoreMode`、`BundleModeKind` はいずれも **open enum** です。このパッケージはマイナーリリースでこれらのいずれにも新しいメンバーを追加できます。メンバーのリネーム・削除、または既存メンバーに割り当てられた数値の変更（別のメンバーへの数値の使い回しを含む）は**メジャー**（破壊的）変更です。`1.0.0` 以降は、これらの enum のメンバーと数値の対応関係が凍結されます。新しいメンバーは常に既存の最大値の後ろにのみ追加されます。ソースコード上の宣言順だけを、いずれのメンバーの割り当て数値も変えずに入れ替えることは、破壊的でも検知可能でもありません（[この文書がどう強制されるか](#この文書がどう強制されるか)を参照）。`1.0.0` より前であっても、意図しないリネーム・削除・数値のずれは同じ節の記載の通り機械的に検知されます。

**メンバー追加がコンパイルを壊さずに実行時の挙動を壊しうる理由**: `default` アームの無い `switch` 文は、認識していない新しいenum値に対してもコンパイル・実行ができてしまいます。ただ何もしない（あるいは周辺コード次第でフォールスルーする）だけで、これは呼び出し元が見たことのないステータスに対してはたいてい誤った挙動です。追加をパッチではなくマイナーとして扱うのはこのためです。ビルドは失敗しないものの、CHANGELOGで可視化され、これらの型を網羅的に `switch` しているコードの持ち主に検討してもらうことを意図しています。

具体的に、`ValidationStatus` は現在 `Ok`、`Skipped`、`LabelsOnly`、`ConflictingAddress`、`GroupNotFound`、`InvalidAddress`、`RuleError`、`GroupWillBeCreated`、`GroupCreationFailed`、`DefaultGroupUnavailable`、`RuleConfigureFailed` を持ちます。これはパッケージの汎用的な「このアセットに何が起きたか」を表す結果型であり、最も増える可能性が高い enum であるため、これに対する `switch` にこそ `default` アームを置く重要性が高いといえます。

これらの enum のいずれにも `[Flags]` は意図的に採用していません。`ValidationStatus` は特に組み合わせ可能に見えるかもしれませんが、`ValidationResult` は1アセットにつきちょうど1つの結果を表します。`[Flags]` にすると組み合わせに意味があるという前提を持ち込んでしまい、JSON・enum名のシリアライズのされ方も変わってしまいます（`[Flags]` の `ToString()` は組み合わせ値に対してカンマ区切りの名前を生成しうる）。これはこのパッケージがコミットしたくない、より大きな互換性の保証範囲です。

**消費者側の義務**:
- これらの enum を `switch` する箇所には必ず `default` アームを置き、デフォルトケースを「未知・要注意」として扱うこと。黙って無視しないこと。
- これらの値は（スナップショット・ログ・外部設定などに）メンバー**名**で永続化し、内部の数値では永続化しないこと。数値の安定性は `1.0.0` 以降にしか保証されず、その場合でも、名前ベースの永続化フォーマットの方が、仮に将来enumが全面刷新された場合にも数値ベースより持ちこたえます。
- 自分のコードが認識しない enum 値に遭遇した場合は、黙って問題なしとして扱うのではなく、フェイルクローズ（要注意な問題として扱う）側に倒すこと。

**レポートの並び順に露出しているenumの数値**: `BundleModeKind` の基底の数値は、JSONレポート（[レポート出力](#5-レポート出力json--junit-xml)参照）と `LogicalBundle` のメモリ上の並び順（`BundleDistributionCalculator.Calculate`）の両方で、`Bundles[]` 配列のソートのタイブレークに使われています。`BundleModeKind` に既存メンバーの最大値より大きい値で新しいメンバーを追加しても既存モードの並び順は変わりませんが、既存メンバーの数値の間に挟まる値を割り当てると変わります。

## 保証対象外のもの

- `internal` な型・メンバー（`InternalsVisibleTo` 経由でテスト・サンプルアセンブリから見えるものを含む）。ただし上記で明示的に列挙したもの（例: [Settings Asset](#7-settings-asset) の設定アセットのシリアライズフィールド名）を除く
- ログメッセージの文言、ダイアログの文言、ウィンドウのタイトル・レイアウト、USS/UIスタイル
- この文書が明示的に「順序が決まっている」と述べていないコレクションの順序（順序が決まっている公開APIの多くはXMLドキュメントで明記しています。明記が無ければ順序は保証されません）
- `Samples~/` 配下のサンプルパッケージのソースの正確な内容（わかりやすさのために編集されることがあります。サンプルが示すAPI自体は保証対象です）
- 性能特性（実行時間・アロケーション量）
- 対応する Unity / Addressables の最低バージョン。いずれかの最低要件の引き上げは**メジャーではなくマイナー**リリースで行います。このパッケージ自身のAPIは変わりませんが、古いEditor/Addressablesバージョンの利用者には対応が必要になるため、CHANGELOG.ja.mdで明示的に告知します。

## 廃止プロセス（1.0以降）

`1.0.0` 以降、[保証対象](#保証対象)に列挙したものを削除・変更するには以下が必要です。

1. 対象メンバーを `[Obsolete]`（可能なら代替APIを示すメッセージ付き）にするマイナーリリースを少なくとも1回事前に挟むこと
2. 実際の削除・変更は次のメジャーリリースで行い、それより早く行わないこと

`1.0.0` より前は、このプロセスは保証されません。上記の[適用範囲と発効時期](#適用範囲と発効時期)の通り、CHANGELOG.ja.md の **BREAKING** 表記のみが唯一の告知手段です。

## この文書がどう強制されるか

- [公開C# API](#1-公開c-api) については、承認テストのベースラインファイルが機械的なチェックです。未承認の差分は `PublicApiApprovalTests` を失敗させます。これらの `.approved.txt` ファイルの変更は、同じ変更の中でCHANGELOG.ja.mdへの記載（該当する場合は **BREAKING** 表記）とセットにしてください。これは [Enum](#enum) で述べたメンバー・数値対応にも及びます。ベースラインは各メンバーを `EnumMember <名前> = <数値>`（`PublicApiSurfaceFormatter.FormatType`）として記録するため、リネーム・削除、または既存メンバーに割り当てられた数値の変更はいずれもこの記録行を変化させ、他の公開API変更と同じ仕組みで検知されます（破壊的かどうかの分類自体は引き続き [Enum](#enum) の規則に従い、承認ファイルの差分は検知シグナルにすぎません）。この6つの enum のすべてのメンバーが（コンパイラの自動採番に頼らず）明示的な数値を持つため、検知されないのは、ソースコード上の宣言順だけを、いずれのメンバーの割り当て数値も変えずに入れ替えるケースです。承認ファイル上のテキストは宣言順ではなくメンバー名でソートされているため、この入れ替えは記録に現れません。また実行時の挙動にも影響しません。これらの enum に対するソート・比較（例: [レポート出力](#5-レポート出力json--junit-xml) の `Bundles[]` のタイブレーク）はソースファイル上の位置ではなく数値そのものを使うためです。
- この文書のそれ以外の項目（CLIエントリポイント・引数・exit code・レポート/スナップショット形式・設定フィールド名・メニューパス・ルール記述の挙動）には自動検知の仕組みがありません。これらのいずれかが黙って変わってもCIは失敗しません。この文書の列挙が、意図しない変更と壊れた消費者との間にある唯一の防波堤です。上記で参照したソースファイルを変更する際は、その変更がここに列挙した項目に触れていないか確認してください。

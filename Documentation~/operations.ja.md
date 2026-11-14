[English](./operations.md)

# 適用と運用

## 適用方法

| 方法 | 説明 |
|---|---|
| インポート時自動適用 | 既定は OFF。Project Settings で有効にすると、`AssetPostprocessor` により、アセットのインポート・移動・削除のたびに自動で `Apply All` 相当が実行されます。 |
| `Tools/AddressTeller/Apply All` | プロジェクト全体に手動でルールを適用します。 |
| `Tools/AddressTeller/Preview Group...` | 既存グループのドロップダウンを表示し、選択したグループの現メンバー（フォルダは展開済み）を起点に有効な全ルールの dry-run を実行し、結果ウィンドウ（Diff/Issuesタブ、算出できた場合は Distribution タブも）を開きます。書き込みは行いません。Apply All や Validate は別途手動で実行する必要があります。 |
| `Tools/AddressTeller/Validate` | 書き込みは行わず、競合・グループ未検出などの問題だけを Console に出力します。 |
| `Tools/AddressTeller/Show Unmatched Assets` | 読み取り専用。ルールが宣言したフォルダの配下にあるのに、どのルールからもアドレスもラベルも付かないアセットを、結果ウィンドウの Unmatched タブに一覧します（[未登録アセットと一致 0 件のルール](#未登録アセットと一致-0-件のルール)を参照）。該当が無ければウィンドウは開きません。 |
| `Tools/AddressTeller/Apply with Validate` | 先に Validate を実行し、問題があれば Apply を中止します。 |
| `Assets/AddressTeller/Explain`（Project ウィンドウの右クリックメニュー） | 選択したアセットに対して全ルールを評価し、その結果を確認ウィンドウで表示します。マッチしたルール・マッチしなかったルール（その `Where` 説明付き）・ルール例外を一覧で見ることができます。`Match` ヘルパーを使用したルールは自動生成された説明（例: `InFolder(Assets/Characters) AND OfType<GameObject>`）が表示されるため、生ラムダよりもルールの動作確認が効率的です。 |
| `Assets/AddressTeller/Preview (Apply Preview)`（Project ウィンドウの右クリックメニュー） | 選択したアセット（フォルダは再帰展開）に対して有効な全ルールの dry-run を実行し、`Preview Group...` と同じ結果ウィンドウを開きます。書き込みは行いません。 |
| `Tools/AddressTeller/Clear All Addresses & Labels...` | AddressTeller が管理するグループのみを対象に、Addressable エントリ（アドレス・グループ割り当て・ラベル）を削除します。実行前に専用スナップショット（`SnapshotFolder/Clear` 以下、ローテーション対象外）を必須で保存し、確認ダイアログを経て実行します。公開前パッケージの初期セットアップ用途を想定した割り切り機能です。削除したエントリは Console に個別ログ（Warning）され、Snapshot Restore で復元できます。 |

`Apply All` と `Apply with Validate` は、書き込みを行う前に dry-run を実行し、追加・変更の件数、および `CleanupStaleEntries` によって削除されるエントリがあればその件数を確認ダイアログで提示します。ここでキャンセルしたり、詳細を確認してから実行したりできます。

## CI 連携

`-executeMethod` で以下を実行できます。

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI`（`ApplyWithValidateCLI` は先に Validate を行い、問題があれば Apply を中止します）
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI`（Apply を行わない dry-run。読み取り専用で差分・問題を検出します）
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI`（既定では AddressTeller 管理下のグループのエントリのみを削除します。`-addressTellerClearScope all` を指定するとプロジェクト内の全 Addressable エントリを削除します。実行には `-addressTellerConfirmClear` の指定が必須です）

`-addressTellerReport <path>` / `-addressTellerReportFormat json|junit` を指定すると、`CheckCLI` は dry-run、`ApplyAllCLI` / `ApplyWithValidateCLI` は Apply 実行前の差分（dry-run）から構造化レポートをファイル出力します。`-addressTellerReportFormat` を省略した場合、拡張子が `.xml` なら `junit`、それ以外は `json` として扱われます。

4つの CLI エントリポイントはいずれも、他の処理を行う前に AddressTeller の設定ファイルが読み込めるかどうかを検査します（後述の [Project Settings](#project-settings) を参照）。読み込めない場合はそのまま処理を続けずエラーログを出して exit code 3 で終了します。

`ApplyAllCLI` / `ApplyWithValidateCLI` / `ClearCLI` は、終了する直前に `AssetDatabase.SaveAssets()` を呼びます。そのため、加えた変更をディスクに保存できるかどうかが `EditorApplication.Exit` 自身のアセットフラッシュ挙動に依存しません。

`CheckCLI` の exit code（読み取り専用。書き込みを一切行わない）:

| exit code | 意味 |
|---|---|
| 0 | 差分なし・問題なし |
| 1 | ドリフトあり（差分あり、Validation エラーなし） |
| 2 | Validation エラーあり、または（`-addressTellerFailOnUnmatched` 指定時のみ）ルールが宣言したフォルダ配下に、どのルールからもアドレスもラベルも付かないアセットが1件以上ある |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`-addressTellerDisableRules` に未知のルールクラス名を指定・レポート書き込み失敗・設定ファイルまたはラベル台帳の読み込み失敗、または予期しない例外） |

`-addressTellerFailOnUnmatched` は `CheckCLI` だけが読み、ほかの CLI エントリポイントは無視し、その旨の警告を1行ログします。詳しくは[未登録アセットと一致 0 件のルール](#未登録アセットと一致-0-件のルール)を参照してください。

`ApplyAllCLI` / `ApplyWithValidateCLI` の exit code（成功時に書き込みを行う。exit code は差分の有無に依存しない——差分だけを検出したい場合は `CheckCLI` を使うこと）:

| exit code | 意味 |
|---|---|
| 0 | 差分の有無を問わず、適用に成功した |
| 2 | Validation エラーあり（適用で書いたラベルをラベル台帳に記録できなかった場合の `ValidationStatus.LabelLedgerWriteFailed` を含む。`RemoveStaleManagedLabels` が ON の間だけブロッキング） |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`-addressTellerDisableRules` に未知のルールクラス名を指定・レポート書き込み失敗・設定ファイルまたはラベル台帳の読み込み失敗、または予期しない例外） |

`ClearCLI` の exit code:

| exit code | 意味 |
|---|---|
| 0 | クリア完了 |
| 2 | 同一アセットが2つ以上の Addressables グループに同時にエントリを持つ状態（`ValidationStatus.DuplicateAssetEntry`）を検出し、クリア前スナップショットを保存する前に中止した |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`scope=managed` で `ownedGroups` の信頼性を損なうルール構成エラー・スナップショット保存失敗・設定ファイルまたはラベル台帳の読み込み失敗、または予期しない例外） |
| 4 | `-addressTellerConfirmClear` が指定されていないため実行を拒否（意図的な拒否） |

### コマンドラインからの非対話実行

上記4つの CLI エントリポイントはいずれも確認ダイアログを出さず、入力待ちで止まることもありません。引数を解析し、実行し、結果をログ出力した後、それぞれが自分自身で必ず `EditorApplication.Exit(<code>)` を呼んで終了します。これは破壊的操作を行う `ClearCLI` でも同様で、確認ダイアログの代わりに必須の `-addressTellerConfirmClear` フラグ（上記 exit code 表を参照）で意思確認を行い、入力待ちにはなりません。（対話メニュー版と比較すると次の違いがあります。`Apply All` / `Apply with Validate` は先に dry-run を行い、適用すべき差分がある場合に限り確認ダイアログを表示します。dry-run の結果、差分も問題も無ければダイアログを出さずにそのまま戻ります（`AddressTellerApplyFlow`）。一方 `Clear All Addresses & Labels...` は dry-run を行わず、削除対象が0件であっても常に確認ダイアログを表示します。ダイアログを出さずに中止するのは、ルール構成エラーにより管理グループの所有権判定が信頼できない場合のみです。[適用方法](#適用方法) を参照。）

例: `ApplyAllCLI` をヘッドレスで実行し、JSON レポートを出力した上で、プロセスの exit code から成否を判定する場合。

```
"<Unity実行ファイルへのパス>" -batchmode -quit -projectPath "<プロジェクトへのパス>" -executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI -addressTellerReport report.json -logFile -
```

- `-batchmode` は Unity をヘッドレスで起動します。`-quit` は通常のケースでは不要です。上記の各 CLI メソッド自身が返る前に `EditorApplication.Exit(<code>)` を呼んで終了するためです。ただし、この呼び出しに到達しない想定外の例外（このパッケージ自身のエラーハンドリングで捕捉していないもの。例: `ApplyAllCLI` から `AddressTellerService.ApplyAll` を呼ぶ箇所）が起きた場合の保険として付けておく価値はあります。
- `-logFile -` は Editor ログをファイルではなく標準出力へ流します。この機能が出力する `Debug.Log` / `Debug.LogError` を CI で捕捉するのに便利です。
- 必要に応じて `CheckCLI`・`ApplyWithValidateCLI`・`ClearCLI`（こちらは `-addressTellerConfirmClear` が別途必須。`-addressTellerClearScope all` も任意で指定可）に差し替えてください。
- ビルドを失敗させるかどうかは、上記の exit code 表と照らし合わせて判定してください。

### 未登録アセットと一致 0 件のルール

`Match.InFolder`・`Match.InFolderByGuid`・`Match.Glob`（固定のフォルダ接頭辞を持つもの）で書いたルールは、条件が依存するフォルダを宣言します。この宣言を使って、ルールが取りこぼしているものを2通りで知らせます。（`Match.Glob` の固定接頭辞は実在検査にだけ使い、Unmatched の範囲にはなりません。`Match.Glob("Assets/**/Characters/**")` で `Assets` 全体が範囲になることはないので、範囲は `Match.InFolder` で宣言してください。）

- **未登録アセット。** 宣言フォルダの配下（深さを問わず、大小文字を区別しない）にあるのに、どのルールからもアドレスもラベルも付かないアセット。`Match.InFolder(path, recursive: false)` で宣言したフォルダは直下の子だけが対象です。結果ウィンドウの `Unmatched` タブ（dry-run で1件以上あるとき、および issue があって開く Validate メニューで表示。プロジェクトの一部しか見ないルール・アセット・グループのプレビューには出ません）。専用の読み取り専用の入口 `Tools/AddressTeller/Show Unmatched Assets` は、プロジェクト全体をこのタブに一覧し、該当が無ければウィンドウを開かず、重複アセットエントリ・Configure() に失敗したルール・実在しない宣言フォルダ・読めない設定ファイルのために一覧が不完全なときは、0 件と言い切らずにその旨を Console に 1 行出します（理由は Validate で確認してください）、`json` レポートの `Unmatched[]`、JUnit レポートの `unmatched` testcase、コンソールの1行の件数で確認できます。コンソールの1行は Info レベル（警告ではありません）で、`CheckCLI`・`ApplyAllCLI`・`ApplyWithValidateCLI`・Validate・Apply All / Apply with Validate が、1件以上あるときに出します。Unmatched は報告であって判定ではありません。`Match.InFolder(x).And(Match.OfType<T>())` のように型や拡張子で絞ったルールは、同じフォルダのそれ以外のアセットを設計どおり未登録のまま残すので、一覧が空でないのは通常のことです。Validate は未登録アセットだけでは結果ウィンドウを開きません。issue があって開くときは、`Issues` の隣に `Unmatched` タブを出します。インポート時の自動適用は、今回インポートまたは移動したアセットだけを見ます。宣言フォルダの配下にあり、どのルールからもアドレスもラベルも付かなかったものを、件数と先頭数件のパスの Info 1 行にまとめるので、追加したばかりのアセットにすぐ気づけます。同じパスはエディタのセッション中（次のドメインリロードまで）1 回だけ出します。同じアセットを再インポートしても黙り、ルールからアドレスやラベルが付くようになった・フォルダから出た・削除または移動されたパスは記憶から外れて、のちに再び未登録になれば再度出ます。別の新しいアセットはそのつど出ます。インポートを止めることはありません。Show Unmatched Assets は常にプロジェクト全体を一覧します。フォルダ自体、ルールが例外を送出したアセット、ラベル専用ルールだけにマッチしたアセットは含まれません。`Where` が生ラムダのルールはフォルダを宣言しないため、アセットが載る原因にはなりません。`Not()` の内側にだけ宣言されたフォルダもルールの範囲ではなく、`Match.InFolder(a).And(Match.InFolder(b).Not())` の `b` の配下のアセットは、このルールが原因で載ることはありません（[宣言フォルダ](writing-rules.ja.md#宣言フォルダ)を参照）。報告専用で、exit code は変わりません。ただし `CheckCLI` に `-addressTellerFailOnUnmatched` を付けると、一覧が空でないとき exit code 2 になります（コンソールの1行も error になり、JUnit の testcase に `<failure>` が付きます。フラグが無いときは Info のままです）。
- **一致 0 件のルール。** アドレスを付けうるルールが、プロジェクト全体の走査でどのアセットにもマッチしなかったとき、`ValidationStatus.RuleMatchedNothing`（ブロッキングでない通知）としてルールごとに1件、`Validate`・`CheckCLI`・`Apply All` / `Apply with Validate` の dry-run が報告し、これらと `ApplyAllCLI` / `ApplyWithValidateCLI` がコンソールに警告としてログします。宣言したフォルダが実在しないルールは除かれます（`DeclaredFolderMissing` が原因を示すため）。`ApplyAll` 自体、範囲指定プレビュー、`BuildPredictedSnapshot` の直接呼び出しは、プロジェクト全体を走査しないため報告しません。条件の typo や、移動したフォルダが原因であることが多いです。

### ルールの重なり

広いルールを狭いルールが意図して上書きする場合と、意図せずルールが重なっている場合は、結果が同じです。`Order` の小さいほうが勝ち、何も報告されません。そのため dry-run は、重なっているルールクラスの組——両方がアセットにマッチしてアドレスを出し、一方が `Order` で勝った組——を、アセット数と数件の例パス（Ordinal 順で小さいもの）つきで一覧します。結果ウィンドウの `Overlaps` タブ（1件以上あるときだけ表示。Validate の結果ウィンドウは issues だけを表示します）、`json` レポートの `Overlaps[]`、`DryRunResult.Overlaps` で確認できます。同点（`ConflictingAddress`）と、ルール例外で書き込みが止まったアセットは重なりではありません。一覧はその実行が調べたアセットが対象なので、プロジェクトの一部だけを見るプレビューには、そのアセットの間の重なりだけが載ります。下の設定が ON かどうかに関わらず作られ、exit code は変えません。

意図した上書きは、上書きされる側のルールで `OverriddenBy<TRule>()` により宣言します（[ルールの書き方: ルールの重なり](writing-rules.ja.md#ルールの重なりoverriddenby)参照）。**厳格モード**（`StrictRuleOverlaps`、既定 OFF）を ON にすると、宣言されていない重なりが、ルールクラスの組ごとに1件、ブロッキングの `ValidationStatus.UndeclaredRuleOverlap` として報告されます。

| エントリポイント | 厳格モードの検査 |
|---|---|
| `Validate`、`Apply with Validate`（Validate の段） | する——結果はエラーで、Apply with Validate は中止される |
| `CheckCLI` | する——exit code 2 で終わり、このステータスの JUnit testcase に `<failure>` が付く |
| `ApplyWithValidateCLI` | する——最初の Validate の段でラン全体が exit code 2 で中止される |
| `Apply All`（メニュー） | dry-run に他の検証エラーと同じく表示される（ダイアログの issue 件数と結果ウィンドウ）。Console にも個別にログし、Apply All は強制しない旨の警告を1行添える。Apply All は先に検証しないので適用は止まらない。差分がなく、見つかったのが未宣言の重なりだけのときは、ダイアログの代わりに「変更なし」のメッセージを出す |
| プレビュー（アセット・グループ） | する（それぞれが調べるアセットについて） |
| プレビュー（ルール） | 対象外——1ルールだけを評価するため、ルール同士の重なりは出ない |
| `ApplyAllCLI` | しない——検証しないため。警告を1行ログし、レポートの `Issues` からこのステータスを除きます。`Overlaps[]` はすべての重なりを載せたままです |
| import 時の自動適用 | しない——この検査を行わない |

どのルールが勝つか、何が書き込まれるかは変わりません。例パスの1つで `Assets > AddressTeller > Explain` を使うと、関与したルールをすべて見られます。

### 論理バンドル分布サマリ

`json` 形式のレポートには `BundleDistribution` セクションが含まれます（`JsonUtility` は大文字小文字の変換を一切行わないため、JSON のキー名は C# のフィールド名とそのまま一致します）。これは dry-run の Predict 結果（アセット→グループ/ラベル）と各グループの BundleMode（PackTogether/PackSeparately/PackTogetherByLabel）から算出した、ビルド前の論理バンドル単位の個数・分布の概算です。「ルール設計が意図せず巨大バンドル1個や数百分割を生んでいないか」を検知するための目安であり、**実 Addressables ビルドのバンドル数を一致させることを保証しません**。

近似の既知差異として以下は反映されません。

- PackTogether のシーン別バンドル分離
- PackSeparately のフォルダ単位まとめ
- PackTogetherByLabel における Addressables 本体のラベル連結方式との差異（本サマリはラベル集合を昇順ソート＋区切り文字で連結した正規化キーで分割しています）

`BundledAssetGroupSchema` が付与されていないグループは BundleMode が判定できないため `Unknown` として扱われ、バンドル数の集計（`TotalLogicalBundleCount`）には含まれません（`UnknownGroupCount` で別集計されます）。JSON の全キー一覧と安定性の保証範囲は [互換性ポリシー](compatibility.ja.md#5-レポート出力json--junit-xml) を参照してください。

## Project Settings

`Project Settings > AddressTeller` には以下の項目があります。

| 設定 | 既定値 | 概要 |
|---|---|---|
| インポート時に自動適用する | OFF | `AssetPostprocessor` により、インポート・移動・削除のたびに `Apply All` 相当を自動実行します。手動の `Apply All` の結果に納得できてから ON にすることを推奨します（導入の流れは [クイックスタート](../README.ja.md#クイックスタート) 参照）。 |
| Postprocessor の実行順序 | `1000` | `AssetPostprocessor.GetPostprocessOrder()` に渡す値です。値が小さいほど先に実行されます。 |
| **マッチしなくなったエントリを削除する** | **OFF** | どのルールにもマッチしなくなった所有グループ内のエントリを削除します——手動登録したエントリも削除対象になります（詳細は後述）。どのグループを所有しているか把握できてから ON にし、直後にメニューから `Apply All` を実行することを推奨します（確認ダイアログと自動スナップショットを経由させるため）。OFF の間、削除されるはずだったエントリは `ValidationStatus.UnmatchedEntryKept`（書き込みを止めない通知）として `Validate` / `Apply with Validate` / `CheckCLI` / Preview 系ウィンドウに報告され、ON にする前に影響範囲を確認できます。 |
| 1回のランで削除できる stale エントリの上限 | `0`（無制限） | 上の設定の安全弁です。`ApplyAll` の1回のラン（インポート時の自動適用、`ApplyAllCLI`、`ApplyWithValidateCLI` を含む）がこの件数を超えて削除しようとした場合、1件も削除せず、ブロッキングの `ValidationStatus.StaleRemovalLimitExceeded` を報告します（詳細は後述）。`Apply All` / `Apply with Validate` メニュー項目には適用されません。 |
| 不要になった管理ラベルを削除する | OFF | `Apply All` がエントリを書き込むとき、AddressTeller 自身が過去に書いた（名前がラベル台帳に記録されている）ラベルのうち、そのアセットにどのルールももう出さないものを外します。手で付けた同名のラベルも外れることがあります（後述）。OFF の間、そのようなラベルは `ValidationStatus.StaleManagedLabelKept`（書き込みを止めない通知）として `Validate` / `Apply with Validate` / `CheckCLI` / Preview 系ウィンドウに1件ずつ報告されます。 |
| ルールの重なりを厳格に検査する | OFF | あるルールのアドレスが別のルールクラスに上書きされている（両方がアセットにマッチし、`Order` の小さいほうが勝つ）場合、上書きされる側のルールが `OverriddenBy<TRule>()` で宣言していなければ、ブロッキングの `ValidationStatus.UndeclaredRuleOverlap` として報告します。`Validate` / `Apply with Validate` / `CheckCLI` / `ApplyWithValidateCLI` / プレビューウィンドウが検査し、import 時の自動適用と `ApplyAllCLI` は検査しません。[ルールの重なり](#ルールの重なり)を参照してください。 |
| 存在しないグループを自動作成する | OFF | ルールが参照するグループが存在しない場合、エラーにせず自動作成します。 |
| スナップショット保存先フォルダ | `AddressTellerSnapshots` | スナップショットの保存先（プロジェクトルートからの相対パス）。 |
| Apply実行前に自動スナップショットを保存する | ON | `Apply All` / `Apply with Validate` メニュー実行前にセーフティスナップショットを保存します。 |
| 自動スナップショットの保持件数 | `10` | 指定件数を超える古い自動スナップショットは自動削除されます（最小1）。 |
| ルールクラスごとの有効/無効 | 全て有効 | 特定のルールクラスを `Apply All` / `Validate` / `Apply with Validate` / `Explain` / dry-run 予測の評価対象から除外します。 |

一行の概要だけでは説明しきれない項目の詳細:

- **Postprocessor の実行順序**（`PostprocessOrder`）— 既定値は後段寄りの大きな値で、他パッケージの Postprocessor がアセットを生成・変更してから AddressTeller が評価することを期待します。
- **マッチしなくなったエントリを削除する**（`CleanupStaleEntries`）— apply 時（手動の `Apply All`、およびインポート時の自動適用では変更されたアセットについて）、AddressTeller が所有するグループから、どのルールにもマッチしなくなったエントリを削除します。「所有する」の定義や、手動登録したエントリへの影響については [設計上の決定事項: 削除は資産単位の所有権で判定する](design-decisions.ja.md#削除は資産単位の所有権で判定する) を参照してください。有効な場合、これに加えて、所有グループ内でアセットパスが構造的に無効なエントリ（拡張子・`Editor` という名前のフォルダ等で判定される、旧バージョンの AddressTeller が作成した残骸等）も削除対象になります。パスがそもそも解決できない（`AssetPath` が空文字になる。LFS 未取得・ブランチ切替中・パッケージ未導入等で一時的に資産へアクセスできないケースを含む）エントリはこの掃除の対象外です。資産が本当に削除された場合の追従は、別経路（削除通知を起点にするもの）が担当します。この判定は実際にインポート・変更されたアセットとは独立に、所有グループ内の全エントリに対して毎回行われるため、インポート時の自動適用（Postprocessor）のような差分適用では本来たどり着けないはずの残骸も掃除されます。つまり Postprocessor が走るたびに、インポート・変更されたアセットの件数に関わらず、所有グループ全件のエントリを毎回スキャンします。この設定が OFF の間、`Validate` / `Apply with Validate` / `CheckCLI` および Preview 系ウィンドウは、削除されるはずだったエントリを実際に削除する代わりに、書き込みを止めない通知 `ValidationStatus.UnmatchedEntryKept` として報告します——コンソールには1件ずつではなく件数のみの1行サマリを出します（個別の内容は常に Result Window と JSON/JUnit レポートに残ります）。インポート時の差分適用（`AddressTellerPostprocessor`）はこの通知を一切報告しません。あわせて [設計上の決定事項: 存在しないグループは作らない（既定）](design-decisions.ja.md#存在しないグループは作らない既定) も参照してください。
- **1回のランで削除できる stale エントリの上限**（`MaxStaleRemovalsPerRun`）— 1回の `ApplyAll` ランが削除してよいエントリ数の上限です。ルールの誤り（フォルダパスの typo、マッチしなくなったルール）で、Addressables 構成の大部分が気付かないうちに消えるのを防ぎます。「マッチしなくなったエントリを削除する」が ON のとき、1回のランの削除（どのルールにもマッチしなくなったエントリと、パスが構造的に無効なエントリ）は、アセットごとにその場で行わず、ランの最後にまとめて実行されます。ランがキャンセルされた場合は1件も削除されません。上限はこの合計に対して、すべての `ApplyAll` ラン——インポート時の自動適用（`AssetPostprocessor`）、`ApplyAllCLI` / `ApplyWithValidateCLI`、`AddressTellerService.ApplyAll` の直接呼び出し——に適用されます。ただし `Apply All` / `Apply with Validate` メニュー項目は、先に確認ダイアログで削除内容を見せるため対象外です。合計が上限を超えるときは、超過分だけでなく削除を1件も行わず、件数・上限・先頭数件のパスを含むブロッキングの `ValidationStatus.StaleRemovalLimitExceeded` を1件報告します（`ApplyAllCLI` / `ApplyWithValidateCLI` は終了コード 2 になります）。アドレスとラベルは通常どおり書き込まれます。見送られたエントリは Addressables に残ります。フルスキャン（`ApplyAllCLI` / `ApplyWithValidateCLI`）では、削除が残っている間は毎回この報告が返ります。インポート時の自動適用では、引き金になったインポートで1回だけ報告されます（報告内容が変わったときは再度報告されます）。進めるには、メニューの `Apply All` で削除内容を確認するか（確認ダイアログが削除件数を見せます。メニュー経路には上限がありません）、上限を引き上げるか 0 に戻してください。`0` は無制限で、既定値です。負の値は `0` として扱われます。プロジェクトから資産が削除されたことへの追従には適用されません。また dry-run（`Validate`、`CheckCLI`、Preview 系ウィンドウ）は見送られる削除を予測しません。
- **不要になった管理ラベルを削除する**（`RemoveStaleManagedLabels`）— ルールはラベルを加えるだけなので、ルールが出さなくなったラベル（あるいはアドレスの優先度で負けたルールに由来するラベル）は、そのままだとエントリに残り続けます。AddressTeller は書いたラベルの名前をすべて**ラベル台帳** `ProjectSettings/AddressTellerLabelLedger.json` に記録します（`labels` に名前をソートして並べたファイルで、`ProjectSettings` の他のファイルと一緒にバージョン管理する想定です。AddressTeller が初めてラベルを書いたときに作られます）。この設定が ON のとき、`Apply All`（およびインポート時の自動適用）がエントリを書き込む際——通常の書き込みでも、既存エントリへのラベルのみの更新でも——そのエントリ上のラベルのうち、台帳にあり、かつそのアセットのルールが現在出すラベルに含まれないものをすべて外します。除去は1件ごとに Warning を出します。名前が台帳にないラベルには一切触れず、そのランで AddressTeller が書き込まないエントリ（どのルールにもマッチしない、同点、ルール例外）もそのままにします。台帳が無い場合は空の台帳として扱われるため、AddressTeller 自身がラベルを書くまでは何も外れません。注意点が2つあります。台帳が持つのは名前だけなので、AddressTeller が書き込むエントリに手で付けた同名のラベルも、そのアセットにどのルールも出していなければ外れること。そして、エントリからラベルを外しても Addressables 上のラベル定義は消えないことです。台帳にあってどのエントリも使っていない定義を削除するには、`Tools/AddressTeller/Remove Unused Managed Labels...` を実行します（先に確認ダイアログが対象を一覧します。利用者が自分で作った定義は一覧に出ません）。除去は、`Apply All` メニューの前に取られるスナップショットを復元すれば元に戻せます——ただし設定を ON のままにしていると、次のインポート時の自動適用で再び外れます。また自動スナップショットを取るのは `Apply All` / `Apply with Validate` メニューの前だけで、自動適用や CLI では取りません。`Apply All` の確認ダイアログには、ラベルを失うエントリの数が（変更されたエントリ数とは別に）表示されます。「1回のランで削除できる stale エントリの上限」はラベルの除去には効きません。設定が OFF の間、`Validate` / `Apply with Validate` / `CheckCLI` および Preview 系ウィンドウは、そのようなラベルを持つエントリを書き込みを止めない通知 `ValidationStatus.StaleManagedLabelKept` として報告します——コンソールには1件ずつではなく件数のみの1行サマリを出し、`AddressTellerPostprocessor` によるインポート時の差分適用は報告しません。設定が ON のときは、dry-run が除去を変更されたエントリとして示します。台帳が存在するのに読めない、または AddressTeller のラベル台帳として認識できない場合は、後述の壊れた設定ファイルと同じ扱いになり、ファイルを直すか削除するまで実行を拒否します（削除すると、AddressTeller は自分が書いたラベルを忘れます。忘れるのは除去が減る方向なので、削除はいつでも安全です）。台帳は、この設定が OFF でも、ラベルを書いた最初の `Apply All` で作られます。台帳でマージ衝突が起きたら、両側の名前の和集合を取ってください（ファイルは名前を並べただけで、AddressTeller 自身も書き込み時にディスク上の内容とマージします）。AddressTeller のものとみなす名前は、ラン開始時点の台帳で固定されるため、ランの中で初めて書かれたラベルが、走査順にかかわらず同じランで外れることはありません。ルールの `Configure()` が失敗した、または宣言フォルダが欠落しているランでは、ラベルを一切外さず、いずれかのルールが例外を投げたアセットからも外しません（stale エントリの削除を止める条件と同じです。加算は影響を受けません）。ランで書いたラベルは、ランの最後に1回だけ台帳へ記録されます。その書き込みが失敗した場合（たとえばファイルがバージョン管理で read-only）は、ランを中断せず、`ValidationStatus.LabelLedgerWriteFailed` を報告します（この設定が ON の間はブロッキングで、CLI は終了コード 2 になります。OFF の間は台帳に依存する機能がまだ働いていないので、終了コードに影響しない通知です）。ラベル自体はエントリに書かれ、除去には影響しません（除去はラン開始時の台帳に基づくため）。影響は、そのランで初めて書いた名前がまだ記録されず、後のランが記録するまで stale として外されないことだけです。範囲について2点: AddressTeller が所有していないグループのエントリでも、ラベルのみのルール（`AnyGroup()`）がマッチすれば、ラベルの書き込みが所有権を問わないため除去の対象になります。また、Project Settings でルールを無効化するとそのルールはラベルを出さなくなるため、この設定が ON なら次の apply で、そのルールが担っていたエントリからそのラベルが外れます。
- **存在しないグループを自動作成する**（`AutoCreateMissingGroups`）— ルールが参照するグループが存在しない場合、Apply はそのグループを DefaultGroup のスキーマを複製して作成します。Validate/Predict では作成予定として表示するのみで、実際にはグループを作成しません。作成自体が失敗することもあります（`DefaultGroup` が取得できない場合、または `AddressableAssetSettings.CreateGroup` が例外を投げた場合）。その場合 Apply は該当アセットに対して `ValidationStatus.GroupCreationFailed` を報告し、書き込みを行いません。自動作成に成功した場合・失敗した場合のどちらも、グループ1件につき1本の `Debug.LogWarning` が出ます。あわせて [設計上の決定事項: 存在しないグループは作らない（既定）](design-decisions.ja.md#存在しないグループは作らない既定) も参照してください。

これらの設定値は `ProjectSettings/AddressTellerSettings.json` に保存されます。プロジェクト単位の設定としてバージョン管理に含めることができ、チームメンバー間で共有されます。ファイルがまだ存在しない場合、AddressTeller は既定値のまま動作し、ファイルを作成しません——実際にいずれかの設定値を既定値以外へ変更した時点で初めてファイルが作られます。各プロパティの setter は値を変更するたびにこのファイルへ書き込みます（同じ値を再代入した場合は何もせず、書き込みも行いません）。書き込みの前に、前回の読み込み以降ディスク上のファイルが変化していれば読み直します（例えば `git pull` による更新）。これにより、別の経路からの変更が黙って上書きされることを防ぎます。設定ファイルが読み込めない場合——ファイルは存在するが読み取れない、または AddressTeller の設定ファイルとして認識できない場合——AddressTeller はエラーをログし、ファイルが修正または置き換えられるまで `Apply All` / `Validate` / `Apply with Validate` / Preview / Explain / CLI の実行を拒否します。この状態では各プロパティの setter も例外を投げ、壊れたファイルの上に書き込むことはありません。Project Settings 画面自体は開けるため、そこから直せます。ファイルが読めない間は画面上の全項目が無効化され、「Back Up Broken File (.bak) and Recreate with Defaults」ボタンが表示されます（クリックすると確認ダイアログが出ます——「Auto-apply on import」「Remove unmatched entries」を含む全設定が既定値に戻るため）——壊れたファイルを退避し、既定値の新しい正常なファイルから作り直せます。退避ファイル名にはタイムスタンプが含まれるため、複数回退避しても以前の退避ファイルを上書きしません。これらの `.bak` ファイルはローカルの保険用コピーであり、バージョン管理へコミットする必要はありません。設定ファイルがまだ存在しない場合は、表示中の値が既定値であり、いずれかを変更するとファイルが作られる旨を1行で案内します。`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI` / `ClearCLI` は設定ファイルが存在しない状態で実行されると同じ内容を Info として1行ログします。インポート時の自動適用（`AssetPostprocessor`）はこのログを出しません——既定値のまま意図的に運用しているプロジェクトで、インポートのたびにログが出るのを避けるためです。

登録されているルールクラス（`AddressRuleBase` 継承クラス）の一覧と、`Order` 値も同じ画面で確認できます。各ルールクラスの横には有効/無効を切り替えるトグルがあり、デバッグ・動作確認時に特定のルールだけを無効化することができます。無効化したルールは `Apply All` / `Validate` / `Apply with Validate` / `Explain` / スナップショットの dry-run 予測の評価対象から除外されます。各ルール行には「Validate/Apply this rule only」ボタンもあり、そのルール1件だけをプロジェクト内の全アセットに対して dry-run し、結果ウィンドウを開きます。1ルールのみのスコープであるため、他ルールが管理するエントリの削除予測は表示されません（結果ウィンドウにその旨の注意文が表示されます）。逆に、同じグループを他のルールも参照している場合、そのルールにしかマッチしないアセットがこの単独ルールプレビューでは「Removed」と表示されることがあります（`Apply All` は全ルールをまとめて評価するため、実際には削除されません）。全ルールを横断した最終結果を確認するには、別途 `Apply All` や `Validate` を実行してください。このボタンは有効/無効トグルを無視するため、無効化中のルールでも明示的にクリックすれば単独で dry-run できます。

ただし資産削除時のエントリ削除追従（`CleanupStaleEntries` 等）は、ルールの有効/無効に関わらず全ルールを対象に行われます。これは、無効化中のルールであっても過去にそのルールが管理していたエントリを正しく追跡し、オーファンエントリが残り続けないようにするための設計です。

## スナップショット

`Tools/AddressTeller/Snapshot/` 以下のメニューで、現在の Addressables 状態（グループ・アドレス・ラベル）を JSON として保存・復元・比較できます。

- **Save Snapshot**: 現在の状態を JSON ファイルに保存します。
- **Restore Snapshot (Additive)**: スナップショットの内容を書き戻します。スナップショットにないラベルは残ります。
- **Restore Snapshot (Exact)**: スナップショットの内容に書き戻し、スナップショットにないラベルは剥がして完全一致させます。
- **Compare with Current State / Compare Two Snapshots**: 追加・削除・変更を Console に出力します。

保存先フォルダは Project Settings で変更できます（既定値: プロジェクトルート直下の `AddressTellerSnapshots/`、Assets 外）。チームで共有するプロジェクトデータではなくローカルの安全網であるため、通常はバージョン管理へコミットする必要はありません——`.gitignore`（または利用しているVCSの同等機能）に追加しておくことを推奨します。特定のスナップショット（例: リスクのあるルール変更前の動作確認済み状態）をチームで共有したい場合は、フォルダ全体ではなく該当ファイルだけを意図的にコミットしてください。

### 自動セーフティスナップショット

`Tools/AddressTeller/Apply All` および `Tools/AddressTeller/Apply with Validate` メニュー実行時、事前に現在の状態を自動でスナップショットとして保存し、ローテーション管理します。`CleanupStaleEntries` によるエントリ削除などの変更を安全に戻せるよう、`Tools/AddressTeller/Undo Last Apply` メニューで最新の自動スナップショットから Exact モードで復元できます。

Project Settings で以下を設定できます：

- **Apply実行前に自動スナップショットを保存する**（既定: ON）— オフにするとメニュー実行時の自動保存を行いません。
- **自動スナップショットの保持件数**（既定: 10、最小: 1）— 指定件数を超える古い自動スナップショットは自動削除されます。

自動スナップショット機能は `Tools/AddressTeller/Apply All` および `Tools/AddressTeller/Apply with Validate` メニューのみ対象です。インポート時の自動適用・CLI（`ApplyAllCLI`/`ApplyWithValidateCLI`）には適用されません。

## トラブルシューティング

**同じアセットが2つの Addressables グループに同時に登録されている**（`ValidationStatus.DuplicateAssetEntry`）。
これは、2つのブランチがそれぞれ同じアセットを別々のグループへ追加した状態をマージが合成してしまった場合などに起こりえます。この状態では、既存の重複エントリのうちどれがそのアセットのエントリか（アドレス・ラベル・手動編集を含む）が一意に決まらず、どれが生き残る/参照されるかがルールと無関係に（たとえばグループの並び順で）決まってしまいます。グループアセットの import 時に Addressables が重複を自動で外すことがありますが、常にそうなるとは限りません。この状態になると、本来なら書き込み・削除・復元を行う入口すべて（`Apply All`・`Validate`・`Apply with Validate`・`CheckCLI`・`ApplyAllCLI`・`ApplyWithValidateCLI`・インポート時の自動適用・`Undo Last Apply`・`Clear All Addresses & Labels...`/`ClearCLI`・Snapshot Manager ウィンドウからの Restore）がこれを報告し、そのラン全体（重複しているアセットだけでなく）で一切何にも触れずに停止します——どちらのエントリを残すべきかを AddressTeller が判断する手立てがないためです。メッセージには対象アセットと、現在存在するグループ・アドレスの一覧が含まれます。

直すには: 削除する前に、重複している各エントリのラベルとアドレスを控えておいてください（ルールで付与していないラベルは、エントリを消すと自力では戻りません）。そのうえで **Addressables Groups** ウィンドウを開き、メッセージに列挙された*すべて*のグループでそのアセットのエントリを選択し、まとめて削除してください（右クリック→**Remove Addressables**、続く確認ダイアログで確定）。一部だけ残して他を消そうとしないでください——このウィンドウの削除自体も、右クリックした行に関わらず、その GUID に対して先に見つかった方のエントリを削除するだけだからです。重複エントリが1件も残らなくなったら、本来所属すべきグループへ自分でアセットを登録し直すか、`Apply All` を実行してルールにエントリを作り直させてください。その後、期待したグループに収まったことを **Groups** ウィンドウで確認してから利用してください。

## サンプル

Package Manager の Samples タブから以下をインポートできます（`Samples~/` 配下）。

- **Basic Rules** — 最小構成のルール定義例。
- **Folder-based Rules** — フォルダ階層をそのままアドレス・ラベルに反映する例。
- **Type-based Rules** — アセットの型ごとにグループ・ラベルを振り分ける例。
- **Rule Unit Test Helper** — ライブな Addressables プロジェクトなしに `AddressRuleBase` のサブクラスを単体テストするためのヘルパー・NUnit サンプル。

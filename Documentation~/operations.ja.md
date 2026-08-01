[English](./operations.md)

# 適用と運用

## 適用方法

| 方法 | 説明 |
|---|---|
| インポート時自動適用 | `AssetPostprocessor` により、アセットのインポート・移動・削除のたびに自動で `Apply All` 相当が実行されます。Project Settings でオフにできます。 |
| `Tools/AddressTeller/Apply All` | プロジェクト全体に手動でルールを適用します。 |
| `Tools/AddressTeller/Validate` | 書き込みは行わず、競合・グループ未検出などの問題だけを Console に出力します。 |
| `Tools/AddressTeller/Apply with Validate` | 先に Validate を実行し、問題があれば Apply を中止します。 |
| `Assets/AddressTeller/Explain`（Project ウィンドウの右クリックメニュー） | 選択したアセットに対して全ルールを評価し、その結果を確認ウィンドウで表示します。マッチしたルール・マッチしなかったルール（その `Where` 説明付き）・ルール例外を一覧で見ることができます。`Match` ヘルパーを使用したルールは自動生成された説明（例: `InFolder(Assets/Characters) AND OfType<GameObject>`）が表示されるため、生ラムダよりもルールの動作確認が効率的です。 |
| `Tools/AddressTeller/Clear All Addresses & Labels...` | AddressTeller が管理するグループのみを対象に、Addressable エントリ（アドレス・グループ割り当て・ラベル）を削除します。実行前に専用スナップショット（`SnapshotFolder/Clear` 以下、ローテーション対象外）を必須で保存し、確認ダイアログを経て実行します。公開前パッケージの初期セットアップ用途を想定した割り切り機能です。削除したエントリは Console に個別ログ（Warning）され、Snapshot Restore で復元できます。 |

## CI 連携

`-executeMethod` で以下を実行できます。

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI`（`ApplyWithValidateCLI` は先に Validate を行い、問題があれば Apply を中止します）
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI`（Apply を行わない dry-run。読み取り専用で差分・問題を検出します）
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI`（既定では AddressTeller 管理下のグループのエントリのみを削除します。`-addressTellerClearScope all` を指定するとプロジェクト内の全 Addressable エントリを削除します。実行には `-addressTellerConfirmClear` の指定が必須です）

`-addressTellerReport <path>` / `-addressTellerReportFormat json|junit` を指定すると、`CheckCLI` は dry-run、`ApplyAllCLI` / `ApplyWithValidateCLI` は Apply 実行前の差分（dry-run）から構造化レポートをファイル出力します。`-addressTellerReportFormat` を省略した場合、拡張子が `.xml` なら `junit`、それ以外は `json` として扱われます。

exit code（`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI` 共通）:

| exit code | 意味 |
|---|---|
| 0 | 差分なし・問題なし |
| 1 | ドリフトあり（差分あり、Validation エラーなし） |
| 2 | Validation エラーあり |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`-addressTellerDisableRules` に未知のルールクラス名を指定・レポート書き込み失敗） |

`ClearCLI` の exit code:

| exit code | 意味 |
|---|---|
| 0 | クリア完了 |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`scope=managed` で `managedGroups` の信頼性を損なうルール構成エラー・スナップショット保存失敗） |
| 4 | `-addressTellerConfirmClear` が指定されていないため実行を拒否（意図的な拒否） |

### 論理バンドル分布サマリ

`json` 形式のレポートには `BundleDistribution` セクションが含まれます（`JsonUtility` は大文字小文字の変換を一切行わないため、JSON のキー名は C# のフィールド名とそのまま一致します）。これは dry-run の Predict 結果（アセット→グループ/ラベル）と各グループの BundleMode（PackTogether/PackSeparately/PackTogetherByLabel）から算出した、ビルド前の論理バンドル単位の個数・分布の概算です。「ルール設計が意図せず巨大バンドル1個や数百分割を生んでいないか」を検知するための目安であり、**実 Addressables ビルドのバンドル数を一致させることを保証しません**。

近似の既知差異として以下は反映されません。

- PackTogether のシーン別バンドル分離
- PackSeparately のフォルダ単位まとめ
- PackTogetherByLabel における Addressables 本体のラベル連結方式との差異（本サマリはラベル集合を昇順ソート＋区切り文字で連結した正規化キーで分割しています）

`BundledAssetGroupSchema` が付与されていないグループは BundleMode が判定できないため `Unknown` として扱われ、バンドル数の集計（`TotalLogicalBundleCount`）には含まれません（`UnknownGroupCount` で別集計されます）。JSON の全キー一覧と安定性の保証範囲は [互換性ポリシー](compatibility.ja.md#5-レポート出力json--junit-xml) を参照してください。

## Project Settings

`Project Settings > AddressTeller` に以下の項目があります。

- **インポート時に自動適用する**（既定: ON）— オフにすると `AssetPostprocessor` による自動適用を行いません。手動メニューには影響しません。
- **Postprocessor の実行順序**（`PostprocessOrder`、既定: 1000）— `AssetPostprocessor.GetPostprocessOrder()` に渡す値です。値が小さいほど他の `AssetPostprocessor` より先に実行されます。既定値は後段寄りの大きな値で、他パッケージの Postprocessor がアセットを生成・変更してから AddressTeller が評価することを期待します。なお `0` は「未設定」を表す予約値であり、明示的に `0` を指定しても既定値の `1000` として扱われます。
- **マッチしなくなったエントリを削除する**（`CleanupStaleEntries`、既定: ON）— `Apply All` 実行時、どのルールにもマッチしなくなったアセットを、AddressTeller が管理するグループ（いずれかのルールが参照しているグループ）から削除します。削除はエントリ単位（`RemoveAssetEntry`）のため、アドレスと（Addressablesの）ラベルの両方が失われます。AddressTeller が管理していないグループに手動で登録したエントリには触れません。**一方、管理グループ内に手動で登録したエントリは、対応するルールがなければ削除対象になります**（資産単位で「現在どのルールにもマッチするか」のみを判定するため）。この挙動の理由は [設計上の決定事項: 削除は資産単位の所有権で判定する](design-decisions.ja.md#削除は資産単位の所有権で判定する) および [設計上の決定事項: 存在しないグループは作らない（既定）](design-decisions.ja.md#存在しないグループは作らない既定) を参照してください。
- **スナップショット保存先フォルダ**（後述）

これらの設定値は `ProjectSettings/AddressTellerSettings.asset` に保存されます。プロジェクト単位の設定としてバージョン管理に含めることができ、チームメンバー間で共有されます。

登録されているルールクラス（`AddressRuleBase` 継承クラス）の一覧と、`Order` 値も同じ画面で確認できます。各ルールクラスの横には有効/無効を切り替えるトグルがあり、デバッグ・動作確認時に特定のルールだけを無効化することができます。無効化したルールは `Apply All` / `Validate` / `Apply with Validate` / `Explain` / スナップショットの dry-run 予測の評価対象から除外されます。

ただし資産削除時のエントリ削除追従（`CleanupStaleEntries` 等）は、ルールの有効/無効に関わらず全ルールを対象に行われます。これは、無効化中のルールであっても過去にそのルールが管理していたエントリを正しく追跡し、オーファンエントリが残り続けないようにするための設計です。

## スナップショット

`Tools/AddressTeller/Snapshot/` 以下のメニューで、現在の Addressables 状態（グループ・アドレス・ラベル）を JSON として保存・復元・比較できます。

- **Save Snapshot**: 現在の状態を JSON ファイルに保存します。
- **Restore Snapshot (Additive)**: スナップショットの内容を書き戻します。スナップショットにないラベルは残ります。
- **Restore Snapshot (Exact)**: スナップショットの内容に書き戻し、スナップショットにないラベルは剥がして完全一致させます。
- **Compare with Current State / Compare Two Snapshots**: 追加・削除・変更を Console に出力します。

保存先フォルダは Project Settings で変更できます（既定値: プロジェクトルート直下の `AddressTellerSnapshots/`、Assets 外）。

### 自動セーフティスナップショット

`Tools/AddressTeller/Apply All` および `Tools/AddressTeller/Apply with Validate` メニュー実行時、事前に現在の状態を自動でスナップショットとして保存し、ローテーション管理します。`CleanupStaleEntries` によるエントリ削除などの変更を安全に戻せるよう、`Tools/AddressTeller/Undo Last Apply` メニューで最新の自動スナップショットから Exact モードで復元できます。

Project Settings で以下を設定できます：

- **Apply実行前に自動スナップショットを保存する**（既定: ON）— オフにするとメニュー実行時の自動保存を行いません。
- **自動スナップショットの保持件数**（既定: 10、最小: 1）— 指定件数を超える古い自動スナップショットは自動削除されます。

自動スナップショット機能は `Tools/AddressTeller/Apply All` および `Tools/AddressTeller/Apply with Validate` メニューのみ対象です。インポート時の自動適用・CLI（`ApplyAllCLI`/`ApplyWithValidateCLI`）には適用されません。

## サンプル

Package Manager の Samples タブから以下をインポートできます（`Samples~/` 配下）。

- **Basic Rules** — 最小構成のルール定義例。
- **Folder-based Rules** — フォルダ階層をそのままアドレス・ラベルに反映する例。
- **Type-based Rules** — アセットの型ごとにグループ・ラベルを振り分ける例。
- **Rule Unit Test Helper** — ライブな Addressables プロジェクトなしに `AddressRuleBase` のサブクラスを単体テストするためのヘルパー・NUnit サンプル。

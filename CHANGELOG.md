# Changelog

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。
`0.x` 系のため、SemVer 上は破壊的変更もマイナーバージョン内で扱います。

## [0.3.0] - 2026-06-14

### Changed

- **BREAKING**: ルート namespace を `Natsume777.AddressTeller` から `AddressTeller` に変更。利用者は `using Natsume777.AddressTeller;` を `using AddressTeller;` に書き換える必要がある。Editor アセンブリ名も `Natsume777.AddressTeller.Editor` から `AddressTeller.Editor` に変更されたため、他の asmdef からこのアセンブリを参照している場合は `references` の更新が必要。
- `Editor/Application/` 配下および Snapshot 関連の EntryPoints を機能別サブフォルダ（`Snapshot/` / `Reporting/` / `Bundle/`）に整理した（内部構成のみの変更で、公開 API への影響はない）。
- パッケージ名（`com.natsume777.addressteller`）は変更していない。

## [0.2.0] - 2026-06-14

### Added

- Project Settings 画面のルール一覧に各ルールクラスの有効/無効トグルを追加。有効/無効の状態は `ProjectSettings/AddressTellerSettings.asset` に保存される。無効化したルールは `Apply All` / `Validate` / `Apply with Validate` / `Explain` / スナップショットの dry-run 予測の評価対象から除外される。ただし資産削除時のエントリ削除追従（所有権判定）は、ルールの有効/無効に関わらず全ルールを対象に行われるため、無効化中も過去にルールが管理したエントリを正しく追跡する。
- `Match` 静的クラス: 条件述語を構築する頻出ヘルパー。`InFolder(string)`（フォルダ配下）/ `OfType<T>()`（型フィルタ）/ `Glob(string)`（ワイルドカード照合）を提供し、`And(AssetCondition)` で合成可能。各ヘルパーは人間可読な説明（`"InFolder(Assets/Characters)"`など）を自動生成し、Explain ウィンドウやエラーメッセージに反映される。`All()` で常に真の条件を返すため、条件なしルールも明示的に記述できる。
- `Naming` 静的クラス: アドレス生成時の頻出パターン。`FileName()`（拡張子付きファイル名）/ `FileNameWithoutExtension()`（拡張子除き）/ `ParentFolderName()`（親フォルダ名）/ `RelativePath(string root)`（相対パス生成）を提供し、`Address()` メソッドに渡せる。パス正規化（大文字小文字・区切り文字）の手間を削減できる。
- `AssetContext` に新規プロパティを追加: `Extension`（ファイル拡張子）/ `IsInFolder(string)`（フォルダ配下判定）/ `PathSegments`（パスをスラッシュで分割した文字列配列）/ `RelativePathFrom(string root)`（指定フォルダ起点の相対パス）。`Where()` で生ラムダを書く場合、これらを活用することでパス解析の定型コードを簡潔に記述できる。
- 自動セーフティスナップショット機能。`Apply All` / `Apply with Validate` メニュー実行直前に現在の状態を `SnapshotFolder/Auto` 以下へ自動保存し、設定した保持件数（既定10件）でローテーションする。`Tools/AddressTeller/Undo Last Apply` メニューで最新の自動スナップショットから Exact モードで復元できる（CleanupStaleEntries によるエントリ削除等の実質的な Undo）。Project Settings で有効/無効・保持件数を設定可能（既定ON）。CLI（`ApplyAllCLI`/`ApplyWithValidateCLI`）は対象外。
- `AddressTellerSnapshotService.BuildPredictedSnapshot`: Apply を実行せずに、適用後の状態（追加・変更・削除）の差分と、衝突・グループ未検出・ルール例外などの問題点を計算する dry-run API。`DryRunResult`（`SnapshotDiff` + `IReadOnlyList<ValidationResult>`）を返す。
- `AddressTellerService.ApplyAll` / `ValidateAll` に `IProgressReporter` を受け取るオーバーロードを追加。`EditorProgressReporter` は `EditorUtility.DisplayCancelableProgressBar` で進捗表示し、キャンセル時はその時点までの結果を返して中断する（Apply のキャンセルは部分適用のまま、巻き戻しは行わない）。
- `Tools/AddressTeller/Apply All` / `Apply with Validate` メニュー実行時、`BuildPredictedSnapshot` による dry-run 計算を行い、「追加 n 件 / 変更 n 件 / ⚠ 削除 n 件 / 問題 n 件」を確認ダイアログ（3択: 実行 / キャンセル / 詳細表示）で表示する。差分・問題がともに 0 件の場合はログのみ。「詳細表示」で結果ウィンドウを開き、同じ母集合での Apply 実行が可能。`Apply with Validate` で Validate 時点で問題があれば、確認ダイアログを出さずに結果ウィンドウ（Issues タブ）で中止。CLI（`ApplyAllCLI` / `ApplyWithValidateCLI`）は対象外。
- `Tools/AddressTeller/Explain` メニュー: Project ウィンドウで選択したアセットに対して全ルールを評価し、マッチしたルール（採用されたアドレス・ラベル）・マッチしなかったルール（その `Where` 説明）・ルール例外を表示するウィンドウを追加。`Where(predicate, description)` の description が活きるため、ルールの動作確認が容易になる。
- `Tools/AddressTeller/Snapshot/Manage Snapshots...` メニュー: 保存済みスナップショットの一覧・復元・比較を統一的に行う管理ウィンドウを追加。スナップショット取得時刻・ユーザーコメント・Unity/パッケージバージョン・スキーマバージョンのメタデータを記録し、JSON 読み込み時に未対応スキーマ・GUID 欠落/重複などを検証することで、スナップショットの整合性を保証する。従前の個別メニュー項目（`Restore Snapshot (Additive)` 等・`Compare with Current State` 等）は同ウィンドウに統合されたため、メニューから削除される。
- `AddressTellerService.ApplyAll` / `ValidateAll` に、リフレクションによるルール収集を経由せず `IReadOnlyList<AddressRuleBase>` を直接渡せるオーバーロードを追加。既存のリフレクション版はこの新オーバーロードへの内部委譲として残るため後方互換。
- Explain の結果（`AddressTellerExplainReport` / `AddressTellerExplainAsset` / `AddressTellerExplainRule`）を JSON 化する `ToJson()` を追加。`AddressTellerReport` / `AddressTellerSnapshot` と同様の手段で外部ツールから結果を取り込める。

### Changed

- AddressTeller の設定の保存先を `EditorPrefs` から `ProjectSettings/AddressTellerSettings.asset` へ変更（チーム共有のため）。旧設定は引き継がれないため再設定が必要。
- インポート時の自動適用を差分適用に変更し、変更・移動されたアセットのみを処理するようにした。プロジェクト全体の整合性チェックは引き続き `Apply All` / `Validate` / CLI のフル走査が担う。
- `Tools/AddressTeller/Snapshot/Compare with Current State...` と `Compare Two Snapshots...` メニューの差分表示を、`Debug.Log` 出力から結果ウィンドウの Diff タブ表示に変更した。
- 内部実装の可視性を整理し、公開 DSL 面（`AddressRuleBase` / `IAddressRuleBuilder` / `Match` / `Naming` / `AssetContext`）に影響しない範囲で多数の型を internal 化した。`AddressRuleBuilderImpl` / `RuleEvaluator` / `RuleExplanation` / `RuleMatchOutcome` / `RuleEvaluationDetail`（DSL内部実装）、`AddressTellerApplier` / `RuleCollector` / `AssetFilter` / `RuleExplainService` / `SnapshotFileCatalog` / `AddressTellerAutoSnapshotService` / `AddressTellerReportBuilder` / `AddressTellerExplainReportBuilder` 等（Addressables統合層の内部実装）、`AddressResolution` / `RuleEvaluationError`（ルール評価の内部結果型）が対象。`ValidationResult.ConflictingCandidates` で公開APIに露出する `AddressCandidate` は public のまま維持。
- ドキュメント構成を再編し、README を概要・要件・インストール・クイックスタート・関連ドキュメントへのリンク集に整理。DSL リファレンス・運用ガイド・設計上の決定事項・テスト指針・アーキテクチャ解説は `Documentation~/` と `CONTRIBUTING.md` に移設した。

## [0.1.0] - 2026-06-08

### Added

- ルール定義 DSL: `AddressRuleBase` を継承したクラスをアセンブリから自動収集し、`Configure(IAddressRuleBuilder)` で `Group().Where().Address().Label()` をチェーン記述できる。
- ルール評価エンジン: `Order` 昇順で全ルールを評価し、アドレス候補が2件以上なら競合エラー、ラベルは全マッチルールから蓄積する。
- `Tools/AddressTeller/Apply All` / `Validate` / `Apply with Validate` メニューと、CI 向け `ApplyAllCLI` / `ApplyWithValidateCLI`。
- `AssetPostprocessor` によるインポート・移動・削除時の自動適用（Project Settings でオン/オフ可能）。
- 同一 `Order` 値を持つルールクラスが複数ある場合の重複警告。
- `CleanupStaleEntries`: どのルールにもマッチしなくなったアセットのエントリを、AddressTeller が管理するグループから自動削除（ラベルは削除しない）。
- スナップショット機能: 現在の Addressables 状態（グループ・アドレス・ラベル）の保存・復元（Additive / Exact）・比較。
- Project Settings 画面: 自動適用・`CleanupStaleEntries`・スナップショット保存先の設定、登録ルール一覧の表示。

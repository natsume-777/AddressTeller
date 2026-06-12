# Changelog

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### Added

- 自動セーフティスナップショット機能。`Apply All` / `Apply with Validate` メニュー実行直前に現在の状態を `SnapshotFolder/Auto` 以下へ自動保存し、設定した保持件数（既定10件）でローテーションする。`Tools/AddressTeller/Undo Last Apply` メニューで最新の自動スナップショットから Exact モードで復元できる（CleanupStaleEntries によるエントリ削除等の実質的な Undo）。Project Settings で有効/無効・保持件数を設定可能（既定ON）。CLI（`ApplyAllCLI`/`ApplyWithValidateCLI`）は対象外。
- `AddressTellerSnapshotService.BuildPredictedSnapshot`: Apply を実行せずに、適用後の状態（追加・変更・削除）の差分と、衝突・グループ未検出・ルール例外などの問題点を計算する dry-run API。`DryRunResult`（`SnapshotDiff` + `IReadOnlyList<ValidationResult>`）を返す。
- `AddressTellerService.ApplyAll` / `ValidateAll` に `IProgressReporter` を受け取るオーバーロードを追加。`EditorProgressReporter` は `EditorUtility.DisplayCancelableProgressBar` で進捗表示し、キャンセル時はその時点までの結果を返して中断する（Apply のキャンセルは部分適用のまま、巻き戻しは行わない）。
- `Tools/AddressTeller/Apply All` / `Apply with Validate` メニュー実行時、`BuildPredictedSnapshot` による dry-run 計算を行い、「追加 n 件 / 変更 n 件 / ⚠ 削除 n 件 / 問題 n 件」を確認ダイアログ（3択: 実行 / キャンセル / 詳細表示）で表示する。差分・問題がともに 0 件の場合はログのみ。「詳細表示」で結果ウィンドウを開き、同じ母集合での Apply 実行が可能。`Apply with Validate` で Validate 時点で問題があれば、確認ダイアログを出さずに結果ウィンドウ（Issues タブ）で中止。CLI（`ApplyAllCLI` / `ApplyWithValidateCLI`）は対象外。

### Changed

- AddressTeller の設定の保存先を `EditorPrefs` から `ProjectSettings/AddressTellerSettings.asset` へ変更（チーム共有のため）。旧設定は引き継がれないため再設定が必要。
- インポート時の自動適用を差分適用に変更し、変更・移動されたアセットのみを処理するようにした。プロジェクト全体の整合性チェックは引き続き `Apply All` / `Validate` / CLI のフル走査が担う。

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

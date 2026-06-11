# Changelog

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### Changed

- AddressTeller の設定の保存先を `EditorPrefs` から `ProjectSettings/AddressTellerSettings.asset` へ変更（チーム共有のため）。旧設定は引き継がれないため再設定が必要。

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

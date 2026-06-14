# アーキテクチャ

AddressTeller のコード構成・レイヤ依存・ルール評価の流れをまとめる。
公開API/内部実装の線引きの根拠は [公開APIと内部実装の境界](design-decisions.md#公開apiと内部実装の境界) を参照（本ファイルでは内容を転記しない）。

## アセンブリと名前空間

エディタコードは単一アセンブリ `Natsume777.AddressTeller.Editor`（asmdef名/rootNamespace）にまとまっているが、名前空間は2つに分かれている。

- `Editor/Core` 配下のルール定義面・評価エンジン（`AddressRuleBase`/`Match`/`Naming`/`AssetContext`/`AddressResolution`/`RuleEvaluator` など）は `namespace Natsume777.AddressTeller`（`.Editor` なし）。
- `Editor/Application`・`Editor/EntryPoints` 配下の型は `namespace Natsume777.AddressTeller.Editor`。

`Editor/Core`・`Editor/Application`・`Editor/EntryPoints` というフォルダ分けは論理レイヤの区分であり、アセンブリ境界で分離されているわけではない（名前空間は上記の通り Core のみ異なる）。レイヤ間の依存方向（後述）は、フォルダ構成の規約と `internal`/`public` のアクセス修飾子によって表現される規律であり、コンパイラが強制するのは「型が `internal` なら同一アセンブリ外からは不可視」という点までである。

テストアセンブリ `Natsume777.AddressTeller.Editor.Tests` は `Editor/AssemblyInfo.cs` の `InternalsVisibleTo` 属性によって、アセンブリ `Natsume777.AddressTeller.Editor` 内の `internal` 型（`Natsume777.AddressTeller`/`Natsume777.AddressTeller.Editor` いずれの名前空間の型も含む）を直接参照できる。`InternalsVisibleTo` はアセンブリ単位の許可であり、名前空間には依存しない。

## レイヤ依存

```
EntryPoints  ── Postprocessor / Menu / CLI / ProjectSettings / 各 Window・TreeView
     │ 依存
     ▼
Application  ── Service / Applier / RuleCollector / Pipeline / Snapshot / Report / Settings
     │ 依存
     ▼
Core         ── ルール定義面・評価エンジン（Addressables 非依存）
```

依存は上位レイヤから下位レイヤへの一方向。Core は Application・EntryPoints のいずれも参照しない。

- **Core**: Addressables に依存しない純粋なロジック（ルール定義面・評価エンジン）。
- **Application**: Addressables 統合層。`AddressableAssetSettings` 等の Addressables API を扱う。
- **EntryPoints**: Unity Editor の UI・フックの層。

各レイヤには公開面と内部実装が混在する。たとえば Core にはルール定義面（`AddressRuleBase` など）という公開API と、評価エンジン（`RuleEvaluator` など）という内部実装が同居する。同様に Application にも、公開の `AddressTellerService`/`AddressTellerSettings` と、内部実装の `AddressTellerApplier`/`RuleEvaluationPipeline` が同居する。どの型が公開でどの型が内部かの一覧と根拠は [公開APIと内部実装の境界](design-decisions.md#公開apiと内部実装の境界) を参照。

## フォルダ別の責務

### Editor/Core

Addressables に依存しないドメインモデルと評価エンジン。アセンブリは `Natsume777.AddressTeller.Editor` だが、ここに置かれる型の名前空間は `Natsume777.AddressTeller`（`.Editor` なし）である。

- ルール定義面（公開）: `AddressRuleBase`、`IAddressRuleBuilder`、`Match`、`AssetCondition`、`Naming`、`AssetContext`、`AddressRuleEntry` など
- 評価実装（内部）: `RuleEvaluator`、`AddressRuleBuilderImpl`、`AddressResolution`、`RuleExplanation` など

### Editor/Application

Addressables 統合層。ルール収集・評価パイプラインの実行・Addressables への書き込み・スナップショット・レポート・設定を担う。名前空間は `Natsume777.AddressTeller.Editor`。

- 公開: 実行エントリ（`AddressTellerService`、`AddressTellerSettings`）、スナップショット関連、進捗報告、結果型、レポートDTO群
- 内部: `AddressTellerApplier`、`RuleEvaluationPipeline`、`RuleCollector` などの組み立て系

### Editor/EntryPoints

Unity Editor へのフック・UI・CLI。名前空間は `Natsume777.AddressTeller.Editor`。`AddressTellerPostprocessor`（インポート時の自動適用）、`AddressTellerMenu`/`AddressTellerCliArgs`（メニュー操作・CI連携）、`AddressTellerProjectSettings`、各種 Window・TreeView などが置かれる。

このレイヤは Application の公開面を呼び出すだけで、ドメインロジック自体は持たない。

### Tests/Editor

NUnit の EditMode テスト。`InternalsVisibleTo` により Application/Core の `internal` 型を直接検証する。`Tests` フォルダは UPM のテストアセンブリとして扱われ、配布物には含まれない。

### Samples~

利用者向けのサンプルルール（`BasicRules`、`FolderBasedRules`、`TypeBasedRules`）。`Samples~` は UPM の規約によりデフォルトではインポートされず、利用者が Package Manager から個別に取り込む配布物である。

## ルール注入と評価フロー

1. EntryPoints（Postprocessor/Menu/CLI）は対象パス（`paths`）と `AddressableAssetSettings`（`settings`）だけを渡して `AddressTellerService.ApplyAll(...)`/`ValidateAll(...)` を呼び出す。EntryPoints 自身はルール収集を行わない。
2. `ApplyAll(paths, settings)`/`ValidateAll(paths, settings)` は進捗報告オーバーロード `ApplyAll(paths, settings, progress)`/`ValidateAll(paths, settings, progress)` を経由する。このオーバーロードが `RuleCollector.CollectEnabledRules()` を呼び出し、ルールをリフレクションで収集する。`RuleCollector` はロード済みアセンブリから `AddressRuleBase` を継承し引数なしコンストラクタを持つ型を収集し、Order 昇順・同値時は型 FullName の Ordinal 比較で決定的にソートする。テストアセンブリ（nunit.framework を参照するアセンブリ）は収集対象から除外される。収集結果はドメインリロードまで static にキャッシュされる。`CollectEnabledRules()` はこのキャッシュから、Project Settings で無効化されたルールクラスをさらに除外したものを返す。
3. 収集されたルール一覧（`IReadOnlyList<AddressRuleBase>`）は、Service の最下層オーバーロード `ApplyAll(paths, settings, progress, rules)` の `rules` 引数として注入される。この `rules` 引数がルール注入点であり、テストや特定のスコープではここに任意のルール集合を直接渡すことで `RuleCollector` によるリフレクション収集をバイパスできる。
4. Service は `RuleEvaluationPipeline.BuildSetup` でループ外の前処理（`EvaluationSetup`：既存グループ名集合・管理対象グループ集合などの不変計算）を1回だけ構築し、各アセットを Core の評価エンジンで評価したうえで、Apply の場合は `AddressTellerApplier`、Validate の場合は検証ロジックに結果を渡す。

簡易シーケンス:

```
EntryPoint → Service.ApplyAll(paths, settings)
                 └→ ApplyAll(paths, settings, progress)   ← 進捗報告オーバーロード
                       └→ RuleCollector.CollectEnabledRules()   [reflection + Order sort + 無効化除外, キャッシュ]
                       └→ ApplyAll(paths, settings, progress, rules)   ← rules 注入点
                             └→ RuleEvaluationPipeline.BuildSetup(...)
                             └→ Core 評価エンジン → Applier / Validate
```

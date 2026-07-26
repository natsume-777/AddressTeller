[English](./architecture.md)

# アーキテクチャ

AddressTeller のコード構成・レイヤ依存・ルール評価の流れをまとめる。
公開API/内部実装の線引きの根拠は [公開APIと内部実装の境界](design-decisions.ja.md#公開apiと内部実装の境界) を参照（本ファイルでは内容を転記しない）。

## アセンブリと名前空間

エディタコードはレイヤ境界と対応する2つのアセンブリに分かれている。

- **`AddressTeller.Core`**（asmdef名/rootNamespace `AddressTeller`）— `Editor/Core` 配下すべて。このアセンブリは Unity のエンジン/エディタモジュールおよび Addressables への参照を持たない（asmdefの `noEngineReferences: true` により `UnityEngine`/`UnityEditor` モジュール参照が外れる）ため、それらの API に意図せず依存することができない。ただし Unity 標準のプリコンパイル済み参照（Test Runner 関連アセンブリ等）は `noEngineReferences` の影響を受けず残っている点に注意。
- **`AddressTeller.Editor`**（asmdef名/rootNamespace `AddressTeller.Editor`）— `Editor/Application`・`Editor/EntryPoints` 配下すべて、および `Editor/` 直下に置かれる `Editor/AssemblyInfo.cs`（すなわち `Editor/Core` 以外の `Editor/` 配下すべて）。`AddressTeller.Core` と Addressables 関連パッケージを参照する。

`Editor/Core`・`Editor/Application`・`Editor/EntryPoints` というフォルダ分けは、このアセンブリ境界と直接対応するようになった。レイヤ間の依存方向（後述）は、コンパイラによって強制される：`AddressTeller.Core` は `AddressTeller.Editor` へのアセンブリ参照を持たないため、アクセス修飾子（`public`/`internal`）に関わらずそちらの型へは一切アクセスできない。

各アセンブリはそれぞれ自身の `InternalsVisibleTo` を宣言している。

- `Editor/Core/AssemblyInfo.cs`（`AddressTeller.Core` 側）は `AddressTeller.Editor` と `AddressTeller.Editor.Tests` に対して、評価エンジン系の `internal` 型（`RuleEvaluator`、`AddressResolution`、`AddressRuleBuilderImpl` 等）を許可する。
- `Editor/AssemblyInfo.cs`（`AddressTeller.Editor` 側）は `AddressTeller.Editor.Tests` に対して、自身の `internal` 型（`AddressTellerApplier`、`RuleEvaluationPipeline` 等）を許可する。

`InternalsVisibleTo` はアセンブリ単位の許可であり、名前空間には依存しない。

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

依存は上位レイヤから下位レイヤへの一方向。Core は Application・EntryPoints のいずれも参照しない。Core が独立アセンブリとなり `AddressTeller.Editor` への参照を持たないため、これは単なる規約ではなくコンパイル時に保証される。

- **Core**: Addressables に依存しない純粋なロジック（ルール定義面・評価エンジン）。
- **Application**: Addressables 統合層。`AddressableAssetSettings` 等の Addressables API を扱う。
- **EntryPoints**: Unity Editor の UI・フックの層。

各レイヤには公開面と内部実装が混在する。たとえば Core にはルール定義面（`AddressRuleBase` など）という公開API と、評価エンジン（`RuleEvaluator` など）という内部実装が同居する。同様に Application にも、公開の `AddressTellerService`/`AddressTellerSettings` と、内部実装の `AddressTellerApplier`/`RuleEvaluationPipeline` が同居する。どの型が公開でどの型が内部かの一覧と根拠は [公開APIと内部実装の境界](design-decisions.ja.md#公開apiと内部実装の境界) を参照。

## フォルダ別の責務

### Editor/Core

Addressables に依存しないドメインモデルと評価エンジン。アセンブリは `AddressTeller.Core` であり、ここに置かれる型の名前空間は asmdef の rootNamespace と同じ `AddressTeller`（`.Editor` なし）である。

- ルール定義面（公開）: `AddressRuleBase`、`IAddressRuleBuilder`、`Match`、`AssetCondition`、`Naming`、`AssetContext`、`AddressRuleEntry` など
- 評価実装（内部）: `RuleEvaluator`、`AddressRuleBuilderImpl`、`AddressResolution`、`RuleExplanation` など

### Editor/Application

Addressables 統合層。ルール収集・評価パイプラインの実行・Addressables への書き込み・スナップショット・レポート・設定を担う。名前空間は `AddressTeller.Editor`。

- 公開: 実行エントリ（`AddressTellerService`、`AddressTellerSettings`）、スナップショット関連、進捗報告、結果型、レポートDTO群
- 内部: `AddressTellerApplier`、`RuleEvaluationPipeline`、`RuleCollector` などの組み立て系

### Editor/EntryPoints

Unity Editor へのフック・UI・CLI。名前空間は `AddressTeller.Editor`。`AddressTellerPostprocessor`（インポート時の自動適用）、`AddressTellerMenu`/`AddressTellerCliArgs`（メニュー操作・CI連携）、`AddressTellerProjectSettings`、各種 Window・TreeView などが置かれる。

このレイヤは Application の公開面を呼び出すだけで、ドメインロジック自体は持たない。

### Tests/Editor

NUnit の EditMode テスト（`AddressTeller.Editor.Tests`）。`AddressTeller.Core`・`AddressTeller.Editor` の両方を直接参照し、それぞれのアセンブリが宣言する `InternalsVisibleTo` により `internal` 型を直接検証する。`Tests` フォルダは UPM のテストアセンブリとして扱われ、配布物には含まれない。

### Samples~

利用者向けのサンプルルール・ユーティリティ（`BasicRules`、`FolderBasedRules`、`TypeBasedRules`、`RuleUnitTestHelper`）。`Samples~` は UPM の規約によりデフォルトではインポートされず、利用者が Package Manager から個別に取り込む配布物である。

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

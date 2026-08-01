[English](./CONTRIBUTING.md)

# コントリビュート

AddressTeller への変更を行う前に、設計上の判断やテストの書き方について以下のドキュメントを確認してください。

## ドキュメント

- [アーキテクチャ](Documentation~/architecture.ja.md) — レイヤ構成・フォルダ別の責務・ルール評価の流れ
- [設計上の決定事項](Documentation~/design-decisions.ja.md) — アドレス・ラベル・グループの扱いと公開API/内部実装の境界
- [テストガイドライン](Documentation~/testing-guidelines.ja.md) — 設計が守る不変条件、テストを書く際の固有ルール
- [互換性ポリシー](Documentation~/compatibility.ja.md) — SemVer の保証対象・対象外

## 型の命名

### 名前空間とアセンブリ

- コアドメイン型: namespace `AddressTeller`、アセンブリ `AddressTeller.Core`。
- Addressables 統合の型: namespace `AddressTeller.Editor`、アセンブリ `AddressTeller.Editor`。
- テスト支援型: namespace `AddressTeller.Testing`。

### 公開型への `AddressTeller` プレフィックス

既定は**プレフィックスなし**です。namespace が既に型を修飾しているためです。

プレフィックスを付けるのは、その型がパッケージの境界に位置する場合、つまり次のいずれかに該当する場合に限ります。

1. **エントリポイント**: 利用者がパッケージを動かすために直接名指しする型。メニューのホスト、`AssetPostprocessor` フック、CLI引数の型、パッケージの主要操作（apply / validate / clear / snapshot / settings / report writing）のためのサービスファサード。これらの名前は `-executeMethod` の文字列、ドキュメント、Editor 自身のグローバル一覧に登場し、プレフィックスなしでは曖昧になります。
2. **シリアライズ成果物のルート**: パッケージが書き出し、利用者がバージョン管理下に置いたりCIに渡したりしうるファイルのトップレベルオブジェクト（`AddressTellerReport`、`AddressTellerSnapshot`）。

それ以外はすべて、飾りのない説明的な名前のままにします。

- ドメインモデル — `LogicalBundle`、`BundleDistribution`
- 結果・値の型 — `ValidationResult`、`ClearedEntry`、`DryRunResult`、`SnapshotDiff`
- enum — `ValidationStatus`、`ClearScope`、`ReportFormat`
- インターフェース — `IProgressReporter`、`IAddressRuleBuilder`
- 既に固有の機能名でスコープされているヘルパー — `BundleDistributionSerializer`、`BundleModeReader`

シリアライズ成果物ルートのサブオブジェクトは、プレフィックス単独ではなく**ルートの名前**を引き継ぎます: `AddressTellerReportSummary`、`AddressTellerReportIssue`。機能領域単体で曖昧さが無い場合は、その短い領域名で十分です: `SnapshotEntry`、`BundleDistributionReportEntry`。新規追加する型でこの2つの方針が競合する場合は、ルートの名前を引き継ぐ方を既定とし、同じ機能領域の既存の兄弟型が既にその短い形を確立している場合に限り、短い領域名に倒してください。

`Dto`・`Impl`・`Model` のような層を表す語を公開型名に入れないでください。それは利用者が操作できる何かではなく、パッケージ内部の実装を説明するものです。シリアライズされた形は、それが「何のシリアライズされた形か」にちなんで名付けてください。

### 例外: ルール記述用DSL

`Match`、`Naming`、`AssetCondition`、`AssetContext`、および `I*RuleBuilder` 系インターフェースは、上記の規則ならプレフィックスが必要になる場面でも短い名前のままにします。`Match` は特に `System.Text.RegularExpressions.Match` と衝突します。これらの名前はルールクラスのほぼ全ての行で入力されるため、簡潔さが名前の曖昧さ解消よりも優先されます。衝突は利用者側で `using` エイリアスによって解決します（[ルールの書き方](Documentation~/writing-rules.ja.md#match-静的クラス)を参照）。

### `internal` な型

既存の `internal` 型は、`AddressTeller` プレフィックス付きのもの（`AddressTellerApplier`、`AddressTellerApplyFlow`、`AddressTellerReportBuilder` など）も含め、現在の名前のままとします。リネームは利用者から見えるメリットのない手間です。新規の `internal` 型は公開型と同じ既定に従い、呼び出し箇所での可読性に本当に寄与する場合を除きプレフィックスを付けません。これは強制ではなく推奨です。

### 公開型を新規追加する場合

新しい公開型にプレフィックスを付けて命名することは、その型がパッケージの境界に属するという主張です。上記の1・2に照らして確認し、どちらにも該当しなければプレフィックスを外してください。現在の公開サーフェス（どの型がプレフィックスを持つかを含む）は `Tests/Editor/PublicApiApproval/PublicAPI.*.approved.txt` に記録されており、これらのいずれかをリネームすることは破壊的変更です（[互換性ポリシー](Documentation~/compatibility.ja.md)を参照）。

## テストの実行

Unity Editor でこのプロジェクトを開き、Test Runner の EditMode を実行してください。
正常終了時は skip 2・fail 0 になります。

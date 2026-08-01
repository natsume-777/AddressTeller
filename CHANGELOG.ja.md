[English](./CHANGELOG.md)

# Changelog

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。
`0.x` の間は、破壊的変更がマイナーリリースに含まれることがあります（含まれる場合は以下で **BREAKING** と明記します）。`1.0.0` 以降は [互換性ポリシー](Documentation~/compatibility.ja.md) の保証が適用され、破壊的変更はメジャーリリースに限定され、対象APIを `[Obsolete]` にするリリースを少なくとも1回挟んでから行われます。

## [Unreleased]

## [0.4.0] - 2026-08-01

### Added

- `ReportFormat` と `DistributionFormat` enum: レポート・分布サマリのエクスポート形式を表現する新規公開型。`ReportFormat` は `Json` と `Junit` に対応し（`AddressTellerReportWriter.WriteToFile` で使用）、`DistributionFormat` は `Csv` と `Markdown` に対応する（`BundleDistributionSerializer.WriteToFile` で使用）。
- `ValidationStatus.RuleConfigureFailed`: ユーザールールの `Configure()` メソッドが例外を投げた場合に返される。問題のあるルールはスキップされ（エントリ0件として扱われ）、評価は継続される。どのルールに構成問題があるかを特定するのに役立つ。
- ルール構成エラーが存在する場合、`Undo Last Apply` ダイアログと `Explain` ウィンドウに警告が表示されるため、利用者は報告された結果が不完全であることに気づくことができる。
- `AddressTellerReport.SchemaVersion`: `AddressTellerSnapshot.SchemaVersion` と対称の新規フィールド。既定値は1で、`AddressTellerReportBuilder.Build` が設定する。
- `AddressTellerReport.CurrentSchemaVersion`: 新規公開定数（`= 1`）。`AddressTellerReport.SchemaVersion` の既定値であり `FromJson` が比較に使う値そのもの。従前はこの値が `AddressTellerReportBuilder` 側の internal 定数としてのみ存在しており、公開APIの承認テストベースラインの対象外だった。`AddressTellerReportBuilder` は自前の定数を持たず、この新しい公開定数を参照するようになった。
- `AddressTeller.Testing.RuleInspector`: 実際のAddressablesプロジェクトなしにルール構成結果を検査できる公開API。`Collect()` でルールが登録した全エントリを取得、`IsUnresolvedDefaultGroup()` で未解決の `GroupDefault()` を判定、`DisplayGroupName()` でグループ名を表示形式に整形。ルール単体テストの充実を実現し、`RuleUnitTestHelper` サンプルで従前使われていた独自 Fake ビルダーに置き換わる。

### Fixed

- `RestoreExactWithRemoval` API を新設。`Undo Last Apply` が確認ダイアログの件数どおりにエントリを削除するようになり、従前のエントリ未削除状態を改正した。
- ルール収集がコンストラクタ例外またはオープンジェネリック型で中断しなくなり、問題のあるルールはスキップして評価を継続する。
- 設定フォルダの除外判定がパス区切り文字を含めるようになり、隣接するフォルダ名（例: `AddressableAssetsData_Backup`）の誤除外を防止。
- クリーンアップがラベルのみルールにマッチするエントリを誤削除しなくなり、`ValidationStatus.LabelsOnly` を新設してラベルのみマッチを正しく追跡。
- `CollectManagedGroups` が `null` 値と未解決の `GroupDefault()` センチネルを管理対象グループ一覧から除外するように修正。
- `ApplyAll` が `paths: null` で呼ばれたときの `NullReferenceException` を修正。
- `AutoCreateMissingGroups` 実行後の同一グループに対する重複警告・処理を修正。
- 結果ウィンドウで `Context` が `null` のときに発生しうる `NullReferenceException` を修正。
- スナップショット保存が同一秒内で上書きされる問題を修正し、書き込み失敗時のエラーハンドリングを改善。
- スナップショットファイル関連ヘルパーの重複とフォルダ境界判定を修正。
- `SnapshotFolder` パストラバーサル脆弱性を修正し、パスをプロジェクトディレクトリ内に制限するレンジチェックを追加。
- `Apply All` がプログレスバー表示・キャンセル機能に欠けていた問題を修正し、`EditorProgressReporter` を統合。
- `Naming` クラスのコメント誤りを修正。
- `AssetContext.PathSegments` の配列割り当てをキャッシュ化して削減。
- ラベルのみルールが管理外グループのエントリに所有権判定なしにラベルを書き込んでいた問題を修正し、管理対象グループのみに制限。
- 自動セーフティスナップショット保存に失敗した場合、従前は無視されていた。`Apply All` / `Apply with Validate` はエラーを適切に処理して実行を停止し、ユーザーに通知するようになった。
- スナップショット保存時の `Directory.CreateDirectory` が失敗する場合（不正なパスやパーミッション不足など）、例外を発生させずに適切に処理するようになった。
- スナップショット復元時、グループ名の重複があると例外で中断していた問題を修正。重複を許容し警告として報告するようになった。`AddressTellerSnapshotService.Restore` と `BundleModeReader` の両方で対応。
- `AssetFilter.ShouldExcludeByPath`: `path.Replace()` 呼び出し前に null チェックを追加し、NullReferenceException を防止。
- スナップショット JSON 読み込み（`LoadFromFile`）: 必須フィールド検証を GroupName・Entries にも拡張（従来の Guid チェックに加えて）。
- Project Settings の Postprocessor order 欄: クランプ後の実効値（0入力時は1000）が UI 表示に反映されない不整合を修正。
- `ExportDistribution`: ファイル書き込み失敗時に、従前はエラーを握りつぶしていたが、エラーダイアログを表示するようになった。
- `AddressTellerSnapshotService.Restore`/`RestoreExactWithRemoval`/`Diff` は、必須引数が null の場合、従前の無防備な `NullReferenceException` ではなく `ArgumentNullException` を送出するようになった（`AddressTellerClearService.Clear` の既存の契約と統一）。このパッケージの null 引数方針（明示的なエントリポイントは例外を送出する、`AddressTellerService.*` のような既定値フォールバックを持つメソッドは送出しない）を該当箇所の XML ドキュメントコメントに明記した。
- `AddressTellerSettings.DisabledRuleClassNames` は、内部リストの実体ではなく防御的コピーを返すようになった。返り値を変更しても永続化された設定には影響しなくなった。

### Changed

- **BREAKING**: ドメインモデル・評価エンジン（`Editor/Core/` 配下）を独立アセンブリ `AddressTeller.Core` に分離した。asmdef 名は `AddressTeller.Core` で、namespace は `AddressTeller` のまま変わらない。プロジェクトの独自 asmdef が `AddressTeller.Editor` を参照している場合、`AddressTeller.Core` も `references` に追加する必要がある。理由: `AddressTeller.Editor` の公開API が Core 型を露出しているため（例: `ValidationResult.Context` は `AddressTeller.AssetContext`、`AddressTellerService.ApplyAll(..., rules)` は `IReadOnlyList<AddressTeller.AddressRuleBase>` を受け取る）。コンパイラは両方のアセンブリが `references` に必要。
- `IsOk=true` の検証通知（例: `GroupWillBeCreated`）が、全エントリポイント（Postprocessor・Menu・ApplyFlow）で `Error` ではなく `Warning` としてログされるようになった。これにより情報通知と実エラーが区別される。
- `AddressTellerMenu.Validate()` は、エラー（`IsOk=false`）が1件以上ある場合に結果ウィンドウ（Issues タブを表示）を開くようになった。従前はコンソール出力のみだった。
- **BREAKING**: `BundleModeReader.ReadBundleModes()` および `BundleDistributionSummarizer.Build()` のシグネチャに `out` 引数（グループ名重複の警告）が追加された。呼び出し元はこの新しい引数を受け入れる必要がある。
- **BREAKING**: `IAddressRuleBuilder.Address()` を同一ルール上で2回呼び出すと `InvalidOperationException` を投げるようになり、`Where()` と同様の早期検出を実現。従前は重複アドレス指定が無警告で上書きされていた。
- `ApplyAll`、`ValidateAll`、`BuildPredictedSnapshot` は、ルール構成エラーが検出された場合、stale entry cleanup（DeletedAssets 追跡）をスキップするようになった。問題のあるルールが管理するエントリの誤削除を防ぐため。
- `ApplyAll` / `Apply with Validate` メニュー実行時、自動セーフティスナップショット保存に失敗した場合はApplyを中止するようになった（`ClearAll` の fail-fast 設計と対称にするため）。
- `ClearAll` メニューおよび `ClearCLI` コマンドは、ルール構成エラーが存在する場合は中止するようになった（CLIではexit code 3）。
- **BREAKING**: `AddressTellerReportWriter.WriteToFile` と `BundleDistributionSerializer.WriteToFile` は、生文字列（`"json"`/`"junit"`、`"csv"`/`"markdown"`）の代わりに `ReportFormat` / `DistributionFormat` enum を引数に取るようになった。CLI のテキスト引数（`-addressTellerReportFormat`）自体には影響しない。公開API側の型のみの変更。
- **BREAKING**: `SnapshotDiff.Added`/`Removed`/`Changed` が `List<T>` から `IReadOnlyList<T>` に変更された。これらのコレクションを直接変更していたコードは修正が必要。
- **BREAKING**: `SnapshotDiff` と `DryRunResult` の引数なしパブリックコンストラクタが `internal` になった。これらの型はライブラリのスナップショット・dry-run API からのみ生成される想定。テストは `InternalsVisibleTo` 経由で引き続き構築できる。
- **BREAKING**: `ValidationResult` と `AddressCandidate` の公開コンストラクタが `internal` になった。これらの型はライブラリ内部のルール評価パイプラインからのみ構築される想定。テストは `InternalsVisibleTo` 経由で引き続き構築できる。
- **BREAKING**: `AddressTellerPostprocessor` が `sealed` になった。
- **BREAKING**: `AddressTellerService.RemoveEntriesForDeletedAssets` が `void` ではなく `IReadOnlyList<ClearedEntry>`（実際に削除されたエントリ一覧）を返すようになった。結果を握りつぶさず返す `ApplyAll`/`ValidateAll` の流儀に揃えた。
- **BREAKING**: `AddressTellerExplainReport`、`AddressTellerExplainAsset`、`AddressTellerExplainRule` が `internal` になった（従前は `public`）。パッケージ外部からこれらの型を構築・取得するサポートされた経路は存在しない。
- **BREAKING**: `AddressTellerCliArgs.ReportFormat` が `string` ではなく `ReportFormat?` になった。この値を生文字列（`"json"`/`"junit"`）として読んでいたコードは `ReportFormat` enum との比較に修正が必要。
- `RuleUnitTestHelper` サンプル: `DefaultGroupSentinel` 定数を削除。`Collect()` がパッケージ本体の `RuleInspector` 公開API に委譲するようになり、パッケージ本体と同じビルダーコントラクトを保証。同一グループへの `Where()`/`Address()` 2回目呼び出しが `InvalidOperationException` を投げるようになった。新規ヘルパーメソッド `IsUnresolvedDefaultGroup()` / `DisplayGroupName()` をテスト内での未解決デフォルトグループセンチネルの判定・表示用に追加。
- **BREAKING**: `LogicalBundleDto` を `BundleDistributionReportEntry` にリネームした。C# API のみの変更であり、JSON レポート出力（フィールド名）は変わらない。
- `ValidationStatus`・`ClearScope`・`ReportFormat`・`DistributionFormat`・`SnapshotRestoreMode`・`BundleModeKind` の各enumメンバーに、ソースコード上で明示的な数値を付与した。これ自体は[互換性ポリシー](Documentation~/compatibility.ja.md#enum)で定めるメンバー・数値対応の凍結を強制するものではなく、将来のソース変更で数値がずれることを防ぐ仕組みではない。ただし承認テストのベースラインが各メンバーの名前と数値を記録するようになったため、意図しないリネーム・削除・数値ずれは `PublicApiApprovalTests` が検知する。挙動は変わらない（暗黙の連番も従来から0始まりの連番と一致していたため）。
- **BREAKING**: `ClearScope` enum の数値を入れ替えた。`Managed` が `0`、`All` が `1`（従前は `All = 0`、`Managed = 1`）。「破壊的操作はデフォルト安全側」というこのパッケージの原則と `default(ClearScope)` の向きを一致させるための変更。`ClearScope` を（メンバー名ではなく）数値そのもので読んでいるコードは修正が必要。`ClearScope.All`/`ClearScope.Managed` をメンバー名で参照しているだけのコードは影響を受けない。従前の `default(ClearScope)` に依存する到達可能な経路はCLI・メニューいずれにも存在しなかった。
- **BREAKING**: `AddressTellerReport.FromJson` が、`SchemaVersion` が `AddressTellerReport.CurrentSchemaVersion` より大きいレポートを拒否するようになった。未知の形状のレポートをそのまま返す代わりに `null` を返し警告ログを出力する。呼び出し側は戻り値の null チェックが必要になった。対称なのはこの拒否ポリシーのみで、`AddressTellerSnapshotService.LoadFromFile` とは異なり `FromJson` はJSONの内容検証を行わず、内部で使用する `JsonUtility` が投げる例外もキャッチしない（正確な違いは[互換性ポリシー](Documentation~/compatibility.ja.md#5-レポート出力json--junit-xml)参照）。

### Documentation

- README.md および Documentation~ 配下の全ファイルに英語版を追加した。元の日本語版は `.ja.md` ファイル（例: `README.ja.md`、`architecture.ja.md`）として保存し、各ファイル冒頭に言語切替リンクを追記した。
- `CONTRIBUTING.md` を英語版に書き換え、日本語版を `CONTRIBUTING.ja.md` として保存した。
- `CHANGELOG.md` を英語版に書き換え、日本語版を `CHANGELOG.ja.md` として保存した。
- `writing-rules.md`: テスティングセクションを追加し、`RuleInspector` API と `RuleUnitTestHelper` サンプルを使ったルールクラスの単体テスト方法を解説。
- `operations.md`: Samples 一覧に `RuleUnitTestHelper` を追記。
- `AddressTellerSettings.CleanupStaleEntries` のXML docと `design-decisions.md`: ラベルは削除されないという誤った記述を訂正した。stale entry削除時に、アドレスとラベルの両方が削除されることを明記。
- 全公開API XMLドキュメントコメントを英語に統一・変換し、これまでドキュメントのなかった公開メンバー（IntelliSense表示）へのドキュメントを新規付与しました。
- [互換性ポリシー](Documentation~/compatibility.ja.md) を新設した。公開C# API・CLIエントリポイント/引数・exit code・レポート/スナップショット/設定ファイルの形式・メニューパス・ルール記述の挙動のうち、SemVerで保証される範囲を一覧化し、`ValidationStatus` 等の open enum としての契約も明記した。
- `CONTRIBUTING.md`: 型命名に関するセクションを追加し、公開型に `AddressTeller` プレフィックスを付ける基準（エントリポイントとシリアライズ成果物のルート型のみ）と、ルール記述用DSL（`Match`・`Naming` 等）における命名上の例外を明文化した。
- `operations.md`: `BundleDistribution` JSONセクションの説明にあった誤ったキー名の大文字小文字表記（`bundleDistribution`・`totalLogicalBundleCount`・`unknownGroupCount`）を修正した。`JsonUtility` は大文字小文字の変換を一切行わないため、実際の出力はC#のフィールド名そのまま（`BundleDistribution`・`TotalLogicalBundleCount`・`UnknownGroupCount`）になる。旧来の（誤った）表記を前提にCIパーサを書いていた場合は修正が必要。
- `operations.md`: `PostprocessOrder` の `0` が「未設定」を表す予約値であり、明示的に `0` を指定しても既定値の `1000` として扱われることを明記した。
- `writing-rules.md`: `System.Text.RegularExpressions` も使用するルールファイルでは、2つの `Match` 型を区別するため `using Match = AddressTeller.Match;` を追加するとよい旨を追記した。
- `operations.md`: `-addressTellerDisableRules`/`ClearCLI` に追加された exit code 3 の2条件（`-addressTellerDisableRules` への未知のルールクラス名指定、および `ClearCLI` の `scope=managed` で `managedGroups` の信頼性を損なうルール構成エラー）を記載した。
- [互換性ポリシー](Documentation~/compatibility.ja.md) に、`BundleDistribution` レポートセクションの「常に存在する」という意味論を明記した。このフィールドは省略されることも JSON の `null` になることもない。算出できなかった場合（`DryRunResult.After` が無い・`AddressableAssetSettings` が渡されなかった・算出処理自体が例外を投げた）は、省略されるのではなく全フィールドがC#の既定値（`Bundles: []`・件数0・空の `Disclaimer`）のオブジェクトとして出力される。

### 検証

- Addressables 2.8.1〜3.1.0 との互換性確認は事前に実施済み（その時点では 353 件の EditMode テストで実施）。
- 現在の EditMode テストスイート: 502 pass / 0 fail / 2 skip（計504件）。最低 Addressables バージョン要件は 2.8.1 のまま変更なし。

---

## [0.3.0] - 2026-06-14

### Added

- `IAddressRuleBuilder.GroupDefault()` を追加。`Group("名前")` の代わりに使うと、Addressables の `AddressableAssetSettings.DefaultGroup` にアドレス・ラベルを付与する。DefaultGroup は評価時に解決されるため、DefaultGroup をリネームしても追従する。`Where`/`Address`/`Label` は `Group()` と同様にチェーンできる。
- `ValidationStatus.DefaultGroupUnavailable` を追加。`GroupDefault()` を使うルールが存在するが `AddressableAssetSettings.DefaultGroup` を解決できない場合に返され、該当アセットへの書き込みはスキップされる（`IsOk = false`）。
- Project Settings の AddressTeller 画面に「Postprocessor の実行順序」設定を追加（`AddressTellerSettings.PostprocessOrder`、既定値1000）。`AddressTellerPostprocessor.GetPostprocessOrder()` がこの値を返し、他のAssetPostprocessorとの実行順序を調整できる。
- `ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI` に `-addressTellerDisableRules <FullName>[,...]` を追加。永続設定（Project Settings）の無効化ルールとの和集合をCLI実行時のみ一時的に除外できる（CI実行時のデバッグ用ルール除外などを想定）。指定したFullNameが既知のルールクラスに一致しない場合はexit code 3で停止する。この除外はCLI実行限定で、Postprocessor/メニューには影響しない。
- Project Settings の AddressTeller 画面に「管理対象グループ」一覧（折りたたみ表示）を追加。`CleanupStaleEntries`/`AutoCreateMissingGroups`が対象とする、有効なルールが参照しているグループ名を確認できる。これらのグループに手動で登録したエントリは、対応するルールがなければ削除対象になる旨も説明文に明記した。

### Documentation

- Documentation~/operations.md のCI連携セクションに `CheckCLI` の記載が漏れていたため追記した。exit code表は3つのCLIメソッド（`ApplyAllCLI`/`ApplyWithValidateCLI`/`CheckCLI`）共通であることを明記した。
- `CleanupStaleEntries` の説明（Project Settings画面・operations.md）にあった「ラベルは削除されません」という誤った記述を修正。エントリ削除（`RemoveAssetEntry`）により、アドレスとAddressablesラベルの両方が失われる。
- `design-decisions.md`/`operations.md`に、管理対象グループ内に手動で登録したエントリも、対応するルールがなければ`CleanupStaleEntries`の削除対象になることを明記した（従来は「管理外グループには触れない」という保証のみが記述されており、逆方向の挙動が書かれていなかった）。

### Changed

- Project Settings の AddressTeller 画面のセクション順序を「自動適用 → 登録されているルール → 運用アクション → スナップショット」に変更し、最も確認頻度の高い「登録されているルール」一覧が画面途中で切れないようにした。
- Project Settings の「自動適用」セクション見出しを「適用・検証の挙動」に変更。`CleanupStaleEntries`/`AutoCreateMissingGroups`はインポート時の自動適用に限らず Apply All/Validate/CLI/スナップショット dry-run など全エントリポイント共通の設定であり、「自動適用」という見出しは誤解を招くため。
- `Tools/AddressTeller/Clear All Addresses & Labels...`（メニュー）および `ClearCLI`（`-addressTellerClearScope` 未指定時）の既定スコープを `All`（全エントリ）から `Managed`（AddressTeller が管理するグループのエントリのみ）に変更。全エントリをクリアする場合は `-addressTellerClearScope all` を指定する。
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

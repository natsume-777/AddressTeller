[English](./CHANGELOG.md)

# Changelog

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。
`0.x` の間は、破壊的変更がマイナーリリースに含まれることがあります（含まれる場合は以下で **BREAKING** と明記します）。`1.0.0` 以降は [互換性ポリシー](Documentation~/compatibility.ja.md) の保証が適用され、破壊的変更はメジャーリリースに限定され、対象APIを `[Obsolete]` にするリリースを少なくとも1回挟んでから行われます。

## [Unreleased]

## [0.7.0] - 2026-09-24

### Added

- `ValidationResult.IsBlocking`: 1件の validation 結果が書き込みを中止すべきかどうかを表す新しい公開
  プロパティ。定義は `!IsOk && Status != ValidationStatus.DuplicateAddress`——この判定基準はこれで一本化され、
  これを必要とする内部ロジック（Apply with Validate の中止判定、Apply 側 CLI の exit code、JUnit の failure
  判定）は同じ条件を複数箇所で再実装せず、このプロパティを参照するようになった。Apply All（Validate を
  先行させない場合）は中止判定の対象となる dry-run issue を持たないため、これを参照しない。`CheckCLI` の
  exit code は影響を受けない——中止すべき書き込みが無いため、引き続き意図的に `IsOk` の否定のみを使う。
- JSON レポート: `Issues[]` の各エントリに、そのエントリの元になった issue の `ValidationResult.IsBlocking` を
  反映する `Blocking`、`ValidationResult.IsOk` を反映する `Ok`（いずれも bool）フィールドを追加。これにより、
  ある issue が `CheckCLI` の exit code 2 判定（`!Ok` を使う）と Apply 系 CLI の exit code 2 判定（`Blocking`
  を使う。dry-run に関する但し書きは `Blocking` 自身のドキュメント参照）のどちらに数えられるかを消費者が
  判別できるようになる。どちらの値も単独では実際の exit code を再現しない——`CheckCLI` の exit 1 は `Drift[]`
  にも依存し、実行の実際の exit code は常に `Summary.ExitCode` である。`SchemaVersion` は `1` のまま
  （非破壊なキー追加）。
- `ValidationStatus.SettingsUnavailable`: 設定ファイルが読み込めなかった場合に返す新しいステータス。
  `AddressTellerService.ApplyAll`/`ValidateAll` と `AddressTellerSnapshotService.BuildPredictedSnapshot`
  は中核オーバーロードで設定ファイルをゲートするようになり、失敗した場合は既定値のまま黙って動くのではなく
  このステータス1件だけを結果として返す。`AddressTellerService.RemoveEntriesForDeletedAssets` は
  `ValidationResult` を返さない API のため、同じ失敗時はエラーをログして空リストを返す。これらのゲートは
  いずれもそれ以外の場面では静か（ログしない）——ログを出す責務は、既にゲート済みの呼び出し元（メニュー・
  CLI・Postprocessor）側に残す。
- Project Settings: 設定ファイルが読み込めない状態になると「Back Up Broken File (.bak) and Recreate with
  Defaults」ボタンが表示されるようになった。確認ダイアログ（「Auto-apply on import」「Remove unmatched
  entries」を含む全設定が既定値に戻る旨）を経てから、壊れたファイルをタイムスタンプ付きの名前で退避し
  （複数回復旧しても以前の退避ファイルを上書きしない）、既定値の新しい正常なファイルを書き直す。
  ボタンを押した時点で（他の何かにより）既にファイルが読める状態へ直っていた場合は、ファイルへは
  一切触れず退避も行わない。ファイルが読めない間は全ての設定項目が無効化される——各 setter が
  この状態で例外を投げるようになったため（詳細は下記 Changed 参照）、操作させても保存できないため。
- `ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI` / `ClearCLI` は、設定ファイルがまだ存在しない状態で
  実行されると Info として1行ログするようになった（`No settings file; using defaults.`）——既定値のまま
  実行されたことが CI のログから確認できるようにするため。Project Settings 画面にも同じ内容を1行表示する。
  import 時の自動適用（Postprocessor）は意図的にこのログを出さない——既定値のまま意図的に運用している
  プロジェクトで、import のたびにログが出るのを避けるため。
- `ValidationStatus.UnmatchedEntryKept`: `CleanupStaleEntries` が OFF の間、`ValidateAll` と
  `BuildPredictedSnapshot` の全呼び出し元（`Validate`、`Apply with Validate`、`CheckCLI`、Preview 系
  ウィンドウ）が報告する、書き込みを止めない新しい通知専用ステータス（`IsOk` は常に true）。`CleanupStaleEntries`
  が ON なら削除されていたはずの所有グループ内エントリ——どのルールにもマッチしなくなったもの、または
  パスが構造的に無効なもの——を示し、設定を ON にする前にその影響を確認できるようにする。コンソールには
  1件ずつではなく件数のみの1行サマリを出す（個別の内容は常に Result Window と JSON/JUnit レポートにある）。
  `AddressTellerPostprocessor` によるインポート時の差分適用ではこのステータスは報告されない。
- `ValidationStatus.BlockedByRuleError`: あるアセットに対して単独の勝者アドレス候補が確定した（同点なし）
  にもかかわらず、勝者の `Order` 以下（同点含む・つまり勝者と同等以上に優先される立場）を持つアドレス産出
  ルールがそのアセットの評価中に例外を送出した場合に返す新しいブロック系ステータス（`IsOk=false`、
  `IsBlocking=true`）。これは通常、勝者とは別の、勝者と同等以上に優先されるルールだが、勝者となったルール
  そのものである場合もある（`AddressSelector` は成功して勝者候補を出したものの、同じルールチェーン上の
  後続の `LabelSelector` が例外を送出したケース）。そのアセットには一切書き込まれない——勝者のアドレスも、
  マッチした他のルールのラベルも書かれない。`ApplyAll`・`ValidateAll`・`BuildPredictedSnapshot` はいずれも
  同じ評価処理を共有するため、同じ判定になる。例外を送出したルールの `RuleError` が必ず同じ結果リストの中で
  合わせて報告される——`RuleError` が原因、`BlockedByRuleError` がそのアセットの書き込みが見送られたという
  結果。Explain ウィンドウでは同じ内容を UI 上で表示する——例外を送出したルールはそのアセットの `[Error]`
  行として現れ、結論は「Blocked by rule error」になる。Explain ウィンドウで勝者になるはずだったアドレス候補の
  `[Match]` 行は、実際には書き込みがブロックされている場合に `(adopted)` ではなく、その旨を明示するように
  なった。ルール例外が原因だとその行だけでは特定できないステータス（`GroupNotFound`、`InvalidAddress`、
  `DefaultGroupUnavailable` 等）では、ルール例外が原因であるかのように誤解させないよう、中立な
  「not written」表示にする。コンソールでは `BlockedByRuleError` を（`UnmatchedEntryKept` と同様に）
  個別には出さず件数だけの1行サマリにまとめる——ただしこちらは実際の問題（IsOk=false）なので `LogError`
  にする（1つのルール例外が多数のアセットへ波及しうるため）。この結果の `ValidationResult.Message` は
  例外本文そのものを再掲せず、ブロックの原因になったルール名と `Order` のみを示す——本文は対になる
  `RuleError` 側にすでに載っているため。
- `ValidationStatus.DuplicateAssetEntry`: 同一アセット（GUID）が2つ以上の Addressables グループに同時に
  エントリを持つ場合に返す新しいブロック系ステータス（`IsOk=false`、`IsBlocking=true`）。Addressables
  自身はグループを跨いだ重複除去を行わないため、一度この状態になると（例えば2つのブランチが別々の
  グループへ同じアセットを追加した状態を VCS のマージが合成してしまった場合など）そのまま残り続ける。
  `ApplyAll`・`ValidateAll`・`BuildPredictedSnapshot` は、そのランで*いずれかの*アセットを評価・書き込む
  前にこれを検出し、「1アセットにつき、どのエントリを対象とすべきか定義できない」状態のままルールを
  評価するのではなく、`DuplicateAssetEntry` のエントリ（重複しているアセットごとに1件、関与するグループ・
  アドレスを列挙）だけを返す。`RemoveEntriesForDeletedAssets` は `ValidationResult` を返せないため、同じ
  内容をエラーとしてログに出し、そのランでは何も削除しない点だけが異なる。Explain ウィンドウはこの影響を
  受けず、引き続きアセットごとに動作する。

### Changed

- **BREAKING**: `CleanupStaleEntries`（「マッチしなくなったエントリを削除する」）と
  `PostprocessEnabled`（「インポート時に自動適用する」）の既定値が、どちらも `true` から `false` に
  変わった。従来の挙動を維持するには `Project Settings > AddressTeller` で両方を ON にすること。
  AddressTeller をゼロから導入する場合に ON にする推奨順序は [クイックスタート](README.ja.md#クイックスタート)
  を参照。
- **BREAKING**: 設定ファイルが読み込めない状態のとき、各設定プロパティの setter（`SetRuleEnabled` 含む）
  が、既定値や古い値のまま黙って上書きするのではなく `InvalidOperationException` を投げるようになった。
  これは全 setter を単一の書き込み経路（`AddressTellerSettingsAsset.Mutate`）へ集約した副作用であり、
  その経路が保存に関して実際に何を変えたかは下記 Fixed を参照。
- **BREAKING**: `IAddressRuleBuilder.Group(groupName)` が、`groupName` に `/` または `\` を含む場合
  `ArgumentException` を投げるようになった。従来は Addressables が実際には決して作らない名前を黙って
  受け入れていた。Addressables はグループの作成・改名時にこれらの文字を `-` に置き換えるため、
  置換前の名前をルールに書いても一致することはない——**Auto-create missing groups** が ON の場合は
  さらに悪く、一致しないまま実行のたびに意図せず重複したグループを新規作成し続けていた。既に
  `/` `\` を含まない名前を使っているルールには影響しない。
- **BREAKING**: アドレス産出ルールがあるアセットの評価中に例外を送出し、その `Order` が実際の勝者候補の
  `Order` 以下（同点含む）だった場合、`Apply`/`ApplyAll` はもはやその低優先勝者のアドレスを黙って書き込まない
  ——書き込みは一切行われず、`RuleError` と合わせて `ValidationStatus.BlockedByRuleError` が報告される。
  従来は例外を送出したルールだけが報告され、低優先候補のアドレスが、あたかも優先度の高いルールが
  最初からマッチしなかったかのように書き込まれていた。例外を送出したルールより純粋に優先度が高い
  （Order がより小さい）勝者の書き込みは、この仕組みではブロックされない——例外を送出したルールが
  そもそもその勝者に優先度で勝てなかったはずだからである。また `Address()` を呼ばないラベルのみルールの
  例外は、その `Order` に関わらずこの形で書き込みをブロックしない。
- **BREAKING**: `ClearCLI` が、同一アセットが2つ以上の Addressables グループに同時にエントリを持つ状態
  （`ValidationStatus.DuplicateAssetEntry`）を検出した場合、クリア前スナップショットの保存や削除を行う前に
  新しい exit code 2 で終了するようになった。詳細は [互換性ポリシー: Exit Code](Documentation~/compatibility.ja.md#4-exit-code)
  を参照。

### Fixed

- 各設定プロパティの setter（`SetRuleEnabled` 含む）が、単一の書き込み経路（`AddressTellerSettingsAsset.Mutate`）
  を経由するようになった。この経路は、前回の読み込み以降ディスク上のファイルが変化していれば（例えば
  `git pull` による更新）書き込み前にまず読み直し、変更を複製に適用してから、ディスクへの書き込みが実際に
  成功した場合のみメモリ上の値を差し替える。従来は、ディスク上で変化したファイルを古いメモリ上のコピーで
  上書きしてしまうことがあり、また書き込みに失敗した場合もメモリ上の値だけが新しい値のまま残り、設定 API
  が報告する値と実際に保存されている値の食い違いが広がっていく余地があった。この経路が使う no-op 判定
  （setter に既に持っている値を代入した場合は書き込みをスキップする）は、両辺とも設定ファイルのマーカーを
  埋める前の状態で比較するため、設定ファイルがまだ存在しない状態でプロパティへ既定値と同じ値を代入しても
  ファイルは作られない。
- Project Settings: setter の呼び出しが（読み込みゲートによる拒否ではなく）ディスクへの書き込み自体に
  失敗した場合でも、変更したコントロールに実際には保存されなかった値が表示され続けることがなくなった——
  ゲート失敗時に既に備えていた「表示を戻してログする」処理が、書き込み失敗の場合も同様にカバーするように
  なった。
- import 時の自動 Apply（Postprocessor）が、設定ファイルが壊れたままの間、同じエラーを import のたびに
  ログし続けなくなった——最初の1回だけログし、ファイルが変化する（またはエラー内容自体が変わる）まで
  静かになる。メニュー・CLI 等の他の入口は、これまでどおり実行のたびに毎回ログする。
- Save Snapshot、Snapshot Manager ウィンドウの Refresh、結果ウィンドウの「Apply with this content」
  ボタンから実行する経路（Apply All / Apply with Validate のダイアログを経由しない経路）も、設定ファイル
  が読み込めない場合は既定値のまま黙って処理を進めず、中止するようになった。Snapshot Manager ウィンドウは
  この場合、画面内にエラーを表示し一覧を空にする。Apply の経路でも、ボタンを押すとウィンドウが閉じるため
  Console のログだけでは中止したことに気付きにくく、ダイアログでも中止理由を表示する。
- ラベルのみのルール（`AnyGroup()`、または `Address()` を呼ばない `Group()` ルール）が、読み取り専用の
  エントリまたはそのグループにラベルを書き込まなくなった。`AddressableAssetSettings.SetLabel` 自体は
  これを検査しないため、従来はラベルのみのルールが読み取り専用のエントリにもラベルを追加できてしまって
  いた。AddressTeller は読み取り専用のエントリ・グループを他の何かが管理しているものとみなし、
  エントリが存在しないアセットに対する既存の静かなスキップ挙動と同様、Apply・Predict とも静かに
  スキップする（報告なし）。この挙動はラベルのみのルールに限る——アドレスルール（`Address()` を伴う
  `Group()`）は引き続き `CreateOrMoveEntry` 経由でエントリを移動し、その既存の副作用として `ReadOnly`
  を解除する（今回の変更による影響なし）。
- `SnapshotFolder` に空文字・空白のみの値を設定しても、プロジェクトルート直下が保存先にならなくなった。
  setter（Project Settings の「Snapshot folder」欄も同じ setter を経由する）に空文字・空白のみを渡すと、
  保存前に既定値（`AddressTellerSnapshots`）へ正規化される。既にディスク上の設定ファイルに空文字・空白のみの
  `_snapshotFolder` が入っていた場合も、読み込み時に同様にメモリ上の値だけを正規化し、Warning を1回ログする
  （ファイルへは書き戻さない）。
- `ValidateAll`・`BuildPredictedSnapshot`・公開 `AddressTellerSnapshotService.Diff` が、同一アセット（GUID）
  が2つ以上の Addressables グループに同時にエントリを持つ状態（上記 `ValidationStatus.DuplicateAssetEntry`
  参照）で `ArgumentException` を投げなくなった——従来はこの状態のまま評価に入ると未捕捉の例外で評価全体が
  落ち、報告すらされなかった。また `ApplyAll` は例外こそ出さないものの、
  `AddressableAssetSettings.FindAssetEntry` がたまたま選んだ側のエントリだけを黙って動かし、もう片方を
  記録に残さないまま消していた。
- すべての `-executeMethod` CLI 入口（`CheckCLI`・`ApplyAllCLI`・`ApplyWithValidateCLI`・`ClearCLI`）が、
  自身の文書化された分岐のどれにも該当しない例外が到達した場合にそれを catch してログに出し、exit code 1
  （ドリフトありと区別できない）で Editor プロセスが未捕捉例外のまま終了する代わりに、exit code 3
  （実行環境エラー）で終了するようになった。詳細は [互換性ポリシー: Exit Code](Documentation~/compatibility.ja.md#4-exit-code)
  を参照。
- `Undo Last Apply` と Snapshot Manager ウィンドウからの Restore も、上記と同じ重複 guid 状態を検出して
  中止するようになった——従来はそのまま `Diff`/`Restore` まで進み、
  `AddressableAssetSettings.FindAssetEntry` がたまたま見つけた側のエントリだけに作用していた。
- `Save Snapshot`、および `Apply All`/`Apply with Validate` が実行前に取る自動スナップショットも、
  同一 guid を2件含む（そのままでは二度と読み込めない——
  `AddressTellerSnapshotService.LoadFromFile` は重複 guid を明示的に拒否する）スナップショットファイルを
  書き出す代わりに中止するようになった。
- `AddressTellerClearService.Clear` が、各エントリを guid 経由（`AddressableAssetSettings.RemoveAssetEntry(guid)`、
  先に見つかったグループのエントリを解決する）ではなく、そのエントリが実際に属していたグループから直接
  （`AddressableAssetGroup.RemoveAssetEntry(entry)`）削除するようになった——同一 guid のエントリが
  クリア対象のグループと対象外のグループの両方に存在する場合に限り、従来は guid ベースの削除が
  対象外グループ側のエントリを誤って消しうる問題があった。`Clear All Addresses & Labels...` と `ClearCLI` も、
  上記 Save Snapshot と同じ理由でクリア前スナップショットを保存する前に重複 guid 状態を検出して中止する
  ようになった。
- `AddressTellerPostprocessor` が、重複 guid 状態が解消されないまま import が繰り返されても、同じ内容を
  毎回ログし続けなくなった——設定ファイルが壊れたままの場合と同じ `OncePerDistinctFailure` 方式で、
  最初の1回だけログし内容が変わるまで静かになる。また、変更と削除の両方を含む import で `ApplyAll` と
  `RemoveEntriesForDeletedAssets` がそれぞれ独立に同じ重複を検出してログしていたため、1回の import で
  同じ内容が二重にログされていた問題も解消した。
- `BundleDistributionSummarizer.Build` が、渡されたスナップショットに同一 guid のエントリが複数含まれていても
  例外を出さなくなった——公開 `AddressTellerSnapshotService.Diff` と同じフォールバック（その guid の最初の
  エントリを採用する）を使う。

### Documentation

- [適用と運用](Documentation~/operations.ja.md) の「インポート時自動適用」の説明を、無効化できる旨だけでなく
  既定が OFF であることを先に明記する書き方に修正した。すべての CLI exit code 表に `ClearCLI` の exit code 2
  （重複アセットエントリ）の行と予期しない例外のケースを追加し、[互換性ポリシー](Documentation~/compatibility.ja.md)
  と揃えた。README に、ルールクラスを置く場所についての注記を追加した——asmdef が `nunit.framework` を参照する
  アセンブリはルール収集から警告なく除外される。`Tests` フォルダが配布物に含まれないという記述を訂正した——
  `~` の付かないフォルダ名のため git URL でのインストールでもパッケージの他の部分と一緒にダウンロードされるが、
  asmdef が `UNITY_INCLUDE_TESTS` を要求するためコンパイルはされない。スナップショット保存先フォルダをバージョン管理に
  含めるかどうかの指針を追記し、トラブルシューティングに、重複した Addressables エントリを削除する前にラベルと
  アドレスを控えておく旨の注記を追加した。

---

## [0.6.1] - 2026-09-23

### Fixed

- JUnit レポート: `CheckCLI` は、そのステータスに `IsOk=false` の結果が1件でもある場合にのみ、Validation の
  `<testcase>` に `<failure>` を付けるようになった——`CheckCLI` 自身の exit code 2 判定基準と一致させたもので、
  このランで書き込む対象を含まない重複（`IsOk=true` の `DuplicateAddress` 通知）だけの場合は `<failure>` が付かない。
  `ApplyAllCLI` / `ApplyWithValidateCLI` は、Apply を実際に中止させるステータスにのみ `<failure>` を付けるように
  なった——こちらも自身の exit code 2 判定基準と一致させたもので、書き込みを止めない `DuplicateAddress` は
  `IsOk=false` として報告されている場合でも、この2つのエントリポイントからは `<failure>` が付かなくなった
  （0.6.0 が文書化した exit code の契約——`DuplicateAddress` は Apply を中止させず exit code にも影響しない——と
  一致する）。`<testcase>` 要素は従来どおり、存在する各ステータスについて生成される（変わらない）。`<failure>`
  子要素の有無のみが変わる。公開の `AddressTellerReportWriter.ToJUnitXml` / `WriteToFile` の直接呼び出しは変わらず、
  `IsOk` に関係なく、すべての Validation `<testcase>` に無条件で `<failure>` を付ける。
- スナップショット自動ローテーション: `ProjectSettings/AddressTellerSettings.json` で `Auto-snapshot retention count` を
  手動編集して 0 以下にすると、`CaptureAndSave` が apply の実行前に保存したばかりのセーフティスナップショットが、
  保存直後に同じ呼び出しの中で行われるローテーションによって——Apply 自体が始まってすらいない時点で——削除されてしまい、
  `Undo Last Apply` が復元に失敗していた。読み込み時に 1 に補正し（メモリ上のみ。JSON ファイルは書き戻さない）、
  Warning をログ出力するようになった。Project Settings UI 経由での設定は既に 1 以上に制限されている。

### Changed

- Project Settings UI: 「Managed Groups」の空リスト表示文言を、現在の所有権の定義（有効なルールが `Address()` を宣言している
  グループ）に合わせた。`IAddressRuleBuilder.Group()` のXML docに、グループが実際に作成・リネームされる際、
  Addressables がグループ名に含まれる `/` や `\` を `-` に置き換えるため、いずれかの文字を含む `Group()` の名前は
  実際のグループ名と一致しないことがある旨の注記を追加した。Writing Rules の例をそれに合わせて変更した
  （`Audio/Boss` ではなく `BossAudio`）。

### Documentation

- `Summary.Issues` は `Summary.ExitCode` に影響しない報告専用の通知も含む `Issues[]` の総件数であり、レポートが
  合格・不合格のどちらのランに対応するかは、この件数ではなく `Summary.ExitCode` で判断すべきことを明記した
  （値そのものは変わらない）。
- JUnit レポートは、apply の exit code の根拠となる実際の実行結果ではなく、Apply 前の dry-run 結果から組み立てられる
  ため、実際に書き込みを行って初めて発生しうるステータス（例: `ValidationStatus.EntryRejectedByAddressables`）は、
  `ApplyAllCLI` / `ApplyWithValidateCLI` の exit code に影響しうる一方で、そのランの JUnit 出力には一切現れないことが
  ある旨を明記した。
- 0.6.0 の設定保存先変更に関するアップグレード案内を「0.5.x からのアップデート」の独立した注記へ拡充した:
  既定値へリセットされる設定項目のより詳しい一覧、アップデート後最初の自動 apply が値を復旧する前に
  できてしまうこと・できないこと、そのタイミングに依存しない保全策を追加した。あわせて
  `ValidationResult.IsOk`・ラベルのみルールの変更・`PostprocessOrder`（いずれも BREAKING）と、
  `TypeBasedRules` サンプル（BREAKING ではない）に関する不正確・記載漏れの記述も修正した。

---

## [0.6.0] - 2026-09-22

_以下の一部の項目はこのバージョンの初回リリース後に訂正されている——何が変わったかは上の
[Unreleased](#unreleased) を参照。訂正はこの文書のみに関するもので、0.6.0 の実際の挙動は変わっていない。_

### Added

- 広いルールを特定のルールで上書きするアドレス記述: `AddressRuleBase.Order` が、2件以上のマッチしたルールが
  同一アセットへアドレスを発行したときの優先順位も兼ねるようになった。例は
  [ルールの書き方: アドレスの優先順位と競合](Documentation~/writing-rules.ja.md#評価ルールと挙動) を参照。
- アセット間のアドレス重複検出: `Validate` / `Apply All`（および `CheckCLI` / `ApplyAllCLI` /
  `ApplyWithValidateCLI`）が、別々のアセットが同じアドレスに解決された場合を報告するようになった
  （`ValidationStatus.DuplicateAddress`）。AddressTeller 自身がその重複アドレスのどちらかを
  このランで書き込む場合はエラー、そうでなければブロックしない通知として扱う。詳しくは
  [設計上の決定事項: アドレスの優先順位と競合](Documentation~/design-decisions.ja.md#アドレスの優先順位と競合) を参照。

### Changed

- **BREAKING**: `AddressRuleBase.Order` が、評価順序だけでなくアドレスの優先順位も兼ねるようになった。
  2件以上のマッチしたルールが同一アセットへアドレスを発行した場合、これまでは常に競合だったが、
  `Order` が最小のものが採用されるようになった。競合になるのは最小 `Order` のマッチが同点のときのみ。
  これまで `ConflictingAddress` として書き込まれずに残っていたアセットがあるプロジェクトでは、
  アップデート後最初の `Apply All` の前に Preview（dry-run）で確認すること——それらのアセットは
  マッチしたルールのうち最小 `Order` のものに従って書き込まれるようになる（import 時の自動適用が
  有効な場合はそちら経由でも同様）。
  [設計上の決定事項: アドレスの優先順位と競合](Documentation~/design-decisions.ja.md#アドレスの優先順位と競合)
  と [互換性: ルール記述の挙動](Documentation~/compatibility.ja.md#9-ルール記述の挙動) を参照。
- **BREAKING**: 設定の保存先が `ProjectSettings/AddressTellerSettings.asset` から
  `ProjectSettings/AddressTellerSettings.json` に変わった。旧 `.asset` ファイルはもう読み込まれず、
  そこからの移行も行わない——この `.json` ファイルがまだ無いプロジェクトでこのバージョンが最初に動く際、
  設定はすべて既定値にリセットされる。**Auto-apply on import** と **Remove unmatched entries** はどちらか
  オフにしていた場合 ON に戻り、ルール一覧で無効化していたルールクラスはすべて再度有効に戻り、
  **Snapshot folder** は既定の `AddressTellerSnapshots` に戻る（これにより
  `Tools/AddressTeller/Undo Last Apply` と `Tools/AddressTeller/Snapshot/Manage Snapshots...` が、値を
  戻すまで別フォルダに保存されたスナップショットを見つけられなくなることがある）。**Auto-create missing
  groups** は OFF に戻る（これにより、この設定に依存していたルールで `Apply All` が `GroupNotFound`
  エラーになることがある）。このリセットが何を引き起こしうるか、どう備えるかは下の
  **0.5.x からのアップデート** を参照。詳細は
  [Settings Asset](Documentation~/compatibility.ja.md#7-settings-asset) を参照。
- **BREAKING**: 削除に関わる操作すべて——`CleanupStaleEntries`、無効パスエントリの掃除、資産削除時の削除追従、
  `ClearScope.Managed`（CLI の `-addressTellerClearScope managed` を含む）、`Undo Last Apply` の削除対象
  フィルタ——の所有権判定の対象グループが、`Group()` で参照しているだけのグループすべてではなく、ルールが
  `Address()` を宣言しているグループに変わった。いずれも「管理対象」とみなすグループの範囲が狭くなる
  ——まだ `Address()` を呼んでいない `Group("X")` ルールは、グループ `X` を対象にしなくなる。詳しくは
  [設計上の決定事項: 削除は資産単位の所有権で判定する](Documentation~/design-decisions.ja.md#削除は資産単位の所有権で判定する)
  を参照。
- **BREAKING**: ラベルのみルール（`AnyGroup()`、または `Address()` を呼ばない `Group()` ルール）が、既存
  エントリの所属グループを問わずラベルを加えるようになった——以前は、そのグループが*有効な*いずれかの
  ルールの `Group()` で参照されている場合にラベルが加わっていた（Project Settings で無効化されている
  ルールの参照はカウントされず、`AddressableAssetSettings.DefaultGroup` が取得できず未解決のまま残った
  `GroupDefault()` 参照もカウントされなかった）。この「いずれかのルール」には、そのラベルのみルール自身が
  呼んだ `Group()` も含まれる——`Address()` を呼ばない `Group("X")` ルールは、それ自身が有効である限り、
  その呼び出し自体によって、既にグループ `X` にあるエントリへはラベルを届かせられていた。以前届かなかった
  のは、どの有効なルールの `Group()` からも一切参照されていないグループにあるエントリや、`AnyGroup()` の
  場合にその参照済みグループの外にあるエントリだった。該当するルールを持つプロジェクトでは、
  アップデート後最初の `Apply All` の前に Preview（dry-run）で確認すること——これまでそのルールが
  届かなかったグループのエントリにもラベルが加わるようになる（import 時の自動適用が有効な場合は
  そちら経由でも同様）。詳しくは [ルールの書き方: AnyGroup](Documentation~/writing-rules.ja.md#anygroup) を参照。
- **BREAKING**: `ApplyAllCLI` / `ApplyWithValidateCLI` が、差分のある適用完了時に exit code 1 を返さなく
  なった——適用に成功すれば常に 0 を返す。詳しくは
  [互換性: Exit Code](Documentation~/compatibility.ja.md#4-exit-code) を参照。
- **BREAKING**: `ApplyAllCLI` / `ApplyWithValidateCLI` の JUnit レポートが、`drift` testcase に対して
  drift を `<failure>` として報告しなくなった（上記 exit code の変更と同じ理由——適用に成功した
  自分自身のレポートで CI ジョブを失敗させてはならない）。`AddressTellerReportWriter.ToJUnitXml` /
  `WriteToFile` に任意パラメータ `treatDriftAsFailure` を追加した（既定値 `true`。`CheckCLI` の従来
  挙動と一致）。詳しくは
  [互換性: レポート出力](Documentation~/compatibility.ja.md#5-レポート出力json--junit-xml) を参照。
- **BREAKING**: `AddressTellerCliArgs.TryParse` が、未知の `-addressTeller` プレフィックス引数を黙って
  無視せず、パースエラーとして拒否するようになった。詳しくは
  [互換性: コマンドライン引数](Documentation~/compatibility.ja.md#3-コマンドライン引数) を参照。
- `ApplyAllCLI` / `ApplyWithValidateCLI` / `ClearCLI` が、終了する直前に `AssetDatabase.SaveAssets()` を
  呼ぶようになった。詳しくは
  [適用と運用: CI 連携](Documentation~/operations.ja.md#ci-連携) を参照。
- 既存プロジェクトで重複アドレスが既にある場合、`CheckCLI` がこれまで 0 か 1 だったところを 2 で
  終了するようになることがある。`ApplyAllCLI` / `ApplyWithValidateCLI` / `Apply with Validate` は
  重複アドレスの有無に関わらず影響を受けない——ログ・レポートに載るのみで、中止や exit code の変化には
  つながらない。詳しくは
  [設計上の決定事項: アドレスの優先順位と競合](Documentation~/design-decisions.ja.md#アドレスの優先順位と競合) を参照。
- **BREAKING**: `!ValidationResult.IsOk` が「このランでこのアセットへの書き込みが見送られた」を常に
  意味するわけではなくなった。`IsOk=false` でも書き込みが行われるステータスが2つある。1つは
  `ValidationStatus.RuleError`（`Context` でアセットに紐づき、`ApplyAll` と `ValidateAll` の両方が返す）
  ——例外を投げたのが最も優先度の高い（Order が最小の）マッチしたルールだった場合、そのルールの候補は
  評価から単純に欠落し、代わりに他の低優先度ルールのアドレスがあればそのアセットへ書き込まれる。これは
  このリリースより前から既にそうだった。詳しくは
  [ルールの書き方: ルール内で例外が発生した場合](Documentation~/writing-rules.ja.md#評価ルールと挙動) を
  参照。もう1つは `ValidationStatus.DuplicateAddress`（単一のアセットに紐づかない——`Context` は
  `null` ——ステータスで、`ValidateAll` と、`BuildPredictedSnapshot` が返す `DryRunResult.Issues` にのみ
  含まれ、`ApplyAll` は返さない）——重複しているアドレスにこのランで AddressTeller 自身が書くものが
  含まれていれば `IsOk` は正確に `false` になるが（`HasWritableDuplicate` 参照）、書き込み自体はそれに
  関わらず行われる——`DuplicateAddress` が書き込みを止めることはない（上記 Added 参照）。`!result.IsOk`
  を Apply を中止すべきかどうかの判定に使っていたコードは、`Apply with Validate` 自身の中止判定や
  `ApplyAllCLI` / `ApplyWithValidateCLI` の exit code 判定が既に行っているように、`DuplicateAddress`
  を明示的に除外する必要がある。`Apply All` はそもそも先に Validate を実行しないため、この基準での
  中止判定自体が存在しない。`CheckCLI` は唯一これを除外しない入口である——書き込みを一切行わないため、
  その exit code 2 は単なる報告シグナルであり中止判定ではない。
- **BREAKING**: `AddressTellerSettings.PostprocessOrder` が、`0` を `DefaultPostprocessOrder`（1000）へ
  読み替える予約済みの「未設定」センチネルとして扱わなくなった。`0` を設定して `1000` 扱いになることを
  期待していた場合、これからは文字通り `0` のまま読み戻され `AssetPostprocessor.GetPostprocessOrder()`
  に渡されるようになり、他パッケージの Postprocessor との相対順序で見て、この Postprocessor が
  以前より早く動くようになる。
  詳しくは [適用と運用: Project Settings](Documentation~/operations.ja.md#project-settings) を参照。
- `TypeBasedRules` サンプル: アドレスにアセット種別のプレフィックスが付くようになった（例:
  `Player` ではなく `prefab/Player`）。同じフォルダ内で型の異なる2つのアセットが同名になる場合
  （例: `Player.prefab` と `Player.png`）に、同じアドレスへ解決されて `ValidationStatus.DuplicateAddress`
  として報告されることを避けるため。サンプルにのみ影響し、再 import した場合のみ反映される。
- **0.5.x からのアップデート:** `ProjectSettings/AddressTellerSettings.json` がまだ無いプロジェクトで
  このバージョンが最初に動く際、上記の設定保存先変更の項に挙げた設定はすべて既定値にリセットされる。
  Auto-apply on import と Remove unmatched entries はどちらも既定 ON のため、この既定状態自体が、有効化
  された瞬間に破壊的な apply を走らせうる——しかも、それを引き起こしたインポート対象のアセットだけに
  留まらない。`AddressTellerPostprocessor` の自動 apply がアドレス・ラベルを書き込み、
  （`CleanupStaleEntries` が ON のとき）マッチしなくなったエントリを削除するのは、その import で
  変更された対象アセットに限られる。一方で、`CleanupStaleEntries` が ON かつこのランで Configure() に
  失敗したルールが無い限り、パスが構造的に不正なエントリの掃除だけは、実行のたびに有効なルールが所有する
  すべてのグループの既存エントリ全件を対象に行われ、実際にインポートされたアセットには限られない。
  この同じリセットで無効から有効に戻ったばかりのルールは、初めて動いた瞬間に、この掃除の対象となる
  「所有」グループの範囲を変える。この Postprocessor へアセットインポートが届くのに、あなた自身が
  アセットを操作する必要はない——IDE でスクリプトを保存した後の再コンパイル、VCS の pull やブランチ
  切替後にフォーカスが戻ったときの Editor の Auto Refresh、更新されたパッケージ自身のスクリプトファイル
  が更新の一部としてインポートされること、のいずれもトリガーになりうる。パッケージ自体の更新が、値を
  復旧する機会を得る前にこれらのいずれかを確実に引き起こすかどうかは未確認。AddressTeller 自身は
  `Project Settings > AddressTeller` からアセットインポートを発生させない——このページで値を変更しても
  `Assets/` の外にある `ProjectSettings/AddressTellerSettings.json` を読み書きするだけである。未確認なのは、
  それ以外の要因（Editor 自体、他のパッケージ、アップデートそのもの）が、このページを開く機会を得る前に
  インポートを Postprocessor へ届けてしまうかどうかである。
  **アップデート前に**、`Project Settings > AddressTeller` から現在の値——Auto-apply on import、
  Postprocessor order、Remove unmatched entries、Auto-create missing groups、Snapshot folder、
  Auto-snapshot before Apply、Auto-snapshot retention count、各ルールクラスの有効/無効状態——を控えて
  おくこと。設定リセットの影響を受けない独立した復元手段として、手動スナップショット
  （`Tools/AddressTeller/Snapshot/Save Snapshot`）も保存しておくこと——保存先は（そして
  `Tools/AddressTeller/Snapshot/Manage Snapshots...` での一覧表示元も）現在の `Snapshot folder` なので、
  この設定をカスタムしている場合、ファイル自体はリセットの影響を受けないが、`Snapshot folder` を元の値へ
  戻すまで UI 上には再表示されない。さらに、アップデート後最初の apply が何をしても `git diff`／revert で
  戻せるよう、Addressable Groups のデータ（既定では `Assets/AddressableAssetsData`）をコミットまたは
  バックアップしておくことも検討すること。
  **アップデート後**は、自分自身でアセットインポートを発生させる前に `Project Settings > AddressTeller`
  を開き、控えておいた値を（Auto-apply on import と Remove unmatched entries を優先して）入れ直すこと。
  旧 `.asset` ファイルは、新しい JSON のキーと同じフィールド名を使う Unity の YAML ファイルである
  （一覧は [Settings Asset](Documentation~/compatibility.ja.md#7-settings-asset) を参照）——事前に値を
  控え忘れていても、そのファイルが値を知るための唯一の手がかりとして残っており、削除していなければ
  テキストエディタで直接読み取れる。このバージョンはどのみちそれを自動では二度と読まないため、値を
  控えたか（もう必要ないと判断できた時点で）削除して構わない。
  チームで運用する場合、値を入れ直した `AddressTellerSettings.json` をパッケージ更新と同じコミットに
  含めておくと、そのコミットを pull したチームメンバーの手元では、チェックアウトした時点で既に
  あなたの値が入った JSON が存在することになり、既定値にリセットされた状態で動く期間を避けられる
  可能性がある——ただし上記のインポートのタイミング問題との関係は未検証のため、確実な対策としてではなく
  提案として扱うこと。既に破壊的な apply が起きてしまった場合、`Tools/AddressTeller/Undo Last Apply` は
  その特定の apply 専用のスナップショットを持たない——import 時の自動 apply はスナップショットを取らない
  ため。直近の手動 `Apply All` / `Apply with Validate` の前に自動保存されたスナップショットまで、
  *現在の* `Snapshot folder` 設定の下で（このプロジェクトにまだ存在すればの話——`Snapshot folder` が
  既定値に戻っていて、自動スナップショットがカスタムフォルダに保存されていた場合、元の値へ戻すまでは
  何も見つからない）Exact モードで戻すことしかできず、このリセットが引き起こした apply だけでなく、
  それ以降の変更すべてをまとめて元に戻すことになる。

### 検証済み

- EditMode テストスイート: 656件（654 pass / 0 fail / 2 skip）。Unity 6000.3.8f1 + Addressables 2.8.1 で実行。Addressables の最低要件は引き続き 2.8.1。

## [0.5.0] - 2026-09-17

### Added

- `IAddressRuleGroupBuilder.IncludeFolders()` / `ILabelRuleBuilder.IncludeFolders()`:
  ルールがフォルダアセットを評価対象に含める opt-in。1ルールまたは1グループにつき1回のみ呼び出し可能。
  2回呼ぶと `InvalidOperationException` を投げる。このメソッドなしではフォルダは完全にスキップされ、述語も呼ばれない。
- `AssetContext.IsFolder`: フォルダアセットの場合に true。ファイルと区別する必要があるフォルダ opt-in ルールや、
  手作業で AssetContext を構築するテスト・ツール用に利用可能。
- `AssetContext` コンストラクタの新規オーバーロード。`isFolder` パラメータを受け取る形に加え、既存のコンストラクタ
  （`isFolder` なし）は false を既定とする形で保持される。
- `AddressRuleEntry.IncludesFolders`: このエントリがフォルダ評価に opt-in したかどうかを示す読み取り専用プロパティ。
  公開の `AddressRuleEntry` コンストラクタからは設定できず、ビルダーが生成したエントリ（`IncludeFolders()` が
  呼ばれた場合）でのみ true になる。独自の評価ループを構築するコード（テストヘルパー等）向け。
- `ValidationStatus.EntryRejectedByAddressables`: ルールがマッチしてアドレスを解決し、対象グループも実在するのに、
  Addressables 本体がこのアセットへの usable エントリ作成・移動を拒否した場合に報告される
  （パスが Addressables のエントリとして無効な場合。メイン型がエディタアセンブリ型なら Addressables はエントリを
  返さず、そうでなければ read-only のプレースホルダを作る）。対象グループ自体が存在しない場合の
  `ValidationStatus.GroupNotFound` とは区別される。このエッジケースで作られた read-only プレースホルダエントリは
  取り除かれる。

### Fixed

- Windows: `AddressableAssetSettings.ConfigFolder` はバックスラッシュ区切りで返ることがあった。
  これにより Windows では、Config Folder 配下のアセットが実際には除外されておらず、また
  `AddressTellerPostprocessor` の早期リターン判定も Config Folder 配下だけの変更を認識できず、Addressables
  設定を保存するたびに、変更された設定アセットに対する不要な差分 apply が走っていた。取得地点でフォワード
  スラッシュに正規化することで両方の挙動を修正した。除外が効いていなかった間に Config Folder 配下へ作られた
  エントリは、`CleanupStaleEntries` が有効なら下記の無効パスクリーンアップで削除される。
- Addressables の `CreateOrMoveEntry` が null を返した場合に Apply が `NullReferenceException` で中止しなくなり、
  そのアセットは `EntryRejectedByAddressables` として報告されるようになった。
- Addressables がパスを無効と判定した際に静かに作成する read-only プレースホルダエントリ（アセットのメイン型が
  エディタアセンブリ型でない場合）が残らなくなった。`Apply` がこれを取り除く。

### Changed

- **BREAKING**: フォルダは `IncludeFolders()` 経由のオプトインになった。既定ではフォルダアセットはルール評価の
  対象外で、`Where()` 述語もアドレス・ラベルセレクタも呼ばれない。フォルダのエントリを登録したいルールは、
  ビルダーで `IncludeFolders()` を明示的に（1ルール / 1 `Group()` / 1 `AnyGroup()` につき1回まで）呼ぶ必要がある。
  従前は `AssetDatabase.GetAllAssetPaths()` が返すフォルダパスもファイルと同様に評価されていたため、広い述語
  （例: `Match.All()`、`Match.InFolder(...)`）がフォルダにマッチし、配下全体を暗黙に含むフォルダエントリが
  作られることがあった。`IncludeFolders()` を呼ばないルールは、広い述語を使ってもフォルダにマッチしなくなった。
  `CleanupStaleEntries` が有効な場合、この変更前は広いルールがマッチしていたことで存在していたフォルダエントリは、
  そのルールが見なくなった時点で無マッチ扱いになり、ファイルエントリに対して以前から適用されていたのと同じ
  stale エントリクリーンアップで削除される（新しい削除の仕組みではない。対象範囲もファイルと同じで、その apply で
  実際に評価されたアセットにしか及ばない）。
- **BREAKING**: パス妥当性判定が Addressables と一致するようになった。Addressables は、ユーザーが Groups
  ウィンドウまたは Inspector の「Addressable」チェックでエントリを手動作成する場合、以下のパスを拒否する:
  `Assets/` の外かつパッケージ自身のフォルダの外にあるパス（`ProjectSettings/`、`Library/`、`Temp/` 配下など）、
  パッケージ自身の `package.json`、それ以上の階層を持たないパッケージ自身のルートフォルダ、拡張子 `.preset`・
  `.asmdef`、`/Editor/` を含むまたは `/Editor` で終わるパス、`Assets` ルート自体、そして Addressables の設定フォルダ
  （下記参照）。これらに該当するパスはルール評価の前に除外されるようになり、どのルールにも渡らず、結果も
  報告されない（他の事前フィルタ対象パスと同様）。`ValidationStatus.EntryRejectedByAddressables` は、この
  フィルタを通過したパスへの書き込みを Addressables 本体がなお拒否した場合にだけ報告される（稀なケース。
  Added 参照）。
- **BREAKING**: `CleanupStaleEntries` が有効な場合、どのルールにもマッチするかを問わず、管理対象グループ内で
  アセットパスがそもそも Addressables のエントリとして構造的に無効なエントリ（例: `ProjectSettings/` 配下や
  `.preset`/`.asmdef` パスに対する、旧バージョンの AddressTeller が作成した残骸）も掃除対象になる。上記の
  stale エントリクリーンアップとは異なり、この判定は実際にインポート・変更されたアセットとは独立に、apply の
  たびに（インポート時自動適用を含め）管理対象グループ内の全エントリに対して行われる。
- **BREAKING**: 拡張子除外判定が大文字小文字を区別するようになった。`.CS`・`.DLL`・`.PRESET` のような大文字拡張子
  を持つパスは、もはや自動除外されず評価対象になる。Addressables 本体の挙動に揃えた。
- **BREAKING**: Config Folder の除外判定が Addressables 本体と同じ「境界なしの前方一致」になり、0.4.0 で導入した
  境界付き判定を取りやめた。Config Folder と名前が前方一致するだけのフォルダ（例: 設定フォルダが
  `AddressableAssetsData` の場合の `AddressableAssetsData_Backup`）も Groups ウィンドウ・Inspector と同様に
  除外される。`CleanupStaleEntries` が有効なら、それらの配下のアセット（ファイル・フォルダとも）の管理グループ内
  エントリは次回の apply で削除される。
- **0.4.x からのアップデート:** `CleanupStaleEntries` が有効（既定）の場合、アップデート後最初の apply で管理
  グループ内の既存エントリが削除されることがある。対象は、`IncludeFolders()` で opt-in したルールのどれにも
  マッチしないフォルダのエントリ（広いルールが作ったものも手動で登録したものも含む）と、Addressables 本体が
  拒否するパスのエントリ（`.preset`/`.asmdef`、`Editor` フォルダ自体とその配下、`Assets` ルート、パッケージの
  ルートとその `package.json`、`ProjectSettings/` など `Assets/`・パッケージ外のパス、Config Folder の配下および
  名前が前方一致するフォルダ）。無効パスのエントリはインポート時の自動適用でも削除され、その経路では自動
  スナップショットは作られない。アップデート前に、スナップショットを保存する
  （`Tools/AddressTeller/Snapshot/Save Snapshot`）か、`CleanupStaleEntries` とインポート時の自動適用を一時的に
  無効化し、フォルダを登録する意図のルールに `IncludeFolders()` を追加してから `Apply All` を実行し、Console の
  削除 Warning を確認すること。
- `RuleUnitTestHelper` サンプル: `ExampleRuleTest.FindFirst` が、ルールが `IncludeFolders()` で opt-in していない
  フォルダの `AssetContext` に対しては `Predicate` を呼ばなくなり、本番の評価パイプラインと同じ挙動になった。
  `RuleTestHelper` のドキュメントコメントとサンプルの README でも、独自の評価ループを組む場合にこの点を説明した。

### Documentation

- `compatibility.md`: ルールビルダーのインターフェース（`IAddressRuleBuilder`・`IAddressRuleGroupBuilder`・
  `ILabelRuleBuilder`）へのメソッド追加が非破壊的変更であることを明記した（これらは内部でのみ実装され、
  ルール作成者は利用するだけで実装することを想定していないため）。あわせて `IncludeFolders()` の単回呼び出し
  制約、フォルダが opt-in しない限りルールの `Where()` に渡らないこと、stale エントリクリーンアップの対象範囲が
  拡張された（無マッチのエントリだけでなく構造的に無効なパスも含む）ことを文書化。
- `design-decisions.md`: フォルダ opt-in モデルの意義、Addressables へのパス妥当性判定の揃え方、および
  AddressTeller のルール面を Addressables Groups ウィンドウが手動で許可・拒否するものと突き合わせる設計原則を
  文書化。
- `operations.md`: 新しく追加されたパス除外カテゴリ、管理対象グループの無効パスエントリのクリーンアップ、
  および上記アップデート注記への参照を文書化。
- `writing-rules.md`: `IncludeFolders()` の使用方法と `IsFolder` 経由のフォルダ判定を追加。

## [0.4.2] - 2026-08-07

### Documentation

- README.md/README.ja.md/Documentation~/writing-rules.md/.ja.md: asmdef の参照に関する記述を修正。ルールクラスを定義するだけの独自アセンブリに必要な参照は `AddressTeller.Core` のみ（`AddressRuleBase` / `IAddressRuleBuilder` / `Match` / `AssetCondition` / `Naming` / `AssetContext` / `RuleInspector` といったルール記述面はすべて Core にあり、`Rule Unit Test Helper` サンプルの asmdef 自体がこの構成）。`AddressTeller.Editor` が必要なのは運用系 API（`AddressTellerService` / `ValidationResult` / スナップショット / レポート）も呼ぶ場合に限られ、その場合はシグネチャが Core の型を露出しているため `AddressTeller.Core` の参照も併せて必要になる。従前の記述はすべてのルールアセンブリに両方の参照を求めていた。
- README.md/README.ja.md: リリースタグによるバージョン固定を記載。[互換性ポリシー](Documentation~/compatibility.ja.md)は `1.0.0` 以降に発効し、`0.x` ではマイナーリリースでも破壊的変更が入りうるため、タグ固定を強く推奨することを明記。タグなしの git URL は既定ブランチを追跡すること、およびリリースタグは 0.4.0 以降にのみ存在することを記載。

## [0.4.1] - 2026-08-02

### Documentation

- README.md/README.ja.md: ルールにマッチしたアセットは現在の所属グループに関わらずそのルールのグループへ移動されること、削除とラベル加算はいずれかのルールが参照しているグループに限定されることを明記。
- README.md/README.ja.md: Addressablesの用語解説（アドレス・ラベル・グループ、Addressablesの初期化）を追加し、Quick Startにエントリの作成・移動、管理グループの範囲、インポート時自動適用（`CleanupStaleEntries` によりマッチしなくなったエントリも削除されることを含む）に関する説明を追記。
- Documentation~/operations.md/.ja.md: 4つのCLIエントリポイントの終了挙動、CI向け batchmode 実行例、`Tools/AddressTeller/Clear All Addresses & Labels...` が常に確認ダイアログを表示し dry-run ゲートを持たないことをまとめた「コマンドラインからの非対話実行」節を追加。
- Documentation~/operations.md/.ja.md: これまで未記載だった `Tools/AddressTeller/Preview Group...` と `Assets/AddressTeller/Preview (Apply Preview)` を Apply Methods 表に追加。
- Documentation~/operations.md/.ja.md: 単独ルールプレビュー（Project Settings の「Validate/Apply this rule only」ボタン）について、評価スコープが選択した1ルールのみに限定されるため、同じグループを対象とする他ルールが管理するエントリを `Removed` と予測しうることを明記。
- AddressTellerScopedPreview.cs のXMLドキュメントコメント: `RunGroupPreview` のフォルダ展開に関する説明から、誤ったサブアセットへの言及を削除。

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

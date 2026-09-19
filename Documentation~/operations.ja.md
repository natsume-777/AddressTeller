[English](./operations.md)

# 適用と運用

## 適用方法

| 方法 | 説明 |
|---|---|
| インポート時自動適用 | `AssetPostprocessor` により、アセットのインポート・移動・削除のたびに自動で `Apply All` 相当が実行されます。Project Settings でオフにできます。 |
| `Tools/AddressTeller/Apply All` | プロジェクト全体に手動でルールを適用します。 |
| `Tools/AddressTeller/Preview Group...` | 既存グループのドロップダウンを表示し、選択したグループの現メンバー（フォルダは展開済み）を起点に有効な全ルールの dry-run を実行し、結果ウィンドウ（Diff/Issuesタブ、算出できた場合は Distribution タブも）を開きます。書き込みは行いません。Apply All や Validate は別途手動で実行する必要があります。 |
| `Tools/AddressTeller/Validate` | 書き込みは行わず、競合・グループ未検出などの問題だけを Console に出力します。 |
| `Tools/AddressTeller/Apply with Validate` | 先に Validate を実行し、問題があれば Apply を中止します。 |
| `Assets/AddressTeller/Explain`（Project ウィンドウの右クリックメニュー） | 選択したアセットに対して全ルールを評価し、その結果を確認ウィンドウで表示します。マッチしたルール・マッチしなかったルール（その `Where` 説明付き）・ルール例外を一覧で見ることができます。`Match` ヘルパーを使用したルールは自動生成された説明（例: `InFolder(Assets/Characters) AND OfType<GameObject>`）が表示されるため、生ラムダよりもルールの動作確認が効率的です。 |
| `Assets/AddressTeller/Preview (Apply Preview)`（Project ウィンドウの右クリックメニュー） | 選択したアセット（フォルダは再帰展開）に対して有効な全ルールの dry-run を実行し、`Preview Group...` と同じ結果ウィンドウを開きます。書き込みは行いません。 |
| `Tools/AddressTeller/Clear All Addresses & Labels...` | AddressTeller が管理するグループのみを対象に、Addressable エントリ（アドレス・グループ割り当て・ラベル）を削除します。実行前に専用スナップショット（`SnapshotFolder/Clear` 以下、ローテーション対象外）を必須で保存し、確認ダイアログを経て実行します。公開前パッケージの初期セットアップ用途を想定した割り切り機能です。削除したエントリは Console に個別ログ（Warning）され、Snapshot Restore で復元できます。 |

## CI 連携

`-executeMethod` で以下を実行できます。

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI`（`ApplyWithValidateCLI` は先に Validate を行い、問題があれば Apply を中止します）
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI`（Apply を行わない dry-run。読み取り専用で差分・問題を検出します）
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI`（既定では AddressTeller 管理下のグループのエントリのみを削除します。`-addressTellerClearScope all` を指定するとプロジェクト内の全 Addressable エントリを削除します。実行には `-addressTellerConfirmClear` の指定が必須です）

`-addressTellerReport <path>` / `-addressTellerReportFormat json|junit` を指定すると、`CheckCLI` は dry-run、`ApplyAllCLI` / `ApplyWithValidateCLI` は Apply 実行前の差分（dry-run）から構造化レポートをファイル出力します。`-addressTellerReportFormat` を省略した場合、拡張子が `.xml` なら `junit`、それ以外は `json` として扱われます。

`-addressTellerFailOnSettingsMismatch` を指定すると、4つの CLI エントリポイントすべてが、他の処理を行う前に `ProjectSettings/AddressTellerSettings.asset` のディスク上の内容が現在メモリにロードされている設定と一致すると確認できるかどうかを検査します（後述の [Project Settings](#project-settings) にある起動時診断と同じ検査結果を、次の Editor セッションを待たずに確認するものです）。確認できなければ——ファイルが不一致・読み取りが「ファイルが存在しない」以外の例外を出した・ファイルは読めたが照合そのものができない、のいずれであっても——そのまま処理を続けずエラーログを出して exit code 3 で終了します——設定ファイルの読み込み失敗（このバージョンの [CHANGELOG.ja.md](../CHANGELOG.ja.md) の「Changed」項目を参照）が CI の結果に影響する前に検出するのに使えます。ただし、AddressTeller 自身がメモリ上の設定を比較用に再シリアライズできなかった等、AddressTeller 側の都合で照合そのものが成立しなかった場合（ファイル側の問題だと確認できたわけではない場合）は、このフラグを指定していても実行を失敗させません——この場合は `Debug.LogError` ではなく `Debug.LogWarning` になり、フラグを指定しない場合と同じ深刻度です。しかもこの検査はCLIの分だけ改めて実行し直すのではなく起動時診断が既に計算した結果をそのまま再利用するため（後述）、この場合は起動時の警告とこのフラグ自身の警告とで**まったく同じ文面のWarningが2回**ログされます——ファイル側の問題があるときの「起動時Warning＋CLI側Error」という組み合わせとは異なる点に注意してください。このフラグは検査そのものの実行有無を制御するものではありません——後述の起動時診断はこのフラグの有無に関わらず `-batchmode` でも無条件に実行されます。フラグが制御するのは、ファイル側の問題が見つかったときに CLI の実行自体を失敗させるかどうかだけです。そのため、ファイル側の問題がある状態でこのフラグを指定した CI 実行では、同じ内容が起動時の `Debug.LogWarning` と CLI 側の `Debug.LogError` の2回ログされます——ログを機械的にパースする場合は注意してください。ただし比較そのもの（比較用の一時ファイルの書き出し・読み取りを含む）は同一 Editor ドメイン内では1回しか実行されません——このフラグの検査は、比較をやり直すのではなく起動時診断の結果をそのまま再利用します。このフラグを指定しなければ、4つの CLI エントリポイントいずれも挙動は変わりません。

exit code（`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI` 共通）:

| exit code | 意味 |
|---|---|
| 0 | 差分なし・問題なし |
| 1 | ドリフトあり（差分あり、Validation エラーなし） |
| 2 | Validation エラーあり |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`-addressTellerDisableRules` に未知のルールクラス名を指定・レポート書き込み失敗、または `-addressTellerFailOnSettingsMismatch` 指定時のみ、設定ファイルが現在使用中の設定と一致すると確認できなかった場合——不一致・読み取り不能・照合不能のいずれか） |

`ClearCLI` の exit code:

| exit code | 意味 |
|---|---|
| 0 | クリア完了 |
| 3 | 実行環境エラー（`AddressableAssetSettings` 不在・引数不正・`scope=managed` で `managedGroups` の信頼性を損なうルール構成エラー・スナップショット保存失敗、または `-addressTellerFailOnSettingsMismatch` 指定時のみ、設定ファイルが現在使用中の設定と一致すると確認できなかった場合——不一致・読み取り不能・照合不能のいずれか） |
| 4 | `-addressTellerConfirmClear` が指定されていないため実行を拒否（意図的な拒否） |

### コマンドラインからの非対話実行

上記4つの CLI エントリポイントはいずれも確認ダイアログを出さず、入力待ちで止まることもありません。引数を解析し、実行し、結果をログ出力した後、それぞれが自分自身で必ず `EditorApplication.Exit(<code>)` を呼んで終了します。これは破壊的操作を行う `ClearCLI` でも同様で、確認ダイアログの代わりに必須の `-addressTellerConfirmClear` フラグ（上記 exit code 表を参照）で意思確認を行い、入力待ちにはなりません。（対話メニュー版と比較すると次の違いがあります。`Apply All` / `Apply with Validate` は先に dry-run を行い、適用すべき差分がある場合に限り確認ダイアログを表示します。dry-run の結果、差分も問題も無ければダイアログを出さずにそのまま戻ります（`AddressTellerApplyFlow`）。一方 `Clear All Addresses & Labels...` は dry-run を行わず、削除対象が0件であっても常に確認ダイアログを表示します。ダイアログを出さずに中止するのは、ルール構成エラーにより管理グループの所有権判定が信頼できない場合のみです。[適用方法](#適用方法) を参照。）

例: `ApplyAllCLI` をヘッドレスで実行し、JSON レポートを出力した上で、プロセスの exit code から成否を判定する場合。

```
"<Unity実行ファイルへのパス>" -batchmode -quit -projectPath "<プロジェクトへのパス>" -executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI -addressTellerReport report.json -logFile -
```

- `-batchmode` は Unity をヘッドレスで起動します。`-quit` は `-executeMethod` が返った時点で終了する Unity 標準の起動オプションですが、実際には上記の各 CLI メソッド自身が返る前に `EditorApplication.Exit(<code>)` を呼んで終了するため（前述の通り）、`-quit` はその呼び出しが何らかの理由でスキップされた場合の保険として付けている程度の意味合いです。
- `-logFile -` は Editor ログをファイルではなく標準出力へ流します。この機能が出力する `Debug.Log` / `Debug.LogError` を CI で捕捉するのに便利です。
- 必要に応じて `CheckCLI`・`ApplyWithValidateCLI`・`ClearCLI`（こちらは `-addressTellerConfirmClear` が別途必須。`-addressTellerClearScope all` も任意で指定可）に差し替えてください。
- ビルドを失敗させるかどうかは、上記の exit code 表と照らし合わせて判定してください。
- `AddressTellerSettings.SaveToDisk()` / `ReloadFromDisk()`（詳細は後述の [Project Settings](#project-settings)）は、
  何か本当に問題がある場合を除き AddressTeller 自身からはログを出しません。`SaveToDisk()` はファイルの
  書き込み、または書き込みの検証に失敗したときだけ `Debug.LogError` を出します。まれに、その検証で使う
  一時ファイルの後始末に失敗した場合に `Debug.LogWarning` を1回だけ出すこともあります（戻り値には影響
  しません。一時ファイルが削除されずに残るだけです）。`ReloadFromDisk()` は自ら何かをログ出力することは
  ありません。ただし設定ファイルが壊れている場合、`ReloadFromDisk()` の読み込み中に Unity 自身のデシリアライザがパース
  エラーをログ出力することがあります——これは AddressTeller ではなく Unity 側が出すログですが、
  `-logFile -` の捕捉結果には含まれます。その場合、設定は無言で既定値へフォールバックします（詳細は後述の
  [Project Settings](#project-settings) を参照）。
  この契約は、後述の [Project Settings](#project-settings) にある設定ロード診断（CI 実行時は上記の
  `-addressTellerFailOnSettingsMismatch` も含む）には及びません。その診断は、設定ファイルを現在使用中の
  設定と照合する際に見つかった問題——ファイルの不一致・読み取りが「ファイルが存在しない」以外の例外を
  出した・ファイルが解釈できない——を知らせること自体が目的のため、これらを見つけるたびに `Debug.LogWarning`（そのフラグ指定時
  かつこの3つのいずれかに該当する場合のみ、`Debug.LogError` を出したうえで終了）を出します——これは上記の
  `SaveToDisk()` / `ReloadFromDisk()` について説明した「書き込み・読み込み自体に問題が無い限り無言」と
  いう契約とは別物です。診断そのものが最後まで実行できなかった場合（例えば AddressTeller がメモリ上の
  設定を比較用に再シリアライズできなかった場合）も同様に `Debug.LogWarning` を出しますが、これはファイル
  側の問題だと確認できたわけではないため、`-addressTellerFailOnSettingsMismatch` を指定していても CLI
  実行を失敗させることはありません。この診断は `SaveToDisk()` と同じ一時ファイル再シリアライズ処理を
  再利用しているため、ファイルとメモリが実際には一致している場合でも、上記の一時ファイル後始末失敗時の
  無関係な `Debug.LogWarning`（2段落前で説明したもの）がまれに同様に出ることがあります。まれに両方が
  重なる場合（診断そのものが比較を完了できず、かつ同じ試行での一時ファイル後始末にも失敗した場合）、
  AddressTeller から独立した `Debug.LogWarning` が2本出ますが、どちらもエラーへ昇格しない単なる警告で
  共通の識別子も無いため、メッセージ本文以外に機械的に区別する手がかりはありません。

### 論理バンドル分布サマリ

`json` 形式のレポートには `BundleDistribution` セクションが含まれます（`JsonUtility` は大文字小文字の変換を一切行わないため、JSON のキー名は C# のフィールド名とそのまま一致します）。これは dry-run の Predict 結果（アセット→グループ/ラベル）と各グループの BundleMode（PackTogether/PackSeparately/PackTogetherByLabel）から算出した、ビルド前の論理バンドル単位の個数・分布の概算です。「ルール設計が意図せず巨大バンドル1個や数百分割を生んでいないか」を検知するための目安であり、**実 Addressables ビルドのバンドル数を一致させることを保証しません**。

近似の既知差異として以下は反映されません。

- PackTogether のシーン別バンドル分離
- PackSeparately のフォルダ単位まとめ
- PackTogetherByLabel における Addressables 本体のラベル連結方式との差異（本サマリはラベル集合を昇順ソート＋区切り文字で連結した正規化キーで分割しています）

`BundledAssetGroupSchema` が付与されていないグループは BundleMode が判定できないため `Unknown` として扱われ、バンドル数の集計（`TotalLogicalBundleCount`）には含まれません（`UnknownGroupCount` で別集計されます）。JSON の全キー一覧と安定性の保証範囲は [互換性ポリシー](compatibility.ja.md#5-レポート出力json--junit-xml) を参照してください。

## Project Settings

**このバージョンより前のバージョンからアップデートする場合**、この画面の全項目が、このバージョンが
`ProjectSettings/AddressTellerSettings.asset` を最初に読み込んだ時点で既定値へリセットされます
（このリセット自体には警告もエラーも伴いません）。アップデート前に以下の現在値を控えておき、アップデート後に再設定してください。
実測で確認した詳細と影響対象の設定については、このバージョンの [CHANGELOG.ja.md](../CHANGELOG.ja.md) の
「Changed」項目を参照してください。

ただしこのバージョンからは、これを記憶だけに頼って把握する必要はありません。Editor セッションにつき
1回——同一セッション内でドメインリロードが何度起きても繰り返されない——、AddressTeller が
`ProjectSettings/AddressTellerSettings.asset` のディスク上の内容と、現在メモリにロードされている
設定を再シリアライズした結果を比較します。この検査自体が比較についてのログを一切出さないのは、
ファイルがまだ存在しない場合（正常な初回起動）と、比較の結果ファイルがメモリと一致した場合の2つだけです。
この無言のケースは「ファイルが存在しない」ことについてのものであり、「中身が空」であることについての
ものではありません——存在はするが空（0バイト）のファイルはこれに該当せず、代わりに解釈不能として警告
されます（実測で確認済み。警告文には `0 characters` と実際に現れます）。したがって、ファイルが削除された
場合や `.gitignore` の設定ミスでチェックアウトから除外された場合は、このバージョンでこの設定を一度も
保存したことのないプロジェクトと区別がつきません——どちらも無言のまま、設定は既定値にフォールバックします。
それ以外の場合は必ず `Debug.LogWarning` を出します。AddressTeller はこれを固定された手順で進めます。
まず設定ファイルの絶対パスを確定し、次にそのパスでファイルを読み取ります——「ファイルが存在しない」旨の
例外はここでの正常な初回起動のケース（この時点では何もログしません）で、それ以外の例外はここで
「ファイルが存在しない」以外の例外を読み取りが出した旨（読み取り権限が無い等。これはファイルが実際に
存在することまでは確認していない）として報告します。ファイルを読み終えた後になって初めて、
AddressTeller はメモリ上の現在の設定を比較用に再シリアライズし、その再シリアライズ結果を自分自身で
認識できた場合に限りフィールド値の比較へ進みます——進んだ場合は、AddressTeller の抽出規則が認識できる
位置にこのバージョンが書き出す設定フィールドが1つも見つからなかった旨、または（比較できた場合は）
差分フィールドをシリアライズ名（例: `_postprocessOrder`）で列挙する旨、あるいは差分無しの旨のいずれかを
報告します。「AddressTeller 側の都合で比較そのものが完了できなかった」は、他の結論がすべて否定された後に
初めて確認される単一のフォールバックではありません——上記の手順のいくつもの地点で個別に発生しえます。
ファイルの場所を特定できなかった場合、読み取りが（ファイル自体ではなく）AddressTeller 側のパス組み立てに
起因する特定の例外を出した場合、メモリ上の設定を再シリアライズできなかった場合、あるいは再シリアライズ
結果を自分自身で認識できなかった場合、のいずれもがこれに当たります。このうち優劣が決まっているのは
最後の1つ（再シリアライズ結果を認識できない場合）だけで、これがファイル側の「認識できる設定フィールドが
1つも無い」という状態と同時に成立するときは、AddressTeller は「ファイルが解釈できない」ではなく
「比較そのものが完了できなかった」と報告します。いずれの結論であっても、ファイル固有の結論（読み取りで
例外が発生した・意味のある比較ができなかった・値が食い違っている）はそれぞれ該当する対処を警告本文に
そのまま書きます。「AddressTeller 側の都合で比較そのものが完了できなかった」という結論だけはファイルに
ついて何も述べておらず、案内すべき対処もありません（唯一の例外は前述の
[コマンドラインからの非対話実行](#コマンドラインからの非対話実行) にある一時ファイル後始末の注記を
参照）。アセットのインポート中には一切実行されないためインポートごとの追加コストもありません。この
起動時検査は `-batchmode` の CI 実行を含め無条件に実行されます。CI で
`-addressTellerFailOnSettingsMismatch`（前述の [CI 連携](#ci-連携) を参照）を指定すると、ファイルが
不一致・読み取り不能・照合不能のいずれかである場合に、警告ではなく `Debug.LogError` を出して実行自体を
失敗させることができます——このフラグは検査自体の実行有無を制御するものではなく、また AddressTeller
側の都合で比較が完了できなかった場合（ファイル側の問題だと確認できたわけではない場合）は実行を失敗
させません（この場合は常に警告のみです。この起動時検査は同一 Editor ドメイン内で既に同じ比較を実行済み
のため、CLI 側もその結果を再利用するだけで比較をやり直さず、この警告は起動時とこのフラグの検査とで
まったく同じ文面で2回出ます）。

`Project Settings > AddressTeller` に以下の項目があります。

- **インポート時に自動適用する**（既定: ON）— オフにすると `AssetPostprocessor` による自動適用を行いません。手動メニューには影響しません。
- **Postprocessor の実行順序**（`PostprocessOrder`、既定: 1000）— `AssetPostprocessor.GetPostprocessOrder()` に渡す値です。値が小さいほど他の `AssetPostprocessor` より先に実行されます。既定値は後段寄りの大きな値で、他パッケージの Postprocessor がアセットを生成・変更してから AddressTeller が評価することを期待します。なお `0` は「未設定」を表す予約値であり、明示的に `0` を指定しても既定値の `1000` として扱われます。
- **マッチしなくなったエントリを削除する**（`CleanupStaleEntries`、既定: ON）— apply 時（手動の `Apply All`、およびインポート時の自動適用では変更されたアセットについて）、どのルールにもマッチしなくなったアセットを、AddressTeller が管理するグループ（いずれかのルールが参照しているグループ）から削除します。削除はエントリ単位（`RemoveAssetEntry`）のため、アドレスと（Addressablesの）ラベルの両方が失われます。AddressTeller が管理していないグループに手動で登録したエントリには触れません。**一方、管理グループ内に手動で登録したエントリは、対応するルールがなければ削除対象になります**（資産単位で「現在どのルールにもマッチするか」のみを判定するため）。有効な場合、これに加えて、管理対象グループ内でアセットパスが構造的に無効なエントリ（拡張子・`Editor` という名前のフォルダ等で判定される、旧バージョンの AddressTeller が作成した残骸等）も削除対象になります。パスがそもそも解決できない（`AssetPath` が空文字になる。LFS 未取得・ブランチ切替中・パッケージ未導入等で一時的に資産へアクセスできないケースを含む）エントリはこの掃除の対象外です。資産が本当に削除された場合の追従は、別経路（削除通知を起点にするもの）が担当します。この判定は実際にインポート・変更されたアセットとは独立に、管理対象グループ内の全エントリに対して毎回行われるため、インポート時の自動適用（Postprocessor）のような差分適用では本来たどり着けないはずの残骸も掃除されます。つまり Postprocessor が走るたびに、インポート・変更されたアセットの件数に関わらず、管理対象グループ全件のエントリを毎回スキャンします。**旧バージョンからアップデートする場合**、最初の apply で、新たに無効・無マッチと判定されるようになった管理グループ内の既存エントリ（`IncludeFolders()` で opt-in していないフォルダのエントリ、Addressables 本体が拒否するパスのエントリ、Config Folder の配下または名前が前方一致するフォルダのエントリ等）が削除されることがあります。対象となる具体的な範囲とアップデート前の準備については、[CHANGELOG.ja.md](../CHANGELOG.ja.md) の「0.4.x からのアップデート」の注記を参照してください。あわせて [設計上の決定事項: 削除は資産単位の所有権で判定する](design-decisions.ja.md#削除は資産単位の所有権で判定する) および [設計上の決定事項: 存在しないグループは作らない（既定）](design-decisions.ja.md#存在しないグループは作らない既定) も参照してください。
- **スナップショット保存先フォルダ**（後述）

これらの設定値は `ProjectSettings/AddressTellerSettings.asset` に保存されます。プロジェクト単位の設定としてバージョン管理に含めることができ、チームメンバー間で共有されます。各プロパティの setter は値を変更するたびにこのファイルへ書き込みますが（同じ値を再代入した場合は何もせず、書き込みも行いません）、ファイルとメモリ上の値がずれてしまった場合——例えばファイルの読み込みに失敗した、あるいは Editor 起動中にファイルがエディタ外で書き換えられた（マージ、手動編集、VCS でのチェックアウト等)——setter だけでは復旧できません。同値判定によって書き込みが黙って省略されるためです。`AddressTellerSettings.ReloadFromDisk()` は、現在メモリ上にある設定オブジェクトを破棄し、Unity 自身にディスクから作り直させることでファイルの内容をメモリへ反映します（この呼び出しにより内部の設定オブジェクトの参照そのものが差し替わり、未保存のメモリ上の変更は失われます）。ファイルが存在しない、またはアクセスできない場合はメモリを変更せず `false` を返します。内部の設定オブジェクトの破棄・再生成そのものが想定外の理由で失敗した場合も、エラーログを出したうえで `false` を返します。`AddressTellerSettings.SaveToDisk()` は値の変更有無に関わらずメモリ上の現在値を無条件でファイルへ書き込みます（書き込みや読み込みに失敗した場合、または書き込み後のファイルの内容が同じ設定を再シリアライズした結果と一致しない場合は、エラーログを出したうえで `false` を返します）。まれに、その検証で使う一時ファイルの後始末に失敗した場合に `Debug.LogWarning` を1回だけ出すこともあります（戻り値には影響しません。一時ファイルが削除されずに残るだけです。前述の[コマンドラインからの非対話実行](#コマンドラインからの非対話実行)にある同じ注記を参照）。

ずれを解消する際は、どちらの値を残したいかを**呼び出す前に**決めてください。`ReloadFromDisk()` を呼んだ時点でメモリ上の値はファイルの値に上書きされるため、呼び出した後では呼び出し前のメモリ上の値を取り戻す方法はありません。

- ファイル側の値を採用し、メモリ上の変更を捨てたい場合: `ReloadFromDisk()` を呼んで終わりです。
- メモリ上の値を残し、ファイルの内容を捨てたい場合: `ReloadFromDisk()` を**呼ばずに**、`SaveToDisk()` を直接呼んでください（`ReloadFromDisk()` を先に呼んでから `SaveToDisk()` を呼んでも、メモリ上の値は既にファイルの値で上書きされてしまっているため、「メモリ側の値を復元する」ことにはなりません。単にファイルの値をファイル自身へ書き戻すだけです）。

Project Settings ウィンドウを開いたままこれらのメソッドを呼んだ場合、ウィンドウの表示は自動更新されません。各フィールドはページ生成時点の値を表示し続けます。フィールドを操作する前にウィンドウを開き直す（または別ページへ移動して戻る）必要があります。そうしないと、古い表示のフィールドを編集した際に、読み込み直前・保存直前の値で上書きしてしまいます。

登録されているルールクラス（`AddressRuleBase` 継承クラス）の一覧と、`Order` 値も同じ画面で確認できます。各ルールクラスの横には有効/無効を切り替えるトグルがあり、デバッグ・動作確認時に特定のルールだけを無効化することができます。無効化したルールは `Apply All` / `Validate` / `Apply with Validate` / `Explain` / スナップショットの dry-run 予測の評価対象から除外されます。各ルール行には「Validate/Apply this rule only」ボタンもあり、そのルール1件だけをプロジェクト内の全アセットに対して dry-run し、結果ウィンドウを開きます。1ルールのみのスコープであるため、他ルールが管理するエントリの削除予測は表示されません（結果ウィンドウにその旨の注意文が表示されます）。逆に、同じグループを他のルールも参照している場合、そのルールにしかマッチしないアセットがこの単独ルールプレビューでは「Removed」と表示されることがあります（`Apply All` は全ルールをまとめて評価するため、実際には削除されません）。全ルールを横断した最終結果を確認するには、別途 `Apply All` や `Validate` を実行してください。このボタンは有効/無効トグルを無視するため、無効化中のルールでも明示的にクリックすれば単独で dry-run できます。

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

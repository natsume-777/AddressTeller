# 設計上の決定事項

AddressTeller の挙動には、一見不便に見えても意図的に選んでいる仕様がいくつかある。
ここでは、その判断とその理由をまとめる。仕様変更を検討する際は、まずここに書かれた前提が変わったかどうかを確認してほしい。

## アドレスは競合時にエラーにする

全ルールを `Order` 昇順で評価し、アドレスを発行するルール（`Address()` を呼んだルール）が2件以上マッチした場合は競合エラーとして書き込みを行わない。1件のみなら採用する。Apply・Validate で同じ判定をする。

暗黙のファーストマッチ採用にすると、ルールの定義順やリフレクション列挙順という見えにくい要因で最終アドレスが変わり、利用者が気づかないまま意図しないアドレスが採用される。曖昧な状態は黙って解決せず、エラーとして表面化させる方を選んだ。

## ラベルは全ルールから蓄積する

ラベルはアドレスと異なり、マッチした全ルールから蓄積する（複数ラベルの同時付与）。ラベルは1アセットに複数付くことが正常な使い方であり、競合の概念がない。分類軸ごとに独立したルールを書けるようにするため、蓄積を既定とする。

## 存在しないグループは作らない（既定）

既定では、ルールが指定したグループが存在しない場合はエラーにし、自動生成はしない。グループはバンドル設定（圧縮・分割方針）を伴う設計上の単位であり、タイプミスで意図しないグループが量産されると気づきにくい。グループの作成は明示的な操作に委ねる。自動作成が必要な場合に備え、既定OFFのオプトイン設定を別途用意している。

## 削除は資産単位の所有権で判定する

`CleanupStaleEntries`（既定: ON）は、`Apply All` 実行時にどのルールにもマッチしなくなった資産を Addressables から取り除く機能だが、対象は AddressTeller が管理しているグループ（いずれかのルールが参照しているグループ）に登録されているエントリに限る。AddressTeller が関与していないグループのエントリには一切触れない。

また、エントリそのものの削除は行うが、ラベルは剥がさない。ラベルは「全ルールから蓄積する」という設計上、どのルールがいつ付与したラベルかを後から一意に特定できないため、誤って利用者が手動で付けたラベルや他ツールのラベルを剥がしてしまうリスクがある。削除してよいと確信できる範囲（管理対象グループのエントリ）だけを対象にし、判断が難しい操作（ラベルの剥がし）は行わない。これにより、所有権が明確な操作だけを安全に自動化し、不確実な操作は利用者の手動操作（スナップショットの Exact 復元など）に委ねる。

## 公開APIと内部実装の境界

利用者が触れるべき面を最小に保つため、公開（`public`）にするのは次に限る。それ以外の評価エンジン・Addressables 統合の実装詳細は `internal` とし、将来のリファクタリングで自由に変更できる余地を残す。

公開API:

- **ルール定義面**: `AddressRuleBase`、`IAddressRuleBuilder`、`IAddressRuleGroupBuilder`、`Match`、`AssetCondition`、`Naming`、`AssetContext`、`AddressRuleEntry`
- **実行エントリ**: `AddressTellerService`、`AddressTellerSettings`
- **スナップショット**: `AddressTellerSnapshotService`、`SnapshotRestoreMode`、`SnapshotDiff`、`AddressTellerSnapshot`、`SnapshotEntry`
- **結果型**: `ValidationResult`、`ValidationStatus`
- **進捗報告**: `IProgressReporter`、`NullProgressReporter`、`EditorProgressReporter`
- **レポート**: `AddressTellerReportWriter`、`AddressTellerExplainReport`、`AddressTellerExplainAsset`、`AddressTellerExplainRule` などのレポートDTO群
- **CLI・メニューエントリ**: `AddressTellerMenu`、`AddressTellerSnapshotMenu`、`AddressTellerExplainMenu`、`AddressTellerCliArgs`
- **インポート時自動適用**: `AddressTellerPostprocessor`

内部実装（`internal`）:

- ルール収集・評価・説明文生成の実装
- Addressables への書き込み実装
- スナップショットのファイル管理・自動退避・レポート組み立て

これらは単一のエディタアセンブリ内に閉じており、テストからは `InternalsVisibleTo` で限定的に参照できるようにしている。利用者から直接参照されない前提のため、内部実装の型・メソッドはシグネチャを保たずに変更できる。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>Predict が返す、適用後のアセットの予測される変化の種類。</summary>
    internal enum PredictedAction
    {
        /// <summary>エントリが新規追加または更新される（現状と内容が一致している場合も含む）。</summary>
        AddOrUpdate,

        /// <summary>エントリが削除される（CleanupStaleEntries による）。</summary>
        Remove,

        /// <summary>Apply を実行しても Addressables の状態に変化はない（Validate が Ok 以外、または削除対象外の Skipped）。</summary>
        NoOp,
    }

    /// <summary>
    /// 書き込みを行わずに Apply 実行後の状態を予測した結果。
    /// </summary>
    internal readonly struct ApplyPrediction
    {
        public PredictedAction Action { get; }
        public ValidationResult Validation { get; }

        /// <summary>Action が AddOrUpdate のときのみ意味を持つ、適用後のエントリ状態。</summary>
        public SnapshotEntry PredictedEntry { get; }

        /// <summary>Action が Remove のときのみ意味を持つ、削除元のグループ名。</summary>
        public string RemovedFromGroup { get; }

        public ApplyPrediction(PredictedAction action, ValidationResult validation, SnapshotEntry predictedEntry, string removedFromGroup)
        {
            Action = action;
            Validation = validation;
            PredictedEntry = predictedEntry;
            RemovedFromGroup = removedFromGroup;
        }
    }

    /// <summary>
    /// AddressResolution を受け取り、検証または Addressables への書き込みを行う。
    /// </summary>
    internal static class AddressTellerApplier
    {
        /// <summary>
        /// 書き込みは行わず、検証結果だけを返す。
        /// グループ存在チェックはテスト可能な existingGroupNames で行う。
        /// </summary>
        /// <param name="autoCreateMissingGroups">
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の値。true の場合、未存在グループは
        /// <see cref="ValidationStatus.GroupNotFound"/> ではなく <see cref="ValidationStatus.GroupWillBeCreated"/>
        /// として返す（Validate/Predict では実際の作成は行わない）。
        /// </param>
        public static ValidationResult Validate(
            AssetContext context,
            AddressResolution resolution,
            IEnumerable<string> existingGroupNames,
            bool autoCreateMissingGroups = false)
        {
            if (resolution.AddressCandidates.Count == 0)
            {
                // アドレス候補は無いが、ラベルが1件以上あればラベルのみルール（AnyGroup() 等）がマッチしている。
                // これは「どのルールにもマッチしなかった」わけではないため、真の無マッチ(Skipped)とは区別する。
                if (resolution.Labels.Count > 0)
                    return new ValidationResult(context, ValidationStatus.LabelsOnly,
                        "Only label rule(s) matched; no address assigned.");
                return new ValidationResult(context, ValidationStatus.Skipped, "No matching rule.");
            }

            if (resolution.AddressCandidates.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append($"Address conflict for '{context.Path}':");
                foreach (var c in resolution.AddressCandidates)
                    sb.Append($"\n  {c.DescribeSource()} [{AddressRuleBuilderImpl.DisplayGroupName(c.GroupName)}] → \"{c.Address}\"");

                return new ValidationResult(
                    context,
                    ValidationStatus.ConflictingAddress,
                    sb.ToString(),
                    resolution.AddressCandidates);
            }

            var candidate = resolution.AddressCandidates[0];
            if (string.IsNullOrEmpty(candidate.Address))
            {
                return new ValidationResult(
                    context,
                    ValidationStatus.InvalidAddress,
                    $"{candidate.DescribeSource()} returned a null/empty address for '{context.Path}'.");
            }

            // RuleEvaluationPipeline.BuildSetup が DefaultGroup を取得できなかった場合、
            // GroupDefault() を使うエントリのグループ名はセンチネルのまま残っている。
            // existingGroupNames には存在しないため、GroupNotFound より優先してここで報告する。
            if (candidate.GroupName == AddressRuleBuilderImpl.DefaultGroupSentinel)
            {
                return new ValidationResult(
                    context,
                    ValidationStatus.DefaultGroupUnavailable,
                    $"{candidate.DescribeSource()} uses GroupDefault(), but AddressableAssetSettings.DefaultGroup could not be resolved for '{context.Path}'.");
            }

            if (!existingGroupNames.Contains(candidate.GroupName))
            {
                if (autoCreateMissingGroups)
                {
                    return new ValidationResult(
                        context,
                        ValidationStatus.GroupWillBeCreated,
                        $"Group '{candidate.GroupName}' does not exist and will be created from DefaultGroup on Apply.");
                }

                return new ValidationResult(
                    context,
                    ValidationStatus.GroupNotFound,
                    $"Group '{candidate.GroupName}' not found in AddressableAssetSettings.");
            }

            return new ValidationResult(context, ValidationStatus.Ok, null);
        }

        /// <summary>
        /// 検証を行い、問題なければ Addressables へ書き込む。
        /// </summary>
        /// <param name="existingGroupNames">
        /// settings.groups から事前に構築したグループ名の集合（呼び出し側でループ外に1回だけ構築する想定）。
        /// AutoCreateMissingGroups により新規グループを作成した場合、このメソッドが呼び出し元のコレクションに
        /// 作成したグループ名を追加する（同じインスタンスをループの全アセットで使い回すことで、
        /// 2件目以降の同名グループ対象アセットで重複した GroupWillBeCreated 警告・重複 EnsureGroup 呼び出しを防ぐ）。
        /// </param>
        /// <param name="managedGroups">
        /// AddressTeller のいずれかのルールが GroupName として参照しているグループ名の集合。
        /// 指定された場合、どのルールにもマッチしなくなった（Skipped な）アセットが
        /// この中のグループに属していれば、<see cref="AddressTellerSettings.CleanupStaleEntries"/>
        /// が true のときエントリを削除する。
        /// </param>
        /// <param name="autoCreateMissingGroups">
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の値。true の場合、未存在グループを
        /// <see cref="AddressTellerGroupFactory.EnsureGroup"/> で DefaultGroup から複製して作成し、
        /// 成功すればそのまま書き込みを続行する。失敗した場合は <see cref="ValidationStatus.GroupCreationFailed"/>
        /// を返し書き込みは行わない。
        /// </param>
        /// <param name="hasConfigureFailures">
        /// この実行で1件以上のルールの Configure() が例外を送出した（<see cref="EvaluationSetup.ConfigureFailures"/>
        /// が空でない）場合に true。true の場合、managedGroups は「本来担当するはずだったルールの Configure()
        /// が失敗した結果、たまたま他のルールが同じグループを宣言していたため残っただけ」の可能性があり信頼できない。
        /// そのためこのアセットが Skipped でも stale クリーンアップは行わない
        /// （失敗したルールが担当していたエントリを誤って削除してしまうことを防ぐ）。
        /// </param>
        public static ValidationResult Apply(
            AssetContext context,
            AddressResolution resolution,
            AddressableAssetSettings settings,
            ICollection<string> existingGroupNames,
            IReadOnlyCollection<string> managedGroups = null,
            bool autoCreateMissingGroups = false,
            bool hasConfigureFailures = false)
        {
            var result = Validate(context, resolution, existingGroupNames, autoCreateMissingGroups);

            if (result.Status == ValidationStatus.Skipped)
            {
                // ルール例外(resolution.Errors)・ルール構成例外(hasConfigureFailures)いずれかがあった場合、
                // 「全ルールが正常評価された上でマッチ0件」とは言えないため、クリーンアップは行わない
                // (ルールのバグで誤ってエントリを削除しないようにする)。
                if (resolution.Errors.Count == 0 && !hasConfigureFailures
                    && managedGroups != null && AddressTellerSettings.CleanupStaleEntries)
                {
                    // 削除されたエントリは RemoveStaleEntryIfManaged 側で個別に Warning ログ済みのため、
                    // ここでは戻り値（削除内容）を意図的に破棄する。
                    _ = RemoveStaleEntryIfManaged(context.Guid, settings, managedGroups);
                }
                return result;
            }

            if (result.Status == ValidationStatus.LabelsOnly)
            {
                // ラベルのみルールは新規エントリを作らない（アドレス/グループを持たないため）。
                // 既存エントリがある場合のみ、そのエントリにラベルを加算適用する。cleanup は呼ばない
                // (このアセットは現在マッチするルールが存在するため stale ではない)。
                // ただし、削除と同様にラベル加算も「管理対象グループに属するエントリのみ」に限定する。
                // 管理外グループ(ユーザーが手動登録したエントリ等)には触れない所有権原則を、
                // 書き込み側であるラベル加算にも適用する。
                var existingEntry = settings.FindAssetEntry(context.Guid);
                if (existingEntry?.parentGroup != null
                    && managedGroups != null && managedGroups.Contains(existingEntry.parentGroup.Name))
                {
                    foreach (var label in resolution.Labels)
                    {
                        settings.AddLabel(label);
                        existingEntry.SetLabel(label, true);
                    }
                }
                return result;
            }

            AddressableAssetGroup group;
            if (result.Status == ValidationStatus.GroupWillBeCreated)
            {
                // Validate では作成しないが、Apply ではここで実際に DefaultGroup を複製して作成する。
                var candidateForCreate = resolution.AddressCandidates[0];
                if (!AddressTellerGroupFactory.EnsureGroup(settings, candidateForCreate.GroupName, out group, out var failureReason))
                {
                    return new ValidationResult(
                        context,
                        ValidationStatus.GroupCreationFailed,
                        $"Failed to auto-create group '{candidateForCreate.GroupName}' for '{context.Path}': {failureReason}");
                }

                // 作成したグループ名を existingGroupNames にも反映する。反映しないと、同じグループを
                // 対象とする次のアセットでも「存在しない」と誤判定され、GroupWillBeCreated 警告と
                // EnsureGroup 呼び出しが重複してしまう。
                existingGroupNames.Add(candidateForCreate.GroupName);
            }
            else if (!result.IsOk)
            {
                return result;
            }
            else
            {
                var candidate = resolution.AddressCandidates[0];
                group = settings.FindGroup(candidate.GroupName);
                if (group == null)
                {
                    // Validate は existingGroupNames（呼び出し側がループ外で1回だけ構築する、テスト可能な
                    // コレクション）だけを見て Ok と判定している。通常は settings.groups から構築されるため
                    // ここで見つからないことはないはずだが、呼び出し側が settings と食い違う
                    // existingGroupNames を渡した場合はここで初めて食い違いが顕在化する。これは Addressables
                    // が拒否したわけではなくグループが実際には存在しないという状態そのものなので、
                    // EntryRejectedByAddressables ではなく既存の GroupNotFound を再利用して原因を正しく伝える。
                    return new ValidationResult(
                        context,
                        ValidationStatus.GroupNotFound,
                        $"Group '{candidate.GroupName}' not found in AddressableAssetSettings.");
                }
            }

            // ここに到達する時点で group は必ず非 null（GroupWillBeCreated 分岐は EnsureGroup 成功時のみ
            // ここに到達し、Ok 分岐は直前の null チェックを通過済みのため）。
            var entry = settings.CreateOrMoveEntry(context.Guid, group);
            if (entry == null || entry.ReadOnly)
            {
                // entry == null になるのは、新規エントリの作成時、CreateAndAddEntryToGroup が内部で
                // Addressables 本体の IsPathValidForEntry 相当の判定を行い、それが false かつメインアセットの
                // 型がエディタアセンブリ定義の場合。entry.ReadOnly が true になるのは、同じくパスが無効と
                // 判定されつつ、メインアセットの型がエディタアセンブリではない場合。この場合
                // CreateAndAddEntryToGroup は例外を投げず、address=guid の readOnly エントリを作成して
                // グループに追加してしまうため、AddressTeller としては拒否扱いにした上で、既に作られてしまった
                // そのエントリを取り除く必要がある。AssetFilter が Addressables 本体の判定を通した上で
                // Apply を呼んでいるため通常は起こらないはずだが、対象の settings とは別の
                // AddressableAssetSettings インスタンス（AddressableAssetSettingsDefaultObject.Settings）の
                // ConfigFolder を Addressables 本体が内部的に参照するため、既定以外の settings を明示的に
                // 対象にした呼び出しでは、両者の ConfigFolder が食い違いこの分岐に到達しうる
                // （詳細は ValidationStatus.EntryRejectedByAddressables の XML doc を参照）。
                // NullReferenceException で ApplyAll 全体を止めず、このアセットだけをエラーとして報告する。
                if (entry != null)
                    settings.RemoveAssetEntry(context.Guid);

                return new ValidationResult(
                    context,
                    ValidationStatus.EntryRejectedByAddressables,
                    $"Addressables refused to create/move a usable entry for '{context.Path}' into group '{group.Name}'.");
            }
            entry.SetAddress(resolution.AddressCandidates[0].Address);

            foreach (var label in resolution.Labels)
            {
                settings.AddLabel(label);
                entry.SetLabel(label, true);
            }

            return result;
        }

        /// <summary>
        /// 書き込みを行わず、<see cref="Apply"/> を実行した場合にこのアセットがどう変化するかを予測する。
        /// 判定分岐は Apply / Validate と1対1で対応させている。
        /// </summary>
        /// <param name="existingGroupNames">settings.groups から事前に構築したグループ名の集合。</param>
        /// <param name="managedGroups">AddressTeller のいずれかのルールが GroupName として参照しているグループ名の集合。</param>
        /// <param name="autoCreateMissingGroups">
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> の値。true の場合、未存在グループは
        /// <see cref="ValidationStatus.GroupNotFound"/> ではなく <see cref="ValidationStatus.GroupWillBeCreated"/>
        /// となり（IsOk=true）、AddOrUpdate として予測される（実際の作成は行わない）。
        /// </param>
        /// <param name="hasConfigureFailures">
        /// <see cref="Apply"/> の同名パラメータと同じ意図。true の場合、managedGroups が信頼できないため
        /// stale クリーンアップ（Remove の予測）を行わない。
        /// </param>
        public static ApplyPrediction Predict(
            AssetContext context,
            AddressResolution resolution,
            AddressableAssetSettings settings,
            HashSet<string> existingGroupNames,
            HashSet<string> managedGroups,
            bool autoCreateMissingGroups = false,
            bool hasConfigureFailures = false)
        {
            var result = Validate(context, resolution, existingGroupNames, autoCreateMissingGroups);

            if (result.Status == ValidationStatus.Skipped)
            {
                var entry = settings.FindAssetEntry(context.Guid);
                if (entry?.parentGroup != null
                    && managedGroups.Contains(entry.parentGroup.Name)
                    && AddressTellerSettings.CleanupStaleEntries
                    && resolution.Errors.Count == 0
                    && !hasConfigureFailures)
                {
                    return new ApplyPrediction(PredictedAction.Remove, result, null, entry.parentGroup.Name);
                }

                return new ApplyPrediction(PredictedAction.NoOp, result, null, null);
            }

            if (result.Status == ValidationStatus.LabelsOnly)
            {
                // ラベルのみルールは新規エントリを作らない。既存エントリが無ければ Apply しても何も変化しない。
                var existingEntryForLabels = settings.FindAssetEntry(context.Guid);
                if (existingEntryForLabels == null)
                    return new ApplyPrediction(PredictedAction.NoOp, result, null, null);

                // Apply 側と対称に、管理外グループ(ユーザーが手動登録したエントリ等)に属する場合は
                // ラベル加算そのものを行わないため、予測も NoOp とする。
                if (existingEntryForLabels.parentGroup == null
                    || !managedGroups.Contains(existingEntryForLabels.parentGroup.Name))
                    return new ApplyPrediction(PredictedAction.NoOp, result, null, null);

                // 既存エントリの address/group は維持しつつ、ラベルのみ既存分と resolution.Labels の和集合にする。
                var labelsOnlyLabels = new HashSet<string>(resolution.Labels);
                foreach (var existingLabel in existingEntryForLabels.labels)
                    labelsOnlyLabels.Add(existingLabel);

                var labelsOnlyPredictedEntry = new SnapshotEntry
                {
                    Guid = context.Guid,
                    Address = existingEntryForLabels.address,
                    GroupName = existingEntryForLabels.parentGroup?.Name,
                    Labels = labelsOnlyLabels.OrderBy(l => l, StringComparer.Ordinal).ToList(),
                };

                return new ApplyPrediction(PredictedAction.AddOrUpdate, result, labelsOnlyPredictedEntry, null);
            }

            if (!result.IsOk)
                return new ApplyPrediction(PredictedAction.NoOp, result, null, null);

            var candidate = resolution.AddressCandidates[0];
            var existingEntry = settings.FindAssetEntry(context.Guid);

            // 実際の Apply は SetLabel(label, true) で加算するのみで既存ラベルを剥がさないため、
            // 予測も既存ラベルと resolution.Labels の和集合とする。
            var labels = new HashSet<string>(resolution.Labels);
            if (existingEntry != null)
                foreach (var existingLabel in existingEntry.labels)
                    labels.Add(existingLabel);

            var predictedEntry = new SnapshotEntry
            {
                Guid = context.Guid,
                Address = candidate.Address,
                GroupName = candidate.GroupName,
                Labels = labels.OrderBy(l => l, StringComparer.Ordinal).ToList(),
            };

            return new ApplyPrediction(PredictedAction.AddOrUpdate, result, predictedEntry, null);
        }

        /// <summary>
        /// 削除されたアセットの GUID に対応するエントリが AddressTeller 管理下のグループに
        /// 属している場合のみ削除する。管理外グループ（ユーザーが手動で登録したエントリ等）には触れない。
        /// <see cref="AddressTellerSettings.CleanupStaleEntries"/> が true のときのみ削除する。
        /// ルール評価を伴わない（資産が既に存在しない）削除専用のエントリポイント。
        /// </summary>
        /// <param name="hasConfigureFailures">
        /// <see cref="Apply"/> の同名パラメータと同じ意図。true の場合、managedGroups が信頼できないため
        /// 削除を行わない。
        /// </param>
        /// <returns>削除を実行した場合はその内容。削除しなかった場合は null。</returns>
        public static ClearedEntry? RemoveEntryForDeletedAsset(
            string guid,
            AddressableAssetSettings settings,
            IReadOnlyCollection<string> managedGroups,
            bool hasConfigureFailures = false)
        {
            if (managedGroups == null) return null;
            if (!AddressTellerSettings.CleanupStaleEntries) return null;
            if (hasConfigureFailures) return null;

            return RemoveStaleEntryIfManaged(guid, settings, managedGroups);
        }

        /// <summary>
        /// 管理対象グループに属するエントリのうち、パスが Addressables のエントリとして構造的に無効になっている
        /// ものを列挙する（書き込みは行わない）。旧バージョンの AddressTeller が誤って作成したエントリ
        /// （ProjectSettings/*.asset 等プロジェクト外パスの readOnly エントリ、.preset/.asmdef、
        /// Editor フォルダ自体等）や、除外条件が後から拡張された場合の残骸を検出するためのもの。
        /// 「無効」の判定基準は <see cref="AssetFilter.IsPathValidForAddressablesEntry"/> が false になること。
        /// <see cref="AssetFilter.ShouldExcludeByPath"/> はこの否定そのもの（path が null の場合の早期リターンを
        /// 除き完全に同値）なので、そのまま流用している。
        /// ただし <see cref="AddressableAssetEntry.AssetPath"/> が空文字のエントリは対象外とする。GUID から
        /// パスが引けない（<c>AssetDatabase.GUIDToAssetPath</c> が空文字を返す）ケースには、本当に資産が
        /// 削除された場合だけでなく、LFS 未取得・ブランチ切替中・パッケージ未導入・インポート途中など
        /// 「一時的に解決できないだけで資産自体は存在する（または後で存在するようになる）」ケースが多く含まれる。
        /// これらを構造的なパス無効（拡張子・Editor フォルダ等）と同列に扱って削除すると、正当なエントリを
        /// 誤って消してしまう。資産が本当に削除された場合の追従は <see cref="RemoveEntryForDeletedAsset"/>
        /// （GUID の削除通知を起点にする別経路）に任せ、このメソッドはパスが引けているのに構造的に無効な
        /// ケースだけを対象にする。
        /// Apply（<see cref="RemoveInvalidPathEntries"/>）と Predict（dry-run）の両方から共有する読み取り専用ステップ。
        /// </summary>
        internal static List<AddressableAssetEntry> FindInvalidPathManagedEntries(
            AddressableAssetSettings settings,
            IReadOnlyCollection<string> managedGroups,
            string configFolder)
        {
            var result = new List<AddressableAssetEntry>();
            if (settings == null || managedGroups == null || managedGroups.Count == 0) return result;

            foreach (var group in settings.groups)
            {
                if (group == null || !managedGroups.Contains(group.Name)) continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.AssetPath)) continue;
                    if (AssetFilter.ShouldExcludeByPath(entry.AssetPath, configFolder))
                        result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// <see cref="FindInvalidPathManagedEntries"/> が見つけたエントリを実際に削除する。
        /// 既存の stale クリーンアップ（<see cref="RemoveStaleEntryIfManaged"/>）と同じく、削除ごとに個別の
        /// Warning ログを出す。呼び出し側で <see cref="AddressTellerSettings.CleanupStaleEntries"/> と
        /// Configure() 失敗の有無（hasConfigureFailures 相当）を判定してから呼ぶこと（このメソッド自体は
        /// 判定を行わない）。
        /// </summary>
        /// <returns>削除したエントリの一覧（削除が無ければ空リスト）。</returns>
        internal static List<ClearedEntry> RemoveInvalidPathEntries(
            AddressableAssetSettings settings,
            IReadOnlyCollection<string> managedGroups,
            string configFolder)
        {
            var targets = FindInvalidPathManagedEntries(settings, managedGroups, configFolder);
            var cleared = new List<ClearedEntry>(targets.Count);

            foreach (var entry in targets)
            {
                var groupName = entry.parentGroup?.Name ?? "";
                var labels = entry.labels.OrderBy(l => l, StringComparer.Ordinal).ToList();
                cleared.Add(new ClearedEntry(entry.guid, entry.address, groupName, labels));

                Debug.LogWarning($"[AddressTeller] Removing stale entry: guid={entry.guid}, group='{groupName}', address='{entry.address}', path='{entry.AssetPath}', labels=[{string.Join(", ", labels)}] (path is not valid for an Addressables entry).");
                settings.RemoveAssetEntry(entry.guid);
            }

            return cleared;
        }

        /// <summary>
        /// 既存のエントリが AddressTeller 管理下のグループに属している場合のみ削除する。
        /// 管理外グループ（ユーザーが手動で登録したエントリ等）には触れない。
        /// </summary>
        /// <returns>削除を実行した場合はその内容。削除しなかった場合は null。</returns>
        private static ClearedEntry? RemoveStaleEntryIfManaged(
            string guid,
            AddressableAssetSettings settings,
            IReadOnlyCollection<string> managedGroups)
        {
            var entry = settings.FindAssetEntry(guid);
            if (entry?.parentGroup == null) return null;
            if (!managedGroups.Contains(entry.parentGroup.Name)) return null;

            var cleared = new ClearedEntry(
                guid,
                entry.address,
                entry.parentGroup.Name,
                entry.labels.OrderBy(l => l, StringComparer.Ordinal).ToList());

            Debug.LogWarning($"[AddressTeller] Removing stale entry: guid={guid}, group='{entry.parentGroup.Name}', address='{entry.address}' (no longer matched by any rule).");
            settings.RemoveAssetEntry(guid);

            return cleared;
        }
    }
}

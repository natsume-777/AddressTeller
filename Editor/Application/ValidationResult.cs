using System.Collections.Generic;

namespace AddressTeller.Editor
{
    /// <summary>
    /// Outcome categories produced by evaluating a rule set against a single asset.
    /// This is an open enum: new members may be appended in a minor release. See the Compatibility Policy
    /// (Documentation~/compatibility.md) for the full contract. Consumers must place a <c>default</c> arm
    /// in any <c>switch</c> over this type, persist values by name rather than by underlying number, and
    /// treat an unrecognized value as a problem (fail closed) rather than silently falling back to
    /// <see cref="Ok"/>.
    /// </summary>
    public enum ValidationStatus
    {
        /// <summary>Ready to apply.</summary>
        Ok = 0,
        /// <summary>No rule matched this asset (nothing was generated, including labels).</summary>
        Skipped = 1,
        /// <summary>
        /// No address candidate, but a label-only rule (AnyGroup() or an addressless Group() rule)
        /// matched and produced a label. The entry is not stale, so it is excluded from cleanup.
        /// </summary>
        LabelsOnly = 2,
        /// <summary>
        /// Two or more matching rules produced an address for the same asset, and the two or more with
        /// the lowest <c>AddressRuleBase.Order</c> tied. Producing multiple address candidates is not
        /// itself a conflict — when they differ in <c>Order</c>, the lowest-<c>Order</c> one wins and is
        /// written without error. This status is reported only for the unresolvable tie case.
        /// </summary>
        ConflictingAddress = 3,
        /// <summary>The specified group does not exist in Addressables.</summary>
        GroupNotFound = 4,
        /// <summary>AddressSelector returned null or an empty string.</summary>
        InvalidAddress = 5,
        /// <summary>The rule's Predicate / AddressSelector / LabelSelector threw an exception.</summary>
        RuleError = 6,
        /// <summary>
        /// The specified group does not exist, but AutoCreateMissingGroups is enabled, so it will be
        /// created automatically on Apply. Validate/Predict (dry-run) do not create it.
        /// </summary>
        GroupWillBeCreated = 7,
        /// <summary>AutoCreateMissingGroups is enabled, but automatic group creation failed.</summary>
        GroupCreationFailed = 8,
        /// <summary>
        /// A rule using GroupDefault() exists, but AddressableAssetSettings.DefaultGroup could not be
        /// obtained. Writes for assets affected by that rule are skipped.
        /// </summary>
        DefaultGroupUnavailable = 9,
        /// <summary>
        /// The rule class's Configure() call itself threw an exception. The rule is treated as having
        /// produced zero entries (unevaluated) for every asset, and evaluation of other rules continues.
        /// Context is null because this is not tied to a specific asset.
        /// </summary>
        RuleConfigureFailed = 10,
        /// <summary>
        /// A rule matched, an address was resolved, and the target group exists, but Addressables itself
        /// refused to create or move a usable entry for this asset: AddressableAssetSettings.CreateOrMoveEntry
        /// either returned null (the asset's main type belongs to an editor assembly), or returned an entry
        /// Addressables itself marked ReadOnly (the asset's path is not valid for an Addressables entry, but
        /// its main asset type is not from an editor assembly — Addressables silently creates a read-only
        /// placeholder entry with the address set to the GUID instead of throwing). This should not
        /// normally happen, since AddressTeller's own pre-filter uses
        /// the same path-validity rules Addressables applies (including the Config Folder exclusion) before
        /// rules run. One known case where it still can: Addressables' internal path-validity check reads
        /// the Config Folder from the *default* AddressableAssetSettings
        /// (AddressableAssetSettingsDefaultObject.Settings), not from whichever settings instance a given
        /// call is actually writing to — so if a caller explicitly targets a non-default
        /// AddressableAssetSettings instance whose Config Folder differs from the default one's, the two
        /// checks can still disagree for an asset under the default settings' Config Folder. If this does
        /// happen, no usable write was performed for this asset (any placeholder entry Addressables did
        /// create is removed again).
        /// </summary>
        EntryRejectedByAddressables = 11,
        /// <summary>
        /// Two or more different assets ended up with the same address string. Unlike
        /// <see cref="ConflictingAddress"/> (two or more rules disagreeing about one asset), this is about
        /// two or more separate assets landing on the same address — something the per-asset conflict check
        /// above cannot see. This is a report-only status: it never blocks a write, since Addressables'
        /// own Groups window accepts duplicate addresses too (AddressTeller only refuses what Addressables
        /// itself would refuse). <see cref="ValidationResult.IsOk"/> for this status depends on
        /// <see cref="ValidationResult.HasWritableDuplicate"/> — see that property for the exact rule.
        /// Reported by <c>ValidateAll</c> and by every caller of
        /// <c>AddressTellerSnapshotService.BuildPredictedSnapshot</c> — that includes not just
        /// <c>CheckCLI</c>/<c>ApplyAllCLI</c>/<c>ApplyWithValidateCLI</c> and the Apply All / Apply with
        /// Validate menu items, but also the scoped dry-run previews (rule/asset/group right-click preview,
        /// <see cref="AddressTellerScopedPreview"/>): since the existing-address side of the comparison
        /// always comes from every entry already in Addressables regardless of the scope requested, a
        /// scoped preview can surface a pre-existing duplicate that has nothing to do with the scope you
        /// asked to preview. The incremental apply AddressTeller performs from
        /// <see cref="AddressTellerPostprocessor"/> on every asset import only evaluates the changed paths,
        /// not the whole project, so it cannot detect a duplicate against an asset outside that set without
        /// re-scanning everything on every import — that path alone does not report this status.
        /// <see cref="Context"/> is null for this status, the same as <see cref="RuleConfigureFailed"/>,
        /// since it is not about one specific asset.
        /// </summary>
        DuplicateAddress = 12,
        /// <summary>
        /// The settings file (ProjectSettings/AddressTellerSettings.json) could not be loaded: it exists
        /// on disk but either could not be read (a filesystem-level error) or does not look like an
        /// AddressTeller settings file (its content could not be parsed, or the file was not written by
        /// AddressTeller). A missing file is not an error case and does not produce this status; it is
        /// treated as "use default settings" instead. Returned as the sole entry of the result list by the
        /// affected API; no other evaluation is attempted. <see cref="ValidationResult.Context"/> is null
        /// for this status, the same as <see cref="RuleConfigureFailed"/> and <see cref="DuplicateAddress"/>,
        /// since it is not about one specific asset.
        /// </summary>
        SettingsUnavailable = 13,
        /// <summary>
        /// An owned-group entry that would have been removed by <c>CleanupStaleEntries</c> — either because
        /// no rule matches the asset it belongs to anymore, or because its path is structurally invalid for
        /// an Addressables entry — was instead left in place because <c>CleanupStaleEntries</c> is off. This
        /// is a non-blocking notice (<see cref="ValidationResult.IsOk"/> is always true for this status) that
        /// exists to make visible, before you opt in, exactly what would change if you turned
        /// <c>CleanupStaleEntries</c> on. <see cref="ValidationResult.Context"/> is set for the "no longer
        /// matched" case. For the "structurally invalid path" case it is set on a best-effort basis — the
        /// path being invalid as an Addressables entry (e.g. an excluded extension or an <c>Editor</c>
        /// folder) does not usually mean the underlying asset itself cannot be resolved — and is null only
        /// when it genuinely cannot be resolved, the same as <see cref="RuleConfigureFailed"/> and
        /// <see cref="DuplicateAddress"/>.
        /// </summary>
        UnmatchedEntryKept = 14,
        /// <summary>
        /// A rule matched and a single winning address candidate was chosen (no tie — see
        /// <see cref="ConflictingAddress"/> for that case), but a rule capable of producing an address
        /// (its <c>AddressSelector</c> is non-null) — at an <c>Order</c> equal to or lower than (i.e.
        /// higher priority than or tied with) the winning candidate's — threw an exception while being
        /// evaluated for this asset. This is usually a different, higher-or-equal-priority rule than the
        /// winner, but it can also be the winning rule itself: its <c>AddressSelector</c> can succeed
        /// (producing the winning candidate) and a later <c>LabelSelector</c> on the same rule chain can
        /// still throw — that still counts, since the winning rule's own evaluation for this asset did not
        /// complete cleanly. Nothing is written for this asset: neither the address nor any labels from any
        /// matching rule (address or label-only). Writing the lower-priority winner's address anyway would
        /// silently resolve what is really an ambiguous state — the failed rule might have outranked the
        /// winner had it not thrown — the same reasoning that makes <see cref="ConflictingAddress"/> refuse
        /// to guess. A rule whose <c>AddressSelector</c> is null (label-only) throwing does not trigger this
        /// status regardless of its <c>Order</c>, since it was never in a position to outrank the winner.
        /// The <see cref="ValidationStatus.RuleError"/> entry for the throwing rule itself is reported
        /// separately (it is the cause; this status is the effect on this asset's write). For
        /// <c>ApplyAll</c>/<c>ValidateAll</c>/<c>BuildPredictedSnapshot</c>, both appear together in the same
        /// returned <see cref="System.Collections.Generic.IReadOnlyList{ValidationResult}"/>. <c>Explain</c>
        /// does not return a single combined list of <see cref="ValidationResult"/>s the way those three do —
        /// the equivalent information is still available, just structured per-asset instead.
        /// </summary>
        BlockedByRuleError = 15,
        /// <summary>
        /// The same asset (GUID) has an entry in two or more Addressables groups at once. Addressables itself
        /// does not deduplicate across groups — its own duplicate-removal logic (<c>AddressableAssetGroup</c>'s
        /// internal entry map) only operates within a single group — so this state can persist once it exists
        /// (for example after a VCS merge combines two branches that each added the same asset to a different
        /// group). Once it exists, evaluating this asset the normal way is not well-defined: which of the
        /// duplicate entries is "the" entry for this asset? <c>AddressableAssetSettings.FindAssetEntry</c>
        /// silently picks whichever group comes first in <c>AddressableAssetSettings.groups</c>, so acting on
        /// that pick would mean editing or moving one entry while leaving the other one behind unnoticed —
        /// exactly the kind of ambiguous state AddressTeller refuses to guess through (the same reasoning as
        /// <see cref="ConflictingAddress"/> and <see cref="BlockedByRuleError"/>). For that reason, whenever
        /// this status is detected, the run stops before evaluating or writing anything for any asset: no
        /// rule is evaluated, and <c>ApplyAll</c>/<c>RemoveEntriesForDeletedAssets</c> write nothing.
        /// This detection runs before that per-asset evaluation, so <see cref="Context"/> is null, the same
        /// as <see cref="RuleConfigureFailed"/>/<see cref="DuplicateAddress"/>/<see cref="SettingsUnavailable"/>;
        /// the message identifies the asset by GUID and lists each group/address it currently sits in.
        /// <c>Explain</c> is a read-only, per-asset diagnostic tool and does not stop on this status.
        /// The public <see cref="AddressTellerSnapshotService.Diff"/> also does not throw when it encounters
        /// this state — it picks the first entry for the duplicated GUID and ignores the rest, documented on
        /// that method.
        /// </summary>
        DuplicateAssetEntry = 16,
    }

    /// <summary>
    /// Result of evaluating rules against a single asset, as returned by ApplyAll/Validate/Predict/Explain.
    /// <see cref="Context"/> is null when <see cref="Status"/> is <see cref="ValidationStatus.RuleConfigureFailed"/>,
    /// <see cref="ValidationStatus.DuplicateAddress"/>, <see cref="ValidationStatus.SettingsUnavailable"/>, or
    /// <see cref="ValidationStatus.DuplicateAssetEntry"/> — the first three are not tied to a single asset, and
    /// the last is detected before any per-asset evaluation runs (its message identifies the asset by GUID
    /// instead). For <see cref="ValidationStatus.UnmatchedEntryKept"/> it is always
    /// set for the "no longer matched" case, and set on a best-effort basis (null only when genuinely
    /// unresolvable) for the "structurally invalid path" case — see that status's XML doc.
    /// </summary>
    public sealed class ValidationResult
    {
        /// <summary>The asset this result is about.</summary>
        public AssetContext Context { get; }

        /// <summary>Outcome of evaluating this asset.</summary>
        public ValidationStatus Status { get; }

        /// <summary>Human-readable message describing the result. Null when <see cref="Status"/> is <see cref="ValidationStatus.Ok"/>.</summary>
        public string Message { get; }

        /// <summary>
        /// Set only when Status is ConflictingAddress. Contains only the candidates tied at the lowest
        /// <c>Order</c> — the ones an unambiguous winner could not be chosen between. Higher-<c>Order</c>
        /// candidates that also matched but lost to a lower-<c>Order</c> one are not included here, since
        /// they were not part of the conflict.
        /// </summary>
        public IReadOnlyList<AddressCandidate> ConflictingCandidates { get; }

        /// <summary>
        /// Set only when Status is <see cref="ValidationStatus.DuplicateAddress"/>. True when at least one
        /// of the assets sharing the duplicated address is one AddressTeller would itself write an address
        /// for during this run — i.e. the duplicate falls within a rule the caller controls and can fix.
        /// False when every asset sharing the address falls outside this run's address-writing scope (for
        /// example, two pre-existing entries neither rule touches this run) — AddressTeller has no rule to
        /// change to resolve it, so it is surfaced as a non-blocking notice instead. Drives the
        /// <see cref="IsOk"/> split for this status: see there for how it is used.
        /// </summary>
        public bool HasWritableDuplicate { get; }

        // Skipped はルール対象外という正常系であり、ApplyAll/ValidateAll の issues には積まれない（IsOk = true）。
        // LabelsOnly はラベルのみルールがマッチした正常系であり、同様に issues には積まれない（IsOk = true）。
        // GroupWillBeCreated は AutoCreateMissingGroups ON 時の作成予定通知であり、Apply をブロックしない（IsOk = true）。
        // UnmatchedEntryKept は CleanupStaleEntries OFF 時、ON なら削除されるはずだったエントリが残っている
        // ことの通知であり、書き込みも削除も行わない（IsOk = true）。
        // DuplicateAddress は書き込みを止めない報告専用ステータスだが、重複の当事者に「このランで
        // AddressTeller 自身が書くアドレス」が含まれるかどうかで重大度を分ける。含まれれば利用者のルールで
        // 直せる問題なので Error（IsOk = false）、含まれなければ AddressTeller に直す手立てが無い既存の状態
        // なので Warning（IsOk = true）として扱う。
        /// <summary>
        /// True when <see cref="Status"/> is <see cref="ValidationStatus.Ok"/>,
        /// <see cref="ValidationStatus.Skipped"/>, <see cref="ValidationStatus.LabelsOnly"/>,
        /// <see cref="ValidationStatus.GroupWillBeCreated"/>, or
        /// <see cref="ValidationStatus.UnmatchedEntryKept"/> — i.e. this result does not represent a
        /// problem. Also true for <see cref="ValidationStatus.DuplicateAddress"/> when
        /// <see cref="HasWritableDuplicate"/> is false (AddressTeller cannot resolve it itself, so it is
        /// reported as a notice rather than a blocking problem).
        /// </summary>
        public bool IsOk => Status == ValidationStatus.Ok || Status == ValidationStatus.Skipped
            || Status == ValidationStatus.LabelsOnly || Status == ValidationStatus.GroupWillBeCreated
            || Status == ValidationStatus.UnmatchedEntryKept
            || (Status == ValidationStatus.DuplicateAddress && !HasWritableDuplicate);

        /// <summary>
        /// True when this result represents a problem that should abort a write (Apply). Defined as
        /// <c>!IsOk &amp;&amp; Status != ValidationStatus.DuplicateAddress</c>: <see cref="ValidationStatus.DuplicateAddress"/>
        /// is excluded even when <see cref="IsOk"/> is false, since it is a report-only status that never
        /// blocks a write by itself (see <see cref="HasWritableDuplicate"/>) — a project-wide Apply should
        /// not abort just because one pre-existing duplicate address was noticed. For every other status,
        /// this is equivalent to <c>!IsOk</c>. Use <see cref="IsOk"/> to ask "did this asset have a
        /// problem at all" (e.g. for display/reporting); use <see cref="IsBlocking"/> to ask "should this
        /// stop a write" (e.g. before Apply, or when classifying Apply-side exit codes/failures).
        /// </summary>
        public bool IsBlocking => !IsOk && Status != ValidationStatus.DuplicateAddress;

        /// <summary>
        /// 公開コンストラクタではなく internal（ライブラリ内部の評価パイプラインからのみ構築される想定）。
        /// テストからは <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> 経由で参照する。
        /// </summary>
        internal ValidationResult(AssetContext context, ValidationStatus status, string message,
            IReadOnlyList<AddressCandidate> conflictingCandidates = null, bool hasWritableDuplicate = false)
        {
            Context = context;
            Status = status;
            Message = message;
            ConflictingCandidates = conflictingCandidates;
            HasWritableDuplicate = hasWritableDuplicate;
        }
    }
}

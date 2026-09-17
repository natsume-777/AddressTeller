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
        /// <summary>Two or more rules produced an address (conflict).</summary>
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
    }

    /// <summary>
    /// Result of evaluating rules against a single asset, as returned by ApplyAll/Validate/Predict/Explain.
    /// <see cref="Context"/> is null when <see cref="Status"/> is <see cref="ValidationStatus.RuleConfigureFailed"/>,
    /// since that status is not tied to a specific asset.
    /// </summary>
    public sealed class ValidationResult
    {
        /// <summary>The asset this result is about.</summary>
        public AssetContext Context { get; }

        /// <summary>Outcome of evaluating this asset.</summary>
        public ValidationStatus Status { get; }

        /// <summary>Human-readable message describing the result. Null when <see cref="Status"/> is <see cref="ValidationStatus.Ok"/>.</summary>
        public string Message { get; }

        /// <summary>Set only when Status is ConflictingAddress: the competing address candidates.</summary>
        public IReadOnlyList<AddressCandidate> ConflictingCandidates { get; }

        // Skipped はルール対象外という正常系であり、ApplyAll/ValidateAll の issues には積まれない（IsOk = true）。
        // LabelsOnly はラベルのみルールがマッチした正常系であり、同様に issues には積まれない（IsOk = true）。
        // GroupWillBeCreated は AutoCreateMissingGroups ON 時の作成予定通知であり、Apply をブロックしない（IsOk = true）。
        /// <summary>
        /// True when <see cref="Status"/> is <see cref="ValidationStatus.Ok"/>,
        /// <see cref="ValidationStatus.Skipped"/>, <see cref="ValidationStatus.LabelsOnly"/>, or
        /// <see cref="ValidationStatus.GroupWillBeCreated"/> — i.e. this result does not represent a problem.
        /// </summary>
        public bool IsOk => Status == ValidationStatus.Ok || Status == ValidationStatus.Skipped
            || Status == ValidationStatus.LabelsOnly || Status == ValidationStatus.GroupWillBeCreated;

        /// <summary>
        /// 公開コンストラクタではなく internal（ライブラリ内部の評価パイプラインからのみ構築される想定）。
        /// テストからは <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> 経由で参照する。
        /// </summary>
        internal ValidationResult(AssetContext context, ValidationStatus status, string message,
            IReadOnlyList<AddressCandidate> conflictingCandidates = null)
        {
            Context = context;
            Status = status;
            Message = message;
            ConflictingCandidates = conflictingCandidates;
        }
    }
}

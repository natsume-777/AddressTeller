using System.Collections.Generic;

namespace AddressTeller.Editor
{
    public enum ValidationStatus
    {
        /// <summary>適用可能。</summary>
        Ok,
        /// <summary>アドレス候補なし（ルール対象外）。</summary>
        Skipped,
        /// <summary>2件以上のルールがアドレスを発行した（競合）。</summary>
        ConflictingAddress,
        /// <summary>指定グループが Addressables に存在しない。</summary>
        GroupNotFound,
        /// <summary>AddressSelector が null または空文字を返した。</summary>
        InvalidAddress,
        /// <summary>ルールの Predicate / AddressSelector / LabelSelector が例外を送出した。</summary>
        RuleError,
        /// <summary>
        /// 指定グループが存在しないが、AutoCreateMissingGroups が有効なため Apply 時に自動作成される予定。
        /// Validate/Predict（dry-run）では実際の作成は行わない。
        /// </summary>
        GroupWillBeCreated,
        /// <summary>AutoCreateMissingGroups が有効な状態で、グループの自動作成に失敗した。</summary>
        GroupCreationFailed,
        /// <summary>
        /// GroupDefault() を使うルールが存在するが、AddressableAssetSettings.DefaultGroup を取得できなかった。
        /// 該当ルールが関わるアセットへの書き込みはスキップされる。
        /// </summary>
        DefaultGroupUnavailable,
    }

    public sealed class ValidationResult
    {
        public AssetContext Context { get; }
        public ValidationStatus Status { get; }
        public string Message { get; }

        /// <summary>競合時のみ設定される候補リスト。</summary>
        public IReadOnlyList<AddressCandidate> ConflictingCandidates { get; }

        // Skipped はルール対象外という正常系であり、ApplyAll/ValidateAll の issues には積まれない（IsOk = true）。
        // GroupWillBeCreated は AutoCreateMissingGroups ON 時の作成予定通知であり、Apply をブロックしない（IsOk = true）。
        public bool IsOk => Status == ValidationStatus.Ok || Status == ValidationStatus.Skipped
            || Status == ValidationStatus.GroupWillBeCreated;

        public ValidationResult(AssetContext context, ValidationStatus status, string message,
            IReadOnlyList<AddressCandidate> conflictingCandidates = null)
        {
            Context = context;
            Status = status;
            Message = message;
            ConflictingCandidates = conflictingCandidates;
        }
    }
}

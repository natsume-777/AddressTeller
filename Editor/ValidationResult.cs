using System.Collections.Generic;

namespace Natsume777.AddressTeller.Editor
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
    }

    public sealed class ValidationResult
    {
        public AssetContext Context { get; }
        public ValidationStatus Status { get; }
        public string Message { get; }

        /// <summary>競合時のみ設定される候補リスト。</summary>
        public IReadOnlyList<AddressCandidate> ConflictingCandidates { get; }

        public bool IsOk => Status == ValidationStatus.Ok || Status == ValidationStatus.Skipped;

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

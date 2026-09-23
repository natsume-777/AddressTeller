using System;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// ValidationResult.IsBlocking の単体テスト。定義（!IsOk &amp;&amp; Status != DuplicateAddress）を
    /// 各ステータスで確認する。AddressTellerGroupAutoCreateTests の IsBlocking_* が集合単位の判定
    /// （issues.Any(i =&gt; i.IsBlocking) 経由）を検証しているのに対し、こちらは公開プロパティ単体の値を見る。
    /// </summary>
    public class ValidationResultTests
    {
        private static AssetContext Ctx(string guid, string path)
            => new(guid, path, typeof(UnityEngine.Object));

        [Test]
        public void Ok_IsNotBlocking()
        {
            var result = new ValidationResult(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.Ok, null);

            Assert.IsFalse(result.IsBlocking);
        }

        [Test]
        public void ConflictingAddress_IsBlocking()
        {
            var result = new ValidationResult(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.ConflictingAddress, "conflict");

            Assert.IsTrue(result.IsBlocking);
        }

        [Test]
        public void DuplicateAssetEntry_IsBlocking()
        {
            var result = new ValidationResult(null, ValidationStatus.DuplicateAssetEntry, "duplicate entry");

            Assert.IsFalse(result.IsOk);
            Assert.IsTrue(result.IsBlocking);
        }

        [Test]
        public void GroupWillBeCreated_IsNotBlocking()
        {
            // IsOk=true な通知ステータス。IsBlocking も false のはず。
            var result = new ValidationResult(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.GroupWillBeCreated, "will be created");

            Assert.IsFalse(result.IsBlocking);
        }

        [Test]
        public void UnmatchedEntryKept_IsOkTrue_NotBlocking()
        {
            // IsOk=true な通知ステータス。CleanupStaleEntries が OFF の間、ON なら削除される対象だった
            // ことを知らせるだけで、書き込みも削除も伴わない。
            var result = new ValidationResult(Ctx("guid-a", "Assets/A.prefab"), ValidationStatus.UnmatchedEntryKept, "kept");

            Assert.IsTrue(result.IsOk);
            Assert.IsFalse(result.IsBlocking);
        }

        [Test]
        public void DuplicateAddress_HasWritableDuplicateFalse_IsOkTrue_NotBlocking()
        {
            var result = new ValidationResult(null, ValidationStatus.DuplicateAddress, "duplicate", hasWritableDuplicate: false);

            Assert.IsTrue(result.IsOk);
            Assert.IsFalse(result.IsBlocking, "DuplicateAddress は書き込みを止めない報告専用ステータスであり、IsOk の値に関わらず IsBlocking=false。");
        }

        [Test]
        public void DuplicateAddress_HasWritableDuplicateTrue_IsOkFalse_StillNotBlocking()
        {
            var result = new ValidationResult(null, ValidationStatus.DuplicateAddress, "duplicate", hasWritableDuplicate: true);

            Assert.IsFalse(result.IsOk, "書き込み対象を含む重複は Error 扱い（IsOk=false）。");
            Assert.IsFalse(result.IsBlocking, "IsOk=false であっても DuplicateAddress は Apply を中止させない。");
        }

        /// <summary>
        /// compatibility.md の Enums 節が定める方針（DuplicateAddress を除く各ステータスは
        /// 「IsOk=false かつ IsBlocking=true」か「IsOk=true」のどちらか一方に固定）を、全 ValidationStatus
        /// 値を総なめして機械的に確認する。将来 Step で追加されたステータスがこの方針から外れると失敗する。
        /// </summary>
        [Test]
        public void AllStatusesExceptDuplicateAddress_IsOkEqualsNotIsBlocking()
        {
            foreach (ValidationStatus status in Enum.GetValues(typeof(ValidationStatus)))
            {
                if (status == ValidationStatus.DuplicateAddress) continue;

                var result = new ValidationResult(null, status, "message");

                Assert.AreEqual(result.IsOk, !result.IsBlocking,
                    $"{status}: DuplicateAddress 以外のステータスは IsOk == !IsBlocking を満たすべき。");
            }
        }
    }
}

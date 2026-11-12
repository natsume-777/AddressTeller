using System.IO;
using NUnit.Framework;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// ラベル台帳（AddressTellerLabelLedger）をテスト前後で空へ戻すヘルパー。Apply がラベルを書くたびに台帳へ
    /// 名前が積まれるため、台帳の中身を検証するテストクラスは SetUp と TearDown でこれを呼ぶ。
    /// 台帳の読み書き先は AddressTellerAddressablesPollutionGuard が一時ファイルへ切り替え済みなので、
    /// ここで消すのはその一時ファイルであり、本番の ProjectSettings には触れない。
    /// </summary>
    internal static class LabelLedgerTestUtil
    {
        public static void Reset()
        {
            // 切り替えられていない（FilePathOverride が null）なら、AbsoluteFilePath は本番の
            // ProjectSettings/AddressTellerLabelLedger.json を指す。それを消さないよう、削除せずテストを失敗させる。
            if (AddressTellerLabelLedger.FilePathOverride == null)
                Assert.Fail("ラベル台帳の読み書き先がテスト用に隔離されていない（FilePathOverride が null）。本番の台帳を削除しないため中止する。");

            var path = AddressTellerLabelLedger.AbsoluteFilePath;
            if (File.Exists(path))
            {
                // 書き込み不可の属性を付けたテストの後始末でも消せるようにする。
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            AddressTellerLabelLedger.ResetInMemoryState();
        }
    }

    /// <summary>
    /// ラベル台帳を各テストの前後で空へ戻す基底クラス。Apply 系・予測系のテストクラスはこれを継承する。
    /// 台帳の一時ファイルはテストアセンブリ全体で共有されるため、継承しないクラスが積んだラベル名が
    /// 後続のテストの結果（ValidateAll や dry-run の <see cref="ValidationStatus.StaleManagedLabelKept"/> 通知）に
    /// 混ざらないようにする。NUnit のアセンブリレベル ITestAction は効かなかったため、明示的な SetUp/TearDown にしている。
    /// </summary>
    public abstract class LabelLedgerIsolatedFixture
    {
        [SetUp]
        public void ResetLabelLedgerBeforeTest() => LabelLedgerTestUtil.Reset();

        [TearDown]
        public void ResetLabelLedgerAfterTest() => LabelLedgerTestUtil.Reset();
    }

    /// <summary>
    /// <see cref="LabelLedgerIsolatedFixture"/> が実際に効いていることの確認。Order で並べた2つのテストの前者が台帳へ
    /// 名前を書き、後者の開始時にそれが残っていないことを見る。
    /// </summary>
    public class LabelLedgerIsolationTests : LabelLedgerIsolatedFixture
    {
        [Test, Order(1)]
        public void Step1_WritesANameToTheLedger()
        {
            AddressTellerLabelLedger.Record(new[] { "leaked" });

            Assert.IsTrue(AddressTellerLabelLedger.IsManaged("leaked"));
            Assert.IsTrue(File.Exists(AddressTellerLabelLedger.AbsoluteFilePath));
        }

        [Test, Order(2)]
        public void Step2_NameFromThePreviousTestIsGone()
        {
            Assert.IsFalse(AddressTellerLabelLedger.IsManaged("leaked"));
            CollectionAssert.IsEmpty(AddressTellerLabelLedger.GetAll());
            Assert.IsFalse(File.Exists(AddressTellerLabelLedger.AbsoluteFilePath));
        }

        [Test]
        public void Reset_WithoutTheTestOverride_FailsInsteadOfTouchingTheRealLedger()
        {
            var original = AddressTellerLabelLedger.FilePathOverride;
            AddressTellerLabelLedger.FilePathOverride = null;
            try
            {
                Assert.Throws<AssertionException>(() => LabelLedgerTestUtil.Reset());
            }
            finally
            {
                AddressTellerLabelLedger.FilePathOverride = original;
            }
        }
    }
}

using NUnit.Framework;
using AddressTeller.Editor.Tests.PublicApiApprovalFixtures;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// <see cref="PublicApiSurfaceFormatter"/> 自体の単体テスト。
    /// 実アセンブリの形状だけでは通らない経路（event/delegate/indexer/ref・in・params引数/
    /// ネストしたジェネリック型/enumの非0定数/protectedメンバー）を、専用フィクスチャ型で直接検証する。
    /// </summary>
    public class PublicApiSurfaceFormatterTests
    {
        [Test]
        public void FormatType_Delegate_IncludesReturnTypeAndParameterModifiers()
        {
            var expected =
                "delegate System.Int32 AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleDelegate" +
                "(System.Int32 x, ref System.String y, in System.Double z, params System.Object[] extra)\n";

            Assert.AreEqual(expected, PublicApiSurfaceFormatter.FormatType(typeof(SampleDelegate)));
        }

        [Test]
        public void FormatType_Enum_ListsMembersWithInvariantCultureValues()
        {
            var expected =
                "enum AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleEnum\n" +
                "  EnumMember Alpha = 0\n" +
                "  EnumMember Beta = 5\n";

            Assert.AreEqual(expected, PublicApiSurfaceFormatter.FormatType(typeof(SampleEnum)));
        }

        [Test]
        public void FormatType_DerivedClass_ShowsBaseTypeAndDedupesInheritedInterfaces()
        {
            // SampleDerived : SampleBase(IDisposable実装), ISampleInterfaceA, ISampleInterfaceB
            // 基底クラス経由で既に持っているIDisposableは重複除去され、
            // 自身で追加した2インターフェースのみがOrdinalソートで表示されることを検証する。
            var expected =
                "class AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleDerived : " +
                "AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleBase, " +
                "AddressTeller.Editor.Tests.PublicApiApprovalFixtures.ISampleInterfaceA, " +
                "AddressTeller.Editor.Tests.PublicApiApprovalFixtures.ISampleInterfaceB\n" +
                "  Constructor ()\n";

            Assert.AreEqual(expected, PublicApiSurfaceFormatter.FormatType(typeof(SampleDerived)));
        }

        [Test]
        public void FormatType_EventIndexerAndRefInParamsMembers_FormatsCorrectly()
        {
            var expected =
                "class AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleMembers\n" +
                "  Constructor ()\n" +
                "  Event System.Action<System.Int32> Changed\n" +
                "  Method System.Void MethodWithRefInParams(ref System.Int32 a, in System.String b, params System.Object[] c)\n" +
                "  Property System.Int32 Item[System.Int32 index] { get; }\n" +
                "  Property AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleContainer<System.Int32>+NestedItem NestedProperty { get; }\n";

            Assert.AreEqual(expected, PublicApiSurfaceFormatter.FormatType(typeof(SampleMembers)));
        }

        [Test]
        public void FormatType_ProtectedMembers_AreIncludedWithAccessibilityPrefix()
        {
            var expected =
                "class AddressTeller.Editor.Tests.PublicApiApprovalFixtures.SampleWithProtectedMembers\n" +
                "  Constructor ()\n" +
                "  Field protected System.Int32 ProtectedField\n" +
                "  Field protected internal System.String ProtectedInternalField\n" +
                "  Method protected virtual System.Void ProtectedMethod()\n" +
                "  Property System.Int32 PublicWithProtectedSetter { get; protected set; }\n";

            Assert.AreEqual(expected, PublicApiSurfaceFormatter.FormatType(typeof(SampleWithProtectedMembers)));
        }
    }
}

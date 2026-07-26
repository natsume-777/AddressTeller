namespace AddressTeller.Editor.Tests.PublicApiApprovalFixtures
{
    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ（delegate。ref/in/params引数を含む）。
    /// </summary>
    public delegate int SampleDelegate(int x, ref string y, in double z, params object[] extra);

    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ（enum。0始まりでない値を含む）。
    /// </summary>
    public enum SampleEnum
    {
        Alpha = 0,
        Beta = 5,
    }

    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ（ネストしたジェネリック型）。
    /// </summary>
    public class SampleContainer<T>
    {
        public class NestedItem
        {
        }
    }

    public interface ISampleInterfaceA
    {
    }

    public interface ISampleInterfaceB
    {
    }

    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ（基底クラス経由のインターフェース）。
    /// </summary>
    public class SampleBase : System.IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ。
    /// 基底クラス経由の IDisposable は重複除去され、自身で追加した2つのインターフェースのみが
    /// Ordinalソートされて表示されることを検証する対象。
    /// </summary>
    public class SampleDerived : SampleBase, ISampleInterfaceA, ISampleInterfaceB
    {
    }

    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ（event/indexer/ref・in・params引数）。
    /// </summary>
    public class SampleMembers
    {
#pragma warning disable 0067 // フィクスチャ用途のため、発火させないことを許容する
        public event System.Action<int> Changed;
#pragma warning restore 0067

        public int this[int index] => index;

        public void MethodWithRefInParams(ref int a, in string b, params object[] c)
        {
        }

        public SampleContainer<int>.NestedItem NestedProperty => null;
    }

    /// <summary>
    /// <see cref="PublicApiSurfaceFormatterTests"/> 専用のフィクスチャ（protected / protected internal メンバー）。
    /// </summary>
#pragma warning disable 0169, 0649 // フィクスチャ用の未使用/未代入フィールド警告を抑止する
    public class SampleWithProtectedMembers
    {
        protected int ProtectedField;
        protected internal string ProtectedInternalField;
        private int PrivateField;

        public int PublicWithProtectedSetter { get; protected set; }

        protected virtual void ProtectedMethod()
        {
        }
    }
#pragma warning restore 0169, 0649
}

using System;

namespace AddressTeller
{
    /// <summary>
    /// ルール定義の基底クラス。利用者はこれを継承し、C# でアドレス／ラベル付与ルールを記述する。
    /// </summary>
    public abstract class AddressRuleBase
    {
        /// <summary>評価順序。小さいほど先に評価される。</summary>
        public virtual int Order => 0;

        /// <summary>ルールを組み立てる。</summary>
        public abstract void Configure(IAddressRuleBuilder rules);
    }

    /// <summary>
    /// ルール組み立て用ビルダー。Group() でグループルールを追加する。
    /// </summary>
    public interface IAddressRuleBuilder
    {
        IAddressRuleGroupBuilder Group(string groupName);

        /// <summary>
        /// Addressables の DefaultGroup にアドレス／ラベルを付与する。グループ名は評価時に
        /// AddressableAssetSettings.DefaultGroup から解決されるため、DefaultGroup をリネームしても
        /// 追従する。Where / Address / Label は <see cref="Group(string)"/> と同様にチェーンできる。
        /// </summary>
        IAddressRuleGroupBuilder GroupDefault();

        /// <summary>
        /// グループに属さないラベル専用ルールを追加する。
        /// アドレスを持つアセットにパスなどの条件でラベルを付与したい場合に使う。
        /// </summary>
        ILabelRuleBuilder AnyGroup();
    }

    /// <summary>
    /// <see cref="IAddressRuleBuilder.AnyGroup"/> から返されるラベル専用ビルダー。
    /// Address() は存在せず、Where / Label のみ指定できる。
    /// </summary>
    public interface ILabelRuleBuilder
    {
        ILabelRuleBuilder Where(Func<AssetContext, bool> predicate);
        ILabelRuleBuilder Where(Func<AssetContext, bool> predicate, string description);
        ILabelRuleBuilder Where(AssetCondition condition);
        ILabelRuleBuilder Label(Func<AssetContext, string> selector);
        ILabelRuleBuilder Label(string label);
    }

    /// <summary>
    /// グループ単位のルール設定。Where / Address / Label をチェーンで記述する。
    /// </summary>
    public interface IAddressRuleGroupBuilder
    {
        /// <summary>
        /// 1グループにつき1回のみ呼び出し可能。複数の条件は1つのラムダ式に && でまとめること。
        /// 2回目の呼び出しは InvalidOperationException をスローする。
        /// </summary>
        IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate);

        /// <summary>
        /// 1グループにつき1回のみ呼び出し可能（もう一方の Where() オーバーロードと合わせて1回）。
        /// description はエラーメッセージで「どの Where 条件にマッチしたか」を示すために使われる。
        /// 2回目の呼び出しは InvalidOperationException をスローする。
        /// </summary>
        IAddressRuleGroupBuilder Where(Func<AssetContext, bool> predicate, string description);

        /// <summary>
        /// AssetCondition を使った Where 指定。Predicate と Description を condition から引き継ぐ。
        /// 1グループにつき1回のみ呼び出し可能（他の Where() オーバーロードと合わせて1回）。
        /// 2回目の呼び出しは InvalidOperationException をスローする。
        /// </summary>
        IAddressRuleGroupBuilder Where(AssetCondition condition);

        IAddressRuleGroupBuilder Address(Func<AssetContext, string> selector);
        IAddressRuleGroupBuilder Address(string address);
        IAddressRuleGroupBuilder Label(Func<AssetContext, string> selector);
        IAddressRuleGroupBuilder Label(string label);
    }
}

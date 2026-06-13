using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Natsume777.AddressTeller.Editor
{
    /// <summary>
    /// ロード済みアセンブリから AddressRuleBase 継承クラスを収集し Order 昇順でソートする。
    /// </summary>
    public static class RuleCollector
    {
        // ルール集合はドメインリロードまで不変なのでキャッシュする。
        // static フィールドはドメインリロード時に自動でリセットされるため、
        // 明示的な無効化処理は不要。
        private static IReadOnlyList<AddressRuleBase> s_cachedRules;

        /// <summary>全ロード済みアセンブリから収集する。テストアセンブリ（nunit.framework 参照）は除外する。結果はドメインリロードまでキャッシュされる。</summary>
        public static IReadOnlyList<AddressRuleBase> CollectRules()
            => s_cachedRules ??= CollectRules(AppDomain.CurrentDomain.GetAssemblies().Where(a => !ReferencesNUnit(a)));

        /// <summary>テストアセンブリかどうかを nunit.framework への参照の有無で判定する。</summary>
        private static bool ReferencesNUnit(Assembly assembly)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name == "nunit.framework") return true;
            }
            return false;
        }

        /// <summary>指定アセンブリのみから収集する（テスト・スコープ制限に使う）。</summary>
        public static IReadOnlyList<AddressRuleBase> CollectRules(IEnumerable<Assembly> assemblies)
        {
            var rules = new List<AddressRuleBase>();

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract) continue;
                    if (!typeof(AddressRuleBase).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                    rules.Add((AddressRuleBase)Activator.CreateInstance(type));
                }
            }

            rules.Sort((a, b) => a.Order.CompareTo(b.Order));
            return rules;
        }

        /// <summary>同一 Order 値を持つルールクラスのグループを返す（Order が重複していないものは含まない）。</summary>
        public static IEnumerable<IGrouping<int, AddressRuleBase>> FindDuplicateOrders(IReadOnlyList<AddressRuleBase> rules)
            => rules.GroupBy(r => r.Order).Where(g => g.Count() > 1);

        /// <summary>
        /// <see cref="CollectRules()"/>（キャッシュ済み）から、Project Settings で無効化されたルールクラスを
        /// 除外した一覧を毎回新しく生成して返す。リフレクションは再実行しない。
        /// </summary>
        public static IReadOnlyList<AddressRuleBase> CollectEnabledRules()
            => CollectEnabledRules(CollectRules(), AddressTellerSettings.DisabledRuleClassNames);

        /// <summary>
        /// <paramref name="rules"/> から <paramref name="disabledClassNames"/> に含まれる型のルールを除外する。
        /// Order 順は維持される。テストや特定スコープでのフィルタ計算に使う。
        /// </summary>
        public static IReadOnlyList<AddressRuleBase> CollectEnabledRules(IReadOnlyList<AddressRuleBase> rules, IReadOnlyList<string> disabledClassNames)
        {
            if (disabledClassNames == null || disabledClassNames.Count == 0) return rules.ToList();

            var disabled = new HashSet<string>(disabledClassNames);
            return rules.Where(r => !disabled.Contains(r.GetType().FullName)).ToList();
        }
    }
}

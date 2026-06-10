using System;
using System.Collections.Generic;
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

        /// <summary>全ロード済みアセンブリから収集する。結果はドメインリロードまでキャッシュされる。</summary>
        public static IReadOnlyList<AddressRuleBase> CollectRules()
            => s_cachedRules ??= CollectRules(AppDomain.CurrentDomain.GetAssemblies());

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
    }
}

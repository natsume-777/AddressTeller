using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AddressTeller.Editor
{
    /// <summary>
    /// ロード済みアセンブリから AddressRuleBase 継承クラスを収集し Order 昇順でソートする。
    /// </summary>
    internal static class RuleCollector
    {
        // ルール集合はドメインリロードまで不変なのでキャッシュする。
        // static フィールドはドメインリロード時に自動でリセットされるため、
        // 明示的な無効化処理は不要。
        private static IReadOnlyList<AddressRuleBase> s_cachedRules;

        /// <summary>
        /// 全ロード済みアセンブリから収集する。テストアセンブリ（nunit.framework 参照）は除外する。結果はドメインリロードまでキャッシュされる。
        /// キャッシュ初回構築時に <see cref="RuleEvaluationPipeline.WarnOnDuplicateOrders"/> を1回だけ呼び、
        /// Order 重複の警告を出す（毎 import / 毎走査での重複警告を避けるため）。
        /// </summary>
        public static IReadOnlyList<AddressRuleBase> CollectRules()
        {
            if (s_cachedRules != null) return s_cachedRules;

            var rules = CollectRules(AppDomain.CurrentDomain.GetAssemblies().Where(a => !ReferencesNUnit(a)));
            RuleEvaluationPipeline.WarnOnDuplicateOrders(rules);
            s_cachedRules = rules;
            return s_cachedRules;
        }

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

            // Order が同値の場合は型のフルネーム（Ordinal）で決定的に並べる。
            return rules
                .OrderBy(r => r.Order)
                .ThenBy(r => r.GetType().FullName, StringComparer.Ordinal)
                .ToList();
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

        /// <summary>
        /// <see cref="CollectEnabledRules(IReadOnlyList{AddressRuleBase}, IReadOnlyList{string})"/> に、
        /// 永続設定（<paramref name="disabledClassNames"/>）とは別経路で一時的に追加指定された除外クラス名
        /// （<paramref name="additionalDisabledClassNames"/>、CLIの一時除外指定を想定）の和集合を適用するオーバーロード。
        /// </summary>
        /// <param name="additionalDisabledClassNames">
        /// <paramref name="rules"/> に存在しない FullName が1件でも含まれる場合は false を返す
        /// （CLIのtypoによる除外漏れの静かな放置を防ぐ）。
        /// </param>
        /// <param name="unknownClassNames">false の場合、未知だった FullName の一覧。成功時は空。</param>
        public static bool TryCollectEnabledRules(
            IReadOnlyList<AddressRuleBase> rules,
            IReadOnlyList<string> disabledClassNames,
            IReadOnlyList<string> additionalDisabledClassNames,
            out IReadOnlyList<AddressRuleBase> enabledRules,
            out IReadOnlyList<string> unknownClassNames)
        {
            if (additionalDisabledClassNames != null && additionalDisabledClassNames.Count > 0)
            {
                var known = new HashSet<string>(rules.Select(r => r.GetType().FullName));
                var unknown = additionalDisabledClassNames.Where(name => !known.Contains(name)).Distinct().ToList();
                if (unknown.Count > 0)
                {
                    enabledRules = null;
                    unknownClassNames = unknown;
                    return false;
                }
            }

            var disabled = (disabledClassNames ?? Array.Empty<string>())
                .Concat(additionalDisabledClassNames ?? Array.Empty<string>())
                .Distinct()
                .ToList();

            enabledRules = CollectEnabledRules(rules, disabled);
            unknownClassNames = Array.Empty<string>();
            return true;
        }
    }
}

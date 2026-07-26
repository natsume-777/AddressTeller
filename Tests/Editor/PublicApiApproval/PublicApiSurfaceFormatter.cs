using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AddressTeller.Editor.Tests
{
    /// <summary>
    /// アセンブリの公開APIサーフェス（public/protected型・メンバー）をリフレクションで列挙し、
    /// 決定的な順序のテキストに変換する。承認テスト（<see cref="PublicApiApprovalTests"/>）専用のユーティリティで、
    /// プロダクトコードからは参照しない。
    /// </summary>
    internal static class PublicApiSurfaceFormatter
    {
        // NonPublic を含めているのは protected / protected internal メンバー（継承先から見た公開契約）を
        // 拾うため。実際に承認対象へ含めるかどうかは IsSurfaceVisible で絞り込む。
        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// 暗黙的な基底型（型ヘッダに表示する意味がないもの）。
        /// </summary>
        private static readonly HashSet<Type> ImplicitBaseTypes = new HashSet<Type>
        {
            typeof(object), typeof(ValueType), typeof(Enum), typeof(MulticastDelegate)
        };

        /// <summary>
        /// assembly 内の外部から可視な public 型（ネストされた public 型を含む）を型名昇順で列挙し、
        /// 型ごとに公開メンバーをテキスト化する。改行は "\n" 固定（git の改行コード変換設定に影響されないよう、
        /// 比較側でも同様に正規化すること）。
        /// </summary>
        public static string Format(Assembly assembly)
        {
            var sb = new StringBuilder();

            var types = assembly.GetTypes()
                .Where(t => t.IsVisible)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

            foreach (var type in types)
            {
                sb.Append(FormatType(type));
                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// 型1つ分のサーフェス（ヘッダ+メンバー）をテキスト化する。テストコードから直接呼び出して
        /// フォーマット挙動を単体検証できるよう internal 公開している（プロダクトからは参照しない）。
        /// </summary>
        internal static string FormatType(Type type)
        {
            var sb = new StringBuilder();
            sb.Append(FormatTypeHeader(type)).Append('\n');

            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type).OrderBy(n => n, StringComparer.Ordinal))
                {
                    var rawValue = Convert.ChangeType(Enum.Parse(type, name), Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
                    sb.Append("  EnumMember ").Append(name).Append(" = ")
                        .Append(Convert.ToString(rawValue, CultureInfo.InvariantCulture)).Append('\n');
                }

                return sb.ToString();
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                // シグネチャは型ヘッダに含めているため、Invoke/BeginInvoke/EndInvoke 等の
                // コンパイラ生成メンバーは別途列挙しない。
                return sb.ToString();
            }

            foreach (var line in EnumerateMemberLines(type))
                sb.Append("  ").Append(line).Append('\n');

            return sb.ToString();
        }

        private static IEnumerable<string> EnumerateMemberLines(Type type)
        {
            var properties = type.GetProperties(MemberFlags);
            var events = type.GetEvents(MemberFlags);

            // プロパティ/イベントのアクセサメソッド（get_/set_/add_/remove_）は、
            // Property/Event側で表現済みのため Method 側の列挙から除外する。
            var accessorMethods = new HashSet<MethodInfo>();
            foreach (var property in properties)
            {
                if (property.GetMethod != null) accessorMethods.Add(property.GetMethod);
                if (property.SetMethod != null) accessorMethods.Add(property.SetMethod);
            }
            foreach (var evt in events)
            {
                if (evt.AddMethod != null) accessorMethods.Add(evt.AddMethod);
                if (evt.RemoveMethod != null) accessorMethods.Add(evt.RemoveMethod);
            }

            var members = new List<(string Kind, string Name, string Line)>();

            foreach (var field in type.GetFields(MemberFlags).Where(f => !f.IsSpecialName && IsSurfaceVisible(f)))
                members.Add(("Field", field.Name, FormatField(field)));

            foreach (var property in properties)
            {
                var line = FormatProperty(property);
                if (line != null) members.Add(("Property", property.Name, line));
            }

            foreach (var evt in events)
            {
                if (!IsSurfaceVisible(evt.AddMethod)) continue;
                members.Add(("Event", evt.Name, FormatEvent(evt)));
            }

            foreach (var ctor in type.GetConstructors(MemberFlags).Where(c => !c.IsStatic && IsSurfaceVisible(c)))
                members.Add(("Constructor", ".ctor", FormatConstructor(ctor)));

            foreach (var method in type.GetMethods(MemberFlags))
            {
                if (accessorMethods.Contains(method)) continue;
                // op_* 以外の IsSpecialName メソッド（プロパティ/イベントの取りこぼし）を除外する。
                if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)) continue;
                if (!IsSurfaceVisible(method)) continue;
                members.Add(("Method", method.Name, FormatMethod(method)));
            }

            return members
                .OrderBy(m => m.Kind, StringComparer.Ordinal)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ThenBy(m => m.Line, StringComparer.Ordinal)
                .Select(m => m.Line);
        }

        private static string FormatTypeHeader(Type type)
        {
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                var invoke = type.GetMethod("Invoke");
                var invokeParameters = string.Join(", ", invoke.GetParameters().Select(FormatParameter));
                return $"delegate {FormatTypeName(invoke.ReturnType)} {FormatTypeName(type)}({invokeParameters})";
            }

            var kind = GetTypeKind(type);
            var modifier = string.Empty;
            if (kind == "class")
            {
                if (type.IsAbstract && type.IsSealed) modifier = "static ";
                else if (type.IsAbstract) modifier = "abstract ";
                else if (type.IsSealed) modifier = "sealed ";
            }

            var header = $"{modifier}{kind} {FormatTypeName(type)}";
            var contract = FormatBaseTypeAndInterfaces(type);
            return contract.Length > 0 ? header + " : " + contract : header;
        }

        /// <summary>
        /// 基底型（object/ValueType/Enum/MulticastDelegate は暗黙的なので省略）と、
        /// 基底型が既に実装しているものを除いた自身のインターフェース（Ordinal昇順）を列挙する。
        /// interface の除去や基底クラスの変更のような契約変更を承認テストで検知するための情報。
        /// </summary>
        private static string FormatBaseTypeAndInterfaces(Type type)
        {
            var parts = new List<string>();

            if (!type.IsInterface && !type.IsEnum)
            {
                var baseType = type.BaseType;
                if (baseType != null && !ImplicitBaseTypes.Contains(baseType))
                    parts.Add(FormatTypeName(baseType));
            }

            // GetInterfaces() は基底型経由で継承したものも含めて返す（順序も非保証）ため、
            // 基底型自身が実装済みのインターフェースを差し引いてから Ordinal ソートする。
            var baseInterfaces = type.BaseType != null
                ? new HashSet<Type>(type.BaseType.GetInterfaces())
                : new HashSet<Type>();

            var ownInterfaces = type.GetInterfaces()
                .Where(i => !baseInterfaces.Contains(i))
                .Select(FormatTypeName)
                .OrderBy(n => n, StringComparer.Ordinal);

            parts.AddRange(ownInterfaces);

            return string.Join(", ", parts);
        }

        private static string GetTypeKind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (type.IsValueType) return "struct";
            return "class";
        }

        /// <summary>public/protected/protected internal の判定。private・internal(assembly専用)・
        /// private protected は公開APIサーフェスの対象外として除外する。</summary>
        private static bool IsSurfaceVisible(MethodBase method)
        {
            return method != null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);
        }

        private static bool IsSurfaceVisible(FieldInfo field)
        {
            return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
        }

        private static string AccessibilityPrefix(MethodBase method)
        {
            if (method == null) return string.Empty;
            if (method.IsFamily) return "protected ";
            if (method.IsFamilyOrAssembly) return "protected internal ";
            return string.Empty;
        }

        private static string AccessibilityPrefix(FieldInfo field)
        {
            if (field.IsFamily) return "protected ";
            if (field.IsFamilyOrAssembly) return "protected internal ";
            return string.Empty;
        }

        private static string FormatField(FieldInfo field)
        {
            var modifiers = new List<string>();
            var accessibility = AccessibilityPrefix(field).Trim();
            if (accessibility.Length > 0) modifiers.Add(accessibility);
            if (field.IsStatic) modifiers.Add("static");
            if (field.IsLiteral) modifiers.Add("const");
            else if (field.IsInitOnly) modifiers.Add("readonly");
            var modifierText = modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : string.Empty;

            var valueSuffix = field.IsLiteral
                ? $" = {FormatDefaultValue(field.GetRawConstantValue(), field.FieldType)}"
                : string.Empty;

            return $"Field {modifierText}{FormatTypeName(field.FieldType)} {field.Name}{valueSuffix}";
        }

        /// <summary>
        /// プロパティ1行を生成する。get/set いずれのアクセサも公開サーフェス対象外（private等）の場合は
        /// null を返し、呼び出し側で除外させる。アクセサごとにアクセシビリティが異なりうる
        /// （例: public get; protected set;）ため、修飾はアクセサ単位で付与する。
        /// </summary>
        private static string FormatProperty(PropertyInfo property)
        {
            var getter = property.GetMethod;
            var setter = property.SetMethod;

            var accessors = new List<string>();
            if (IsSurfaceVisible(getter)) accessors.Add(AccessibilityPrefix(getter) + "get;");
            if (IsSurfaceVisible(setter)) accessors.Add(AccessibilityPrefix(setter) + "set;");

            if (accessors.Count == 0) return null;

            // static/abstract/virtual修飾の代表として、公開サーフェス上のアクセサを優先的に採用する
            // （継承先でのオーバーライド可否は公開APIの契約として意味を持つため、Methodと同様に表現する）。
            var accessor = IsSurfaceVisible(getter) ? getter : setter;
            var modifier = FormatAccessorModifier(accessor);

            var indexParameters = property.GetIndexParameters();
            var indexerSuffix = indexParameters.Length > 0
                ? "[" + string.Join(", ", indexParameters.Select(FormatParameter)) + "]"
                : string.Empty;

            return $"Property {modifier}{FormatTypeName(property.PropertyType)} {property.Name}{indexerSuffix} {{ {string.Join(" ", accessors)} }}";
        }

        private static string FormatEvent(EventInfo evt)
        {
            var accessor = evt.AddMethod;
            var isStatic = accessor != null && accessor.IsStatic;
            var modifier = AccessibilityPrefix(accessor) + (isStatic ? "static " : string.Empty);
            return $"Event {modifier}{FormatTypeName(evt.EventHandlerType)} {evt.Name}";
        }

        private static string FormatConstructor(ConstructorInfo ctor)
        {
            var parameters = string.Join(", ", ctor.GetParameters().Select(FormatParameter));
            return $"Constructor {AccessibilityPrefix(ctor)}({parameters})";
        }

        private static string FormatMethod(MethodInfo method)
        {
            var modifierText = AccessibilityPrefix(method) + FormatAccessorModifier(method);

            var genericSuffix = method.IsGenericMethodDefinition
                ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
                : string.Empty;

            var parameters = string.Join(", ", method.GetParameters().Select(FormatParameter));

            return $"Method {modifierText}{FormatTypeName(method.ReturnType)} {method.Name}{genericSuffix}({parameters})";
        }

        /// <summary>
        /// メソッド（またはプロパティ/イベントのアクセサ）から static/abstract/virtual 修飾語を判定する。
        /// sealed override（IsFinal=true）は virtual として表示しない（呼び出し側からは通常のメンバーと同じ契約のため）。
        /// アクセシビリティ（protected等）は <see cref="AccessibilityPrefix(MethodBase)"/> 側で別途付与する。
        /// </summary>
        private static string FormatAccessorModifier(MethodInfo accessor)
        {
            if (accessor == null) return string.Empty;

            if (accessor.IsStatic) return "static ";
            if (accessor.IsAbstract) return "abstract ";
            if (accessor.IsVirtual && !accessor.IsFinal) return "virtual ";
            return string.Empty;
        }

        private static string FormatParameter(ParameterInfo parameter)
        {
            var modifier = string.Empty;
            if (parameter.ParameterType.IsByRef)
            {
                modifier = parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
            }
            else if (parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
            {
                modifier = "params ";
            }

            var defaultSuffix = parameter.HasDefaultValue
                ? $" = {FormatDefaultValue(parameter.DefaultValue, parameter.ParameterType)}"
                : string.Empty;

            return $"{modifier}{FormatTypeName(parameter.ParameterType)} {parameter.Name}{defaultSuffix}";
        }

        /// <summary>
        /// 既定値をテキスト化する。enum型の既定値は、ランタイムによってboxingされた表現
        /// （enum本体 or 下位の整数型）が一定しないリフレクションの既知の癖があるため、
        /// declaredType を使ってenum名に正規化する。
        /// </summary>
        private static string FormatDefaultValue(object value, Type declaredType)
        {
            if (value == null) return "null";

            if (declaredType != null && declaredType.IsEnum)
                return Enum.GetName(declaredType, value) ?? value.ToString();

            switch (value)
            {
                case string s:
                    return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case bool b:
                    return b ? "true" : "false";
                case float f:
                    return f.ToString(CultureInfo.InvariantCulture) + "f";
                case double d:
                    return d.ToString(CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static string FormatTypeName(Type type)
        {
            if (type.IsByRef) return FormatTypeName(type.GetElementType());
            if (type.IsArray) return FormatTypeName(type.GetElementType()) + "[]";
            if (type.IsGenericParameter) return type.Name;

            if (type.IsGenericType)
            {
                var backtickIndex = type.Name.IndexOf('`');
                var simpleName = backtickIndex >= 0 ? type.Name.Substring(0, backtickIndex) : type.Name;
                var allArgs = type.GetGenericArguments();

                if (type.IsNested)
                {
                    // ネストされたジェネリック型は、CLR上は外側の型の型引数を自身の型引数の先頭に
                    // 引き継ぐ（例: Outer<T>.Inner の Inner は実質 Inner<T> 相当）。
                    // 表示上は "Outer<...>+Inner<own args>" の形に組み立て直し、外側の型名を落とさない。
                    var outerArity = type.DeclaringType.IsGenericType
                        ? type.DeclaringType.GetGenericArguments().Length
                        : 0;
                    var outerArgs = allArgs.Take(outerArity).ToArray();
                    var ownArgs = allArgs.Skip(outerArity).ToArray();

                    var outerName = outerArity > 0
                        ? FormatTypeName(type.DeclaringType.MakeGenericType(outerArgs))
                        : FormatTypeName(type.DeclaringType);

                    var ownSuffix = ownArgs.Length > 0
                        ? "<" + string.Join(", ", ownArgs.Select(FormatTypeName)) + ">"
                        : string.Empty;

                    return $"{outerName}+{simpleName}{ownSuffix}";
                }

                var ns = type.Namespace != null ? type.Namespace + "." : string.Empty;
                var args = string.Join(", ", allArgs.Select(FormatTypeName));
                return $"{ns}{simpleName}<{args}>";
            }

            return type.FullName ?? type.Name;
        }
    }
}

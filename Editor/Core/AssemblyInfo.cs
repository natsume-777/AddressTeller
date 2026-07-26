using System.Runtime.CompilerServices;

// Core の internal 評価エンジン（RuleEvaluator/AddressResolution/AddressRuleBuilderImpl 等）は
// Application/EntryPoints 層および EditMode テストから直接参照する運用のため、
// asmdef 分離後も internal のまま可視化する。
[assembly: InternalsVisibleTo("AddressTeller.Editor")]
[assembly: InternalsVisibleTo("AddressTeller.Editor.Tests")]

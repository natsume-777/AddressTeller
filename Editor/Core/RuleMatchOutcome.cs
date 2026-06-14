namespace AddressTeller
{
    /// <summary>
    /// 1ルール×1アセットの評価結果。
    /// </summary>
    internal enum RuleMatchOutcome
    {
        /// <summary>Predicate が true で、エラーなく評価できた。</summary>
        Matched,

        /// <summary>Predicate が false だった。</summary>
        NotMatched,

        /// <summary>Predicate / AddressSelector / LabelSelector の評価中に例外が発生した。</summary>
        Errored,
    }
}

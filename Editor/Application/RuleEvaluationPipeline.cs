using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressTeller.Editor
{
    /// <summary>
    /// ルール評価のループに入る前に1回だけ構築すればよいセットアップ情報。
    /// </summary>
    internal readonly struct EvaluationSetup
    {
        public IReadOnlyList<AddressRuleEntry> Entries { get; }
        public string ConfigFolder { get; }

        /// <summary>
        /// いずれかのルールが Address() を宣言しているグループ名の集合。AddressTeller が「所有」し、
        /// 削除（stale cleanup / 無効パスの掃除 / 削除追従）の対象にしてよいグループはこれだけ。
        /// Group() だけ宣言して Address() を呼んでいないエントリや、AnyGroup() 由来のラベル専用エントリは
        /// 含めない（ラベルの加算はグループの所有権と無関係であり、この集合を参照しない）。
        /// </summary>
        public HashSet<string> OwnedGroups { get; }

        public HashSet<string> ExistingGroupNames { get; }

        /// <summary>
        /// true の場合、Apply は未存在グループを DefaultGroup のスキーマ構成を複製して自動作成し、
        /// Validate/Predict は作成せず <see cref="ValidationStatus.GroupWillBeCreated"/> として提示する。
        /// <see cref="AddressTellerSettings.AutoCreateMissingGroups"/> を全エントリポイントに一貫供給するためのフィールド。
        /// </summary>
        public bool AutoCreateMissingGroups { get; }

        /// <summary>
        /// true の場合、GroupDefault() を使うルールが存在するが AddressableAssetSettings.DefaultGroup が
        /// 取得できなかった。該当エントリのグループ名はセンチネルのまま残っており、
        /// AddressTellerApplier.Validate が <see cref="ValidationStatus.DefaultGroupUnavailable"/> を返す。
        /// </summary>
        public bool DefaultGroupUnavailable { get; }

        /// <summary>
        /// Configure() が例外を送出したルールごとの失敗一覧（<see cref="ValidationStatus.RuleConfigureFailed"/>、
        /// Context は null）。ApplyAll/ValidateAll/BuildPredictedSnapshot 等、issues リストを返す呼び出し側は
        /// これをそのまま結果に含めること。ここに含まれるルールは <see cref="Entries"/> に一切寄与しない。
        /// </summary>
        public IReadOnlyList<ValidationResult> ConfigureFailures { get; }

        /// <summary>
        /// ルールが宣言したフォルダの検査結果（<see cref="ValidationStatus.DeclaredFolderMissing"/> と
        /// <see cref="ValidationStatus.DeclaredFolderCaseMismatch"/>、Context は null）。
        /// <see cref="ConfigureFailures"/> と同様、issues リストを返す呼び出し側はこれをそのまま結果に含めること。
        /// </summary>
        public IReadOnlyList<ValidationResult> DeclaredFolderIssues { get; }

        /// <summary>宣言フォルダが1つでも実在しない（<see cref="ValidationStatus.DeclaredFolderMissing"/> がある）。</summary>
        public bool HasMissingDeclaredFolder { get; }

        /// <summary>
        /// このランで stale クリーンアップ（無マッチ・無効パスエントリの削除と、資産削除への追従）を止めるべきか。
        /// Configure() の失敗（所有グループが信頼できない）か、宣言フォルダの欠落（そのルールが何にもマッチ
        /// しないだけで、エントリが「無マッチ」に見えている可能性がある）のどちらかがあれば true。
        /// Apply/Predict/RemoveEntryForDeletedAsset の <c>suspendStaleCleanup</c> 引数にはこの値を渡す。
        /// </summary>
        public bool SuspendsStaleCleanup => ConfigureFailures.Count > 0 || HasMissingDeclaredFolder;

        public EvaluationSetup(IReadOnlyList<AddressRuleEntry> entries, string configFolder, HashSet<string> ownedGroups, HashSet<string> existingGroupNames, bool autoCreateMissingGroups, bool defaultGroupUnavailable = false, IReadOnlyList<ValidationResult> configureFailures = null, IReadOnlyList<ValidationResult> declaredFolderIssues = null)
        {
            Entries = entries;
            ConfigFolder = configFolder;
            OwnedGroups = ownedGroups;
            ExistingGroupNames = existingGroupNames;
            AutoCreateMissingGroups = autoCreateMissingGroups;
            DefaultGroupUnavailable = defaultGroupUnavailable;
            ConfigureFailures = configureFailures ?? Array.Empty<ValidationResult>();
            DeclaredFolderIssues = declaredFolderIssues ?? Array.Empty<ValidationResult>();
            HasMissingDeclaredFolder = DeclaredFolderIssues.Any(i => i.Status == ValidationStatus.DeclaredFolderMissing);
        }
    }

    /// <summary>
    /// <see cref="RuleEvaluationPipeline.BuildPredictedRunState"/> の戻り値。ValidateAll と
    /// BuildPredictedSnapshot が「Apply を実行した場合の予測される最終状態」として全く同じ集合を見るように、
    /// 両者が共有する読み取り専用の計算結果をまとめたもの。
    /// </summary>
    internal readonly struct PredictedRunState
    {
        /// <summary>実行前の状態（AddressTellerSnapshotService.Capture の結果）。</summary>
        public AddressTellerSnapshot Before { get; }

        /// <summary>
        /// guid → 予測される最終状態のエントリ。Before から始まり、無効パスエントリの除去・
        /// 各アセットの予測（AddOrUpdate/Remove/NoOp）を反映済み。呼び出し側が変更しても影響がないよう、
        /// 呼び出しごとに新しい Dictionary を返す。
        /// </summary>
        public Dictionary<string, SnapshotEntry> AfterMap { get; }

        /// <summary>このランで AddressTeller が実際にアドレスを書く（書く予定の）guid の集合。</summary>
        public HashSet<string> WrittenGuids { get; }

        /// <summary>ConfigureFailures・ルール例外・各アセットの非 IsOk（GroupWillBeCreated 含む）結果。</summary>
        public List<ValidationResult> Issues { get; }

        /// <summary>AutoCreateMissingGroups 有効時に、このランで作成が必要と判定されたグループ名の集合。</summary>
        public HashSet<string> GroupsToCreate { get; }

        /// <summary>progress がキャンセルを返し、走査を打ち切った場合は true。</summary>
        public bool Cancelled { get; }

        public PredictedRunState(
            AddressTellerSnapshot before,
            Dictionary<string, SnapshotEntry> afterMap,
            HashSet<string> writtenGuids,
            List<ValidationResult> issues,
            HashSet<string> groupsToCreate,
            bool cancelled)
        {
            Before = before;
            AfterMap = afterMap;
            WrittenGuids = writtenGuids;
            Issues = issues;
            GroupsToCreate = groupsToCreate;
            Cancelled = cancelled;
        }
    }

    /// <summary>
    /// ApplyAll/ValidateAll/BuildPredictedSnapshot が共有する前処理（セットアップ構築・コンテキスト生成・
    /// ルール例外の集約）をまとめたパイプライン。
    /// </summary>
    internal static class RuleEvaluationPipeline
    {
        /// <summary>
        /// ループ外で1回だけ構築するセットアップ情報をまとめて返す。
        /// GroupDefault() を使うルールがある場合のみ settings.DefaultGroup を1回取得し、
        /// そのグループ名へ正規化する（センチネル文字列はここで解消する。以降は実名のみを扱う）。
        /// DefaultGroup が取得できない場合は該当エントリのグループ名をセンチネルのまま残し、
        /// <see cref="EvaluationSetup.DefaultGroupUnavailable"/> を true にする
        /// （AddressTellerApplier.Validate がセンチネルを検出して <see cref="ValidationStatus.DefaultGroupUnavailable"/> を返す）。
        /// 同様に、ルールが宣言したフォルダ（GUID 宣言の解決を含む）もここで1回だけ検査し、結果を
        /// <see cref="EvaluationSetup.DeclaredFolderIssues"/> に載せる。
        /// </summary>
        /// <param name="useDeclaredFolderCache">
        /// true のとき、宣言フォルダの検査結果のうちキャッシュ済みのものを再利用する（増分適用用。
        /// <see cref="DeclaredFolderInspector"/> 参照）。フルスキャン系の呼び出しは false のまま常に検査し直す。
        /// </param>
        public static EvaluationSetup BuildSetup(AddressableAssetSettings settings, IReadOnlyList<AddressRuleBase> rules, bool useDeclaredFolderCache = false)
        {
            var entries = GetOrderedEntries(rules, out var configureFailures);
            // settings.ConfigFolder は Windows では "Assets\AddressableAssetsData" のようにバックスラッシュ
            // 区切りで返ることがある（Addressables 本体が OS のパス区切りを使って構築しているため）。
            // AddressTeller のパスは常にフォワードスラッシュに正規化されている前提（AssetContext/AssetFilter 参照）なので、
            // ConfigFolder を使う全箇所（AssetFilter の除外判定、Postprocessor の ShouldSkip、無効パス掃除）に
            // 一貫して効かせるため、取得箇所であるここで1回だけ正規化する。
            var configFolder = settings.ConfigFolder?.Replace('\\', '/');
            var defaultGroupUnavailable = false;

            // センチネルを使うルールが1件もなければ DefaultGroup を取得しない（不要な Addressables アクセスを避ける）。
            if (entries.Any(e => e.GroupName == AddressRuleBuilderImpl.DefaultGroupSentinel))
            {
                // settings.DefaultGroup は通常 null を返さず未設定時は自動作成するが、
                // その自動作成自体が失敗する異常系（CreateGroup の例外等）に備えて try/catch する。
                AddressableAssetGroup defaultGroup = null;
                try
                {
                    defaultGroup = settings.DefaultGroup;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AddressTeller] Failed to retrieve AddressableAssetSettings.DefaultGroup: {ex.Message}. Rules using GroupDefault() will be skipped.");
                }

                if (defaultGroup != null)
                {
                    entries = entries
                        .Select(e => e.GroupName == AddressRuleBuilderImpl.DefaultGroupSentinel
                            ? e.WithGroupName(defaultGroup.Name)
                            : e)
                        .ToArray();
                }
                else
                {
                    defaultGroupUnavailable = true;
                    Debug.LogWarning("[AddressTeller] AddressableAssetSettings.DefaultGroup could not be retrieved. Rules using GroupDefault() will be skipped.");
                }
            }

            // GUID で宣言されたフォルダを現在のパスへ解決し（引けなければ DeclaredFolderMissing）、
            // そのうえで宣言された全フォルダの実在・大小文字を検査する。
            var declaredFolderIssues = new List<ValidationResult>();
            entries = DeclaredFolderInspector.ResolveGuids(entries, declaredFolderIssues);
            declaredFolderIssues.AddRange(DeclaredFolderInspector.Inspect(entries, useDeclaredFolderCache));

            // 削除の権限が発生するのは Address() を宣言したグループだけ（AnyGroup() 由来の GroupName=null、
            // 未解決のセンチネル、Group() だけでまだ Address() を呼んでいないエントリは除外する）。
            // ラベルの加算はこの集合を参照しないため、Group() だけ宣言した時点では削除対象にもラベル加算不可にもならない。
            var ownedGroups = new HashSet<string>(entries
                .Where(e => e.GroupName != null && e.GroupName != AddressRuleBuilderImpl.DefaultGroupSentinel && e.AddressSelector != null)
                .Select(e => e.GroupName));
            // settings.groups の null 要素を除外する（Capture の if (group == null) continue; と対称にする）。
            var existingGroupNames = new HashSet<string>(settings.groups.Where(g => g != null).Select(g => g.Name));
            return new EvaluationSetup(entries, configFolder, ownedGroups, existingGroupNames, AddressTellerSettings.AutoCreateMissingGroups, defaultGroupUnavailable, configureFailures, declaredFolderIssues);
        }

        /// <summary>
        /// Predict ベースで「Apply を実行した場合の予測される最終状態」を1回だけ計算する、ValidateAll と
        /// BuildPredictedSnapshot 共有のコア処理。書き込みは一切行わない。
        /// 両者が同じ計算をそれぞれ再実装すると分岐が二重管理になるため（不変条件が2箇所で食い違う温床）、
        /// ここに集約する。個別の非重複系 issue（GroupNotFound 等）は Validate/Predict の結果そのものなので
        /// 両呼び出し元で従来通り同一だが、重複アドレス検出（DuplicateAddressDetector）の入力となる
        /// AfterMap/WrittenGuids も両者で完全に同じ計算過程（stale 削除・無効パス掃除を反映済み）から得られる。
        /// </summary>
        public static PredictedRunState BuildPredictedRunState(
            AddressableAssetSettings settings,
            IEnumerable<string> paths,
            IReadOnlyList<AddressRuleBase> rules,
            IProgressReporter progress = null)
        {
            progress ??= NullProgressReporter.Instance;

            var before = AddressTellerSnapshotService.Capture(settings);

            // 同一 guid が2つ以上のグループにまたがって存在する状態では、この直後の ToDictionary（guid をキーにする）
            // が例外を出すだけでなく、そもそもどちらのエントリを「そのアセットのエントリ」として扱うべきかが
            // 定義できない（FindAssetEntry は先勝ちで片方を無言で選ぶ）。評価そのものを始める前に検出し、
            // このランでは何も予測・書き込みせず重複の報告だけを返す（ValidationStatus.DuplicateAssetEntry の XML doc 参照）。
            var duplicateAssetEntries = DuplicateAssetEntryDetector.Detect(settings);
            if (duplicateAssetEntries.Count > 0)
            {
                return new PredictedRunState(
                    new AddressTellerSnapshot(),
                    new Dictionary<string, SnapshotEntry>(),
                    new HashSet<string>(),
                    new List<ValidationResult>(duplicateAssetEntries),
                    new HashSet<string>(),
                    false);
            }

            var afterMap = before.Entries.ToDictionary(e => e.Guid);

            var setup = BuildSetup(settings, rules);
            // ApplyAll と同じく、Configure() の失敗と宣言フォルダの欠落のどちらでも stale クリーンアップを止める
            // （以降の suspendStaleCleanup はこの両方を含む。EvaluationSetup.SuspendsStaleCleanup 参照）。
            var suspendStaleCleanup = setup.SuspendsStaleCleanup;

            var issues = new List<ValidationResult>(setup.ConfigureFailures);
            issues.AddRange(setup.DeclaredFolderIssues);

            // ApplyAll と同じ理由・同じ条件（Configure() 失敗・宣言フォルダ欠落時は停止）で、所有グループ内の
            // 無効パスエントリを洗い出す。paths には依存させない（旧バージョンの残骸は paths に含まれるとは限らないため）。
            if (!suspendStaleCleanup)
            {
                var invalidEntries = AddressTellerApplier.FindInvalidPathManagedEntries(settings, setup.OwnedGroups, setup.ConfigFolder);
                if (AddressTellerSettings.CleanupStaleEntries)
                {
                    foreach (var invalidEntry in invalidEntries)
                        afterMap.Remove(invalidEntry.guid);
                }
                else
                {
                    // CleanupStaleEntries が OFF なので削除は予測しないが、ON なら削除される対象だったことを
                    // 通知する（UnmatchedEntryKept は IsOk=true の通知専用ステータス）。「パスが構造的に無効」
                    // というのは Addressables のエントリとして無効というだけで、AssetDatabase 上のアセットと
                    // しては大抵解決できる（拡張子・Editor フォルダ名などによる除外がほとんどのため）。
                    // JSON/JUnit レポートの Path 列を空にしないよう、BuildContext できる場合は Context を
                    // 埋める。GUID からパスが引けない等で構築できない場合のみ Context は null のままにする
                    // （RuleConfigureFailed/DuplicateAddress と同じ扱い。ValidationStatus.UnmatchedEntryKept の
                    // XML doc 参照）。
                    foreach (var invalidEntry in invalidEntries)
                    {
                        var invalidContext = BuildContext(invalidEntry.AssetPath);
                        issues.Add(new ValidationResult(
                            invalidContext,
                            ValidationStatus.UnmatchedEntryKept,
                            $"Entry guid={invalidEntry.guid} in group '{invalidEntry.parentGroup?.Name}' (address='{invalidEntry.address}', path='{invalidEntry.AssetPath}') has a path that is not valid for an Addressables entry, and would be removed if Remove unmatched entries were on."));
                    }
                }
            }

            var groupsToCreate = new HashSet<string>();
            var writtenGuids = new HashSet<string>();

            var pathList = paths as IList<string> ?? paths.ToList();
            var total = pathList.Count;
            var cancelled = false;

            for (var i = 0; i < total; i++)
            {
                var path = pathList[i];

                if (!progress.Report(i, total, path))
                {
                    cancelled = true;
                    break;
                }

                if (AssetFilter.ShouldExcludeByPath(path, setup.ConfigFolder)) continue;

                var ctx = BuildContext(path, out var typeUnresolved);
                if (ctx == null)
                {
                    if (typeUnresolved != null) issues.Add(typeUnresolved);
                    continue;
                }
                if (AssetFilter.ShouldExclude(ctx, setup.ConfigFolder)) continue;

                var resolution = RuleEvaluator.Evaluate(ctx, setup.Entries);
                AddRuleErrors(ctx, resolution, issues);

                var prediction = AddressTellerApplier.Predict(ctx, resolution, settings, setup.ExistingGroupNames, setup.OwnedGroups, setup.AutoCreateMissingGroups, suspendStaleCleanup);

                switch (prediction.Action)
                {
                    case PredictedAction.AddOrUpdate:
                        afterMap[ctx.Guid] = prediction.PredictedEntry;
                        break;
                    case PredictedAction.Remove:
                        afterMap.Remove(ctx.Guid);
                        break;
                    case PredictedAction.NoOp:
                        break;
                }

                if (prediction.Validation.Status == ValidationStatus.GroupWillBeCreated)
                    groupsToCreate.Add(prediction.PredictedEntry.GroupName);

                // Ok/GroupWillBeCreated は単独勝者の候補が確定し、そのアドレスが書き込まれる予定であることを意味する
                // （LabelsOnly は既存アドレスを維持するだけなので対象外。AddressTellerApplier.Predict 参照）。
                if (prediction.Validation.Status == ValidationStatus.Ok || prediction.Validation.Status == ValidationStatus.GroupWillBeCreated)
                    writtenGuids.Add(ctx.Guid);

                // GroupWillBeCreated と UnmatchedEntryKept はどちらも IsOk=true（情報提供）だが、ValidateAll の
                // 従来契約（アセットごとの通知を issues に含める）を保つため、ここでは !IsOk と同列に issues へ積む。
                // BuildPredictedSnapshot 側は GroupsToCreate（グループ名の集合）で GroupWillBeCreated と同じ情報を
                // 提供する契約のため、呼び出し側（BuildPredictedSnapshot）で GroupWillBeCreated だけを Issues から
                // 除外する（AddressTellerSnapshotService.BuildPredictedSnapshot 参照）。UnmatchedEntryKept には
                // そのような別経路が無いため、BuildPredictedSnapshot 側でも issues にそのまま残る。
                if (!prediction.Validation.IsOk
                    || prediction.Validation.Status == ValidationStatus.GroupWillBeCreated
                    || prediction.Validation.Status == ValidationStatus.UnmatchedEntryKept)
                    issues.Add(prediction.Validation);

                // RemoveStaleManagedLabels が OFF のとき、ON なら外されるラベルがあることの通知（IsOk=true）。
                // Validation（Apply の結果そのもの）とは別に、同じ資産について追加で報告する。
                if (prediction.Notice != null)
                    issues.Add(prediction.Notice);
            }

            // キャンセル時は完了通知を呼ばない（中断したのに「完了」を通知すると意味的に矛盾するため）。
            if (!cancelled)
                progress.Report(total, total, string.Empty);

            return new PredictedRunState(before, afterMap, writtenGuids, issues, groupsToCreate, cancelled);
        }

        /// <summary>
        /// 同一 Order のルールクラスが複数あれば警告を出す。Order はアドレスの優先順位でもあるため、
        /// 同じ Order を持つ2つのルールが同一アセットに対してアドレスを返すと、どちらを採用すべきか
        /// 決める根拠が無く競合になる。
        /// Project Settings で無効化中のルールも含む全ルールが対象（無効化しても警告は消えない）。
        /// </summary>
        public static void WarnOnDuplicateOrders(IReadOnlyList<AddressRuleBase> rules)
        {
            foreach (var group in RuleCollector.FindDuplicateOrders(rules))
            {
                var names = string.Join(", ", group.Select(r => r.GetType().Name));
                Debug.LogWarning($"[AddressTeller] Multiple rule classes share Order={group.Key} (all rules including disabled ones are considered): {names}. If two of these match the same asset and both produce an address, it will be reported as a conflict.");
            }
        }

        /// <summary>パスから AssetContext を構築する。</summary>
        public static AssetContext BuildContext(string path)
            => BuildContext(path, out _);

        /// <summary>
        /// パスから AssetContext を構築する。null を返す理由が「型が解決できなかった」場合に限り、
        /// <paramref name="typeUnresolved"/> に <see cref="ValidationStatus.AssetTypeUnresolved"/> の通知を返す。
        /// GUID が空（アセットとして存在しないパス）の場合は従来どおり null のみで、通知は返さない。
        /// </summary>
        public static AssetContext BuildContext(string path, out ValidationResult typeUnresolved)
            => BuildContext(path, p => AssetDatabase.AssetPathToGUID(p), p => AssetDatabase.GetMainAssetTypeAtPath(p), p => AssetDatabase.IsValidFolder(p), out typeUnresolved);

        /// <summary>
        /// <see cref="BuildContext(string, out ValidationResult)"/> の本体。AssetDatabase への依存を引数で
        /// 差し替えられるようにしてあるのは、型が解決できない実アセットを用意せずに分岐を単体テストするため。
        /// </summary>
        internal static AssetContext BuildContext(
            string path,
            Func<string, string> guidOf,
            Func<string, Type> typeOf,
            Func<string, bool> isValidFolder,
            out ValidationResult typeUnresolved)
        {
            typeUnresolved = null;
            var guid = guidOf(path);
            if (string.IsNullOrEmpty(guid)) return null;
            var type = typeOf(path);
            if (type == null)
            {
                // GUID はあるのに型が引けないアセット（依存スクリプトの欠落など）。無言で飛ばさず通知する。
                typeUnresolved = new ValidationResult(
                    null,
                    ValidationStatus.AssetTypeUnresolved,
                    $"The type of asset '{path}' could not be resolved (AssetDatabase.GetMainAssetTypeAtPath returned null), so no rule was evaluated for it. A script the asset depends on may be missing.");
                return null;
            }
            // フォルダのメインアセット型は必ず DefaultAsset なので、その場合だけ IsValidFolder を呼ぶ。
            // プロジェクト全アセットのループから毎回 AssetDatabase を叩かないための絞り込みで、
            // DefaultAsset でないパスがフォルダになることはないため取りこぼしは生じない。
            var isFolder = type == typeof(DefaultAsset) && isValidFolder(path);
            return new AssetContext(guid, path, type, isFolder);
        }

        /// <summary>ルールの評価エラーを issues へ追加する。</summary>
        public static void AddRuleErrors(AssetContext ctx, AddressResolution resolution, List<ValidationResult> issues)
        {
            foreach (var error in resolution.Errors)
                issues.Add(new ValidationResult(
                    ctx,
                    ValidationStatus.RuleError,
                    $"{error.RuleSource} threw for '{ctx.Path}': {error.Message}"));
        }

        /// <summary>
        /// Configure() を実行してルールエントリ一覧を構築する。Configure() はルールごとに try/catch し、
        /// 例外が発生したルールは他のルールの評価をブロックせずスキップする（該当ルールのエントリは0件）。
        /// 失敗したルールは <see cref="ValidationStatus.RuleConfigureFailed"/> の <see cref="ValidationResult"/>
        /// として <paramref name="configureFailures"/> に毎回まとめる（ClearAll/UndoLastApply 等、issues リストを
        /// 持たない呼び出し側でも失敗が可視化されるようにするため）。コンソールへのエラー出力（スタックトレース込み）も
        /// 呼び出しのたびに毎回行う。同じ失敗が import のたびに繰り返しログされうるが、原因調査の容易さを優先する。
        /// </summary>
        private static IReadOnlyList<AddressRuleEntry> GetOrderedEntries(IEnumerable<AddressRuleBase> rules, out IReadOnlyList<ValidationResult> configureFailures)
        {
            var all = new List<AddressRuleEntry>();
            List<ValidationResult> failures = null;

            foreach (var rule in rules)
            {
                var builder = new AddressRuleBuilderImpl(rule.GetType().Name, rule.Order);
                try
                {
                    rule.Configure(builder);
                }
                catch (Exception ex)
                {
                    var message = $"{rule.GetType().Name}.Configure() threw and will be skipped: {ex.GetType().Name}: {ex.Message}";
                    // ValidationResult.Message は簡潔なままにし、ログ側にのみスタックトレースを含める。
                    // 呼び出しのたびに毎回出す（ログが増える代わりに、原因特定に必要な情報を常に確保する）。
                    Debug.LogError($"[AddressTeller] {message}\n{ex}");
                    failures ??= new List<ValidationResult>();
                    failures.Add(new ValidationResult(null, ValidationStatus.RuleConfigureFailed, message));
                    continue;
                }

                all.AddRange(builder.Entries);
            }

            configureFailures = (IReadOnlyList<ValidationResult>)failures ?? Array.Empty<ValidationResult>();
            return all;
        }
    }
}

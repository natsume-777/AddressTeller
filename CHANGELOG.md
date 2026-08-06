[日本語](./CHANGELOG.ja.md)

# Changelog

This file follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Versioning follows [Semantic Versioning](https://semver.org/).
While the version is `0.x`, breaking changes may land in a minor release; each one is marked **BREAKING** below. From `1.0.0` onward the guarantees in [Compatibility Policy](Documentation~/compatibility.md) apply: breaking changes are limited to major releases and are preceded by at least one release marking the affected API `[Obsolete]`.

## [Unreleased]

### Documentation

- README.md, README.ja.md, Documentation~/writing-rules.md and .ja.md: corrected the asmdef
  reference guidance. A custom assembly that only defines rule classes needs `AddressTeller.Core`
  alone — the entire rule-authoring surface (`AddressRuleBase`, `IAddressRuleBuilder`, `Match`,
  `AssetCondition`, `Naming`, `AssetContext`, `RuleInspector`) lives there, as the
  `Rule Unit Test Helper` sample's own asmdef demonstrates. `AddressTeller.Editor` is required
  only when the assembly also calls the operational APIs (`AddressTellerService`,
  `ValidationResult`, snapshots, reports); referencing it always requires referencing
  `AddressTeller.Core` too, since those signatures expose Core types. The previous text told
  every rule assembly to reference both.
- README.md and README.ja.md: documented pinning the install to a release tag and clarified
  that the [Compatibility Policy](Documentation~/compatibility.md) takes effect only from `1.0.0`
  onward; while in `0.x`, breaking changes may land in minor releases, so tag pinning is strongly
  recommended. Noted that unpinned git URLs track the default branch, and that release tags exist
  only for version 0.4.0 and later.

## [0.4.1] - 2026-08-02

### Documentation

- README.md and README.ja.md: clarified that an asset matched by a rule is moved
  into that rule's group regardless of its current group, while deletions and
  label additions are limited to groups referenced by at least one rule.
- README.md and README.ja.md: added an Addressables primer (address / label /
  group, initializing Addressables) and expanded Quick Start with notes on entry
  creation and moving, managed-group scoping, and auto-apply on import —
  including that auto-apply also deletes entries that no longer match any rule
  (`CleanupStaleEntries`).
- Documentation~/operations.md and .ja.md: added a non-interactive CLI section
  covering the four CLI entry points' exit behavior, a batch-mode example for CI,
  and the fact that `Tools/AddressTeller/Clear All Addresses & Labels...` always
  shows a confirmation dialog with no dry-run gate.
- Documentation~/operations.md and .ja.md: added the previously undocumented
  `Tools/AddressTeller/Preview Group...` and
  `Assets/AddressTeller/Preview (Apply Preview)` entries to the Apply Methods table.
- Documentation~/operations.md and .ja.md: documented the per-rule preview
  ("Validate/Apply this rule only" button in Project Settings), including that it
  can predict `Removed` for entries owned by another rule targeting the same
  group, because only the selected rule is in evaluation scope.
- AddressTellerScopedPreview.cs XML doc comment: removed an inaccurate mention of
  sub-assets from the folder-expansion note on `RunGroupPreview`.

## [0.4.0] - 2026-08-01

### Added

- `ReportFormat` and `DistributionFormat` enums: new public types representing the format choices for report and distribution exports. `ReportFormat` supports `Json` and `Junit` (used by `AddressTellerReportWriter.WriteToFile`), while `DistributionFormat` supports `Csv` and `Markdown` (used by `BundleDistributionSerializer.WriteToFile`).
- `ValidationStatus.RuleConfigureFailed`: returned when a user rule's `Configure()` method throws an exception. The problematic rule is skipped (treated as producing no entries) and evaluation continues; this status helps identify which rule has a configuration problem.
- Warnings displayed in `Undo Last Apply` dialog and `Explain` window when rule configuration errors exist, so users are aware that reported results are incomplete.
- `AddressTellerReport.SchemaVersion`: a new field parallel to `AddressTellerSnapshot.SchemaVersion`, defaulting to 1 and set by `AddressTellerReportBuilder.Build`.
- `AddressTellerReport.CurrentSchemaVersion`: a new public constant (`= 1`) exposing the schema version `AddressTellerReport.SchemaVersion` defaults to and `FromJson` compares against. Previously this value existed only as an internal constant on `AddressTellerReportBuilder`, which meant it wasn't covered by the public API approval baseline; `AddressTellerReportBuilder` now reads this new public constant instead of defining its own.
- `AddressTeller.Testing.RuleInspector`: public API for inspecting rule configuration results without a real Addressables project. Provides `Collect()` to retrieve all rule entries registered by the rule, `IsUnresolvedDefaultGroup()` to check for unresolved `GroupDefault()` references, and `DisplayGroupName()` to format group names for display. Enables comprehensive rule unit tests; replaces the custom Fake builders previously used in `RuleUnitTestHelper` samples.

### Fixed

- `RestoreExactWithRemoval` API: `Undo Last Apply` now correctly deletes entries matching the confirmation dialog count, instead of leaving all entries intact.
- Rule collection no longer stops on constructor exceptions or open generic types; problematic rules are skipped and evaluation continues.
- Settings folder exclusion filter now includes path separators to prevent adjacent folders (e.g., `AddressableAssetsData_Backup`) from being mistakenly excluded.
- Cleanup no longer incorrectly deletes entries for assets matched by label-only rules; added `ValidationStatus.LabelsOnly` to properly track label-only rule matches.
- `CollectManagedGroups` now excludes `null` values and unresolved `GroupDefault()` sentinels from the managed groups list.
- `NullReferenceException` when `ApplyAll` is called with `paths: null`.
- Duplicate warnings and processing for the same group after `AutoCreateMissingGroups`.
- Potential `NullReferenceException` in the result window when `Context` is null.
- Snapshot file overwriting when multiple saves occur within the same second; improved error handling for write failures.
- Snapshot file helper duplication and folder boundary detection.
- `SnapshotFolder` path traversal vulnerability; added range check to restrict paths to the project directory.
- `Apply All` lacking progress bar display and cancellation support; integrated `EditorProgressReporter`.
- Incorrect comment in `Naming` class.
- Unnecessary array allocations in `AssetContext.PathSegments` by caching.
- Label-only rules incorrectly writing labels to unmanaged group entries without ownership check; labels are now restricted to managed groups.
- Automatic safety snapshot save failures were previously ignored; `Apply All` / `Apply with Validate` now properly handle these errors by stopping the operation and notifying the user.
- Snapshot save operation now handles `Directory.CreateDirectory` failures (e.g., invalid path or permission error) gracefully, preventing unhandled exceptions.
- Snapshot restoration now tolerates duplicate group names instead of throwing an exception; duplicate groups are reported as warnings. `AddressTellerSnapshotService.Restore` and `BundleModeReader` have been updated to handle this gracefully.
- `AssetFilter.ShouldExcludeByPath`: added null check before calling `path.Replace()` to prevent `NullReferenceException`.
- Snapshot JSON loading (`LoadFromFile`): extended mandatory field validation to include `GroupName` and `Entries` (in addition to the existing `Guid` check).
- Project Settings UI: Postprocessor order field now displays the effective clamped value (e.g., 0 becomes 1000) when the user leaves the field or presses Enter.
- `ExportDistribution`: now displays an error dialog when file write fails, instead of silently suppressing the error.
- `AddressTellerSnapshotService.Restore`/`RestoreExactWithRemoval`/`Diff` now throw `ArgumentNullException` for missing required arguments instead of an unguarded `NullReferenceException`, matching `AddressTellerClearService.Clear`'s existing contract. The package's null-argument policy (explicit entry points throw; methods with default-value fallback such as `AddressTellerService.*` do not) is now documented in the relevant XML doc comments.
- `AddressTellerSettings.DisabledRuleClassNames` now returns a defensive copy instead of the internal list instance, so mutating the returned collection can no longer affect the persisted setting.

### Changed

- **BREAKING**: Core domain model and evaluation engine (`Editor/Core/` content) are now split into an independent assembly `AddressTeller.Core`. The asmdef name is `AddressTeller.Core`, and the namespace remains `AddressTeller` (unchanged). If your project has a custom asmdef that references `AddressTeller.Editor`, you must also add `AddressTeller.Core` to its `references`; since public APIs in `AddressTeller.Editor` expose Core types (e.g., `ValidationResult.Context` is `AddressTeller.AssetContext`, `AddressTellerService.ApplyAll(..., rules)` accepts `IReadOnlyList<AddressTeller.AddressRuleBase>`), the compiler requires both assemblies in `references`.
- Validation notifications with `IsOk=true` (e.g., `GroupWillBeCreated`) are now logged as `Warning` instead of `Error` across all entry points (Postprocessor, Menu, ApplyFlow). This distinguishes informational status messages from actual errors.
- `AddressTellerMenu.Validate()` now opens the result window (showing Issues tab) when one or more errors (`IsOk=false`) are detected. Previously, results were only logged to the console.
- **BREAKING**: `BundleModeReader.ReadBundleModes()` and `BundleDistributionSummarizer.Build()` now include an `out` parameter for reporting duplicate group name warnings. Callers must accept this new parameter.
- **BREAKING**: `IAddressRuleBuilder.Address()` now throws `InvalidOperationException` when called twice on the same rule, matching `Where()` behavior. Previously, duplicate address assignments were silently overwritten; this change ensures early detection of ambiguous address specification.
- `ApplyAll`, `ValidateAll`, and `BuildPredictedSnapshot` now skip stale-entry cleanup (DeletedAssets tracking) if any rule configuration error is detected, preventing incorrect deletions of entries managed by broken rules.
- `ApplyAll` / `Apply with Validate` menu operations now cancel instead of continuing when automatic safety snapshot save fails, matching the fail-fast design of `ClearAll`.
- `ClearAll` menu and `ClearCLI` command now abort when rule configuration errors exist (CLI exits with code 3).
- **BREAKING**: `AddressTellerReportWriter.WriteToFile` and `BundleDistributionSerializer.WriteToFile` now take `ReportFormat` / `DistributionFormat` enum arguments instead of raw strings (`"json"`/`"junit"` and `"csv"`/`"markdown"`). CLI text arguments (`-addressTellerReportFormat`) are unaffected; only the public API surface changed.
- **BREAKING**: `SnapshotDiff.Added`/`Removed`/`Changed` are now `IReadOnlyList<T>` instead of `List<T>`. Code that mutated these collections directly must be updated.
- **BREAKING**: `SnapshotDiff` and `DryRunResult` parameterless public constructors are now `internal`. These types are instantiated only by the library's snapshot and dry-run APIs; tests continue to construct them via `InternalsVisibleTo`.
- **BREAKING**: `ValidationResult` and `AddressCandidate` public constructors are now `internal`. These types are only ever constructed by the library's own rule evaluation pipeline; tests continue to construct them via `InternalsVisibleTo`.
- **BREAKING**: `AddressTellerPostprocessor` is now `sealed`.
- **BREAKING**: `AddressTellerService.RemoveEntriesForDeletedAssets` now returns `IReadOnlyList<ClearedEntry>` (previously `void`), reporting the entries actually removed. This matches the convention already used by `ApplyAll`/`ValidateAll` of surfacing results instead of discarding them.
- **BREAKING**: `AddressTellerExplainReport`, `AddressTellerExplainAsset`, and `AddressTellerExplainRule` are now `internal` (previously `public`). No supported code path constructs or exposes these types outside the package.
- **BREAKING**: `AddressTellerCliArgs.ReportFormat` is now `ReportFormat?` instead of `string`. Code that read this property as a raw string (`"json"`/`"junit"`) must be updated to compare against the `ReportFormat` enum.
- `RuleUnitTestHelper` sample: `DefaultGroupSentinel` constant removed; `Collect()` now delegates to the package's own `RuleInspector` public API, ensuring the exact same builder contract (calling `Where()`/`Address()` a second time on the same group now throws `InvalidOperationException`). New helper methods `IsUnresolvedDefaultGroup()` and `DisplayGroupName()` added for checking and displaying unresolved default group sentinels in tests.
- **BREAKING**: `LogicalBundleDto` renamed to `BundleDistributionReportEntry`. This is a C# API-only rename; the JSON report output (field names) is unchanged.
- Enum members of `ValidationStatus`, `ClearScope`, `ReportFormat`, `DistributionFormat`, `SnapshotRestoreMode`, and `BundleModeKind` now carry explicit numeric values in source. This doesn't by itself enforce the member-to-number freeze described in [Compatibility Policy](Documentation~/compatibility.md#enums) — nothing prevents a future edit from renumbering — but the public API approval baseline now records each member's name and value, so `PublicApiApprovalTests` catches an accidental rename, removal, or renumbering. No behavior change (the implicit numbering was already sequential from 0).
- **BREAKING**: `ClearScope` enum values swapped: `Managed` is now `0` and `All` is now `1` (previously `All = 0`, `Managed = 1`). This aligns `default(ClearScope)` with the package's "destructive operations default to the safe side" principle. Code that reads `ClearScope` by its underlying numeric value (rather than by member name) must be updated; code that only refers to `ClearScope.All`/`ClearScope.Managed` by name is unaffected. No CLI or menu entry point had a reachable code path that relied on the previous `default(ClearScope)` value.
- **BREAKING**: `AddressTellerReport.FromJson` now rejects a report whose `SchemaVersion` is greater than `AddressTellerReport.CurrentSchemaVersion`, returning `null` and logging a warning instead of returning a report of an unrecognized shape. Callers must now null-check the result. Only this rejection policy mirrors `AddressTellerSnapshotService.LoadFromFile`'s existing rejection of snapshots from a newer schema — unlike `LoadFromFile`, `FromJson` does not validate the JSON's content and does not catch exceptions from the underlying `JsonUtility` call (see [Compatibility Policy](Documentation~/compatibility.md#5-report-output-json--junit-xml) for the precise differences).

### Documentation

- Added English versions of README.md and all Documentation~ files. The original Japanese content is preserved as `.ja.md` files (e.g., `README.ja.md`, `architecture.ja.md`). Each file includes a language switch link at the top.
- Converted CONTRIBUTING.md to English; Japanese version saved as CONTRIBUTING.ja.md.
- Converted CHANGELOG.md to English; Japanese version saved as CHANGELOG.ja.md.
- `writing-rules.md`: added Testing section with guidance on using `RuleInspector` API and the `RuleUnitTestHelper` sample for unit-testing rule classes.
- `operations.md`: added `RuleUnitTestHelper` to the Samples section.
- `AddressTellerSettings.CleanupStaleEntries` XML doc and `design-decisions.md`: corrected misleading text that incorrectly stated "labels are not deleted." Both addresses and labels are removed from stale entries.
- Converted all public API XML documentation comments to English, and documented previously undocumented public members (IntelliSense text is now English).
- Added [Compatibility Policy](Documentation~/compatibility.md), listing the public C# API, CLI entry points/arguments, exit codes, report/snapshot/settings file formats, menu paths, and rule-authoring behavior covered by SemVer guarantees, along with the open-enum contract for `ValidationStatus` and friends.
- `CONTRIBUTING.md`: added a Type Naming section documenting when public types take the `AddressTeller` prefix (entry points and serialized artifact roots only) and the naming exception for the rule-authoring DSL (`Match`, `Naming`, etc.).
- `operations.md`: fixed the `BundleDistribution` JSON section description, which previously showed the key names in the wrong casing (`bundleDistribution`, `totalLogicalBundleCount`, `unknownGroupCount`) — `JsonUtility` does not apply any casing convention, so the actual output uses the C# field names verbatim (`BundleDistribution`, `TotalLogicalBundleCount`, `UnknownGroupCount`). CI parsers written against the old (incorrect) casing should be updated.
- `operations.md`: documented that `PostprocessOrder`'s `0` is a reserved "unset" sentinel — explicitly setting the field to `0` is treated the same as leaving it unset and falls back to `1000`.
- `writing-rules.md`: noted that a rule file also using `System.Text.RegularExpressions` should add `using Match = AddressTeller.Match;` to disambiguate the two `Match` types.
- `operations.md`: documented the two exit-code-3 conditions added for `-addressTellerDisableRules`/`ClearCLI` — an unknown rule class name passed to `-addressTellerDisableRules`, and (for `ClearCLI` with `scope=managed`) a rule configuration error that makes `managedGroups` untrustworthy.
- Added [Compatibility Policy](Documentation~/compatibility.md) documentation of the `BundleDistribution` report section's "always present" semantics: the field is never omitted or JSON `null`; when it could not be calculated (no `DryRunResult.After`, no `AddressableAssetSettings` supplied, or the calculation itself threw), it appears as an all-C#-defaults object (`Bundles: []`, counts at `0`, empty `Disclaimer`) rather than being left out.

### Verified

- Addressables 2.8.1 through 3.1.0 compatibility was confirmed in prior work (353 EditMode tests at that time).
- Current EditMode test suite: 502 pass / 0 fail / 2 skip (504 total). The minimum Addressables requirement remains 2.8.1.

---

## [0.3.0] - 2026-06-14

### Added

- `IAddressRuleBuilder.GroupDefault()`: use instead of `Group("name")` to assign addresses and labels to the Addressables `DefaultGroup`. The group is resolved from `AddressableAssetSettings.DefaultGroup` at evaluation time, so it follows DefaultGroup renames automatically. `Where`/`Address`/`Label` chain the same way as `Group()`.
- `ValidationStatus.DefaultGroupUnavailable`: returned when a `GroupDefault()` rule exists but `AddressableAssetSettings.DefaultGroup` cannot be resolved. The write for the affected asset is skipped (`IsOk = false`).
- Project Settings: added "Postprocessor execution order" setting (`AddressTellerSettings.PostprocessOrder`, default 1000). `AddressTellerPostprocessor.GetPostprocessOrder()` returns this value to control execution order relative to other `AssetPostprocessor`s.
- CLI: added `-addressTellerDisableRules <FullName>[,...]` to `ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI`. The specified names are temporarily excluded (unioned with the persistent Project Settings disabled list) for that CLI run only — intended for excluding debug-only rules in CI. If any specified FullName does not match a known rule class, the command exits with code 3. This exclusion is CLI-only and does not affect the Postprocessor or menu.
- Project Settings: added a collapsible "Managed Groups" list showing the group names referenced by enabled rules — the groups targeted by `CleanupStaleEntries`/`AutoCreateMissingGroups`. The description notes that manually registered entries inside these groups are subject to deletion if no rule matches them.

### Documentation

- Added missing `CheckCLI` entry to the CI integration section of Documentation~/operations.md. Clarified that the exit code table applies to all three CLI methods (`ApplyAllCLI`/`ApplyWithValidateCLI`/`CheckCLI`).
- Fixed incorrect description of `CleanupStaleEntries` (Project Settings UI and operations.md) that stated "labels are not deleted." Entry deletion via `RemoveAssetEntry` removes both the address and Addressables labels.
- Added to `design-decisions.md`/`operations.md`: manually registered entries inside a managed group are also subject to `CleanupStaleEntries` deletion if no rule matches them. Previously only the "unmanaged groups are untouched" guarantee was documented; the reverse was not.

### Changed

- Reordered Project Settings sections to "Apply & Validate behavior → Registered rules → Operations → Snapshots" so the most frequently checked "Registered rules" list is no longer cut off at the bottom of the screen.
- Renamed the "Auto-apply" section heading to "Apply & Validate behavior." `CleanupStaleEntries`/`AutoCreateMissingGroups` affect all entry points (Apply All / Validate / CLI / snapshot dry-run), not just import-time auto-apply, so the old heading was misleading.
- Changed the default scope of `Tools/AddressTeller/Clear All Addresses & Labels...` (menu) and `ClearCLI` (when `-addressTellerClearScope` is not specified) from `All` (all entries) to `Managed` (entries in AddressTeller-managed groups only). Use `-addressTellerClearScope all` to clear all entries.
- **BREAKING**: Changed root namespace from `Natsume777.AddressTeller` to `AddressTeller`. Users must replace `using Natsume777.AddressTeller;` with `using AddressTeller;`. The editor assembly was also renamed from `Natsume777.AddressTeller.Editor` to `AddressTeller.Editor`; update `references` in any asmdef that references it.
- Reorganized `Editor/Application/` and Snapshot-related EntryPoints into feature-based subfolders (`Snapshot/` / `Reporting/` / `Bundle/`). Internal structure only — no public API impact.
- Package name (`com.natsume777.addressteller`) is unchanged.

## [0.2.0] - 2026-06-14

### Added

- Rule enable/disable toggles in the Project Settings rule list. The enabled/disabled state is saved to `ProjectSettings/AddressTellerSettings.asset`. Disabled rules are excluded from `Apply All` / `Validate` / `Apply with Validate` / `Explain` / snapshot dry-run predictions. Note: stale-entry cleanup (ownership determination) always considers all rules regardless of the toggle, so entries previously managed by a disabled rule are still tracked correctly.
- `Match` static class: helpers for building condition predicates. Provides `InFolder(string)` / `OfType<T>()` / `Glob(string)`, composable with `And(AssetCondition)`. Each helper auto-generates a human-readable description (e.g., `"InFolder(Assets/Characters)"`) shown in the Explain window and error messages. `All()` returns an unconditional match for rules without filters.
- `Naming` static class: common address generation patterns. Provides `FileName()` / `FileNameWithoutExtension()` / `ParentFolderName()` / `RelativePath(string root)` for use with `Address()`. Handles path normalization so you don't have to.
- New `AssetContext` properties: `Extension` / `IsInFolder(string)` / `PathSegments` / `RelativePathFrom(string root)`. Useful for concise path analysis in raw `Where()` lambdas.
- Auto safety snapshot: automatically saves the current state to `SnapshotFolder/Auto` immediately before `Apply All` / `Apply with Validate` menu execution, with configurable rotation (default 10 snapshots). `Tools/AddressTeller/Undo Last Apply` restores from the latest auto-snapshot in Exact mode (effectively an undo for `CleanupStaleEntries` deletions etc.). Configurable in Project Settings (default ON). Not applied to CLI (`ApplyAllCLI`/`ApplyWithValidateCLI`).
- `AddressTellerSnapshotService.BuildPredictedSnapshot`: dry-run API that computes the post-apply diff (additions, changes, deletions) and issues (conflicts, missing groups, rule exceptions) without writing anything. Returns `DryRunResult` (`SnapshotDiff` + `IReadOnlyList<ValidationResult>`).
- `IProgressReporter` overloads for `AddressTellerService.ApplyAll` / `ValidateAll`. `EditorProgressReporter` shows a cancelable progress bar via `EditorUtility.DisplayCancelableProgressBar`; cancellation returns results up to the point of cancellation without rollback.
- Apply confirmation dialog for `Tools/AddressTeller/Apply All` / `Apply with Validate` menu: runs `BuildPredictedSnapshot` first and shows "added N / changed N / ⚠ deleted N / issues N" with three options (Apply / Cancel / Show details). Skips the dialog and shows only a log when there are no diffs or issues. "Show details" opens the result window and allows Apply from the same result set. If `Apply with Validate` detects issues at the Validate step, it goes directly to the result window (Issues tab) without the dialog. Not applied to CLI.
- `Tools/AddressTeller/Explain` menu: evaluates all rules against the selected asset in the Project window and displays the results in a window — matched rules (address/labels assigned), unmatched rules (with `Where` description), and rule exceptions. Works best with `Where(predicate, description)` to get readable descriptions.
- `Tools/AddressTeller/Snapshot/Manage Snapshots...` menu: unified window for listing, restoring, and comparing saved snapshots. Records metadata (timestamp, user comment, Unity/package version, schema version) and validates JSON on load (unsupported schema, missing/duplicate GUIDs, etc.). Replaces the previous individual menu items (`Restore Snapshot (Additive)`, `Compare with Current State`, etc.), which are removed from the menu.
- Rules injection overload for `AddressTellerService.ApplyAll` / `ValidateAll`: accepts `IReadOnlyList<AddressRuleBase>` directly without going through reflection-based collection. The existing reflection overload delegates internally to this new one, remaining backward compatible.
- `ToJson()` for Explain results (`AddressTellerExplainReport` / `AddressTellerExplainAsset` / `AddressTellerExplainRule`). Results can be consumed by external tools the same way as `AddressTellerReport` / `AddressTellerSnapshot`.

### Changed

- Moved settings storage from `EditorPrefs` to `ProjectSettings/AddressTellerSettings.asset` for team sharing. Existing settings are not migrated and must be reconfigured.
- Changed import-time auto-apply to differential mode — only changed or moved assets are processed. Full-project consistency checks remain the responsibility of `Apply All` / `Validate` / CLI.
- Changed the diff display for `Tools/AddressTeller/Snapshot/Compare with Current State...` and `Compare Two Snapshots...` from `Debug.Log` output to a Diff tab in the result window.
- Internalized many types without affecting the public DSL surface (`AddressRuleBase` / `IAddressRuleBuilder` / `Match` / `Naming` / `AssetContext`). Internalized types include: `AddressRuleBuilderImpl` / `RuleEvaluator` / `RuleExplanation` / `RuleMatchOutcome` / `RuleEvaluationDetail` (DSL internals); `AddressTellerApplier` / `RuleCollector` / `AssetFilter` / `RuleExplainService` / `SnapshotFileCatalog` / `AddressTellerAutoSnapshotService` / `AddressTellerReportBuilder` / `AddressTellerExplainReportBuilder` (Addressables integration internals); `AddressResolution` / `RuleEvaluationError` (rule evaluation internal result types). `AddressCandidate`, exposed via the public API `ValidationResult.ConflictingCandidates`, remains public.
- Reorganized documentation: README now serves as an overview with links (requirements, installation, quick start, docs). DSL reference, operations guide, design decisions, testing guidelines, and architecture are moved to `Documentation~/` and `CONTRIBUTING.md`.

## [0.1.0] - 2026-06-08

### Added

- Rule definition DSL: classes inheriting `AddressRuleBase` are collected automatically from assemblies; rules are defined by chaining `Group().Where().Address().Label()` inside `Configure(IAddressRuleBuilder)`.
- Rule evaluation engine: evaluates all rules in ascending `Order`; two or more address candidates result in a conflict error; labels accumulate from all matching rules.
- `Tools/AddressTeller/Apply All` / `Validate` / `Apply with Validate` menus and CI-oriented `ApplyAllCLI` / `ApplyWithValidateCLI`.
- `AssetPostprocessor`-based auto-apply on import, move, and delete (can be toggled in Project Settings).
- Duplicate `Order` value warning when multiple rule classes share the same order.
- `CleanupStaleEntries`: automatically removes entries for assets that no longer match any rule from AddressTeller-managed groups.
- Snapshot feature: save, restore (Additive / Exact), and compare the current Addressables state (groups, addresses, labels) as JSON.
- Project Settings UI: auto-apply toggle, `CleanupStaleEntries` toggle, snapshot folder setting, and registered rule list.
